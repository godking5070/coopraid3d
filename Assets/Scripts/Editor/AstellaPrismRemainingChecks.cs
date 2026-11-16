#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Astella.PrismV4;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RaidRogue.Editor
{
    /// <summary>Read-only source/copy contract checks. Never invokes actions, imports, saves assets or changes scene state.</summary>
    public static class AstellaPrismRemainingChecks
    {
        [Serializable] public sealed class Reference
        {
            public string address;
            public bool isNull;
            [NonSerialized] internal Object value;
        }

        [Serializable] public sealed class Field
        {
            public string key, value;
            public Reference target;
        }

        [Serializable] public sealed class Element
        {
            public string address, type;
            public Reference component;
            public Field[] references;
            public string controlSemantics;
        }

        [Serializable] public sealed class Node
        {
            public string address, name;
            public Reference target;
            public bool activeSelf, oldChart;
        }

        [Serializable] public sealed class Typography
        {
            public string address;
            public Reference text, font;
            public int fontSize;
            public FontStyle fontStyle;
        }

        [Serializable] public sealed class Geometry
        {
            public string address;
            public Reference rect;
            public bool corePrism;
            public Vector2 anchorMin, anchorMax, pivot, sizeDelta;
            public Vector3 anchoredPosition, localScale;
            public Quaternion localRotation;
        }

        [Serializable] public sealed class Contract
        {
            public string address, kind;
            public Reference owner;
            public Field[] fields;
        }

        [Serializable] public sealed class Report
        {
            public string rootName, generatedUtc;
            public bool passed;
            public int checks, originalObjects, originalComponents, originalTexts;
            public string[] errors, permittedGeometryChanges;
            public string boundary = "Loaded-prefab source/copy contracts only. No callbacks invoked; live pointer behavior and rendering are not certified.";
        }

        [Serializable] public sealed class Snapshot
        {
            public string rootName;
            public Node[] nodes;
            public Element[] components;
            public Typography[] typography;
            public Geometry[] geometry;
            public Contract[] contracts;
            public Report report;
            [NonSerialized] internal GameObject loadedRoot;
        }

        public static Snapshot Capture(GameObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var snapshot = new Snapshot { rootName = root.name, loadedRoot = root };
            var nodes = new List<Node>(); var elements = new List<Element>();
            var texts = new List<Typography>(); var geometry = new List<Geometry>(); var contracts = new List<Contract>();
            var bridge = root.GetComponentInChildren<AstellaPrismHUDBridge>(true);
            Transform prism = bridge != null && bridge.presentationRoot != null ? bridge.presentationRoot.transform : null;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                GameObject node = transform.gameObject;
                nodes.Add(new Node { address = Address(node, root), name = node.name, target = Ref(node, root), activeSelf = node.activeSelf,
                    oldChart = node.name == "Astella_StarChart" && node.GetComponentInChildren<Selectable>(true) == null });
                if (transform is RectTransform rect)
                    geometry.Add(new Geometry { address = Address(rect, root), rect = Ref(rect, root),
                        corePrism = prism != null && (rect == prism || rect.IsChildOf(prism)),
                        anchorMin = rect.anchorMin, anchorMax = rect.anchorMax, pivot = rect.pivot, sizeDelta = rect.sizeDelta,
                        anchoredPosition = rect.anchoredPosition3D, localScale = rect.localScale, localRotation = rect.localRotation });
                foreach (var component in node.GetComponents<Component>())
                {
                    if (component == null) throw new InvalidOperationException("Missing component before styling: " + PathOf(transform, root.transform));
                    elements.Add(new Element { address = Address(component, root), type = component.GetType().AssemblyQualifiedName,
                        component = Ref(component, root), references = ObjectFields(component, root), controlSemantics = Semantics(component) });
                    if (component is Text text)
                        texts.Add(new Typography { address = Address(text, root), text = Ref(text, root), font = Ref(text.font, root), fontSize = text.fontSize, fontStyle = text.fontStyle });
                    if (component is RaidUIView view) contracts.Add(CaptureView(view, root));
                    if (component is RaidUIRoot hud) contracts.Add(CapturePresenters(hud, root));
                    if (component is Button button) contracts.Add(CaptureButton(button, root));
                }
            }
            snapshot.nodes = nodes.ToArray(); snapshot.components = elements.ToArray(); snapshot.typography = texts.ToArray();
            snapshot.geometry = geometry.ToArray(); snapshot.contracts = contracts.ToArray();
            return snapshot;
        }

        public static void AssertPreserved(Snapshot before, GameObject after)
        {
            if (before == null || after == null) throw new ArgumentNullException(before == null ? nameof(before) : nameof(after));
            var errors = new List<string>(); var changedGeometry = new List<string>(); int checks = 0;
            bool sameLoadedRoot = before.loadedRoot != null && before.loadedRoot == after;
            var lookup = after.GetComponentsInChildren<Transform>(true).SelectMany(transform =>
                new Object[] { transform.gameObject }.Concat(transform.GetComponents<Component>())).Where(value => value != null)
                .GroupBy(value => Address(value, after)).ToDictionary(group => group.Key, group => group.First());
            foreach (var item in before.nodes)
            {
                var node = Resolve(item.target, after, lookup, sameLoadedRoot) as GameObject;
                Check(node != null, item.address + ": original GameObject was removed/replaced", errors, ref checks);
                if (node == null) continue;
                Check(node.name == item.name, item.address + ": original object name changed", errors, ref checks);
                bool allowedChartDisable = item.oldChart && item.activeSelf && !node.activeSelf;
                Check(node.activeSelf == item.activeSelf || allowedChartDisable, item.address + ": original activeSelf changed", errors, ref checks);
            }
            foreach (var item in before.components)
            {
                var component = Resolve(item.component, after, lookup, sameLoadedRoot) as Component;
                Check(component != null && component.GetType().AssemblyQualifiedName == item.type,
                    item.address + ": original component was removed/replaced", errors, ref checks);
                if (component == null) continue;
                CompareFields(item.references, ObjectFields(component, after), item.address, after, lookup, sameLoadedRoot, errors, ref checks);
                Check(item.controlSemantics == Semantics(component), item.address + ": original control behavior/settings changed", errors, ref checks);
            }
            foreach (var item in before.typography)
            {
                var text = Resolve(item.text, after, lookup, sameLoadedRoot) as Text;
                if (text == null) continue; // Component preservation already reports the missing text.
                Check(SameReference(item.font, text.font, after, lookup, sameLoadedRoot), item.address + ": original font changed", errors, ref checks);
                Check(item.fontSize == text.fontSize && item.fontStyle == text.fontStyle, item.address + ": original font size/style changed", errors, ref checks);
            }
            foreach (var item in before.contracts)
            {
                var owner = Resolve(item.owner, after, lookup, sameLoadedRoot) as Component;
                Contract current = owner is RaidUIView view ? CaptureView(view, after) : owner is RaidUIRoot hud ? CapturePresenters(hud, after) :
                    owner is Button button ? CaptureButton(button, after) : null;
                Check(current != null, item.address + ": original " + item.kind + " contract owner missing", errors, ref checks);
                if (current != null) CompareFields(item.fields, current.fields, item.address + "/" + item.kind, after, lookup, sameLoadedRoot, errors, ref checks);
            }
            foreach (var item in before.geometry)
            {
                var rect = Resolve(item.rect, after, lookup, sameLoadedRoot) as RectTransform;
                if (rect == null) continue;
                bool equal = Near(item.anchorMin, rect.anchorMin) && Near(item.anchorMax, rect.anchorMax) && Near(item.pivot, rect.pivot) &&
                    Near(item.sizeDelta, rect.sizeDelta) && Near(item.anchoredPosition, rect.anchoredPosition3D) &&
                    Near(item.localScale, rect.localScale) && Quaternion.Angle(item.localRotation, rect.localRotation) < .001f;
                if (item.corePrism) Check(equal, item.address + ": approved combat Prism geometry changed", errors, ref checks);
                else if (!equal) changedGeometry.Add(item.address);
            }
            var originalGraphics = new HashSet<Object>(before.components.Select(item => item.component.value).OfType<Graphic>());
            foreach (var graphic in after.GetComponentsInChildren<Graphic>(true))
                if (sameLoadedRoot && !originalGraphics.Contains(graphic))
                    Check(!graphic.raycastTarget, Address(graphic, after) + ": new artwork intercepts pointer input", errors, ref checks);
            try { RaidUIAuthoring.ValidateScripts(after); checks++; }
            catch (Exception error) { errors.Add(error.Message); }
            before.report = new Report { rootName = after.name, generatedUtc = DateTime.UtcNow.ToString("O"), checks = checks,
                originalObjects = before.nodes.Length, originalComponents = before.components.Length, originalTexts = before.typography.Length,
                passed = errors.Count == 0, errors = errors.ToArray(), permittedGeometryChanges = changedGeometry.ToArray() };
            if (errors.Count != 0) throw new InvalidOperationException("Remaining UI preservation failed: " + string.Join(" | ", errors.Take(12)));
        }

        /// <summary>Caller may save this JSON to its explicit evidence path; this checker performs no file writes.</summary>
        public static string ReportJson(Snapshot snapshot) => JsonUtility.ToJson(snapshot?.report, true);

        private static Contract CaptureView(RaidUIView view, GameObject root)
        {
            var fields = new List<Field> { Value("viewId", view.viewId), Value("referenceCount", view.references.Count.ToString()) };
            for (int i = 0; i < view.references.Count; i++)
            {
                var reference = view.references[i]; string key = "references[" + i + "]";
                fields.Add(Value(key + "/path", reference.path)); fields.Add(Value(key + "/count", reference.count.ToString())); fields.Add(Target(key + "/target", reference.target, root));
            }
            fields.Add(Value("actionCount", view.actions.Count.ToString()));
            for (int i = 0; i < view.actions.Count; i++)
            { fields.Add(Value("actions[" + i + "]/id", view.actions[i].id)); fields.Add(Target("actions[" + i + "]/button", view.actions[i].button, root)); }
            if (view.actions.GroupBy(action => action.id).Any(group => group.Count() > 1)) throw new InvalidOperationException("Duplicate source action ID: " + view.viewId);
            return new Contract { address = Address(view, root), kind = "RaidUIView", owner = Ref(view, root), fields = fields.ToArray() };
        }

        private static Contract CapturePresenters(RaidUIRoot hud, GameObject root)
        {
            var fields = new List<Field> { Value("presenterCount", hud.presenters.Count.ToString()) };
            for (int i = 0; i < hud.presenters.Count; i++)
            {
                var binding = hud.presenters[i]; string key = "presenters[" + i + "]";
                fields.Add(Value(key + "/typeName", binding.typeName)); fields.Add(Target(key + "/embeddedPresenter", binding.embeddedPresenter, root));
                fields.Add(Value(key + "/referenceCount", binding.references.Count.ToString()));
                for (int j = 0; j < binding.references.Count; j++)
                {
                    var reference = binding.references[j]; string field = key + "/references[" + j + "]";
                    fields.Add(Value(field + "/path", reference.path)); fields.Add(Value(field + "/count", reference.count.ToString())); fields.Add(Target(field + "/target", reference.target, root));
                }
            }
            return new Contract { address = Address(hud, root), kind = "RaidUIRoot", owner = Ref(hud, root), fields = fields.ToArray() };
        }

        private static Contract CaptureButton(Button button, GameObject root)
        {
            var fields = new List<Field> { Value("persistentCount", button.onClick.GetPersistentEventCount().ToString()) };
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                string key = "onClick[" + i + "]";
                fields.Add(Target(key + "/target", button.onClick.GetPersistentTarget(i), root));
                fields.Add(Value(key + "/method", button.onClick.GetPersistentMethodName(i)));
                fields.Add(Value(key + "/state", button.onClick.GetPersistentListenerState(i).ToString()));
            }
            using (var serialized = new SerializedObject(button))
            {
                var calls = serialized.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
                if (calls != null)
                {
                    var current = calls.Copy(); var end = current.GetEndProperty();
                    while (current.Next(true) && !SerializedProperty.EqualContents(current, end))
                    {
                        if (current.propertyType == SerializedPropertyType.ObjectReference) fields.Add(Target(current.propertyPath, current.objectReferenceValue, root));
                        else if (current.propertyType == SerializedPropertyType.String) fields.Add(Value(current.propertyPath, current.stringValue));
                        else if (current.propertyType == SerializedPropertyType.Integer || current.propertyType == SerializedPropertyType.Enum) fields.Add(Value(current.propertyPath, current.intValue.ToString()));
                        else if (current.propertyType == SerializedPropertyType.Boolean) fields.Add(Value(current.propertyPath, current.boolValue.ToString()));
                        else if (current.propertyType == SerializedPropertyType.Float) fields.Add(Value(current.propertyPath, current.floatValue.ToString("R", CultureInfo.InvariantCulture)));
                    }
                }
            }
            return new Contract { address = Address(button, root), kind = "Button.onClick", owner = Ref(button, root), fields = fields.ToArray() };
        }

        private static Field[] ObjectFields(Component component, GameObject root)
        {
            // Visual assets may change. Ownership, control targets and presenter object references may not.
            if (component is PrismV4Gauge gauge) return new[] { Target("sourceFill", gauge.sourceFill, root) };
            if (component is Transform || component is Graphic || component is CanvasRenderer) return Array.Empty<Field>();
            var fields = new List<Field>();
            using (var serialized = new SerializedObject(component))
            {
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (component is RaidUIStyle && property.propertyPath == "themeOverride") continue;
                    if (component is Selectable && property.propertyPath.StartsWith("m_SpriteState.", StringComparison.Ordinal)) continue;
                    fields.Add(Target(property.propertyPath, property.objectReferenceValue, root));
                }
            }
            return fields.ToArray();
        }

        private static string Semantics(Component component)
        {
            string value = "";
            // Painted replacement layers may retire an old visual effect without changing its owner or bindings.
            if (component is Behaviour behaviour && !(component is BaseMeshEffect)) value = "enabled=" + behaviour.enabled;
            if (component is Graphic graphic) value += ";raycast=" + graphic.raycastTarget;
            if (component is Selectable selectable) value += ";interactable=" + selectable.interactable + ";navigation=" + selectable.navigation.mode + ";wrap=" + selectable.navigation.wrapAround;
            if (component is Toggle toggle) value += ";isOn=" + toggle.isOn + ";transition=" + toggle.toggleTransition;
            if (component is Slider slider) value += ";range=" + F(slider.minValue) + "," + F(slider.maxValue) + ";whole=" + slider.wholeNumbers + ";value=" + F(slider.value) + ";direction=" + slider.direction;
            if (component is InputField input) value += ";text=" + input.text + ";content=" + input.contentType + ";line=" + input.lineType + ";limit=" + input.characterLimit + ";readOnly=" + input.readOnly;
            if (component is Dropdown dropdown) value += ";selected=" + dropdown.value + ";options=" + string.Join("|", dropdown.options.Select(option => option.text));
            if (component is ScrollRect scroll) value += ";horizontal=" + scroll.horizontal + ";vertical=" + scroll.vertical + ";movement=" + scroll.movementType + ";inertia=" + scroll.inertia;
            if (component is CanvasGroup group) value += ";interactable=" + group.interactable + ";blocks=" + group.blocksRaycasts + ";ignoreParents=" + group.ignoreParentGroups + ";alpha=" + F(group.alpha);
            if (component is Image image && (image is PrismV4Gauge || image.type == Image.Type.Filled))
                value += ";fill=" + image.type + "," + image.fillMethod + "," + image.fillOrigin + "," + image.fillClockwise + "," + F(image.fillAmount);
            if (component is PrismV4Gauge prism)
                value += ";prism=" + prism.direction + "," + F(prism.pointWidth) + "," + F(prism.previewValue) + "," +
                    F(prism.innerBounds.x) + "," + F(prism.innerBounds.y) + "," + F(prism.innerBounds.width) + "," + F(prism.innerBounds.height);
            return value;
        }

        private static void CompareFields(Field[] before, Field[] after, string context, GameObject root, Dictionary<string, Object> lookup, bool sameRoot, List<string> errors, ref int checks)
        {
            Check(before.Length == after.Length, context + ": reference/contract field count changed", errors, ref checks);
            var current = after.GroupBy(field => field.key).ToDictionary(group => group.Key, group => group.First());
            foreach (var field in before)
            {
                if (!current.TryGetValue(field.key, out var candidate)) { Check(false, context + ": field missing " + field.key, errors, ref checks); continue; }
                if (field.target != null)
                    Check(SameReference(field.target, candidate.target?.value, root, lookup, sameRoot), context + "/" + field.key + ": original target changed", errors, ref checks);
                else Check(field.value == candidate.value, context + "/" + field.key + ": original value changed", errors, ref checks);
            }
        }

        private static bool SameReference(Reference expected, Object current, GameObject root, Dictionary<string, Object> lookup, bool sameRoot)
        {
            if (expected.isNull) return current == null;
            if (sameRoot) return expected.value != null && expected.value == current;
            return expected.address == Address(current, root);
        }

        private static Object Resolve(Reference reference, GameObject root, Dictionary<string, Object> lookup, bool sameRoot)
        {
            if (reference == null || reference.isNull) return null;
            if (sameRoot)
            {
                Object value = reference.value;
                Transform transform = value is GameObject node ? node.transform : value is Component component ? component.transform : null;
                return value != null && transform != null && (transform == root.transform || transform.IsChildOf(root.transform)) ? value : null;
            }
            return lookup.TryGetValue(reference.address, out var found) ? found : null;
        }

        private static Reference Ref(Object value, GameObject root) => new Reference { value = value, isNull = value == null, address = Address(value, root) };
        private static Field Value(string key, string value) => new Field { key = key, value = value ?? "" };
        private static Field Target(string key, Object value, GameObject root) => new Field { key = key, target = Ref(value, root) };
        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static bool Near(Vector3 first, Vector3 second) => (first - second).sqrMagnitude < .000001f;
        private static void Check(bool condition, string message, List<string> errors, ref int checks) { checks++; if (!condition) errors.Add(message); }

        private static string Address(Object value, GameObject root)
        {
            if (value == null) return "<null>";
            Transform transform = value is GameObject node ? node.transform : value is Component component ? component.transform : null;
            if (transform != null && (transform == root.transform || transform.IsChildOf(root.transform)))
            {
                string path = PathOf(transform, root.transform);
                if (value is GameObject) return path + "|GameObject";
                int index = Array.IndexOf(transform.GetComponents<Component>().Where(item => item != null && item.GetType() == value.GetType()).ToArray(), value);
                return path + "|" + value.GetType().FullName + "#" + index;
            }
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long localId))
                return "asset:" + guid + ":" + localId + "|" + value.GetType().FullName;
            return "external:" + value.GetType().FullName + ":" + value.name;
        }

        private static string PathOf(Transform transform, Transform root)
        {
            if (transform == root) return "$";
            var parts = new List<string>();
            for (Transform current = transform; current != null && current != root; current = current.parent)
            {
                int ordinal = 0;
                if (current.parent != null)
                    for (int i = 0; i < current.GetSiblingIndex(); i++) if (current.parent.GetChild(i).name == current.name) ordinal++;
                parts.Add(Uri.EscapeDataString(current.name) + "[" + ordinal + "]");
            }
            parts.Reverse(); return "$/" + string.Join("/", parts);
        }
    }
}
#endif
