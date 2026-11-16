#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue.Editor
{
    /// <summary>Author crisp dark outlines on HUD labels once; preserve fonts, colors and placement.</summary>
    public static class AstellaHUDTextOutlineInstaller
    {
        private const string Stamp = "Logs/HUD-text-outline-v1.applied.txt";
        // Importing scripts must not replace an artist's label styles.
        [MenuItem("Tools/RaidRogue/UI/Apply HUD Text Outlines")]
        public static void Apply()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/SampleScene.unity" || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Use Astella SampleScene in Edit Mode.");
            var live = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<AstellaOrnateHUD>(true))
                .Where(h => h.gameObject.activeInHierarchy).ToArray();
            if (live.Length != 1) throw new InvalidOperationException("Expected one active HUD.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>("Assets/Resources/UI/RaidUIPresentation.asset");
            if (profile == null || profile.hudPrefab == null) throw new InvalidOperationException("HUD profile missing.");
            string path = AssetDatabase.GetAssetPath(profile.hudPrefab);
            string backup = "Logs/UIRepairBackups/hud-text-outline-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(backup);
            foreach (string file in new[] { path, path + ".meta", scene.path, scene.path + ".meta" })
                File.Copy(file, backup + "/" + Path.GetFileName(file));
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Build(contents.GetComponentInChildren<AstellaOrnateHUD>(true));
                RaidUIAuthoring.ValidateScripts(contents);
                if (PrefabUtility.SaveAsPrefabAsset(contents, path) == null) throw new IOException(path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            AssetDatabase.SaveAssets();
            Build(live[0]);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save SampleScene.");
            File.WriteAllText(Stamp, DateTime.Now.ToString("s") + "\n" + path + "\nBackup: " + backup +
                "\nDark HUD text outlines; existing fonts, colors, labels, input and layout retained.");
            Debug.Log("HUD TEXT OUTLINES APPLIED. " + backup);
        }

        private static void Build(AstellaOrnateHUD hud)
        {
            if (hud == null) throw new InvalidOperationException("HUD missing.");
            foreach (var text in hud.GetComponentsInChildren<Text>(true))
            {
                var canvas = text.GetComponentInParent<Canvas>();
                // Width is measured in root-canvas units, including the scaled class instruments.
                // This keeps small key labels crisp instead of giving them thick, blurred borders.
                float referenceScale = canvas != null ? Mathf.Abs(canvas.rootCanvas.transform.lossyScale.x) : 1;
                float scale = Mathf.Abs(text.transform.lossyScale.x) / Mathf.Max(.0001f, referenceScale);
                float width = Mathf.Clamp(.85f / Mathf.Max(.1f, scale), .65f, 3);
                var outlines = text.GetComponents<Outline>();
                var outline = outlines.Length > 0 ? outlines[0] : text.gameObject.AddComponent<Outline>();
                outline.enabled = true;
                outline.effectColor = new Color32(12, 10, 17, 235);
                outline.effectDistance = new Vector2(width, -width);
                outline.useGraphicAlpha = true;
                foreach (var duplicate in outlines.Skip(1)) duplicate.enabled = false;
                foreach (var shadow in text.GetComponents<Shadow>())
                    if (!(shadow is Outline)) shadow.enabled = false;
                EditorUtility.SetDirty(outline);
            }
        }
    }
}
#endif
