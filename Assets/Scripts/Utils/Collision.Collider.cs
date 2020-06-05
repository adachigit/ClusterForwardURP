using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Utils
{
    public partial class Collision
    {
        public static class Collider
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct Sphere
            {
                public float3 center;
                public float radius;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct Plane
            {
                public float3 normal;
                public float distance;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct AABB
            {
                public float3 min;
                public float3 max;
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
}