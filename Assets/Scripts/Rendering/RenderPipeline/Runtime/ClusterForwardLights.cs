using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static Unity.Mathematics.math;

using float4 = Unity.Mathematics.float4;

namespace Rendering.RenderPipeline
{
    public class ClusterForwardLights : IDisposable
    {
        static class LightConstantBuffer
        {
            //主光源
            public static int _MainLightPosition = Shader.PropertyToID("_MainLightPosition");
            public static int _MainLightColor = Shader.PropertyToID("_MainLightColor");
            //附加光源
            public static int _AdditionalLights = Shader.PropertyToID("_AdditionalLightsBuffer");
            public static int _AdditionalLightsPosition = Shader.PropertyToID("_AdditionalLightsPosition");
            public static int _AdditionalLightsColor = Shader.PropertyToID("_AdditionalLightsColor");
            public static int _AdditionalLightsAttenuation = Shader.PropertyToID("_AdditionalLightsAttenuation");
            public static int _AdditionalLightsSpotDir = Shader.PropertyToID("_AdditionalLightsSpotDir");
            //附加光源Constant Buffer各数组size和offset定义
            public static int _ElemCount_AdditionalLights = k_MaxAdditionalLightsCount * 5;

            public static unsafe int _ElemSize_AdditionalLightsPosition = sizeof(float4);
            public static unsafe int _ElemSize_AdditionalLightsColor = sizeof(float4);
            public static unsafe int _ElemSize_AdditionalLightsAttenuation = sizeof(float4);
            public static unsafe int _ElemSize_AdditionalLightsSpotDir = sizeof(float4);
            public static unsafe int _ElemSize_AdditionalLightsProbeChannel = sizeof(float4);
            
            public static unsafe int _ArraySize_AdditionalLightsPosition = _ElemSize_AdditionalLightsPosition * k_MaxAdditionalLightsCount;
            public static unsafe int _ArraySize_AdditionalLightsColor = _ElemSize_AdditionalLightsColor * k_MaxAdditionalLightsCount;
            public static unsafe int _ArraySize_AdditionalLightsAttenuation = _ElemSize_AdditionalLightsAttenuation * k_MaxAdditionalLightsCount;
            public static unsafe int _ArraySize_AdditionalLightsSpotDir = _ElemSize_AdditionalLightsSpotDir * k_MaxAdditionalLightsCount;
            public static unsafe int _ArraySize_AdditionalLightsProbeChannel = _ElemSize_AdditionalLightsProbeChannel * k_MaxAdditionalLightsCount;

            public static int _Buffer_Total_Size = _ArraySize_AdditionalLightsPosition +
                                                   _ArraySize_AdditionalLightsColor +
                                                   _ArraySize_AdditionalLightsAttenuation +
                                                   _ArraySize_AdditionalLightsSpotDir +
                                                   _ArraySize_AdditionalLightsProbeChannel;
            
            public static int _Offset_AdditionalLightsPosition = 0;
            public static int _Offset_AdditionalLightsColor = _ArraySize_AdditionalLightsPosition / _ElemSize_AdditionalLightsColor;
            public static int _Offset_AdditionalLightsAttenuation = (_ArraySize_AdditionalLightsPosition + _ArraySize_AdditionalLightsColor) / _ElemSize_AdditionalLightsAttenuation;
            public static int _Offset_AdditionalLightsSpotDir = (_ArraySize_AdditionalLightsPosition + _ArraySize_AdditionalLightsColor + _ArraySize_AdditionalLightsAttenuation) / _ElemSize_AdditionalLightsSpotDir;
            public static int _Offset_AdditionalLightsProbeChannel = (_ArraySize_AdditionalLightsPosition + _ArraySize_AdditionalLightsColor + _ArraySize_AdditionalLightsAttenuation + _ArraySize_AdditionalLightsSpotDir) /
                _ElemSize_AdditionalLightsProbeChannel;
        }

        private const int k_MaxAdditionalLightsCount = 256;
        private const string k_SetupLightConstants = "Setup Light Constants";

        private static readonly float4 k_DefaultLightPosition = float4(0.0f, 0.0f, 1.0f, 0.0f);
        private static readonly half4 k_DefaultLightColor = half4((Vector4)Color.black);
        private static readonly half4 k_DefaultLightAttenuation = half4(half(0.0f), half(1.0f), half(0.0f), half(1.0f));
        private static readonly half4 k_DefaultLightSpotDirection = half4(half(0.0f), half(0.0f), half(1.0f), half(0.0f));
        private static readonly half4 k_DefaultLightsProbeChannel = half4(half(-1.0f), half(1.0f), half(-1.0f), half(-1.0f));

        private ClusterForwardRenderer m_Renderer;
        
        // 附加光源属性Constant Buffer数组
        Vector4[] m_AdditionalLightPositions;    //光源位置，平行光为光源方向
        Vector4[] m_AdditionalLightColors;    // 光源颜色
        Vector4[] m_AdditionalLightAttenuations;    // 光源衰减
        Vector4[] m_AdditionalLightSpotDirections;    // 聚光灯方向
        Vector4[] m_AdditionalLightOcclusionProbeChannels;

        private NativeArray<float4> m_AdditionalLightsContainer;
        private ComputeBuffer m_AdditionalLightsBuffer;
        
        // 主光源属性Constant Buffer数据
        private float4 m_MainLightPosition;
        private half4 m_MainLightColor;
        // 有效的附加光源数量
        private int m_AdditionalLightsCount;
        // 附加光源列表，供外部剔除功能使用
        private NativeArray<VisibleLight> m_AdditionalLights;

        public int additionalLightsCount => m_AdditionalLightsCount;
        public NativeArray<VisibleLight> additionalLights => m_AdditionalLights;

        struct LightClusterInfo : IComparable<LightClusterInfo>
        {
            public int visibleLightIndex;
            public LightType lightType;
            public int3 clusterIndex3D;
            
            public int CompareTo(LightClusterInfo other)
            {
                if (lightType == other.lightType && lightType == LightType.Directional)
                    return visibleLightIndex - other.visibleLightIndex;
                if (lightType == LightType.Directional)
                    return 1;
                if (other.lightType == LightType.Directional)
                    return -1;
                
                if (clusterIndex3D.x != other.clusterIndex3D.x)
                    return clusterIndex3D.x - other.clusterIndex3D.x;
                
                if (clusterIndex3D.y != other.clusterIndex3D.y)
                    return clusterIndex3D.y - other.clusterIndex3D.y;
                
                return clusterIndex3D.z - other.clusterIndex3D.z;
            }
        }
        
        public unsafe ClusterForwardLights(ClusterForwardRenderer renderer)
        {
            m_AdditionalLightPositions = new Vector4[k_MaxAdditionalLightsCount];
            m_AdditionalLightColors = new Vector4[k_MaxAdditionalLightsCount];
            m_AdditionalLightAttenuations = new Vector4[k_MaxAdditionalLightsCount];
            m_AdditionalLightSpotDirections = new Vector4[k_MaxAdditionalLightsCount];
            m_AdditionalLightOcclusionProbeChannels = new Vector4[k_MaxAdditionalLightsCount];

            m_AdditionalLights = new NativeArray<VisibleLight>(k_MaxAdditionalLightsCount, Allocator.Persistent);

            m_AdditionalLightsContainer = new NativeArray<float4>(LightConstantBuffer._ElemCount_AdditionalLights, Allocator.Persistent);
            m_AdditionalLightsBuffer = new ComputeBuffer(LightConstantBuffer._Buffer_Total_Size, sizeof(byte), ComputeBufferType.Constant);

            m_Renderer = renderer;
        }
        
        public void Setup(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            SetupLights(context, ref renderingData);
        }

        private void SetupLights(ScriptableRenderContext content, ref RenderingData renderingData)
        {
            SetupMainLightConstants(ref renderingData);
            SetupAdditionalLightConstants(ref renderingData);
        }
        
        private void SetupMainLightConstants(ref RenderingData renderingData)
        {
            half4 lightAttenuation, lightSpotDir;
            SetupLightInfos(renderingData.lightData.visibleLights, renderingData.lightData.mainLightIndex, out m_MainLightPosition, out m_MainLightColor, out lightAttenuation, out lightSpotDir);
        }

        private void SetupAdditionalLightConstants(ref RenderingData renderingData)
        {
            int[] lightIndexArray = GetAdditionalLightsIndexArray(ref renderingData, m_Renderer.rendererData.lightsSorting);
            
            m_AdditionalLightsCount = 0;
            
            for(int i = 0; i < lightIndexArray.Length; ++i)
            {
                int lightIndex = lightIndexArray[i];
                if (lightIndex != renderingData.lightData.mainLightIndex)
                {
                    SetupLightInfos(renderingData.lightData.visibleLights, lightIndex,
//                        out m_AdditionalLightPositions[m_AdditionalLightsCount],
//                        out m_AdditionalLightColors[m_AdditionalLightsCount],
//                        out m_AdditionalLightAttenuations[m_AdditionalLightsCount],
//                        out m_AdditionalLightSpotDirections[m_AdditionalLightsCount]
                            out float4 position,
                            out half4 color,
                            out half4 attenuation,
                            out half4 spotDir
                        );
                    m_AdditionalLightsContainer[LightConstantBuffer._Offset_AdditionalLightsPosition + m_AdditionalLightsCount] = position;
                    m_AdditionalLightsContainer[LightConstantBuffer._Offset_AdditionalLightsColor + m_AdditionalLightsCount] = color;
                    m_AdditionalLightsContainer[LightConstantBuffer._Offset_AdditionalLightsAttenuation + m_AdditionalLightsCount] = attenuation;
                    m_AdditionalLightsContainer[LightConstantBuffer._Offset_AdditionalLightsSpotDir + m_AdditionalLightsCount] = spotDir;

                    m_AdditionalLights[m_AdditionalLightsCount++] = renderingData.lightData.visibleLights[lightIndex];
                }
            }
        }

        private void SetupLightInfos(NativeArray<VisibleLight> lights, int lightIndex, out Vector4 lightPos, out Vector4 lightColor, out Vector4 lightAttenuation, out Vector4 lightSpotDir)
        {
            lightPos = k_DefaultLightPosition;
            lightColor = new Vector4(k_DefaultLightColor.x, k_DefaultLightColor.y, k_DefaultLightColor.z, k_DefaultLightColor.w);
            lightAttenuation = new Vector4(k_DefaultLightAttenuation.x, k_DefaultLightAttenuation.y, k_DefaultLightAttenuation.z, k_DefaultLightAttenuation.w);
            lightSpotDir = new Vector4(k_DefaultLightSpotDirection.x, k_DefaultLightAttenuation.y, k_DefaultLightAttenuation.z, k_DefaultLightAttenuation.w);

            if (lightIndex < 0)
                return;

            VisibleLight lightData = lights[lightIndex];
            if (lightData.lightType == LightType.Directional)
            {
                lightPos = -lightData.localToWorldMatrix.GetColumn(2);    // 方向光将光源方向取反
            }
            else
            {
                lightPos = lightData.localToWorldMatrix.GetColumn(3);
            }

            lightColor = lightData.finalColor;

            if (lightData.lightType != LightType.Directional)
            {
                // 基本的光照衰减公式是1除以物体到灯光距离的平方，即:
                // attenuation = 1.0 / distanceToLightSqr
                // 这里URP使用另外两种不同的平滑衰减因子，平滑衰减因子可以保证光照强度在光照范围外衰减到0
                //
                // * 第一个平滑衰减因子是从光照范围的80%开始的线性衰减，公式如下
                //   smoothFactor = (lightRangeSqr - distanceToLightSqr) / (lightRangeSqr - fadeStartDistanceSqr)
                //   URP重写了这个公式，以使常量部分可被预计算，同时在shader端可被一条MAD乘加指令处理
                //   smoothFactor =  distanceSqr * (1.0 / (fadeDistanceSqr - lightRangeSqr)) + (-lightRangeSqr / (fadeDistanceSqr - lightRangeSqr)
                //                   distanceSqr *           oneOverFadeRangeSqr             +              lightRangeSqrOverFadeRangeSqr
                //
                // * 另一个平滑衰减因子使用了与Unity lightMapper中相同的公式，但是要比第一个慢，公式如下
                //   smoothFactor = (1.0 - saturate((distanceSqr * 1.0 / lightRangeSqr)^2))^2
                float lightRangeSqr = lightData.range * lightData.range;
                float fadeStartDistanceSqr = math.pow(1.0f - m_Renderer.rendererData.pointLightAttenRange, 2.0f) * lightRangeSqr;    
                float fadeRangeSqr = (fadeStartDistanceSqr - lightRangeSqr);
                float oneOverFadeRangeSqr = 1.0f / fadeRangeSqr;
                float lightRangeSqrOverFadeRangeSqr = -lightRangeSqr / fadeRangeSqr;
                float oneOverLightRangeSqr = 1.0f / Mathf.Max(0.0001f, lightData.range * lightData.range);

                // 为保持手机和编辑器效果一致，这里统一采用第一种算法。
                // 由于Unity GI使用第二种算法，因此可能会造成实时效果与Bake效果不一致。
                lightAttenuation.x = half(oneOverFadeRangeSqr);//Application.isMobilePlatform || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Switch ? oneOverFadeRangeSqr : oneOverLightRangeSqr;
                lightAttenuation.y = half(lightRangeSqrOverFadeRangeSqr);
            }
            
            if (lightData.lightType == LightType.Spot)
            {
                lightSpotDir = -lightData.localToWorldMatrix.GetColumn(2);

                // 聚光灯的线性衰减可以被定义为
                // (SdotL - cosOuterAngle) / (cosInnerAngle - cosOuterAngle)
                // 其中SdotL为光源到物体的方向与聚光灯方向的点积值
                // 这个公式可以被改写为:
                // invAngleRange = 1.0 / (cosInnerAngle - cosOuterAngle)
                // SdotL * invAngleRange + (-cosOuterAngle * invAngleRange)
                // 这样我们可以通过预计算来使公式计算被一条MAD乘加指令处理
                float cosOuterAngle = Mathf.Cos(Mathf.Deg2Rad * lightData.spotAngle * 0.5f);
                float cosInnerAngle;
                if (lightData.light != null)    // 根据Unity当前版本，这里判空是针对粒子特效里面的光源
                    cosInnerAngle = Mathf.Cos(lightData.light.innerSpotAngle * Mathf.Deg2Rad * 0.5f);
                else
                    cosInnerAngle = Mathf.Cos((2.0f * Mathf.Atan(Mathf.Tan(lightData.spotAngle * 0.5f * Mathf.Deg2Rad) * (64.0f - 18.0f) / 64.0f)) * 0.5f);
                float smoothAngleRange = Mathf.Max(0.001f, cosInnerAngle - cosOuterAngle);
                float invAngleRange = 1.0f / smoothAngleRange;
                float add = -cosOuterAngle * invAngleRange;
                lightAttenuation.z = half(invAngleRange);
                lightAttenuation.w = half(add);
            }
        }

        private void SetupLightInfos(NativeArray<VisibleLight> lights, int lightIndex, out float4 lightPos, out half4 lightColor, out half4 lightAttenuation, out half4 lightSpotDir)
        {
            lightPos = k_DefaultLightPosition;
            lightColor = k_DefaultLightColor;
            lightAttenuation = k_DefaultLightAttenuation;
            lightSpotDir = k_DefaultLightSpotDirection;

            if (lightIndex < 0)
                return;

            VisibleLight lightData = lights[lightIndex];
            if (lightData.lightType == LightType.Directional)
            {
                lightPos = -lightData.localToWorldMatrix.GetColumn(2);    // 方向光将光源方向取反
            }
            else
            {
                lightPos = lightData.localToWorldMatrix.GetColumn(3);
            }

            lightColor = half4((Vector4)lightData.finalColor);

            if (lightData.lightType != LightType.Directional)
            {
                // 基本的光照衰减公式是1除以物体到灯光距离的平方，即:
                // attenuation = 1.0 / distanceToLightSqr
                // 这里URP使用另外两种不同的平滑衰减因子，平滑衰减因子可以保证光照强度在光照范围外衰减到0
                //
                // * 第一个平滑衰减因子是从光照范围的80%开始的线性衰减，公式如下
                //   smoothFactor = (lightRangeSqr - distanceToLightSqr) / (lightRangeSqr - fadeStartDistanceSqr)
                //   URP重写了这个公式，以使常量部分可被预计算，同时在shader端可被一条MAD乘加指令处理
                //   smoothFactor =  distanceSqr * (1.0 / (fadeDistanceSqr - lightRangeSqr)) + (-lightRangeSqr / (fadeDistanceSqr - lightRangeSqr)
                //                   distanceSqr *           oneOverFadeRangeSqr             +              lightRangeSqrOverFadeRangeSqr
                //
                // * 另一个平滑衰减因子使用了与Unity lightMapper中相同的公式，但是要比第一个慢，公式如下
                //   smoothFactor = (1.0 - saturate((distanceSqr * 1.0 / lightRangeSqr)^2))^2
                float lightRangeSqr = lightData.range * lightData.range;
                float fadeStartDistanceSqr = math.pow(1.0f - m_Renderer.rendererData.pointLightAttenRange, 2.0f) * lightRangeSqr;    
                float fadeRangeSqr = (fadeStartDistanceSqr - lightRangeSqr);
                float oneOverFadeRangeSqr = 1.0f / fadeRangeSqr;
                float lightRangeSqrOverFadeRangeSqr = -lightRangeSqr / fadeRangeSqr;
                float oneOverLightRangeSqr = 1.0f / Mathf.Max(0.0001f, lightData.range * lightData.range);

                // 为保持手机和编辑器效果一致，这里统一采用第一种算法。
                // 由于Unity GI使用第二种算法，因此可能会造成实时效果与Bake效果不一致。
                lightAttenuation.x = half(oneOverFadeRangeSqr);//Application.isMobilePlatform || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Switch ? oneOverFadeRangeSqr : oneOverLightRangeSqr;
                lightAttenuation.y = half(lightRangeSqrOverFadeRangeSqr);
            }
            
            if (lightData.lightType == LightType.Spot)
            {
                lightSpotDir = half4(-lightData.localToWorldMatrix.GetColumn(2));

                // 聚光灯的线性衰减可以被定义为
                // (SdotL - cosOuterAngle) / (cosInnerAngle - cosOuterAngle)
                // 其中SdotL为光源到物体的方向与聚光灯方向的点积值
                // 这个公式可以被改写为:
                // invAngleRange = 1.0 / (cosInnerAngle - cosOuterAngle)
                // SdotL * invAngleRange + (-cosOuterAngle * invAngleRange)
                // 这样我们可以通过预计算来使公式计算被一条MAD乘加指令处理
                float cosOuterAngle = Mathf.Cos(Mathf.Deg2Rad * lightData.spotAngle * 0.5f);
                float cosInnerAngle;
                if (lightData.light != null)    // 根据Unity当前版本，这里判空是针对粒子特效里面的光源
                    cosInnerAngle = Mathf.Cos(lightData.light.innerSpotAngle * Mathf.Deg2Rad * 0.5f);
                else
                    cosInnerAngle = Mathf.Cos((2.0f * Mathf.Atan(Mathf.Tan(lightData.spotAngle * 0.5f * Mathf.Deg2Rad) * (64.0f - 18.0f) / 64.0f)) * 0.5f);
                float smoothAngleRange = Mathf.Max(0.001f, cosInnerAngle - cosOuterAngle);
                float invAngleRange = 1.0f / smoothAngleRange;
                float add = -cosOuterAngle * invAngleRange;
                lightAttenuation.z = half(invAngleRange);
                lightAttenuation.w = half(add);
            }
        }

        private int[] GetAdditionalLightsIndexArray(ref RenderingData renderingData, bool sorting)
        {
            List<int> indexArray = new List<int>();
            List<LightClusterInfo> infoArray = new List<LightClusterInfo>();
            
            Cluster cluster = m_Renderer.GetCluster(renderingData.cameraData.camera);
            float4x4 worldToViewMat = renderingData.cameraData.camera.worldToCameraMatrix;
            LightData lightData = renderingData.lightData;

            for (int i = 0; i < lightData.visibleLights.Length && i < k_MaxAdditionalLightsCount; ++i)
            {
                if (i != lightData.mainLightIndex)
                {
                    infoArray.Add(new LightClusterInfo
                    {
                        visibleLightIndex = i,
                        lightType = lightData.visibleLights[i].lightType,
                        clusterIndex3D = cluster.GetClusterIndex3DFromViewPos(mul(worldToViewMat, lightData.visibleLights[i].localToWorldMatrix.GetColumn(3)).xyz),
                    });
                }
            }

            if (sorting)
                infoArray.Sort();

            for (int i = 0; i < infoArray.Count; ++i)
            {
                indexArray.Add(infoArray[i].visibleLightIndex);
            }
            
            return indexArray.ToArray();
        }
        
        public void ApplyConstantBuffer(ScriptableRenderContext context)
        {
            CommandBuffer cmd = CommandBufferPool.Get(k_SetupLightConstants);

            cmd.SetGlobalVector(LightConstantBuffer._MainLightPosition, m_MainLightPosition);
            cmd.SetGlobalVector(LightConstantBuffer._MainLightColor, float4(m_MainLightColor));

            //            cmd.SetGlobalVectorArray(LightConstantBuffer._AdditionalLightsPosition, m_AdditionalLightPositions);
            //            cmd.SetGlobalVectorArray(LightConstantBuffer._AdditionalLightsColor, m_AdditionalLightColors);
            //            cmd.SetGlobalVectorArray(LightConstantBuffer._AdditionalLightsAttenuation, m_AdditionalLightAttenuations);
            //            cmd.SetGlobalVectorArray(LightConstantBuffer._AdditionalLightsSpotDir, m_AdditionalLightSpotDirections);
            m_AdditionalLightsBuffer.SetData(m_AdditionalLightsContainer, 0, 0, LightConstantBuffer._ElemCount_AdditionalLights);
            cmd.SetGlobalConstantBuffer(m_AdditionalLightsBuffer, LightConstantBuffer._AdditionalLights, 0, LightConstantBuffer._Buffer_Total_Size);
            
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        
        public void Dispose()
        {
            if (m_AdditionalLights.IsCreated) m_AdditionalLights.Dispose();

            if (m_AdditionalLightsContainer.IsCreated) m_AdditionalLightsContainer.Dispose();
            if(m_AdditionalLightsBuffer != null && m_AdditionalLightsBuffer.IsValid())
                m_AdditionalLightsBuffer.Dispose();
        }
    }
}
