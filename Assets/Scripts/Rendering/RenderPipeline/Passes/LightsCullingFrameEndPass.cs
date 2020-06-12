using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Rendering.RenderPipeline.Passes
{
    public class LightsCullingFrameEndPass : ScriptableRenderPass
    {
        private ClusterForwardRenderer m_Renderer;

        public LightsCullingFrameEndPass(ClusterForwardRenderer renderer)
        {
            m_Renderer = renderer;
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            
        }
    }
}