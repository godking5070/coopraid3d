using UnityEngine;

namespace RaidRogue
{
    [DefaultExecutionOrder(-200)]
    public sealed class CombatMetricsManager : MonoBehaviour
    {
        public static CombatMetricsManager Instance { get; private set; }
        public CombatMetricsLedger Ledger { get; } = new CombatMetricsLedger();
        public static int EncounterId => Instance != null ? Instance.Ledger.Generation : -1;
        private StageManager _stage;
        private CombatMetricsUI _ui;

        public static CombatMetricsManager Ensure(StageManager stage)
        {
            var metrics = stage.GetComponent<CombatMetricsManager>();
            if (metrics == null) metrics = stage.gameObject.AddComponent<CombatMetricsManager>();
            metrics._stage = stage;
            if (metrics._ui == null)
            {
                metrics._ui = stage.GetComponent<CombatMetricsUI>();
                if (metrics._ui == null) metrics._ui = stage.gameObject.AddComponent<CombatMetricsUI>();
            }
            metrics._ui.Initialize(metrics, stage);
            return metrics;
        }

        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
            Ledger.Advance(Time.unscaledDeltaTime, !StageManager.CombatSuspended && Time.timeScale > 0.001f && _stage != null &&
                _stage.boss != null && !_stage.boss.IsBossDefeatedCollapse && !_stage.IsStageTransitioning);
        }

        public void BeginStage(int stage, bool resetRun)
        {
            if (resetRun) Ledger.ResetRun();
            RefreshRoster();
            Ledger.BeginStage(stage);
            _ui.CloseResults();
        }

        public void RefreshRoster()
        {
            if (_stage != null && _stage.player != null) Register(_stage.player.transform);
            foreach (var bot in FindObjectsByType<PartyBotAI>(FindObjectsSortMode.InstanceID)) Register(bot.transform);
        }

        public int Register(Transform source)
        {
            if (source == null) return 0;
            int id = source.GetInstanceID();
            var player = source.GetComponent<PlayerController>();
            if (player != null)
            {
                Ledger.Register(id, "나 · " + RoleName((int)player.currentRole), (int)player.currentRole);
                return id;
            }
            var bot = source.GetComponent<PartyBotAI>();
            if (bot == null) return 0;
            int role = bot.role == PartyBotRole.Tank ? 1 : bot.role == PartyBotRole.Healer ? 2 :
                bot.role == PartyBotRole.MeleeDPS ? 3 : 4;
            Ledger.Register(id, bot.botDisplayName, role);
            return id;
        }

        public static string RoleName(int role) => role == 1 ? "가디언" : role == 2 ? "오라클" : role == 3 ? "블레이드" : "아크메이지";
        public static int SourceId(Transform source) => Instance != null ? Instance.Register(source) : 0;

        public static void Damage(Transform source, float before, float after, bool isAdd)
        {
            if (Instance == null || Time.timeScale <= 0.001f) return;
            Instance.Ledger.RecordDamage(SourceId(source), before, after, isAdd);
        }

        public static void Healing(Transform source, float before, float after, HealingKind kind = HealingKind.Ability)
        {
            if (Instance == null || Time.timeScale <= 0.001f) return;
            Instance.Ledger.RecordHealing(SourceId(source), before, after, kind);
        }

        public static void Absorption(int source, double amount)
        {
            if (Instance != null && Time.timeScale > 0.001f) Instance.Ledger.RecordAbsorption(source, amount);
        }

        public static void SetDead(Transform member, bool dead)
        {
            if (Instance != null && (!dead || Time.timeScale > 0.001f)) Instance.Ledger.SetDead(SourceId(member), dead);
        }
    }
}
