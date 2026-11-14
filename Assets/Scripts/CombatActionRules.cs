using System;
using System.Collections.Generic;

namespace RaidRogue
{
    // Separate action provenance from damage kind: a direct-damage auto attack is
    // not a deliberate combo input, and a triggered heal is not a healing input.
    public enum CombatActionOrigin { Unknown, SkillInput, AutoAttack, Periodic, Secondary, TriggeredHealing }
    public enum CombatActionPurpose { Unknown, Attack, Heal, Defense }

    public readonly struct CombatActionReceipt
    {
        public readonly int ActorId, EncounterEpoch, ComboChain, ComboStep;
        public readonly ulong ActionId;
        public readonly CombatActionOrigin Origin;
        public readonly CombatActionPurpose Purpose;
        public readonly bool InCombat, ValidTarget, Resolved;

        // One server-issued ActionId per accepted skill use, shared by all its
        // targets/hits. Never use the per-hit CombatHitContext.EventId here.
        // Resolved means attack hit / direct heal executed on a living target.
        // Effective healing is deliberately absent: overhealing still qualifies.
        public CombatActionReceipt(int actorId, int encounterEpoch, ulong actionId,
            CombatActionOrigin origin, CombatActionPurpose purpose, int comboChain,
            int comboStep, bool inCombat, bool validTarget, bool resolved)
        {
            ActorId = actorId; EncounterEpoch = encounterEpoch; ActionId = actionId;
            Origin = origin; Purpose = purpose; ComboChain = comboChain; ComboStep = comboStep;
            InCombat = inCombat; ValidTarget = validTarget; Resolved = resolved;
        }
    }

    public static class CombatActionRules
    {
        // This is eligibility, not a balance value or a resource grant. Route-specific
        // resource replacement (e.g. tank A2 / healer alchemist) remains the caller's job.
        public static bool IsDirectComboAction(CombatActionReceipt action)
        {
            if (action.ActorId == 0 || action.EncounterEpoch <= 0 || action.ActionId == 0 ||
                action.Origin != CombatActionOrigin.SkillInput || !action.InCombat ||
                !action.ValidTarget || !action.Resolved || action.ComboStep < 1 || action.ComboStep > 3)
                return false;
            return action.ComboChain == 1 && action.Purpose == CombatActionPurpose.Attack ||
                action.ComboChain == 2 && (action.Purpose == CombatActionPurpose.Attack || action.Purpose == CombatActionPurpose.Heal);
        }

        public static bool IsComboCompletion(CombatActionReceipt action)
            => IsDirectComboAction(action) && action.ComboStep == 3;
    }

    // A route supplies one replacement policy, never a base plus a route bonus.
    // These are explicit input rewards only; ammo spending, reservations, typed
    // ingredients and projection history require their own route state machine.
    public sealed class CombatComboResourcePolicy
    {
        public string ResourceId { get; }
        private readonly float[] _amounts;
        public CombatComboResourcePolicy(string resourceId,float first1,float second1,float last1,
            float first2,float second2,float last2)
        {
            if(string.IsNullOrWhiteSpace(resourceId))throw new ArgumentException(nameof(resourceId));
            ResourceId=resourceId;_amounts=new[]{first1,second1,last1,first2,second2,last2};
            foreach(float amount in _amounts)
                if(float.IsNaN(amount)||float.IsInfinity(amount)||amount<0)throw new ArgumentOutOfRangeException(nameof(amount));
        }
        public float Reward(CombatActionReceipt action) => CombatActionRules.IsDirectComboAction(action)
            ? _amounts[(action.ComboChain-1)*3+action.ComboStep-1] : 0;
        public bool SupportsIntegerResource
        {
            get{foreach(float amount in _amounts)if(amount!=Math.Floor(amount))return false;return true;}
        }
    }

    // Synchronous effect scope owned by the authoritative actor. The receipt is
    // issued after validation, before the handler, and closed even on failure.
    // Delayed projectiles must use a separate explicit lifetime; they cannot
    // borrow whatever action happens to be executing when they land.
    public sealed class CombatActionExecution
    {
        private readonly int _actorId;
        private readonly CombatActionClaims _claims;
        private ulong _nextId;
        private CombatActionReceipt _action;
        public bool Active { get; private set; }
        public CombatActionExecution(int actorId)
        { _actorId=actorId;_claims=new CombatActionClaims(actorId,1); }
        public bool Begin(CombatActionPurpose purpose,int chain,int step,bool inCombat,bool validTarget)
        {
            if(Active||_nextId==ulong.MaxValue)return false;
            _action=new CombatActionReceipt(_actorId,_claims.EncounterEpoch,++_nextId,
                CombatActionOrigin.SkillInput,purpose,chain,step,inCombat,validTarget,false);
            Active=true;return true;
        }
        public bool TryResolveResource(CombatComboResourcePolicy policy,bool resolved,out float amount)
        {
            amount=0;if(!Active||!resolved)return false;
            var a=_action;
            return _claims.TryClaimResource(policy,new CombatActionReceipt(a.ActorId,a.EncounterEpoch,a.ActionId,
                a.Origin,a.Purpose,a.ComboChain,a.ComboStep,a.InCombat,a.ValidTarget,true),out amount);
        }
        public void End(){Active=false;_action=default;}
        public bool TryResolveDirectGrowth(string key,bool resolved)
        {
            if(!Active||!resolved||string.IsNullOrEmpty(key))return false;
            var a=_action;
            return _claims.TryClaimDirectGrowth(key,new CombatActionReceipt(a.ActorId,a.EncounterEpoch,a.ActionId,
                a.Origin,a.Purpose,a.ComboChain,a.ComboStep,a.InCombat,a.ValidTarget,true));
        }
        public bool TryResolveFinisherGauge(bool resolved)
        {
            if(!Active||!resolved||_action.ComboStep!=3)return false;
            var a=_action;
            return _claims.TryClaimDirectGrowth("ultimate.finisher",new CombatActionReceipt(a.ActorId,a.EncounterEpoch,a.ActionId,
                a.Origin,a.Purpose,a.ComboChain,a.ComboStep,a.InCombat,a.ValidTarget,true));
        }
        public void Reset()
        {
            End();
            if(_claims.EncounterEpoch==int.MaxValue)throw new InvalidOperationException("Combat action epoch exhausted");
            _claims.BeginEncounter(_claims.EncounterEpoch+1);
        }
    }

    // Per actor, per encounter, authoritative main-thread state for the new kits.
    // Not a network authentication boundary and not yet connected to legacy skills.
    // The caller validates ownership/target/action first, then commits each effect
    // synchronously after a successful claim. IDs increase across accepted uses;
    // delayed outcomes are allowed while their action remains inside the window.
    public sealed class CombatActionClaims
    {
        private sealed class Claims
        {
            public readonly CombatActionReceipt Receipt;
            public bool Resource;
            public readonly HashSet<string> Growth = new HashSet<string>(StringComparer.Ordinal);
            public Claims(CombatActionReceipt receipt) { Receipt = receipt; }
        }

        private readonly int _actorId, _capacity;
        private readonly SortedDictionary<ulong, Claims> _actions = new SortedDictionary<ulong, Claims>();
        private ulong _retiredThrough;
        public int EncounterEpoch { get; private set; }
        public int TrackedActions => _actions.Count;

        public CombatActionClaims(int actorId, int encounterEpoch, int capacity = 256)
        {
            if (actorId == 0) throw new ArgumentOutOfRangeException(nameof(actorId));
            if (encounterEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(encounterEpoch));
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _actorId = actorId; EncounterEpoch = encounterEpoch; _capacity = capacity;
        }

        // A same/stale epoch cannot clear deduplication or reopen consumed actions.
        public bool BeginEncounter(int nextEpoch)
        {
            if (nextEpoch <= EncounterEpoch) return false;
            EncounterEpoch = nextEpoch; _actions.Clear(); _retiredThrough = 0;
            return true;
        }

        public bool TryClaimCompletionResource(CombatActionReceipt action)
        {
            if (!CombatActionRules.IsComboCompletion(action))return false;
            return TryClaimResource(action);
        }

        // All resource policies share the same action claim. Changing policy or
        // resource ID cannot pay a second reward for an already handled input.
        // Caller validates resource ownership/type before claiming and applies
        // the returned amount synchronously, even when already at the cap.
        public bool TryClaimResource(CombatComboResourcePolicy policy,CombatActionReceipt action,out float amount)
        {
            amount=policy?.Reward(action)??0;
            if(amount<=0||!TryClaimResource(action)){amount=0;return false;}
            return true;
        }
        private bool TryClaimResource(CombatActionReceipt action)
        {
            if (!CombatActionRules.IsDirectComboAction(action) || !TryGet(action, out var claims) || claims.Resource)
                return false;
            claims.Resource = true;
            return true;
        }

        // effectKey is a stable configured route-effect ID, never a target or a
        // fresh GUID per hit. One action may grow distinct explicitly allowed
        // effects, but each effect can grow only once regardless of target count.
        public bool TryClaimDirectGrowth(string effectKey, CombatActionReceipt action)
        {
            if (string.IsNullOrWhiteSpace(effectKey) || !CombatActionRules.IsDirectComboAction(action) ||
                !TryGet(action, out var claims)) return false;
            return claims.Growth.Add(effectKey);
        }

        private bool TryGet(CombatActionReceipt action, out Claims claims)
        {
            claims = null;
            if (action.ActorId != _actorId || action.EncounterEpoch != EncounterEpoch || action.ActionId <= _retiredThrough)
                return false;
            if (_actions.TryGetValue(action.ActionId, out claims))
            {
                // A later hit cannot promote an earlier combo step into a finisher
                // or reuse the same accepted action as a different skill purpose.
                var original = claims.Receipt;
                return original.ComboChain == action.ComboChain && original.ComboStep == action.ComboStep &&
                    original.Origin == action.Origin && original.Purpose == action.Purpose;
            }
            if (_actions.Count == _capacity)
            {
                ulong oldest;
                using (var entries = _actions.GetEnumerator()) { entries.MoveNext(); oldest = entries.Current.Key; }
                if (action.ActionId < oldest) return false;
                _actions.Remove(oldest); _retiredThrough = oldest;
            }
            claims = new Claims(action); _actions.Add(action.ActionId, claims);
            return true;
        }
    }
}
