using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Maro.UILineDrawer
{
    public static class BezierUtility
    {
        private const int MaxRecursionDepth = 16;
        private const float BaseTolerance = 2f;

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

        /// <summary>
        /// Finds the nearest point on the spline to the given world point.
        /// </summary>
        public static float GetNearestPoint(Spline2D spline, float2 point, out float2 nearestPosition, out float normalizedT)
        {
            float minDstSq = float.MaxValue;
            nearestPosition = float2.zero;
            normalizedT = 0;

            int curveCount = spline.GetCurveCount();
            if (curveCount == 0) return float.MaxValue;

            float tPerCurve = 1.0f / curveCount;

            for (int i = 0; i < curveCount; i++)
            {
                var curve = spline.GetCurve(i);

                GetNearestPointOnCurve(curve, point, out float curveT, out float2 pos, out float dstSq);

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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GetNearestPointOnCurve(BezierCurve2D curve, float2 p, out float t, out float2 pos, out float dstSq)
        {
            const int steps = 10;
            float minSq = float.MaxValue;
            t = 0;

            for (int i = 0; i <= steps; i++)
            {
                float currT = i / (float)steps;
                float2 currPos = curve.Evaluate(currT);
                float sq = math.distancesq(p, currPos);
                if (sq < minSq)
                {
                    minSq = sq;
                    t = currT;
                }
            }

            float range = 1.0f / (2 * steps);
            for (int i = 0; i < 4; i++)
            {
                float t1 = math.clamp(t - range, 0, 1);
                float t2 = math.clamp(t + range, 0, 1);

                float2 p1 = curve.Evaluate(t1);
                float2 p2 = curve.Evaluate(t2);

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