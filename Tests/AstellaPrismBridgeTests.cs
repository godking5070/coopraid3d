using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Astella.PrismV4;
using RaidRogue;

namespace UnityEngine
{
    public sealed class DefaultExecutionOrder : Attribute { public int order; public DefaultExecutionOrder(int value) { order = value; } }
    public class Sprite { public string name; public Sprite(string value) { name = value; } }
    public class Material { }
    public sealed class GameObject
    {
        public bool activeSelf = true;
        public Transform transform;
        public GameObject() { transform = new Transform { gameObject = this }; }
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public void SetActive(bool value) { activeSelf = value; }
    }
    public sealed class Transform { public GameObject gameObject; public Transform parent; }
    public class MonoBehaviour
    {
        public GameObject gameObject = new GameObject(); public bool enabled = true;
        public Transform transform => gameObject.transform;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
    }
    public static class Mathf
    {
        public static float Clamp01(float x) => Math.Max(0, Math.Min(1, x));
        public static bool Approximately(float a, float b) => Math.Abs(a - b) < .000001f;
        public static int FloorToInt(float x) => (int)Math.Floor(x);
    }
}
namespace UnityEngine.UI
{
    public class Image : MonoBehaviour { public float fillAmount; public Sprite sprite; public Material material; }
    public sealed class Text : MonoBehaviour { public string text = ""; }
    // No action/event API is provided. The actual bridge compiles using display-only reads.
    public sealed class Button : MonoBehaviour { public bool interactable = true; public bool IsInteractable() => interactable; }
}
namespace Astella.PrismV4
{
    public sealed class PrismV4Gauge : Image
    {
        public Image sourceFill; public float previewValue = 1; public int writes;
        public float Ratio => sourceFill != null ? sourceFill.fillAmount : previewValue;
        public void Bind(Image source) { sourceFill = source; }
        public void SetValue(float value) { previewValue = value; writes++; }
    }
    public sealed class PrismV4Bindings : MonoBehaviour
    {
        public PrismV4Gauge healthFill, jobResourceFill, bossFill, castFill, ultimateChargeFill;
        public Text nameText, valueText, resourceText, keyText, cooldownText, castNameText, castTimeText;
        public Image artwork, frame, cooldownShade, disabledOverlay, selectedOverlay, deadOverlay;
        public Sprite criticalHealthSprite; public Button button;
        public GameObject readyGlow, unavailableOverlay;
    }
}
namespace RaidRogue
{
    public enum CharacterSlot { Gcd1, Ultimate }
    public sealed class AstellaOrnateHUD : MonoBehaviour
    {
        public sealed class SlotBinding
        {
            public Button button; public bool sharedResurrection; public CharacterSlot slot;
            public Text keycap, cooldown, sourceLabel; public Image artwork, cooldownShade; public Sprite fallbackArt;
        }
        public GameObject playerRoot = new GameObject(), bossRoot = new GameObject(), castRoot = new GameObject();
        public Image playerHpFill, bossHpFill, castFill;
        public Text playerHpText, bossTitleText, bossPercentText, stageText, castNameText, castTimeText;
        public SlotBinding[] slots = Array.Empty<SlotBinding>();
    }
    public sealed class JobResourceDefinition { public string id, name; public bool integer; }
    public sealed class Route { public string id; }
    public sealed class CharacterDefinition { public string id = "job.example"; public bool tank; public JobResourceDefinition[] resources; public Route[] routes = Array.Empty<Route>(); }
    public sealed class CharacterActor
    {
        public CharacterDefinition Definition; public string RouteId;
        public Dictionary<string, float> current = new Dictionary<string, float>(), maximum = new Dictionary<string, float>();
        public float Resource(string id) => current.TryGetValue(id, out float value) ? value : 0;
        public float ResourceMaximum(string id) => maximum.TryGetValue(id, out float value) ? value : 0;
    }
    public sealed class Tank { public int index = 2; }
    public static class NineKeyTankKit { public static bool Supports(CharacterDefinition d) => d.tank; }
    public static class CharacterRosterData { public static Tank Find(string id) => new Tank(); }
    public static class NineKeyTankData { public static string Heat(Tank tank) => "heat"; }
    public sealed class NineKeyDamageKit
    {
        public bool Active; public float scale = 1, capacityScale = 1;
        public float DisplayResource(string id, float value) => value * scale;
        public float DisplayMaximum(string id, float value) => value * capacityScale;
    }
    public sealed class AstellaClassJobHUD : MonoBehaviour
    {
        public sealed class View { public GameObject root; public string jobId; }
        public View[] views = Array.Empty<View>();
        private CharacterActor _actor; private NineKeyDamageKit _damage;
        /* ACTUAL_JOB_PROPERTIES */
        public void SetActor(CharacterActor actor, NineKeyDamageKit damage = null) { _actor = actor; _damage = damage; }
        public void Sample() => LateUpdate();
        public void Disable() => OnDisable();
        void Resolve() { }
        static void SetVisible(GameObject root, bool visible) { if (root != null) root.SetActive(visible); }
        static void UpdateView(View view, JobResourceDefinition resource, float current, float maximum) { }
        static void RestoreGeometry(View view) { }
        /* ACTUAL_JOB_LATE */
        /* ACTUAL_JOB_DISABLE */
    }
    public sealed class AstellaClassUltimateHUD : MonoBehaviour
    {
        public enum UltimateState { Unavailable, Charging, Ready }
        public sealed class ClassView { public GameObject root; public Text percentText; }
        public ClassView[] views = Array.Empty<ClassView>();
        public float ChargeRatio; public UltimateState CurrentState;
        public bool IsReady => CurrentState == UltimateState.Ready;
    }
    public sealed class AstellaPlayerHUDV3 : MonoBehaviour { public bool IsReady; }
    public sealed class AstellaPointedGauge { public float Value; }
    public sealed class AstellaPartyHUDV4 : MonoBehaviour
    {
        public sealed class Row
        {
            public int sourceIndex; public GameObject root; public Transform target;
            public Text nameText, hpText, keyText; public Image jobIcon;
            public AstellaPointedGauge gauge, shieldGauge;
            public GameObject dead, selected, aggro;
        }
        public Row[] rows; public GameObject panelRoot;
    }
    public static class LocalHUDLayoutController { public static bool IsEditing; }
    public sealed class RaidNetworkSession { public static RaidNetworkSession Instance = new RaidNetworkSession(); public bool MenuOpen; }
    public static class CombatMetricsUI { public static bool ResultsOpen; }
}

static class AstellaPrismBridgeTests
{
    static int checks;
    static void Check(bool valid, string message) { checks++; if (!valid) throw new Exception(message); }
    static bool Near(float a, float b) => Math.Abs(a - b) < .00001f;
    static Text Text(string value = "") => new Text { text = value };
    static void Tick(AstellaPrismHUDBridge bridge) => typeof(AstellaPrismHUDBridge).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(bridge, null);
    static PrismV4Bindings View() => new PrismV4Bindings
    {
        healthFill = new PrismV4Gauge(), jobResourceFill = new PrismV4Gauge(), bossFill = new PrismV4Gauge(), castFill = new PrismV4Gauge(), ultimateChargeFill = new PrismV4Gauge(),
        nameText = Text(), valueText = Text(), resourceText = Text(), keyText = Text(), cooldownText = Text(), castNameText = Text(), castTimeText = Text(),
        artwork = new Image(), frame = new Image(), cooldownShade = new Image(), disabledOverlay = new Image(), selectedOverlay = new Image(), deadOverlay = new Image(),
        button = new Button(), readyGlow = new GameObject(), unavailableOverlay = new GameObject()
    };
    static CharacterActor Actor(params JobResourceDefinition[] resources) => new CharacterActor { Definition = new CharacterDefinition { resources = resources } };
    static void Main()
    {
        var ultimate = new JobResourceDefinition { id = "job.example.ultimate", name = "필살기", integer = true };
        var primary = new JobResourceDefinition { id = "primary", name = "주 자원", integer = true };
        var actor = Actor(ultimate, primary);
        actor.current[ultimate.id] = 80; actor.maximum[ultimate.id] = 100;
        actor.current[primary.id] = 3; actor.maximum[primary.id] = 5;
        var job = new AstellaClassJobHUD(); job.SetActor(actor); job.Sample();
        Check(job.HasResource && job.ResourceId == "primary" && job.ResourceLabel == "주 자원" && job.CurrentValue == 3 && job.Maximum == 5 && job.ResourceIsInteger, "actual primary policy excludes an earlier ultimate resource and exposes current units");
        var converted = new NineKeyDamageKit { Active = true, scale = 2, capacityScale = 2 };
        job.SetActor(actor, converted); job.Sample();
        Check(job.CurrentValue == 6 && job.Maximum == 10 && actor.Resource(primary.id) == 3, "readonly API exposes display conversion without mutating gameplay units");
        job.SetActor(actor); job.Sample();

        var hud = new AstellaOrnateHUD { playerHpFill = new Image { fillAmount = .6f }, playerHpText = Text("600"), bossHpFill = new Image { fillAmount = .34f }, bossPercentText = Text("34%"), bossTitleText = Text("실제 보스"), stageText = Text("STAGE 04 · HARD"), castFill = new Image { fillAmount = .75f }, castNameText = Text("실제 시전"), castTimeText = Text("2.4s") };
        var oldButton = new Button();
        Sprite originalIcon = new Sprite("existing skill"), normalFrame = new Sprite("authored frame"), normalFill = new Sprite("normal party"), criticalFill = new Sprite("critical party");
        Material iconMaterial = new Material(), frameMaterial = new Material();
        var slot = new AstellaOrnateHUD.SlotBinding { button = oldButton, slot = CharacterSlot.Gcd1, keycap = Text("J"), cooldown = Text("3.2"), sourceLabel = Text("실제 기술 이름"), artwork = new Image { enabled = false, sprite = originalIcon, material = iconMaterial }, cooldownShade = new Image { enabled = false, fillAmount = .65f } };
        var ultimateSlot = new AstellaOrnateHUD.SlotBinding { button = new Button(), slot = CharacterSlot.Ultimate, keycap = Text("Z"), cooldown = Text("") };
        hud.slots = new[] { slot, ultimateSlot };
        var partyOriginal = new AstellaPartyHUDV4.Row { sourceIndex = 2, root = new GameObject(), target = new GameObject().transform, nameText = Text("실제 동료"), hpText = Text("20%"), keyText = Text("F3"), jobIcon = new Image { sprite = originalIcon }, gauge = new AstellaPointedGauge { Value = .2f }, shieldGauge = new AstellaPointedGauge { Value = .4f }, dead = new GameObject(), selected = new GameObject(), aggro = new GameObject() };
        partyOriginal.dead.SetActive(false);
        var party = new AstellaPartyHUDV4 { panelRoot = new GameObject(), rows = new[] { partyOriginal } };
        var playerView = View(); var bossView = View(); var centerView = View(); var slotView = View(); var partyView = View();
        slotView.frame.sprite = normalFrame; slotView.frame.material = frameMaterial;
        partyView.healthFill.sprite = normalFill; partyView.criticalHealthSprite = criticalFill;
        var castGroup = new GameObject(); castGroup.transform.parent = bossView.transform; bossView.castFill.transform.parent = castGroup.transform;
        var classUltimate = new AstellaClassUltimateHUD { ChargeRatio = .8f, CurrentState = AstellaClassUltimateHUD.UltimateState.Charging, views = new[] { new AstellaClassUltimateHUD.ClassView { root = new GameObject(), percentText = Text("80%") } } };
        var ready = new AstellaPlayerHUDV3();
        var mirror = new AstellaPrismHUDBridge.PartyMirror { sourceIndex = 2, view = partyView, shieldFill = new PrismV4Gauge(), aggroOverlay = new GameObject() };
        var bridge = new AstellaPrismHUDBridge { hud = hud, jobSource = job, ultimateSource = classUltimate, readinessSource = ready, partySource = party, playerView = playerView, bossView = bossView, centerView = centerView, slots = new[] { new AstellaPrismHUDBridge.SlotMirror { sourceIndex = 0, view = slotView } }, partyRows = new[] { mirror } };
        playerView.healthFill.sourceFill = new Image { fillAmount = .99f };
        Tick(bridge);
        Check(Near(playerView.healthFill.Ratio, .6f) && playerView.healthFill.sourceFill == null && playerView.valueText.text == "600", "health mirrors the live source and owns an independent Prism gauge input");
        Check(Near(playerView.jobResourceFill.Ratio, .6f) && playerView.resourceText.text == "3 / 5" && playerView.nameText.text == "주 자원", "primary values and capacity remain independent of ultimate charge");
        Check(Near(centerView.ultimateChargeFill.Ratio, .8f) && centerView.valueText.text == "80%" && centerView.keyText.text == "Z" && !centerView.readyGlow.activeSelf, "central charge and remapped key follow the ultimate source");
        Check(Near(bossView.bossFill.Ratio, .34f) && bossView.nameText.text == "실제 보스" && bossView.resourceText.text == "STAGE 04 · HARD" && Near(bossView.castFill.Ratio, .75f) && bossView.castTimeText.text == "2.4s", "boss name, difficulty, cast and charge read existing presenters");
        Check(slotView.keyText.text == "J" && slotView.cooldownText.text == "3.2" && Near(slotView.cooldownShade.fillAmount, .65f) && slotView.cooldownShade.enabled, "remapped slot key and cooldown remain live even with hidden source graphics");
        Check(slotView.artwork.sprite == originalIcon && slotView.artwork.material == iconMaterial && slotView.artwork.enabled && !slotView.nameText.gameObject.activeSelf, "only existing icon artwork and its material are mirrored");
        Check(slotView.frame.sprite == normalFrame && slotView.frame.material == frameMaterial && hud.slots[0].button == oldButton && oldButton.interactable, "bridge preserves Button ownership and authored frame/palette");
        Check(partyView.nameText.text == "실제 동료" && partyView.keyText.text == "F3" && Near(partyView.healthFill.Ratio, .2f) && Near(mirror.shieldFill.Ratio, .4f), "party row follows sourceIndex and existing health/shield data");
        Check(partyView.healthFill.sprite == criticalFill && partyView.selectedOverlay.enabled && !partyView.deadOverlay.enabled && mirror.aggroOverlay.activeSelf, "critical health, selection, death and aggro reflect distinct source states");
        partyOriginal.gauge.Value = .8f; Tick(bridge);
        Check(partyView.healthFill.sprite == normalFill, "recovery restores normal party artwork");
        slot.artwork.sprite = null; Tick(bridge);
        Check(slotView.nameText.gameObject.activeSelf && slotView.nameText.text == "실제 기술 이름" && !slotView.artwork.enabled, "pending artwork exposes its existing live skill name without inventing an icon");
        slot.fallbackArt = originalIcon; oldButton.interactable = false; Tick(bridge);
        Check(slotView.artwork.sprite == originalIcon && !slotView.nameText.gameObject.activeSelf && slotView.disabledOverlay.enabled && !oldButton.interactable && slotView.frame.sprite == normalFrame, "existing fallback artwork and unavailable overlay preserve Button SpriteSwap ownership");
        hud.castRoot.SetActive(false); Tick(bridge);
        Check(!castGroup.activeSelf && !bossView.castNameText.gameObject.activeSelf && bossView.gameObject.activeSelf, "idle cast hiding preserves the boss module");
        hud.castRoot.SetActive(true); Tick(bridge);
        Check(castGroup.activeSelf && bossView.castNameText.gameObject.activeSelf && bossView.castNameText.text == "실제 시전", "new casts restore their original live text and visibility");

        foreach (float value in new[] { 0f, .01f, .5f, .6f, 1f })
        {
            hud.playerHpFill.fillAmount = value; Tick(bridge);
            Check(Near(playerView.healthFill.Ratio, value) && hud.playerHpFill.fillAmount == value, "gauge endpoints mirror without mutating the data Image");
        }
        classUltimate.ChargeRatio = 1; classUltimate.CurrentState = AstellaClassUltimateHUD.UltimateState.Unavailable; classUltimate.views[0].percentText.text = "100%"; Tick(bridge);
        Check(Near(centerView.ultimateChargeFill.Ratio, 1) && !centerView.readyGlow.activeSelf && centerView.unavailableOverlay.activeSelf, "full charge alone never implies readiness");
        classUltimate.CurrentState = AstellaClassUltimateHUD.UltimateState.Ready; Tick(bridge);
        Check(!centerView.readyGlow.activeSelf && centerView.unavailableOverlay.activeSelf, "readinessSource remains an independent requirement");
        ready.IsReady = true; classUltimate.ChargeRatio = .9999999f; Tick(bridge);
        Check(centerView.readyGlow.activeSelf && !centerView.unavailableOverlay.activeSelf && centerView.valueText.text == "100%", "existing full-charge percentage policy is preserved at the readiness epsilon");

        RaidNetworkSession.Instance.MenuOpen = true; Tick(bridge);
        Check(!playerView.gameObject.activeSelf && !bossView.gameObject.activeSelf && !centerView.gameObject.activeSelf && !partyView.gameObject.activeSelf && !centerView.readyGlow.activeSelf, "menus hide only the mirrored view modules and glow");
        Check(hud.playerRoot.activeSelf && hud.bossRoot.activeSelf && partyOriginal.root.activeSelf, "source object activation and source calculations are untouched");
        RaidNetworkSession.Instance.MenuOpen = false; CombatMetricsUI.ResultsOpen = true; Tick(bridge);
        Check(!playerView.gameObject.activeSelf, "results modal uses existing visibility rules");
        LocalHUDLayoutController.IsEditing = true; Tick(bridge);
        Check(playerView.gameObject.activeSelf && bossView.gameObject.activeSelf && centerView.gameObject.activeSelf, "HUD editing follows the original preview visibility policy");
        LocalHUDLayoutController.IsEditing = false; CombatMetricsUI.ResultsOpen = false; Tick(bridge);
        Check(playerView.gameObject.activeSelf && centerView.readyGlow.activeSelf, "closing overlays restores current presentation state");

        var heat = new JobResourceDefinition { id = "heat", name = "온도", integer = true };
        var ammo = new JobResourceDefinition { id = "ammo", name = "탄", integer = true };
        var dragon = Actor(heat, ammo, ultimate); dragon.Definition.tank = true;
        dragon.Definition.routes = new[] { new Route { id = "one" }, new Route { id = "two" }, new Route { id = "three" } };
        dragon.RouteId = "one"; dragon.current["heat"] = 80; dragon.maximum["heat"] = 100; dragon.current["ammo"] = 2; dragon.maximum["ammo"] = 4;
        job.SetActor(dragon); job.Sample(); Tick(bridge);
        Check(job.ResourceId == "ammo" && playerView.resourceText.text == "2 / 4", "actual primary selector preserves Dragon heat route restrictions");
        dragon.RouteId = "three"; job.Sample(); Tick(bridge);
        Check(job.ResourceId == "heat" && playerView.resourceText.text == "80 / 100", "actual primary selector exposes eligible route-specific heat");
        var onlyUltimate = Actor(ultimate); onlyUltimate.current[ultimate.id] = 100; onlyUltimate.maximum[ultimate.id] = 100;
        job.SetActor(onlyUltimate); job.Sample(); Tick(bridge);
        Check(!job.HasResource && playerView.resourceText.text == "—" && Near(playerView.jobResourceFill.Ratio, 0), "missing primary resource never falls back to ultimate data");
        job.SetActor(actor, converted); job.Sample(); Tick(bridge);
        Check(playerView.resourceText.text == "6 / 10", "Prism receives existing display-unit conversions unchanged");
        job.Disable(); Tick(bridge);
        Check(!job.HasResource && job.ResourceId == "" && !job.ResourceIsInteger && playerView.resourceText.text == "—", "disabled primary source does not leave stale values");
        partyOriginal.gauge.Value = .1f; Tick(bridge);
        typeof(AstellaPrismHUDBridge).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(bridge, null);
        Check(!centerView.readyGlow.activeSelf && partyView.healthFill.sprite == normalFill, "bridge disable retires stale readiness and restores captured party art");
        var order = (DefaultExecutionOrder)Attribute.GetCustomAttribute(typeof(AstellaPrismHUDBridge), typeof(DefaultExecutionOrder));
        Check(order != null && order.order == 13000, "bridge runs after all existing data presenters");
        Console.WriteLine("PASS: " + checks + " actual Prism bridge/data-source checks; no Unity rendering, input, button callbacks or gameplay simulation.");
    }
}
