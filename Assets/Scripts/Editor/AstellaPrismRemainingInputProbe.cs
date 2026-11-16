#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Object = UnityEngine.Object;

namespace RaidRogue.Editor
{
    /// <summary>Explicit, passive Play-mode input inspection. Never invokes an action or changes selection.</summary>
    public static class AstellaPrismRemainingInputProbe
    {
        private const string Menu = "Tools/Astella/Prism V4/Z Inspect Remaining UI Input";
        private const string Output = "C:/Astella/prism-v4-remaining-ui-20261116/input-probe.json";
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [Serializable] private sealed class Report
        {
            public string utc, eventSystem, inputModule, selectedObject;
            public int frame, screenWidth, screenHeight;
            public bool applicationFocused, settingsIsOpen, inputSystemEnabled, mouseAvailable, mouseLeftPressed;
            public Vector2 mousePosition;
            public string[] activeRoots;
            public ViewReport[] connectionViews, settingsViews;
            public SettingsReport[] settings;
        }
        [Serializable] private sealed class ViewReport
        {
            public string root, path, viewId, owner;
            public bool active;
            public ButtonReport[] actions;
        }
        [Serializable] private sealed class ButtonReport
        {
            public string id, path, targetGraphic, canvas, renderMode, eventCamera, raycastError;
            public bool missing, activeSelf, activeInHierarchy, enabled, interactable, effectiveInteractable;
            public bool targetGraphicRaycast, topHitBelongsToButton;
            public float targetGraphicAlpha;
            public int persistentListeners, runtimeListeners;
            public Vector3 worldCenter;
            public Vector3[] worldCorners;
            public Rect screenRect;
            public Vector2 screenCenter, centerIn1920x1080;
            public GroupReport[] canvasGroups;
            public HitReport[] centerHits;
        }
        [Serializable] private sealed class GroupReport
        {
            public string path;
            public float alpha;
            public bool enabled, active, interactable, blocksRaycasts, ignoreParentGroups;
        }
        [Serializable] private sealed class HitReport
        {
            public string path, raycaster, raycasterPath, graphicType, canvas, sprite;
            public bool belongsToButton, graphicRaycastTarget;
            public float graphicAlpha, distance;
            public int depth, sortingLayer, sortingOrder;
        }
        [Serializable] private sealed class SettingsReport
        {
            public string path, canvas, modal, buttonRoot;
            public bool activeSelf, activeInHierarchy, enabled, openField, modalActive, buttonRootActive;
        }

        [MenuItem(Menu, false, 2000)]
        public static void Inspect()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Run the remaining UI input probe in Play mode.");
            var roots = Object.FindObjectsByType<RaidUIRoot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var paths = new List<string>();
            var views = new List<ViewReport>();
            var settingsViews = new List<ViewReport>();
            foreach (var root in roots)
            {
                if (!root.gameObject.activeInHierarchy) continue;
                paths.Add(Path(root.transform));
                foreach (var view in root.GetComponentsInChildren<RaidUIView>(true))
                {
                    if (view.viewId != "connection" && view.viewId != "settings") continue;
                    var buttons = new List<ButtonReport>();
                    if (view.actions != null)
                        foreach (var action in view.actions)
                            if (action != null && WantedAction(view.viewId, action.id))
                                buttons.Add(InspectButton(action.id, action.button));
                    var sample = new ViewReport { root = Path(root.transform), path = Path(view.transform), viewId = view.viewId,
                        owner = view.owner != null ? view.owner.GetType().FullName + " @ " + Path(view.owner.transform) : "",
                        active = view.gameObject.activeInHierarchy, actions = buttons.ToArray() };
                    if (view.viewId == "settings") settingsViews.Add(sample); else views.Add(sample);
                }
            }
            var settings = new List<SettingsReport>();
            foreach (var ui in Object.FindObjectsByType<LocalCameraSettingsUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var modal = ReadField(ui, "_modal") as GameObject;
                var button = ReadField(ui, "_buttonRoot") as GameObject;
                var canvas = ReadField(ui, "_canvas") as Canvas;
                settings.Add(new SettingsReport { path = Path(ui.transform), activeSelf = ui.gameObject.activeSelf,
                    activeInHierarchy = ui.gameObject.activeInHierarchy, enabled = ui.enabled,
                    openField = ReadField(ui, "_open") is bool open && open,
                    modal = modal != null ? Path(modal.transform) : "", modalActive = modal != null && modal.activeInHierarchy,
                    buttonRoot = button != null ? Path(button.transform) : "", buttonRootActive = button != null && button.activeInHierarchy,
                    canvas = canvas != null ? Path(canvas.transform) : "" });
            }
            var events = EventSystem.current;
            var report = new Report { utc = DateTime.UtcNow.ToString("O"), frame = Time.frameCount,
                screenWidth = Screen.width, screenHeight = Screen.height, applicationFocused = Application.isFocused,
                eventSystem = events != null ? Path(events.transform) : "",
                inputModule = events != null && events.currentInputModule != null ? events.currentInputModule.GetType().FullName : "",
                selectedObject = events != null && events.currentSelectedGameObject != null ? Path(events.currentSelectedGameObject.transform) : "",
                settingsIsOpen = LocalCameraSettingsUI.IsOpen, activeRoots = paths.ToArray(), connectionViews = views.ToArray(),
                settingsViews = settingsViews.ToArray(), settings = settings.ToArray() };
#if ENABLE_INPUT_SYSTEM
            report.inputSystemEnabled = true;
            var mouse = Mouse.current;
            report.mouseAvailable = mouse != null;
            if (mouse != null)
            {
                report.mousePosition = mouse.position.ReadValue();
                report.mouseLeftPressed = mouse.leftButton.isPressed;
            }
#endif
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Output));
            File.WriteAllText(Output, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            Debug.Log("Prism remaining UI input probe: " + Output + " (passive; no actions invoked)");
        }

        [MenuItem(Menu, true)] private static bool CanInspect() => EditorApplication.isPlaying;

        private static bool WantedAction(string viewId, string id) => viewId == "connection"
            ? id == "설정" || id == "방 만들기"
            : id == "화면" || id == "사운드" || id == "조작" || id == "카메라" || id == "HUDLayout";

        private static ButtonReport InspectButton(string id, Button button)
        {
            var sample = new ButtonReport { id = id, missing = button == null, runtimeListeners = -1, persistentListeners = -1,
                canvasGroups = Array.Empty<GroupReport>(), centerHits = Array.Empty<HitReport>(), worldCorners = Array.Empty<Vector3>() };
            if (button == null) return sample;
            sample.path = Path(button.transform); sample.activeSelf = button.gameObject.activeSelf;
            sample.activeInHierarchy = button.gameObject.activeInHierarchy; sample.enabled = button.enabled;
            sample.interactable = button.interactable; sample.effectiveInteractable = button.IsInteractable();
            sample.persistentListeners = button.onClick.GetPersistentEventCount(); sample.runtimeListeners = RuntimeCount(button);
            if (button.targetGraphic != null)
            {
                sample.targetGraphic = Path(button.targetGraphic.transform);
                sample.targetGraphicRaycast = button.targetGraphic.raycastTarget;
                sample.targetGraphicAlpha = button.targetGraphic.color.a;
            }
            var groups = new List<GroupReport>();
            for (var node = button.transform; node != null; node = node.parent)
                foreach (var group in node.GetComponents<CanvasGroup>())
                    groups.Add(new GroupReport { path = Path(node), alpha = group.alpha, enabled = group.enabled,
                        active = group.gameObject.activeInHierarchy, interactable = group.interactable,
                        blocksRaycasts = group.blocksRaycasts, ignoreParentGroups = group.ignoreParentGroups });
            sample.canvasGroups = groups.ToArray();
            var canvas = button.GetComponentInParent<Canvas>();
            if (canvas != null) canvas = canvas.rootCanvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera ?? Camera.main : null;
            sample.canvas = canvas != null ? Path(canvas.transform) : "";
            sample.renderMode = canvas != null ? canvas.renderMode.ToString() : "none";
            sample.eventCamera = camera != null ? Path(camera.transform) : "overlay/null";
            var rect = button.transform as RectTransform;
            if (rect == null) { sample.raycastError = "Button has no RectTransform"; return sample; }
            var world = new Vector3[4]; rect.GetWorldCorners(world); sample.worldCorners = world;
            sample.worldCenter = (world[0] + world[2]) * .5f;
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var corner in world)
            {
                var point = RectTransformUtility.WorldToScreenPoint(camera, corner);
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            sample.screenRect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            sample.screenCenter = RectTransformUtility.WorldToScreenPoint(camera, sample.worldCenter);
            sample.centerIn1920x1080 = new Vector2(Screen.width > 0 ? sample.screenCenter.x * 1920f / Screen.width : 0,
                Screen.height > 0 ? sample.screenCenter.y * 1080f / Screen.height : 0);
            var events = EventSystem.current;
            if (events == null) { sample.raycastError = "No current EventSystem"; return sample; }
            var hits = new List<RaycastResult>();
            events.RaycastAll(new PointerEventData(events) { position = sample.screenCenter }, hits);
            var details = new List<HitReport>();
            for (int i = 0; i < hits.Count && i < 16; i++)
            {
                var hit = hits[i]; var graphic = hit.gameObject != null ? hit.gameObject.GetComponent<Graphic>() : null;
                var image = graphic as Image;
                bool own = hit.gameObject != null && (hit.gameObject.transform == button.transform || hit.gameObject.transform.IsChildOf(button.transform));
                if (i == 0) sample.topHitBelongsToButton = own;
                details.Add(new HitReport { path = hit.gameObject != null ? Path(hit.gameObject.transform) : "", belongsToButton = own,
                    raycaster = hit.module != null ? hit.module.GetType().FullName : "", raycasterPath = hit.module != null ? Path(hit.module.transform) : "",
                    graphicType = graphic != null ? graphic.GetType().FullName : "", graphicRaycastTarget = graphic != null && graphic.raycastTarget,
                    graphicAlpha = graphic != null ? graphic.color.a : 0, canvas = graphic != null && graphic.canvas != null ? Path(graphic.canvas.transform) : "",
                    sprite = image != null && image.overrideSprite != null ? image.overrideSprite.name : "",
                    distance = hit.distance, depth = hit.depth, sortingLayer = hit.sortingLayer, sortingOrder = hit.sortingOrder });
            }
            sample.centerHits = details.ToArray();
            return sample;
        }

        private static int RuntimeCount(Button button)
        {
            try
            {
                var calls = typeof(UnityEngine.Events.UnityEventBase).GetField("m_Calls", Fields)?.GetValue(button.onClick);
                var runtime = calls?.GetType().GetField("m_RuntimeCalls", Fields)?.GetValue(calls);
                return runtime is ICollection collection ? collection.Count : -1;
            }
            catch { return -1; }
        }

        private static object ReadField(object owner, string name) => owner.GetType().GetField(name, Fields)?.GetValue(owner);
        private static string Path(Transform node)
        {
            if (node == null) return "";
            var result = node.name;
            for (var parent = node.parent; parent != null; parent = parent.parent) result = parent.name + "/" + result;
            return node.gameObject.scene.name + ":/" + result;
        }
    }
}
#endif
