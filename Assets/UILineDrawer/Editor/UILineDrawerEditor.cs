using System.Collections.Generic;
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

        private enum TangentMode
        {
            Free,
            Mirror
        }

        private TangentMode _tangentMode = TangentMode.Free;
        private Transform _transform;

        private const string ShowGizmosKey = "Maro.UILineDrawer.ShowGizmos";
        private const string TangentModeKey = "Maro.UILineDrawer.TangentMode";

        private const float KnotHandleSize = 0.12f;
        private const float TangentHandleSize = 0.08f;
        private const float ZeroTangentOffset = 0.2f;
        private const float ZeroTangentSizeFactor = 0.6f;
        private const float LineThickness = 2.0f;

        private readonly Color _lineColor = Color.cyan;
        private readonly Color _positionColor = Color.yellow;
        private readonly Color _rotationColor = new Color(1f, 0.5f, 0f, 0.7f);
        private readonly Color _tangentInColor = Color.green;
        private readonly Color _tangentOutColor = Color.blue;
        private readonly Color _raycastOffsetColor = Color.red;

        private void OnEnable()
        {
            _target = target as UILineDrawer;
            _transform = _target != null ? _target.transform : null;
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            var pointsProp = serializedObject.FindProperty("m_Points");
            var colorProp = serializedObject.FindProperty("m_Color");
            var useGradientProp = serializedObject.FindProperty("m_UseGradient");
            var gradientProp = serializedObject.FindProperty("m_Gradient");
            var spriteProp = serializedObject.FindProperty("m_Sprite");
            var tilingProp = serializedObject.FindProperty("m_Tiling");
            var materialProp = serializedObject.FindProperty("m_Material");
            var thicknessProp = serializedObject.FindProperty("m_Thickness");
            var subdivisionsProp = serializedObject.FindProperty("m_Subdivisions");
            var raycastTargetProp = serializedObject.FindProperty("m_RaycastTarget");
            var maskableProp = serializedObject.FindProperty("m_Maskable");
            var raycastExtraThicknessProp = serializedObject.FindProperty("m_RaycastExtraThickness");
            var raycastStartOffsetProp = serializedObject.FindProperty("m_RaycastStartOffset");
            var raycastEndOffsetProp = serializedObject.FindProperty("m_RaycastEndOffset");

            root.Add(CreateHeader("Path Configuration", 0, 2));
            root.Add(new PropertyField(pointsProp) { tooltip = "The list of points defining the shape of the path." });
            root.Add(new PropertyField(thicknessProp) { tooltip = "The visual width of the path." });
            root.Add(new PropertyField(subdivisionsProp) { tooltip = "The number of segments between points for smoother curves." });

            root.Add(CreateHeader("Appearance", 4, 2));

            var colorModeDropdown = new DropdownField(
                "Color Mode",
                new List<string> { "Solid Color", "Gradient" },
                useGradientProp.boolValue ? 1 : 0
            );
            colorModeDropdown.AddToClassList(DropdownField.alignedFieldUssClassName);

            var solidColorField = new PropertyField(colorProp)
                { label = "Color", tooltip = "Solid color applied to the path when Color Mode is set to Solid." };
            var gradientField = new PropertyField(gradientProp)
                { label = "Gradient", tooltip = "Gradient applied across the path length. This takes priority over solid color when enabled." };

            var colorContainer = new VisualElement();
            colorContainer.Add(colorModeDropdown);
            colorContainer.Add(solidColorField);
            colorContainer.Add(gradientField);
            root.Add(colorContainer);

            root.Add(new PropertyField(materialProp) { tooltip = "Optional. Custom material to override the default path rendering." });
            root.Add(new PropertyField(spriteProp) { tooltip = "Optional. Sprite used as a texture along the path." });
            root.Add(new PropertyField(tilingProp) { tooltip = "Controls how many times the texture repeats along the path length." });

            UpdateColorMode(useGradientProp.boolValue ? "Gradient" : "Solid Color");
            colorModeDropdown.RegisterValueChangedCallback(evt => UpdateColorMode(evt.newValue));

            root.Add(CreateHeader("Interaction", 4, 2));
            root.Add(new PropertyField(raycastTargetProp) { tooltip = "Whether this path can be hit by UI pointer events." });

            var raycastOptionsContainer = new VisualElement { style = { paddingLeft = 15 } };
            raycastOptionsContainer.Add(
                new PropertyField(raycastExtraThicknessProp)
                    { tooltip = "Increases the hit detection area beyond the visual thickness." }
            );
            raycastOptionsContainer.Add(
                new PropertyField(raycastStartOffsetProp)
                    { tooltip = "Adjusts where the raycast detection starts (normalized 0-1)." }
            );
            raycastOptionsContainer.Add(
                new PropertyField(raycastEndOffsetProp)
                    { tooltip = "Adjusts where the raycast detection ends (normalized 0-1)." }
            );
            root.Add(raycastOptionsContainer);
            root.Add(new PropertyField(maskableProp) { tooltip = "Whether the path is affected by UI Mask components." });

            ToggleRaycastOptions(raycastTargetProp.boolValue);
            root.TrackPropertyValue(raycastTargetProp, prop => ToggleRaycastOptions(prop.boolValue));

            root.Add(
                new VisualElement
                {
                    style =
                    {
                        height = 1, marginTop = 4, marginBottom = 4, backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.5f)
                    }
                }
            );

            var gizmoBtn = new Button { style = { height = 25 } };
            var helpBox = new HelpBox(
                "Controls:\n" +
                "• Drag Point: Move Position\n" +
                "• Drag Orange Ring: Rotate\n" +
                "• Hold SHIFT + Drag: Edit Tangents",
                HelpBoxMessageType.Info
            );

            root.Add(gizmoBtn);
            root.Add(helpBox);

            var tangentModeContainer = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

            var tangentModeLabel = new Label("Tangent Mode");
            tangentModeLabel.style.minWidth = 100;

            var tangentModeFreeButton = new Button
            {
                text = "Free",
                style =
                {
                    flexGrow = 1,
                    marginRight = 2
                }
            };
            tangentModeFreeButton.AddToClassList("toggle-button");

            var tangentModeMirrorButton = new Button
            {
                text = "Mirror",
                style =
                {
                    flexGrow = 1,
                    marginLeft = 2
                }
            };
            tangentModeMirrorButton.AddToClassList("toggle-button");

            _tangentMode = (TangentMode)SessionState.GetInt(TangentModeKey, 0);

            tangentModeFreeButton.clicked += () =>
            {
                if (_tangentMode == TangentMode.Free) return;
                _tangentMode = TangentMode.Free;
                SessionState.SetInt(TangentModeKey, (int)_tangentMode);
                UpdateTangentModeButtons(_tangentMode);
            };

            tangentModeMirrorButton.clicked += () =>
            {
                if (_tangentMode == TangentMode.Mirror) return;
                _tangentMode = TangentMode.Mirror;
                SessionState.SetInt(TangentModeKey, (int)_tangentMode);
                UpdateTangentModeButtons(_tangentMode);
            };

            UpdateTangentModeButtons(_tangentMode);

            tangentModeContainer.Add(tangentModeLabel);
            tangentModeContainer.Add(tangentModeFreeButton);
            tangentModeContainer.Add(tangentModeMirrorButton);

            var areGizmosVisible = SessionState.GetBool(ShowGizmosKey, true);
            tangentModeContainer.style.display = areGizmosVisible ? DisplayStyle.Flex : DisplayStyle.None;

            root.Add(tangentModeContainer);

            UpdateButtonState();
            gizmoBtn.clicked += () =>
            {
                var currentState = SessionState.GetBool(ShowGizmosKey, true);
                SessionState.SetBool(ShowGizmosKey, !currentState);
                UpdateButtonState();
                SceneView.RepaintAll();

                tangentModeContainer.style.display = !currentState ? DisplayStyle.Flex : DisplayStyle.None;
            };

            return root;

            void UpdateTangentModeButtons(TangentMode tangentMode)
            {
                var activeColor = new Color(0.6f, 0.6f, 0.6f, 0.6f);
                var inactiveColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);

                if (tangentMode == TangentMode.Free)
                {
                    tangentModeFreeButton.style.backgroundColor = activeColor;
                    tangentModeFreeButton.style.unityFontStyleAndWeight = FontStyle.Bold;
                    tangentModeMirrorButton.style.backgroundColor = inactiveColor;
                    tangentModeMirrorButton.style.unityFontStyleAndWeight = FontStyle.Normal;
                }
                else
                {
                    tangentModeFreeButton.style.backgroundColor = inactiveColor;
                    tangentModeFreeButton.style.unityFontStyleAndWeight = FontStyle.Normal;
                    tangentModeMirrorButton.style.backgroundColor = activeColor;
                    tangentModeMirrorButton.style.unityFontStyleAndWeight = FontStyle.Bold;
                }
            }

            void UpdateButtonState()
            {
                var areGizmosVisible = SessionState.GetBool(ShowGizmosKey, true);
                if (areGizmosVisible)
                {
                    gizmoBtn.text = "Hide Gizmos";
                    gizmoBtn.style.backgroundColor = new Color(0.8f, 0.2f, 0.2f, 1f);
                    gizmoBtn.style.color = Color.white;
                    helpBox.style.display = DisplayStyle.Flex;
                }
                else
                {
                    gizmoBtn.text = "Show Gizmos";
                    gizmoBtn.style.backgroundColor = new Color(0.2f, 0.6f, 0.2f, 1f);
                    gizmoBtn.style.color = Color.white;
                    helpBox.style.display = DisplayStyle.None;
                }
            }

            void ToggleRaycastOptions(bool isEnabled)
            {
                raycastOptionsContainer.style.display = isEnabled ? DisplayStyle.Flex : DisplayStyle.None;
            }

            void UpdateColorMode(string mode)
            {
                bool isGradient = mode == "Gradient";
                if (useGradientProp.boolValue != isGradient)
                {
                    useGradientProp.boolValue = isGradient;
                    serializedObject.ApplyModifiedProperties();
                }

                solidColorField.style.display = isGradient ? DisplayStyle.None : DisplayStyle.Flex;
                gradientField.style.display = isGradient ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private static Label CreateHeader(string text, float mt = 0, float mb = 0)
        {
            return new Label(text)
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginTop = mt,
                    marginBottom = mb,
                }
            };
        }

        private void OnSceneGUI()
        {
            if (_target == null || _transform == null) return;
            if (!SessionState.GetBool(ShowGizmosKey, true)) return;

            var isShiftHeld = (Event.current.modifiers & EventModifiers.Shift) != 0;

            serializedObject.UpdateIfRequiredOrScript();

            var pointsProp = serializedObject.FindProperty("m_Points");
            DrawConnections(pointsProp);

            DrawRaycastOffsets();

            for (var i = 0; i < pointsProp.arraySize; i++)
            {
                DrawAndEditKnot(pointsProp, i, isShiftHeld);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRaycastOffsets()
        {
            var raycastTargetProp = serializedObject.FindProperty("m_RaycastTarget");
            if (raycastTargetProp != null && !raycastTargetProp.boolValue) return;

            var startOffset = serializedObject.FindProperty("m_RaycastStartOffset").floatValue;
            var endOffset = serializedObject.FindProperty("m_RaycastEndOffset").floatValue;

            if (startOffset <= 0f && endOffset >= 1f) return;

            var spline = _target.Spline;
            if (spline == null || spline.Count < 2) return;

            Handles.color = _raycastOffsetColor;
            float gizmoSize = HandleUtility.GetHandleSize(_transform.position) * 0.5f;

            if (startOffset > 0)
            {
                float t = Mathf.Clamp01(startOffset);
                DrawOffsetGizmo(spline, t, gizmoSize);
            }

            if (endOffset < 1)
            {
                float t = Mathf.Clamp01(endOffset);
                DrawOffsetGizmo(spline, t, gizmoSize);
            }
        }

        private void DrawOffsetGizmo(Spline2D spline, float t, float size)
        {
            spline.Evaluate(t, out var localPos, out var localNormal);
            var worldPos = _transform.TransformPoint(new Vector3(localPos.x, localPos.y, 0));
            var worldNormal = _transform.TransformDirection(new Vector3(localNormal.x, localNormal.y, 0));

            Handles.DrawLine(worldPos - worldNormal * size, worldPos + worldNormal * size, LineThickness);
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
            if (posProp == null || rotProp == null || tanInProp == null || tanOutProp == null) return;

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
                Undo.RecordObject(_target, "Rotate Spline Point");
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
                    Undo.RecordObject(_target, "Edit Tangent In");
                    var newLocalPoint = _transform.InverseTransformPoint(newWorldTanIn);
                    var diff = (Vector2)newLocalPoint - localPos;
                    Vector2 rotatedDiff = Quaternion.Inverse(knotRot) * diff;
                    SetFloat2(tanInProp, rotatedDiff);
                    if (_tangentMode == TangentMode.Mirror)
                    {
                        // Mirror the out tangent
                        SetFloat2(tanOutProp, -rotatedDiff);
                    }
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
                    Undo.RecordObject(_target, "Edit Tangent Out");
                    var newLocalPoint = _transform.InverseTransformPoint(newWorldTanOut);
                    var diff = (Vector2)newLocalPoint - localPos;
                    Vector2 rotatedDiff = Quaternion.Inverse(knotRot) * diff;
                    SetFloat2(tanOutProp, rotatedDiff);
                    if (_tangentMode == TangentMode.Mirror)
                    {
                        // Mirror the in tangent
                        SetFloat2(tanInProp, -rotatedDiff);
                    }
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
