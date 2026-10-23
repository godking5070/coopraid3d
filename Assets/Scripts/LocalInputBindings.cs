using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace RaidRogue
{
    // One runtime action map replaces the scattered fixed-key polling. Mouse targeting/orbit stay unchanged.
    public static class LocalInputBindings
    {
        private static InputActionMap _map;
        private static LocalKeyBinding[] _bindings;
        private static readonly HashSet<Key> Quarantined = new HashSet<Key>();
        private static int _blockedThrough = -1;
        public static int Revision { get; private set; }
        public static bool Capturing { get; set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        { _map?.Dispose(); _map = null; _bindings = null; Quarantined.Clear(); _blockedThrough = -1; Capturing = false; Revision = 0; }
        public static void Configure(LocalKeyBinding[] bindings)
        {
            QuarantineHeld(); _map?.Dispose(); _map = new InputActionMap("RaidLocal");
            _bindings = LocalBindingRules.Repair(bindings, true);
            foreach (var b in _bindings)
            {
                var action = _map.AddAction(b.action, InputActionType.Button);
                int row = System.Array.IndexOf(_bindings, b);
                if (LocalBindingRules.Conflicts(_bindings, row, 0, b.primary).Count == 0) action.AddBinding("<Keyboard>/" + LocalBindingRules.ControlName(b.primary));
                if (b.secondary.Length > 0 && LocalBindingRules.Conflicts(_bindings, row, 1, b.secondary).Count == 0) action.AddBinding("<Keyboard>/" + LocalBindingRules.ControlName(b.secondary));
            }
            _map.Enable(); Revision++;
        }
        private static void Ensure() { if (_map == null) Configure(LocalSettingsRuntime.Instance != null ? LocalSettingsRuntime.Instance.Applied.bindings : LocalBindingRules.Defaults()); }
        public static void QuarantineHeld()
        {
            _blockedThrough = Time.frameCount + 1;
            if (Keyboard.current != null) foreach (var key in Keyboard.current.allKeys) if (key.isPressed) Quarantined.Add(key.keyCode);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            LocalGameplayInput.SuppressThisFrame(); LocalGameplayInput.SuppressPointerThisFrame();
        }
        public static void Tick()
        {
            if (Keyboard.current == null) { Quarantined.Clear(); return; }
            Quarantined.RemoveWhere(k => !Keyboard.current[k].isPressed);
        }
        public static bool Modified => Keyboard.current != null && (Keyboard.current.shiftKey.isPressed || Keyboard.current.ctrlKey.isPressed || Keyboard.current.altKey.isPressed || Keyboard.current.leftMetaKey.isPressed || Keyboard.current.rightMetaKey.isPressed);
        private static bool Allowed => Application.isFocused && !Capturing && Time.frameCount > _blockedThrough && !Modified;
        public static bool Pressed(string id)
        {
            Ensure(); if (!Allowed) return false;
            var action = _map.FindAction(id); if (action == null || !action.WasPressedThisFrame()) return false;
            foreach (var control in action.controls) if (control is KeyControl key && key.wasPressedThisFrame && !Quarantined.Contains(key.keyCode)) return true;
            return false;
        }
        public static bool Held(string id)
        {
            Ensure(); if (!Allowed) return false;
            var action = _map.FindAction(id); if (action == null) return false;
            foreach (var control in action.controls) if (control is KeyControl key && key.isPressed && !Quarantined.Contains(key.keyCode)) return true;
            return false;
        }
        public static Vector2 Movement => Vector2.ClampMagnitude(new Vector2((Held("Right") ? 1 : 0) - (Held("Left") ? 1 : 0), (Held("Forward") ? 1 : 0) - (Held("Back") ? 1 : 0)), 1);
        public static string Label(string id, bool alternatives = false)
        {
            if (Application.isPlaying) Ensure();
            var b = System.Array.Find(Application.isPlaying ? _bindings : LocalBindingRules.Defaults(), v => v.action == id); if (b == null) return "—";
            return KeyLabel(b.primary) + (alternatives && b.secondary.Length > 0 ? " / " + KeyLabel(b.secondary) : "");
        }
        public static string KeyLabel(string key)
        {
            if (string.IsNullOrEmpty(key)) return "—";
            switch (key)
            {
                case "space": return "SPACE"; case "upArrow": return "↑"; case "downArrow": return "↓";
                case "leftArrow": return "←"; case "rightArrow": return "→"; case "pageUp": return "PGUP";
                case "pageDown": return "PGDN"; case "insert": return "INS"; case "delete": return "DEL";
                case "backspace": return "BKSP";
            }
            if (key.StartsWith("numpad")) return "NUM " + key.Substring(6).Replace("Plus", "+").Replace("Minus", "−").Replace("Multiply", "×").Replace("Divide", "÷").Replace("Period", ".");
            var control = Keyboard.current?.TryGetChildControl<KeyControl>(LocalBindingRules.ControlName(key));
            return control != null ? control.displayName.ToUpperInvariant() : key.Replace("digit", "").Replace("numpad", "NUM ").Replace("Arrow", "").ToUpperInvariant();
        }
        public static string PartyLabels(int count = 4)
        { var names = new List<string>(); for (int i = 1; i <= count; i++) names.Add(Label("Party" + i)); return string.Join(" / ", names); }
        public static string MovementLabels => string.Join("/", new[] { Label("Forward"), Label("Left"), Label("Back"), Label("Right") });
        public static string SkillText(string text)
        {
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\[([1-5])\]", m => "[" + Label("Skill" + m.Groups[1].Value) + "]");
            return System.Text.RegularExpressions.Regex.Replace(text.Replace("WASD", MovementLabels), @"\{key:(\w+)\}", m => Label(m.Groups[1].Value));
        }
    }
    // Retains the original template, so swapping number keys never recursively rewrites other labels.
    public sealed class LocalShortcutText : MonoBehaviour
    {
        private UnityEngine.UI.Text _text;
        private string _template;
        private int _revision = -1;
        public static void Set(UnityEngine.UI.Text text, string template)
        {
            if (text == null) return;
            if (!Application.isPlaying) { text.text = LocalInputBindings.SkillText(template); return; }
            var label = text.GetComponent<LocalShortcutText>() ?? text.gameObject.AddComponent<LocalShortcutText>();
            label._text = text; label._template = template; label._revision = -1; label.Refresh();
        }
        private void LateUpdate() => Refresh();
        private void Refresh()
        {
            if (_text == null || _template == null || _revision == LocalInputBindings.Revision) return;
            _text.text = LocalInputBindings.SkillText(_template); _revision = LocalInputBindings.Revision;
        }
    }
}
