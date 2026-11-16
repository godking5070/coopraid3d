#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UI;

namespace Astella.PrismV4.Editor
{
    /// <summary>ImageEditor otherwise hides fields introduced by derived Image components.</summary>
    [CustomEditor(typeof(PrismV4Gauge)), CanEditMultipleObjects]
    public sealed class PrismV4GaugeEditor : ImageEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            serializedObject.Update();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Prism V4 fixed-texture fill", EditorStyles.boldLabel);
            foreach (string field in new[] { "sourceFill", "previewValue", "innerBounds", "pointWidth", "direction" })
                EditorGUILayout.PropertyField(serializedObject.FindProperty(field));
            EditorGUILayout.HelpBox("Use a Simple image, matching full-sprite RGBA layers and preserveAspect off. Source Fill supplies only the ratio; its appearance is never changed.", MessageType.Info);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
