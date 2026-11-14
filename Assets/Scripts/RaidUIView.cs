using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace RaidRogue
{
    // Binding keys are code contracts. Object names, parents and RectTransforms are presentation only.
    public sealed class RaidUIView : MonoBehaviour
    {
        [Serializable] public sealed class Reference
        {
            public string path;
            public UnityEngine.Object target;
            public int count = -1;
        }
        [Serializable] public sealed class ActionReference { public string id; public Button button; }
        public string viewId;
        [Tooltip("Generated field bindings. Rename/reparent the referenced objects freely; retain binding keys.")]
        public List<Reference> references = new List<Reference>();
        public List<ActionReference> actions = new List<ActionReference>();
        [NonSerialized] public MonoBehaviour owner;
        private readonly Dictionary<string, UnityAction> _listeners = new Dictionary<string, UnityAction>();
        private static readonly Dictionary<MonoBehaviour, RaidUIView> Owners = new Dictionary<MonoBehaviour, RaidUIView>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() { Owners.Clear(); }
        internal static void ReleaseOwners() { Owners.Clear(); }

        public static bool Attach(MonoBehaviour owner, string id, Transform parent)
        {
            if (Owners.TryGetValue(owner, out var bound) && bound != null) return true;
            RaidUIView view = null;
            // Only once per presenter, never a frame-time hierarchy search.
            foreach (var candidate in parent.GetComponentsInChildren<RaidUIView>(true))
                if (candidate.viewId == id && candidate.owner == null) { view = candidate; break; }
            if (view == null)
            {
                var prefab = RaidUIPresentation.Settings?.View(id);
                if (prefab != null) view = Instantiate(prefab, parent, false);
            }
            if (view == null) return false;
            view.owner = owner; Owners[owner] = view;
            view.Restore(owner);
            return true;
        }

        public static RectTransform Begin(MonoBehaviour owner, string id, Transform parent)
        {
            var root = new GameObject(id, typeof(RectTransform), typeof(RaidUIView));
            var rect = (RectTransform)root.transform; rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var view = root.GetComponent<RaidUIView>(); view.viewId = id; view.owner = owner; Owners[owner] = view;
            return rect;
        }

        public static RaidUIView For(MonoBehaviour owner) => owner != null && Owners.TryGetValue(owner, out var view) ? view : null;

        public static Button Bind(MonoBehaviour owner, string id, UnityAction callback, Button created = null)
        {
            var view = For(owner);
            if (view == null) { created?.onClick.AddListener(callback); return created; }
            var entry = view.actions.Find(x => x.id == id);
            if (entry == null && created != null) { entry = new ActionReference { id = id, button = created }; view.actions.Add(entry); }
            if (entry?.button == null) { Debug.LogWarning($"UI action missing: {view.viewId}/{id}", view); return null; }
            if (view._listeners.TryGetValue(id, out var previous)) entry.button.onClick.RemoveListener(previous);
            view._listeners[id] = callback; entry.button.onClick.AddListener(callback);
            return entry.button;
        }

        public void Capture(MonoBehaviour source)
        {
            references.Clear(); owner = source;
            foreach (var field in Fields(source.GetType())) CaptureValue(field.GetValue(source), field.FieldType, field.Name, 0);
        }
        private static FieldInfo[] Fields(Type type) => type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private void CaptureValue(object value, Type type, string path, int depth)
        {
            if (value == null || depth > 5) return;
            if (value is UnityEngine.Object obj)
            {
                // Reflection boxes Unity's unassigned/destroyed references as non-null CLR
                // objects. Check Unity's overloaded equality before any native property access.
                if (obj == null) return;
                Transform tr = obj is GameObject go ? go.transform : obj is Component component ? component.transform : null;
                if (tr != null && (tr == transform || tr.IsChildOf(transform))) references.Add(new Reference { path = path, target = obj });
                return;
            }
            if (value is IDictionary dictionary)
            {
                Type itemType = type.GetGenericArguments()[1];
                foreach (DictionaryEntry item in dictionary) CaptureValue(item.Value, itemType, path + "/{" + item.Key + "}", depth + 1);
                return;
            }
            if (value is IList list && ContainsUI(type))
            {
                references.Add(new Reference { path = path, count = list.Count });
                Type itemType = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
                for (int i = 0; i < list.Count; i++) CaptureValue(list[i], itemType, path + "/[" + i + "]", depth + 1);
                return;
            }
            if (type.IsClass && type.Namespace == "RaidRogue" && type.IsSerializable)
                foreach (var field in Fields(type)) CaptureValue(field.GetValue(value), field.FieldType, path + "/" + field.Name, depth + 1);
        }
        private static bool ContainsUI(Type type)
        {
            if (type.IsArray) return ContainsUI(type.GetElementType());
            if (type.IsGenericType) return ContainsUI(type.GetGenericArguments()[0]);
            return typeof(Graphic).IsAssignableFrom(type) || typeof(Selectable).IsAssignableFrom(type) || type == typeof(GameObject) || type == typeof(HotbarSlotView) || type == typeof(PartyMemberRowUI) || type.Name == "EnemyRowUI";
        }
        public void Restore(MonoBehaviour destination)
        {
            foreach (var entry in references)
            {
                try { Set(destination, destination.GetType(), entry.path.Split('/'), 0, entry); }
                catch (Exception e) { Debug.LogError($"UI binding {viewId}/{entry.path}: {e.Message}", this); }
            }
        }
        private static object Set(object current, Type type, string[] path, int index, Reference entry)
        {
            if (index == path.Length)
            {
                if (entry.count < 0) return entry.target;
                if (type.IsArray) return Array.CreateInstance(type.GetElementType(), entry.count);
                var list = current as IList ?? (IList)Activator.CreateInstance(type); list.Clear();
                for (int i = 0; i < entry.count; i++) list.Add(null);
                return list;
            }
            current = current ?? Activator.CreateInstance(type, true);
            string key = path[index];
            if (key.StartsWith("["))
            {
                int i = int.Parse(key.Substring(1, key.Length - 2)); var list = (IList)current;
                Type itemType = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
                list[i] = Set(list[i], itemType, path, index + 1, entry);
            }
            else if (key.StartsWith("{"))
            {
                var dictionary = (IDictionary)current; Type[] args = type.GetGenericArguments(); string text = key.Substring(1, key.Length - 2);
                object dictKey = args[0].IsEnum ? Enum.Parse(args[0], text) : (object)text;
                dictionary[dictKey] = Set(dictionary[dictKey], args[1], path, index + 1, entry);
            }
            else
            {
                var field = type.GetField(key, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null) throw new InvalidOperationException("Unknown presenter field " + key);
                var updated = Set(field.GetValue(current), field.FieldType, path, index + 1, entry);
                if (field.IsInitOnly && updated is Array replacement && field.GetValue(current) is Array existing)
                    Array.Copy(replacement, existing, Math.Min(replacement.Length, existing.Length));
                else field.SetValue(current, updated);
            }
            return current;
        }
        private void OnDestroy()
        {
            if (owner != null && Owners.TryGetValue(owner, out var current) && current == this) Owners.Remove(owner);
        }
    }
}
