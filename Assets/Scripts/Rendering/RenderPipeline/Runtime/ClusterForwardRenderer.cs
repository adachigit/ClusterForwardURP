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
        const int k_DepthStencilBufferBits = 32;
        const string k_CreateCameraTextures = "Create Camera Texture";
        
        DepthOnlyPass m_DepthPrepass;
        MainLightShadowCasterPass m_MainLightShadowCasterPass;
        AdditionalLightsShadowCasterPass m_AdditionalLightsShadowCasterPass;
        CopyDepthPass m_CopyDepthPass;
        CopyColorPass m_CopyColorPass;
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
            m_MainLightShadowCasterPass = new MainLightShadowCasterPass(RenderPassEvent.BeforeRenderingShadows);
            m_AdditionalLightsShadowCasterPass = new AdditionalLightsShadowCasterPass(RenderPassEvent.BeforeRenderingShadows);
            m_DepthPrepass = new DepthOnlyPass(RenderPassEvent.BeforeRenderingPrepasses, RenderQueueRange.opaque, data.opaqueLayerMask);
            m_CopyDepthPass = new CopyDepthPass(RenderPassEvent.AfterRenderingSkybox, m_CopyDepthMaterial);
            m_CopyColorPass = new CopyColorPass(RenderPassEvent.AfterRenderingTransparents, m_SamplingMaterial);
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

            bool isSceneViewCamera = cameraData.isSceneViewCamera;
            bool requiresDepthTexture = cameraData.requiresDepthTexture;

            bool mainLightShadows = m_MainLightShadowCasterPass.Setup(ref renderingData);
            bool additionalLightShadows = m_AdditionalLightsShadowCasterPass.Setup(ref renderingData);

            // Depth prepass 在以下情况下被生成：
            // - Scene窗口默认需要Depth Texture，因此默认执行Depth prepass
            // - 如果Game窗口或者离屏摄像机需要Depth Texture，那么我们检查设备是否支持深度纹理拷贝，如果支持则用正常渲染中生成的深度纹理替代，否则启用Depth prepass
            bool requiresDepthPrepass = isSceneViewCamera;
            requiresDepthPrepass |= (requiresDepthTexture && !CanCopyDepth(ref cameraData));
            // 如果只是Scene窗口或者后处理需要拷贝深度纹理，那么将拷贝操作放到透明物体渲染之后，否则放在渲染不透明物体之后执行
            m_CopyDepthPass.renderPassEvent = (!requiresDepthTexture && (applyPostProcessing || isSceneViewCamera)) ? RenderPassEvent.AfterRenderingTransparents : RenderPassEvent.AfterRenderingOpaques;

            bool createColorTexture = RequiresIntermediateColorTexture(ref renderingData, cameraTargetDescriptor) || rendererFeatures.Count != 0;
            // 如果摄像机需要深度纹理，而没有Depth prepass的话，这里生成深度纹理
            bool createDepthTexture = cameraData.requiresDepthTexture && !requiresDepthPrepass;
            createDepthTexture |= (renderingData.cameraData.renderType == CameraRenderType.Base && !renderingData.resolveFinalTarget);

            // 如果是Base相机，说明正在开始一个全新的相机渲染流程，那么在这里进行此次渲染流程的配置工作
            if (cameraData.renderType == CameraRenderType.Base)
            {
                m_ActiveCameraColorAttachment = createColorTexture ? m_CameraColorAttachment : RenderTargetHandle.CameraTarget;
                m_ActiveCameraDepthAttachment = createDepthTexture ? m_CameraDepthAttachment : RenderTargetHandle.CameraTarget;

                bool intermediateRenderTexture = createColorTexture || createDepthTexture;
                if (intermediateRenderTexture)
                    CreateCameraRenderTarget(context, ref cameraData);

                // 如果是渲染到中间纹理，不需要进行msaa
                int backbufferMsaaSamples = (intermediateRenderTexture) ? 1 : cameraTargetDescriptor.msaaSamples;

                if (Camera.main == camera && camera.cameraType == CameraType.Game && camera.targetTexture == null)
                    QualitySettings.antiAliasing = backbufferMsaaSamples;
            }
            else
            {
                m_ActiveCameraColorAttachment = m_CameraColorAttachment;
                m_ActiveCameraDepthAttachment = m_CameraDepthAttachment;
            }
            
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
        
        void CreateCameraRenderTarget(ScriptableRenderContext context, ref CameraData cameraData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(k_CreateCameraTextures);
            var descriptor = cameraData.cameraTargetDescriptor;
            int msaaSamples = descriptor.msaaSamples;
            if (m_ActiveCameraColorAttachment != RenderTargetHandle.CameraTarget)
            {
                bool useDepthRenderBuffer = m_ActiveCameraDepthAttachment == RenderTargetHandle.CameraTarget;
                var colorDescriptor = descriptor;
                colorDescriptor.depthBufferBits = (useDepthRenderBuffer) ? k_DepthStencilBufferBits : 0;
                cmd.GetTemporaryRT(m_ActiveCameraColorAttachment.id, colorDescriptor, FilterMode.Bilinear);
            }

            if (m_ActiveCameraDepthAttachment != RenderTargetHandle.CameraTarget)
            {
                var depthDescriptor = descriptor;
                depthDescriptor.colorFormat = RenderTextureFormat.Depth;
                depthDescriptor.depthBufferBits = k_DepthStencilBufferBits;
                depthDescriptor.bindMS = msaaSamples > 1 && !SystemInfo.supportsMultisampleAutoResolve && (SystemInfo.supportsMultisampledTextures != 0);
                cmd.GetTemporaryRT(m_ActiveCameraDepthAttachment.id, depthDescriptor, FilterMode.Point);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        bool RequiresIntermediateColorTexture(ref RenderingData renderingData, RenderTextureDescriptor baseDescriptor)
        {
            // When rendering a camera stack we always create an intermediate render texture to composite camera results.
            // We create it upon rendering the Base camera.
            if (renderingData.cameraData.renderType == CameraRenderType.Base && !renderingData.resolveFinalTarget)
                return true;

            ref CameraData cameraData = ref renderingData.cameraData;
            int msaaSamples = cameraData.cameraTargetDescriptor.msaaSamples;
            bool isStereoEnabled = renderingData.cameraData.isStereoEnabled;
            bool isScaledRender = !Mathf.Approximately(cameraData.renderScale, 1.0f) && !cameraData.isStereoEnabled;
            bool isCompatibleBackbufferTextureDimension = baseDescriptor.dimension == TextureDimension.Tex2D;
            bool requiresExplicitMsaaResolve = msaaSamples > 1 && !SystemInfo.supportsMultisampleAutoResolve;
            bool isOffscreenRender = cameraData.targetTexture != null && !cameraData.isSceneViewCamera;
            bool isCapturing = cameraData.captureActions != null;

#if ENABLE_VR && ENABLE_VR_MODULE
            if (isStereoEnabled)
                isCompatibleBackbufferTextureDimension = UnityEngine.XR.XRSettings.deviceEyeTextureDimension == baseDescriptor.dimension;
#endif

            bool requiresBlitForOffscreenCamera = cameraData.postProcessEnabled || cameraData.requiresOpaqueTexture || requiresExplicitMsaaResolve;
            if (isOffscreenRender)
                return requiresBlitForOffscreenCamera;

            return requiresBlitForOffscreenCamera || cameraData.isSceneViewCamera || isScaledRender || cameraData.isHdrEnabled ||
                   !isCompatibleBackbufferTextureDimension || !cameraData.isDefaultViewport || isCapturing ||
                   (Display.main.requiresBlitToBackbuffer && !isStereoEnabled);
        }

        bool CanCopyDepth(ref CameraData cameraData)
        {
            bool msaaEnabledForCamera = cameraData.cameraTargetDescriptor.msaaSamples > 1;
            bool supportsTextureCopy = SystemInfo.copyTextureSupport != CopyTextureSupport.None;
            bool supportsDepthTarget = RenderingUtils.SupportsRenderTextureFormat(RenderTextureFormat.Depth);
            bool supportsDepthCopy = !msaaEnabledForCamera && (supportsDepthTarget || supportsTextureCopy);

            // TODO:  We don't have support to highp Texture2DMS currently and this breaks depth precision.
            // currently disabling it until shader changes kick in.
            //bool msaaDepthResolve = msaaEnabledForCamera && SystemInfo.supportsMultisampledTextures != 0;
            bool msaaDepthResolve = false;
            return supportsDepthCopy || msaaDepthResolve;
        }
    }
}