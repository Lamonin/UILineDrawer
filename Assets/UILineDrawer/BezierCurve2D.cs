using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Maro.UILineDrawer
{
    /// <summary>
    /// Represents a cached cubic bezier curve segment (P0, P1, P2, P3).
    /// </summary>
    public readonly struct BezierCurve2D
    {
        public readonly float3 P0; // Start Position
        public readonly float3 P1; // Start Tangent (World/Local space, not relative)
        public readonly float3 P2; // End Tangent (World/Local space, not relative)
        public readonly float3 P3; // End Position

        public BezierCurve2D(float3 p0, float3 p1, float3 p2, float3 p3)
        {
            P0 = p0;
            P1 = p1;
            P2 = p2;
            P3 = p3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float3 Evaluate(float t)
        {
            t = math.saturate(t);
            float rt = 1 - t;
            return rt * rt * rt * P0 +
                   3 * rt * rt * t * P1 +
                   3 * rt * t * t * P2 +
                   t * t * t * P3;
        }
    }
}