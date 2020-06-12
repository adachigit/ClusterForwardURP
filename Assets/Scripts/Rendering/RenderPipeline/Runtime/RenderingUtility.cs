using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Rendering.RenderPipeline
{
    public class RenderingUtility
    {
        public static bool CheckUseStructuredBuffer(ref RenderingData renderingData)
        {
            CameraType cameraType = renderingData.cameraData.camera.cameraType;

            return cameraType == CameraType.SceneView;
        }
    }
}