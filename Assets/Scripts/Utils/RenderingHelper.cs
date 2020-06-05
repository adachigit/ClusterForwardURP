using Unity.Mathematics;
using static Unity.Mathematics.math;
using float4 = Unity.Mathematics.float4;

namespace Utils
{
    public class RenderingHelper
    {
        /// <summary>
        /// 将屏幕坐标转换到剪切空间坐标。Unity中使用opengl屏幕坐标系，即左下角为屏幕空间(0, 0)，x和y坐标分别向右和向上递增
        /// </summary>
        /// <param name="screenPos">屏幕空间坐标</param>
        /// <param name="screenDimension">屏幕分辨率</param>
        /// <returns>剪切空间坐标</returns>
        public static float4 ScreenToClip(float4 screenPos, float2 screenDimension)
        {
            var texCoord = screenPos.xy / screenDimension;

            return float4(texCoord * 2.0f - 1.0f, screenPos.z, screenPos.w);
        }

        /// <summary>
        /// 将剪切空间坐标转换到屏幕空间坐标。Unity中使用opengl屏幕坐标系，即左下角为屏幕空间(0, 0)，x和y坐标分别向右和向上递增
        /// </summary>
        /// <param name="clipPos">剪切空间坐标</param>
        /// <param name="screenDimension">屏幕分辨率</param>
        /// <returns></returns>
        public static float2 ClipToScreen(float4 clipPos, float2 screenDimension)
        {
            var scaledClip = clipPos.xy * 0.5f + 0.5f;

            return float2(scaledClip * screenDimension);
        }

        public static float4 ClipToView(float4 clipPos, ref float4x4 inverseProjMatrix)
        {
            var viewPos = mul(inverseProjMatrix, clipPos);
            viewPos /= viewPos.w;

            return viewPos;
        }

        public static float4 ScreenToView(float4 screenPos, float2 screenDimension, ref float4x4 inverseProjMatrix)
        {
            var clipPos = ScreenToClip(screenPos, screenDimension);

            return ClipToView(clipPos, ref inverseProjMatrix);
        }

        public static float2 ViewToScreen(float4 viewPos, float2 screenDimension, ref float4x4 projectMatrix)
        {
            var clipPos = mul(projectMatrix, viewPos);
            clipPos /= clipPos.w;
            
            return ClipToScreen(clipPos, screenDimension);
        }
    }
}