using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Maro.UILineDrawer
{
    public static class BezierUtility
    {
        private const int MaxRecursionDepth = 16;
        private const float BaseTolerance = 1f;

        /// <summary>
        /// Populates the provided buffer with points representing the flattened spline.
        /// </summary>
        public static void GenerateOptimizedSplinePoints(Spline2D spline, List<float2> buffer, int subdivisions = 1)
        {
            buffer.Clear();

            int curveCount = spline.GetCurveCount();
            if (curveCount == 0) return;

            int estimatedPoints = curveCount * subdivisions * 8;
            if (buffer.Capacity < estimatedPoints)
            {
                buffer.Capacity = estimatedPoints;
            }

            float tolerance = BaseTolerance / math.max(1, subdivisions);
            float toleranceSq = tolerance * tolerance;

            for (int i = 0; i < curveCount; i++)
            {
                var curve = spline.GetCurve(i);

                // Add the very first point of the spline
                if (i == 0) buffer.Add(curve.P0);

                RecursiveBezierFlatnessCheck(
                    buffer,
                    curve.P0,
                    curve.P1,
                    curve.P2,
                    curve.P3,
                    toleranceSq,
                    0
                );
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RecursiveBezierFlatnessCheck(
            List<float2> points,
            float2 p0, float2 p1, float2 p2, float2 p3,
            float toleranceSq, int depth)
        {
            if (depth >= MaxRecursionDepth)
            {
                points.Add(p3);
                return;
            }

            // Flatness Test (Distance of Control Points from Chord)
            float2 v03 = p3 - p0;

            // 2D Cross product magnitude (Area of triangle * 2)
            // (p1 - p0) cross (p3 - p0)
            float d1Cross = math.abs((p1.x - p0.x) * v03.y - (p1.y - p0.y) * v03.x);
            float d2Cross = math.abs((p2.x - p0.x) * v03.y - (p2.y - p0.y) * v03.x);

            float lenSq03 = math.lengthsq(v03);

            if (lenSq03 < 1e-6f)
            {
                if (math.distancesq(p1, p0) <= toleranceSq && math.distancesq(p2, p0) <= toleranceSq)
                {
                    points.Add(p3);
                    return;
                }
            }
            else
            {
                if (d1Cross * d1Cross <= toleranceSq * lenSq03 && d2Cross * d2Cross <= toleranceSq * lenSq03)
                {
                    points.Add(p3);
                    return;
                }
            }

            // De Casteljau subdivision
            float2 p01 = (p0 + p1) * 0.5f;
            float2 p12 = (p1 + p2) * 0.5f;
            float2 p23 = (p2 + p3) * 0.5f;
            float2 p012 = (p01 + p12) * 0.5f;
            float2 p123 = (p12 + p23) * 0.5f;
            float2 p0123 = (p012 + p123) * 0.5f;

            RecursiveBezierFlatnessCheck(points, p0, p01, p012, p0123, toleranceSq, depth + 1);
            RecursiveBezierFlatnessCheck(points, p0123, p123, p23, p3, toleranceSq, depth + 1);
        }
    }
}