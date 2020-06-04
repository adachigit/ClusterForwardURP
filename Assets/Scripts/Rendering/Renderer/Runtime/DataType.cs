using System.Runtime.InteropServices;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace Rendering.RenderPipeline
{
    public class DataType
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct AABB
        {
            public float3 min;
            public float3 max;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Plane
        {
            public float3 normal;
            public float distance;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Sphere
        {
            public float3 center;
            public float radius;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Cone
        {
            public float3 pos;
            public float height;
            public float3 direction;
            public float radius;
        }
    }
}