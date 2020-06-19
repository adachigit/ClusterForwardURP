using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Utils;
using Collision = Utils.Collision;
using static Unity.Mathematics.math;
using float2 = Unity.Mathematics.float2;

namespace Rendering.RenderPipeline
{
    public class Cluster : IDisposable
    {
        private const int k_MaxClustersCountUBO = 8192;
        private const int k_MaxClustersCountSSBO = 32768;

        private const int k_MinClusterGridSize = 16;
        private const int k_MaxClusterZCount = 20;

        private float m_MaxClusterZFar;
        private float m_CameraNearZ;
        private int m_ClusterGridSizeX;
        private int m_ClusterGridSizeY;
        private float m_ZLogFactor;

        private int m_ClusterCountX;
        private int m_ClusterCountY;
        private int m_ClusterCountZ;
        private int m_ClusterTotalCount;

        private bool m_UseStructuredBuffer;

        private int m_ScreenWidth;
        private int m_ScreenHeight;

        private ClusterForwardRenderer m_Renderer;
        private Camera m_Camera;

        private int2 m_GridDimension;    // x是cluster在x方向上的像素数，y是cluster在y方向上的像素数
        
        private NativeArray<Collision.Collider.AABB> m_ClusterAABBs;
        private NativeArray<Collision.Collider.Sphere> m_ClusterSpheres;
        private NativeArray<float> m_ClusterZDistances;

        public ClusterForwardRendererData rendererData => m_Renderer.rendererData;
        public float clusterZFar => m_MaxClusterZFar;
        public int2 clusterSize => int2(m_GridDimension.x, m_GridDimension.y);
        public float zLogFactor => m_ZLogFactor;
        public int3 clusterCount => int3(m_ClusterCountX, m_ClusterCountY, m_ClusterCountZ);
        public float2 screenDimension => float2(m_ScreenWidth, m_ScreenHeight);
        public NativeArray<Collision.Collider.AABB> clusterAABBs => m_ClusterAABBs;
        public NativeArray<Collision.Collider.Sphere> clusterSpheres => m_ClusterSpheres;
        
        public int maxClustersCount
        {
            get 
            {
//                if (m_UseStructuredBuffer)
//                    return k_MaxClustersCountSSBO;
//                else
                    return k_MaxClustersCountUBO;
            }
        }
        
        public Cluster(ClusterForwardRenderer renderer)
        {
            ReleaseClusterNativeArray();
            
            m_Renderer = renderer;
            
            m_MaxClusterZFar = rendererData.maxClusterZFar;
            m_ClusterGridSizeX = rendererData.clusterGridSizeX;
            m_ClusterGridSizeY = rendererData.clusterGridSizeY;
            m_ClusterCountZ = rendererData.clusterZCount;
        }

        public bool Setup(ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
//            m_UseStructuredBuffer = RenderingUtility.CheckUseStructuredBuffer(ref renderingData);

            if (camera != m_Camera || camera.pixelWidth != m_ScreenWidth || camera.pixelHeight != m_ScreenHeight)
            {
                m_Camera = camera;
                
                m_ScreenWidth = camera.pixelWidth;
                m_ScreenHeight = camera.pixelHeight;
                m_CameraNearZ = camera.nearClipPlane;
                m_MaxClusterZFar = rendererData.maxClusterZFar;
                
                if (!BuildClusters())
                    return false;
            }
            
            return true;
        }

        private bool BuildClusters()
        {
            ComputeClusterCount();
            m_ClusterTotalCount = m_ClusterCountX * m_ClusterCountY * m_ClusterCountZ;

            ReleaseClusterNativeArray();
            
            m_ClusterAABBs = new NativeArray<Collision.Collider.AABB>(m_ClusterTotalCount, Allocator.Persistent);
            m_ClusterSpheres = new NativeArray<Collision.Collider.Sphere>(m_ClusterTotalCount, Allocator.Persistent);
            m_ClusterZDistances = new NativeArray<float>(m_ClusterCountZ + 1, Allocator.Persistent);
            
            ComputeZDistances();

            float4x4 inverseProjMat = GL.GetGPUProjectionMatrix(m_Camera.projectionMatrix, false).inverse;
            float2 screenDimension = float2(m_ScreenWidth, m_ScreenHeight);
            int3 clusterDimension = int3(m_ClusterCountX, m_ClusterCountY, m_ClusterCountZ);
            float3 eye = float3(0, 0, 0);
            
            for (int z = 0; z < m_ClusterCountZ; ++z)
            {
                var zClusterNear = m_ClusterZDistances[z];
                var zClusterFar = m_ClusterZDistances[z + 1];
                
                Collision.Collider.Plane nearPlane = new Collision.Collider.Plane
                {
                    normal = float3(0.0f, 0.0f, -1.0f),
                    distance = zClusterNear,
                };
                Collision.Collider.Plane farPlane = new Collision.Collider.Plane
                {
                    normal = float3(0.0f, 0.0f, -1.0f),
                    distance = zClusterFar,
                };
                
                for (int x = 0; x < m_ClusterCountX; ++x)
                {
                    for (int y = 0; y < m_ClusterCountY; ++y)
                    {
                        float3 pMin = float3(x * m_ClusterGridSizeX, y * m_ClusterGridSizeY, 0.0f);
                        float3 pMax = float3((x + 1) * m_ClusterGridSizeX, (y + 1) * m_ClusterGridSizeY, 0.0f);

                        pMin = RenderingHelper.ScreenToView(float4(pMin, 1.0f), screenDimension, ref inverseProjMat).xyz;
                        pMax = RenderingHelper.ScreenToView(float4(pMax, 1.0f), screenDimension, ref inverseProjMat).xyz;
//                        pMin.z *= -1;
//                        pMax.z *= -1;
                        //将两个点放大两倍，以确保可以和cluster的远近平面相交
                        pMin *= 2;
                        pMax *= 2;

                        float3 minNear, maxNear, minFar, maxFar;
                        Collision.Evaluation.IntersectionOfSegmentWithPlane(ref eye, ref pMin, ref nearPlane, out minNear);
                        Collision.Evaluation.IntersectionOfSegmentWithPlane(ref eye, ref pMax, ref nearPlane, out maxNear);
                        Collision.Evaluation.IntersectionOfSegmentWithPlane(ref eye, ref pMin, ref farPlane, out minFar);
                        Collision.Evaluation.IntersectionOfSegmentWithPlane(ref eye, ref pMax, ref farPlane, out maxFar);

                        float3 aabbMin = math.min(minNear, math.min(maxNear, math.min(minFar, maxFar)));
                        float3 aabbMax = math.max(minNear, math.max(maxNear, math.max(minFar, maxFar)));
                        int clusterIndex1D = GetClusterIndex1D(int3(x, y, z), clusterDimension, rendererData.zPriority);
                        
                        m_ClusterAABBs[clusterIndex1D] = new Collision.Collider.AABB { min = aabbMin, max = aabbMax };
                        m_ClusterSpheres[clusterIndex1D] = new Collision.Collider.Sphere { center = (aabbMin + aabbMax) / 2.0f, radius = math.distance(aabbMin, aabbMax) / 2.0f };
                    }
                }
            }
            
            return true;
        }

        private void ComputeClusterCount()
        {
            var screenWidth = math.max(k_MinClusterGridSize, m_ScreenWidth);
            var screenHeight = math.max(k_MinClusterGridSize, m_ScreenHeight);
            
            var gridSizeX = math.max(k_MinClusterGridSize, m_ClusterGridSizeX);
            var gridSizeY = math.max(k_MinClusterGridSize, m_ClusterGridSizeY);

            m_ClusterCountZ = math.max(1, m_ClusterCountZ);
            m_ClusterCountX = (screenWidth + gridSizeX - 1) / gridSizeX;
            m_ClusterCountY = (screenHeight + gridSizeY - 1) / gridSizeY;
            var clusterTotalCount = m_ClusterCountX * m_ClusterCountY * m_ClusterCountZ;

            if (clusterTotalCount > maxClustersCount)
            {
                m_ClusterCountZ = m_ClusterCountZ > k_MaxClusterZCount ? k_MaxClusterZCount : m_ClusterCountZ;
                //根据z轴切分的数量，计算x-y平面上的cluster数量
                var clusterCountXY = maxClustersCount / m_ClusterCountZ;
                // 根据以下公式重新计算x方向和y方向的cluster数量
                // clusterCountX * clusterCountY = clusterCountXY
                // clusterCountX / clusterCountY = screenWidth / screenHeight
                m_ClusterCountX = (int)math.floor(math.sqrt((float)clusterCountXY * screenWidth / screenHeight));
                m_ClusterCountY = (int) math.floor(math.sqrt((float) clusterCountXY * screenHeight / screenWidth));
                // 向上取整x方向和y方向的cluster像素宽度和高度
                gridSizeX = (screenWidth + m_ClusterCountX - 1) / m_ClusterCountX;
                gridSizeY = (screenHeight + m_ClusterCountY - 1) / m_ClusterCountY;
                
                // 根据最终计算的gridSize(xy)，重新计算x方向和y方向的cluster数量
                m_ClusterCountX = (screenWidth + gridSizeX - 1) / gridSizeX;
                m_ClusterCountY = (screenHeight + gridSizeY - 1) / gridSizeY;
                
                Debug.LogWarning($"Clusters count was recomputed because the total clusters count is overload. The new clusters count is ({m_ClusterCountX}, {m_ClusterCountY}, {m_ClusterCountZ}, {m_ClusterCountX * m_ClusterCountY * m_ClusterCountZ}), grid dimension is ({gridSizeX}, {gridSizeY})");
            }
            else
            {
                Debug.Log($"The clusters count is ({m_ClusterCountX}, {m_ClusterCountY}, {m_ClusterCountZ}, {m_ClusterCountX * m_ClusterCountY * m_ClusterCountZ}), grid dimension is ({gridSizeX}, {gridSizeY})");
            }

            m_GridDimension.x = gridSizeX;
            m_GridDimension.y = gridSizeY;
        }

        private void ComputeZDistances()
        {
            m_ClusterZDistances[0] = m_CameraNearZ;

            // 公式如下
            // zLogFactor = log2(zFar / zNear) / countZ;
            // clusterZ = zFar * 2^((zIndex - countZ) * zLogFactor)
            m_ZLogFactor = math.log2(m_MaxClusterZFar / m_CameraNearZ) / math.max(1, m_ClusterCountZ);
            for (int i = 1; i <= m_ClusterCountZ; ++i)
            {
                m_ClusterZDistances[i] = m_MaxClusterZFar * math.exp2((i - m_ClusterCountZ) * m_ZLogFactor);
            }
        }

        private void ReleaseClusterNativeArray()
        {
            if (m_ClusterAABBs.IsCreated)
            {
                m_ClusterAABBs.Dispose();
            }
            if (m_ClusterSpheres.IsCreated)
            {
                m_ClusterSpheres.Dispose();
            }
            if (m_ClusterZDistances.IsCreated)
            {
                m_ClusterZDistances.Dispose();
            }
        }

        public void Dispose()
        {
            ReleaseClusterNativeArray();
        }

        public int3 GetClusterIndex3DFromViewPos(float3 viewPos)
        {
            float4x4 projectionMatrix = m_Camera.projectionMatrix;
            float2 screenPos = RenderingHelper.ViewToScreen(float4(viewPos, 1.0f), screenDimension, ref projectionMatrix);

            return int3(GetClusterXYIndexFromScreenPos(screenPos, clusterSize), GetClusterZIndex(viewPos.z, clusterZFar, clusterCount.z, zLogFactor));
        }
        
        public static int GetClusterIndex1D(int3 index3D, int3 clustersCount, bool zPrior)
        {
            if (zPrior)
            {
                return index3D.y * clustersCount.x * clustersCount.z + index3D.x * clustersCount.z + index3D.z;
            }
            else
            {
                return index3D.z * clustersCount.x * clustersCount.y + index3D.y * clustersCount.x + index3D.x;
            }
        }

        public static int3 GetClusterIndex3D(int index1D, int3 clustersCount, bool zPrior)
        {
            if (zPrior)
            {
                return int3(index1D % (clustersCount.x * clustersCount.z) / clustersCount.z,
                            index1D / (clustersCount.x * clustersCount.z),
                            index1D % clustersCount.z
                            );
            }
            else
            {
                return int3(index1D % clustersCount.x,
                            index1D % (clustersCount.x * clustersCount.y) / clustersCount.x,
                            index1D / (clustersCount.x * clustersCount.y)
                            );
            }
        }

        public static int2 GetClusterXYIndexFromScreenPos(float2 screenCoord, int2 clusterSize)
        {
            return int2((int)screenCoord.x / clusterSize.x, (int)screenCoord.y / clusterSize.y);
        }

        public static int GetClusterZIndex(float viewZ, float clusterZFar, int clusterCountZ, float logFactor)
        {
            return (int)(log2(-viewZ / clusterZFar) / logFactor + clusterCountZ);
        }

        public static bool IsValidIndex3D(int3 index3D, int3 clustersCount)
        {
            return (index3D.x >= 0 && index3D.x < clustersCount.x) &&
                   (index3D.y >= 0 && index3D.y < clustersCount.y) &&
                   (index3D.z >= 0 && index3D.z < clustersCount.z);
        }

        public static bool IsValidIndex1D(int index1D, int3 clustersCount, bool zPrior)
        {
            int3 index3D = GetClusterIndex3D(index1D, clustersCount, zPrior);

            return IsValidIndex3D(index3D, clustersCount);
        }
    }
}