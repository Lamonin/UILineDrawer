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

        /// <summary>
        /// Helper to mark caches as invalid.
        /// </summary>
        private void SetDirty()
        {
            m_IsDirty = true;
            m_IsLengthDirty = true;
        }

        private static float NormalizeAngleDeg(float angle)
        {
            angle = math.fmod(angle, 360f);
            if (angle > 180f) angle -= 360f;
            if (angle < -180f) angle += 360f;
            return angle;
        }

        /// <summary>
        /// Validates and rebuilds the BezierCurve2D cache if needed.
        /// </summary>
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

            // Determine how many curves we have
            int curveCount = m_Closed ? knotCount : knotCount - 1;

            for (int i = 0; i < curveCount; i++)
            {
                int nextIndex = (i + 1) % knotCount;

                var startKnot = m_Knots[i];
                var endKnot = m_Knots[nextIndex];

                // Rotate Tangents
                var startKnotRotation = NormalizeAngleDeg(startKnot.Rotation);
                float2 tOut = math.mul(float2x2.Rotate(math.radians(startKnotRotation)), startKnot.TangentOut);

                var endKnotRotation = NormalizeAngleDeg(endKnot.Rotation);
                float2 tIn = math.mul(float2x2.Rotate(math.radians(endKnotRotation)), endKnot.TangentIn);

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
        /// Calculates or returns the cached total length of the spline.
        /// </summary>
        public float GetLength(int resolutionPerCurve = 10)
        {
            EnsureCurveCache();

            // If knots changed OR requested resolution changed, we recalculate
            if (m_IsLengthDirty || resolutionPerCurve != m_CachedLengthResolution)
            {
                m_CachedLength = 0;
                int count = m_CachedCurves.Count;
                float step = 1.0f / resolutionPerCurve;

                for (int i = 0; i < count; i++)
                {
                    // Access direct struct from list (avoid copying if possible, though List indexer returns copy for struct)
                    var curve = m_CachedCurves[i];

                    float2 prevPos = curve.P0;

                    // Numerical integration
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