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
        private const string OutlineEvidence = "Logs/OxaniumNumericOutline";
        private const string NumericCharacters = "0123456789.,/%+-s:—";
        private static readonly Color NumericInk = new Color(19 / 255f, 16 / 255f, 34 / 255f, 1);

        [Serializable] private sealed class PlayerCaptionReport
        {
            public string utc, prefab, backup, error, resourceNamePath, hpCaptionPath;
            public string prefabHashBefore, prefabHashAfter, metaHashBefore, metaHashAfter;
            public Vector2 resourcePositionBefore, resourcePositionAfter, resourceSize;
            public bool passed, alreadyApplied, hpCaptionActiveBefore, hpCaptionActiveAfter;
            public bool resourceEnabled, resourceActive, allTextUnchanged, geometryUnchanged, effectsUnchanged, bindingsUnchanged, otherActivationUnchanged;
            public string boundary = "Only the static HP caption GameObject is disabled. The current dynamic resource label, its position, all other authored text and scene/profile files are preserved. Play rendering requires a separate check.";
        }

        [MenuItem("Tools/Astella/Refine Player Captions")]
        public static void RefinePlayerCaptions()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play and close Prefab Mode before refining captions.");
            if (!string.Equals(Path.GetFullPath(Application.dataPath).TrimEnd('/', '\\'), @"C:\Astella\Astella\Assets", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Run only in the original Astella Unity project.");
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            if (profile?.hudPrefab == null) throw new InvalidOperationException("An active authored HUD is required.");
            var report = new PlayerCaptionReport { utc = DateTime.UtcNow.ToString("O"), prefab = AssetDatabase.GetAssetPath(profile.hudPrefab),
                backup = "Logs/PlayerCaptionRefinement/Backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") };
            report.prefabHashBefore = Hash(report.prefab); report.metaHashBefore = Hash(report.prefab + ".meta");
            string sceneHash = Hash(ScenePath), profileHash = Hash(ProfilePath);
            Directory.CreateDirectory(report.backup);
            File.Copy(report.prefab, report.backup + "/RaidHUD.prefab");
            File.Copy(report.prefab + ".meta", report.backup + "/RaidHUD.prefab.meta");
            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(report.prefab);
                var (bridge, label, caption) = PlayerCaptions(contents);
                report.resourceNamePath = AnimationUtility.CalculateTransformPath(label.transform, contents.transform);
                report.hpCaptionPath = AnimationUtility.CalculateTransformPath(caption.transform, contents.transform);
                report.resourcePositionBefore = label.rectTransform.anchoredPosition;
                report.resourceSize = label.rectTransform.sizeDelta;
                report.hpCaptionActiveBefore = caption.gameObject.activeSelf;
                string texts = OtherTexts(contents.GetComponentsInChildren<Text>(true), contents.transform);
                string geometry = Geometry(contents), effects = Effects(contents), bindings = EditorJsonUtility.ToJson(bridge);
                string activation = CaptionActivation(contents, caption.gameObject);
                caption.gameObject.SetActive(false);
                report.resourcePositionAfter = label.rectTransform.anchoredPosition;
                report.resourceEnabled = label.enabled; report.resourceActive = label.gameObject.activeSelf;
                report.hpCaptionActiveAfter = caption.gameObject.activeSelf;
                report.allTextUnchanged = texts == OtherTexts(contents.GetComponentsInChildren<Text>(true), contents.transform);
                report.geometryUnchanged = geometry == Geometry(contents);
                report.effectsUnchanged = effects == Effects(contents);
                report.bindingsUnchanged = bindings == EditorJsonUtility.ToJson(bridge);
                report.otherActivationUnchanged = activation == CaptionActivation(contents, caption.gameObject);
                if (!report.allTextUnchanged || !report.geometryUnchanged || !report.effectsUnchanged || !report.bindingsUnchanged || !report.otherActivationUnchanged)
                    throw new InvalidOperationException("Caption refinement changed authored state outside the static HP caption.");
                RaidUIAuthoring.ValidateScripts(contents);
                report.alreadyApplied = !report.hpCaptionActiveBefore;
                if (!report.alreadyApplied && PrefabUtility.SaveAsPrefabAsset(contents, report.prefab) == null)
                    throw new IOException("Could not save the reviewed HUD caption change.");
                report.prefabHashAfter = Hash(report.prefab); report.metaHashAfter = Hash(report.prefab + ".meta");
                if (report.metaHashBefore != report.metaHashAfter || Hash(ScenePath) != sceneHash || Hash(ProfilePath) != profileHash)
                    throw new InvalidOperationException("Caption refinement must preserve the GUID and scene/profile files.");
                report.passed = true;
                Debug.Log("PLAYER CAPTIONS PASS: dynamic resource name stays in its authored position; only the static HP caption is hidden.");
            }
            catch (Exception error) { report.error = error.ToString(); throw; }
            finally
            {
                if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
                Directory.CreateDirectory("Logs/PlayerCaptionRefinement");
                File.WriteAllText("Logs/PlayerCaptionRefinement/report.json", JsonUtility.ToJson(report, true));
            }
        }

        [MenuItem("Tools/Astella/Validate Player Captions")]
        public static void ValidatePlayerCaptions()
        {
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            if (profile?.hudPrefab == null) throw new InvalidOperationException("An active authored HUD is required.");
            foreach (var root in new[] { profile.hudPrefab }.Concat(Object.FindObjectsByType<RaidUIRoot>(FindObjectsSortMode.None)))
            {
                var (_, _, caption) = PlayerCaptions(root.gameObject);
                if (caption.gameObject.activeSelf) throw new InvalidOperationException("Static HP caption is still active.");
            }
        }

        private static (AstellaPrismHUDBridge bridge, Text label, Text caption) PlayerCaptions(GameObject root)
        {
            var bridge = root.GetComponentsInChildren<AstellaPrismHUDBridge>(true).Single();
            var view = bridge.playerView;
            var label = view?.nameText;
            var health = view?.healthFill != null ? view.healthFill.transform.parent : null;
            var caption = health != null ? health.Find("Caption")?.GetComponent<Text>() : null;
            if (bridge.jobSource == null || label == null || !label.enabled || !label.gameObject.activeSelf ||
                label.rectTransform.sizeDelta.x <= 0 || label.rectTransform.sizeDelta.y <= 0 ||
                view.jobResourceFill == null || label.transform.parent != view.jobResourceFill.transform.parent ||
                caption == null || caption.text != "HP" || caption == view.valueText || caption == view.resourceText)
                throw new InvalidOperationException("Expected the visible dynamic resource label and a separate static HP caption.");
            return (bridge, label, caption);
        }
        private static string CaptionActivation(GameObject root, GameObject caption) => string.Join("\n",
            root.GetComponentsInChildren<Transform>(true).Where(node => node.gameObject != caption).Select(node =>
                AnimationUtility.CalculateTransformPath(node, root.transform) + "|" + node.gameObject.activeSelf));

        [Serializable] private sealed class Target
        {
            public string role, path, text, previousFont, font, previousStyle, outlineBefore, outlineAfter, widthSample;
            public int size;
            public float availableWidth, preferredWidth;
            public Vector2 outlineDistanceBefore, outlineDistanceAfter;
        }
        [Serializable] private sealed class SourceAsset
        { public string source, copy, sourceHashBefore, sourceHashAfter; }
        [Serializable] private sealed class Report
        {
            public string utc, folder, source, copy, sourceHashBefore, sourceHashAfter, backup, font, error;
            public bool passed, sceneSaved, outlinesUnchanged, geometryUnchanged, otherEffectsUnchanged, numericFontsUnchanged;
            public bool strongerNumericOutlines;
            public int preservedOtherTexts;
            public Target[] targets;
            public SourceAsset[] assets;
            public string boundary = "Numeric font only. MaruBuri headings, body, keys, all rectangles and existing Outline/Shadow settings are preserved. Thicker outlines remain review variants; Play rendering needs separate verification.";
        }

        [MenuItem("Tools/Astella/Apply Oxanium Numerals (Preserved Copy)")]
        public static void Apply() => Apply(false);

        [MenuItem("Tools/Astella/Apply Strong Numeric Outlines (Preserved Copy)")]
        public static void ApplyStrongNumericOutlines() => Apply(true);

        private static void Apply(bool strongerOutlines)
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
            if (strongerOutlines) ValidateRoot(profile.hudPrefab.gameObject, font);
            if (sourceTargets.Keys.All(text => IsApplied(text, font) && (!strongerOutlines || IsStrongOutline(text))))
            {
                if (strongerOutlines) ValidateNumericOutlines(); else ValidateActive();
                Debug.Log(strongerOutlines ? "NUMERIC OUTLINES ALREADY APPLIED: name/title outlines preserved." : "OXANIUM NUMERALS ALREADY APPLIED: authored outlines preserved.");
                return;
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string evidence = strongerOutlines ? OutlineEvidence : Evidence;
            var report = new Report { utc = DateTime.UtcNow.ToString("O"), font = FontPath,
                strongerNumericOutlines = strongerOutlines,
                folder = "Assets/UI/AstellaPrismV4/Typography/" + (strongerOutlines ? "NumericOutline-" : "Oxanium-") + stamp,
                source = AssetDatabase.GetAssetPath(profile.hudPrefab), backup = evidence + "/Backup-" + stamp };
            if (strongerOutlines) report.boundary = "Numeric Outline effects only, at 7.5% of logical font size. Numeric fonts, MaruBuri names/titles and all other text/effects/geometry are preserved. Actual Canvas rendering needs separate verification.";
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
                    string otherEffectsBefore = PreservedEffects(contents, targets.Keys);
                    string fontsBefore = Fonts(targets.Keys);
                    report.targets = targets.Select(pair => ApplyText(pair.Key, pair.Value, font, contents.transform, strongerOutlines)).ToArray();
                    report.geometryUnchanged = Geometry(contents) == geometryBefore;
                    report.outlinesUnchanged = Effects(contents) == outlinesBefore;
                    report.otherEffectsUnchanged = PreservedEffects(contents, targets.Keys) == otherEffectsBefore;
                    report.numericFontsUnchanged = Fonts(targets.Keys) == fontsBefore;
                    if (!report.geometryUnchanged || (!strongerOutlines && !report.outlinesUnchanged) ||
                        !report.otherEffectsUnchanged || (strongerOutlines && !report.numericFontsUnchanged) || OtherTexts(otherTexts, contents.transform) != otherBefore ||
                        NumericPresentation(targets.Keys, contents.transform) != numericBefore)
                        throw new InvalidOperationException("Numeric authoring changed text, fonts, rectangles or effects outside the selected operation.");
                    report.preservedOtherTexts = otherTexts.Length;
                    ValidateRoot(contents, font);
                    if (strongerOutlines) ValidateOutlines(contents);
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
                instance.name = strongerOutlines ? "Astella_PrismV4_NumericOutline" : "Astella_PrismV4_MaruBuri_Oxanium"; instance.SetActive(true);
                ValidateActive();
                if (strongerOutlines) ValidateNumericOutlines();
                AstellaMaruBuriTypography.ValidateActive();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the main scene.");
                report.sceneSaved = true; report.passed = true;
                Debug.Log((strongerOutlines ? "NUMERIC OUTLINES PASS: " : "OXANIUM NUMERALS PASS: ") + report.targets.Length +
                    " numeric fields; MaruBuri names/titles, other text, layout and non-target effects preserved. " + report.folder);
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
                Directory.CreateDirectory(evidence);
                File.WriteAllText(evidence + "/report.json", JsonUtility.ToJson(report, true));
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

        [MenuItem("Tools/Astella/Validate Numeric Outlines")]
        public static void ValidateNumericOutlines()
        {
            ValidateActive();
            var profile = AssetDatabase.LoadAssetAtPath<RaidUIPresentation>(ProfilePath);
            ValidateOutlines(profile.hudPrefab.gameObject);
            foreach (var root in Object.FindObjectsByType<RaidUIRoot>(FindObjectsSortMode.None)) ValidateOutlines(root.gameObject);
            AstellaMaruBuriTypography.ValidateActive();
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
        private static Target ApplyText(Text text, string role, Font font, Transform root, bool strongerOutlines)
        {
            var target = new Target { role = role, path = AnimationUtility.CalculateTransformPath(text.transform, root), text = text.text,
                previousFont = AssetDatabase.GetAssetPath(text.font), font = FontPath, previousStyle = text.fontStyle.ToString(),
                size = text.fontSize, outlineBefore = Effects(text.gameObject),
                outlineDistanceBefore = text.GetComponent<Outline>() != null ? text.GetComponent<Outline>().effectDistance : Vector2.zero };
            ValidateGlyphs(text, font);
            CheckWidth(text, font, role, out target.widthSample, out target.availableWidth, out target.preferredWidth);
            if (strongerOutlines)
            {
                if (text.GetComponents<Outline>().Length > 1) throw new InvalidOperationException("Duplicate numeric outlines: " + text.name);
                var outline = text.GetComponent<Outline>() ?? text.gameObject.AddComponent<Outline>();
                outline.enabled = true; outline.effectColor = NumericInk; outline.useGraphicAlpha = true;
                float distance = OutlineDistance(text);
                outline.effectDistance = new Vector2(distance, -distance);
            }
            else
            {
                text.font = font; text.fontStyle = FontStyle.Normal;
                foreach (var style in text.GetComponents<RaidUIStyle>()) style.useTheme = false;
            }
            target.outlineAfter = Effects(text.gameObject);
            target.outlineDistanceAfter = text.GetComponent<Outline>() != null ? text.GetComponent<Outline>().effectDistance : Vector2.zero;
            return target;
        }
        // Logical font size and effect distance share the same Canvas/parent scale.
        private static float OutlineDistance(Text text) => Mathf.Round(text.fontSize * .075f * 1000) / 1000;
        private static bool IsStrongOutline(Text text)
        {
            var outline = text.GetComponent<Outline>();
            float distance = OutlineDistance(text);
            return text.GetComponents<Outline>().Length == 1 && outline != null && outline.enabled && outline.useGraphicAlpha &&
                outline.effectColor == NumericInk && (outline.effectDistance - new Vector2(distance, -distance)).sqrMagnitude < .000001f;
        }
        private static void ValidateOutlines(GameObject root)
        {
            foreach (var text in Numerals(root).Keys)
                if (!IsStrongOutline(text)) throw new InvalidOperationException("Numeric outline mismatch: " + AnimationUtility.CalculateTransformPath(text.transform, root.transform));
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
        private static string PreservedEffects(GameObject root, IEnumerable<Text> numerals)
        {
            var targets = new HashSet<Text>(numerals);
            return string.Join("\n", root.GetComponentsInChildren<Shadow>(true)
                .Where(effect => !(effect is Outline) || !targets.Contains(effect.GetComponent<Text>()))
                .Select(effect => AnimationUtility.CalculateTransformPath(effect.transform, root.transform) + "|" + effect.GetType().FullName + "|" + EditorJsonUtility.ToJson(effect)));
        }
        private static string Fonts(IEnumerable<Text> texts) => string.Join("\n", texts.Select(text =>
            AssetDatabase.GetAssetPath(text.font) + "|" + text.fontStyle + "|" + string.Join(",", text.GetComponents<RaidUIStyle>().Select(style => style.useTheme))));
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
