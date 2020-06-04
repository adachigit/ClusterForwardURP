
using UnityEngine;

namespace Utils
{
    public class MathUtility
    {
        public static bool NearlyEquals(float a, float b)
        {
            return Mathf.Abs(a - b) <= float.Epsilon;
        }
        
        
    }
}