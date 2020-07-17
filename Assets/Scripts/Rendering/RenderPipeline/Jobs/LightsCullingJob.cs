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
using int3 = Unity.Mathematics.int3;

namespace Rendering.RenderPipeline.Jobs
{
    [BurstCompile(CompileSynchronously = true)]
    public struct LightsCullingJob : IJob
    {
        [ReadOnly] public NativeArray<ClusterForwardLights.LightInfo> lightsInfo;
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
        public float cameraZNear;

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
                VisibleLight l = lightsInfo[i].visibleLight;
                
                switch (l.lightType)
                {
                case LightType.Directional:
                    AssignDirectionalLight(i);
                    break;
                case LightType.Point:
                    AssignPointLight(i, ref l);
                    break;
                case LightType.Spot:
                    AssignSpotLight(i, ref l);
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

            GetSphereClusterIndexAABB(ref sphere, out Collision.Collider.AABBi aabb, out int3 centerIndex3D);

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
            
            for (int z = aabb.min.z; z <= aabb.max.z; ++z)
            {
                for (int y = aabb.min.y; y <= aabb.max.y; ++y)
                {
                    for (int x = aabb.min.x; x <= aabb.max.x; ++x)
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
                    }
                }
            }
        }

        private void AssignSpotLight(int lightIndex, ref VisibleLight lightData)
        {
            float4 viewPos = mul(worldToViewMat, lightData.localToWorldMatrix.GetColumn(3));
            float4 dir = lightData.localToWorldMatrix.GetColumn(2);
            float4 viewDir = mul(worldToViewMat, dir);

            Collision.Collider.Cone cone = new Collision.Collider.Cone
            {
                pos = viewPos.xyz,
                direction = viewDir.xyz,
                angle = lightData.spotAngle,
                height = lightData.range,
            };

            Collision.Collider.Sphere spotSphere;
            if (lightData.spotAngle > 90)
            {
                spotSphere.radius = cone.height;
                spotSphere.center = cone.pos;
            }
            else
            {
                // 当聚光灯张开角小于等于90度时，外接圆半径公式为：半径 = 底边长度 / 2 * sin(聚光灯张开角)
                spotSphere.radius = (2.0f * cone.height * math.tan(cone.angle * 0.5f * Mathf.Deg2Rad)) / (2.0f * math.sin(Mathf.Deg2Rad * cone.angle));
                // 聚光灯定点向圆心方向移动半径长度个单位即为圆心位置
                spotSphere.center = cone.pos + spotSphere.radius * cone.direction;
            }
            
            GetSphereClusterIndexAABB(ref spotSphere, out Collision.Collider.AABBi aabb, out int3 centerIndex3D);
            
            // 聚光灯定点所在的cluster肯定被光源覆盖
            int centerIndex1D = -1;
            if (Cluster.IsValidIndex3D(centerIndex3D, clusterCount))
            {
                centerIndex1D = Cluster.GetClusterIndex1D(centerIndex3D, clusterCount, isClusterZPrior);
                if (Cluster.IsValidIndex1D(centerIndex1D, clusterCount, isClusterZPrior))
                {
                    AddLightIndexToCluster(centerIndex1D, lightIndex);
                }
            }
            
            for (int z = aabb.min.z; z <= aabb.max.z; ++z)
            {
                for (int y = aabb.min.y; y <= aabb.max.y; ++y)
                {
                    for (int x = aabb.min.x; x <= aabb.max.x; ++x)
                    {
                        int3 index3D = int3(x, y, z);
                        if (!Cluster.IsValidIndex3D(index3D, clusterCount))
                            continue;
                        int index1D = Cluster.GetClusterIndex1D(index3D, clusterCount, isClusterZPrior);
                        //跳过点光源中心点所在的cluster
                        if (index1D == centerIndex1D) continue;

                        var clusterSphere = clusterSpheres[index1D];
                        if (Collision.Detection.ConeIntersectSphere(ref cone, ref clusterSphere))
                        {
                            AddLightIndexToCluster(index1D, lightIndex);
                        }
                    }
                }
            }
        }

        private void GetSphereClusterIndexAABB(ref Collision.Collider.Sphere sphere, out Collision.Collider.AABBi aabb, out int3 centerIndex3D)
        {
            float4 centerPos = float4(sphere.center, 1.0f);
            
            float2 screenCenter = RenderingHelper.ViewToScreen(centerPos, screenDimension, ref projectionMat);
            centerIndex3D = int3(Cluster.GetClusterXYIndexFromScreenPos(screenCenter, clusterSize),
                Cluster.GetClusterZIndex(centerPos.z, clusterZFar, clusterCount.z, zLogFactor));

            float sphereZNear = math.min(-cameraZNear, sphere.center.z + sphere.radius);
            float sphereZFar = math.max(-clusterZFar, sphere.center.z - sphere.radius);

            float2 xyLeftNear = RenderingHelper.ViewToScreen(float4(centerPos.xy - sphere.radius, sphereZNear, 1.0f), screenDimension, ref projectionMat);
            float2 xyLeftFar = RenderingHelper.ViewToScreen(float4(centerPos.xy - sphere.radius, sphereZFar, 1.0f), screenDimension, ref projectionMat);
            float2 xyRightNear = RenderingHelper.ViewToScreen(float4(centerPos.xy + sphere.radius, sphereZNear, 1.0f), screenDimension, ref projectionMat);
            float2 xyRightFar = RenderingHelper.ViewToScreen(float4(centerPos.xy + sphere.radius, sphereZFar, 1.0f), screenDimension, ref projectionMat);

            int2 minIndexXY = Cluster.GetClusterXYIndexFromScreenPos(math.min(xyLeftNear, xyLeftFar), clusterSize);
            int2 maxIndexXY = Cluster.GetClusterXYIndexFromScreenPos(math.max(xyRightNear, xyRightFar), clusterSize);
            
            minIndexXY.x = max(0, minIndexXY.x);
            minIndexXY.y = max(0, minIndexXY.y);
            maxIndexXY.x = min(clusterCount.x - 1, maxIndexXY.x);
            maxIndexXY.y = min(clusterCount.y - 1, maxIndexXY.y);

            int minIndexZ = max(0, Cluster.GetClusterZIndex(sphereZNear, clusterZFar, clusterCount.z, zLogFactor));
            int maxIndexZ = min(clusterCount.z - 1, Cluster.GetClusterZIndex(sphereZFar, clusterZFar, clusterCount.z, zLogFactor));

            aabb.min = int3(minIndexXY, minIndexZ);
            aabb.max = int3(maxIndexXY, maxIndexZ);
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