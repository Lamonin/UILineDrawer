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
        
        public int Count => m_Knots.Count;
        
        public BezierKnot2D this[int index]
        {
            get => m_Knots[index];
            set => m_Knots[index] = value;
        }

        public void SetKnots(List<BezierKnot2D> knots)
        {
            m_Knots = knots;
        }

        public void Add(BezierKnot2D knot) => m_Knots.Add(knot);
        public void Update(int index, BezierKnot2D newKnot) => m_Knots[index] = newKnot; 
        
        public void Resize(int newSize) 
        {
            if (m_Knots.Count > newSize)
                m_Knots.RemoveRange(newSize, m_Knots.Count - newSize);
            else
            {
                while (m_Knots.Count < newSize)
                    m_Knots.Add(new BezierKnot2D());
            }
        }
        
        public int GetCurveCount()
        {
            if (m_Knots.Count < 2) return 0;
            return Closed ? m_Knots.Count : m_Knots.Count - 1;
        }

        /// <summary>
        /// Constructs a BezierCurve2D struct from two knots.
        /// Handles rotation and relative tangent conversion to absolute world space.
        /// </summary>
        public BezierCurve2D GetCurve(int index)
        {
            if (m_Knots.Count < 2) return default;

            int nextIndex = (index + 1) % m_Knots.Count;
            
            var startKnot = m_Knots[index];
            var endKnot = m_Knots[nextIndex];
            
            float2 tOut = math.mul(float2x2.Rotate(startKnot.Rotation), startKnot.TangentOut);
            float2 tIn = math.mul(float2x2.Rotate(endKnot.Rotation), endKnot.TangentIn);
            
            return new BezierCurve2D(
                startKnot.Position,
                startKnot.Position + tOut,
                endKnot.Position + tIn,
                endKnot.Position
            );
        }

        public float GetLength(int resolutionPerCurve = 10)
        {
            float totalLength = 0;
            int curveCount = GetCurveCount();

            for (int i = 0; i < curveCount; i++)
            {
                var curve = GetCurve(i);
                float2 prevPos = curve.P0;
                float step = 1.0f / resolutionPerCurve;
                
                for (int j = 1; j <= resolutionPerCurve; j++)
                {
                    float t = j * step;
                    float2 currentPos = curve.Evaluate(t);
                    totalLength += math.distance(prevPos, currentPos);
                    prevPos = currentPos;
                }
            }
            return totalLength;
        }
    }
}