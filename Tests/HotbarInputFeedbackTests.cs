using System;
using System.Collections.Generic;
using System.Linq;
using RaidRogue;
using UnityEngine;
using UnityEngine.UI;

namespace UnityEngine
{
    public class Object
    {
        public static void Destroy(Object value) { if (value is GameObject go) go.destroyed = true; }
    }
    public struct Vector2
    {
        public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(); public static Vector2 one => new Vector2(1, 1);
    }
    public struct Color { public Color(float r, float g, float b, float a) { } }
    public class Transform { public Transform parent; public void SetParent(Transform value, bool keepWorld) { parent = value; } }
    public sealed class RectTransform : Transform { public Vector2 anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta; }
    public sealed class GameObject : Object
    {
        public static List<GameObject> created = new List<GameObject>();
        public string name; public bool activeSelf = true, destroyed; public Transform transform;
        public Image image;
        public GameObject(string name, params Type[] types)
        {
            this.name = name; transform = new RectTransform();
            if (types.Contains(typeof(Image))) image = new Image { gameObject = this };
            created.Add(this);
        }
        public T GetComponent<T>() where T : class => image as T;
    }
    public static class Time { public static float unscaledTime; }
}
namespace UnityEngine.UI
{
    public sealed class Image : UnityEngine.Object { public bool enabled = true, raycastTarget = true; public Color color; public GameObject gameObject; }
    public sealed class ClickEvent
    {
        readonly List<Action> callbacks = new List<Action>();
        public int Count => callbacks.Count;
        public void AddListener(Action action) { callbacks.Add(action); }
        public void RemoveListener(Action action) { callbacks.RemoveAll(item => item == action); }
        public void Invoke() { foreach (var callback in callbacks.ToArray()) callback(); }
    }
    public sealed class Button
    {
        public GameObject gameObject = new GameObject("Original button");
        public Transform transform => gameObject.transform;
        public ClickEvent onClick = new ClickEvent(); public AstellaSkillSlotSkin skin;
        public T GetComponent<T>() where T : class => skin as T;
    }
}
namespace RaidRogue
{
    public sealed class AstellaSkillSlotSkin { public bool enabled = true, animateKeyboard = true; public Image frame; public Button button; }
    public sealed class CharacterActor { public object Definition = new object(); public bool local = true; public int health = 100; }
    public enum CharacterSlot { Gcd1 }
    public static class CharacterSlots { public static string Binding(object definition, CharacterSlot slot) => "Skill1"; }
    public static class RaidCombatAuthority { public static bool Local(CharacterActor actor) => actor.local; }
    public static class LocalGameplayInput { public static bool Blocked; }
    public static class LocalInputBindings
    {
        public static bool pressed, held;
        public static bool Pressed(string key) => pressed;
        public static bool Held(string key) => held;
    }
    /* ACTUAL_FEEDBACK */
}
static class HotbarInputFeedbackTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static GameObject[] Edges() => GameObject.created.Where(go => go.name == "KeyboardInputEdge").ToArray();
    static void Main()
    {
        try { Run(); }
        catch (Exception error) { Console.Error.WriteLine("FAIL: " + error.Message); Environment.ExitCode = 1; }
    }
    static void Run()
    {
        var feedback = new HotbarInputFeedback(); var actor = new CharacterActor(); var button = new Button();
        var frameObject = new GameObject("Authored frame", typeof(Image));
        button.skin = new AstellaSkillSlotSkin { button = button, frame = frameObject.image };
        int actions = 0; button.onClick.AddListener(() => actions++);
        LocalInputBindings.held = LocalInputBindings.pressed = true;
        feedback.Tick(button, actor, CharacterSlot.Gcd1);
        Check(Edges().Length == 0 && button.onClick.Count == 1, "authored keyboard skin prevents procedural edges and extra click listeners");
        button.onClick.Invoke(); Check(actions == 1 && actor.health == 100, "authored feedback ownership preserves the original gameplay callback");

        button.skin.enabled = false; feedback.Tick(button, actor, CharacterSlot.Gcd1);
        var originalEdges = Edges();
        Check(originalEdges.Length == 4 && originalEdges.All(go => go.image.enabled) && button.onClick.Count == 2, "disabled authored skin uses the original edge fallback");
        button.onClick.Invoke(); Check(actions == 2, "fallback adds only its acknowledgement without duplicating gameplay");
        button.skin.enabled = true; feedback.Tick(button, actor, CharacterSlot.Gcd1);
        Check(originalEdges.All(go => go.destroyed && !go.image.enabled) && button.onClick.Count == 1, "transition to authored skin immediately hides and retires old edges and only its own listener");
        feedback.Tick(button, actor, CharacterSlot.Gcd1);
        Check(Edges().Length == 4 && button.onClick.Count == 1, "authored owner remains stable across subsequent ticks");
        frameObject.activeSelf = false; feedback.Tick(button, actor, CharacterSlot.Gcd1);
        Check(Edges().Length == 4 && button.onClick.Count == 1, "hidden menu visuals do not cause procedural edges to be recreated");
        frameObject.activeSelf = true;

        button.skin.animateKeyboard = false; feedback.Tick(button, actor, CharacterSlot.Gcd1);
        Check(Edges().Length == 8 && button.onClick.Count == 2, "pointer-only skin retains keyboard edge fallback");
        button.skin.animateKeyboard = true; button.skin.frame = null; feedback.Tick(button, actor, CharacterSlot.Gcd1);
        Check(Edges().Length == 8 && button.onClick.Count == 2, "missing authored frame cannot claim keyboard feedback ownership");
        button.skin.frame = frameObject.image; frameObject.image.enabled = false; feedback.Tick(button, actor, CharacterSlot.Gcd1);
        Check(button.onClick.Count == 2, "disabled authored frame preserves fallback feedback");
        frameObject.image.enabled = true; feedback.Tick(button, actor, CharacterSlot.Gcd1);
        Check(Edges().All(go => go.destroyed && !go.image.enabled) && button.onClick.Count == 1, "restored frame removes its fallback without changing the original callback");

        var other = new Button(); int otherActions = 0; other.onClick.AddListener(() => otherActions++);
        button.skin.button = other; feedback.Tick(button, actor, CharacterSlot.Gcd1);
        Check(button.onClick.Count == 2, "skin bound to another Button cannot suppress this Button's fallback");
        feedback.Tick(other, actor, CharacterSlot.Gcd1);
        Check(button.onClick.Count == 1 && other.onClick.Count == 2, "switching buttons removes only the old feedback subscription");
        button.onClick.Invoke(); other.onClick.Invoke();
        Check(actions == 3 && otherActions == 1, "both original gameplay callbacks survive feedback ownership changes");
        feedback.Tick(null, actor, CharacterSlot.Gcd1);
        Check(other.onClick.Count == 1 && Edges().All(go => go.destroyed && !go.image.enabled), "null target retires current edges and subscription");

        LocalInputBindings.held = LocalInputBindings.pressed = false;
        feedback.Tick(other, actor, CharacterSlot.Gcd1);
        LocalGameplayInput.Blocked = true; other.onClick.Invoke(); feedback.Tick(other, actor, CharacterSlot.Gcd1);
        Check(Edges().Where(go => !go.destroyed).All(go => !go.image.enabled), "blocked input does not display a queued acknowledgement");
        LocalGameplayInput.Blocked = false; feedback.Tick(other, actor, CharacterSlot.Gcd1);
        Check(Edges().Where(go => !go.destroyed).All(go => !go.image.enabled) && actor.health == 100, "unblocking input cannot replay old feedback or simulate gameplay");
        Console.WriteLine("PASS: " + checks + " actual hotbar feedback ownership/cleanup checks; no Unity rendering or gameplay execution.");
    }
}
