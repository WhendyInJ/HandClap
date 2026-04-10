using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(Reel.SlotChoice))]
public class ReelSlotChoiceDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        Rect foldoutRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);

        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;

            SerializedProperty categoryProperty = property.FindPropertyRelative("category");
            SerializedProperty elementProperty = property.FindPropertyRelative("elementType");
            SerializedProperty bodyProperty = property.FindPropertyRelative("bodyType");
            SerializedProperty handProperty = property.FindPropertyRelative("handType");

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float lineSpacing = EditorGUIUtility.standardVerticalSpacing;
            float y = position.y + lineHeight + lineSpacing;

            Rect categoryRect = new Rect(position.x, y, position.width, lineHeight);
            EditorGUI.PropertyField(categoryRect, categoryProperty);

            y += lineHeight + lineSpacing;

            Rect valueRect = new Rect(position.x, y, position.width, lineHeight);
            switch ((Reel.SlotCategory)categoryProperty.enumValueIndex)
            {
                case Reel.SlotCategory.Body:
                    EditorGUI.PropertyField(valueRect, bodyProperty, new GUIContent("Value"));
                    break;
                case Reel.SlotCategory.Hand:
                    EditorGUI.PropertyField(valueRect, handProperty, new GUIContent("Value"));
                    break;
                default:
                    EditorGUI.PropertyField(valueRect, elementProperty, new GUIContent("Value"));
                    break;
            }

            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float lineHeight = EditorGUIUtility.singleLineHeight;

        if (!property.isExpanded)
            return lineHeight;

        return (lineHeight * 3f) + (EditorGUIUtility.standardVerticalSpacing * 2f);
    }
}
