using UnityEngine;
namespace RaidRogue
{
    public partial class BossController
    {
        // Semantic completion only: never cleanup, death, replica or reset.
        private void NotifyDamageCastEnded()
        {
            if(RaidCombatAuthority.Replica||StageManager.CombatSuspended||currentHp<=0||string.IsNullOrEmpty(_currentCastSkillName)||_castTotalDuration<=0)return;
            NineKeyDamageKit.NotifyBossAction(transform,DamageSealTrigger.CastEnd);
        }
        private void NotifyDamagePatternMoveEnded()
        {
            if(RaidCombatAuthority.Replica||StageManager.CombatSuspended||currentHp<=0)return;
            NineKeyDamageKit.NotifyBossAction(transform,DamageSealTrigger.PatternMoveEnd);
        }
    }
}
