using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Maro.UILineDrawer
{
    [CustomEditor(typeof(UILineDrawer))]
    public class UILineDrawerEditor : Editor
    {
        private VisualElement _root;
        private VisualElement _pointsListContainer;
        private Foldout _pointsFoldout;
        private Label _pageLabel;
        private Button _prevButton;
        private Button _nextButton;
        private IntegerField _arraySizeField;

        private const int ItemsPerPage = 10;

        private string FoldoutStateKey => $"UILineDrawer.PointsFoldout.{target.GetInstanceID()}";
        private string PointStateKey(int index) => $"UILineDrawer.Point.{target.GetInstanceID()}.{index}";
        private string PageStateKey => $"UILineDrawer.Page.{target.GetInstanceID()}";

        private int CurrentPage
        {
            get => SessionState.GetInt(PageStateKey, 0);
            set => SessionState.SetInt(PageStateKey, value);
        }

        public override VisualElement CreateInspectorGUI()
        {
            _root = new VisualElement();
            UILineDrawer drawer = (UILineDrawer)target;

            DrawPointsSection(drawer);

            AddPropertyField("m_Color");
            AddPropertyField("m_Thickness");
            AddPropertyField("m_Subdivisions");
            AddPropertyField("m_Maskable");
            AddPropertyField("m_RaycastTarget");
            AddPropertyField("m_RaycastExtraThickness");
            AddPropertyField("m_RaycastStartOffset");
            AddPropertyField("m_RaycastEndOffset");

            _root.TrackSerializedObjectValue(serializedObject, _ => { if (target != null) ((UILineDrawer)target).Refresh(); });

            return _root;
        }

        private void DrawPointsSection(UILineDrawer drawer)
        {
            _pointsFoldout = new Foldout { text = "Points", value = SessionState.GetBool(FoldoutStateKey, true) };
            _pointsFoldout.RegisterValueChangedCallback(evt => SessionState.SetBool(FoldoutStateKey, evt.newValue));

            var pointsProp = serializedObject.FindProperty("m_Points");
            _arraySizeField = new IntegerField { value = pointsProp.arraySize, isDelayed = true, style = { width = 32, height = 16, position = Position.Absolute, right = 0, top = 0 } };
            
            var input = _arraySizeField.Q("unity-text-input");
            if (input != null) { input.style.alignSelf = Align.FlexEnd; input.style.unityTextAlign = TextAnchor.MiddleRight; }

            _arraySizeField.RegisterValueChangedCallback(evt =>
            {
                int newSize = Mathf.Max(0, evt.newValue);
                serializedObject.FindProperty("m_Points").arraySize = newSize;
                serializedObject.ApplyModifiedProperties();
                RefreshPointsList();
                drawer.Refresh();
            });

            var toggle = _pointsFoldout.Q<Toggle>();
            if (toggle != null) { toggle.style.height = 16; toggle.Add(_arraySizeField); }

            _pointsListContainer = new VisualElement();
            _pointsFoldout.contentContainer.Add(_pointsListContainer);

            var paginationContainer = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.Center, marginTop = 10, marginBottom = 5 } };
            _prevButton = new Button(() => ChangePage(-1)) { text = "<", style = { width = 30 } };
            _nextButton = new Button(() => ChangePage(1)) { text = ">", style = { width = 30 } };
            _pageLabel = new Label { style = { alignSelf = Align.Center, marginLeft = 10, marginRight = 10, minWidth = 60, unityTextAlign = TextAnchor.MiddleCenter } };
            
            paginationContainer.Add(_prevButton);
            paginationContainer.Add(_pageLabel);
            paginationContainer.Add(_nextButton);
            _pointsFoldout.contentContainer.Add(paginationContainer);

            var addButton = new Button(() => OnAddPoint()) { text = "+ Add Point", style = { height = 25, marginTop = 5, marginBottom = 5, backgroundColor = new Color(0.25f, 0.35f, 0.25f) } };
            _pointsFoldout.contentContainer.Add(addButton);

            RefreshPointsList();
            _root.Add(_pointsFoldout);
        }

        private void RefreshPointsList()
        {
            _pointsListContainer.Clear();
            SerializedProperty pointsArray = serializedObject.FindProperty("m_Points");
            int totalPoints = pointsArray.arraySize;

            int totalPages = Mathf.CeilToInt((float)totalPoints / ItemsPerPage);
            if (CurrentPage >= totalPages && totalPages > 0) CurrentPage = totalPages - 1;
            if (totalPages == 0) CurrentPage = 0;
            if (CurrentPage < 0) CurrentPage = 0;

            int startIndex = CurrentPage * ItemsPerPage;
            int endIndex = Mathf.Min(startIndex + ItemsPerPage, totalPoints);

            for (int i = startIndex; i < endIndex; i++)
            {
                string basePath = $"m_Points.Array.data[{i}]";
                _pointsListContainer.Add(CreatePointElement(i, basePath, totalPoints));
            }

            _pageLabel.text = $"Page {CurrentPage + 1} / {(totalPages == 0 ? 1 : totalPages)}";
            _prevButton.SetEnabled(CurrentPage > 0);
            _nextButton.SetEnabled(CurrentPage < totalPages - 1);
            _arraySizeField.SetValueWithoutNotify(totalPoints);
            _pointsListContainer.Bind(serializedObject);
        }

        private VisualElement CreatePointElement(int index, string propertyPath, int totalPoints)
        {
            var pointFoldout = new Foldout { text = $"Point {index}", value = SessionState.GetBool(PointStateKey(index), false), style = { marginBottom = 2, marginLeft = 5 } };
            pointFoldout.RegisterValueChangedCallback(evt => SessionState.SetBool(PointStateKey(index), evt.newValue));

            var actionsContainer = new VisualElement { style = { flexDirection = FlexDirection.Row, position = Position.Absolute, right = 5, top = 0 } };
            Button CreateHeaderBtn(string text, System.Action onClick, bool enabled = true)
            {
                var btn = new Button(onClick) { text = text, style = { width = 20, height = 18, fontSize = 10, marginLeft = 0, marginRight = 2, paddingLeft = 0, paddingRight = 0, backgroundColor = new Color(0.22f, 0.22f, 0.22f) } };
                btn.SetEnabled(enabled);
                btn.RegisterCallback<MouseUpEvent>(evt => evt.StopPropagation());
                btn.RegisterCallback<MouseDownEvent>(evt => evt.StopPropagation());
                return btn;
            }

            actionsContainer.Add(CreateHeaderBtn("▲", () => OnMovePoint(index, -1), index > 0));
            actionsContainer.Add(CreateHeaderBtn("▼", () => OnMovePoint(index, 1), index < totalPoints - 1));
            var removeBtn = CreateHeaderBtn("X", () => OnRemovePoint(index));
            removeBtn.style.backgroundColor = new Color(0.4f, 0.2f, 0.2f);
            actionsContainer.Add(removeBtn);

            var toggle = pointFoldout.Q<Toggle>();
            if (toggle != null) toggle.Add(actionsContainer);

            var content = pointFoldout.contentContainer;
            content.style.marginLeft = 5;

            void AddField(string relativePath, string label)
            {
                var pf = new PropertyField();
                pf.bindingPath = $"{propertyPath}.{relativePath}";
                pf.label = label;
                content.Add(pf);
            }

            // Updated Fields for BezierKnot2D
            AddField("Position", "Position");
            AddField("Rotation", "Rotation (Deg)");
            AddField("TangentIn", "Tangent In (Vec2)");
            AddField("TangentOut", "Tangent Out (Vec2)");

            return pointFoldout;
        }

        private void ChangePage(int direction) { CurrentPage += direction; RefreshPointsList(); }
        
        private void OnAddPoint()
        {
            SerializedProperty pointsProp = serializedObject.FindProperty("m_Points");
            pointsProp.arraySize++;
            serializedObject.ApplyModifiedProperties();
            int totalPages = Mathf.CeilToInt((float)pointsProp.arraySize / ItemsPerPage);
            CurrentPage = totalPages - 1;
            RefreshPointsList();
            ((UILineDrawer)target).Refresh();
        }

        private void OnRemovePoint(int index)
        {
            SerializedProperty pointsProp = serializedObject.FindProperty("m_Points");
            pointsProp.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            RefreshPointsList();
            ((UILineDrawer)target).Refresh();
        }

        private void OnMovePoint(int index, int direction)
        {
            SerializedProperty pointsProp = serializedObject.FindProperty("m_Points");
            int newIndex = index + direction;
            if (newIndex >= 0 && newIndex < pointsProp.arraySize)
            {
                pointsProp.MoveArrayElement(index, newIndex);
                serializedObject.ApplyModifiedProperties();
                bool stateA = SessionState.GetBool(PointStateKey(index), false);
                bool stateB = SessionState.GetBool(PointStateKey(newIndex), false);
                SessionState.SetBool(PointStateKey(index), stateB);
                SessionState.SetBool(PointStateKey(newIndex), stateA);
                RefreshPointsList();
                ((UILineDrawer)target).Refresh();
            }
        }

        private void AddPropertyField(string propertyName)
        {
            var prop = serializedObject.FindProperty(propertyName);
            if (prop != null) _root.Add(new PropertyField(prop));
        }
    }
}