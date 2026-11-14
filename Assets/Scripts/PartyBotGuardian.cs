using UnityEngine;

namespace RaidRogue
{
    public partial class PartyBotAI
    {
        private float _guardianCounterReady, _guardianDefenseReady;
        private RosterJob GuardianJob => System.Array.Find(CharacterRosterData.Jobs, j => j.Role == (int)role + 1);
        // Legacy role bots already attack through their normal timers/projectiles. Attach the
        // corresponding basic skill's stagger, never a proportion of the HP damage dealt.
        public float GuardianAttackStagger => role == PartyBotRole.Tank ? AegisPaladinData.SwordStagger[0] : GuardianJob?.Ability("g1")?.stagger ?? 0;
        private void UpdateGuardianActions()
        {
            if (targetBoss == null || !targetBoss.GuardianNormal) return;
            float now = RaidCombatAuthority.ServerTime;
            if (role == PartyBotRole.Tank && now >= _guardianDefenseReady &&
                targetBoss.GuardianPatternNow == GuardianPattern.Punishment && targetBoss.GuardableAttackImminent(transform, 1.5f))
            {
                ActivateTankDefensive(targetBoss.GuardianHard ? 4.5f : 3f, "징벌 대비 · 방어기"); _guardianDefenseReady = now + 15;
            }
            if (now < _guardianCounterReady || !targetBoss.GuardianAiCounterReady(transform)) return;
            var ability = GuardianJob?.Ability("c");
            float range = role == PartyBotRole.Tank ? MeleeAttackRange : ability?.range ?? MeleeAttackRange;
            var counter = CombatCounterTarget.Ensure(targetBoss.transform);
            // Rotation is the existing movement/turning result, never snapped to satisfy a hit.
            if (!counter.CanCounter(transform, range)) return;
            _guardianCounterReady = now + (role == PartyBotRole.Tank ? AegisPaladinData.CounterCooldown : ability?.cooldown ?? 10);
            if (counter.TryCounter(transform, range))
            {
                targetBoss.ReceiveBotAttackDamage(role == PartyBotRole.Tank ? AegisPaladinData.CounterPower : Mathf.RoundToInt(ability?.power ?? 150), true, transform);
                ShowBeamEffect(transform.position, targetBoss.transform.position + Vector3.up, Color.cyan, .2f, .15f);
            }
        }
    }
}
