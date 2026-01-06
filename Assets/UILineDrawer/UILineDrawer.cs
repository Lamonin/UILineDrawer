/*
KNOW ISSUES:
1. Завязан на Unity.Splines, что не есть хорошо, т.к. рассчитано на 2D. Возможно стоит сделать кастомную реализацию для сплайнов.
*/

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.UI;
# if UNITY_EDITOR
using UnityEditor.Splines;
# endif

namespace Maro.UILineDrawer
{
    [RequireComponent(typeof(CanvasRenderer))]
    public class UILineDrawer : MaskableGraphic
    {
        internal const int MinSubdivisions = 1;
        internal const int MaxSubdivisions = 9;
        internal const float MinThickness = 0.1f;
        private const float MiterLimit = 2.0f;

        [SerializeField]
        private List<Spline2DPoint> m_Points = new();

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

        /// <summary>
        /// Gets or sets the thickness of the line.
        /// </summary>
        public float Thickness
        {
            get => m_Thickness;
            set
            {
                m_Thickness = Mathf.Max(value, MinThickness);
                SetVerticesDirty();
            }
        }

        /// <summary>
        /// Gets or sets the additional thickness used for raycasting.
        /// Ensures that the value is always non-negative.
        /// </summary>
        public float RaycastExtraThickness
        {
            get => m_RaycastExtraThickness;
            set { m_RaycastExtraThickness = Mathf.Max(value, 0); }
        }

        public float RaycastStartOffset
        {
            get => m_RaycastStartOffset;
            set { m_RaycastStartOffset = Mathf.Max(value, 0); }
        }

        public float RaycastEndOffset
        {
            get => m_RaycastEndOffset;
            set { m_RaycastEndOffset = Mathf.Max(value, 0); }
        }

        /// <summary>
        /// Number of subdivisions for the bezier curve. The higher the number, the more points will be generated.
        /// </summary>
        /// <remarks>
        /// The default value 1 will be enough for most cases. Increasing the value will make the line smoother,
        /// but will also increase the number of vertices.
        /// </remarks>
        public int Subdivisions
        {
            get => m_Subdivisions;
            set
            {
                m_Subdivisions = Mathf.Clamp(value, MinSubdivisions, MaxSubdivisions);
                UpdateSplineKnots();
            }
        }

        private Spline m_Spline;

        // For performance reasons, we cache the VertexHelper
        private VertexHelper vh;

        private List<float3> _optimizedPoints = new();

        protected override void OnEnable()
        {
            base.OnEnable();

            if (Application.isPlaying)
            {
                CreateSpline();
                Spline.Changed += OnSplineChangedRuntime;
            }

#if UNITY_EDITOR
            EditorSplineUtility.AfterSplineWasModified += OnSplineChangedEditor;
#endif
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            if (Application.isPlaying)
            {
                Spline.Changed -= OnSplineChangedRuntime;
            }

#if UNITY_EDITOR
            EditorSplineUtility.AfterSplineWasModified -= OnSplineChangedEditor;
#endif
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();

            CreateSpline();

            if (m_Thickness < MinThickness)
                m_Thickness = MinThickness;

            if (m_Subdivisions < MinSubdivisions)
                m_Subdivisions = MinSubdivisions;

            if (m_Subdivisions > MaxSubdivisions)
                m_Subdivisions = MaxSubdivisions;
        }
#endif

        private void CreateSpline()
        {
            m_Spline ??= new Spline();
        }

        private void OnSplineChangedRuntime(Spline spline, int i, SplineModification modification)
        {
            if (spline == m_Spline)
            {
                SetAllDirty();
            }
        }

#if UNITY_EDITOR
        private void OnSplineChangedEditor(Spline spline)
        {
            if (spline == m_Spline)
            {
                SetAllDirty();
            }
        }
#endif

        /// <summary>
        /// Adds a new point to the spline and updates the corresponding knot in m_Spline.
        /// </summary>
        public void AddPoint(Spline2DPoint point)
        {
            m_Points.Add(point);
            UpdateSplineKnots();
        }

        /// <summary>
        /// Removes a point at the specified index and updates the corresponding knot in m_Spline.
        /// </summary>
        public void RemovePoint(int index)
        {
            if (index < 0 || index >= m_Points.Count)
            {
                Debug.LogWarning("Index out of range while trying to remove a point.");
                return;
            }

            m_Points.RemoveAt(index);
            UpdateSplineKnots();
        }

        /// <summary>
        /// Updates an existing point at the specified index and updates the corresponding knot in m_Spline.
        /// </summary>
        public void UpdatePoint(int index, Spline2DPoint newPoint)
        {
            if (index < 0 || index >= m_Points.Count)
            {
                Debug.LogWarning("Index out of range while trying to update a point.");
                return;
            }

            m_Points[index] = newPoint;
            UpdateSplineKnots();
        }

        public void UpdatePoints(Spline2DPoint[] points)
        {
            m_Points.Clear();
            m_Points.AddRange(points);
            UpdateSplineKnots();
        }

        /// <summary>
        /// Sets the values of a knot based on a point.
        /// </summary>
        private static void SetKnotValues(ref BezierKnot knot, in Spline2DPoint point)
        {
            knot.Position = point.Position;
            knot.TangentIn = point.TangentIn;
            knot.TangentOut = point.TangentOut;
            knot.Rotation = quaternion.RotateZ(math.radians(point.Rotation));
        }

        /// <summary>
        /// Sets the values of a knot relative to an offset.
        /// </summary>
        private static void SetKnotValuesRelative(
            ref BezierKnot knot,
            in Spline2DPoint point,
            Vector3 offset
        )
        {
            knot.Position = point.Position - offset;
            knot.TangentIn = point.TangentIn;
            knot.TangentOut = point.TangentOut;
            knot.Rotation = quaternion.RotateZ(math.radians(point.Rotation));
        }

        private void RecalculateBounds()
        {
            var bounds = new Bounds(rectTransform.localPosition, Vector3.zero);
            foreach (var point in _optimizedPoints)
            {
                bounds.Encapsulate(point);
            }

            rectTransform.sizeDelta = new Vector2(
                bounds.size.x + m_Thickness + m_RaycastExtraThickness,
                bounds.size.y + m_Thickness + m_RaycastExtraThickness
            );
            rectTransform.anchoredPosition = bounds.center;
        }

        /// <summary>
        /// Synchronizes the m_Points list with the m_Spline object by updating its knots.
        /// </summary>
        private void UpdateSplineKnots()
        {
            if (m_Spline == null)
            {
                Debug.LogWarning("m_Spline is null. Cannot update knots.");
                return;
            }

            if (m_Spline.Count != m_Points.Count)
            {
                m_Spline.Resize(m_Points.Count);
            }

            // Set Broken mode for more control over tangents
            m_Spline.SetTangentMode(TangentMode.Broken);

            // First, set real knots values
            for (int i = 0; i < m_Points.Count; i++)
            {
                var knot = m_Spline[i];
                var point = m_Points[i];

                SetKnotValues(ref knot, point);

                m_Spline.SetKnotNoNotify(i, knot);
            }

            // Get optimized points
            _optimizedPoints = BezierUtility.GenerateOptimizedSplinePoints(
                m_Spline,
                m_Subdivisions
            );

            RecalculateBounds();

            // Offset points after bounds recalculation
            var localPosition = rectTransform.localPosition;

            var offset = (float3)localPosition;
            for (int i = 0; i < _optimizedPoints.Count; i++)
            {
                _optimizedPoints[i] -= offset;
            }

            // Also offset spline knots after bounds recalculation
            for (int i = 0; i < m_Points.Count; i++)
            {
                var knot = m_Spline[i];
                var point = m_Points[i];

                SetKnotValuesRelative(ref knot, point, localPosition);

                if (i == m_Points.Count - 1)
                    m_Spline.SetKnot(i, knot);
                else
                    m_Spline.SetKnotNoNotify(i, knot);
            }

            // Redraw mesh after updating spline knots
            SetVerticesDirty();
        }

        [ContextMenu("Refresh")]
        public void Refresh()
        {
            UpdateSplineKnots();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            this.vh ??= vh;

            vh.Clear();

            CreateMesh();
        }

        private void CreateMesh()
        {
            if (m_Spline == null || m_Spline.Count < 2 || m_Spline.GetLength() < m_Thickness)
                return;

            if (_optimizedPoints.Count < 2)
                return;

            UIVertex vertex = UIVertex.simpleVert;
            vertex.color = color;

            float3 current = _optimizedPoints[0];
            float3 next = _optimizedPoints[1];

            BeginLineSegment(vertex, current, next, out var prevVertIndex);

            var pointsCount = _optimizedPoints.Count;
            for (int i = 0; i < pointsCount - 2; i++)
            {
                var previous = _optimizedPoints[i];
                current = _optimizedPoints[i + 1];
                next = _optimizedPoints[i + 2];

                AddJoint(vertex, previous, current, next, ref prevVertIndex);
            }

            EndLineSegment(vertex, current, next, prevVertIndex);
        }

        private void BeginLineSegment(UIVertex vertex, float3 start, float3 next, out int currentVertIndex)
        {
            var direction = next - start;
            var normal = math.normalize(new float3(-direction.y, direction.x, 0));
            var perp = normal * (m_Thickness / 2);

            vertex.position = start + perp; // Top
            vertex.uv0 = new Vector2(0, 1);
            vh.AddVert(vertex);

            vertex.position = start - perp; // Bottom
            vertex.uv0 = new Vector2(0, 0);
            vh.AddVert(vertex);

            // The index of the first vertex of this pair
            currentVertIndex = vh.currentVertCount - 2;
        }

        private void AddJoint(UIVertex vertex, float3 previous, float3 current, float3 next, ref int prevVertIndex)
        {
            // Calculate directions
            var d1 = math.normalize(current - previous);
            var d2 = math.normalize(next - current);

            // Calculate normals (rotated 90 degrees)
            var n1 = new float3(-d1.y, d1.x, 0);
            var n2 = new float3(-d2.y, d2.x, 0);

            // Calculate Miter direction (average of normals)
            var miter = math.normalize(n1 + n2);

            // Calculate length needed to reach the intersection point
            // formula: thickness / 2 / dot(miter, normal)
            float dot = math.dot(miter, n1);

            // Check for parallel lines (dot ~ 0) or extremely sharp angles
            if (math.abs(dot) < 0.01f)
            {
                // Fallback for straight lines or 180 turns to avoid div by zero
                dot = 1.0f;
                miter = n1;
            }

            float miterLength = (m_Thickness / 2) / dot;

            // DECISION: Miter vs Bevel
            // If the intersection is too far away, chop it off (Bevel)
            if (math.abs(miterLength) > m_Thickness * MiterLimit)
            {
                // --- BEVEL JOINT ---
                // We add two pairs of vertices: one for the end of the prev segment, 
                // one for the start of the next segment.

                // Pair 1: End of previous segment
                var perp1 = n1 * (m_Thickness / 2);

                vertex.position = current + perp1;
                vertex.uv0 = new Vector2(0, 1);
                vh.AddVert(vertex);

                vertex.position = current - perp1;
                vertex.uv0 = new Vector2(0, 0);
                vh.AddVert(vertex);

                int bevelStartIndex = vh.currentVertCount - 2;

                // Connect previous segment to this bevel start
                vh.AddTriangle(prevVertIndex, bevelStartIndex, prevVertIndex + 1);
                vh.AddTriangle(prevVertIndex + 1, bevelStartIndex, bevelStartIndex + 1);

                // Pair 2: Start of next segment
                var perp2 = n2 * (m_Thickness / 2);

                vertex.position = current + perp2;
                vertex.uv0 = new Vector2(0, 1);
                vh.AddVert(vertex);

                vertex.position = current - perp2;
                vertex.uv0 = new Vector2(0, 0);
                vh.AddVert(vertex);

                int bevelEndIndex = vh.currentVertCount - 2;

                // Connect the gap between the two bevel edges (The actual corner triangle)
                // This fills the "wedge"
                vh.AddTriangle(bevelStartIndex, bevelEndIndex, bevelStartIndex + 1);
                vh.AddTriangle(bevelStartIndex + 1, bevelEndIndex, bevelEndIndex + 1);

                // Update index so the next segment connects to the end of this bevel
                prevVertIndex = bevelEndIndex;
            }
            else
            {
                // --- MITER JOINT ---
                // Standard sharp corner
                var miterVec = miter * miterLength;

                vertex.position = current + miterVec;
                vertex.uv0 = new Vector2(0, 1);
                vh.AddVert(vertex);

                vertex.position = current - miterVec;
                vertex.uv0 = new Vector2(0, 0);
                vh.AddVert(vertex);

                int currentIndex = vh.currentVertCount - 2;

                // Connect previous segment to current miter
                vh.AddTriangle(prevVertIndex, currentIndex, prevVertIndex + 1);
                vh.AddTriangle(prevVertIndex + 1, currentIndex, currentIndex + 1);

                prevVertIndex = currentIndex;
            }
        }

        private void EndLineSegment(UIVertex vertex, float3 previous, float3 end, int prevVertIndex)
        {
            var dir = end - previous;
            var normal = math.normalize(new float3(-dir.y, dir.x, 0));
            var perp = normal * (m_Thickness / 2);

            vertex.position = end + perp;
            vertex.uv0 = new Vector2(0, 1);
            vh.AddVert(vertex);

            vertex.position = end - perp;
            vertex.uv0 = new Vector2(0, 0);
            vh.AddVert(vertex);

            int currentIndex = vh.currentVertCount - 2;

            vh.AddTriangle(prevVertIndex, currentIndex, prevVertIndex + 1);
            vh.AddTriangle(prevVertIndex + 1, currentIndex, currentIndex + 1);
        }

        public override bool Raycast(Vector2 sp, Camera camera)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform,
                sp,
                camera,
                out Vector2 localPoint
            );

            var point = new float3(localPoint.x, localPoint.y, 0);
            var distance = SplineUtility.GetNearestPoint(m_Spline, point, out _, out var t);

            if (distance < m_Thickness + m_RaycastExtraThickness)
            {
                var splineLength = m_Spline.GetLength();
                var pointPosition = t * splineLength;

                return !(pointPosition < m_RaycastStartOffset)
                       && !(pointPosition > splineLength - m_RaycastEndOffset);
            }

            return false;
        }

        public float GetNearestNormalizedPosition(float3 point)
        {
            SplineUtility.GetNearestPoint(m_Spline, point, out _, out var t);
            return t;
        }
    }
}