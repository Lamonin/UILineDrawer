using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Maro.UILineDrawer
{
    /// <summary>
    /// A high-performance 2D Spline container using Unity.Mathematics.
    /// </summary>
    public class Spline2D
    {
        private List<BezierKnot2D> m_Knots;

        private readonly List<BezierCurve2D> m_CachedCurves;
        private float m_CachedLength;
        private int m_CachedLengthResolution;

        private bool m_IsDirty;
        private bool m_IsLengthDirty;
        private bool m_Closed = false;

        public bool Closed
        {
            get => m_Closed;
            set
            {
                if (m_Closed != value)
                {
                    m_Closed = value;
                    SetDirty();
                }
            }
        }

        public Spline2D()
        {
            m_Knots = new List<BezierKnot2D>();
            m_CachedCurves = new List<BezierCurve2D>();
            m_IsDirty = true;
        }

        public int Count => m_Knots.Count;

        public BezierKnot2D this[int index]
        {
            get => m_Knots[index];
            set
            {
                m_Knots[index] = value;
                SetDirty();
            }
        }

        public void SetKnots(List<BezierKnot2D> knots)
        {
            m_Knots = knots;
            SetDirty();
        }

        public void Add(BezierKnot2D knot)
        {
            m_Knots.Add(knot);
            SetDirty();
        }

        public void Update(int index, BezierKnot2D newKnot)
        {
            m_Knots[index] = newKnot;
            SetDirty();
        }

        public void Resize(int newSize)
        {
            if (m_Knots.Count == newSize) return;

            if (m_Knots.Count > newSize)
            {
                m_Knots.RemoveRange(newSize, m_Knots.Count - newSize);
            }
            else
            {
                while (m_Knots.Count < newSize)
                    m_Knots.Add(new BezierKnot2D());
            }

            SetDirty();
        }

        private void SetDirty()
        {
            m_IsDirty = true;
            m_IsLengthDirty = true;
        }

        private static float NormalizeAngleDeg(float angle)
        {
            if (angle >= -180f && angle <= 180f) return angle;
            angle = math.fmod(angle, 360f);
            if (angle > 180f) angle -= 360f;
            if (angle < -180f) angle += 360f;
            return angle;
        }

        private static float2 RotateTangent(float2 tangent, float rotationDeg)
        {
            if (math.lengthsq(tangent) <= 1e-12f) return tangent;

            float angle = NormalizeAngleDeg(rotationDeg);
            if (math.abs(angle) <= 1e-5f) return tangent;

            float radians = math.radians(angle);
            math.sincos(radians, out float sin, out float cos);
            return new float2(
                (cos * tangent.x) - (sin * tangent.y),
                (sin * tangent.x) + (cos * tangent.y)
            );
        }

        private void EnsureCurveCache()
        {
            if (!m_IsDirty) return;

            m_CachedCurves.Clear();
            int knotCount = m_Knots.Count;

            if (knotCount < 2)
            {
                m_IsDirty = false;
                return;
            }

            int curveCount = m_Closed ? knotCount : knotCount - 1;

            for (int i = 0; i < curveCount; i++)
            {
                int nextIndex = (i + 1) % knotCount;

                var startKnot = m_Knots[i];
                var endKnot = m_Knots[nextIndex];

                float2 tOut = RotateTangent(startKnot.TangentOut, startKnot.Rotation);
                float2 tIn = RotateTangent(endKnot.TangentIn, endKnot.Rotation);

                m_CachedCurves.Add(
                    new BezierCurve2D(
                        startKnot.Position,
                        startKnot.Position + tOut,
                        endKnot.Position + tIn,
                        endKnot.Position
                    )
                );
            }

            m_IsDirty = false;
        }

        public int GetCurveCount()
        {
            EnsureCurveCache();
            return m_CachedCurves.Count;
        }

        /// <summary>
        /// Returns a cached BezierCurve2D. O(1) access after first build.
        /// </summary>
        public BezierCurve2D GetCurve(int index)
        {
            EnsureCurveCache();
            if (index < 0 || index >= m_CachedCurves.Count) return default;
            return m_CachedCurves[index];
        }

        /// <summary>
        /// Evaluates the spline at a normalized position t (0.0 to 1.0).
        /// Returns both the position and the 2D normal vector at that point.
        /// </summary>
        public void Evaluate(float t, out float2 position, out float2 normal)
        {
            EnsureCurveCache();

            int curveCount = m_CachedCurves.Count;
            if (curveCount == 0)
            {
                position = float2.zero;
                normal = new float2(0, 1);
                return;
            }

            // Map global t to curve index and local t
            t = math.saturate(t);
            float totalT = t * curveCount;
            int index = (int)totalT;
            float localT = totalT - index;

            // Handle end of spline edge case
            if (index >= curveCount)
            {
                index = curveCount - 1;
                localT = 1f;
            }

            var curve = m_CachedCurves[index];
            position = curve.Evaluate(localT);

            // Calculate tangent, then rotate 90 degrees for normal (-y, x)
            float2 tangent = curve.EvaluateDerivative(localT);
            float2 perp = new float2(-tangent.y, tangent.x);

            // NormalizeSafe handles zero-length tangents gracefully
            normal = math.normalizesafe(perp);
        }

        public float GetLength(int resolutionPerCurve = 10)
        {
            EnsureCurveCache();

            if (m_IsLengthDirty || resolutionPerCurve != m_CachedLengthResolution)
            {
                m_CachedLength = 0;
                int count = m_CachedCurves.Count;
                float step = 1.0f / resolutionPerCurve;

                for (int i = 0; i < count; i++)
                {
                    var curve = m_CachedCurves[i];
                    float2 prevPos = curve.P0;

                    for (int j = 1; j <= resolutionPerCurve; j++)
                    {
                        float t = j * step;
                        float2 currentPos = curve.Evaluate(t);
                        m_CachedLength += math.distance(prevPos, currentPos);
                        prevPos = currentPos;
                    }
                }

                m_CachedLengthResolution = resolutionPerCurve;
                m_IsLengthDirty = false;
            }

            return m_CachedLength;
        }

        /// <summary>
        /// Finds the closest point on the spline to the specified target position.
        /// </summary>
        public Vector2 GetClosestPoint(Vector2 point, int resolution = 10, int iterations = 5)
        {
            return GetClosestPoint(point, out _, resolution, iterations);
        }

        /// <summary>
        /// Finds the closest point on the spline to the specified target position and returns the position and normalized global t value via out parameters.
        /// </summary>
        public Vector2 GetClosestPoint(Vector2 point, out float normalizedT, int resolution = 10, int iterations = 5)
        {
            EnsureCurveCache();

            if (m_CachedCurves.Count == 0)
            {
                normalizedT = 0f;
                return default;
            }

            float minSqDist = float.MaxValue;
            int bestCurveIndex = 0;
            float bestCurveT = 0f;
            float2 bestPos = float2.zero;

            int curveCount = m_CachedCurves.Count;

            // Rough pass
            for (int i = 0; i < curveCount; i++)
            {
                var curve = m_CachedCurves[i];

                for (int s = 0; s <= resolution; s++)
                {
                    float t = (float)s / resolution;
                    float2 pos = curve.Evaluate(t);
                    float sqDist = math.distancesq(point, pos);

                    if (sqDist < minSqDist)
                    {
                        minSqDist = sqDist;
                        bestCurveIndex = i;
                        bestCurveT = t;
                        bestPos = pos;
                    }
                }
            }

            // Binary search refinement
            var bestCurve = m_CachedCurves[bestCurveIndex];
            float stepSize = 1.0f / resolution;

            for (int i = 0; i < iterations; i++)
            {
                stepSize *= 0.5f;

                float tNeg = math.saturate(bestCurveT - stepSize);
                float tPos = math.saturate(bestCurveT + stepSize);

                float2 posNeg = bestCurve.Evaluate(tNeg);
                float2 posPos = bestCurve.Evaluate(tPos);

                float sqDistNeg = math.distancesq(point, posNeg);
                float sqDistPos = math.distancesq(point, posPos);

                if (sqDistNeg < minSqDist)
                {
                    minSqDist = sqDistNeg;
                    bestCurveT = tNeg;
                    bestPos = posNeg;
                }
                else if (sqDistPos < minSqDist)
                {
                    minSqDist = sqDistPos;
                    bestCurveT = tPos;
                    bestPos = posPos;
                }
            }

            normalizedT = (bestCurveIndex + bestCurveT) / curveCount;
            return bestPos;
        }
    }
}
