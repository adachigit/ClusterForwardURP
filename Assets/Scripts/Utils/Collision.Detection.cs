using Unity.Mathematics;
using UnityEngine;

namespace Utils
{
    public partial class Collision
    {
        public class Detection
        {
            /// <summary>
            /// 点是否在平面负半空间。平面法线指向的半空间为平面正半空间
            /// </summary>
            /// <param name="point">点</param>
            /// <param name="plane">平面</param>
            /// <returns>点在平面负半空间返回true，否则返回false</returns>
            public static bool PointBehindPlane(ref float3 point, ref Collider.Plane plane)
            {
                return math.dot(plane.normal, point) - plane.distance < 0;
            }

            /// <summary>
            /// 球体是否在平面负半空间。平面法线指向的半空间为平面正半空间
            /// </summary>
            /// <param name="sphere">球体</param>
            /// <param name="plane">平面</param>
            /// <returns>球体在平面负半空间返回true，否则返回false</returns>
            public static bool SphereBehindPlane(ref Collider.Sphere sphere, ref Collider.Plane plane)
            {
                return math.dot(plane.normal, sphere.center) - plane.distance < -sphere.radius;
            }

            /// <summary>
            /// 球体是否与平面相交
            /// </summary>
            /// <param name="sphere">球体</param>
            /// <param name="plane">平面</param>
            /// <returns>球体与平面相交返回true，否则返回false</returns>
            public static bool SphereIntersectPlane(ref Collider.Sphere sphere, ref Collider.Plane plane)
            {
                return math.abs(math.dot(plane.normal, sphere.center) - plane.distance) < sphere.radius;
            }

            /// <summary>
            /// 球体与AABB是否相交
            /// </summary>
            /// <param name="sphere">球体</param>
            /// <param name="aabb">AABB</param>
            /// <returns>相交返回true，否则返回false</returns>
            public static bool SphereIntersectAABB(ref Collider.Sphere sphere, ref Collider.AABB aabb)
            {
                float sqrDist = Evaluation.SqrDistancePointToAABB(ref sphere.center, ref aabb);

                return sqrDist <= sphere.radius * sphere.radius;
            }
            
            /// <summary>
            /// 锥体是否在平面的负半空间。平面法线指向的空间为平面的正半空间
            /// </summary>
            /// <param name="cone">锥体</param>
            /// <param name="plane">平面</param>
            /// <returns>锥体在平面负半空间返回true，否则返回false</returns>
            public static bool ConeBehindPlane(ref Collider.Cone cone, ref Collider.Plane plane)
            {
                float3 pos = cone.pos;
                float3 m = math.cross(math.cross(plane.normal, cone.direction), cone.direction);
                float3 Q = cone.pos + cone.direction * cone.height + m * cone.radius;

                return PointBehindPlane(ref pos, ref plane) && PointBehindPlane(ref Q, ref plane);
            }

            /// <summary>
            /// 线段是否与平面相交
            /// </summary>
            /// <param name="startPoint">线段起点坐标</param>
            /// <param name="endPoint">线段终点坐标</param>
            /// <param name="plane"></param>
            /// <returns>相交返回true，否则返回false</returns>
            public static bool SegmentIntersectPlane(ref float3 startPoint, ref float3 endPoint, ref Collider.Plane plane)
            {
                return Evaluation.IntersectionOfSegmentWithPlane(ref startPoint, ref endPoint, ref plane, out var intersection);
            }

            public static bool ConeIntersectSphere(ref Collider.Cone cone, ref Collider.Sphere sphere)
            {
                if (Evaluation.SqrDistancePointToPoint(ref cone.pos, ref sphere.center) < math.pow(sphere.radius, 2.0f))
                    return true;

                var vSphere = math.normalize(sphere.center - cone.pos);
//                if (math.dot(vSphere, cone.direction) > math.cos(cone.angle * 0.5f * Mathf.Deg2Rad))
//                    return true;

                float3 v1 = math.cross(cone.direction, vSphere);
                float3 v2 = math.cross(vSphere, v1);

                float3 movedCenter = sphere.center + v2 * sphere.radius;
                float3 v3 = math.normalize(movedCenter - cone.pos);

                return (math.dot(v3, cone.direction) + 0.05f >= math.cos(cone.angle * 0.5f * Mathf.Deg2Rad));
            }
        }
    }
}