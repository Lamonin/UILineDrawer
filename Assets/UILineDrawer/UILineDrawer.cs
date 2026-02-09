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
        private const float MiterLimit = 1.0f;
        private const float MinTiling = 0.01f;
        private const float TilingFactor = 0.2f;
        private const float MinSegmentLengthSq = 1e-6f;
        private const float MinMiterDot = 0.01f;
        private const float RectWriteEpsilon = 0.001f;

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
        private RaycastOffsetMode m_RaycastOffsetMode = RaycastOffsetMode.Normalized;

        [SerializeField]
        private float m_RaycastStartOffset = 0.0f;

        [SerializeField]
        private float m_RaycastEndOffset = 1.0f;

        [SerializeField]
        private int m_Subdivisions = MinSubdivisions;

        private readonly Spline2D _spline = new Spline2D();
        private readonly List<float2> _optimizedPoints = new List<float2>();
        private readonly List<float> _cumulativeLengths = new List<float>();
        private float _optimizedTotalLength;
        private bool _isDirty = true;
        private bool _isBoundsDirty = true;

#if UNITY_EDITOR
        private RaycastOffsetMode _previousRaycastOffsetMode = RaycastOffsetMode.Normalized;
        private bool _hasRaycastOffsetModeSnapshot;
#endif

        /// <summary>
        /// Gets the underlying spline used for drawing.
        /// </summary>
        public Spline2D Spline => _spline;

        /// <summary>
        /// Gets or sets the thickness of the line.
        /// </summary>
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

        /// <summary>
        /// Gets or sets the number of subdivisions used for spline interpolation.
        /// </summary>
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

        /// <summary>
        /// Gets or sets the tiling factor for the line texture.
        /// </summary>
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

        /// <summary>
        /// Gets or sets whether a gradient is used for the line color.
        /// </summary>
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

        /// <summary>
        /// Gets or sets the gradient used for the line color.
        /// </summary>
        public Gradient Gradient
        {
            get => m_Gradient;
            set
            {
                m_Gradient = value;
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

        public float RaycastExtraThickness
        {
            get => m_RaycastExtraThickness;
            set => m_RaycastExtraThickness = Mathf.Max(value, 0);
        }

        public RaycastOffsetMode OffsetMode
        {
            get => m_RaycastOffsetMode;
            set
            {
                if (m_RaycastOffsetMode == value) return;
                ConvertRaycastOffsets(m_RaycastOffsetMode, value);
                m_RaycastOffsetMode = value;
                SnapshotRaycastOffsetMode();
                SanitizeRaycastOffsets();
            }
        }

        public float RaycastStartOffset
        {
            get => m_RaycastStartOffset;
            set
            {
                m_RaycastStartOffset = value;
                SanitizeRaycastOffsets();
            }
        }

        public float RaycastEndOffset
        {
            get => m_RaycastEndOffset;
            set
            {
                m_RaycastEndOffset = value;
                SanitizeRaycastOffsets();
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
            SnapshotRaycastOffsetMode();
            SetDirty();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();

            m_Thickness = Mathf.Max(m_Thickness, MinThickness);
            m_Subdivisions = Mathf.Clamp(m_Subdivisions, MinSubdivisions, MaxSubdivisions);
            m_Tiling = Mathf.Max(m_Tiling, MinTiling);

            if (_hasRaycastOffsetModeSnapshot)
            {
                if (_previousRaycastOffsetMode != m_RaycastOffsetMode)
                {
                    ConvertRaycastOffsets(_previousRaycastOffsetMode, m_RaycastOffsetMode);
                }
            }

            SnapshotRaycastOffsetMode();
            SanitizeRaycastOffsets();

            SetDirty();
        }
#endif

        private void SnapshotRaycastOffsetMode()
        {
#if UNITY_EDITOR
            _previousRaycastOffsetMode = m_RaycastOffsetMode;
            _hasRaycastOffsetModeSnapshot = true;
#endif
        }

        private float GetOffsetConversionLength()
        {
            EnsureValidSpline(recalculateBounds: false);
            return math.max(_optimizedTotalLength, 0f);
        }

        private void ConvertRaycastOffsets(RaycastOffsetMode fromMode, RaycastOffsetMode toMode)
        {
            float length = GetOffsetConversionLength();
            RaycastOffsetUtility.Convert(
                fromMode,
                toMode,
                length,
                ref m_RaycastStartOffset,
                ref m_RaycastEndOffset
            );
        }

        private void SanitizeRaycastOffsets()
        {
            RaycastOffsetUtility.Sanitize(
                ref m_RaycastExtraThickness,
                m_RaycastOffsetMode,
                ref m_RaycastStartOffset,
                ref m_RaycastEndOffset
            );
        }

        private void SetDirty()
        {
            _isDirty = true;
            _isBoundsDirty = true;
        }

        private void Update()
        {
            if (_isDirty || _isBoundsDirty)
            {
                UpdateSplineLogic(markVerticesDirty: _isDirty, recalculateBounds: true);
                _isDirty = false;
                _isBoundsDirty = false;
            }
        }

        private void RecalculateBounds()
        {
            if (!PolylineUtility.TryCalculatePaddedBounds(
                    _optimizedPoints,
                    (m_Thickness * 0.5f) + m_RaycastExtraThickness,
                    MinThickness,
                    out var min,
                    out var max
                ))
            {
                var defaultSize = new Vector2(1f, 1f);
                var defaultPivot = new Vector2(0.5f, 0.5f);

                if (!Approximately(rectTransform.sizeDelta, defaultSize, RectWriteEpsilon))
                {
                    rectTransform.sizeDelta = defaultSize;
                }

                if (!Approximately(rectTransform.pivot, defaultPivot, RectWriteEpsilon))
                {
                    rectTransform.pivot = defaultPivot;
                }

                return;
            }

            var size = max - min;
            var newPivot = new Vector2(-min.x / size.x, -min.y / size.y);
            var newSize = new Vector2(size.x, size.y);

            if (!IsFinite(new float2(newPivot.x, newPivot.y)))
            {
                newPivot = new Vector2(0.5f, 0.5f);
            }

            if (!Approximately(rectTransform.sizeDelta, newSize, RectWriteEpsilon))
            {
                rectTransform.sizeDelta = newSize;
            }

            if (!Approximately(rectTransform.pivot, newPivot, RectWriteEpsilon))
            {
                rectTransform.pivot = newPivot;
            }
        }

        private static bool Approximately(Vector2 a, Vector2 b, float epsilon)
        {
            return (a - b).sqrMagnitude <= epsilon * epsilon;
        }

        private static bool IsFinite(float2 p)
        {
            return math.all(math.isfinite(p));
        }

        private static bool TryGetDirection(float2 from, float2 to, out float2 direction)
        {
            direction = to - from;
            float lengthSq = math.lengthsq(direction);
            if (lengthSq <= MinSegmentLengthSq || !math.isfinite(lengthSq))
            {
                direction = new float2(1, 0);
                return false;
            }

            direction *= math.rsqrt(lengthSq);
            return true;
        }

        private static void GetPerpendicular(float2 from, float2 to, float halfThickness, out float2 perpendicular)
        {
            if (!TryGetDirection(from, to, out var direction))
            {
                perpendicular = new float2(0, halfThickness);
                return;
            }

            perpendicular = new float2(-direction.y, direction.x) * halfThickness;
        }

        private void UpdateSplineLogic(bool markVerticesDirty, bool recalculateBounds)
        {
            m_Points ??= new List<BezierKnot2D>();
            _spline.SetKnots(m_Points);

            if (m_Points.Count == 0)
            {
                _optimizedPoints.Clear();
                _cumulativeLengths.Clear();
                _optimizedTotalLength = 0f;
                if (recalculateBounds)
                {
                    RecalculateBounds();
                }

                if (markVerticesDirty)
                {
                    SetVerticesDirty();
                }

                return;
            }

            BezierUtility.GenerateOptimizedSplinePoints(_spline, _optimizedPoints, m_Subdivisions);
            PolylineUtility.CollapseDegeneratePoints(_optimizedPoints, MinSegmentLengthSq);
            _optimizedTotalLength = PolylineUtility.RebuildLengthCache(_optimizedPoints, _cumulativeLengths);

            if (recalculateBounds)
            {
                RecalculateBounds();
            }

            if (markVerticesDirty)
            {
                SetVerticesDirty();
            }
        }

        private void EnsureValidSpline(bool recalculateBounds = true)
        {
            if (_isDirty || (recalculateBounds && _isBoundsDirty))
            {
                UpdateSplineLogic(markVerticesDirty: false, recalculateBounds: recalculateBounds);
                _isDirty = false;

                if (recalculateBounds)
                {
                    _isBoundsDirty = false;
                }
            }
        }

        /// <summary>
        /// Adds a new control point to the spline.
        /// </summary>
        public void AddPoint(BezierKnot2D point)
        {
            m_Points.Add(point);
            SetDirty();
        }

        /// <summary>
        /// Removes the control point at the specified index.
        /// </summary>
        public void RemovePoint(int index)
        {
            if (index < 0 || index >= m_Points.Count) return;
            m_Points.RemoveAt(index);
            SetDirty();
        }

        /// <summary>
        /// Updates the control point at the specified index.
        /// </summary>
        public void UpdatePoint(int index, BezierKnot2D newPoint)
        {
            if (index < 0 || index >= m_Points.Count) return;
            m_Points[index] = newPoint;
            SetDirty();
        }

        /// <summary>
        /// Replaces all control points with the provided collection.
        /// </summary>
        private void ReplacePoints(IReadOnlyList<BezierKnot2D> points, int count)
        {
            if (m_Points.Capacity < count)
            {
                m_Points.Capacity = count;
            }

            int existingCount = m_Points.Count;

            if (existingCount > count)
            {
                m_Points.RemoveRange(count, existingCount - count);
            }
            else
            {
                while (m_Points.Count < count)
                {
                    m_Points.Add(default);
                }
            }

            for (int i = 0; i < count; i++)
            {
                m_Points[i] = points[i];
            }
        }

        public void UpdatePoints(IEnumerable<BezierKnot2D> points)
        {
            if (points == null)
            {
                m_Points.Clear();
                SetDirty();
                return;
            }

            if (points is IReadOnlyList<BezierKnot2D> readOnlyPoints)
            {
                ReplacePoints(readOnlyPoints, readOnlyPoints.Count);
            }
            else
            {
                m_Points.Clear();
                m_Points.AddRange(points);
            }

            SetDirty();
        }

        public float GetNormalizedPosition(Vector2 point, int resolution = 10, int iterations = 5)
        {
            EnsureValidSpline();
            resolution = Mathf.Max(1, resolution);
            iterations = Mathf.Max(0, iterations);
            _spline.GetClosestPoint(point, out var t, resolution, iterations);
            return t;
        }

        public override bool Raycast(Vector2 sp, Camera eventCamera)
        {
            EnsureValidSpline();

            if (_spline.Count < 2) return false;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, sp, eventCamera, out Vector2 localPoint);

            var point = new float2(localPoint.x, localPoint.y);
            var pointOnSpline = _spline.GetClosestPoint(point, out var t);
            var distance = math.distance(point, pointOnSpline);

            if (distance < m_Thickness + m_RaycastExtraThickness)
            {
                float distanceAlongLine = 0f;
                if (m_RaycastOffsetMode == RaycastOffsetMode.Fixed)
                {
                    if (!PolylineUtility.TryGetDistanceAlongPolyline(
                            _optimizedPoints,
                            _cumulativeLengths,
                            pointOnSpline,
                            MinSegmentLengthSq,
                            out distanceAlongLine
                        ))
                    {
                        return false;
                    }
                }

                return RaycastOffsetUtility.IsPointInsideRange(
                    m_RaycastOffsetMode,
                    m_RaycastStartOffset,
                    m_RaycastEndOffset,
                    t,
                    distanceAlongLine,
                    _optimizedTotalLength
                );
            }

            return false;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            EnsureValidSpline(recalculateBounds: false);
            CreateMesh(vh);
        }

        private void CreateMesh(VertexHelper vh)
        {
            float totalLength = _optimizedTotalLength;

            if (_spline.Count < 2 || _optimizedPoints.Count < 2 || _cumulativeLengths.Count != _optimizedPoints.Count || totalLength <= 0f)
                return;

            UIVertex vertex = UIVertex.simpleVert;
            Color baseColor = color;

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

                currentDistance = _cumulativeLengths[i + 1];

                float u = uvBounds.x + (currentDistance * m_Tiling * TilingFactor);
                float t = currentDistance / totalLength;
                Color currentColor = m_UseGradient ? m_Gradient.Evaluate(t) : baseColor;

                AddJoint(vh, vertex, previous, current, next, u, uvBounds, currentColor, ref prevVertIndex);
            }

            currentDistance = totalLength;

            Color endColor = m_UseGradient ? m_Gradient.Evaluate(1f) : baseColor;
            float endU = uvBounds.x + (currentDistance * m_Tiling * TilingFactor);

            EndLineSegment(vh, vertex, current, next, endU, uvBounds, endColor, prevVertIndex);
        }

        private void BeginLineSegment(
            VertexHelper vh, UIVertex vertex, float2 start, float2 next, float u, Vector4 uvBounds, Color col,
            out int currentVertIndex
        )
        {
            float halfThickness = m_Thickness * 0.5f;
            GetPerpendicular(start, next, halfThickness, out var perp);

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
            bool hasD1 = TryGetDirection(previous, current, out var d1);
            bool hasD2 = TryGetDirection(current, next, out var d2);

            if (!hasD1 && !hasD2)
                return;

            if (!hasD1) d1 = d2;
            if (!hasD2) d2 = d1;

            float halfThickness = m_Thickness * 0.5f;

            var n1 = new float2(-d1.y, d1.x);
            var n2 = new float2(-d2.y, d2.x);
            var miter = n1 + n2;
            float miterLenSq = math.lengthsq(miter);
            bool useBevel = miterLenSq <= MinSegmentLengthSq || !math.isfinite(miterLenSq);
            float dot = 1f;

            if (!useBevel)
            {
                miter *= math.rsqrt(miterLenSq);
                dot = math.dot(miter, n1);
                useBevel = dot <= MinMiterDot;
            }

            vertex.color = col;

            if (useBevel)
            {
                var perp1 = n1 * halfThickness;

                vertex.position = new float3(current + perp1, 0);
                vertex.uv0 = new Vector2(u, uvBounds.w);
                vh.AddVert(vertex);

                vertex.position = new float3(current - perp1, 0);
                vertex.uv0 = new Vector2(u, uvBounds.y);
                vh.AddVert(vertex);

                int bevelStartIndex = vh.currentVertCount - 2;
                vh.AddTriangle(prevVertIndex, bevelStartIndex, prevVertIndex + 1);
                vh.AddTriangle(prevVertIndex + 1, bevelStartIndex, bevelStartIndex + 1);

                var perp2 = n2 * halfThickness;

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
                float miterLength = halfThickness / dot;
                if (miterLength > m_Thickness * MiterLimit)
                {
                    var perp1 = n1 * halfThickness;

                    vertex.position = new float3(current + perp1, 0);
                    vertex.uv0 = new Vector2(u, uvBounds.w);
                    vh.AddVert(vertex);

                    vertex.position = new float3(current - perp1, 0);
                    vertex.uv0 = new Vector2(u, uvBounds.y);
                    vh.AddVert(vertex);

                    int bevelStartIndex = vh.currentVertCount - 2;
                    vh.AddTriangle(prevVertIndex, bevelStartIndex, prevVertIndex + 1);
                    vh.AddTriangle(prevVertIndex + 1, bevelStartIndex, bevelStartIndex + 1);

                    var perp2 = n2 * halfThickness;

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
                    return;
                }

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
            float halfThickness = m_Thickness * 0.5f;
            GetPerpendicular(previous, end, halfThickness, out var perp);

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
