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

        static class ClusterInputUniform
        {
            public static int _ClusterParams = Shader.PropertyToID("_ClusterParams");
            public static int _ClusterCountParams = Shader.PropertyToID("_ClusterCountParams");
            public static int _ClusterSizeParams = Shader.PropertyToID("_ClusterSizeParams");
        }
        
        private const string k_SetupClusterCullingConstants = "Setup Cluster Culling Constants";
        
        private static Dictionary<Camera, JobHandle> m_CameraToJobHandle = new Dictionary<Camera, JobHandle>();
        private static Dictionary<Camera, NativeArray<int>> m_CameraToLightIndicesContainer = new Dictionary<Camera, NativeArray<int>>();
        private static Dictionary<Camera, NativeArray<int>> m_CameraToLightsCountContainer = new Dictionary<Camera, NativeArray<int>>();

        private static Dictionary<Camera, NativeArray<int4>> m_CameraToLightsCountCBContainer = new Dictionary<Camera, NativeArray<int4>>();
        private static Dictionary<Camera, NativeArray<int4>> m_CameraToLightIndicesCBContainer = new Dictionary<Camera, NativeArray<int4>>();
        
        private static Dictionary<Camera, ComputeBuffer> m_CameraToLightsCountCBuffer = new Dictionary<Camera, ComputeBuffer>();
        private static Dictionary<Camera, ComputeBuffer> m_CameraToLightIndicesCBuffer = new Dictionary<Camera, ComputeBuffer>();
        
        public static void Start(ScriptableRenderContext context, ref RenderingData renderingData, ClusterForwardLights lights, Cluster cluster)
        {
            Camera camera = renderingData.cameraData.camera;

            JobHandle jobHandle = GetJobHandle(camera);
            jobHandle.Complete();
            
            lights.ApplyConstantBuffer(context);
            ApplyConstantBuffer(context, ref renderingData, lights, cluster);
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
                maxLightsCountPerCluster = cluster.rendererData.lightsCountPerCluster,
                
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
                maxLightsCountPerCluster = cluster.rendererData.lightsCountPerCluster,
                
                lightsCountBuffer = GetLightsCountCBContainer(camera),
                lightIndicesBuffer = GetLightIndicesCBContainer(camera),
            };
            var generateJobHandle = generateJob.Schedule(cullingJobHandle);
            JobHandle.ScheduleBatchedJobs();
            
            SetJobHandle(camera, generateJobHandle);
        }

        private static void ApplyConstantBuffer(ScriptableRenderContext context, ref RenderingData renderingData, ClusterForwardLights lights, Cluster cluster)
        {
            CommandBuffer cmd = CommandBufferPool.Get(k_SetupClusterCullingConstants);

            ComputeBuffer cbuffer = GetLightsCountCBuffer(renderingData.cameraData.camera);
            cbuffer.SetData(GetLightsCountCBContainer(renderingData.cameraData.camera));
            cmd.SetGlobalConstantBuffer(cbuffer, LightsCullingConstantBuffer._ClusterLightsCountBuffer, 0, Constant.ConstantBuffer_LightsCount_Size);

            cbuffer = GetLightIndicesCBuffer(renderingData.cameraData.camera);
            cbuffer.SetData(GetLightIndicesCBContainer(renderingData.cameraData.camera));
            cmd.SetGlobalConstantBuffer(cbuffer, LightsCullingConstantBuffer._ClusterLightIndicesBuffer, 0, Constant.ConstantBuffer_LightIndices_Size);

            cmd.SetGlobalVector(ClusterInputUniform._ClusterParams, new Vector4(1.0f / cluster.zLogFactor, 1.0f / cluster.clusterZFar, cluster.rendererData.zPriority ? 1.0f : -1.0f, 0.0f));
            cmd.SetGlobalVector(ClusterInputUniform._ClusterCountParams, new Vector4(cluster.clusterCount.x, cluster.clusterCount.y, cluster.clusterCount.z, cluster.clusterCount.x * cluster.clusterCount.y * cluster.clusterCount.z));
            cmd.SetGlobalVector(ClusterInputUniform._ClusterSizeParams, new Vector4(cluster.clusterSize.x, cluster.clusterSize.y, 1.0f / cluster.clusterSize.x, 1.0f / cluster.clusterSize.y));
            
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        
        private static NativeArray<int> GetLightIndicesContainer(Camera camera, Cluster cluster)
        {
            NativeArray<int> container;

            if (m_CameraToLightIndicesContainer.TryGetValue(camera, out container))
            {
                return container;
            }

            container = new NativeArray<int>(cluster.maxClustersCount * cluster.rendererData.lightsCountPerCluster, Allocator.Persistent);
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

        private static NativeArray<int4> GetLightsCountCBContainer(Camera camera)
        {
            NativeArray<int4> container;

            if (m_CameraToLightsCountCBContainer.TryGetValue(camera, out container))
            {
                return container;
            }

            container = new NativeArray<int4>(Constant.ConstantBuffer_Max_EntryCount, Allocator.Persistent);
            m_CameraToLightsCountCBContainer.Add(camera, container);

            return container;
        }

        private static NativeArray<int4> GetLightIndicesCBContainer(Camera camera)
        {
            NativeArray<int4> container;

            if (m_CameraToLightIndicesCBContainer.TryGetValue(camera, out container))
            {
                return container;
            }

            container = new NativeArray<int4>(Constant.ConstantBuffer_Max_EntryCount, Allocator.Persistent);
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

            cbuffer = new ComputeBuffer(Constant.ConstantBuffer_Max_EntryCount, sizeof(float4), ComputeBufferType.Constant);
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