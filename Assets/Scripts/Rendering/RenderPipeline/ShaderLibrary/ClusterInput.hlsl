#ifndef CUSTOMRP_CLUSTER_INPUT_INCLUDED
#define CUSTOMRP_CLUSTER_INPUT_INCLUDED

#define CLUSTER_LIGHTSCOUNT_BUFFER_SIZE     4096
#define CLUSTER_LIGHTINDICES_BUFFER_SIZE    4096

CBUFFER_START(_ClusterLightsCountBuffer)
    float4 _ClusterLightsCount[CLUSTER_LIGHTSCOUNT_BUFFER_SIZE];
CBUFFER_END
CBUFFER_START(_ClusterLightIndicesBuffer)
    float4 _ClusterLightIndices[CLUSTER_LIGHTINDICES_BUFFER_SIZE];
CBUFFER_END

float4 _ClusterParams;      // x is 1/zLogFactor, y is 1/clusterZFar, z > 0 means cluster in z-prior, z < 0 means cluster in normal sequence.
int4 _ClusterCountParams;    // x is clusters count in horizental, y is clusters count in vertical, z is clusters count in Axis-Z, w is total clusters count.
float4 _ClusterSizeParams;  // x is cluster width, y is cluster height, z is 1/x, w is 1/y

#endif
