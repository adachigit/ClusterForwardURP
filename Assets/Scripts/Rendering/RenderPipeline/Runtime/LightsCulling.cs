using System.Collections.Generic;
using Rendering.RenderPipeline.Jobs;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Rendering.RenderPipeline
{
    public class LightsCulling
    {
        private static Dictionary<Camera, JobHandle> m_CameraToJobHandle = new Dictionary<Camera, JobHandle>();
        private static Dictionary<Camera, NativeMultiHashMap<int, int>> m_CameraToLightIndicesContainer = new Dictionary<Camera, NativeMultiHashMap<int, int>>();
        private static Dictionary<Camera, NativeArray<int>> m_CameraToLightsCountContainer = new Dictionary<Camera, NativeArray<int>>();
        
        public static void Start(ScriptableRenderContext context, ref RenderingData renderingData, ClusterForwardLights lights, Cluster cluster)
        {
            Camera camera = renderingData.cameraData.camera;
            
            var job = new LightsCullingJob
            {
                lightDatas = lights.additionalLights,
                clusterAABBs = cluster.clusterAABBs,
                clusterSpheres = cluster.clusterSpheres,
                lightsCount = lights.additionalLightsCount,
                
                screenDimension = cluster.screenDimension,
                clusterSize = cluster.clusterSize,
                clusterCount = cluster.clusterCount,
                clusterZFar = cluster.clusterZFar,
                zLogFactor = cluster.zLogFactor,
                isClusterZPrior = cluster.rendererData.zPriority,
                
                clusterLightIndexes = GetLightIndicesContainer(camera, cluster),
                clusterLightsCount = GetLightsCountContainer(camera, cluster),
                worldToViewMat = camera.worldToCameraMatrix,
                projectionMat = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false),
            };
            
            var jobHandle = job.Schedule();
            JobHandle.ScheduleBatchedJobs();
            SetJobHandle(camera, jobHandle);
        }

        public static void Finish(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;

            JobHandle jobHandle = GetJobHandle(camera);
            jobHandle.Complete();
        }

        private static NativeMultiHashMap<int, int> GetLightIndicesContainer(Camera camera, Cluster cluster)
        {
            NativeMultiHashMap<int, int> container;

            if (m_CameraToLightIndicesContainer.TryGetValue(camera, out container))
            {
                return container;
            }

            container = new NativeMultiHashMap<int, int>(cluster.maxClustersCount * cluster.rendererData.lightsCountPerCluster, Allocator.Persistent);
            m_CameraToLightIndicesContainer.Add(camera, container);

            return container;
        }

        private static NativeArray<int> GetLightsCountContainer(Camera camera, Cluster cluster)
        {
            NativeArray<int> container;

            if (m_CameraToLightsCountContainer.TryGetValue(camera, out container))
            {
                return container;
            }
            
            container = new NativeArray<int>(cluster.maxClustersCount, Allocator.Persistent);
            m_CameraToLightsCountContainer.Add(camera, container);

            return container;
        }

        private static void ReleaseNativeContainers(Camera camera)
        {
            if (m_CameraToLightIndicesContainer.TryGetValue(camera, out var indicesContainer))
            {
                if(indicesContainer.IsCreated) indicesContainer.Dispose();
                m_CameraToLightIndicesContainer.Remove(camera);
            }

            if (m_CameraToLightsCountContainer.TryGetValue(camera, out var countContainer))
            {
                if(countContainer.IsCreated) countContainer.Dispose();
                m_CameraToLightsCountContainer.Remove(camera);
            }
        }
        
        private static JobHandle GetJobHandle(Camera camera)
        {
            JobHandle jobHandle;

            if (m_CameraToJobHandle.TryGetValue(camera, out jobHandle))
            {
                return jobHandle;
            }
            
            jobHandle = new JobHandle();
            m_CameraToJobHandle.Add(camera, jobHandle);

            return jobHandle;
        }

        private static void SetJobHandle(Camera camera, JobHandle jobHandle)
        {
            m_CameraToJobHandle[camera] = jobHandle;
        }

        public static void Dispose()
        {
            foreach (var jobHandle in m_CameraToJobHandle.Values)
            {
                jobHandle.Complete();
            }
            m_CameraToJobHandle.Clear();

            foreach (var container in m_CameraToLightIndicesContainer.Values)
            {
                if (container.IsCreated) container.Dispose();
            }
            m_CameraToLightIndicesContainer.Clear();

            foreach (var container in m_CameraToLightsCountContainer.Values)
            {
                if (container.IsCreated) container.Dispose();
            }
            m_CameraToLightsCountContainer.Clear();
        }
    }
}