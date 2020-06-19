#ifndef CUSTOMRP_CLUSTER_INCLUDED
#define CUSTOMRP_CLUSTER_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInput.hlsl"
#include "ClusterInput.hlsl"

#define REQUIRES_WORLD_SPACE_POS_INTERPOLATOR 1

int2 GetClusterIndexXY(float2 screenPos)
{
    float screenX = screenPos.x;
    float screenY = screenPos.y;

    if(_ProjectionParams.x > 0)
    {
        screenY = _ScreenParams.y - screenY;
    }

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

void GetClusterLightStartIndexAndCount(int index1D, out float startIndex, out float count)
{
    int entryIndex = index1D >> 1;
    float4 entry = _ClusterLightsCount[entryIndex];
    
    startIndex = entry[(index1D & 0x1) << 1];
    count = entry[((index1D & 0x1) << 1) + 1];
}

#endif