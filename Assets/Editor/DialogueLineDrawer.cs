using UnityEditor;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CustomPropertyDrawer(typeof(DialogueLine))]
    public class DialogueLineDrawer : PropertyDrawer
    {
        private const int PreviewLength = 60;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var roleProp    = property.FindPropertyRelative("role");
            var speakerProp = property.FindPropertyRelative("speaker");
            var textProp    = property.FindPropertyRelative("text");

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing    = EditorGUIUtility.standardVerticalSpacing;

            var foldoutRect = new Rect(position.x, position.y, position.width, lineHeight);

            property.isExpanded = EditorGUI.Foldout(
                foldoutRect,
                property.isExpanded,
                BuildSummary(roleProp, speakerProp, textProp),
                true);

            if (!property.isExpanded) return;

            EditorGUI.indentLevel++;

            float y = position.y + lineHeight + spacing;

            var roleRect = new Rect(position.x, y, position.width, lineHeight);
            EditorGUI.PropertyField(roleRect, roleProp);
            y += lineHeight + spacing;

            if (roleProp.enumValueIndex == (int)DialogueSpeakerRole.Explicit)
            {
                var speakerRect = new Rect(position.x, y, position.width, lineHeight);
                EditorGUI.PropertyField(speakerRect, speakerProp);
                y += lineHeight + spacing;
            }

            var textRect = new Rect(position.x, y, position.width, lineHeight * 3f);
            EditorGUI.PropertyField(textRect, textProp);

            EditorGUI.indentLevel--;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing    = EditorGUIUtility.standardVerticalSpacing;

            if (!property.isExpanded) return lineHeight;

            var roleProp = property.FindPropertyRelative("role");
            bool showSpeaker = roleProp.enumValueIndex == (int)DialogueSpeakerRole.Explicit;

            float rows = showSpeaker ? 6f : 5f;

            return (lineHeight * rows) + (spacing * (showSpeaker ? 3f : 2f));
        }

        private static string BuildSummary(SerializedProperty roleProp,
                                           SerializedProperty speakerProp,
                                           SerializedProperty textProp)
        {
            string speakerName;

            if (roleProp.enumValueIndex == (int)DialogueSpeakerRole.PartyLeader)
            {
                speakerName = "Party Leader";
            }
            else
            {
                var speaker = speakerProp.objectReferenceValue as DialogueSpeakerData;

                speakerName = speaker != null && !string.IsNullOrEmpty(speaker.DisplayName)
                    ? speaker.DisplayName
                    : "No Speaker";
            }

            string preview = textProp.stringValue;

            if (string.IsNullOrWhiteSpace(preview))
                preview = "<empty>";
            else
                preview = preview.Replace("\n", " ").Trim();

            if (preview.Length > PreviewLength)
                preview = preview.Substring(0, PreviewLength) + "…";

            return $"{speakerName}: {preview}";
        }
    }
}