using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    // Local practice owns transient actors/metrics only. RaidRunFlow stays in Lobby for its entire lifetime.
    [DefaultExecutionOrder(1500)]
    public sealed class CharacterTrainingSession : MonoBehaviour
    {
        public static CharacterTrainingSession Instance { get; private set; }
        public static bool Active => Instance != null && RaidNetworkSession.Instance?.Mode == RaidSessionMode.Training && !RaidNetworkSession.Instance.Ending;
        public static bool CanEdit(CharacterActor actor) => Active && !RaidCombatAuthority.Active && actor != null && actor == Instance.Actor;
        public CharacterActor Actor { get; private set; }
        public PlayerController Player { get; private set; }
        public BossController Target { get; private set; }
        public CharacterDefinition[] Jobs { get; private set; }
        public string Message { get; private set; } = "훈련 준비 중";
        public readonly TrainingAttackClock AttackClock = new TrainingAttackClock();
        public TrainingAttack Attack { get; private set; }
        public bool MovingTargets { get; set; }
        public bool MultiTarget { get; private set; }
        public bool FarAllies { get; private set; }
        public int Evolution { get; private set; }
        public double Overheal { get; private set; }
        public double Stagger { get; private set; }
        public int Counters { get; private set; }
        public readonly Dictionary<string, Vector2> ResourceChanges = new Dictionary<string, Vector2>();
        internal void RecordResource(string id, float delta)
        {
            if (!Active) return;
            ResourceChanges.TryGetValue(id, out var totals);
            if (delta >= 0) totals.x += delta; else totals.y -= delta;
            ResourceChanges[id] = totals;
        }
        private readonly List<BossAddMinion> _adds = new List<BossAddMinion>();
        public IReadOnlyList<BossAddMinion> ExtraTargets => _adds;
        private float _damage;
        private Transform _victim;
        private int _successAtWarning;
        private LineRenderer _warning;
        private Material _warningMaterial;
        private Vector3 _origin;

        private void Awake() { Instance = this; }
        private void Start()
        {
            var stage = StageManager.Instance;
            Player = stage != null ? stage.player : null; Target = stage != null ? stage.boss : null;
            if (Player == null || Target == null || Camera.main == null)
            { Message = "자동 전장 구성이 없습니다. 원본 SceneAutoBuilder 구성을 확인하세요."; RaidNetworkSession.Instance.FailSession(Message); return; }
            Actor = CharacterActor.Ensure(Player.gameObject);
            Jobs = CharacterCatalog.All.Where(j => j.legacyRole == 0 && j.playable).ToArray();
            stage.cardManager?.CancelPendingSelection();
            Target.StopEncounterEffects(); Target.skillTable.Clear();
            Target.addSpawner?.ResetWaveFlagsAndClearAdds();
            if (Target.addSpawner != null) Target.addSpawner.enabled = false;
            Target.currentBossTitle = "훈련 표적 · 정면 빨강 / 측면 초록 / 후면 파랑";
            Target.maxHp = Target.currentHp = 1000000;
            Target.enableRotation = false;
            if (stage.stageHeaderText != null) stage.stageHeaderText.text = "12직업 훈련장 · 로컬 전용 · 진행/보상 저장 없음";
            for (int i = 0; i < 2; i++) CreateExtraTarget(i);
            var line = new GameObject("Training stimulus telegraph"); line.transform.SetParent(transform, false);
            _warning = line.AddComponent<LineRenderer>(); _warning.useWorldSpace = true; _warning.positionCount = 2;
            _warning.startWidth = _warning.endWidth = .12f;
            _warningMaterial = new Material(Shader.Find("Sprites/Default")); _warning.sharedMaterial = _warningMaterial; _warning.enabled = false;
            SetMultipleTargets(false);
            SetBuild(Jobs[0], Jobs[0].routes[0].id, FirstChoice(Jobs[0], CharacterSlot.Active), FirstChoice(Jobs[0], CharacterSlot.OffGcd2), FirstChoice(Jobs[0], CharacterSlot.Ultimate), 0);
            gameObject.AddComponent<CharacterTrainingUI>();
            QuarterViewCamera.LocalCamera?.BeginCombat(true);
        }
        public static string FirstChoice(CharacterDefinition job, CharacterSlot slot) => job.unlocks.First(r => r.slot == slot).choices[0];
        private void CreateExtraTarget(int index)
        {
            var go = RaidPrimitive.Create(PrimitiveType.Capsule); go.name = "훈련 표적 " + (index + 2);
            var add = go.AddComponent<BossAddMinion>(); add.enabled = false;
            add.addType = AddMinionType.Assassin_HealerHunter; add.addDisplayName = go.name;
            add.maxHp = add.currentHp = 1000000; add.combatRadius = .65f;
            // No spawner registration, attack AI, tether, death rewards or boss damage modifiers.
            var ring = new GameObject("Direction ring"); ring.transform.SetParent(go.transform, false);
            ring.transform.localPosition = Vector3.down;
            var renderer = ring.AddComponent<TargetRingRenderer>(); renderer.outerRadius = add.combatRadius; renderer.innerRadius = .3f; renderer.BuildTargetRing();
            _adds.Add(add);
        }
        public bool SetBuild(CharacterDefinition job, string route, string r, string f, string v, int evolution)
        {
            if (!CanEdit(Actor) || !Jobs.Contains(job) || !CharacterTrainingRules.TryBuild(job, route, r, f, v, evolution, out var candidate))
            { Message = "제공되지 않거나 실행 불가능한 구성입니다."; return false; }
            StopAttacks(); ClearEffects();
            if (!Actor.ConfigureTraining(candidate)) { Message = "직업 실행기 연결 실패"; return false; }
            Evolution = evolution;
            ResetParty(); ResetPositions(); ResetRecords();
            TargetingManager.Instance?.SetTarget(Target.transform);
            Message = "적용: " + job.name + " · " + job.routes.First(x => x.id == route).name + " (훈련 전용)";
            return true;
        }
        public void ResetAll()
        {
            if (!CanEdit(Actor)) return;
            var d = Actor.Definition;
            SetBuild(d, Actor.RouteId, Actor.Selected(CharacterSlot.Active).id, Actor.Selected(CharacterSlot.OffGcd2).id, Actor.Selected(CharacterSlot.Ultimate).id, Evolution);
        }
        private void ClearEffects()
        {
            foreach (var member in PartyLife.OrderedMembers())
            {
                member.GetComponent<CharacterActor>()?.InterruptCast();
                member.GetComponent<CharacterKit>()?.Clear();
                member.GetComponent<AegisPaladin>()?.ReleaseRouteEffects();
                member.GetComponent<CombatStatusEffects>()?.Clear();
            }
            Target?.GetComponent<CombatStatusEffects>()?.Clear();
            foreach (var add in _adds) if (add != null) add.GetComponent<CombatStatusEffects>()?.Clear();
            foreach (var zone in FindObjectsByType<HealingSanctuaryZone>(FindObjectsSortMode.None)) { zone.gameObject.SetActive(false); Destroy(zone.gameObject); }
            foreach (var projectile in FindObjectsByType<RangedBotProjectile>(FindObjectsSortMode.None)) { projectile.gameObject.SetActive(false); Destroy(projectile.gameObject); }
        }
        private void ResetParty()
        {
            foreach (var member in PartyLife.OrderedMembers())
            { PartyLife.Ensure(member.gameObject).ResetForEncounter(); if (member != Actor.transform) CharacterActor.Ensure(member.gameObject).ResetEncounter(CharacterReset.Retry); }
        }
        public void RestoreHealthAndResources()
        {
            if (!CanEdit(Actor)) return;
            foreach (var member in PartyLife.OrderedMembers())
            {
                if (!CombatRange.LivingPartyMember(member)) PartyLife.Ensure(member.gameObject).ResetForEncounter();
                member.GetComponent<PlayerController>()?.TrainingHealth(1, false); member.GetComponent<PartyBotAI>()?.TrainingHealth(1, false);
            }
            Actor.TrainingRefill(true, false); Message = "훈련 패널: HP·자원 복원 (회복 미터 제외)";
        }
        public void RefreshCooldowns() { if (CanEdit(Actor)) { Actor.TrainingRefill(false, true); Message = "GCD·직업 쿨타임·이동기 충전 초기화"; } }
        public void ResetRecords()
        {
            if (!CanEdit(Actor)) return;
            StopAttacks();
            // Do not advance the encounter generation: live ground effects must continue ticking.
            CombatMetricsManager.Instance?.ResetTrainingRecords(); Overheal = Stagger = 0; Counters = 0; ResourceChanges.Clear();
            Message = "전투 기록 초기화";
        }
        internal void RecordOverheal(float value) { if (Active) Overheal += Math.Max(0, value); }
        internal void RecordStagger(float value) { if (Active) Stagger += Math.Max(0, value); }
        internal void RecordCounter() { if (Active) Counters++; }
        public void SetMultipleTargets(bool multiple)
        {
            if (!Active) return; StopAttacks(); MultiTarget = multiple;
            foreach (var add in _adds) add.gameObject.SetActive(multiple);
            TargetingManager.Instance?.SetTarget(Target.transform); ResetPositions();
        }
        public void SetFarAllies(bool far) { if (!Active) return; FarAllies = far; PlaceAllies(); }
        private void PlaceAllies()
        {
            int i = 0;
            foreach (var member in PartyLife.OrderedMembers()) if (member != Player.transform)
            {
                var point = new Vector3((i - 1) * 3, member.position.y, FarAllies ? 12 : -4);
                member.GetComponent<PlayerController>()?.TrainingPlace(point);
                member.GetComponent<PartyBotAI>()?.TrainingPlace(point); i++;
            }
        }
        public void ResetPositions()
        {
            if (!CanEdit(Actor)) return; StopAttacks();
            Player.TrainingPlace(new Vector3(0, 1, -6));
            Target.transform.SetPositionAndRotation(new Vector3(0, 1, 0), Quaternion.Euler(0, 180, 0));
            Target.currentHp = Target.maxHp; Target.fsmState = BossFSMState.IdleChasing;
            CombatCounterTarget.Ensure(Target.transform).ResetState();
            for (int i = 0; i < _adds.Count; i++) { _adds[i].transform.SetPositionAndRotation(new Vector3((i == 0 ? -1 : 1) * 3, 1, 2), Quaternion.Euler(0, 180, 0)); _adds[i].currentHp = _adds[i].maxHp; _adds[i].isDead = false; _adds[i].GetComponent<CombatCounterTarget>()?.ResetState(); }
            PlaceAllies();
        }
        public void SetAllyHealth(float fraction, bool clearShields, bool debuff)
        {
            if (!CanEdit(Actor) || !CharacterDefinitionValidation.Finite(fraction)) return;
            foreach (var member in PartyLife.OrderedMembers()) if (member != Player.transform)
            {
                member.GetComponent<PlayerController>()?.TrainingHealth(fraction, clearShields);
                member.GetComponent<PartyBotAI>()?.TrainingHealth(fraction, clearShields);
                if (debuff) ApplyDebuff(member);
            }
        }
        private static void ApplyDebuff(Transform target) => CombatStatusEffects.Ensure(target.gameObject).Apply("training.dispel", "poison", 15, true, root: true);
        public void BeginAttack(TrainingAttack attack, float damage, float interval, bool repeat)
        {
            if (!CanEdit(Actor) || !Enum.IsDefined(typeof(TrainingAttack), attack) || !CharacterDefinitionValidation.Finite(damage) || damage < 1 || damage > 10000) return;
            StopAttacks(); Attack = attack; _damage = damage;
            var selected = TargetingManager.Instance?.CurrentTarget;
            _victim = CombatRange.LivingPartyMember(selected) ? selected : Player.transform;
            if (AttackClock.Start(Time.time, interval, repeat)) Warn();
        }
        private void Warn()
        {
            if (!CombatRange.LivingPartyMember(_victim)) { StopAttacks(); Message = "시험 대상이 사망했습니다."; return; }
            _origin = Target.transform.position;
            _warning.startColor = _warning.endColor = Attack == TrainingAttack.Counter ? new Color(1f,.75f,.15f) : Color.red;
            _warning.enabled = true; _warning.SetPosition(0, _origin + Vector3.up); _warning.SetPosition(1, _victim.position + Vector3.up);
            _successAtWarning = Counters;
            if (Attack == TrainingAttack.Counter) CombatCounterTarget.Ensure(Target.transform).OpenWindow((float)TrainingAttackClock.WarningSeconds + .1f);
            Message = "예고: " + (Attack == TrainingAttack.Counter ? "⚔ 금색 일반 카운터" : Attack.ToString()) + " → " + _victim.name + " (1.5초)";
            Target.ShowExternalAlert(Message, Attack == TrainingAttack.Counter ? new Color(1f,.75f,.15f) : Color.red, 1.5f);
        }
        public void StopAttacks()
        {
            AttackClock.Stop(); if (_warning != null) _warning.enabled = false;
            if (Target != null) Target.GetComponent<CombatCounterTarget>()?.CloseWindow(); Message = "시험 공격 정지";
        }
        private void ApplyAttack()
        {
            _warning.enabled = false;
            bool countered = Attack == TrainingAttack.Counter && Counters != _successAtWarning;
            Target.GetComponent<CombatCounterTarget>()?.CloseWindow();
            if (countered) { Message = "카운터 성공 · 시험 공격 취소 · 파티 이동기 충전 복구"; return; }
            var victims = Attack == TrainingAttack.Area ? PartyLife.OrderedMembers() : new List<Transform> { _victim };
            var kind = Attack == TrainingAttack.Area ? CombatDamageKind.Area : Attack == TrainingAttack.System ? CombatDamageKind.System : CombatDamageKind.Direct;
            var hit = new CombatHitContext(Target.transform, "training." + Attack, kind, projectile: Attack == TrainingAttack.Projectile);
            foreach (var member in victims.Where(CombatRange.LivingPartyMember))
            {
                if (Attack == TrainingAttack.Dispel) ApplyDebuff(member);
                else if (Attack == TrainingAttack.Knockback)
                { member.GetComponent<PlayerController>()?.StartKnockbackXZ(_origin, 4); member.GetComponent<PartyBotAI>()?.StartKnockbackXZ(_origin, 4); }
                else { member.GetComponent<PlayerController>()?.TakeDamage(_damage, hit); member.GetComponent<PartyBotAI>()?.TakeDamage(_damage, true, hit); }
            }
            Message = "적용: " + Attack + " · 입력 피해 " + _damage.ToString("0");
        }
        private void Update()
        {
            if (!Active || Target == null || Time.timeScale <= .001f) return;
            // Dummies cannot die or start the ordinary boss/add progression from accumulated practice damage.
            Target.currentHp = Target.maxHp;
            foreach (var add in _adds) if (add != null) add.currentHp = add.maxHp;
            if (MovingTargets && !AttackClock.Telegraphing && !Target.IsGroggy)
                Target.transform.position = new Vector3(Mathf.Sin(Time.time * .25f) * 2, 1, 0);
            if (AttackClock.Advance(Time.time, out bool warn)) ApplyAttack(); else if (warn) Warn();
        }
        private void OnDestroy()
        {
            StopAttacks(); foreach (var add in _adds) if (add != null) Destroy(add.gameObject);
            if (_warningMaterial != null) Destroy(_warningMaterial);
            if (Instance == this) Instance = null;
        }
    }
}
