using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    public partial class BossController
    {
        private readonly Dictionary<Transform, float> _combatThreat = new Dictionary<Transform, float>();
        private Vector3 _replicaPosition;
        private float _replicaYaw, _networkCastStarted, _replicaCastEnd;
        public void AddCombatThreat(Transform source, float amount, bool taunt = false)
        {
            if (RaidCombatAuthority.Replica || !CombatRange.LivingPartyMember(source)) return;
            float maximum = 0; if (taunt) foreach (float value in _combatThreat.Values) maximum = Mathf.Max(maximum, value);
            _combatThreat.TryGetValue(source, out float existing);
            float mult = source.GetComponent<PartyLife>()?.IsTank == true ? 5 : 1;
            _combatThreat[source] = taunt ? maximum + 1000 : existing + amount * mult;
        }
        public RaidCombatEnemy CaptureNetworkCombat() => new RaidCombatEnemy
        {
            brandEnd=GetComponent<CombatStatusEffects>()?.BrandEnd??0, stagger=GetComponent<CombatCounterTarget>()?.Stagger??0, counterWindowEnd=GetComponent<CombatCounterTarget>()?.WindowEnd??0,
            id = -1, stage = currentStageNumber, phase = (int)currentPhase, state = (int)fsmState,
            target = RaidCombatAuthority.Id(aggroTarget), name = name, x = transform.position.x, y = transform.position.y, z = transform.position.z,
            yaw = transform.eulerAngles.y, hp = currentHp, maxHp = maxHp, radius = CombatRadius, dead = currentHp <= 0,
            casting = IsCasting, castName = _currentCastSkillName, castRemaining = _castRemainingTime, castTotal = _castTotalDuration, groggy = _groggyRemainingTime,
            alert = mechanicAlertBannerText != null ? mechanicAlertBannerText.text : ""
        };
        public void ApplyNetworkCombat(RaidCombatEnemy state, float sampledTime)
        {
            if (state == null) return;
            CombatCounterTarget.Ensure(transform).Restore(state.stagger,state.counterWindowEnd);
            CombatStatusEffects.Ensure(gameObject).RestoreBrand(state.brandEnd);
            if (currentStageNumber != state.stage) SetupBossForStage(state.stage);
            bool collapse = state.dead && !IsBossDefeatedCollapse;
            currentHp = state.hp; maxHp = state.maxHp; currentPhase = (BossPhase)state.phase; fsmState = (BossFSMState)state.state;
            _replicaPosition = new Vector3(state.x, state.y, state.z); _replicaYaw = state.yaw;
            aggroTarget = RaidCombatAuthority.Resolve(state.target); _currentCastSkillName = state.castName;
            if (state.stage == 1) stage1CombatRadius = state.radius; else if (state.stage == 2) stage2CombatRadius = state.radius; else stage3CombatRadius = state.radius;
            _replicaCastEnd = sampledTime + state.castRemaining;
            _castRemainingTime = state.castRemaining; _castTotalDuration = state.castTotal; _groggyRemainingTime = state.groggy;
            if (mechanicAlertBannerText != null) mechanicAlertBannerText.text = state.alert;
            if (collapse) PlayBossCollapseAnimation();
        }
        private void UpdateReplicaBoss()
        {
            if (IsCasting) _castRemainingTime = Mathf.Max(0, _replicaCastEnd - RaidCombatAuthority.ServerTime);
            float t = 1 - Mathf.Exp(-18 * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, _replicaPosition, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, _replicaYaw, 0), t);
        }
        public void CapturePartyMechanics(List<RaidCombatMechanic> output)
        {
            foreach (var marker in _activePartyMarkers)
            {
                if (marker == null || marker.followTarget == null) continue;
                bool stack = marker == _activeStackMarker;
                output.Add(new RaidCombatMechanic { id = marker.GetInstanceID(), kind = stack ? 4 : 3, target = RaidCombatAuthority.Id(marker.followTarget),
                    x = marker.followTarget.position.x, z = marker.followTarget.position.z, outer = stack ? stackRadius : _currentSpreadRadius,
                    start = _networkCastStarted, resolve = _networkCastStarted + _castTotalDuration, end = _networkCastStarted + _castTotalDuration });
            }
            if (_isTetherActive)
            {
                Transform end = _isTetherInterceptedByPlayer ? (_tetherInterceptingBot != null ? _tetherInterceptingBot.transform : _tetherInterceptingPlayer) : _tetherTankTarget;
                if (end != null) output.Add(new RaidCombatMechanic { id = -2, kind = 5, target = RaidCombatAuthority.Id(end), x = transform.position.x, z = transform.position.z,
                    start = _networkCastStarted, resolve = _networkCastStarted + _castTotalDuration, end = _networkCastStarted + _castTotalDuration, intercepted = _isTetherInterceptedByPlayer });
            }
        }
    }
    public partial class BossAddMinion
    {
        private Vector3 _replicaPosition;
        private float _replicaYaw;
        public RaidCombatEnemy CaptureNetworkCombat(int id) => new RaidCombatEnemy { id = id, brandEnd=GetComponent<CombatStatusEffects>()?.BrandEnd??0, stagger=GetComponent<CombatCounterTarget>()?.Stagger??0,counterWindowEnd=GetComponent<CombatCounterTarget>()?.WindowEnd??0,type = (int)addType, name = addDisplayName,
            x = transform.position.x, y = transform.position.y, z = transform.position.z, yaw = transform.eulerAngles.y, hp = currentHp, maxHp = maxHp, radius = combatRadius,
            dead = isDead, casting = _isCastingHeavyStrike, castName = _currentCastSkillName, castRemaining = 1 - _castProgress, castTotal = 1, target = RaidCombatAuthority.Id(aggroTarget) };
        public void ApplyNetworkCombat(RaidCombatEnemy state)
        {
            CombatCounterTarget.Ensure(transform).Restore(state.stagger,state.counterWindowEnd);
            CombatStatusEffects.Ensure(gameObject).RestoreBrand(state.brandEnd);
            currentHp = state.hp; maxHp = state.maxHp; isDead = state.dead; _isCastingHeavyStrike = state.casting; _currentCastSkillName = state.castName;
            _castProgress = 1 - state.castRemaining; aggroTarget = RaidCombatAuthority.Resolve(state.target);
            _replicaPosition = new Vector3(state.x, state.y, state.z); _replicaYaw = state.yaw;
        }
        private void UpdateReplicaAdd()
        {
            float t = 1 - Mathf.Exp(-18 * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, _replicaPosition, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, _replicaYaw, 0), t);
            UpdateOverheadHpBar();
            if (_tetherOrAttackLine != null)
            {
                _tetherOrAttackLine.enabled = !isDead && aggroTarget != null && (addType == AddMinionType.PowerSupplier_BossTether || IsCastingSkill);
                if (_tetherOrAttackLine.enabled) { _tetherOrAttackLine.SetPosition(0, transform.position); _tetherOrAttackLine.SetPosition(1, aggroTarget.position + Vector3.up); }
            }
        }
    }
    public partial class AoETelegraph
    {
        private float _networkStart;
        private bool _networkReplica;
        public RaidCombatMechanic CaptureNetworkMechanic() => new RaidCombatMechanic { id = GetInstanceID(), kind = (int)telegraphType,
            x = transform.position.x, z = transform.position.z, yaw = transform.eulerAngles.y, inner = innerRadius, outer = outerRadius, angle = coneAngleDeg,
            start = _networkStart, resolve = _networkStart + _duration, end = _networkStart + _duration };
        public void ApplyNetworkTiming(RaidCombatMechanic state) { _networkReplica = true; _networkStart = state.start; _duration = Mathf.Max(.01f, state.resolve - state.start); }
    }
    public partial class HealingSanctuaryZone
    {
        private float _networkStart;
        public RaidCombatMechanic CaptureNetworkMechanic() => new RaidCombatMechanic { id = GetInstanceID(), kind = 6,
            x = transform.position.x, z = transform.position.z, outer = radius, start = _networkStart, resolve = _networkStart + duration, end = _networkStart + duration };
        public void CancelIfSource(Transform source) { if (_source == source) { gameObject.SetActive(false); Destroy(gameObject); } }
    }
    public partial class PartyBotAI
    {
        public void CaptureNetworkCombat(RaidCombatActor state)
        {
            state.hp = currentHp; state.maxHp = maxHp; state.shield = currentShield; state.dead = isDead; state.defense = _tankDefensiveTimer;
            state.speed = moveSpeed; state.target = RaidCombatAuthority.Id(CurrentActionTarget);
            // Native bots keep their existing AI cadence; expose its actual timer in common state.
            float interval = role == PartyBotRole.Tank ? tankAttackInterval : role == PartyBotRole.Healer ? healCheckInterval : role == PartyBotRole.MeleeDPS ? meleeAttackInterval : rangedFireInterval;
            state.gcdTotal = interval; state.gcd = Mathf.Max(0, interval - _actionTimer);
            if (role == PartyBotRole.Healer) { state.cd4 = _rescueCooldownTimer; state.cd4Total = rescueCooldown; }
        }
    }
    public sealed partial class BattleResurrection
    {
        public void CancelIfTarget(Transform target) { if (_target != null && _target.transform == target) Cancel(); }
    }
    public sealed partial class RaidNetworkSession
    {
        private readonly Dictionary<int, RaidReplicaProjectile> _replicaProjectiles = new Dictionary<int, RaidReplicaProjectile>();
        private RaidCombatMechanic[] CaptureMechanics()
        {
            var result = new List<RaidCombatMechanic>();
            foreach (var telegraph in FindObjectsByType<AoETelegraph>(FindObjectsSortMode.None)) result.Add(telegraph.CaptureNetworkMechanic());
            foreach (var zone in FindObjectsByType<HealingSanctuaryZone>(FindObjectsSortMode.None)) result.Add(zone.CaptureNetworkMechanic());
            foreach (var projectile in FindObjectsByType<RangedBotProjectile>(FindObjectsSortMode.None)) result.Add(projectile.CaptureNetworkMechanic());
            StageManager.Instance.boss.CapturePartyMechanics(result); return result.ToArray();
        }
        private void ApplyNetworkAdds(RaidCombatEnemy[] states)
        {
            var live = new HashSet<int>(); var spawner = StageManager.Instance.boss.addSpawner;
            foreach (var state in states)
            {
                live.Add(state.id);
                if (!_combatAdds.TryGetValue(state.id, out var add) || add == null)
                {
                    var obj = new GameObject("NetworkAdd_" + state.id); add = obj.AddComponent<BossAddMinion>();
                    add.InitializeAdd((AddMinionType)state.type, StageManager.Instance.boss, spawner, new Vector3(state.x, state.y, state.z), StageManager.Instance.boss.EncounterTemplate);
                    _combatAdds[state.id] = add; spawner.activeAdds.Add(add);
                }
                add.ApplyNetworkCombat(state);
            }
            foreach (int id in new List<int>(_combatAdds.Keys)) if (!live.Contains(id))
            {
                var add = _combatAdds[id]; if (add != null) { if (TargetingManager.Instance?.CurrentTarget == add.transform) TargetingManager.Instance.ClearTarget(); spawner.activeAdds.Remove(add); add.gameObject.SetActive(false); Destroy(add.gameObject); }
                _combatAdds.Remove(id);
            }
        }
        private void ApplyReplicaMechanics(RaidCombatMechanic[] mechanics)
        {
            var live = new HashSet<int>();
            foreach (var state in mechanics)
            {
                if (ServerTime >= state.end + .15f) continue;
                live.Add(state.id); Transform target = CombatTarget(state.target);
                if (state.kind <= 2 || state.kind == 6)
                {
                    if (!_replicaTelegraphs.TryGetValue(state.id, out var aoe) || aoe == null)
                    {
                        var obj = new GameObject("NetworkTelegraph_" + state.id); obj.transform.position = new Vector3(state.x, 0, state.z); obj.transform.rotation = Quaternion.Euler(0, state.yaw, 0);
                        aoe = obj.AddComponent<AoETelegraph>();
                        if (state.kind == 6) { aoe.baseFillColor = new Color(.2f, 1, .4f, .15f); aoe.borderColor = Color.green; aoe.progressFillColor = new Color(.2f, 1, .4f, .3f); }
                        aoe.Setup(state.kind == 6 ? AoETelegraphType.Circle_Radius6 : (AoETelegraphType)state.kind, state.end - state.start, state.inner, state.outer, state.angle);
                        _replicaTelegraphs[state.id] = aoe;
                    }
                    aoe.transform.SetPositionAndRotation(new Vector3(state.x, 0, state.z), Quaternion.Euler(0, state.yaw, 0));
                    aoe.ApplyNetworkTiming(state);
                }
                else if (state.kind == 7)
                {
                    if (!_replicaProjectiles.TryGetValue(state.id, out var projectile) || projectile == null)
                        _replicaProjectiles[state.id] = projectile = RaidReplicaProjectile.Create();
                    projectile.Apply(state, target, ServerTime);
                }
                else if ((state.kind == 3 || state.kind == 4) && target != null)
                {
                    if (!_replicaMarkers.TryGetValue(state.id, out var marker) || marker == null)
                        _replicaMarkers[state.id] = state.kind == 3 ? AttachedPartyMechanicMarker.SpawnSpreadMarker(target, state.outer) : AttachedPartyMechanicMarker.SpawnStackMarker(target, state.outer);
                    _replicaMarkers[state.id].followTarget = target;
                }
                else if (state.kind == 5 && target != null)
                {
                    if (!_replicaLines.TryGetValue(state.id, out var line) || line == null)
                    {
                        line = new GameObject("NetworkTether").AddComponent<LineRenderer>(); line.sharedMaterial = new Material(Shader.Find("Sprites/Default")); line.positionCount = 2;
                        line.startWidth = line.endWidth = .16f; _replicaLines[state.id] = line;
                    }
                    line.startColor = line.endColor = state.intercepted ? Color.green : Color.red;
                    line.SetPosition(0, new Vector3(state.x, 1.15f, state.z)); line.SetPosition(1, target.position);
                }
            }
            RemoveReplicaObjects(_replicaTelegraphs, live); RemoveReplicaObjects(_replicaMarkers, live); RemoveReplicaObjects(_replicaLines, live); RemoveReplicaObjects(_replicaProjectiles, live);
        }
        private static void RemoveReplicaObjects<T>(Dictionary<int, T> objects, HashSet<int> live) where T : Component
        {
            foreach (int id in new List<int>(objects.Keys)) if (!live.Contains(id)) { if (objects[id] != null) { if (objects[id] is LineRenderer line) Destroy(line.sharedMaterial); Destroy(objects[id].gameObject); } objects.Remove(id); }
        }
        private void ClearReplicaMechanics()
        { var empty = new HashSet<int>(); RemoveReplicaObjects(_replicaTelegraphs, empty); RemoveReplicaObjects(_replicaMarkers, empty); RemoveReplicaObjects(_replicaLines, empty); RemoveReplicaObjects(_replicaProjectiles, empty); }
    }
}
