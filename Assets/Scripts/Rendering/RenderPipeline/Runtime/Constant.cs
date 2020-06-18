using Unity.Mathematics;

namespace Rendering.RenderPipeline
{
    public class Constant
    {
        public static readonly int ConstantBuffer_Max_EntryCount = 4096;

        public static readonly unsafe int ConstantBuffer_LightsCount_Size = sizeof(half4) * ConstantBuffer_Max_EntryCount;
        public static readonly unsafe int ConstantBuffer_LightIndices_Size = sizeof(float4) * ConstantBuffer_Max_EntryCount;
    }
}