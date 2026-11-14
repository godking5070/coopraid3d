using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    public partial class PlayerCombatController
    {
        [Min(0)] public float rangedSkillRange = 20f;
        [Min(0)] public float friendlySkillRange = 20f;
        private Transform SelectedCombatTarget => RaidCombatAuthority.Active
            ? (RaidCombatAuthority.Local(this) && RaidCombatAuthority.ExecutingActor == 0 ? TargetingManager.Instance?.CurrentTarget : GetComponent<NetworkRaidAvatar>()?.CombatTarget)
            : TargetingManager.Instance?.CurrentTarget;
        public bool ValidateCombatSkill(int skill, out string reason)
        {
            reason = "";
            if(StageManager.Instance?.boss?.MarionetteControlled(transform)==true)
            {reason="조종당함 · 인형의 실 절단 대기";return false;}
            if ((StageManager.Instance?.boss?.GuardianClashHolding(transform)==true ||
                StageManager.Instance?.boss?.CampanellaLockHolding(transform)==true) && skill!=3)
            { reason="힘겨루기 중에는 [3] 자기 방어기와 포션만 사용할 수 있습니다"; return false; }
            if (!CharacterActor.Ensure(gameObject).Available(CharacterSlots.LegacySlot(skill), out reason)) return false;
            if (!CanUseCombatAction || playerController.IsBeingRescued || playerController.IsBeingKnockedBack) reason = "현재 행동할 수 없습니다";
            else if (skill <= 2 && (IsOnGcd || _isCastingSpell)) reason = "글로벌 쿨타임 또는 시전 중입니다";
            else if ((skill == 3 && _skill3CooldownRemaining > 0) || (skill == 4 && _skill4CooldownRemaining > 0)) reason = "스킬 쿨타임 중입니다";
            else if (CurrentRole == PlayerRole.Oracle_Healer && (skill == 2 || skill == 4))
            {
                var target = ResolveFriendlyCombatTarget(skill == 4);
                if (target == null) reason = "유효한 아군을 선택하세요";
                else if (!CombatRange.LivingPartyMember(target)) reason = "사망한 아군에게는 [5] 부활을 사용하세요";
                else if (!CombatRange.InRange(transform, target, friendlySkillRange)) reason = "대상이 사거리 밖에 있습니다";
            }
            else if (skill <= 2 || (skill == 4 && (CurrentRole == PlayerRole.Guardian_Tank || CurrentRole == PlayerRole.Blade_MeleeDPS)))
            {
                var target = GetOffensiveTarget();
                float range = rangedSkillRange;
                if (CurrentRole == PlayerRole.Guardian_Tank && skill == 1) range = meleeRanges.Get(MeleeSkill.TankPrimary, _combo1Stage);
                if (CurrentRole == PlayerRole.Blade_MeleeDPS) range = meleeRanges.Get(skill == 1 ? MeleeSkill.BladeFlank : skill == 2 ? MeleeSkill.BladeRear : MeleeSkill.BladeGale, skill == 2 ? _combo2Stage : _combo1Stage);
                if (target == null || !CombatRange.LivingEnemy(target)) reason = "공격할 수 있는 대상이 없습니다";
                else if (!CombatRange.InRange(transform, target, range)) reason = "대상이 사거리 밖에 있습니다";
            }
            return reason.Length == 0;
        }
        private Transform ResolveFriendlyCombatTarget(bool rescue)
        {
            var selected = SelectedCombatTarget;
            if (selected != null && PartyLife.OrderedMembers().Contains(selected)) return rescue && selected == transform ? null : selected;
            Transform best = null; float score = float.MaxValue;
            foreach (var member in PartyLife.OrderedMembers())
            {
                if (!CombatRange.LivingPartyMember(member) || (rescue && member == transform) || !CombatRange.InRange(transform, member, friendlySkillRange)) continue;
                var pc = member.GetComponent<PlayerController>(); var bot = member.GetComponent<PartyBotAI>();
                float value = rescue ? -BattleResurrection.FlatDistance(transform.position, member.position) : pc != null ? pc.HpRatio : bot.HpRatio;
                if (rescue && targetBoss?.ActiveTelegraph != null && targetBoss.ActiveTelegraph.IsPointInsideXZ(member.position)) value -= 100;
                if (value < score) { score = value; best = member; }
            }
            return best;
        }
        public void InterruptForConnectionLoss()
            => InterruptForControl();
        public void InterruptForControl()
        {
            GetComponent<CharacterActor>()?.InterruptCast();
            // Control interruption preserves combo, buffs and cooldowns.
            _isCastingSpell = false; _castElapsed = 0; _castingSpellName = "";
        }
        public void ReportNetworkFailure(string reason) => ShowRangeFeedback(reason);
        public void CaptureNetworkCombat(RaidCombatActor state)
        {
            state.gcd = GcdRemaining; state.gcdTotal = GcdTotal; state.cd3 = Skill3CooldownRemaining; state.cd3Total = Skill3CooldownMax;
            state.cd4 = Skill4CooldownRemaining; state.cd4Total = Skill4CooldownMax; state.combo1 = Combo1Stage; state.combo2 = Combo2Stage;
            state.comboTime1 = Combo1TimerRemaining; state.comboTime2 = Combo2TimerRemaining;
            state.casting = IsCastingSpell; state.castName = CastingSpellName; state.castRemaining = CastRemaining; state.castTotal = CastDurationTotal; state.triplecast = TriplecastRemaining;
        }
        public void ApplyNetworkCombat(RaidCombatActor state)
        {
            _gcdRemaining = state.gcd; _gcdTotalForCurrentCycle = state.gcdTotal;
            _skill3CooldownRemaining = state.cd3; skill3CooldownTotal = state.cd3Total; _skill4CooldownRemaining = state.cd4; skill4CooldownTotal = state.cd4Total;
            _combo1Stage = state.combo1; _combo2Stage = state.combo2; _combo1Timer = state.comboTime1; _combo2Timer = state.comboTime2;
            _isCastingSpell = state.casting; _castingSpellName = state.castName; _castDurationTotal = state.castTotal; _castElapsed = state.castTotal - state.castRemaining; _triplecastBuffRemaining = state.triplecast;
        }
    }
    public partial class PlayerController
    {
        public RaidNetworkMotor NetworkMotor => RaidNetworkSession.Multiplayer ? RaidNetworkSession.Instance.MotorFor(transform) : null;
        public void SetNetworkMoving(bool moving) => IsMovingXZ = moving;
        public void StepNetworkForcedMovement(float seconds)
        {
            if (_isBeingRescued) UpdateRescuePullMovementXZ(seconds);
            else if (_isBeingKnockedBack) UpdateKnockbackMovementXZ(seconds);
            else if(IsSkillRushing)StepSkillRush(seconds);
        }
        public float NetworkMoveSpeed => moveSpeed * (!RaidCombatAuthority.Replica && _rescueSpeedBoostTimer > 0 ? 1.35f : 1);
        public void CaptureNetworkCombat(RaidCombatActor state)
        {
            state.hp = currentHp; state.maxHp = maxHp; state.shield = currentShield; state.dead = IsDead;
            state.defense = _defensiveRemainingTime; state.tactical = _tacticalRescueBuffTimer; state.speedBoost = _rescueSpeedBoostTimer; state.speed = NetworkMoveSpeed;
            state.haste = cardManager != null ? cardManager.NetworkHaste : 0;
            state.stunned = IsStunned; state.rooted = IsMovementLocked || GetComponent<CharacterKit>()?.DashBlocked==true; state.forced = IsBeingRescued || IsBeingKnockedBack || IsSkillRushing;
            state.cards = cardManager!=null?cardManager.CaptureCardStacks():new[] { masterOfRearStacks, ghostStepStacks, thornArmorStacks, tacticalRescueStacks, berserkerShoutStacks, vampiricBladeStacks, swiftAwakeningStacks };
            combatController?.CaptureNetworkCombat(state);
        }
        public void ApplyNetworkCombat(RaidCombatActor state)
        {
            currentHp = state.hp; maxHp = state.maxHp; currentShield = state.shield;
            if (cardManager != null) cardManager.NetworkHaste = state.haste;
            _defensiveRemainingTime = state.defense; _tacticalRescueBuffTimer = state.tactical; _rescueSpeedBoostTimer = state.speedBoost;
            IsStunned = state.stunned; IsMovementLocked = state.rooted; _isBeingRescued = state.forced; _isBeingKnockedBack = false;
            moveSpeed = state.speed; ApplyBodyColor(IsDefensiveBuffActive ? defensiveBodyColor : normalBodyColor);
            if (state.cards != null && state.cards.Length >= 7)
            {
                masterOfRearStacks = state.cards[0]; ghostStepStacks = state.cards[1]; thornArmorStacks = state.cards[2]; tacticalRescueStacks = state.cards[3];
                berserkerShoutStacks = state.cards[4]; vampiricBladeStacks = state.cards[5]; swiftAwakeningStacks = state.cards[6];
                hasMasterOfRear = masterOfRearStacks > 0; hasGhostStep = ghostStepStacks > 0; hasThornArmor = thornArmorStacks > 0; hasTacticalRescue = tacticalRescueStacks > 0;
                hasBerserkerShout = berserkerShoutStacks > 0; hasVampiricBlade = vampiricBladeStacks > 0; positionalAngleBonusDeg = 15 * ghostStepStacks;
            }
            Life?.ApplyNetworkDeath(state.dead); combatController?.ApplyNetworkCombat(state);
        }
    }
    public sealed partial class PartyLife
    {
        public void ApplyNetworkDeath(bool dead)
        {
            if (dead == IsDead) return;
            if (dead)
            {
                _state.Die(); var visual = GetComponent<DualVisualController>(); _body = visual?.visual3DRoot?.transform;
                if (_body != null) { _upright = _body.localRotation; _body.localRotation = Quaternion.Euler(0, 0, 75) * _upright; }
            }
            else { _state.Revive(); if (_body != null) _body.localRotation = _upright; }
        }
    }
    public sealed partial class CommonPotion
    {
        private RaidCombatActor Replicated => RaidCombatAuthority.Replica ? GetComponent<NetworkRaidAvatar>()?.State : null;
        public void CaptureNetworkCombat(RaidCombatActor state)
        { state.potion = CooldownRemaining; state.potionTotal = (float)_state.CooldownDuration; state.protection = (float)_state.RevivalProtectionRemaining; }
    }
    public sealed partial class BattleResurrection
    {
        private RaidCombatActor Replicated => RaidCombatAuthority.Replica ? GetComponent<NetworkRaidAvatar>()?.State : null;
        public void CaptureNetworkCombat(RaidCombatActor state)
        { state.resurrecting = IsCasting; state.resurrection = Remaining; state.resurrectionTotal = (float)_state.Duration; state.resurrectionCooldown = Cooldown; }
    }
}
