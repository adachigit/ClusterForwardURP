#ifndef CUSTOMRP_CLUSTER_INCLUDED
#define CUSTOMRP_CLUSTER_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInput.hlsl"
#include "ClusterInput.hlsl"

#define REQUIRES_WORLD_SPACE_POS_INTERPOLATOR 1

int2 GetClusterIndexXY(float2 screenPos)
{
    float screenX = screenPos.x;
    float screenY = screenPos.y;

    // The screen coordinate (0, 0) is at bottom-left in OpenGL and at top-lef in DirectX.
#if UNITY_UV_STARTS_AT_TOP
    if(_ProjectionParams.x > 0)
    {
        screenY = _ScreenParams.y - screenY;
    }
#endif

    return int2(int(screenX * _ClusterSizeParams.z), int(screenY * _ClusterSizeParams.w));
}

int3 GetClusterIndex3D(float3 positionWS, float2 screenPos)
{
    float3 positionVS = mul(UNITY_MATRIX_V, float4(positionWS, 1.0)).xyz;
    int indexZ = int(log2(-positionVS.z * _ClusterParams.y) * _ClusterParams.x + _ClusterCountParams.z);
    
    return int3(GetClusterIndexXY(screenPos), indexZ);
}

int GetClusterIndex1D(int3 index3D)
{
    if (_ClusterParams.z > 0)
    {
        return index3D.y * _ClusterCountParams.x * _ClusterCountParams.z + index3D.x * _ClusterCountParams.z + index3D.z;
    }
    else
    {
        return index3D.z * _ClusterCountParams.x * _ClusterCountParams.y + index3D.y * _ClusterCountParams.x + index3D.x;
    }
}

void GetClusterLightStartIndexAndCount(int clusterIndex1D, out int startIndex, out int count)
{
    int entryIndex = clusterIndex1D >> 1;       // clusterIndex1D / 2
    int4 entry = _ClusterLightsCount[entryIndex];
    
    startIndex = entry[(clusterIndex1D & 0x1) << 1];    // (clusterIndex1D % 2) * 2 
    count = entry[((clusterIndex1D & 0x1) << 1) + 1];   // (clusterIndex1D % 2) * 2 + 1
}

int GetAdditionalLightIndexOfCluster(int lightIndexInIndicesArray)
{
    int entryIndex = lightIndexInIndicesArray >> 4; // lightIndexInIndicesArray / 16
    int compIndex = (lightIndexInIndicesArray & 0xf) >> 2;  // (lightIndexInIndicesArray % 16) / 4
    int maskIndex = lightIndexInIndicesArray & 0x3; // lightIndexInIndicesArray % 4
    int mask = 0xff << (maskIndex << 3);    // 0xff << (maskIndex * 8)
    
    int index32 = (int)_ClusterLightIndices[entryIndex][compIndex];
    
    return (index32 >> (maskIndex << 3)) & 0xff;    // (index32 & mask) >> (maskIndex * 8)
}
 
#endif