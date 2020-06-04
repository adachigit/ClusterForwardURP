using Unity.Collections;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

namespace Rendering.RenderPipeline
{
    public class ClusterForwardRenderer : ScriptableRenderer
    {
        private NativeArray<DataType.AABB> m_ClusterAABBs;
        private NativeArray<DataType.Sphere> m_ClusterSpheres;
        
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

        private ClusterForwardRendererData m_RendererData;

        private Cluster m_Cluster;
        
        public ClusterForwardRenderer(ClusterForwardRendererData data) : base(data)
        {
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
            
            m_Cluster = new Cluster(data);
        }

        public override void Setup(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!m_Cluster.Setup(ref renderingData))
                return;
            
            //画不透明物体
            EnqueuePass(m_RenderOpaqueForwardPass);
            //画天空盒
            EnqueuePass(m_DrawSkyboxPass);
            //画透明物体
            EnqueuePass(m_RenderTransparentForwardPass);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            
            m_Cluster.Dispose();
        }
    }
}