using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Maro.UILineDrawer
{
    public static class BezierUtility
    {
        private const int MaxRecursionDepth = 16;
        private const float BaseTolerance = 0.5f;

        /// <summary>
        /// Generates a list of points representing the flattened spline.
        /// </summary>
        public static List<float3> GenerateOptimizedSplinePoints(Spline2D spline, int subdivisions = 1)
        {
            int curveCount = spline.GetCurveCount();
            // Heuristic allocation
            int estimatedPoints = curveCount * subdivisions * 8;
            var points = new List<float3>(math.max(8, estimatedPoints));

            float tolerance = BaseTolerance / math.max(1, subdivisions);
            float toleranceSq = tolerance * tolerance;

            for (int i = 0; i < curveCount; i++)
            {
                var curve = spline.GetCurve(i);

                if (i == 0) points.Add(curve.P0);

                RecursiveBezierFlatnessCheck(
                    points,
                    curve.P0,
                    curve.P1,
                    curve.P2,
                    curve.P3,
                    toleranceSq,
                    0
                );
            }

            return points;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RecursiveBezierFlatnessCheck(
            List<float3> points,
            float3 p0, float3 p1, float3 p2, float3 p3,
            float toleranceSq, int depth)
        {
            if (depth >= MaxRecursionDepth)
            {
                points.Add(p3);
                return;
            }

            // Flatness Test (Distance of Control Points from Chord)
            float3 v03 = p3 - p0;

            // 2D Cross product magnitude (Area of triangle * 2)
            // (p1 - p0) cross (p3 - p0)
            float d1_cross = math.abs((p1.x - p0.x) * v03.y - (p1.y - p0.y) * v03.x);
            float d2_cross = math.abs((p2.x - p0.x) * v03.y - (p2.y - p0.y) * v03.x);

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
                if ((d1_cross * d1_cross) <= (toleranceSq * lenSq03) &&
                    (d2_cross * d2_cross) <= (toleranceSq * lenSq03))
                {
                    points.Add(p3);
                    return;
                }
            }

            // De Casteljau subdivision
            float3 p01 = (p0 + p1) * 0.5f;
            float3 p12 = (p1 + p2) * 0.5f;
            float3 p23 = (p2 + p3) * 0.5f;
            float3 p012 = (p01 + p12) * 0.5f;
            float3 p123 = (p12 + p23) * 0.5f;
            float3 p0123 = (p012 + p123) * 0.5f;

            RecursiveBezierFlatnessCheck(points, p0, p01, p012, p0123, toleranceSq, depth + 1);
            RecursiveBezierFlatnessCheck(points, p0123, p123, p23, p3, toleranceSq, depth + 1);
        }

        /// <summary>
        /// Finds the nearest point on the spline to the given world point.
        /// Returns the distance squared and the normalized T (0 to 1) along the WHOLE spline.
        /// </summary>
        public static float GetNearestPoint(Spline2D spline, float3 point, out float3 nearestPosition, out float normalizedT)
        {
            float minDstSq = float.MaxValue;
            nearestPosition = float3.zero;
            normalizedT = 0;

            int curveCount = spline.GetCurveCount();
            if (curveCount == 0) return float.MaxValue;

            float totalLength = 0;
            // Note: For perfect normalized T, we need exact arc lengths. 
            // For UI Raycasting, linear approximation of T based on curve index is usually sufficient 
            // and significantly faster.
            float tPerCurve = 1.0f / curveCount;

            for (int i = 0; i < curveCount; i++)
            {
                var curve = spline.GetCurve(i);

                // Get nearest point on this specific curve
                GetNearestPointOnCurve(curve, point, out float curveT, out float3 pos, out float dstSq);

                if (dstSq < minDstSq)
                {
                    minDstSq = dstSq;
                    nearestPosition = pos;
                    normalizedT = (i * tPerCurve) + (curveT * tPerCurve);
                }
            }

            return math.sqrt(minDstSq);
        }

        // Iterative approximation for finding nearest point on a cubic bezier.
        // Solves 5th degree polynomial via iterations (Gradient Descent style).
        private static void GetNearestPointOnCurve(BezierCurve2D curve, float3 p, out float t, out float3 pos, out float dstSq)
        {
            // 1. Sample coarse points to find rough area
            const int Steps = 10;
            float minSq = float.MaxValue;
            t = 0;

            for (int i = 0; i <= Steps; i++)
            {
                float currT = i / (float)Steps;
                float3 currPos = curve.Evaluate(currT);
                float sq = math.distancesq(p, currPos);
                if (sq < minSq)
                {
                    minSq = sq;
                    t = currT;
                }
            }

            // 2. Refine using binary search / iterative reduction around the best T
            // This is much faster/stable than Newton-Raphson for this specific use case
            float range = 1.0f / (2 * Steps);
            for (int i = 0; i < 4; i++) // 4 iterations of refinement
            {
                float t1 = math.clamp(t - range, 0, 1);
                float t2 = math.clamp(t + range, 0, 1);

                float3 p1 = curve.Evaluate(t1);
                float3 p2 = curve.Evaluate(t2);

                float d1 = math.distancesq(p, p1);
                float d2 = math.distancesq(p, p2);

                if (d1 < minSq)
                {
                    t = t1;
                    minSq = d1;
                }
                else if (d2 < minSq)
                {
                    t = t2;
                    minSq = d2;
                }

                range *= 0.5f;
            }

            pos = curve.Evaluate(t);
            dstSq = minSq;
        }
    }
}