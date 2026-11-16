using System;

enum PartyBotRole { Tank, Healer, MeleeDPS, RangedDPS }
struct Vector3
{
    public float x, y, z;
    public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
}
struct Color { public Color(float r, float g, float b, float a) { } }
sealed class GameObject { public bool activeSelf = true; public void SetActive(bool value) { activeSelf = value; } }
sealed class Transform { public Vector3 position, localPosition, localScale; public float rotation; public GameObject gameObject = new GameObject(); }
sealed class TextMesh { public string text; public Color color; }
sealed class Camera { public static Camera main = new Camera(); public Transform transform = new Transform(); }
static class Mathf { public static int CeilToInt(float value) => (int)Math.Ceiling(value); }
static class ViewModeManager { public static bool Is2DViewActive; }
static class RaidCombatAuthority { public static bool Replica; }
enum RaidBattlePhase { Lobby, Countdown, Combat, Reward, Wiped, Victory, Loading, BossClear, Closed, Approach, Arrival }
sealed class StageProgress { public RaidBattlePhase Phase; }
sealed class StageManager { public static bool CombatSuspended; public static StageManager Instance = new StageManager(); public StageProgress Progress = new StageProgress(); }
sealed class RaidNetworkSession { public static bool Multiplayer; public static RaidNetworkSession Instance = new RaidNetworkSession(); public RaidBattlePhase BattlePhase; }
static class Time { public static float timeScale = 1; }
sealed class BossController { public bool IsStackMechanicActive, IsSpreadMechanicActive, HardTransformationActive; }

sealed class PartyBotAI
{
    public PartyBotRole role;
    public string _customDisplayName;
    public bool isDead, _isInterceptingTetherForPlayer, isTankDefensiveActive;
    public float currentHp = 50, maxHp = 100, _rescueCooldownTimer;
    public BossController targetBoss;
    public Transform transform = new Transform();
    public Transform _hpBarRoot = new Transform(), _hpFillBar = new Transform();
    public TextMesh _nameLabelText = new TextMesh();
    public float HpRatio => Math.Max(0, Math.Min(1, currentHp / maxHp));
    static Color GetRoleBrightColor(PartyBotRole value) => new Color();
    public string ReadName() => OverheadDisplayName();
    public void Present() => LateUpdate();
    /* ACTUAL_OVERHEAD_NAME */
    /* ACTUAL_OVERHEAD_UPDATE */
    /* ACTUAL_LATE_UPDATE */
}

static class PartyBotPresentationTests
{
    static int checks;
    static void Check(bool valid, string message) { checks++; if (!valid) throw new Exception(message); }
    static bool Near(float a, float b) => Math.Abs(a - b) < .0001f;
    static void Main()
    {
        var roles = new[] { PartyBotRole.Tank, PartyBotRole.Healer, PartyBotRole.MeleeDPS, PartyBotRole.RangedDPS };
        var names = new[] { "가디언", "오라클", "블레이드", "아크메이지" };
        var generated = new[] { "BOT 1: 가디언 (탱커)", "BOT 2: 오라클 (힐러)", "BOT 1: 블레이드 (근딜·줄수호)", "BOT 3: 아크메이지 (캐스터)" };
        for (int i = 0; i < roles.Length; i++)
        {
            var bot = new PartyBotAI { role = roles[i] };
            Check(bot.ReadName() == names[i], "default overhead uses a short role name");
            bot._customDisplayName = generated[i];
            Check(bot.ReadName() == names[i] && bot._customDisplayName == generated[i], "legacy overhead shortening preserves full identity");
            bot._customDisplayName = "BOT1: 블레이드 (근딜)";
            Check(bot.ReadName() == names[i], "generated role metadata cannot override the bot's current role");
        }
        foreach (string custom in new[] { "내 동료", "BOT 연구원", "BOT 7: 새 이름", "BOT x: 오라클 (힐러)", "BOT 1: 오라클 (힐러) 확장", "BOT 1: Alice (Healer)", " BOT 1: 오라클 (힐러)" })
        {
            var bot = new PartyBotAI { role = PartyBotRole.Healer, _customDisplayName = custom };
            Check(bot.ReadName() == custom && bot._customDisplayName == custom, "custom names remain byte-for-byte intact");
        }
        var visual = new PartyBotAI { role = PartyBotRole.Healer, _customDisplayName = generated[1], _rescueCooldownTimer = 12.3f };
        for (int state = 0; state < 5; state++)
        {
            RaidCombatAuthority.Replica = state == 1;
            StageManager.CombatSuspended = state == 2;
            Time.timeScale = state == 3 ? 0 : 1;
            visual.targetBoss = state == 0 ? null : new BossController { HardTransformationActive = state == 4 };
            visual.transform.position = new Vector3(state, 1, -state);
            Camera.main.transform.rotation = 20 + state;
            visual.Present();
            Check(Near(visual._hpBarRoot.position.x, state) && Near(visual._hpBarRoot.position.y, 2.55f) && Near(visual._hpBarRoot.position.z, -state), "late presentation follows actor even while AI is suspended");
            Check(visual._hpBarRoot.rotation == Camera.main.transform.rotation, "late presentation faces the current camera");
            Check(visual._nameLabelText.text == "오라클 · 구출 13s", "suspended presentation retains compact identity and cooldown");
            Check(visual.currentHp == 50 && visual._rescueCooldownTimer == 12.3f && Near(visual._hpFillBar.localScale.x, .6f), "visual refresh does not advance combat, healing or cooldowns");
        }
        ViewModeManager.Is2DViewActive = true; visual.Present();
        Check(Near(visual._hpBarRoot.position.z, visual.transform.position.z + 1.15f), "2D overhead uses the north-side offset");
        visual.isDead = true; visual.targetBoss.IsStackMechanicActive = true; visual.Present();
        Check(visual._nameLabelText.text == "오라클 · 전투 불능", "death overrides movement instructions");
        visual.isDead = false; visual.Present();
        Check(visual._nameLabelText.text == "오라클 · 집결", "compact stack state remains visible");
        visual.targetBoss.IsStackMechanicActive = false; visual.targetBoss.IsSpreadMechanicActive = true; visual.Present();
        Check(visual._nameLabelText.text == "오라클 · 산개", "compact spread state remains visible");
        visual._isInterceptingTetherForPlayer = true; visual.Present();
        Check(visual._nameLabelText.text == "오라클 · 줄 인계", "tether handoff retains its brief status");
        visual._isInterceptingTetherForPlayer = false; visual.targetBoss.IsSpreadMechanicActive = false; visual.isTankDefensiveActive = true; visual.Present();
        Check(visual._nameLabelText.text == "오라클 · 방어", "active defense retains its brief status");

        RaidNetworkSession.Instance.BattlePhase = RaidBattlePhase.Combat;
        StageManager.Instance.Progress.Phase = RaidBattlePhase.Arrival; visual.Present();
        Check(!visual._hpBarRoot.gameObject.activeSelf, "solo arrival hides only the generated overhead root");
        visual.transform.position = new Vector3(12, 1, 7); Camera.main.transform.rotation = 75;
        StageManager.Instance.Progress.Phase = RaidBattlePhase.Countdown; visual.Present();
        Check(visual._hpBarRoot.gameObject.activeSelf && Near(visual._hpBarRoot.position.x, 12) && visual._hpBarRoot.rotation == 75, "countdown restores the overhead at the current actor and camera pose");
        StageManager.Instance.Progress.Phase = RaidBattlePhase.BossClear; visual.Present();
        Check(!visual._hpBarRoot.gameObject.activeSelf, "solo boss-clear shot hides overhead labels");
        StageManager.Instance.Progress.Phase = RaidBattlePhase.Reward; visual.Present();
        Check(visual._hpBarRoot.gameObject.activeSelf, "reward phase restores labels after a clear shot");

        RaidNetworkSession.Multiplayer = true; RaidCombatAuthority.Replica = true;
        StageManager.Instance.Progress.Phase = RaidBattlePhase.Combat;
        RaidNetworkSession.Instance.BattlePhase = RaidBattlePhase.Arrival; visual.Present();
        Check(!visual._hpBarRoot.gameObject.activeSelf, "replica arrival uses the received phase instead of host-only Progress");
        RaidNetworkSession.Instance.BattlePhase = RaidBattlePhase.Countdown; visual.Present();
        Check(visual._hpBarRoot.gameObject.activeSelf, "replica countdown restores labels");
        RaidNetworkSession.Instance.BattlePhase = RaidBattlePhase.BossClear; visual.Present();
        Check(!visual._hpBarRoot.gameObject.activeSelf, "replica boss-clear shot hides overhead labels");
        StageManager.Instance.Progress.Phase = RaidBattlePhase.Arrival;
        RaidNetworkSession.Instance.BattlePhase = RaidBattlePhase.Reward; visual.Present();
        Check(visual._hpBarRoot.gameObject.activeSelf && visual._customDisplayName == generated[1] && visual.currentHp == 50 && visual._rescueCooldownTimer == 12.3f, "replica reward restores visuals without mutating identity, health or cooldowns");
        Console.WriteLine("PASS: " + checks + " actual bot overhead identity and late presentation checks; Unity camera timing and rendering remain unverified.");
    }
}
