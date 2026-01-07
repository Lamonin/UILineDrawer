using System.Collections.Generic;
using Unity.Mathematics;

namespace Maro.UILineDrawer
{
    /// <summary>
    /// A high-performance 2D Spline container using Unity.Mathematics.
    /// </summary>
    public class Spline2D
    {
        private List<BezierKnot2D> m_Knots;

        public bool Closed { get; set; } = false;

        public Spline2D()
        {
            m_Knots = new List<BezierKnot2D>();
        }

        public Spline2D(int capacity)
        {
            m_Knots = new List<BezierKnot2D>(capacity);
        }

        public int Count => m_Knots.Count;

        public BezierKnot2D this[int index]
        {
            get => m_Knots[index];
            set => m_Knots[index] = value;
        }

        public void Add(BezierKnot2D knot) => m_Knots.Add(knot);
        public void Clear() => m_Knots.Clear();

        public void Resize(int newSize)
        {
            // Simple resize logic
            if (m_Knots.Count > newSize)
                m_Knots.RemoveRange(newSize, m_Knots.Count - newSize);
            else
            {
                while (m_Knots.Count < newSize)
                    m_Knots.Add(new BezierKnot2D());
            }
        }

        /// <summary>
        /// Updates a knot without triggering internal events (API compatibility).
        /// </summary>
        public void SetKnotNoNotify(int index, BezierKnot2D knot)
        {
            if (index >= 0 && index < m_Knots.Count)
                m_Knots[index] = knot;
        }

        public void SetKnot(int index, BezierKnot2D knot) => SetKnotNoNotify(index, knot);

        /// <summary>
        /// Returns the number of curves. If closed, count == knots, else count == knots - 1.
        /// </summary>
        public int GetCurveCount()
        {
            if (m_Knots.Count < 2) return 0;
            return Closed ? m_Knots.Count : m_Knots.Count - 1;
        }

        /// <summary>
        /// Constructs a BezierCurve2D struct from two knots.
        /// Converts relative tangents to absolute positions.
        /// </summary>
        public BezierCurve2D GetCurve(int index)
        {
            if (m_Knots.Count < 2) return default;

            int nextIndex = (index + 1) % m_Knots.Count;

            var startKnot = m_Knots[index];
            var endKnot = m_Knots[nextIndex];

            // P0 = Start Position
            // P1 = Start Position + Start TangentOut
            // P2 = End Position + End TangentIn
            // P3 = End Position

            return new BezierCurve2D(
                startKnot.Position,
                startKnot.Position + startKnot.TangentOut,
                endKnot.Position + endKnot.TangentIn,
                endKnot.Position
            );
        }

        /// <summary>
        /// Calculates the approximate length of the entire spline.
        /// </summary>
        public float GetLength(int resolutionPerCurve = 10)
        {
            float totalLength = 0;
            int curveCount = GetCurveCount();

            for (int i = 0; i < curveCount; i++)
            {
                var curve = GetCurve(i);
                float3 prevPos = curve.P0;
                float step = 1.0f / resolutionPerCurve;

                for (int j = 1; j <= resolutionPerCurve; j++)
                {
                    float t = j * step;
                    float3 currentPos = curve.Evaluate(t);
                    totalLength += math.distance(prevPos, currentPos);
                    prevPos = currentPos;
                }
            }

            return totalLength;
        }
    }
}