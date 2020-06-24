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
        public int maxLightsCountPerCluster;
        
        [ReadOnly] public NativeArray<int> clusterLightsCount;
        [ReadOnly] public NativeArray<int> clusterLightsIndices;
        
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
            for (int clusterIndex1D = 0; clusterIndex1D < clustersCount && totalIndicesCount < lightIndicesBuffer.Length * 16; ++clusterIndex1D)
            {
                int lightsCount = clusterLightsCount[clusterIndex1D];
                if (lightsCount <= 0) continue;

                for(int i = 0; i < lightsCount; ++i)
                {
                    int index = totalIndicesCount / 16;    // 算出float4在Constant Buffer中的索引
                    var compIndex = (totalIndicesCount % 16) / 4;    // 算出使用float4中的第几个分量
                    int maskIndex = totalIndicesCount % 4;    // 掩码的索引位置
                    int mask = ~(0xff << (maskIndex * 8));    // 掩码
                    
                    var lightIndex = clusterLightsIndices[clusterIndex1D * maxLightsCountPerCluster + i];
                    int4 entry = lightIndicesBuffer[index];
                    entry[compIndex] = (entry[compIndex] & mask) | (0xff & lightIndex) << (maskIndex * 8);
                    lightIndicesBuffer[index] = entry;

                    ++totalIndicesCount;

                    if (totalIndicesCount >= lightIndicesBuffer.Length * 16)
                        break;
                }
            }
            
        }
    }
}