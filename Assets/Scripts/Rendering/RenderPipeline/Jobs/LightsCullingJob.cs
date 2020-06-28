using Rendering.RenderPipeline;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Utils;
using Collision = Utils.Collision;
using static Unity.Mathematics.math;

namespace Rendering.RenderPipeline.Jobs
{
    [BurstCompile(CompileSynchronously = true)]
    public struct LightsCullingJob : IJob
    {
        [ReadOnly] public NativeArray<VisibleLight> lightDatas;
        [ReadOnly] public NativeArray<Collision.Collider.AABB> clusterAABBs;
        [ReadOnly] public NativeArray<Collision.Collider.Sphere> clusterSpheres;
        public int lightsCount;

        public float2 screenDimension;
        public int2 clusterSize;
        public int3 clusterCount;
        public float clusterZFar;
        public float zLogFactor;
        public bool isClusterZPrior;
        public int maxLightsCountPerCluster;

        public NativeArray<int> clusterLightIndices;
        public NativeArray<int> clusterLightsCount;

        public float4x4 worldToViewMat;
        public float4x4 projectionMat;

        private int totalClustersCount;
        
        public unsafe void Execute()
        {
            totalClustersCount = clusterCount.x * clusterCount.y * clusterCount.z;

            UnsafeUtility.MemSet(clusterLightsCount.GetUnsafePtr(), 0, clusterLightsCount.Length * sizeof(int));

            for (int i = 0; i < lightsCount; ++i)
            {
                VisibleLight l = lightDatas[i];
                
                switch (l.lightType)
                {
                case LightType.Directional:
                    AssignDirectionalLight(i);
                    break;
                case LightType.Point:
                    AssignPointLight(i, ref l);
                    break;
                case LightType.Spot:
                    break;
                }
            }
        }

        private void AssignDirectionalLight(int lightIndex)
        {
            for (int i = 0; i < totalClustersCount; ++i)
            {
                AddLightIndexToCluster(i, lightIndex);
            }
        }

        private void AssignPointLight(int lightIndex, ref VisibleLight lightData)
        {
            float4 viewPos = mul(worldToViewMat, lightData.localToWorldMatrix.GetColumn(3));
            if (viewPos.z + lightData.range < -clusterZFar)
                return;

            Collision.Collider.Sphere sphere = new Collision.Collider.Sphere
            {
                center = viewPos.xyz,
                radius = lightData.range
            };
            
            float2 screenCenter = RenderingHelper.ViewToScreen(viewPos, screenDimension, ref projectionMat);
            int3 centerIndex3D = int3(Cluster.GetClusterXYIndexFromScreenPos(screenCenter, clusterSize),
                Cluster.GetClusterZIndex(viewPos.z, clusterZFar, clusterCount.z, zLogFactor));

            // 点光源中心所在的cluster肯定被光源覆盖
            int centerIndex1D = -1;
            if (Cluster.IsValidIndex3D(centerIndex3D, clusterCount))
            {
                centerIndex1D = Cluster.GetClusterIndex1D(centerIndex3D, clusterCount, isClusterZPrior);
                if (Cluster.IsValidIndex1D(centerIndex1D, clusterCount, isClusterZPrior))
                {
                    AddLightIndexToCluster(centerIndex1D, lightIndex);
                }
            }
            
            float2 minScreen = RenderingHelper.ViewToScreen(viewPos - float4(sphere.radius, sphere.radius, 0.0f, 0.0f), screenDimension, ref projectionMat);
            float2 maxScreen = RenderingHelper.ViewToScreen(viewPos + float4(sphere.radius, sphere.radius, 0.0f, 0.0f), screenDimension, ref projectionMat);
            int2 minIndexXY = Cluster.GetClusterXYIndexFromScreenPos(minScreen, clusterSize);
            int2 maxIndexXY = Cluster.GetClusterXYIndexFromScreenPos(maxScreen, clusterSize);
            
            minIndexXY.x = max(0, minIndexXY.x);
            minIndexXY.y = max(0, minIndexXY.y);
            maxIndexXY.x = min(clusterCount.x - 1, maxIndexXY.x);
            maxIndexXY.y = min(clusterCount.y - 1, maxIndexXY.y);

            int minIndexZ = max(0, Cluster.GetClusterZIndex(viewPos.z + sphere.radius, clusterZFar, clusterCount.z, zLogFactor));
            int maxIndexZ = min(clusterCount.z - 1, Cluster.GetClusterZIndex(viewPos.z - sphere.radius, clusterZFar, clusterCount.z, zLogFactor));

            for (int z = minIndexZ; z <= maxIndexZ; ++z)
            {
                for (int y = minIndexXY.y; y <= maxIndexXY.y; ++y)
                {
                    for (int x = minIndexXY.x; x <= maxIndexXY.x; ++x)
                    {
                        int3 index3D = int3(x, y, z);
                        if (!Cluster.IsValidIndex3D(index3D, clusterCount))
                            continue;
                        int index1D = Cluster.GetClusterIndex1D(index3D, clusterCount, isClusterZPrior);
                        //跳过点光源中心点所在的cluster
                        if (index1D == centerIndex1D) continue;

                        var clusterAABB = clusterAABBs[index1D];
                        if (Collision.Detection.SphereIntersectAABB(ref sphere, ref clusterAABB))
                        {
                            AddLightIndexToCluster(index1D, lightIndex);
                        }
                        else
                        {
                            ++x;
                            --x;
                        }
                    }
                }
            }
        }

        private void AddLightIndexToCluster(int clusterIndex1D, int lightIndex)
        {
            int lightsCountOfCluster = clusterLightsCount[clusterIndex1D];
            if (lightsCountOfCluster >= maxLightsCountPerCluster)
                return;

            clusterLightIndices[clusterIndex1D * maxLightsCountPerCluster + lightsCountOfCluster] = lightIndex;
            clusterLightsCount[clusterIndex1D] = lightsCountOfCluster + 1;
        }
    }
}