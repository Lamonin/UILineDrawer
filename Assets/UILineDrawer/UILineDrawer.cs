using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace Maro.UILineDrawer
{
    [RequireComponent(typeof(CanvasRenderer))]
    [ExecuteAlways]
    public class UILineDrawer : MaskableGraphic
    {
        private const int MinSubdivisions = 1;
        private const int MaxSubdivisions = 9;
        private const float MinThickness = 0.1f;
        private const float MinTiling = 0.01f;
        private const float MiterLimit = 1.0f;
        private const float TilingFactor = 0.2f;

        [SerializeField]
        private List<BezierKnot2D> m_Points = new List<BezierKnot2D>()
        {
            new BezierKnot2D(new float2(-50, 0)),
            new BezierKnot2D(new float2(50, 0))
        };

        [SerializeField]
        private Sprite m_Sprite;

        [SerializeField]
        private float m_Tiling = 1.0f;

        [SerializeField]
        private bool m_UseGradient = false;

        [SerializeField]
        private Gradient m_Gradient = new Gradient();

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

        private readonly Spline2D _spline = new Spline2D();
        private readonly List<float2> _optimizedPoints = new List<float2>();
        private bool _isDirty = true;

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

        public float Tiling
        {
            get => m_Tiling;
            set
            {
                if (Mathf.Approximately(m_Tiling, value)) return;
                m_Tiling = Mathf.Max(value, MinTiling);
                SetDirty();
            }
        }

        public bool UseGradient
        {
            get => m_UseGradient;
            set
            {
                if (m_UseGradient == value) return;
                m_UseGradient = value;
                SetDirty();
            }
        }

        public Gradient Gradient
        {
            get => m_Gradient;
            set
            {
                m_Gradient = value;
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

        public Sprite Sprite
        {
            get => m_Sprite;
            set
            {
                if (m_Sprite == value) return;
                m_Sprite = value;
                SetDirty();
                SetMaterialDirty();
            }
        }

        public override Texture mainTexture
        {
            get
            {
                if (material != null && material.mainTexture != null)
                    return material.mainTexture;

                if (m_Sprite != null)
                    return m_Sprite.texture;

                return s_WhiteTexture;
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
            m_Tiling = Mathf.Max(m_Tiling, MinTiling);

            SetDirty();
        }
#endif

        private void SetDirty()
        {
            _isDirty = true;
        }

        private void Update()
        {
            if (_isDirty)
            {
                UpdateSplineLogic();
                _isDirty = false;
            }
        }

        private void RecalculateBounds()
        {
            if (_optimizedPoints.Count == 0)
            {
                rectTransform.sizeDelta = new Vector2(1f, 1f);
                rectTransform.pivot = new Vector2(0.5f, 0.5f);
                return;
            }

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
            _spline.SetKnots(m_Points);

            if (m_Points.Count == 0)
            {
                _optimizedPoints.Clear();
                return;
            }

            BezierUtility.GenerateOptimizedSplinePoints(_spline, _optimizedPoints, m_Subdivisions);
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
            _spline.GetClosestPoint(point, out var t, resolution, iterations);
            return t;
        }

        public override bool Raycast(Vector2 sp, Camera camera)
        {
            EnsureValidSpline();

            if (_spline.Count < 2) return false;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, sp, camera, out Vector2 localPoint);

            var point = new float2(localPoint.x, localPoint.y);
            var pointOnSpline = _spline.GetClosestPoint(point, out var t);
            var distance = math.distance(point, pointOnSpline);

            if (distance < m_Thickness + m_RaycastExtraThickness)
            {
                var splineLength = _spline.GetLength();
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
            float totalLength = _spline.GetLength();

            if (_spline.Count < 2 || _optimizedPoints.Count < 2 || totalLength < m_Thickness)
                return;

            UIVertex vertex = UIVertex.simpleVert;
            Color baseColor = color;

            if (totalLength <= 0) totalLength = 1f;

            Vector4 uvBounds = (m_Sprite != null)
                ? DataUtility.GetOuterUV(m_Sprite)
                : new Vector4(0, 0, 1, 1);

            float currentDistance = 0f;
            float2 current = _optimizedPoints[0];
            float2 next = _optimizedPoints[1];

            Color startColor = m_UseGradient ? m_Gradient.Evaluate(0f) : baseColor;

            float startU = uvBounds.x + (currentDistance * m_Tiling * TilingFactor);

            BeginLineSegment(vh, vertex, current, next, startU, uvBounds, startColor, out var prevVertIndex);

            for (int i = 0; i < _optimizedPoints.Count - 2; i++)
            {
                var previous = _optimizedPoints[i];
                current = _optimizedPoints[i + 1];
                next = _optimizedPoints[i + 2];

                currentDistance += math.distance(previous, current);

                float u = uvBounds.x + (currentDistance * m_Tiling * TilingFactor);
                float t = currentDistance / totalLength;
                Color currentColor = m_UseGradient ? m_Gradient.Evaluate(t) : baseColor;

                AddJoint(vh, vertex, previous, current, next, u, uvBounds, currentColor, ref prevVertIndex);
            }

            currentDistance += math.distance(current, next);

            Color endColor = m_UseGradient ? m_Gradient.Evaluate(1f) : baseColor;
            float endU = uvBounds.x + (currentDistance * m_Tiling * TilingFactor);

            EndLineSegment(vh, vertex, current, next, endU, uvBounds, endColor, prevVertIndex);
        }

        private void BeginLineSegment(
            VertexHelper vh, UIVertex vertex, float2 start, float2 next, float u, Vector4 uvBounds, Color col,
            out int currentVertIndex
        )
        {
            var direction = next - start;
            var normal = math.normalize(new float2(-direction.y, direction.x));
            var perp = normal * (m_Thickness / 2);

            vertex.color = col;

            vertex.position = new float3(start + perp, 0);
            vertex.uv0 = new Vector2(u, uvBounds.w); // Top V
            vh.AddVert(vertex);

            vertex.position = new float3(start - perp, 0);
            vertex.uv0 = new Vector2(u, uvBounds.y); // Bottom V
            vh.AddVert(vertex);

            currentVertIndex = vh.currentVertCount - 2;
        }

        private void AddJoint(
            VertexHelper vh, UIVertex vertex, float2 previous, float2 current, float2 next,
            float u, Vector4 uvBounds, Color col, ref int prevVertIndex
        )
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

            vertex.color = col;

            if (math.abs(miterLength) > m_Thickness * MiterLimit)
            {
                var perp1 = n1 * (m_Thickness / 2);

                vertex.position = new float3(current + perp1, 0);
                vertex.uv0 = new Vector2(u, uvBounds.w);
                vh.AddVert(vertex);

                vertex.position = new float3(current - perp1, 0);
                vertex.uv0 = new Vector2(u, uvBounds.y);
                vh.AddVert(vertex);

                int bevelStartIndex = vh.currentVertCount - 2;
                vh.AddTriangle(prevVertIndex, bevelStartIndex, prevVertIndex + 1);
                vh.AddTriangle(prevVertIndex + 1, bevelStartIndex, bevelStartIndex + 1);

                var perp2 = n2 * (m_Thickness / 2);

                vertex.position = new float3(current + perp2, 0);
                vertex.uv0 = new Vector2(u, uvBounds.w);
                vh.AddVert(vertex);

                vertex.position = new float3(current - perp2, 0);
                vertex.uv0 = new Vector2(u, uvBounds.y);
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
                vertex.uv0 = new Vector2(u, uvBounds.w);
                vh.AddVert(vertex);

                vertex.position = new float3(current - miterVec, 0);
                vertex.uv0 = new Vector2(u, uvBounds.y);
                vh.AddVert(vertex);

                int currentIndex = vh.currentVertCount - 2;
                vh.AddTriangle(prevVertIndex, currentIndex, prevVertIndex + 1);
                vh.AddTriangle(prevVertIndex + 1, currentIndex, currentIndex + 1);

                prevVertIndex = currentIndex;
            }
        }

        private void EndLineSegment(
            VertexHelper vh, UIVertex vertex, float2 previous, float2 end, float u, Vector4 uvBounds, Color col,
            int prevVertIndex
        )
        {
            var dir = end - previous;
            var normal = math.normalize(new float2(-dir.y, dir.x));
            var perp = normal * (m_Thickness / 2);

            vertex.color = col;

            vertex.position = new float3(end + perp, 0);
            vertex.uv0 = new Vector2(u, uvBounds.w);
            vh.AddVert(vertex);

            vertex.position = new float3(end - perp, 0);
            vertex.uv0 = new Vector2(u, uvBounds.y);
            vh.AddVert(vertex);

            int currentIndex = vh.currentVertCount - 2;
            vh.AddTriangle(prevVertIndex, currentIndex, prevVertIndex + 1);
            vh.AddTriangle(prevVertIndex + 1, currentIndex, currentIndex + 1);
        }
    }
}