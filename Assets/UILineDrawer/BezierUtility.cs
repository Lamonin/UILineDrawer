using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace Maro.UILineDrawer
{
    public static class BezierUtility
    {
        private const int MaxRecursionDepth = 16;

        // This tolerance acts as a "pixel error" threshold. 
        // 0.5f means the curve won't deviate more than 0.5 units from the true spline.
        private const float BaseTolerance = 0.5f;

        public static List<float3> GenerateOptimizedSplinePoints(Spline spline, int subdivisions = 1)
        {
            // Estimate capacity to avoid resizing: CurveCount * 2^Subdivisions is a safe upper bound estimate,
            // but we use a more conservative heuristic to save memory while being faster than default.
            int curveCount = spline.GetCurveCount();
            int estimatedPoints = curveCount * subdivisions * 4;
            var points = new List<float3>(estimatedPoints);

            // Calculate squared tolerance based on subdivisions.
            // Higher subdivisions -> smaller tolerance -> more points.
            float tolerance = BaseTolerance / math.max(1, subdivisions);
            float toleranceSq = tolerance * tolerance;

            for (int i = 0; i < curveCount; i++)
            {
                var curve = spline.GetCurve(i);

                // For the very first curve, add the start point.
                // For subsequent curves, the start point is the same as the previous end point.
                if (i == 0)
                {
                    points.Add(curve.P0);
                }

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
            float3 p0,
            float3 p1,
            float3 p2,
            float3 p3,
            float toleranceSq,
            int depth)
        {
            // Stop recursion if deep enough
            if (depth >= MaxRecursionDepth)
            {
                points.Add(p3);
                return;
            }

            // --- FLATTNESS TEST (Roger Willcocks' algorithm simplified) ---
            // We check if the control points P1 and P2 are close enough to the 
            // chord connecting P0 and P3.

            // Vector from Start to End
            float3 v03 = p3 - p0;

            // Cross product in 2D (z is ignored) gives twice the area of the triangle.
            // Area = (x1*y2 - y1*x2)
            // Distance = Area / Base

            float d1_cross = math.abs((p1.x - p0.x) * v03.y - (p1.y - p0.y) * v03.x);
            float d2_cross = math.abs((p2.x - p0.x) * v03.y - (p2.y - p0.y) * v03.x);

            // Using squared lengths to avoid Sqrt.
            // Condition: (Distance)^2 < ToleranceSq
            // Equivalent: (Cross / Base)^2 < ToleranceSq
            // Equivalent: Cross^2 < ToleranceSq * Base^2

            float lenSq03 = math.lengthsq(v03);

            // Handle edge case where p0 and p3 are identical (loop or zero length)
            if (lenSq03 < 1e-6f)
            {
                // Just check distance of control points to p0
                if (math.distancesq(p1, p0) <= toleranceSq &&
                    math.distancesq(p2, p0) <= toleranceSq)
                {
                    points.Add(p3);
                    return;
                }
            }
            else
            {
                // Check if both control points are close to the baseline
                if ((d1_cross * d1_cross) <= (toleranceSq * lenSq03) &&
                    (d2_cross * d2_cross) <= (toleranceSq * lenSq03))
                {
                    // It's flat enough
                    points.Add(p3);
                    return;
                }
            }

            // --- DE CASTELJAU SUBDIVISION ---
            // Standard mid-point calculation

            float3 p01 = (p0 + p1) * 0.5f;
            float3 p12 = (p1 + p2) * 0.5f;
            float3 p23 = (p2 + p3) * 0.5f;

            float3 p012 = (p01 + p12) * 0.5f;
            float3 p123 = (p12 + p23) * 0.5f;

            float3 p0123 = (p012 + p123) * 0.5f; // New shared anchor point

            // Recurse Left
            RecursiveBezierFlatnessCheck(points, p0, p01, p012, p0123, toleranceSq, depth + 1);

            // Recurse Right
            RecursiveBezierFlatnessCheck(points, p0123, p123, p23, p3, toleranceSq, depth + 1);
        }
    }
}