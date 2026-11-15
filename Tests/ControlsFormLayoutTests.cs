using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RaidRogue;
using UnityEngine;
using UnityEngine.UI;

namespace UnityEngine
{
    public class Component
    {
        public Transform transform;
        public T GetComponent<T>() where T : Component => transform.Components.TryGetValue(typeof(T), out var value) ? (T)value : null;
    }
    public class MonoBehaviour : Component { }
    public class Transform : Component, IEnumerable
    {
        internal readonly Dictionary<Type, Component> Components = new Dictionary<Type, Component>();
        private readonly List<Transform> _children = new List<Transform>();
        public Transform() { transform = this; }
        public void Add(Transform child) => _children.Add(child);
        public void Attach(Component component) { component.transform = this; Components[component.GetType()] = component; }
        public IEnumerator GetEnumerator() => _children.GetEnumerator();
    }
    public sealed class RectTransform : Transform { public Vector2 anchoredPosition; }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
    }
}
namespace UnityEngine.UI
{
    public sealed class Button : Component { }
    public sealed class Text : Component { }
}
internal static class ControlsLayout
{
    public static void Extend(RectTransform parent, LocalKeyBinding[] bindings) => ExtendControlsForm(parent, bindings);
    /* ACTUAL_CONTROLS_FORM_EXTENSION */
}
internal static class ControlsFormLayoutTests
{
    private static int _checks;
    private static void Check(bool condition, string message) { _checks++; if (!condition) throw new Exception(message); }
    private static RectTransform Form()
    {
        var parent = new RectTransform(); parent.Attach(new RaidUIForm()); return parent;
    }
    private static RectTransform Register<T>(RectTransform parent, string key, float x, float y) where T : Component, new()
    {
        var rect = new RectTransform { anchoredPosition = new Vector2(x, y) }; parent.Add(rect);
        var component = new T(); rect.Attach(component); parent.GetComponent<RaidUIForm>().Remember(key, component); return rect;
    }
    private static RectTransform[] Row(RectTransform parent, string action, float y)
    {
        return new[] {
            Register<Text>(parent, "label.binding." + action, -230, y + 16),
            Register<Button>(parent, "button.binding." + action + ".0", 35, y),
            Register<Button>(parent, "button.binding." + action + ".1", 225, y),
            Register<Button>(parent, "button.clear." + action, 355, y)
        };
    }
    // BuildControls registers these exact controls after extending an older saved form.
    private static void RegisterNewRows(RectTransform parent)
    {
        var form = parent.GetComponent<RaidUIForm>();
        if (form.Find<Button>("button.row.이동 방식") == null)
            Register<Button>(parent, "button.row.이동 방식", 205, -25);
        if (form.Find<Button>("button.binding.Interact.0") == null) Row(parent, "Interact", -433);
    }
    private static string Positions(RectTransform parent) => string.Join("|", parent.Cast<RectTransform>().Select(r => r.anchoredPosition.x + ":" + r.anchoredPosition.y));
    private static void Reopen(RectTransform parent, LocalKeyBinding[] bindings)
    {
        string before = Positions(parent); int controls = parent.GetComponent<RaidUIForm>().controls.Count;
        for (int i = 0; i < 20; i++) { ControlsLayout.Extend(parent, bindings); RegisterNewRows(parent); }
        Check(before == Positions(parent), "reopening must not move authored or inserted controls");
        Check(controls == parent.GetComponent<RaidUIForm>().controls.Count, "reopening must reuse controls");
    }
    private static void LegacyForm()
    {
        var parent = Form(); var bindings = LocalBindingRules.Defaults();
        string keys = string.Join("|", bindings.Select(b => b.action + ":" + b.primary + ":" + b.secondary));
        var potion = Row(parent, "Potion", -300); var first = Row(parent, "Skill1", -343); var second = Row(parent, "Skill2", -386);
        var hint = Register<Text>(parent, "label.hint", 17, -30);
        first[2].anchoredPosition.x = 249; second[0].anchoredPosition.y += 7; // Individual authored adjustments survive.
        ControlsLayout.Extend(parent, bindings); RegisterNewRows(parent);
        Check(potion[1].anchoredPosition.y == -390, "rows before G only make room for movement preferences");
        Check(first[1].anchoredPosition.y == -476 && second[1].anchoredPosition.y == -519, "old skill rows make exactly one row of room for G");
        Check(first[2].anchoredPosition.x == 249 && second[0].anchoredPosition.y == -496, "preserve individual authored offsets");
        Check(hint.anchoredPosition.x == 17 && hint.anchoredPosition.y == -120, "unrelated authored content only receives the header insertion");
        var interaction = parent.GetComponent<RaidUIForm>().Find<Button>("button.binding.Interact.0");
        Check(((RectTransform)interaction.transform).anchoredPosition.y - first[1].anchoredPosition.y == 43, "G must not cover the former Skill1 row");
        Check(keys == string.Join("|", bindings.Select(b => b.action + ":" + b.primary + ":" + b.secondary)), "layout migration never changes input bindings");
        Reopen(parent, bindings);
    }
    private static void PartiallyExtendedForms()
    {
        var bindings = LocalBindingRules.Defaults();
        var parent = Form(); var first = Row(parent, "Skill1", -433);
        Register<Button>(parent, "button.row.이동 방식", 205, -25);
        ControlsLayout.Extend(parent, bindings); RegisterNewRows(parent);
        Check(first[1].anchoredPosition.y == -476, "existing movement row prevents a second header shift");
        Reopen(parent, bindings);

        parent = Form(); var interaction = Row(parent, "Interact", -343); first = Row(parent, "Skill1", -386);
        ControlsLayout.Extend(parent, bindings); RegisterNewRows(parent);
        Check(interaction[1].anchoredPosition.y == -433 && first[1].anchoredPosition.y == -476, "existing G row prevents another row insertion");
        Reopen(parent, bindings);
    }
    private static void NewAndSharedControls()
    {
        var parent = Form(); var bindings = LocalBindingRules.Defaults();
        ControlsLayout.Extend(parent, bindings); RegisterNewRows(parent); Reopen(parent, bindings);
        parent = Form(); var shared = Register<Button>(parent, "button.binding.Skill1.0", 35, -343);
        parent.GetComponent<RaidUIForm>().Remember("button.binding.Skill1.1", shared.GetComponent<Button>());
        ControlsLayout.Extend(parent, bindings);
        Check(shared.anchoredPosition.y == -476, "a shared authored control moves once");
        var noForm = new RectTransform(); var untouched = new RectTransform { anchoredPosition = new Vector2(3, 5) }; noForm.Add(untouched);
        ControlsLayout.Extend(noForm, bindings);
        Check(untouched.anchoredPosition.x == 3 && untouched.anchoredPosition.y == 5, "unregistered custom forms remain untouched");
    }
    public static int Main()
    {
        LegacyForm(); PartiallyExtendedForms(); NewAndSharedControls();
        Console.WriteLine("PASS: " + _checks + " actual controls-form extension and registry lifecycle checks (no Unity UI execution)."); return 0;
    }
}
