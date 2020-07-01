using Unity.Mathematics;

namespace Utils
{
    public partial class Collision
    {
        public class Evaluation
        {
            /// <summary>
            /// 求AABB包围盒上与指定点最近的点
            /// </summary>
            /// <param name="point">指定点</param>
            /// <param name="aabb">AABB包围盒</param>
            /// <returns>AABB包围盒上与point最近的点</returns>
            public static float3 ClosestPointOnAABBToPoint(ref float3 point, ref Collider.AABB aabb)
            {
                float3 p = point;

                for (int i = 0; i < 3; ++i)
                {
                    if (p[i] < aabb.min[i]) p[i] = aabb.min[i];
                    if (p[i] > aabb.max[i]) p[i] = aabb.max[i];
                }

                return p;
            }

            /// <summary>
            /// 求指定点与AABB包围盒距离的平方
            /// </summary>
            /// <param name="point">指定点</param>
            /// <param name="aabb">AABB包围盒</param>
            /// <returns>距离的平方</returns>
            public static float SqrDistancePointToAABB(ref float3 point, ref Collider.AABB aabb)
            {
                float distance = 0f;

                for (int i = 0; i < 3; ++i)
                {
                    float v = point[i];
                    if (v < aabb.min[i]) distance += (aabb.min[i] - v) * (aabb.min[i] - v);
                    if (v > aabb.max[i]) distance += (v - aabb.max[i]) * (v - aabb.max[i]);
                }

                return distance;
            }

            /// <summary>
            /// 点到点距离的平方
            /// </summary>
            /// <param name="point1"></param>
            /// <param name="point2"></param>
            /// <returns></returns>
            public static float SqrDistancePointToPoint(ref float3 point1, ref float3 point2)
            {
                float3 diff = point2 - point1;
                
                return math.dot(diff, diff);
            }
            
            /// <summary>
            /// 获得线段与平面的交点
            /// </summary>
            /// <param name="startPoint">线段起点坐标</param>
            /// <param name="endPoint">线段终点坐标</param>
            /// <param name="plane">平面</param>
            /// <param name="intersection">交点</param>
            /// <returns>存在交点返回true，否则返回false</returns>
            public static bool IntersectionOfSegmentWithPlane(ref float3 startPoint, ref float3 endPoint, ref Collider.Plane plane, out float3 intersection)
            {
                intersection = float3.zero;
                
                float3 v = endPoint - startPoint;
                float t = (plane.distance - math.dot(plane.normal, startPoint)) / math.dot(plane.normal, v);

                if (t >= 0.0f && t <= 1.0f)
                {
                    intersection = startPoint + t * v;
                    return true;
                }

                return false;
            }
        }
    }
}