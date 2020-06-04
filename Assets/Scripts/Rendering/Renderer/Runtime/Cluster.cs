using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Utils;

namespace Rendering.RenderPipeline
{
    public class Cluster : IDisposable
    {
        private const int k_MaxClustersCountUBO = 8192;
        private const int k_MaxClustersCountSSBO = 32768;

        private const int k_MinClusterGridSize = 16;
        private const int k_MaxClusterZCount = 16;

        private float m_MaxClusterZFar;
        private float m_CameraNearZ;
        private int m_ClusterGridSizeX;
        private int m_ClusterGridSizeY;

        private int m_ClusterCountX;
        private int m_ClusterCountY;
        private int m_ClusterCountZ;
        private int m_ClusterTotalCount;

        private bool m_UseStructuredBuffer;

        private int m_ScreenWidth;
        private int m_ScreenHeight;

        private ClusterForwardRendererData m_RendererData;

        private float4 m_GridDimension;    // x是cluster在x方向上的像素数，y是cluster在y方向上的像素数
        
        private NativeArray<DataType.AABB> m_ClusterAABBs;
        private NativeArray<DataType.Sphere> m_ClusterSpheres;
        private NativeArray<float> m_ClusterZDistances;

        public NativeArray<DataType.AABB> clusterAABBs => m_ClusterAABBs;
        public NativeArray<DataType.Sphere> clusterSpheres => m_ClusterSpheres;

        public int maxClustersCount
        {
            get 
            {
                if (m_UseStructuredBuffer)
                    return k_MaxClustersCountSSBO;
                else
                    return k_MaxClustersCountUBO;
            }
        }
        
        public Cluster(ClusterForwardRendererData rendererData)
        {
            ReleaseClusterNativeArray();
            
            m_RendererData = rendererData;
            
            m_MaxClusterZFar = rendererData.maxClusterZFar;
            m_ClusterGridSizeX = rendererData.clusterGridSizeX;
            m_ClusterGridSizeY = rendererData.clusterGridSizeY;
            m_ClusterCountZ = rendererData.clusterZCount;
        }

        public bool Setup(ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            m_UseStructuredBuffer = RenderingUtility.CheckUseStructuredBuffer(ref renderingData);

            if (camera.pixelWidth != m_ScreenWidth || camera.pixelHeight != m_ScreenHeight || !MathUtility.NearlyEquals(m_RendererData.maxClusterZFar, m_MaxClusterZFar))
            {
                m_ScreenWidth = camera.pixelWidth;
                m_ScreenHeight = camera.pixelHeight;
                m_CameraNearZ = camera.nearClipPlane;
                m_MaxClusterZFar = m_RendererData.maxClusterZFar;
                
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
            
            Debug.Log("new cluster array");
            m_ClusterAABBs = new NativeArray<DataType.AABB>(m_ClusterTotalCount, Allocator.Persistent);
            m_ClusterSpheres = new NativeArray<DataType.Sphere>(m_ClusterTotalCount, Allocator.Persistent);
            m_ClusterZDistances = new NativeArray<float>(m_ClusterCountZ + 1, Allocator.Persistent);
            
            ComputeZDistances();
            
            return true;
        }

        private void ComputeClusterCount()
        {
            var screenWidth = math.max(k_MinClusterGridSize, m_ScreenWidth);
            var screenHeight = math.max(k_MinClusterGridSize, m_ScreenHeight);
            
            var gridSizeX = math.max(k_MinClusterGridSize, m_ClusterGridSizeX);
            var gridSizeY = math.max(k_MinClusterGridSize, m_ClusterGridSizeY);
            
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
                
                Debug.LogWarning($"Clusters count was recomputed because the total clusters count is overload. The new clusters count is ({m_ClusterCountX}, {m_ClusterCountY}, {m_ClusterCountZ}), grid dimension is ({gridSizeX}, {gridSizeY})");
            }

            m_GridDimension.x = gridSizeX;
            m_GridDimension.y = gridSizeY;
        }

        private void ComputeZDistances()
        {
            m_ClusterZDistances[0] = m_CameraNearZ;

            var zLogFactor = math.log2(m_MaxClusterZFar / m_CameraNearZ) / math.max(1, m_ClusterCountZ);
            for (int i = 1; i <= m_ClusterCountZ; ++i)
            {
                m_ClusterZDistances[i] = m_MaxClusterZFar * math.exp2((i - m_ClusterCountZ) * zLogFactor);
            }
        }
        
        private void ReleaseClusterNativeArray()
        {
            if (m_ClusterAABBs.IsCreated)
            {
                Debug.Log("Release cluster aabb array");
                m_ClusterAABBs.Dispose();
            }
            if (m_ClusterSpheres.IsCreated)
            {
                Debug.Log("Release cluster sphere array");
                m_ClusterSpheres.Dispose();
            }
            if (m_ClusterZDistances.IsCreated)
            {
                Debug.Log("Release cluster zdistance array");
                m_ClusterZDistances.Dispose();
            }
        }

        public void Dispose()
        {
            ReleaseClusterNativeArray();
        }
    }
}