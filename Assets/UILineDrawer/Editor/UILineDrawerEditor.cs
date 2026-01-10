using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Maro.UILineDrawer
{
    [CustomEditor(typeof(UILineDrawer))]
    public class UILineDrawerEditor : Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            var pointsProp = serializedObject.FindProperty("m_Points");
            var pointsField = new PropertyField(pointsProp);
            root.Add(pointsField);

            root.Add(new PropertyField(serializedObject.FindProperty("m_Color")));
            root.Add(new PropertyField(serializedObject.FindProperty("m_Thickness")));
            root.Add(new PropertyField(serializedObject.FindProperty("m_Subdivisions")));

            var raycastTargetProp = serializedObject.FindProperty("m_RaycastTarget");
            var raycastTargetField = new PropertyField(raycastTargetProp);
            root.Add(raycastTargetField);

            var raycastOptionsContainer = new VisualElement();
            raycastOptionsContainer.style.paddingLeft = 15;

            raycastOptionsContainer.Add(new PropertyField(serializedObject.FindProperty("m_RaycastExtraThickness")));
            raycastOptionsContainer.Add(new PropertyField(serializedObject.FindProperty("m_RaycastStartOffset")));
            raycastOptionsContainer.Add(new PropertyField(serializedObject.FindProperty("m_RaycastEndOffset")));

            root.Add(raycastOptionsContainer);

            root.Add(new PropertyField(serializedObject.FindProperty("m_Maskable")));

            void ToggleRaycastOptions(bool isEnabled)
            {
                raycastOptionsContainer.style.display = isEnabled ? DisplayStyle.Flex : DisplayStyle.None;
            }

            ToggleRaycastOptions(raycastTargetProp.boolValue);

            // This is necessary because otherwise OnValidate is not called when the point fields are changed to default values.
            root.TrackPropertyValue(
                pointsProp,
                _ =>
                {
                    serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(target);
                }
            );

            root.TrackPropertyValue(raycastTargetProp, (prop) => { ToggleRaycastOptions(prop.boolValue); });

            return root;
        }
    }
}