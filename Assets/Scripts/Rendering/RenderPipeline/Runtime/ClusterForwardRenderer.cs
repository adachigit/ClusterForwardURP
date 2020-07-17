using System.Collections.Generic;
using Rendering.RenderPipeline.Passes;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

namespace Rendering.RenderPipeline
{
    public class ClusterForwardRenderer : ScriptableRenderer
    {
        DrawObjectsPass m_RenderOpaqueForwardPass;
        DrawSkyboxPass m_DrawSkyboxPass;
        DrawObjectsPass m_RenderTransparentForwardPass;

        RenderTargetHandle m_ActiveCameraColorAttachment;
        RenderTargetHandle m_ActiveCameraDepthAttachment;
        RenderTargetHandle m_CameraColorAttachment;
        RenderTargetHandle m_CameraDepthAttachment;
        RenderTargetHandle m_DepthTexture;
        RenderTargetHandle m_OpaqueColor;

        StencilState m_DefaultStencilState;

        private Material m_BlitMaterial;
        private Material m_CopyDepthMaterial;
        private Material m_SamplingMaterial;
        private Material m_ScreenspaceShadowsMaterial;
        
        private ClusterForwardRendererData m_RendererData;

        private Dictionary<Camera, Cluster> m_CameraToClusterDic;
        private Dictionary<Camera, ClusterForwardLights> m_CameraToLightsDic;

        public ClusterForwardRendererData rendererData => m_RendererData;
        
        public ClusterForwardRenderer(ClusterForwardRendererData data) : base(data)
        {
            m_RendererData = data;
            
            //生成系统材质
            m_BlitMaterial = CoreUtils.CreateEngineMaterial(data.shaders.blitPS);
            m_CopyDepthMaterial = CoreUtils.CreateEngineMaterial(data.shaders.copyDepthPS);
            m_SamplingMaterial = CoreUtils.CreateEngineMaterial(data.shaders.samplingPS);
            m_ScreenspaceShadowsMaterial = CoreUtils.CreateEngineMaterial(data.shaders.screenSpaceShadowPS);
            
            //设置默认的模版操作
            StencilStateData stencilData = data.defaultStencilState;
            m_DefaultStencilState = StencilState.defaultValue;
            m_DefaultStencilState.enabled = stencilData.overrideStencilState;
            m_DefaultStencilState.SetCompareFunction(stencilData.stencilCompareFunction);
            m_DefaultStencilState.SetPassOperation(stencilData.passOperation);
            m_DefaultStencilState.SetFailOperation(stencilData.failOperation);
            m_DefaultStencilState.SetZFailOperation(stencilData.zFailOperation);
            
            //生成各个Pass
            m_RenderOpaqueForwardPass = new DrawObjectsPass("Render Opaques", true, RenderPassEvent.BeforeRenderingOpaques, RenderQueueRange.opaque, data.opaqueLayerMask, m_DefaultStencilState, stencilData.stencilReference);
            m_RenderTransparentForwardPass = new DrawObjectsPass("Render Transparents", false, RenderPassEvent.BeforeRenderingTransparents, RenderQueueRange.transparent, data.transparentLayerMask, m_DefaultStencilState, stencilData.stencilReference);
            m_DrawSkyboxPass = new DrawSkyboxPass(RenderPassEvent.BeforeRenderingSkybox);
            
            //初始化各个RenderTarget的Id
            m_CameraColorAttachment.Init("_CameraColorTexture");
            m_CameraDepthAttachment.Init("_CameraDepthAttachment");
            m_DepthTexture.Init("_CameraDepthTexture");
            m_OpaqueColor.Init("_CameraOpaqueTexture");

            supportedRenderingFeatures = new RenderingFeatures
            {
                cameraStacking = true,
            };

            //生成相机与Cluster和Lights对象的关联Map
            m_CameraToClusterDic = new Dictionary<Camera, Cluster>();
            m_CameraToLightsDic = new Dictionary<Camera, ClusterForwardLights>();

            NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;
        }

        public override void SetupCullingParameters(ref ScriptableCullingParameters cullingParameters, ref CameraData cameraData)
        {
            base.SetupCullingParameters(ref cullingParameters, ref cameraData);

            cullingParameters.cullingOptions |= CullingOptions.DisablePerObjectCulling;
        }

        public override void Setup(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            ref CameraData cameraData = ref renderingData.cameraData;
            RenderTextureDescriptor cameraTargetDescriptor = cameraData.cameraTargetDescriptor;

            // 如果只画离屏深度纹理，那么只会执行不透明、透明和天空盒的pass，不执行光照处理
            bool isOffscreenDepthTexture = camera.targetTexture != null && camera.targetTexture.format == RenderTextureFormat.Depth;
            if (isOffscreenDepthTexture)
            {
                ConfigureCameraTarget(BuiltinRenderTextureType.CameraTarget, BuiltinRenderTextureType.CameraTarget);

                for (int i = 0; i < rendererFeatures.Count; ++i)
                {
                    if (rendererFeatures[i].isActive)
                        rendererFeatures[i].AddRenderPasses(this, ref renderingData);
                }

                EnqueuePass(m_RenderOpaqueForwardPass);
                EnqueuePass(m_DrawSkyboxPass);
                EnqueuePass(m_RenderTransparentForwardPass);

                return;
            }
            
            Cluster cluster = GetCluster(renderingData.cameraData.camera);
            ClusterForwardLights lights = GetLights(renderingData.cameraData.camera);

            LightsCulling.Start(context, ref renderingData, lights, cluster);

            // 当前相机是否开启后处理
            bool applyPostProcessing = cameraData.postProcessEnabled;
            // 当前Camera Stack上是否有相机开启后处理
            bool anyPostProcessing = renderingData.postProcessingEnabled;

            var postProcessFeatureSet = UniversalRenderPipeline.asset.postProcessingFeatureSet;
            

            // 只在Base相机上生成Color Grading查找表
            bool generateColorGradingLUT = anyPostProcessing && cameraData.renderType == CameraRenderType.Base;
#if POST_PROCESSING_STACK_2_0_0_OR_NEWER
            // PPSv2 不需要生成Color Grading查找表
            if(postProcessFeatureSet == PostProcessingFeatureSet.PostProcessingV2)
                generateColorGradingLUT = false;
#endif
            
            //画不透明物体
            EnqueuePass(m_RenderOpaqueForwardPass);
            //画天空盒
            EnqueuePass(m_DrawSkyboxPass);
            //画透明物体
            EnqueuePass(m_RenderTransparentForwardPass);

            LightsCulling.Finish(context, ref renderingData, lights, cluster);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            
            LightsCulling.Dispose();
            
            DestroyClusters();
            DestroyLights();

            CoreUtils.Destroy(m_BlitMaterial);
            CoreUtils.Destroy(m_CopyDepthMaterial);
            CoreUtils.Destroy(m_SamplingMaterial);
            CoreUtils.Destroy(m_ScreenspaceShadowsMaterial);
        }

        public Cluster GetCluster(Camera camera)
        {
            Cluster cluster;
            
            if (m_CameraToClusterDic.TryGetValue(camera, out cluster))
            {
                return cluster;
            }
            
            cluster = new Cluster(this);
            m_CameraToClusterDic.Add(camera, cluster);

            return cluster;
        }

        public ClusterForwardLights GetLights(Camera camera)
        {
            ClusterForwardLights lights;

            if (m_CameraToLightsDic.TryGetValue(camera, out lights))
            {
                return lights;
            }
            
            lights = new ClusterForwardLights(this);
            m_CameraToLightsDic.Add(camera, lights);

            return lights;
        }

        private void DestroyClusters()
        {
            foreach (var cluster in m_CameraToClusterDic.Values)
            {
                cluster.Dispose();
            }
            m_CameraToClusterDic.Clear();
        }

        private void DestroyLights()
        {
            foreach (var lights in m_CameraToLightsDic.Values)
            {
                lights.Dispose();
            }
            m_CameraToLightsDic.Clear();
        }
    }
}