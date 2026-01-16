using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Maro.UILineDrawer
{
    /// <summary>
    /// Represents a cached cubic bezier curve segment (P0, P1, P2, P3).
    /// </summary>
    public readonly struct BezierCurve2D
    {
        public readonly float2 P0;
        public readonly float2 P1;
        public readonly float2 P2;
        public readonly float2 P3;

        public BezierCurve2D(float2 p0, float2 p1, float2 p2, float2 p3)
        {
            P0 = p0;
            P1 = p1;
            P2 = p2;
            P3 = p3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float2 Evaluate(float t)
        {
            t = math.saturate(t);
            float rt = 1 - t;
            return rt * rt * rt * P0 +
                   3 * rt * rt * t * P1 +
                   3 * rt * t * t * P2 +
                   t * t * t * P3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float2 EvaluateDerivative(float t)
        {
            t = math.saturate(t);
            float rt = 1f - t;
            // Derivative of Cubic Bezier: 3(1-t)^2(P1-P0) + 6(1-t)t(P2-P1) + 3t^2(P3-P2)
            return 3f * rt * rt * (P1 - P0) +
                   6f * rt * t * (P2 - P1) +
                   3f * t * t * (P3 - P2);
        }
    }
}