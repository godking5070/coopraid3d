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
            if (targetBoss == null || !(targetBoss.GuardianNormal || targetBoss.FloraEncounter || targetBoss.CampanellaEncounter || targetBoss.MarionetteEncounter || targetBoss.JustitiaEncounter || targetBoss.JanusEncounter)) return;
            float now = RaidCombatAuthority.ServerTime;
            if(role==PartyBotRole.Healer&&targetBoss.GuardianLureRescueWindow)targetBoss.TryGuardianLureRescue(transform,out _);
            if (role == PartyBotRole.Tank && now >= _guardianDefenseReady &&
                (targetBoss.GuardableAttackImminent(transform, 1.5f) || targetBoss.FloraTankbusterImminent(transform,1.5f) || targetBoss.CampanellaTankbusterImminent(transform,1.5f) || targetBoss.MarionetteTankbusterImminent(transform,1.5f) || targetBoss.JustitiaTankbusterImminent(transform,1.5f) || targetBoss.JanusTankbusterImminent(transform,1.5f)))
            {
                ActivateTankDefensive(targetBoss.GuardianHard ? 4.5f : 3f, "징벌 대비 · 방어기"); _guardianDefenseReady = now + 15;
            }
            if (now < _guardianCounterReady || !(targetBoss.GuardianNormal ? targetBoss.GuardianAiCounterReady(transform) :
                targetBoss.FloraEncounter ? targetBoss.FloraAiCounterReady(transform) :
                targetBoss.CampanellaEncounter ? targetBoss.CampanellaAiCounterReady(transform) : targetBoss.MarionetteEncounter ? targetBoss.MarionetteAiCounterReady(transform) : targetBoss.JanusEncounter ? targetBoss.JanusAiCounterReady(transform) : targetBoss.JustitiaAiCounterReady(transform))) return;
            var ability = GuardianJob?.Ability("c");
            float range = role == PartyBotRole.Tank ? MeleeAttackRange : ability?.range ?? MeleeAttackRange;
            var counterTarget=targetBoss.JanusEncounter?targetBoss.JanusAiCounterTarget(transform):targetBoss.transform;
            if(counterTarget==null)return;
            var counter = CombatCounterTarget.Ensure(counterTarget);
            // Rotation is the existing movement/turning result, never snapped to satisfy a hit.
            if (!counter.CanCounter(transform, range)) return;
            _guardianCounterReady = now + (role == PartyBotRole.Tank ? AegisPaladinData.CounterCooldown : ability?.cooldown ?? 10);
            if (counter.TryCounter(transform, range))
            {
                int power=role == PartyBotRole.Tank ? AegisPaladinData.CounterPower : Mathf.RoundToInt(ability?.power ?? 150);
                if(counterTarget==targetBoss.transform)targetBoss.ReceiveBotAttackDamage(power,true,transform);
                else counterTarget.GetComponent<BossAddMinion>()?.ReceiveBotAttack(power,transform);
                ShowBeamEffect(transform.position,counterTarget.position+Vector3.up,Color.cyan,.2f,.15f);
            }
        }
    }
}
