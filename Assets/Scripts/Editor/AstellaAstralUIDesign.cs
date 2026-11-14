#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RaidRogue.Editor
{
    /// <summary>Explicit, editable design copies for Astella's existing main-scene UI bindings.</summary>
    public static class AstellaAstralUIDesign
    {
        private const string ProfilePath = "Assets/Resources/UI/RaidUIPresentation.asset";
        private static readonly Color Ink = new Color32(13, 19, 34, 250);
        private static readonly Color Surface = new Color32(25, 36, 54, 255);
        private static readonly Color Gold = new Color32(220, 188, 126, 255);
        private static readonly Color Teal = new Color32(104, 215, 204, 255);
        private static readonly Color Paper = new Color32(231, 234, 239, 255);
        private static readonly Color Muted = new Color32(150, 166, 186, 255);
        private static readonly Color Edge = new Color32(66, 81, 104, 255);

        [MenuItem("Tools/RaidRogue/UI/Fix Active Astella UI")]
        public static void RepairActive()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play Mode and close Prefab Mode first.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            if (profile?.hudPrefab == null || profile.theme == null)
                throw new InvalidOperationException("The active UI profile is missing.");
            var paths = new[] { AssetDatabase.GetAssetPath(profile.hudPrefab) }
                .Concat(profile.views.Where(v => v.prefab != null).Select(v => AssetDatabase.GetAssetPath(v.prefab)))
                .Distinct().ToArray();
            if (paths.Any(p => !p.StartsWith("Assets/UI/AstellaAstral-", StringComparison.Ordinal)))
                throw new InvalidOperationException("Repair only edits Astella Astral copies. Apply Astella Astral Design first.");
            string backup = Path.Combine("Logs", "UIRepairBackups", "astella-embedded-menu-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(backup);
            foreach (string path in paths)
            {
                File.Copy(path, Path.Combine(backup, Path.GetFileName(path)));
                File.Copy(path + ".meta", Path.Combine(backup, Path.GetFileName(path) + ".meta"));
            }
            int embedded = 0;
            foreach (string path in paths)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    DecorateMenu(root);
                    foreach (var menu in root.GetComponentsInChildren<RaidUIView>(true).Where(v => v.viewId == "connection"))
                    {
                        if (!menu.GetComponentsInChildren<RectTransform>(true).Any(r => r.name == "Astella_StarChart"))
                            throw new InvalidOperationException("Missing decorated menu in " + path);
                        if (root.GetComponent<RaidUIRoot>() != null) embedded++;
                    }
                    if (PrefabUtility.SaveAsPrefabAsset(root, path) == null) throw new IOException(path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            if (embedded == 0) throw new InvalidOperationException("No embedded connection view found in the active HUD.");
            RaidUIAuthoring.Validate(profile);
            AssetDatabase.SaveAssets();
            Debug.Log("ASTELLA EMBEDDED MENU FIXED: " + embedded + " HUD menu(s). Backup: " + backup);
        }

        [MenuItem("Tools/RaidRogue/UI/Apply Astella Astral Design (Copy Assets)")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play Mode and close Prefab Mode first.");
            string project = Directory.GetParent(Application.dataPath).FullName;
            if (File.Exists(Path.Combine(project, ".git")))
                throw new InvalidOperationException("Apply from the original Astella project, not a worktree.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            if (profile == null || profile.hudPrefab == null)
                throw new InvalidOperationException("Astella's active UI presentation is missing.");
            RaidUIAuthoring.Validate(profile);

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string backup = Path.Combine(project, "Logs", "UIRepairBackups", "astella-astral-" + stamp);
            Directory.CreateDirectory(backup);
            File.Copy(ProfilePath, Path.Combine(backup, "RaidUIPresentation.asset"));
            File.Copy(ProfilePath + ".meta", Path.Combine(backup, "RaidUIPresentation.asset.meta"));
            string folder = "Assets/UI/AstellaAstral-" + stamp;
            AssetDatabase.CreateFolder("Assets/UI", "AstellaAstral-" + stamp);
            var copies = new Dictionary<string, string>();
            string Copy(Object asset)
            {
                if (asset == null) return null;
                string source = AssetDatabase.GetAssetPath(asset);
                if (copies.TryGetValue(source, out string existing)) return existing;
                string destination = folder + "/" + Path.GetFileName(source);
                if (copies.Values.Contains(destination))
                    throw new InvalidOperationException("Two source assets have the same name: " + destination);
                if (!AssetDatabase.CopyAsset(source, destination)) throw new IOException(source);
                copies.Add(source, destination);
                return destination;
            }
            string oldProfile = EditorJsonUtility.ToJson(profile);
            try
            {
                string hud = Copy(profile.hudPrefab);
                foreach (var view in profile.views) Copy(view.prefab);
                foreach (var item in new[] { profile.skillTemplate, profile.hotbarTemplate, profile.partyTemplate,
                    profile.resourceTemplate, profile.statusTemplate, profile.formButtonTemplate, profile.formTextTemplate }) Copy(item);
                var theme = profile.theme != null ? Object.Instantiate(profile.theme) : ScriptableObject.CreateInstance<RaidUITheme>();
                theme.name = "Astella Astral Theme";
                theme.panel = Ink; theme.text = Paper; theme.accent = Gold;
                theme.resource = Teal; theme.health = new Color32(111, 199, 165, 255);
                theme.ready = Paper; theme.selected = Teal; theme.unavailable = Muted; theme.locked = Edge;
                AssetDatabase.CreateAsset(theme, folder + "/AstellaAstralTheme.asset");

                foreach (string path in copies.Values)
                {
                    var root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        foreach (var child in root.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject).ToArray())
                            if (child != root && PrefabUtility.IsAnyPrefabInstanceRoot(child))
                                PrefabUtility.UnpackPrefabInstance(child, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                        Style(root, theme);
                        DecorateMenu(root);
                        if (root.GetComponentsInChildren<Transform>(true).Any(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0))
                            throw new InvalidOperationException("Missing scripts in " + path);
                        if (PrefabUtility.SaveAsPrefabAsset(root, path) == null) throw new IOException(path);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
                profile.hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(hud).GetComponent<RaidUIRoot>();
                foreach (var view in profile.views)
                    if (view.prefab != null) view.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(copies[AssetDatabase.GetAssetPath(view.prefab)]).GetComponent<RaidUIView>();
                RaidUIRepeatedItem Remap(RaidUIRepeatedItem item) => item == null ? null :
                    AssetDatabase.LoadAssetAtPath<GameObject>(copies[AssetDatabase.GetAssetPath(item)]).GetComponent<RaidUIRepeatedItem>();
                profile.skillTemplate = Remap(profile.skillTemplate); profile.hotbarTemplate = Remap(profile.hotbarTemplate);
                profile.partyTemplate = Remap(profile.partyTemplate); profile.resourceTemplate = Remap(profile.resourceTemplate);
                profile.statusTemplate = Remap(profile.statusTemplate); profile.formButtonTemplate = Remap(profile.formButtonTemplate);
                profile.formTextTemplate = Remap(profile.formTextTemplate); profile.theme = theme;
                RaidUIAuthoring.Validate(profile);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                File.WriteAllText(Path.Combine(backup, "design.txt"), "Astella Astral UI\nAssets: " + folder +
                    "\nMain scene: Assets/Scenes/SampleScene.unity\nPrevious prefab assets preserved.\n");
                Debug.Log("ASTELLA ASTRAL UI APPLIED: " + folder + " | Profile backup: " + backup);
            }
            catch
            {
                EditorJsonUtility.FromJsonOverwrite(oldProfile, profile);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                throw;
            }
        }

        private static void Style(GameObject root, RaidUITheme theme)
        {
            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                var style = graphic.GetComponent<RaidUIStyle>() ?? graphic.gameObject.AddComponent<RaidUIStyle>();
                style.graphic = graphic; style.useTheme = false; style.themeOverride = theme;
                style.ready = Paper; style.selected = Teal; style.unavailable = Muted; style.locked = Edge;
                if (graphic is Text text)
                {
                    text.color = Paper;
                    text.raycastTarget = false;
                    // Keep authored size, font, wrapping, and layout; long job descriptions still fit their own panels.
                    continue;
                }
                if (!(graphic is Image image)) continue;
                string name = image.name.ToLowerInvariant();
                // Skill/portrait/minimap artwork and role markers retain their semantic colors and sprites.
                if (image.GetComponent<RaidUIIcon>() != null || name.Contains("icon") || name.Contains("marker") ||
                    name.Contains("aggro") || name.Contains("role") || name == "boss" || name == "player" || name == "arena (north up)") continue;
                if (name == "raid_mainmenu") image.color = new Color32(9, 15, 29, 255);
                else if (name.Contains("cooldown")) image.color = new Color32(6, 12, 23, 207);
                else if (image.GetComponent<Button>() != null) image.color = Surface;
                else if (name.Contains("overlay") || name.Contains("modalroot")) image.color = new Color32(5, 9, 18, 244);
                else if (name.Contains("shield") || name.Contains("accent") || name.Contains("cast") && name.Contains("fill")) image.color = Gold;
                else if (style.role == RaidUIStyle.ThemeRole.Resource) image.color = Teal;
                else if (style.role == RaidUIStyle.ThemeRole.Health) image.color = theme.health;
                else if (name.Contains("border") || name.Contains("highlight") || name.Contains("divider")) image.color = Edge;
                else if (!name.Contains("fill") && image.color.a > .15f) image.color = Ink;
            }
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
            {
                var colors = button.colors;
                colors.normalColor = Color.white; colors.highlightedColor = new Color(1.2f, 1.25f, 1.3f);
                colors.pressedColor = new Color(.68f, .85f, .87f); colors.selectedColor = new Color(1.15f, 1.22f, 1.25f);
                colors.disabledColor = new Color(.48f, .52f, .58f, .8f); colors.fadeDuration = .12f;
                button.colors = colors;
            }
        }

        private static void DecorateMenu(GameObject root)
        {
            // Attach() chooses an existing descendant view before the standalone profile prefab.
            // The HUD copy contains its own connection view, so both must receive the design.
            foreach (var view in root.GetComponentsInChildren<RaidUIView>(true))
                if (view.viewId == "connection") DecorateConnection(view);
        }

        private static void DecorateConnection(RaidUIView view)
        {
            T Reference<T>(string key) where T : Object => view.references.FirstOrDefault(r => r.path == key)?.target as T;
            var title = Reference<Text>("_menuTitle");
            if (title != null) { title.color = Gold; title.fontSize = 46; }
            var subtitle = Reference<Text>("_menuSubtitle");
            if (subtitle != null) subtitle.color = Muted;
            LayoutHome(view);
            foreach (Transform t in view.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Accent")
                {
                    if (t.TryGetComponent<Image>(out var accent)) { accent.color = Gold; accent.raycastTarget = false; }
                }
                if (t.name != "Home" || t.Find("Astella_StarChart") != null) continue;
                var chart = new GameObject("Astella_StarChart", typeof(RectTransform));
                chart.transform.SetParent(t, false); chart.transform.SetAsFirstSibling();
                var rect = (RectTransform)chart.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = new Vector2(-300, -28); rect.sizeDelta = new Vector2(420, 420);
                var faintGold = new Color(Gold.r, Gold.g, Gold.b, .38f);
                for (int ring = 0; ring < 2; ring++)
                {
                    float radius = 168 + ring * 18;
                    for (int i = 0; i < 64; i++)
                    {
                        float a = i * Mathf.PI * 2 / 64, b = (i + 1) * Mathf.PI * 2 / 64;
                        Stroke(rect, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius,
                            new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, faintGold, 1);
                    }
                }
                for (int i = 0; i < 6; i++)
                {
                    float angle = (30 + i * 60) * Mathf.Deg2Rad;
                    Vector2 point = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 186;
                    Stroke(rect, point - Vector2.right * 5, point + Vector2.up * 7, Gold, 1);
                    Stroke(rect, point + Vector2.up * 7, point + Vector2.right * 5, Gold, 1);
                    Stroke(rect, point + Vector2.right * 5, point - Vector2.up * 7, Gold, 1);
                    Stroke(rect, point - Vector2.up * 7, point - Vector2.right * 5, Gold, 1);
                }
                for (int i = 0; i < 6; i++)
                {
                    float a = (30 + i * 60) * Mathf.Deg2Rad;
                    float b = (150 + i * 60) * Mathf.Deg2Rad;
                    Stroke(rect, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 168,
                        new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * 168, faintGold, 1);
                }
                var font = title != null ? title.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                Caption(rect, "ASTELLA", new Vector2(0, 8), new Vector2(300, 55), 34, Gold, font);
                Caption(rect, "네 명의 동료 · 여섯 전장", new Vector2(0, -38), new Vector2(300, 28), 16, Paper, font);
            }
        }

        private static void LayoutHome(RaidUIView view)
        {
            var home = view.references.FirstOrDefault(r => r.path == "_homeMenu")?.target as GameObject;
            if (home == null) throw new InvalidOperationException("Connection view has no Home binding.");
            foreach (var old in home.GetComponentsInChildren<RectTransform>(true).Where(r => r.name == "Astella_StarChart").ToArray())
                Object.DestroyImmediate(old.gameObject);
            for (int i = 0; i < 2; i++)
            {
                string id = i == 0 ? "방 만들기" : "들어가기";
                var button = view.actions.FirstOrDefault(a => a.id == id)?.button;
                if (button == null) throw new InvalidOperationException("Missing home action: " + id);
                Place(button.GetComponent<RectTransform>(), new Vector2(270, i == 0 ? 55 : -175), new Vector2(460, 210));
                foreach (var text in button.GetComponentsInChildren<Text>(true))
                {
                    text.alignment = TextAnchor.MiddleLeft;
                    if (text.text == id) { Place(text.rectTransform, new Vector2(0, 38), new Vector2(408, 40)); text.fontSize = 29; }
                    else if (text.text == "01" || text.text == "02")
                    { Place(text.rectTransform, new Vector2(0, 80), new Vector2(408, 22)); text.text = i == 0 ? "01 / EXPEDITION" : "02 / ALLIANCE"; text.fontSize = 13; text.color = Gold; }
                    else if (text.text.StartsWith("01 /", StringComparison.Ordinal) || text.text.StartsWith("02 /", StringComparison.Ordinal))
                    { Place(text.rectTransform, new Vector2(0, 80), new Vector2(408, 22)); }
                    else if (text.text.Contains("새 원정 시작") || text.text.Contains("파티 참가"))
                    { Place(text.rectTransform, new Vector2(0, -80), new Vector2(408, 28)); text.fontSize = 17; text.color = Teal; }
                    else { Place(text.rectTransform, new Vector2(0, -16), new Vector2(408, 56)); text.fontSize = 17; text.color = Muted; }
                }
                foreach (var image in button.GetComponentsInChildren<Image>(true))
                {
                    if (image.name == "Accent") Place(image.rectTransform, new Vector2(0, 103), new Vector2(460, 3));
                    if (image.name == "Divider") Place(image.rectTransform, new Vector2(0, -56), new Vector2(408, 1));
                }
            }
            var resume = view.references.FirstOrDefault(r => r.path == "_homeContinue")?.target as Button;
            if (resume != null) Place(resume.GetComponent<RectTransform>(), new Vector2(-300, -300), new Vector2(420, 48));
            var save = view.references.FirstOrDefault(r => r.path == "_homeSave")?.target as Text;
            if (save != null) Place(save.rectTransform, new Vector2(270, -307), new Vector2(460, 48));
            foreach (string id in new[] { "저장 관리", "설정", "게임 종료" })
            {
                var action = view.actions.FirstOrDefault(a => a.id == id)?.button;
                if (action == null || !action.transform.IsChildOf(home.transform)) continue;
                var rect = action.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -370);
            }
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position; rect.sizeDelta = size;
        }

        private static void Caption(Transform parent, string value, Vector2 position, Vector2 size, int fontSize, Color color, Font font)
        {
            var go = new GameObject("StarChart_Caption", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>(); text.text = value; text.font = font; text.fontSize = fontSize;
            text.color = color; text.raycastTarget = false; text.alignment = TextAnchor.MiddleCenter;
            Place(text.rectTransform, position, size);
        }

        private static void Stroke(Transform parent, Vector2 a, Vector2 b, Color color, float width)
        {
            var go = new GameObject("StarChart_Line", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = (a + b) * .5f; rect.sizeDelta = new Vector2((b - a).magnitude, width);
            rect.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
            var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
        }
    }
}
#endif
