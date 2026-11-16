using System;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    public sealed partial class CharacterActor
    {
        internal bool RestoreCampaignBuild(CampaignPartySlot slot)
        {
            if (RaidCombatAuthority.Replica || CampaignRuntime.Instance?.BuildLocked != true || slot?.character == null) return false;
            var definition = CharacterCatalog.Find(slot.character.jobId);
            if (definition == null || !definition.playable) return false;
            var candidate = new CharacterRuntimeState(definition);
            if (!candidate.Restore(slot.character)) return false;
            GetComponent<AegisPaladin>()?.ReleaseRouteEffects(); GetComponent<CharacterKit>()?.Clear();
            GetComponent<CharacterPartyStats>()?.ResetFactors(); GetComponent<CombatStatusEffects>()?.Clear();
            if (_player != null)
            {
                _player.ApplyRoleStatsAndVisuals((PlayerRole)slot.role, true);
                if (definition.legacyRole == 0) _player.maxHp = _player.currentHp = definition.baseHp;
                _player.moveSpeed = definition.moveSpeed;
                _player.combatRadius = definition.combatRadius;
                var avatar = GetComponent<NetworkRaidAvatar>();
                if (avatar != null) avatar.Configure(avatar.Id, avatar.IsLocal, definition.combatRadius);
                _player.RestoreCampaignCards(slot.cards);
                _player.cardManager?.RestoreCampaignCards(slot.cards);
            }
            _state = candidate; _role = definition.legacyRole; ObserveResources();
            if (definition.id == AegisPaladinData.JobId) AegisPaladin.Ensure(gameObject);
            if (CharacterRosterData.Find(definition.id) != null) CharacterKit.Ensure(gameObject);
            if(NineKeyHealerKit.Supports(definition))NineKeyHealerKit.Ensure(gameObject).Clear();
            if(NineKeyTankKit.Supports(definition)){var tank=NineKeyTankKit.Ensure(gameObject);tank.Clear();tank.Recalculate();}
            if(NineKeyDamageKit.Supports(definition))NineKeyDamageKit.Ensure(gameObject).Clear();
            _previous = transform.position; _singleCastTarget = null; _identityHeld = false;
            GetComponent<AegisPaladin>()?.RecalculateStats(); GetComponent<CharacterKit>()?.Recalculate();
            if (_player != null)
            {
                _player.currentHp = _player.maxHp; _player.playerRoleDisplayName = definition.name;
                var avatar = GetComponent<NetworkRaidAvatar>();
                if (avatar != null) avatar.RefreshJobLabel();
                else _player.SetNetworkDisplayName(definition.name, NetworkRaidAvatar.RoleColor(slot.role));
            }
            if (CampaignRuntime.Instance?.ResumingSettlement == true)
            {
                if (_player != null) _player.currentHp = _player.maxHp * Mathf.Clamp01(slot.healthFraction);
                var bot = GetComponent<PartyBotAI>();
                if (bot != null) bot.currentHp = bot.maxHp * Mathf.Clamp01(slot.healthFraction);
            }
            return true;
        }
        internal bool RestoreCampaignState(CharacterStateSnapshot snapshot)
        {
            if (RaidCombatAuthority.Replica || CampaignRuntime.Instance?.Restoring != true || _state == null) return false;
            if(!_state.Restore(snapshot))return false;
            // Stage-boundary restore may change the magazine after PrepareEncounter.
            // Rebuild transient route state from these authoritative resources.
            GetComponent<NineKeyTankKit>()?.Clear();GetComponent<NineKeyHealerKit>()?.Clear();GetComponent<NineKeyDamageKit>()?.Clear();
            return true; // No resource gain/cast/reward events on restore.
        }
    }
    public partial class PlayerController
    {
        internal void RestoreCampaignCards(CampaignCardStack[] cards)
        {
            var counts = new int[31]; foreach (var c in cards) counts[CampaignCardIds.Index(c.id)] = c.stacks;
            masterOfRearStacks = counts[0]; ghostStepStacks = counts[1]; thornArmorStacks = counts[2]; tacticalRescueStacks = counts[3];
            berserkerShoutStacks = counts[4]; vampiricBladeStacks = counts[5]; swiftAwakeningStacks = counts[6];
            hasMasterOfRear = counts[0] > 0; hasGhostStep = counts[1] > 0; hasThornArmor = counts[2] > 0; hasTacticalRescue = counts[3] > 0;
            hasBerserkerShout = counts[4] > 0; hasVampiricBlade = counts[5] > 0;
            thornReflectDamage = 600 * thornArmorStacks; positionalAngleBonusDeg = 15 * ghostStepStacks;
            moveSpeed *= Mathf.Pow(1.3f, ghostStepStacks);
            if (combatController != null) combatController.currentGcdDuration = Mathf.Max(combatController.minGcdDuration, combatController.baseGcdDuration - .4f * swiftAwakeningStacks);
            if (!RaidCombatAuthority.Active && targetBoss != null) targetBoss.positionalAngleBonus = positionalAngleBonusDeg;
        }
    }
    public partial class RoguelikeCardManager
    {
        internal CampaignCardStack[] CaptureCampaignCards() => _acquiredCardStacks.OrderBy(p => (int)p.Key).Select(p => new CampaignCardStack { id = CampaignCardIds.Id((int)p.Key), stacks = p.Value }).ToArray();
        internal void RestoreCampaignCards(CampaignCardStack[] cards)
        {
            var stacks = new int[31]; foreach (var card in cards) stacks[CampaignCardIds.Index(card.id)] = card.stacks;
            ApplyNetworkCardStacks(stacks); ResetTemporaryCombatState();
            // No ApplyCardEffect: restoration must not fire shout pulses, popups or reward callbacks.
        }
    }
    public partial class StageManager
    {
        internal void RestoreCampaignHeader(CampaignCheckpoint checkpoint)
        { RunId = checkpoint.runId; currentStage = checkpoint.nextStage; _totalPlayTime = checkpoint.runSeconds; UpdateStageHeaderUI(); }
        internal bool PrepareCampaignEncounter()
        {
            var campaign = CampaignRuntime.Instance;
            if (campaign?.Resume == null) return false;
            campaign.BeginRestoreBoundary();
            bool prepared = PrepareEncounter(currentStage, false, false, true);
            if (prepared) _hasStartedEncounter = true;
            return prepared;
        }
        internal void ContinueCampaignCountdown() { StartCoroutine(PreparedCountdown(Progress.Revision)); }
    }
}
