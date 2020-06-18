using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Rendering.RenderPipeline.Jobs
{
    [BurstCompile(CompileSynchronously = true)]
    public struct GenerateLightsBufferJob : IJob
    {
        public NativeArray<half4> lightsCountBuffer;
        public NativeArray<float4> lightIndicesBuffer;

        public int clustersCount;
        
        [ReadOnly] public NativeArray<int> clusterLightsCount;
        [ReadOnly] public NativeMultiHashMap<int, int> clusterLightsIndices;
        
        public void Execute()
        {
            half indexStartOffset = (half)0;
            for (int i = 0; i < clusterLightsCount.Length && i < clustersCount; ++i)
            {
                int index = i >> 1;    // index = i / 2
                half4 entry = lightsCountBuffer[index];
                entry[(index & 0x1) << 1] = indexStartOffset;    // index % 2 * 2
                entry[((index & 0x1) << 1) + 1] = (half)clusterLightsCount[i];    // index % 2 * 2 + 1
                lightsCountBuffer[index] = entry;

                indexStartOffset += (half)clusterLightsCount[i];
            }

            int totalIndicesCount = 0;
            for (int i = 0; i < clustersCount && totalIndicesCount < lightIndicesBuffer.Length * 16; ++i)
            {
                var ite = clusterLightsIndices.GetValuesForKey(i);
                while (ite.MoveNext())
                {
                    var lightIndex = ite.Current;
                    int index = totalIndicesCount / 16;
                    float4 entry = lightIndicesBuffer[index];
                    var compIndex = (totalIndicesCount % 16) / 4;
                    entry[compIndex] = (int) entry[compIndex] | (0xff & lightIndex) << (index % 4);
                    lightIndicesBuffer[index] = entry;

                    ++totalIndicesCount;

                    if (totalIndicesCount >= lightIndicesBuffer.Length * 16)
                        break;
                }
            }
            
        }
    }
}