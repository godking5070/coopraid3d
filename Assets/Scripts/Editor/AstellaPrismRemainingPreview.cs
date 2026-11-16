#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RaidRogue.Editor
{
    /// <summary>Isolated authored-asset previews, deliberately independent of gameplay presenters.</summary>
    public static class AstellaPrismRemainingPreview
    {
        private const string Folder = "C:/Astella/prism-v4-remaining-ui-20261116/catalog-preview";
        private const int Width = 1920, Height = 1080, Layer = 31;
        [Serializable] private sealed class Entry
        {
            public string id, source, image, selectedBinding, skipReason, previewOnlyVisibility;
            public bool rendered;
            public int visibleGraphics;
            public string[] previewOnlyText;
        }
        [Serializable] private sealed class Report
        {
            public string purpose = "Design preview, not gameplay certification";
            public string capturedUtc, renderPath, overview;
            public int width = Width, height = Height;
            public bool originalActiveSceneRestored, originalSceneDirtyUnchanged;
            public Entry[] entries;
            public string[] errors;
        }

        [MenuItem("Tools/Astella/Prism V4/Render Remaining UI Catalog")]
        public static void RenderCatalog() => RenderCatalogTo(Folder);

        public static void RenderCatalogTo(string outputFolder)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Render the catalog in Edit Mode; no gameplay state is required.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("A real GPU is required; do not use -nographics.");
            var profile = Resources.Load<RaidUIPresentation>("UI/RaidUIPresentation");
            if (profile == null) throw new InvalidOperationException("Current RaidUIPresentation profile is missing.");
            Directory.CreateDirectory(outputFolder);
            var report = new Report { capturedUtc = DateTime.UtcNow.ToString("o") };
            var entries = new List<Entry>(); var errors = new List<string>();
            var original = SceneManager.GetActiveScene(); bool originalDirty = original.IsValid() && original.isDirty;
            Scene scene = default(Scene); RenderTexture target = null;
            RenderTexture previous = RenderTexture.active; Texture2D overview = null;
            try
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                scene.name = "Prism isolated remaining UI design catalog";
                var camera = New("Catalog camera", scene, typeof(Camera)).GetComponent<Camera>();
                camera.enabled = false; camera.cameraType = CameraType.Game; camera.scene = scene;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                camera.orthographic = true; camera.orthographicSize = Height * .5f; camera.aspect = (float)Width / Height;
                camera.transform.position = new Vector3(0, 0, -10); camera.nearClipPlane = .1f; camera.farClipPlane = 30;
                camera.cullingMask = 1 << Layer; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(13, 16, 33, 255);
                camera.allowHDR = false; camera.allowMSAA = false; camera.useOcclusionCulling = false;
                var canvas = New("Catalog Canvas", scene, typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
                ((RectTransform)canvas.transform).sizeDelta = new Vector2(Width, Height);
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                target.Create(); camera.targetTexture = target;
                overview = new Texture2D(1920, 1080, TextureFormat.RGBA32, false);
                var blank = new Color32[1920 * 1080];
                for (int i = 0; i < blank.Length; i++) blank[i] = new Color32(13, 16, 33, 255);
                overview.SetPixels32(blank);
                string[] ids = { "settings", "connection", "origin", "character", "metrics", "resurrection", "wipe", "skill-reward", "build-station" };
                for (int index = 0; index < ids.Length; index++)
                {
                    string id = ids[index]; var entry = new Entry { id = id }; entries.Add(entry);
                    GameObject clone = null; GameObject heading = null, footer = null; Texture2D image = null;
                    try
                    {
                        var source = id == "build-station"
                            ? (profile.buildStationPrefab != null ? profile.buildStationPrefab.gameObject
                                : Resources.Load<RaidBuildStationView>("UI/ConstellationBuild")?.gameObject)
                            : profile.View(id)?.gameObject;
                        if (source == null) throw new InvalidOperationException("No authored prefab in the current profile/fallback.");
                        entry.source = AssetDatabase.GetAssetPath(source);
                        clone = Object.Instantiate(source, canvas.transform, false); clone.name = id + " (temporary design preview)";
                        foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
                            if (!(behaviour is UnityEngine.EventSystems.UIBehaviour)) behaviour.enabled = false;
                        foreach (var group in clone.GetComponentsInChildren<CanvasGroup>(true)) group.alpha = 1;
                        clone.SetActive(true);
                        var texts = new List<string>(); var view = clone.GetComponent<RaidUIView>();
                        if (view != null) { Stretch((RectTransform)view.transform); Prepare(view, entry, texts); }
                        else
                        {
                            Center((RectTransform)clone.transform); entry.selectedBinding = "RaidBuildStationView";
                            int hidden = 0;
                            foreach (var artwork in clone.GetComponentsInChildren<Image>(true))
                                if (artwork.name == "Artwork" && artwork.sprite == null) { artwork.enabled = false; hidden++; }
                            entry.previewOnlyVisibility = "Hidden " + hidden + " unassigned Artwork Images on the temporary build-station clone.";
                        }
                        SetLayer(clone.transform);
                        Font font = null;
                        foreach (var text in clone.GetComponentsInChildren<Text>(true)) if (text.font != null) { font = text.font; break; }
                        heading = Label(canvas.transform, id + " / authored design preview", 496, 31, font, 26);
                        footer = Label(canvas.transform, "디자인 미리보기 · 게임플레이 검증 아님 · 표시용 문구는 복제본에만 적용", -505, 26, font, 20);
                        Canvas.ForceUpdateCanvases();
                        foreach (var graphic in clone.GetComponentsInChildren<Graphic>(false))
                            if (graphic.enabled && graphic.color.a > 0) entry.visibleGraphics++;
                        if (entry.visibleGraphics == 0) throw new InvalidOperationException("Selected authored panel contains no active graphics.");
                        image = Capture(camera, target, report);
                        entry.image = outputFolder + "/" + (index + 1).ToString("00") + "-" + id + ".png";
                        File.WriteAllBytes(entry.image, image.EncodeToPNG());
                        entry.previewOnlyText = texts.ToArray(); entry.rendered = true;
                        var pixels = new Color32[640 * 360];
                        for (int y = 0; y < 360; y++) for (int x = 0; x < 640; x++)
                            pixels[y * 640 + x] = image.GetPixelBilinear((x + .5f) / 640, (y + .5f) / 360);
                        overview.SetPixels32(index % 3 * 640, (2 - index / 3) * 360, 640, 360, pixels);
                    }
                    catch (Exception error) { entry.skipReason = error.Message; errors.Add(id + ": " + error.Message); }
                    finally
                    {
                        if (image != null) Object.DestroyImmediate(image);
                        if (clone != null) Object.DestroyImmediate(clone);
                        if (heading != null) Object.DestroyImmediate(heading);
                        if (footer != null) Object.DestroyImmediate(footer);
                    }
                }
                overview.Apply(); report.overview = outputFolder + "/catalog-overview.png";
                File.WriteAllBytes(report.overview, overview.EncodeToPNG());
            }
            catch (Exception error) { errors.Add(error.ToString()); }
            finally
            {
                RenderTexture.active = previous;
                if (overview != null) Object.DestroyImmediate(overview);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                report.originalActiveSceneRestored = SceneManager.GetActiveScene() == original;
                report.originalSceneDirtyUnchanged = !original.IsValid() || original.isDirty == originalDirty;
                if (!report.originalActiveSceneRestored || !report.originalSceneDirtyUnchanged)
                    errors.Add("Original scene active/dirty state was not preserved.");
                report.entries = entries.ToArray(); report.errors = errors.ToArray();
                File.WriteAllText(outputFolder + "/catalog-preview.json", JsonUtility.ToJson(report, true) + "\n");
            }
            Debug.Log("PRISM REMAINING DESIGN CATALOG: " + entries.FindAll(e => e.rendered).Count + "/" + entries.Count
                + " authored panels. Design preview, not gameplay certification. " + outputFolder + "/catalog-preview.json");
        }

        private static void Prepare(RaidUIView view, Entry entry, List<string> texts)
        {
            string key = view.viewId == "settings" ? "_modal" : view.viewId == "connection" ? "_overlay"
                : view.viewId == "metrics" ? "_results" : view.viewId == "resurrection" ? "_deathPanel"
                : view.viewId == "origin" ? "_panel" : "_root";
            var selected = Bound(view, key); if (selected == null) throw new InvalidOperationException("Missing binding " + key);
            entry.selectedBinding = key; Reveal(selected, view.transform);
            bool fullscreen = view.viewId == "settings" || view.viewId == "connection" || view.viewId == "metrics"
                || view.viewId == "wipe" || view.viewId == "skill-reward";
            if (fullscreen) Stretch((RectTransform)selected); else Center((RectTransform)selected);
            if (view.viewId == "settings")
            {
                Center(Bound(view, "_panel") as RectTransform); Toggle(view, "_confirmation", false); Toggle(view, "_conflict", false);
                for (int i = 0; i < 4; i++) Toggle(view, "_tabs/[" + i + "]", i == 0);
            }
            if (view.viewId == "connection")
            {
                foreach (string hide in new[] { "_room", "_confirmation", "_recoveryPanel", "_campaignDialog", "_campaignMenu", "_createMenu", "_joinMenu", "_lanMenu" }) Toggle(view, hide, false);
                entry.previewOnlyVisibility = "Unrelated menu overlays hidden on the temporary connection clone; runtime Home is verified separately.";
                var home = Bound(view, "_homeMenu"); if (home == null) throw new InvalidOperationException("No Home panel binding.");
                for (Transform path = home; path != null && path != view.transform; path = path.parent) path.gameObject.SetActive(true);
            }
            if (view.viewId == "origin")
            {
                for (int i = 0; i < 3; i++) Toggle(view, "_routeChoices/[" + i + "]", false);
                for (int i = 0; i < 12; i++) Toggle(view, "_choices/[" + i + "]", true);
                var jobs = PreviewDefinitions();
                for (int i = 0; i < jobs.Length && i < 12; i++)
                {
                    var job = jobs[i];
                    string role = job.role == CharacterRole.Tank ? "탱커" : job.role == CharacterRole.Healer ? "힐러" :
                        job.classification == AttackClassification.Melee ? "근딜" : job.classification == AttackClassification.RangedPhysical ? "원거리" : "캐스터";
                    Sample(view, "_choiceLabels/[" + i + "]", job.name + " · " + role + "\n" + job.description, texts);
                }
            }
            if (view.viewId == "metrics")
            {
                Center(Bound(view, "_resultBox") as RectTransform);
                Sample(view, "_resultTitle", "전투 결과 · 디자인 미리보기", texts);
                Sample(view, "_resultTime", "전투 시간 180.00초 · 전체 런 재도전 2회 · 표시 검사용 샘플", texts);
                Sample(view, "_resultEvents", "카운터 4회 · 무력화 2회 · 부활 1회 · 표시 검사용 샘플", texts);
                string[] names = { "이지스 팔라딘", "블러드 아포테카리", "나이트블레이드", "아스트로맨서" };
                for (int row = 0; row < 4; row++) for (int column = 0; column < 10; column++)
                    Sample(view, "_resultRows/[" + row + "]/[" + column + "]", column == 0 ? names[row] :
                        column == 6 ? row.ToString() : column == 2 || column == 4 ? "1,234.5" : "123,456", texts);
            }
            if (view.viewId == "resurrection") Sample(view, "_deathText", "전투 불능\n동료를 관전하거나 부활을 기다립니다.", texts);
            if (view.viewId == "wipe") { Sample(view, "_title", "파티 전멸", texts); Sample(view, "_details", "디자인 미리보기 · 전투 상태와 무관한 표시용 화면", texts); Toggle(view, "_buttons", true); }
            if (view.viewId == "skill-reward")
            {
                Center(Bound(view, "_panel") as RectTransform);
                Sample(view, "_header", "확정 스킬 보상 · 디자인 미리보기", texts);
                Sample(view, "_status", "보상 카드의 기본 스타일을 확인하는 화면입니다.", texts);
                var skills = PreviewDefinitions().SelectMany(job => job.skills).Where(skill => skill != null && skill.implemented && !string.IsNullOrEmpty(skill.description))
                    .OrderByDescending(skill => skill.description.Length).Take(3).ToArray();
                for (int i = 0; i < skills.Length; i++) Sample(view, "_labels/[" + i + "]", "[" + (i + 1) + "] " + skills[i].name +
                    "\n\n" + skills[i].description + "\n\n이번 런에서 한 종류만 확정", texts);
            }
        }

        private static CharacterDefinition[] PreviewDefinitions()
        {
            var registered = CharacterCatalog.All.Where(job => job.legacyRole == 0 && job.playable).ToArray();
            if (registered.Length > 0) return registered;
            // Pure definition construction only: never register jobs or mutate a runtime catalog to render an Edit Mode preview.
            return new[] { AegisPaladinData.BuildNineKey() }.Concat(CharacterRosterData.Jobs.Select(job =>
                NineKeyTankData.Supports(job) ? NineKeyTankData.Build(job) : NineKeyHealerData.Supports(job) ? NineKeyHealerData.Build(job) :
                NineKeyDamageData.Supports(job) ? NineKeyDamageData.Build(job) : CharacterRosterData.Definition(job)))
                .Where(job => job.legacyRole == 0 && job.playable).ToArray();
        }

        private static Transform Bound(RaidUIView view, string key)
        {
            var value = view.references.Find(r => r.path == key)?.target;
            return value is GameObject go ? go.transform : value is Component component ? component.transform : null;
        }
        private static void Sample(RaidUIView view, string key, string value, List<string> changed)
        {
            var target = Bound(view, key); var text = target != null ? target.GetComponent<Text>() : null;
            if (text == null || !string.IsNullOrWhiteSpace(text.text)) return;
            text.text = value; changed.Add(key + ": " + value);
        }
        private static void Toggle(RaidUIView view, string key, bool active) { var tr = Bound(view, key); if (tr != null) tr.gameObject.SetActive(active); }
        private static void Reveal(Transform selected, Transform root)
        {
            for (var current = selected; current != null; current = current.parent)
            {
                current.gameObject.SetActive(true); if (current == root) break;
                foreach (Transform sibling in current.parent) if (sibling != current) sibling.gameObject.SetActive(false);
            }
        }
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one;
        }
        private static void Center(RectTransform rect)
        {
            if (rect == null) return; var size = rect.rect.size;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(0, -10); rect.localScale = Vector3.one;
        }
        private static GameObject New(string name, Scene scene, params Type[] components)
        {
            var go = new GameObject(name, components); SceneManager.MoveGameObjectToScene(go, scene); go.layer = Layer; return go;
        }
        private static void SetLayer(Transform root) { root.gameObject.layer = Layer; foreach (Transform child in root) SetLayer(child); }
        private static GameObject Label(Transform parent, string value, float y, float height, Font font, int size)
        {
            var go = new GameObject("Preview annotation", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false); go.layer = Layer;
            var rect = (RectTransform)go.transform; rect.sizeDelta = new Vector2(1820, height); rect.anchoredPosition = new Vector2(0, y);
            var text = go.GetComponent<Text>(); text.text = value; text.font = font ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size; text.color = new Color(.85f, .83f, .94f); text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false; return go;
        }
        private static Texture2D Capture(Camera camera, RenderTexture target, Report report)
        {
            Canvas.ForceUpdateCanvases();
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                var additional = camera.GetUniversalAdditionalCameraData(); additional.renderPostProcessing = false;
                additional.renderShadows = false; additional.volumeLayerMask = 0;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                report.renderPath = "UniversalRenderPipeline.SingleCameraRequest";
            }
            else { camera.Render(); report.renderPath = "Camera.Render"; }
            RenderTexture.active = target; var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); image.Apply(); return image;
        }
    }
}
#endif
