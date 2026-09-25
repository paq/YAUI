using System;
using UnityEditor;

namespace Yaui.Editor
{
    /// <summary>Shows the fill properties only for filled images, with the fill origin of the fill method.</summary>
    [CustomEditor(typeof(YauiImage), true)]
    [CanEditMultipleObjects]
    class YauiImageEditor : YauiElementEditor
    {
        protected override bool DrawProperty(SerializedProperty property)
        {
            switch (property.name)
            {
                case "fillMethod":
                case "fillAmount":
                case "fillClockwise":
                    if (!IsFilled())
                    {
                        return true;
                    }

                    if (property.name == "fillClockwise" && !IsRadial())
                    {
                        return true;
                    }

                    return false;
                case "fillOrigin":
                    if (IsFilled())
                    {
                        DrawFillOrigin(property);
                    }

                    return true;
                case "borderScale":
                    // Only sliced images use the sprite borders.
                    return !Type().hasMultipleDifferentValues && Type().intValue != (int)ImageType.Sliced;
                default:
                    return false;
            }
        }

        SerializedProperty Type() => serializedObject.FindProperty("type");

        SerializedProperty Method() => serializedObject.FindProperty("fillMethod");

        bool IsFilled() => Type().hasMultipleDifferentValues || Type().intValue == (int)ImageType.Filled;

        bool IsRadial() => Method().hasMultipleDifferentValues || Method().intValue >= (int)FillMethod.Radial90;

        void DrawFillOrigin(SerializedProperty property)
        {
            var method = Method();
            if (method.hasMultipleDifferentValues)
            {
                EditorGUILayout.PropertyField(property);
                return;
            }

            var names = (FillMethod)method.intValue switch
            {
                FillMethod.Horizontal => Enum.GetNames(typeof(FillOriginHorizontal)),
                FillMethod.Vertical => Enum.GetNames(typeof(FillOriginVertical)),
                FillMethod.Radial90 => Enum.GetNames(typeof(FillOrigin90)),
                FillMethod.Radial180 => Enum.GetNames(typeof(FillOrigin180)),
                _ => Enum.GetNames(typeof(FillOrigin360)),
            };

            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            var value = EditorGUILayout.Popup("Fill Origin", Math.Clamp(property.intValue, 0, names.Length - 1), names);
            if (EditorGUI.EndChangeCheck())
            {
                property.intValue = value;
            }

            EditorGUI.showMixedValue = false;
        }
    }
}
