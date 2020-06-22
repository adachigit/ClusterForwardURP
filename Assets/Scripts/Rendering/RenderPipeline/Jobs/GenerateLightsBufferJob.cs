using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Rendering.RenderPipeline.Jobs
{
    [BurstCompile(CompileSynchronously = true)]
    public struct GenerateLightsBufferJob : IJob
    {
        public NativeArray<int4> lightsCountBuffer;
        public NativeArray<int4> lightIndicesBuffer;

        public int clustersCount;
        
        [ReadOnly] public NativeArray<int> clusterLightsCount;
        [ReadOnly] public NativeMultiHashMap<int, int> clusterLightsIndices;
        
        public void Execute()
        {
            int indexStartOffset = 0;
            for (int i = 0; i < clusterLightsCount.Length && i < clustersCount; ++i)
            {
                int index = i / 2;
                int4 entry = lightsCountBuffer[index];
                entry[i % 2 * 2] = indexStartOffset;
                entry[i % 2 * 2 + 1] = clusterLightsCount[i];
                lightsCountBuffer[index] = entry;

                indexStartOffset += clusterLightsCount[i];
            }

            int totalIndicesCount = 0;
            for (int i = 0; i < clustersCount && totalIndicesCount < lightIndicesBuffer.Length * 16; ++i)
            {
                int lightsCount = clusterLightsCount[i];
                if (lightsCount <= 0) continue;

                var lCounter = 0;
                var ite = clusterLightsIndices.GetValuesForKey(i);
                while (ite.MoveNext() && lCounter < lightsCount)
                {
                    int index = totalIndicesCount / 16;    // 算出float4在Constant Buffer中的索引
                    var compIndex = (totalIndicesCount % 16) / 4;    // 算出使用float4中的第几个分量
                    int maskIndex = totalIndicesCount % 4;    // 掩码的索引位置
                    int mask = ~(0xff << (maskIndex * 8));    // 掩码
                    
                    var lightIndex = ite.Current;
                    int4 entry = lightIndicesBuffer[index];
                    entry[compIndex] = (entry[compIndex] & mask) | (0xff & lightIndex) << (maskIndex * 8);
                    lightIndicesBuffer[index] = entry;

                    ++totalIndicesCount;
                    ++lCounter;

                    if (totalIndicesCount >= lightIndicesBuffer.Length * 16)
                        break;
                }
            }
            
        }
    }
}