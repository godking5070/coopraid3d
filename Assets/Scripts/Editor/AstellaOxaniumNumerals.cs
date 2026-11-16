#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RaidRogue.Editor
{
    // Explicit authoring only. Numerical data, action listeners and outline variants have separate owners.
    public static class AstellaOxaniumNumerals
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string ProfilePath = "Assets/Resources/UI/RaidUIPresentation.asset";
        private const string FontPath = "Assets/UI/AstellaPrismV4/Typography/Fonts/Oxanium-Medium.ttf";
        private const string Evidence = "Logs/OxaniumNumerals";
        private const string NumericCharacters = "0123456789.,/%+-s:—";

        [Serializable] private sealed class Target
        {
            public string role, path, text, previousFont, font, previousStyle, outlineBefore, outlineAfter, widthSample;
            public int size;
            public float availableWidth, preferredWidth;
        }
        [Serializable] private sealed class SourceAsset
        { public string source, copy, sourceHashBefore, sourceHashAfter; }
        [Serializable] private sealed class Report
        {
            public string utc, folder, source, copy, sourceHashBefore, sourceHashAfter, backup, font, error;
            public bool passed, sceneSaved, outlinesUnchanged, geometryUnchanged;
            public int preservedOtherTexts;
            public Target[] targets;
            public SourceAsset[] assets;
            public string boundary = "Numeric font only. MaruBuri headings, body, keys, all rectangles and existing Outline/Shadow settings are preserved. Thicker outlines remain review variants; Play rendering needs separate verification.";
        }

        [MenuItem("Tools/Astella/Apply Oxanium Numerals (Preserved Copy)")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play and close Prefab Mode before authoring numeric typography.");
            if (!string.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/', '\\'), @"C:\Astella\Astella\Assets", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Run only in the original Astella Unity project.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isDirty) throw new InvalidOperationException("Save existing scene edits before numeric typography authoring.");
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null) throw new InvalidOperationException("Import the approved static Oxanium Medium font first: " + FontPath);
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            if (profile?.hudPrefab == null) throw new InvalidOperationException("An active authored HUD is required.");
            var sourceTargets = Numerals(profile.hudPrefab.gameObject);
            if (sourceTargets.Count == 0) throw new InvalidOperationException("The active HUD has no explicit Prism numeric bindings.");
            if (sourceTargets.Keys.All(text => IsApplied(text, font)))
            { ValidateActive(); Debug.Log("OXANIUM NUMERALS ALREADY APPLIED: authored outlines preserved."); return; }

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            var report = new Report { utc = DateTime.UtcNow.ToString("O"), font = FontPath,
                folder = "Assets/UI/AstellaPrismV4/Typography/Oxanium-" + stamp,
                source = AssetDatabase.GetAssetPath(profile.hudPrefab), backup = Evidence + "/Backup-" + stamp };
            report.copy = report.folder + "/RaidHUD.prefab";
            report.sourceHashBefore = Hash(report.source);
            Directory.CreateDirectory(report.backup);
            File.Copy(ProfilePath, report.backup + "/RaidUIPresentation.asset");
            File.Copy(ScenePath, report.backup + "/SampleScene.unity");
            string savedProfile = EditorJsonUtility.ToJson(profile);
            var disabled = new List<GameObject>();
            GameObject instance = null;
            try
            {
                Folder(report.folder);
                if (!AssetDatabase.CopyAsset(report.source, report.copy)) throw new IOException("Could not preserve numeric typography copy.");
                var contents = PrefabUtility.LoadPrefabContents(report.copy);
                try
                {
                    var targets = Numerals(contents);
                    if (targets.Count != sourceTargets.Count) throw new InvalidOperationException("Numeric binding count changed during copying.");
                    var otherTexts = contents.GetComponentsInChildren<Text>(true).Except(targets.Keys).ToArray();
                    string otherBefore = OtherTexts(otherTexts, contents.transform);
                    string numericBefore = NumericPresentation(targets.Keys, contents.transform);
                    string geometryBefore = Geometry(contents);
                    string outlinesBefore = Effects(contents);
                    report.targets = targets.Select(pair => ApplyText(pair.Key, pair.Value, font, contents.transform)).ToArray();
                    report.geometryUnchanged = Geometry(contents) == geometryBefore;
                    report.outlinesUnchanged = Effects(contents) == outlinesBefore;
                    if (!report.geometryUnchanged || !report.outlinesUnchanged || OtherTexts(otherTexts, contents.transform) != otherBefore ||
                        NumericPresentation(targets.Keys, contents.transform) != numericBefore)
                        throw new InvalidOperationException("Numerical font authoring must preserve other text, numerical presentation, rectangles and existing effects.");
                    report.preservedOtherTexts = otherTexts.Length;
                    ValidateRoot(contents, font);
                    RaidUIAuthoring.ValidateScripts(contents);
                    if (PrefabUtility.SaveAsPrefabAsset(contents, report.copy) == null) throw new IOException("Could not save numeric typography copy.");
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                report.sourceHashAfter = Hash(report.source);
                if (report.sourceHashAfter != report.sourceHashBefore) throw new IOException("Previous authored HUD changed.");
                profile.hudPrefab = AssetDatabase.LoadAssetAtPath<RaidUIRoot>(report.copy);
                RaidUIAuthoring.Validate(profile);
                EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
                if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                foreach (var root in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<RaidUIRoot>(true)))
                    if (root.gameObject.activeInHierarchy)
                    {
                        disabled.Add(root.gameObject); root.gameObject.SetActive(false);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(root.gameObject);
                    }
                // Scene loading can unload unused prefab handles; resolve after opening it.
                var prefab = AssetDatabase.LoadAssetAtPath<RaidUIRoot>(report.copy);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, scene);
                instance.name = "Astella_PrismV4_MaruBuri_Oxanium"; instance.SetActive(true);
                ValidateActive();
                AstellaMaruBuriTypography.ValidateActive();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the main scene.");
                report.sceneSaved = true; report.passed = true;
                Debug.Log("OXANIUM NUMERALS PASS: " + report.targets.Length + " numeric fields; MaruBuri headings, other text, layout and existing outlines preserved. " + report.folder);
            }
            catch (Exception error)
            {
                report.error = error.ToString();
                EditorJsonUtility.FromJsonOverwrite(savedProfile, profile);
                EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
                if (!report.sceneSaved)
                {
                    if (instance != null) Object.DestroyImmediate(instance);
                    foreach (var root in disabled) if (root != null) { root.SetActive(true); PrefabUtility.RecordPrefabInstancePropertyModifications(root); }
                }
                throw;
            }
            finally
            {
                report.assets = new[] { new SourceAsset { source = report.source, copy = report.copy,
                    sourceHashBefore = report.sourceHashBefore, sourceHashAfter = report.sourceHashAfter } };
                Directory.CreateDirectory(Evidence);
                File.WriteAllText(Evidence + "/report.json", JsonUtility.ToJson(report, true));
            }
        }

        [MenuItem("Tools/Astella/Validate Oxanium Numerals")]
        public static void ValidateActive()
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            if (font == null || profile?.hudPrefab == null) throw new InvalidOperationException("Approved numeric typography assets missing.");
            RaidUIAuthoring.Validate(profile);
            ValidateRoot(profile.hudPrefab.gameObject, font);
            foreach (var root in Object.FindObjectsByType<RaidUIRoot>(FindObjectsSortMode.None)) ValidateRoot(root.gameObject, font);
        }

        private static Dictionary<Text, string> Numerals(GameObject root)
        {
            var result = new Dictionary<Text, string>();
            void Add(Text text, string role)
            {
                if (text != null && text.transform.IsChildOf(root.transform) && !result.ContainsKey(text)) result.Add(text, role);
            }
            foreach (var bridge in root.GetComponentsInChildren<AstellaPrismHUDBridge>(true))
            {
                Add(bridge.playerView?.valueText, "player health");
                Add(bridge.playerView?.resourceText, "primary resource");
                Add(bridge.bossView?.valueText, "boss health percent");
                Add(bridge.bossView?.castTimeText, "boss cast seconds");
                Add(bridge.centerView?.valueText, "ultimate percent");
                Add(bridge.centerView?.cooldownText, "ultimate cooldown");
                foreach (var slot in bridge.slots ?? Array.Empty<AstellaPrismHUDBridge.SlotMirror>())
                    Add(slot?.view?.cooldownText, "skill cooldown " + slot?.sourceIndex);
                foreach (var party in bridge.partyRows ?? Array.Empty<AstellaPrismHUDBridge.PartyMirror>())
                    Add(party?.view?.valueText, "party health percent " + party?.sourceIndex);
            }
            return result.OrderBy(pair => AnimationUtility.CalculateTransformPath(pair.Key.transform, root.transform), StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }
        private static Target ApplyText(Text text, string role, Font font, Transform root)
        {
            var target = new Target { role = role, path = AnimationUtility.CalculateTransformPath(text.transform, root), text = text.text,
                previousFont = AssetDatabase.GetAssetPath(text.font), font = FontPath, previousStyle = text.fontStyle.ToString(),
                size = text.fontSize, outlineBefore = Effects(text.gameObject) };
            ValidateGlyphs(text, font);
            CheckWidth(text, font, role, out target.widthSample, out target.availableWidth, out target.preferredWidth);
            text.font = font; text.fontStyle = FontStyle.Normal;
            foreach (var style in text.GetComponents<RaidUIStyle>()) style.useTheme = false;
            target.outlineAfter = Effects(text.gameObject);
            return target;
        }
        private static bool IsApplied(Text text, Font font) => text.font == font && text.fontStyle == FontStyle.Normal &&
            !text.GetComponents<RaidUIStyle>().Any(style => style.useTheme);
        private static void ValidateRoot(GameObject root, Font font)
        {
            var targets = Numerals(root);
            if (targets.Count == 0) throw new InvalidOperationException("No Prism numeric fields in " + root.name);
            foreach (var text in targets.Keys)
            {
                if (!IsApplied(text, font)) throw new InvalidOperationException("Numeric font mismatch: " + AnimationUtility.CalculateTransformPath(text.transform, root.transform));
                ValidateGlyphs(text, font);
            }
        }
        private static void ValidateGlyphs(Text text, Font font)
        {
            string plain = text.supportRichText ? Regex.Replace(text.text ?? "", "<[^>]+>", "") : text.text ?? "";
            if (plain.Any(character => !char.IsWhiteSpace(character) && NumericCharacters.IndexOf(character) < 0))
                throw new InvalidOperationException("A numeric binding contains non-numeric text; preserve its language font: " + text.name + " = " + plain);
            foreach (char character in (plain + NumericCharacters).Distinct())
                if (!char.IsWhiteSpace(character) && !font.HasCharacter(character))
                    throw new InvalidOperationException("Approved numeric font lacks U+" + ((int)character).ToString("X4") + " in " + text.name);
        }
        private static void CheckWidth(Text text, Font font, string role, out string sample, out float available, out float preferred)
        {
            sample = role == "player health" ? "99,999 / 99,999" : role == "primary resource" ? "100 / 100" :
                role == "boss cast seconds" ? "99.9s" : role.Contains("cooldown") ? "999" : "100%";
            var settings = text.GetGenerationSettings(Vector2.zero);
            settings.font = font; settings.fontStyle = FontStyle.Normal; settings.scaleFactor = 1;
            settings.resizeTextForBestFit = false; settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            available = text.rectTransform.rect.width;
            using var generator = new TextGenerator();
            preferred = Mathf.Max(generator.GetPreferredWidth(sample, settings), generator.GetPreferredWidth(text.text ?? "", settings));
            if (available <= 0 || float.IsNaN(preferred) || float.IsInfinity(preferred) || preferred > available + .5f)
                throw new InvalidOperationException("Numeric sample exceeds authored field: " + role + " (" + preferred + " / " + available + "): " + sample);
        }
        private static string NumericPresentation(IEnumerable<Text> texts, Transform root) => string.Join("\n", texts.Select(text =>
            AnimationUtility.CalculateTransformPath(text.transform, root) + "|" + text.fontSize + "|" + text.text + "|" + text.color + "|" +
            text.alignment + "|" + text.supportRichText + "|" + text.lineSpacing + "|" + text.resizeTextForBestFit + "|" +
            text.resizeTextMinSize + "|" + text.resizeTextMaxSize + "|" + text.horizontalOverflow + "|" + text.verticalOverflow));
        private static string OtherTexts(IEnumerable<Text> texts, Transform root) => string.Join("\n", texts.Select(text =>
            AnimationUtility.CalculateTransformPath(text.transform, root) + "|" + EditorJsonUtility.ToJson(text)));
        private static string Effects(GameObject root) => string.Join("\n", root.GetComponentsInChildren<Shadow>(true).Select(effect =>
            AnimationUtility.CalculateTransformPath(effect.transform, root.transform) + "|" + effect.GetType().FullName + "|" + EditorJsonUtility.ToJson(effect)));
        private static string Geometry(GameObject root) => string.Join("\n", root.GetComponentsInChildren<RectTransform>(true).Select(rect =>
            AnimationUtility.CalculateTransformPath(rect, root.transform) + "|" + EditorJsonUtility.ToJson(rect)));
        private static string Hash(string path)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
        private static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
#endif
