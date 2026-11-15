using UnityEngine;

namespace RaidRogue
{
    public sealed partial class CharacterActor
    {
        internal bool ConfigureTraining(CharacterRuntimeState candidate)
        {
            if (!CharacterTrainingSession.CanEdit(this) || candidate == null || !AssignDefinition(candidate.Definition)) return false;
            InterruptCast();
            _player.ApplyRoleStatsAndVisuals((PlayerRole)CharacterRosterData.Role(candidate.Definition), true);
            _state = candidate; ObserveResources(); _role = candidate.Definition.legacyRole;
            _player.maxHp = _player.currentHp = Definition.baseHp;
            _player.moveSpeed = Definition.moveSpeed;
            _player.playerRoleDisplayName = Definition.name;
            _player.SetNetworkDisplayName(Definition.name, NetworkRaidAvatar.RoleColor(CharacterRosterData.Role(Definition)));
            _previous = transform.position; _identityHeld = false; _singleCastTarget = null;
            GetComponent<AegisPaladin>()?.RecalculateStats(); GetComponent<CharacterKit>()?.Recalculate();
            PartyLife.Ensure(gameObject).ResetForEncounter(); RefreshMovementCharges();
            return true;
        }
        internal void TrainingRefill(bool resources, bool cooldowns)
        {
            if (!CharacterTrainingSession.CanEdit(this)) return;
            if (resources) _state.ApplySkillTest(SkillTestOperation.FillResources, out _);
            if (cooldowns) { InterruptCast(); _state.ApplySkillTest(SkillTestOperation.RefreshJobCooldowns, out _); RefreshMovementCharges(); }
        }
        public bool HasExecutableHandler(CharacterSkillDefinition skill) => skill != null && skill.implemented &&
            (skill.legacyAction > 0 || skill.handlerId == "legacy.dash" || _handlers.ContainsKey(skill.handlerId));
    }
    public partial class PlayerController
    {
        internal void TrainingPlace(Vector3 position)
        {
            if (!CharacterTrainingSession.Active) return;
            GetComponent<CharacterActor>()?.InterruptCast();
            _dash.Cancel(); _dashPress.RequireRelease();
            _isBeingRescued = _isBeingKnockedBack = IsMovingXZ = false;
            if (_dashPresentation != null) _dashPresentation.ClearTrail();
            transform.SetPositionAndRotation(position, Quaternion.identity);
        }
        internal void TrainingHealth(float fraction, bool clearShields)
        {
            if (!CharacterTrainingSession.Active) return;
            if (clearShields) { _metricsShields.Clear(); currentShield = 0; }
            if (!IsDead) currentHp = maxHp * Mathf.Clamp(fraction, .01f, 1);
        }
    }
    public partial class PartyBotAI
    {
        internal void TrainingPlace(Vector3 position)
        {
            if (!CharacterTrainingSession.Active) return;
            GetComponent<CharacterActor>()?.InterruptCast();
            _isBeingRescuedByPlayer = _isBeingKnockedBack = _isInterceptingTetherForPlayer = false;
            transform.SetPositionAndRotation(position, Quaternion.identity);
        }
        internal void TrainingHealth(float fraction, bool clearShields)
        {
            if (!CharacterTrainingSession.Active) return;
            if (clearShields) { _metricsShields.Clear(); currentShield = 0; }
            if (!isDead) currentHp = maxHp * Mathf.Clamp(fraction, .01f, 1);
        }
    }
}
