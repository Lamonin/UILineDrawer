using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace Maro.UILineDrawer
{
    [RequireComponent(typeof(CanvasRenderer))]
    [ExecuteAlways]
    public class UILineDrawer : MaskableGraphic
    {
        internal const int MinSubdivisions = 1;
        internal const int MaxSubdivisions = 9;
        internal const float MinThickness = 0.1f;
        private const float MiterLimit = 1.0f;

        [SerializeField]
        private List<BezierKnot2D> m_Points = new();

        [SerializeField]
        private float m_Thickness = 5;

        [SerializeField]
        private float m_RaycastExtraThickness = 1;

        [SerializeField]
        private float m_RaycastStartOffset = 0.0f;

        [SerializeField]
        private float m_RaycastEndOffset = 0.0f;

        [SerializeField]
        private int m_Subdivisions = MinSubdivisions;

        private readonly Spline2D m_Spline = new Spline2D();
        private readonly List<float2> _optimizedPoints = new();
        private bool _isDirty;

        public float Thickness
        {
            get => m_Thickness;
            set
            {
                if (Mathf.Approximately(m_Thickness, value)) return;
                m_Thickness = Mathf.Max(value, MinThickness);
                SetDirty();
            }
        }

        public float RaycastExtraThickness
        {
            get => m_RaycastExtraThickness;
            set { m_RaycastExtraThickness = Mathf.Max(value, 0); }
        }

        public int Subdivisions
        {
            get => m_Subdivisions;
            set
            {
                int val = Mathf.Clamp(value, MinSubdivisions, MaxSubdivisions);
                if (m_Subdivisions == val) return;
                m_Subdivisions = val;
                SetDirty();
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            SetDirty();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();

            m_Thickness = Mathf.Max(m_Thickness, MinThickness);
            m_Subdivisions = Mathf.Clamp(m_Subdivisions, MinSubdivisions, MaxSubdivisions);

            SetDirty();
        }
#endif

        private void SetDirty()
        {
            _isDirty = true;
        }

        protected virtual void Update()
        {
            if (_isDirty)
            {
                UpdateSplineLogic();
                _isDirty = false;
            }
        }

        private void RecalculateBounds()
        {
            if (_optimizedPoints.Count == 0) return;

            var min = new float2(float.MaxValue);
            var max = new float2(float.MinValue);

            foreach (var p in _optimizedPoints)
            {
                min = math.min(min, p);
                max = math.max(max, p);
            }

            float padding = m_Thickness * 0.5f + m_RaycastExtraThickness;
            min -= padding;
            max += padding;

            var size = max - min;
            size = math.max(size, new float2(MinThickness));

            var newPivot = new Vector2(-min.x / size.x, -min.y / size.y);

            rectTransform.sizeDelta = new Vector2(size.x, size.y);
            rectTransform.pivot = newPivot;
        }

        private void UpdateSplineLogic()
        {
            m_Spline.SetKnots(m_Points);

            if (m_Points.Count == 0)
            {
                _optimizedPoints.Clear();
                return;
            }

            BezierUtility.GenerateOptimizedSplinePoints(m_Spline, _optimizedPoints, m_Subdivisions);
            RecalculateBounds();
            SetVerticesDirty();
        }

        private void EnsureValidSpline()
        {
            if (_isDirty)
            {
                UpdateSplineLogic();
                _isDirty = false;
            }
        }

        public void AddPoint(BezierKnot2D point)
        {
            m_Points.Add(point);
            SetDirty();
        }

        public void RemovePoint(int index)
        {
            if (index < 0 || index >= m_Points.Count) return;
            m_Points.RemoveAt(index);
            SetDirty();
        }

        public void UpdatePoint(int index, BezierKnot2D newPoint)
        {
            if (index < 0 || index >= m_Points.Count) return;
            m_Points[index] = newPoint;
            SetDirty();
        }

        public void UpdatePoints(IEnumerable<BezierKnot2D> points)
        {
            m_Points.Clear();
            m_Points.AddRange(points);
            SetDirty();
        }

        public float GetNormalizedPosition(Vector2 point, int resolution = 10, int iterations = 5)
        {
            EnsureValidSpline();
            m_Spline.GetClosestPoint(point, out var t, resolution, iterations);
            return t;
        }

        public override bool Raycast(Vector2 sp, Camera camera)
        {
            EnsureValidSpline();

            if (m_Spline == null || m_Spline.Count < 2) return false;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, sp, camera, out Vector2 localPoint);

            var point = new float2(localPoint.x, localPoint.y);
            var pointOnSpline = m_Spline.GetClosestPoint(point, out var t);
            var distance = math.distance(point, pointOnSpline);

            if (distance < m_Thickness + m_RaycastExtraThickness)
            {
                var splineLength = m_Spline.GetLength();
                var pointPosition = t * splineLength;

                return !(pointPosition < m_RaycastStartOffset)
                       && !(pointPosition > splineLength - m_RaycastEndOffset);
            }

            return false;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            CreateMesh(vh);
        }

        private void CreateMesh(VertexHelper vh)
        {
            if (m_Spline == null || m_Spline.Count < 2 || _optimizedPoints.Count < 2 || m_Spline.GetLength() < m_Thickness)
                return;

            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;

            float2 current = _optimizedPoints[0];
            float2 next = _optimizedPoints[1];

            BeginLineSegment(vh, vertex, current, next, out var prevVertIndex);

            for (int i = 0; i < _optimizedPoints.Count - 2; i++)
            {
                var previous = _optimizedPoints[i];
                current = _optimizedPoints[i + 1];
                next = _optimizedPoints[i + 2];
                AddJoint(vh, vertex, previous, current, next, ref prevVertIndex);
            }

            EndLineSegment(vh, vertex, current, next, prevVertIndex);
        }

        private void BeginLineSegment(VertexHelper vh, UIVertex vertex, float2 start, float2 next, out int currentVertIndex)
        {
            var direction = next - start;
            var normal = math.normalize(new float2(-direction.y, direction.x));
            var perp = normal * (m_Thickness / 2);

            vertex.position = new float3(start + perp, 0);
            vertex.uv0 = new Vector2(0, 1);
            vh.AddVert(vertex);

            vertex.position = new float3(start - perp, 0);
            vertex.uv0 = new Vector2(0, 0);
            vh.AddVert(vertex);

            currentVertIndex = vh.currentVertCount - 2;
        }

        private void AddJoint(VertexHelper vh, UIVertex vertex, float2 previous, float2 current, float2 next, ref int prevVertIndex)
        {
            var d1 = math.normalize(current - previous);
            var d2 = math.normalize(next - current);

            var n1 = new float2(-d1.y, d1.x);
            var n2 = new float2(-d2.y, d2.x);

            var miter = math.normalize(n1 + n2);
            float dot = math.dot(miter, n1);

            if (math.abs(dot) < 0.01f)
            {
                dot = 1.0f;
                miter = n1;
            }

            float miterLength = (m_Thickness / 2) / dot;

            if (math.abs(miterLength) > m_Thickness * MiterLimit)
            {
                var perp1 = n1 * (m_Thickness / 2);
                vertex.position = new float3(current + perp1, 0);
                vertex.uv0 = new Vector2(0, 1);
                vh.AddVert(vertex);
                vertex.position = new float3(current - perp1, 0);
                vertex.uv0 = new Vector2(0, 0);
                vh.AddVert(vertex);

                int bevelStartIndex = vh.currentVertCount - 2;
                vh.AddTriangle(prevVertIndex, bevelStartIndex, prevVertIndex + 1);
                vh.AddTriangle(prevVertIndex + 1, bevelStartIndex, bevelStartIndex + 1);

                var perp2 = n2 * (m_Thickness / 2);
                vertex.position = new float3(current + perp2, 0);
                vertex.uv0 = new Vector2(0, 1);
                vh.AddVert(vertex);
                vertex.position = new float3(current - perp2, 0);
                vertex.uv0 = new Vector2(0, 0);
                vh.AddVert(vertex);

                int bevelEndIndex = vh.currentVertCount - 2;
                vh.AddTriangle(bevelStartIndex, bevelEndIndex, bevelStartIndex + 1);
                vh.AddTriangle(bevelStartIndex + 1, bevelEndIndex, bevelEndIndex + 1);

                prevVertIndex = bevelEndIndex;
            }
            else
            {
                var miterVec = miter * miterLength;
                vertex.position = new float3(current + miterVec, 0);
                vertex.uv0 = new Vector2(0, 1);
                vh.AddVert(vertex);
                vertex.position = new float3(current - miterVec, 0);
                vertex.uv0 = new Vector2(0, 0);
                vh.AddVert(vertex);

                int currentIndex = vh.currentVertCount - 2;
                vh.AddTriangle(prevVertIndex, currentIndex, prevVertIndex + 1);
                vh.AddTriangle(prevVertIndex + 1, currentIndex, currentIndex + 1);

                prevVertIndex = currentIndex;
            }
        }

        private void EndLineSegment(VertexHelper vh, UIVertex vertex, float2 previous, float2 end, int prevVertIndex)
        {
            var dir = end - previous;
            var normal = math.normalize(new float2(-dir.y, dir.x));
            var perp = normal * (m_Thickness / 2);

            vertex.position = new float3(end + perp, 0);
            vertex.uv0 = new Vector2(0, 1);
            vh.AddVert(vertex);
            vertex.position = new float3(end - perp, 0);
            vertex.uv0 = new Vector2(0, 0);
            vh.AddVert(vertex);

            int currentIndex = vh.currentVertCount - 2;
            vh.AddTriangle(prevVertIndex, currentIndex, prevVertIndex + 1);
            vh.AddTriangle(prevVertIndex + 1, currentIndex, currentIndex + 1);
        }
    }
}