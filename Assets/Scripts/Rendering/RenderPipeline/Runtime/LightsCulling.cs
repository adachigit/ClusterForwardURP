using System.Collections.Generic;
using Rendering.RenderPipeline.Jobs;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Rendering.RenderPipeline
{
    public class LightsCulling
    {
        static class LightsCullingConstantBuffer
        {
            public static int _ClusterLightsCountBuffer = Shader.PropertyToID("_ClusterLightsCountBuffer");
            public static int _ClusterLightIndicesBuffer = Shader.PropertyToID("_ClusterLightIndicesBuffer");
        }
        
        private const string k_SetupClusterCullingConstants = "Setup Cluster Culling Constants";
        
        private static Dictionary<Camera, JobHandle> m_CameraToJobHandle = new Dictionary<Camera, JobHandle>();
        private static Dictionary<Camera, NativeMultiHashMap<int, int>> m_CameraToLightIndicesContainer = new Dictionary<Camera, NativeMultiHashMap<int, int>>();
        private static Dictionary<Camera, NativeArray<int>> m_CameraToLightsCountContainer = new Dictionary<Camera, NativeArray<int>>();

        private static Dictionary<Camera, NativeArray<half4>> m_CameraToLightsCountCBContainer = new Dictionary<Camera, NativeArray<half4>>();
        private static Dictionary<Camera, NativeArray<float4>> m_CameraToLightIndicesCBContainer = new Dictionary<Camera, NativeArray<float4>>();
        
        private static Dictionary<Camera, ComputeBuffer> m_CameraToLightsCountCBuffer = new Dictionary<Camera, ComputeBuffer>();
        private static Dictionary<Camera, ComputeBuffer> m_CameraToLightIndicesCBuffer = new Dictionary<Camera, ComputeBuffer>();
        
        public static void Start(ScriptableRenderContext context, ref RenderingData renderingData, ClusterForwardLights lights, Cluster cluster)
        {
            Camera camera = renderingData.cameraData.camera;

            JobHandle jobHandle = GetJobHandle(camera);
            jobHandle.Complete();
            
            lights.ApplyConstantBuffer(context);
            ApplyConstantBuffer(context, ref renderingData);
        }

        public static void Finish(ScriptableRenderContext context, ref RenderingData renderingData, ClusterForwardLights lights, Cluster cluster)
        {
            Camera camera = renderingData.cameraData.camera;
            
            var cullingJob = new LightsCullingJob
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
                
                clusterLightIndices = GetLightIndicesContainer(camera, cluster),
                clusterLightsCount = GetLightsCountContainer(camera, cluster),
                worldToViewMat = camera.worldToCameraMatrix,
                projectionMat = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false),
            };
            var cullingJobHandle = cullingJob.Schedule();

            var generateJob = new GenerateLightsBufferJob
            {
                clusterLightsCount = GetLightsCountContainer(camera, cluster),
                clusterLightsIndices = GetLightIndicesContainer(camera, cluster),
                clustersCount = cluster.clusterCount.x * cluster.clusterCount.y * cluster.clusterCount.z,
                
                lightsCountBuffer = GetLightsCountCBContainer(camera),
                lightIndicesBuffer = GetLightIndicesCBContainer(camera),
            };
            var generateJobHandle = generateJob.Schedule(cullingJobHandle);
            JobHandle.ScheduleBatchedJobs();
            
            SetJobHandle(camera, generateJobHandle);
        }

        public static void ApplyConstantBuffer(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(k_SetupClusterCullingConstants);

            ComputeBuffer cbuffer = GetLightsCountCBuffer(renderingData.cameraData.camera);
            cbuffer.SetData(GetLightsCountCBContainer(renderingData.cameraData.camera));
            cmd.SetGlobalConstantBuffer(cbuffer, LightsCullingConstantBuffer._ClusterLightsCountBuffer, 0, Constant.ConstantBuffer_LightsCount_Size);

            cbuffer = GetLightIndicesCBuffer(renderingData.cameraData.camera);
            cbuffer.SetData(GetLightIndicesCBContainer(renderingData.cameraData.camera));
            cmd.SetGlobalConstantBuffer(cbuffer, LightsCullingConstantBuffer._ClusterLightIndicesBuffer, 0, Constant.ConstantBuffer_LightIndices_Size);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
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

        private static NativeArray<half4> GetLightsCountCBContainer(Camera camera)
        {
            NativeArray<half4> container;

            if (m_CameraToLightsCountCBContainer.TryGetValue(camera, out container))
            {
                return container;
            }

            container = new NativeArray<half4>(Constant.ConstantBuffer_Max_EntryCount, Allocator.Persistent);
            m_CameraToLightsCountCBContainer.Add(camera, container);

            return container;
        }

        private static NativeArray<float4> GetLightIndicesCBContainer(Camera camera)
        {
            NativeArray<float4> container;

            if (m_CameraToLightIndicesCBContainer.TryGetValue(camera, out container))
            {
                return container;
            }

            container = new NativeArray<float4>(Constant.ConstantBuffer_Max_EntryCount, Allocator.Persistent);
            m_CameraToLightIndicesCBContainer.Add(camera, container);

            return container;
        }

        private static unsafe ComputeBuffer GetLightsCountCBuffer(Camera camera)
        {
            ComputeBuffer cbuffer;

            if (m_CameraToLightsCountCBuffer.TryGetValue(camera, out cbuffer))
            {
                return cbuffer;
            }

            cbuffer = new ComputeBuffer(Constant.ConstantBuffer_Max_EntryCount, sizeof(half4), ComputeBufferType.Constant);
            m_CameraToLightsCountCBuffer.Add(camera, cbuffer);

            return cbuffer;
        }

        private static unsafe ComputeBuffer GetLightIndicesCBuffer(Camera camera)
        {
            ComputeBuffer cbuffer;

            if (m_CameraToLightIndicesCBuffer.TryGetValue(camera, out cbuffer))
            {
                return cbuffer;
            }

            cbuffer = new ComputeBuffer(Constant.ConstantBuffer_Max_EntryCount, sizeof(float4), ComputeBufferType.Constant);
            m_CameraToLightIndicesCBuffer.Add(camera, cbuffer);

            return cbuffer;
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

            foreach (var container in m_CameraToLightsCountCBContainer.Values)
            {
                if (container.IsCreated) container.Dispose();
            }
            m_CameraToLightsCountCBContainer.Clear();

            foreach (var container in m_CameraToLightIndicesCBContainer.Values)
            {
                if (container.IsCreated) container.Dispose();
            }
            m_CameraToLightIndicesCBContainer.Clear();

            foreach (var cbuffer in m_CameraToLightsCountCBuffer.Values)
            {
                cbuffer.Release();
            }
            m_CameraToLightsCountCBuffer.Clear();

            foreach (var cbuffer in m_CameraToLightIndicesCBuffer.Values)
            {
                cbuffer.Release();
            }
            m_CameraToLightIndicesCBuffer.Clear();
        }
    }
}