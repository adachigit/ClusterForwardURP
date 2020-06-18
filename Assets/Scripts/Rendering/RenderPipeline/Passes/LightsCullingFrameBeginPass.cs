using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Rendering.RenderPipeline.Passes
{
    public class LightsCullingFrameBeginPass : ScriptableRenderPass
    {
        private ClusterForwardRenderer m_Renderer;

        public LightsCullingFrameBeginPass(ClusterForwardRenderer renderer, RenderPassEvent evt)
        {
            m_Renderer = renderer;
            renderPassEvent = evt;
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;

//            LightsCulling.Finish(context, ref renderingData);
            
            ClusterForwardLights clusterLights = m_Renderer.GetLights(camera);
            clusterLights.ApplyConstantBuffer(context);
            clusterLights.Setup(context, ref renderingData);

            Cluster cluster = m_Renderer.GetCluster(camera);

            LightsCulling.Start(context, ref renderingData, clusterLights, cluster);
        }
    }
}