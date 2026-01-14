using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Maro.UILineDrawer
{
    [CustomEditor(typeof(UILineDrawer))]
    public class UILineDrawerEditor : Editor
    {
        private UILineDrawer _target;
        private Transform _transform;
        private int _previousArraySize;

        private const string ShowGizmosKey = "Maro.UILineDrawer.ShowGizmos";

        private const float KnotHandleSize = 0.12f;
        private const float TangentHandleSize = 0.08f;
        private const float ZeroTangentOffset = 0.2f;
        private const float ZeroTangentSizeFactor = 0.6f;
        private const float LineThickness = 2.0f;
        private readonly Vector2 _newPointOffset = new Vector2(1f, 1f);

        private readonly Color _lineColor = Color.cyan;
        private readonly Color _positionColor = Color.yellow;
        private readonly Color _rotationColor = new Color(1f, 0.5f, 0f, 0.7f);
        private readonly Color _tangentInColor = Color.green;
        private readonly Color _tangentOutColor = Color.blue;

        private void OnEnable()
        {
            _target = (UILineDrawer)target;
            _transform = _target.transform;

            var pointsProp = serializedObject.FindProperty("m_Points");
            _previousArraySize = pointsProp?.arraySize ?? 0;
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            var pointsProp = serializedObject.FindProperty("m_Points");
            var pointsField = new PropertyField(pointsProp);

            var colorField = new PropertyField(serializedObject.FindProperty("m_Color"));
            var thicknessField = new PropertyField(serializedObject.FindProperty("m_Thickness"));
            var subdivisionsField = new PropertyField(serializedObject.FindProperty("m_Subdivisions"));

            var raycastTargetProp = serializedObject.FindProperty("m_RaycastTarget");
            var raycastTargetField = new PropertyField(raycastTargetProp);

            var raycastOptionsContainer = new VisualElement();
            raycastOptionsContainer.style.paddingLeft = 15;

            var raycastExtraThicknessField =
                new PropertyField(serializedObject.FindProperty("m_RaycastExtraThickness"));
            var raycastStartOffsetField = new PropertyField(serializedObject.FindProperty("m_RaycastStartOffset"));
            var raycastEndOffsetField = new PropertyField(serializedObject.FindProperty("m_RaycastEndOffset"));

            var maskableField = new PropertyField(serializedObject.FindProperty("m_Maskable"));

            var spacer = new VisualElement { style = { height = 15 } };
            var divider = new VisualElement
                { style = { height = 1, backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.5f) } };
            var bottomSpacer = new VisualElement { style = { height = 10 } };

            var gizmoBtn = new Button();
            var helpBox = new HelpBox(
                "Controls:\n" +
                "• Drag Point: Move Position\n" +
                "• Drag Orange Ring: Rotate\n" +
                "• Hold SHIFT + Drag: Edit Tangents",
                HelpBoxMessageType.Info);

            root.Add(pointsField);
            root.Add(colorField);
            root.Add(thicknessField);
            root.Add(subdivisionsField);
            root.Add(raycastTargetField);

            raycastOptionsContainer.Add(raycastExtraThicknessField);
            raycastOptionsContainer.Add(raycastStartOffsetField);
            raycastOptionsContainer.Add(raycastEndOffsetField);

            root.Add(raycastOptionsContainer);
            root.Add(maskableField);
            root.Add(spacer);
            root.Add(divider);
            root.Add(bottomSpacer);
            root.Add(gizmoBtn);
            root.Add(helpBox);

            ToggleRaycastOptions(raycastTargetProp.boolValue);
            UpdateButtonState();

            root.TrackPropertyValue(pointsProp, prop =>
            {
                if (prop.arraySize > _previousArraySize)
                {
                    for (var i = _previousArraySize; i < prop.arraySize; i++)
                    {
                        if (i > 0)
                        {
                            var prevElement = prop.GetArrayElementAtIndex(i - 1);
                            var newElement = prop.GetArrayElementAtIndex(i);

                            var prevPosProp = prevElement.FindPropertyRelative("Position");
                            var newPosProp = newElement.FindPropertyRelative("Position");

                            var prevPos = GetFloat2(prevPosProp);
                            var newPos = GetFloat2(newPosProp);

                            if (prevPos == newPos)
                            {
                                SetFloat2(newPosProp, prevPos + _newPointOffset);
                            }
                        }
                    }

                    serializedObject.ApplyModifiedProperties();
                }

                _previousArraySize = prop.arraySize;

                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            });

            root.TrackPropertyValue(raycastTargetProp, (prop) => { ToggleRaycastOptions(prop.boolValue); });

            gizmoBtn.clicked += () =>
            {
                var currentState = SessionState.GetBool(ShowGizmosKey, true);
                SessionState.SetBool(ShowGizmosKey, !currentState);
                UpdateButtonState();
                SceneView.RepaintAll();
            };

            return root;

            void UpdateButtonState()
            {
                var areGizmosVisible = SessionState.GetBool(ShowGizmosKey, true);

                if (areGizmosVisible)
                {
                    gizmoBtn.text = "Hide Gizmos";
                    gizmoBtn.style.backgroundColor = new Color(0.8f, 0.2f, 0.2f, 1f);
                    gizmoBtn.style.color = Color.white;
                    helpBox.visible = true;
                    helpBox.style.display = DisplayStyle.Flex;
                }
                else
                {
                    gizmoBtn.text = "Show Gizmos";
                    gizmoBtn.style.backgroundColor = new Color(0.2f, 0.6f, 0.2f, 1f);
                    gizmoBtn.style.color = Color.white;
                    helpBox.visible = false;
                    helpBox.style.display = DisplayStyle.None;
                }
            }

            void ToggleRaycastOptions(bool isEnabled)
            {
                raycastOptionsContainer.style.display = isEnabled ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void OnSceneGUI()
        {
            if (!SessionState.GetBool(ShowGizmosKey, true)) return;

            var isShiftHeld = (Event.current.modifiers & EventModifiers.Shift) != 0;

            serializedObject.Update();

            var pointsProp = serializedObject.FindProperty("m_Points");
            DrawConnections(pointsProp);

            for (var i = 0; i < pointsProp.arraySize; i++)
            {
                DrawAndEditKnot(pointsProp, i, isShiftHeld);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawConnections(SerializedProperty pointsProp)
        {
            if (pointsProp.arraySize < 2) return;

            Handles.color = _lineColor;

            for (var i = 0; i < pointsProp.arraySize - 1; i++)
            {
                var p1Prop = pointsProp.GetArrayElementAtIndex(i);
                var p2Prop = pointsProp.GetArrayElementAtIndex(i + 1);

                var p1Local = (Vector3)GetFloat2(p1Prop.FindPropertyRelative("Position"));
                var p2Local = (Vector3)GetFloat2(p2Prop.FindPropertyRelative("Position"));

                var p1World = _transform.TransformPoint(p1Local);
                var p2World = _transform.TransformPoint(p2Local);

                Handles.DrawLine(p1World, p2World, LineThickness);
            }
        }

        private void DrawAndEditKnot(SerializedProperty pointsProp, int index, bool isShiftHeld)
        {
            var knotProp = pointsProp.GetArrayElementAtIndex(index);

            var posProp = knotProp.FindPropertyRelative("Position");
            var rotProp = knotProp.FindPropertyRelative("Rotation");
            var tanInProp = knotProp.FindPropertyRelative("TangentIn");
            var tanOutProp = knotProp.FindPropertyRelative("TangentOut");

            var localPos = GetFloat2(posProp);
            var rotation = rotProp.floatValue;
            var localTanIn = GetFloat2(tanInProp);
            var localTanOut = GetFloat2(tanOutProp);

            var knotRot = Quaternion.Euler(0, 0, rotation);
            var worldPos = _transform.TransformPoint(localPos);
            var handleSize = HandleUtility.GetHandleSize(worldPos);

            var isTanInZero = localTanIn.sqrMagnitude < 0.0001f;
            var isTanOutZero = localTanOut.sqrMagnitude < 0.0001f;

            var posId = GUIUtility.GetControlID(FocusType.Passive);
            var tanInId = GUIUtility.GetControlID(FocusType.Passive);
            var tanOutId = GUIUtility.GetControlID(FocusType.Passive);

            // Rotation Handle
            Handles.color = _rotationColor;
            EditorGUI.BeginChangeCheck();
            var currentRot = Quaternion.Euler(0, 0, rotation);

            var newRot = Handles.Disc(
                currentRot,
                worldPos,
                _transform.forward,
                handleSize * 0.5f,
                false,
                0
            );

            if (EditorGUI.EndChangeCheck())
            {
                rotProp.floatValue = newRot.eulerAngles.z;
            }

            // Position Handle
            Handles.color = _positionColor;
            if (!isShiftHeld)
            {
                EditorGUI.BeginChangeCheck();
                var newWorldPos = Handles.FreeMoveHandle(
                    posId,
                    worldPos,
                    handleSize * KnotHandleSize,
                    Vector3.zero,
                    Handles.DotHandleCap
                );

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_target, "Move Spline Point");
                    var newLocalPos = _transform.InverseTransformPoint(newWorldPos);
                    SetFloat2(posProp, newLocalPos);
                }
            }
            else
            {
                Handles.DotHandleCap(
                    posId,
                    worldPos,
                    Quaternion.identity,
                    handleSize * KnotHandleSize,
                    EventType.Repaint
                );
            }

            // Tangent Handles
            if (isShiftHeld)
            {
                Vector3 worldTanInPos, worldTanOutPos;
                float tanInSize, tanOutSize;

                if (isTanInZero)
                {
                    worldTanInPos =
                        _transform.TransformPoint(localPos + (Vector2.left * (handleSize * ZeroTangentOffset)));
                    tanInSize = handleSize * TangentHandleSize * ZeroTangentSizeFactor;
                }
                else
                {
                    worldTanInPos = _transform.TransformPoint(localPos + (Vector2)(knotRot * localTanIn));
                    tanInSize = handleSize * TangentHandleSize;
                }

                if (isTanOutZero)
                {
                    worldTanOutPos =
                        _transform.TransformPoint(localPos + (Vector2.right * (handleSize * ZeroTangentOffset)));
                    tanOutSize = handleSize * TangentHandleSize * ZeroTangentSizeFactor;
                }
                else
                {
                    worldTanOutPos = _transform.TransformPoint(localPos + (Vector2)(knotRot * localTanOut));
                    tanOutSize = handleSize * TangentHandleSize;
                }

                Handles.color = _tangentInColor;
                Handles.DrawLine(worldPos, worldTanInPos);

                Handles.color = _tangentOutColor;
                Handles.DrawLine(worldPos, worldTanOutPos);

                // Tangent IN
                Handles.color = _tangentInColor;
                EditorGUI.BeginChangeCheck();
                var newWorldTanIn = Handles.FreeMoveHandle(
                    tanInId,
                    worldTanInPos,
                    tanInSize,
                    Vector3.zero,
                    Handles.DotHandleCap
                );
                if (EditorGUI.EndChangeCheck())
                {
                    var newLocalPoint = _transform.InverseTransformPoint(newWorldTanIn);
                    var diff = (Vector2)newLocalPoint - localPos;
                    Vector2 rotatedDiff = Quaternion.Inverse(knotRot) * diff;
                    SetFloat2(tanInProp, rotatedDiff);
                }

                // Tangent OUT
                Handles.color = _tangentOutColor;
                EditorGUI.BeginChangeCheck();
                var newWorldTanOut = Handles.FreeMoveHandle(
                    tanOutId,
                    worldTanOutPos,
                    tanOutSize,
                    Vector3.zero,
                    Handles.DotHandleCap
                );
                if (EditorGUI.EndChangeCheck())
                {
                    var newLocalPoint = _transform.InverseTransformPoint(newWorldTanOut);
                    var diff = (Vector2)newLocalPoint - localPos;
                    Vector2 rotatedDiff = Quaternion.Inverse(knotRot) * diff;
                    SetFloat2(tanOutProp, rotatedDiff);
                }
            }
            else
            {
                if (!isTanInZero)
                {
                    var worldTanInPos = _transform.TransformPoint(localPos + (Vector2)(knotRot * localTanIn));
                    Handles.color = _tangentInColor;
                    Handles.DrawLine(worldPos, worldTanInPos);
                }

                if (!isTanOutZero)
                {
                    var worldTanOutPos = _transform.TransformPoint(localPos + (Vector2)(knotRot * localTanOut));
                    Handles.color = _tangentOutColor;
                    Handles.DrawLine(worldPos, worldTanOutPos);
                }
            }

            Handles.Label(worldPos + Vector3.up * (handleSize * 0.2f), $"P{index}");
        }

        private static Vector2 GetFloat2(SerializedProperty prop)
        {
            if (prop == null) return Vector2.zero;
            var x = prop.FindPropertyRelative("x").floatValue;
            var y = prop.FindPropertyRelative("y").floatValue;
            return new Vector2(x, y);
        }

        private static void SetFloat2(SerializedProperty prop, Vector2 value)
        {
            if (prop == null) return;
            prop.FindPropertyRelative("x").floatValue = value.x;
            prop.FindPropertyRelative("y").floatValue = value.y;
        }
    }
}