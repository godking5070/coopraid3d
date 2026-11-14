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
    /// <summary>Author the requested key order and mirrored small consumable/movement slots once.</summary>
    public static class AstellaClassHubLayoutRefinement
    {
        private const string Stamp = "Logs/ClassHubs-layout-v2.applied.txt";
        [InitializeOnLoadMethod] private static void Schedule()
        {
            if (!File.Exists(Stamp)) EditorApplication.update += WhenIdle;
        }
        private static void WhenIdle()
        {
            if (File.Exists(Stamp)) { EditorApplication.update -= WhenIdle; return; }
            if (!string.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/', '\\'), @"C:\Astella\Astella\Assets", StringComparison.OrdinalIgnoreCase))
            { EditorApplication.update -= WhenIdle; return; }
            if (!File.Exists("Logs/ClassHubs-v6-v8.applied.txt") || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null ||
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/SampleScene.unity") return;
            EditorApplication.update -= WhenIdle;
            try { Apply(); }
            catch (Exception e) { Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/ClassHubs-layout-v2.error.txt", e.ToString()); Debug.LogException(e); }
        }

        [MenuItem("Tools/RaidRogue/UI/Apply Class Hub Key Layout")]
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
            string backup = "Logs/UIRepairBackups/class-hub-layout-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
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
                "\nUltimate: percent above, V below. Right: Q / reserved E / R / F, small Space outside right, mirrored small potion outside left. Actual actions preserved.");
            Debug.Log("CLASS HUB KEY LAYOUT APPLIED. " + backup);
        }

        private static void Build(AstellaOrnateHUD hud)
        {
            var ultimate = hud.playerRoot.GetComponent<AstellaClassUltimateHUD>();
            if (ultimate == null || ultimate.views.Length != 12) throw new InvalidOperationException("Class hubs must be installed first.");
            foreach (var view in ultimate.views)
            {
                // Reuse the ornament's lower value plaque for the binding; charge is now above the instrument.
                Top(view.percentText.rectTransform, new Vector2(120, 0), new Vector2(120, 38));
                Top(view.keyText.rectTransform, new Vector2(152, 215), new Vector2(56, 34));
                view.keyText.fontSize = 25;
                foreach (var state in new[] { view.charging, view.ready, view.unavailable })
                {
                    var badge = state.root.transform.Find("State key badge");
                    if (badge != null) badge.gameObject.SetActive(false);
                    var symbol = state.root.transform.Find("State symbol") as RectTransform;
                    if (symbol != null) Top(symbol, new Vector2(244, 8), new Vector2(24, 24));
                }
            }
            var right = (RectTransform)hud.playerRoot.transform.Find("Right - Q F R Space");
            if (right == null) throw new InvalidOperationException("Right action root missing.");
            Center((RectTransform)Slot(hud, CharacterSlot.Job1).button.transform, new Vector2(-216, 0), new Vector2(128, 128));
            Center((RectTransform)Slot(hud, CharacterSlot.Identity).button.transform, new Vector2(72, 0), new Vector2(128, 128));
            Center((RectTransform)Slot(hud, CharacterSlot.OffGcd2).button.transform, new Vector2(216, 0), new Vector2(128, 128));
            // Keep the original button, binding and parent contract; scale the complete slot so its descendants stay proportional.
            var movement = (RectTransform)Slot(hud, CharacterSlot.Movement).button.transform;
            Center(movement, new Vector2(344, 0), new Vector2(128, 128)); movement.localScale = Vector3.one * .65625f;
            var potion = (RectTransform)Slot(hud, CharacterSlot.Potion).button.transform;
            potion.anchorMin = potion.anchorMax = new Vector2(.5f, 0); potion.pivot = new Vector2(.5f, .5f);
            potion.anchoredPosition = new Vector2(-394, 68);
            float width = potion.sizeDelta.x;
            if (width <= 0) throw new InvalidOperationException("Potion native width missing.");
            potion.localScale = Vector3.one * (42 / width);
            ReservedE(right, Slot(hud, CharacterSlot.Job1));
            if (!AstellaSplitCombatLayout.IsApplied(hud)) throw new InvalidOperationException("Action layout contract changed.");
        }

        private static void ReservedE(RectTransform parent, AstellaOrnateHUD.SlotBinding source)
        {
            const string name = "Reserved E - no skill assigned";
            var existing = parent.Find(name);
            if (existing != null) { Center((RectTransform)existing, new Vector2(-72, 0), new Vector2(128, 128)); return; }
            var root = new GameObject(name, typeof(RectTransform)); root.transform.SetParent(parent, false);
            Center((RectTransform)root.transform, new Vector2(-72, 0), new Vector2(128, 128));
            var skin = source.button.GetComponent<AstellaSkillSlotSkin>();
            if (skin == null) skin = source.button.GetComponentInChildren<AstellaSkillSlotSkin>(true);
            if (skin == null || skin.normalFrame == null) throw new InvalidOperationException("Existing skill frame missing.");
            var frame = new GameObject("Empty skill frame", typeof(RectTransform), typeof(Image)); frame.transform.SetParent(root.transform, false);
            Stretch((RectTransform)frame.transform); var image = frame.GetComponent<Image>(); image.sprite = skin.normalFrame; image.raycastTarget = false;
            // This is intentionally visual-only: no invented ability, cooldown, click handler or input mapping.
            var key = UnityEngine.Object.Instantiate(source.keycap, root.transform, false);
            key.name = "Reserved key E"; key.text = "E"; key.raycastTarget = false;
            var badge = new GameObject("Key badge", typeof(RectTransform), typeof(Image)); badge.transform.SetParent(root.transform, false);
            var badgeImage = badge.GetComponent<Image>(); badgeImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/AstellaClassHubs/SkillFrames/key-badge.png"); badgeImage.raycastTarget = false;
            Top((RectTransform)badge.transform, new Vector2(0, 0), new Vector2(42, 28));
            Top(key.rectTransform, new Vector2(0, 0), new Vector2(42, 28)); key.transform.SetAsLastSibling();
        }
        private static AstellaOrnateHUD.SlotBinding Slot(AstellaOrnateHUD hud, CharacterSlot slot) => hud.slots.First(s => !s.sharedResurrection && s.slot == slot);
        private static void Center(RectTransform r, Vector2 p, Vector2 size)
        { r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f); r.anchoredPosition = p; r.sizeDelta = size; r.localScale = Vector3.one; }
        private static void Top(RectTransform r, Vector2 p, Vector2 size)
        { r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); r.anchoredPosition = new Vector2(p.x, -p.y); r.sizeDelta = size; r.localScale = Vector3.one; }
        private static void Stretch(RectTransform r)
        { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
    }
}
#endif
