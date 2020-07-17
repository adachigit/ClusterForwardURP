using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

using static Unity.Mathematics.math;
using float4 = Unity.Mathematics.float4;

namespace Rendering.RenderPipeline.Jobs
{
    [BurstCompile(CompileSynchronously = true)]
    public struct CollectLightsJob : IJob
    {
        struct LightSortInfo
        {
            public ClusterForwardLights.LightInfo lightInfo;
            public int3 clusterIndex3D;
            public bool isValid;
        }

        struct Comparer : IComparer<LightSortInfo>
        {
            public int Compare(LightSortInfo x, LightSortInfo y)
            {
                if (!x.isValid && !y.isValid)
                    return 0;
                else if (!x.isValid)
                    return 1;
                else if (!y.isValid)
                    return -1;
                
                if (x.lightInfo.visibleLight.lightType == y.lightInfo.visibleLight.lightType && x.lightInfo.visibleLight.lightType == LightType.Directional)
                    return 0;
                if (x.lightInfo.visibleLight.lightType == LightType.Directional)
                    return 1;
                if (y.lightInfo.visibleLight.lightType == LightType.Directional)
                    return -1;
                
                if (x.clusterIndex3D.x != y.clusterIndex3D.x)
                    return x.clusterIndex3D.x - y.clusterIndex3D.x;
                
                if (x.clusterIndex3D.y != y.clusterIndex3D.y)
                    return x.clusterIndex3D.y - y.clusterIndex3D.y;
                
                return x.clusterIndex3D.z - y.clusterIndex3D.z;
            }
        }
        
        public NativeArray<ClusterForwardLights.LightInfo> additionalLightsInfo;

        public int additionalLightsCount;
        public NativeArray<float4> additionalLightsUBO;

        public int lightsPositionOffset;
        public int lightsColorOffset;
        public int lightsAttenuationOffset;
        public int lightsSpotDirOffset;

        public float4x4 viewMat;
        public float4x4 projMat;
        public float2 screenDimension;
        public int2 clusterSize;
        public int3 clusterCount;
        public float clusterZFar;
        public float zLogFactor;
        public bool isLightsSorting;
        public float pointLightAttenRange;
        
        private static readonly float4 k_DefaultLightPosition = float4(0.0f, 0.0f, 1.0f, 0.0f);
        private static readonly float4 k_DefaultLightColor = float4(0.0f, 0.0f, 0.0f, 1.0f);
        private static readonly float4 k_DefaultLightAttenuation = float4(0.0f, 1.0f, 0.0f, 1.0f);
        private static readonly float4 k_DefaultLightSpotDirection = float4(0.0f, 0.0f, 1.0f, 0.0f);
        private static readonly float4 k_DefaultLightsProbeChannel = float4(-1.0f, 1.0f, -1.0f, -1.0f);
        
        public void Execute()
        {
            if(isLightsSorting) SortLightInfoArray();
            
            for(int i = 0; i < additionalLightsCount; ++i)
            {
                SetupLightInfos(additionalLightsInfo, i,
                    out float4 position,
                    out float4 color,
                    out float4 attenuation,
                    out float4 spotDir
                );
                additionalLightsUBO[lightsPositionOffset + i] = position;
                additionalLightsUBO[lightsColorOffset + i] = color;
                additionalLightsUBO[lightsAttenuationOffset + i] = attenuation;
                additionalLightsUBO[lightsSpotDirOffset + i] = spotDir;
            }
        }

        private void SetupLightInfos(NativeArray<ClusterForwardLights.LightInfo> lightsInfo, int lightIndex, out float4 lightPos, out float4 lightColor, out float4 lightAttenuation, out float4 lightSpotDir)
        {
            //No GC
            lightPos = k_DefaultLightPosition;
            lightColor = k_DefaultLightColor;
            lightAttenuation = k_DefaultLightAttenuation;
            lightSpotDir = k_DefaultLightSpotDirection;

            if (lightIndex < 0)
                return;

            ClusterForwardLights.LightInfo lightInfo = lightsInfo[lightIndex];
            VisibleLight visibleLight = lightsInfo[lightIndex].visibleLight;
            if (visibleLight.lightType == LightType.Directional)
            {
                lightPos = -visibleLight.localToWorldMatrix.GetColumn(2);    // 方向光将光源方向取反
            }
            else
            {
                lightPos = visibleLight.localToWorldMatrix.GetColumn(3);
            }

            lightColor = float4((Vector4)visibleLight.finalColor);

            if (visibleLight.lightType != LightType.Directional)
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
                float lightRangeSqr = visibleLight.range * visibleLight.range;
                float fadeStartDistanceSqr = math.pow(1.0f - pointLightAttenRange, 2.0f) * lightRangeSqr;    
                float fadeRangeSqr = (fadeStartDistanceSqr - lightRangeSqr);
                float oneOverFadeRangeSqr = 1.0f / fadeRangeSqr;
                float lightRangeSqrOverFadeRangeSqr = -lightRangeSqr / fadeRangeSqr;
                float oneOverLightRangeSqr = 1.0f / Mathf.Max(0.0001f, visibleLight.range * visibleLight.range);

                // 为保持手机和编辑器效果一致，这里统一采用第一种算法。
                // 由于Unity GI使用第二种算法，因此可能会造成实时效果与Bake效果不一致。
                lightAttenuation.x = oneOverFadeRangeSqr;//Application.isMobilePlatform || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Switch ? oneOverFadeRangeSqr : oneOverLightRangeSqr;
                lightAttenuation.y = lightRangeSqrOverFadeRangeSqr;
            }
            
            if (visibleLight.lightType == LightType.Spot)
            {
                lightSpotDir = -visibleLight.localToWorldMatrix.GetColumn(2);

                // 聚光灯的线性衰减可以被定义为
                // (SdotL - cosOuterAngle) / (cosInnerAngle - cosOuterAngle)
                // 其中SdotL为光源到物体的方向与聚光灯方向的点积值
                // 这个公式可以被改写为:
                // invAngleRange = 1.0 / (cosInnerAngle - cosOuterAngle)
                // SdotL * invAngleRange + (-cosOuterAngle * invAngleRange)
                // 这样我们可以通过预计算来使公式计算被一条MAD乘加指令处理
                float cosOuterAngle = Mathf.Cos(Mathf.Deg2Rad * visibleLight.spotAngle * 0.6f);
                float cosInnerAngle;
                if (additionalLightsInfo[lightIndex].isLightNull)    // 根据Unity当前版本，这里判空是针对粒子特效里面的光源
                    cosInnerAngle = Mathf.Cos((2.0f * Mathf.Atan(Mathf.Tan(visibleLight.spotAngle * 0.7f * Mathf.Deg2Rad) * (64.0f - 18.0f) / 64.0f)) * 0.5f);
                else
                    cosInnerAngle = Mathf.Cos(lightInfo.innerSpotAngle * Mathf.Deg2Rad * 0.5f);
                float smoothAngleRange = Mathf.Max(0.001f, cosInnerAngle - cosOuterAngle);
                float invAngleRange = 1.0f / smoothAngleRange;
                float add = -cosOuterAngle * invAngleRange;
                lightAttenuation.z = invAngleRange;
                lightAttenuation.w = add;
            }
        }

        private void SortLightInfoArray()
        {
            NativeArray<LightSortInfo> sortedInfoArray = new NativeArray<LightSortInfo>(ClusterForwardLights.k_MaxAdditionalLightsCount, Allocator.Temp);

            for (int i = 0; i < additionalLightsCount; ++i)
            {
                sortedInfoArray[i] = new LightSortInfo
                {
                    lightInfo = additionalLightsInfo[i],
                    clusterIndex3D = Cluster.GetClusterIndex3DFromViewPos(math.mul(viewMat, additionalLightsInfo[i].visibleLight.localToWorldMatrix.GetColumn(3)).xyz,
                        ref screenDimension,
                        ref projMat,
                        ref clusterSize,
                        ref clusterCount,
                        clusterZFar,
                        zLogFactor),
                    isValid = true,
                };
            }

            for (int i = additionalLightsCount; i < ClusterForwardLights.k_MaxAdditionalLightsCount; ++i)
            {
                sortedInfoArray[i] = new LightSortInfo
                {
                    isValid = false,
                };
            }
            
            sortedInfoArray.Sort(new Comparer());

            for (int i = 0; i < additionalLightsCount; ++i)
            {
                additionalLightsInfo[i] = sortedInfoArray[i].lightInfo;
            }

            sortedInfoArray.Dispose();
        }
    }
}