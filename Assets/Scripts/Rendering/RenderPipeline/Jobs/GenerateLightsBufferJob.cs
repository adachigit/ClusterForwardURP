using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Rendering.RenderPipeline.Jobs
{
    [BurstCompile(CompileSynchronously = true)]
    public struct GenerateLightsBufferJob : IJob
    {
        public NativeArray<float4> lightsCountBuffer;
        public NativeArray<float4> lightIndicesBuffer;

        public int clustersCount;
        
        [ReadOnly] public NativeArray<int> clusterLightsCount;
        [ReadOnly] public NativeMultiHashMap<int, int> clusterLightsIndices;
        
        public void Execute()
        {
            half indexStartOffset = (half)0;
            for (int i = 0; i < clusterLightsCount.Length && i < clustersCount; ++i)
            {
                int index = i / 2;    // index = i / 2
                float4 entry = lightsCountBuffer[index];
                entry[i % 2 * 2] = indexStartOffset;    // index % 2 * 2
                entry[i % 2 * 2 + 1] = (half)clusterLightsCount[i];    // index % 2 * 2 + 1
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