using UnityEngine;

namespace RaidRogue
{
    [DefaultExecutionOrder(-200)]
    public sealed partial class CombatMetricsManager : MonoBehaviour
    {
        public static CombatMetricsManager Instance { get; private set; }
        public CombatMetricsLedger Ledger { get; } = new CombatMetricsLedger();
        public static int EncounterId => Instance != null ? Instance.Ledger.Generation : -1;
        public readonly struct DamageReceipt
        {
            public readonly int Owner,Encounter;public readonly string OriginalSkill;public readonly CombatDamageKind Kind;public readonly float Actual;
            public DamageReceipt(int owner,int encounter,string skill,CombatDamageKind kind,float actual){Owner=owner;Encounter=encounter;OriginalSkill=skill;Kind=kind;Actual=actual;}
        }
        private readonly System.Collections.Generic.Queue<DamageReceipt> _recentDamage=new System.Collections.Generic.Queue<DamageReceipt>();
        public DamageReceipt[] RecentDamage=>_recentDamage.ToArray();
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
            if (RaidCombatAuthority.Replica) return;
            Ledger.Advance(Time.unscaledDeltaTime, !StageManager.CombatSuspended && Time.timeScale > 0.001f && _stage != null &&
                _stage.boss != null && !_stage.boss.IsBossDefeatedCollapse && !_stage.IsStageTransitioning);
        }

        public void BeginStage(int stage, bool resetRun)
        {
            _recentDamage.Clear();
            if (resetRun) Ledger.ResetRun();
            if (RaidCombatAuthority.Active)
            {
                var ids = new System.Collections.Generic.List<int>();
                foreach (var member in PartyLife.OrderedMembers()) ids.Add(RaidCombatAuthority.Id(member));
                Ledger.RetainCurrentRoster(ids); // Previous encounter results keep departed members; a new attempt has only its current party.
            }
            RefreshRoster();
            Ledger.BeginStage(stage);
            _ui.CloseResults();
        }

        internal void ResetTrainingRecords()
        {
            if (!CharacterTrainingSession.Active) return;
            if (!Ledger.Active) BeginStage(0, true);
            else { _recentDamage.Clear(); Ledger.ResetCurrentCounters(); RefreshRoster(); }
        }
        public void RefreshRoster()
        {
            foreach (var member in PartyLife.OrderedMembers()) Register(member);
        }

        public int Register(Transform source)
        {
            if (source == null) return 0;
            int id = RaidCombatAuthority.Active ? RaidCombatAuthority.Id(source) : source.GetInstanceID();
            if (source.GetComponent<PlayerController>() == null && source.GetComponent<PartyBotAI>() == null) return 0;
            string jobId = CharacterActor.Ensure(source.gameObject).Definition?.id ?? "";
            var avatar = source.GetComponent<NetworkRaidAvatar>();
            if (avatar != null) { Ledger.Register(id, avatar.DisplayName, avatar.Role, jobId); return id; }
            var player = source.GetComponent<PlayerController>();
            if (player != null)
            {
                var definition=CharacterActor.Ensure(source.gameObject).Definition;
                Ledger.Register(id, "나 · " + (definition.legacyRole==0?definition.name:RoleName((int)player.currentRole)), (int)player.currentRole, jobId);
                return id;
            }
            var bot = source.GetComponent<PartyBotAI>();
            if (bot == null) return 0;
            int role = bot.role == PartyBotRole.Tank ? 1 : bot.role == PartyBotRole.Healer ? 2 :
                bot.role == PartyBotRole.MeleeDPS ? 3 : 4;
            Ledger.Register(id, bot.botDisplayName, role, jobId);
            return id;
        }

        public static string RoleName(int role) => role == 1 ? "가디언" : role == 2 ? "오라클" : role == 3 ? "블레이드" : "아크메이지";
        public static int SourceId(Transform source) => Instance != null ? Instance.Register(source) : 0;

        public static void Damage(Transform source, float before, float after, bool isAdd,string origin="",CombatDamageKind kind=CombatDamageKind.Unclassified)
        {
            if (RaidCombatAuthority.Replica || Instance == null || Time.timeScale <= 0.001f) return;
            int owner=SourceId(source);Instance.Ledger.RecordDamage(owner, before, after, isAdd);
            if(owner!=0&&before>after){Instance._recentDamage.Enqueue(new DamageReceipt(owner,EncounterId,origin,kind,before-after));while(Instance._recentDamage.Count>256)Instance._recentDamage.Dequeue();}
        }

        public static void Healing(Transform source, float before, float after, HealingKind kind = HealingKind.Ability)
        {
            if (RaidCombatAuthority.Replica || Instance == null || Time.timeScale <= 0.001f) return;
            Instance.Ledger.RecordHealing(SourceId(source), before, after, kind);
            RaidCombatAuthority.Threat(source, Mathf.Max(0, after - before) * .5f);
        }

        public static void Absorption(int source, double amount,bool grantResource=true)
        {
            if (RaidCombatAuthority.Replica || amount <= 0 || Time.timeScale <= .001f) return;
            if (Instance != null) Instance.Ledger.RecordAbsorption(source, amount);
            if(!grantResource)return;
            foreach(var member in PartyLife.OrderedMembers())
                if(SourceId(member)==source){member.GetComponent<AegisPaladin>()?.ShieldAbsorbed(amount);break;}
        }

        public static void SetDead(Transform member, bool dead)
        {
            if (!RaidCombatAuthority.Replica && Instance != null && (!dead || Time.timeScale > 0.001f)) Instance.Ledger.SetDead(SourceId(member), dead);
        }
    }
}
