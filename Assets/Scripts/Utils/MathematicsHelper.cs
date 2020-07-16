using Unity.Mathematics;

namespace Utils
{
    public class MathematicsHelper
    {
        public static void Copy(out float4 a, ref float4 b)
        {
            a.x = b.x;
            a.y = b.y;
            a.z = b.z;
            a.w = b.w;
        }
    }
}