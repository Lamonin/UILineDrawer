using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Maro.UILineDrawer
{
    internal static class PolylineUtility
    {
        public static void CollapseDegeneratePoints(List<float2> points, float minSegmentLengthSq)
        {
            if (points.Count == 0)
            {
                return;
            }

            int writeIndex = 0;
            float2 previousPoint = default;

            for (int i = 0; i < points.Count; i++)
            {
                float2 point = points[i];
                if (!IsFinite(point))
                {
                    continue;
                }

                if (writeIndex > 0 && math.distancesq(previousPoint, point) <= minSegmentLengthSq)
                {
                    continue;
                }

                points[writeIndex] = point;
                previousPoint = point;
                writeIndex++;
            }

            if (writeIndex < points.Count)
            {
                points.RemoveRange(writeIndex, points.Count - writeIndex);
            }
        }

        public static float RebuildLengthCache(List<float2> points, List<float> cumulativeLengths)
        {
            cumulativeLengths.Clear();

            int count = points.Count;
            if (count == 0)
            {
                return 0f;
            }

            if (cumulativeLengths.Capacity < count)
            {
                cumulativeLengths.Capacity = count;
            }

            cumulativeLengths.Add(0f);

            float totalLength = 0f;
            for (int i = 1; i < count; i++)
            {
                totalLength += math.distance(points[i - 1], points[i]);
                cumulativeLengths.Add(totalLength);
            }

            return totalLength;
        }

        public static bool TryGetDistanceAlongPolyline(
            List<float2> points,
            List<float> cumulativeLengths,
            float2 point,
            float minSegmentLengthSq,
            out float distance
        )
        {
            distance = 0f;

            if (points.Count < 2 || cumulativeLengths.Count != points.Count)
            {
                return false;
            }

            float bestSqDist = float.MaxValue;
            float bestDistance = 0f;

            for (int i = 0; i < points.Count - 1; i++)
            {
                float2 a = points[i];
                float2 b = points[i + 1];
                float2 ab = b - a;
                float abLenSq = math.lengthsq(ab);
                if (abLenSq <= minSegmentLengthSq)
                {
                    continue;
                }

                float segmentT = math.saturate(math.dot(point - a, ab) / abLenSq);
                float2 projectedPoint = a + (ab * segmentT);
                float sqDist = math.distancesq(point, projectedPoint);
                if (sqDist >= bestSqDist)
                {
                    continue;
                }

                bestSqDist = sqDist;
                float segmentLength = cumulativeLengths[i + 1] - cumulativeLengths[i];
                bestDistance = cumulativeLengths[i] + (segmentLength * segmentT);
            }

            if (Mathf.Approximately(bestSqDist, float.MaxValue))
            {
                return false;
            }

            distance = bestDistance;
            return true;
        }

        public static bool TryCalculatePaddedBounds(
            List<float2> points,
            float padding,
            float minSize,
            out float2 min,
            out float2 max
        )
        {
            min = default;
            max = default;

            if (points.Count == 0)
            {
                return false;
            }

            float2 localMin = new float2(float.MaxValue);
            float2 localMax = new float2(float.MinValue);
            bool hasFinitePoint = false;

            for (int i = 0; i < points.Count; i++)
            {
                float2 point = points[i];
                if (!IsFinite(point))
                {
                    continue;
                }

                hasFinitePoint = true;
                localMin = math.min(localMin, point);
                localMax = math.max(localMax, point);
            }

            if (!hasFinitePoint)
            {
                return false;
            }

            localMin -= padding;
            localMax += padding;

            float2 size = localMax - localMin;
            size = math.max(size, new float2(minSize));

            min = localMin;
            max = localMin + size;
            return true;
        }

        private static bool IsFinite(float2 point)
        {
            return math.all(math.isfinite(point));
        }
    }
}