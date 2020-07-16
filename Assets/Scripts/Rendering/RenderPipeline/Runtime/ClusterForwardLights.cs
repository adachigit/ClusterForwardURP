using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Utils;
using static Unity.Mathematics.math;

using float4 = Unity.Mathematics.float4;

namespace Rendering.RenderPipeline
{
    public class ClusterForwardLights : IDisposable
    {
        public static class LightConstantBuffer
        {
            //主光源
            public static int _MainLightPosition = Shader.PropertyToID("_MainLightPosition");
            public static int _MainLightColor = Shader.PropertyToID("_MainLightColor");
            //附加光源
            public static int _AdditionalLights = Shader.PropertyToID("_AdditionalLightsBuffer");
            //附加光源Constant Buffer各数组size和offset定义
            public static int _ElemCount_AdditionalLights = k_MaxAdditionalLightsCount * 5;

            public static unsafe int _ElemSize_AdditionalLightsPosition = sizeof(float4);
            public static unsafe int _ElemSize_AdditionalLightsColor = sizeof(float4);
            public static unsafe int _ElemSize_AdditionalLightsAttenuation = sizeof(float4);
            public static unsafe int _ElemSize_AdditionalLightsSpotDir = sizeof(float4);
            public static unsafe int _ElemSize_AdditionalLightsProbeChannel = sizeof(float4);
            
            public static int _ArraySize_AdditionalLightsPosition = _ElemSize_AdditionalLightsPosition * k_MaxAdditionalLightsCount;
            public static int _ArraySize_AdditionalLightsColor = _ElemSize_AdditionalLightsColor * k_MaxAdditionalLightsCount;
            public static int _ArraySize_AdditionalLightsAttenuation = _ElemSize_AdditionalLightsAttenuation * k_MaxAdditionalLightsCount;
            public static int _ArraySize_AdditionalLightsSpotDir = _ElemSize_AdditionalLightsSpotDir * k_MaxAdditionalLightsCount;
            public static int _ArraySize_AdditionalLightsProbeChannel = _ElemSize_AdditionalLightsProbeChannel * k_MaxAdditionalLightsCount;

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

        public const int k_MaxAdditionalLightsCount = 256;
        private const string k_SetupLightConstants = "Setup Light Constants";

        private ClusterForwardRenderer m_Renderer;
        
        public NativeArray<float4> m_AdditionalLightsContainer;
        private ComputeBuffer m_AdditionalLightsBuffer;
        
        // 主光源属性Constant Buffer数据
        private float4 m_MainLightPosition;
        private float4 m_MainLightColor;
        // 有效的附加光源数量
        private int m_AdditionalLightsCount;
        // 附加光源列表，供外部剔除功能使用
        private NativeArray<LightInfo> m_AdditionalLightsInfo;

        public int additionalLightsCount => m_AdditionalLightsCount;
        public NativeArray<LightInfo> additionalLightsInfo => m_AdditionalLightsInfo;

        public struct LightInfo
        {
            public VisibleLight visibleLight;
            public bool isLightNull;
            public float innerSpotAngle;
        }
        
        public ClusterForwardLights(ClusterForwardRenderer renderer)
        {
            m_AdditionalLightsInfo = new NativeArray<LightInfo>(k_MaxAdditionalLightsCount, Allocator.Persistent);

            m_AdditionalLightsContainer = new NativeArray<float4>(LightConstantBuffer._ElemCount_AdditionalLights, Allocator.Persistent);
            m_AdditionalLightsBuffer = new ComputeBuffer(LightConstantBuffer._Buffer_Total_Size, sizeof(byte), ComputeBufferType.Constant);

            m_Renderer = renderer;
        }
        
        public void Setup(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            SetupMainLightConstants(ref renderingData);
            CollectAdditionalLightsInfo(ref renderingData);
        }

        public void SetupMainLightConstants(ref RenderingData renderingData)
        {
            if (renderingData.lightData.mainLightIndex < 0)
            {
                m_MainLightPosition = float4.zero;
                m_MainLightColor = float4.zero;
            }
            else
            {
                VisibleLight visibleLight = renderingData.lightData.visibleLights[renderingData.lightData.mainLightIndex];
                if (visibleLight.lightType == LightType.Directional)
                {
                    m_MainLightPosition = -visibleLight.localToWorldMatrix.GetColumn(2);
                }
                else
                {
                    m_MainLightPosition = visibleLight.localToWorldMatrix.GetColumn(3);
                }

                m_MainLightColor = float4((Vector4)visibleLight.finalColor);
            }
        }

        private void CollectAdditionalLightsInfo(ref RenderingData renderingData)
        {
            m_AdditionalLightsCount = 0;
            
            for (int i = 0; i < renderingData.lightData.visibleLights.Length && i < k_MaxAdditionalLightsCount; ++i)
            {
                if (i != renderingData.lightData.mainLightIndex)
                {
                    m_AdditionalLightsInfo[m_AdditionalLightsCount++] = new LightInfo
                    {
                        visibleLight = renderingData.lightData.visibleLights[i],
                        isLightNull = renderingData.lightData.visibleLights[i].light == null,
                        innerSpotAngle = (renderingData.lightData.visibleLights[i].light == null) ? 0.0f : renderingData.lightData.visibleLights[i].light.innerSpotAngle,
                    };
                }
            }
        }
        
        public void ApplyConstantBuffer(ScriptableRenderContext context)
        {
            CommandBuffer cmd = CommandBufferPool.Get(k_SetupLightConstants);

            cmd.SetGlobalVector(LightConstantBuffer._MainLightPosition, m_MainLightPosition);
            cmd.SetGlobalVector(LightConstantBuffer._MainLightColor, float4(m_MainLightColor));

            m_AdditionalLightsBuffer.SetData(m_AdditionalLightsContainer, 0, 0, LightConstantBuffer._ElemCount_AdditionalLights);
            cmd.SetGlobalConstantBuffer(m_AdditionalLightsBuffer, LightConstantBuffer._AdditionalLights, 0, LightConstantBuffer._Buffer_Total_Size);
            
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        
        public void Dispose()
        {
            if (m_AdditionalLightsInfo.IsCreated) m_AdditionalLightsInfo.Dispose();

            if (m_AdditionalLightsContainer.IsCreated) m_AdditionalLightsContainer.Dispose();
            if(m_AdditionalLightsBuffer != null && m_AdditionalLightsBuffer.IsValid())
                m_AdditionalLightsBuffer.Dispose();
        }
    }
}
