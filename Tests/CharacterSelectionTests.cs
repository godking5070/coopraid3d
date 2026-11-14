using System;
using System.Collections.Generic;
using System.Linq;
using RaidRogue;

namespace RaidRogue
{
    // Production validation and picker methods are inserted by the runner. Only scene/UI services are stubbed.
    public sealed class CharacterActor
    {
        private readonly Dictionary<string,Func<CharacterSkillDefinition,object,bool>> _handlers=new Dictionary<string,Func<CharacterSkillDefinition,object,bool>>();
        public string rule,choice;
        public void Register(string id)=>_handlers[id]=(skill,target)=>true;
        public void Remove(string id)=>_handlers.Remove(id);
        public bool CanAssign(CharacterDefinition definition)=>HasRequiredSkillHandlers(definition);
        public void RequestChoice(string selectedRule,string selectedChoice){rule=selectedRule;choice=selectedChoice;}
        /* ACTUAL_HANDLER_VALIDATION */
    }
    public sealed class RoleSelectionManager
    {
        public static readonly RoleSelectionManager Instance=new RoleSelectionManager();
        public bool accepted;
        public string requested;
        public bool SelectRosterJob(string id){requested=id;return accepted;}
    }
    public sealed class RaidNetworkSession
    {
        public static readonly RaidNetworkSession Instance=new RaidNetworkSession();
        public string requested;
        public void RequestCharacterAction(int kind,string id,int slot){requested=id;}
    }
    public sealed class SelectionUiFixture
    {
        private bool _jobs=true,_network;
        private string _pendingJob;
        private readonly string[] _routes=new[]{AegisPaladinData.JobId,"another.job","third.job"};
        private readonly CharacterActor _actor=new CharacterActor();
        public int refreshes;
        public string Pending=>_pendingJob;
        public bool Jobs=>_jobs;
        public string RouteChoice=>_actor.choice;
        public void Click(int index)=>Select(index);
        public void Network()=>_network=true;
        private void RefreshChoices(){refreshes++;_routes[0]=AegisRouteData.Bulwark;}
        /* ACTUAL_PICKER_SELECT */
    }
}

internal static class CharacterSelectionTests
{
    private static int checks;
    private static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    public static int Main()
    {
        try
        {
            var definition=AegisPaladinData.BuildNineKey();
            var actor=new CharacterActor();
            // The executable handlers registered by AegisPaladin/AegisExtensions; no passive execution adapter exists.
            foreach(var slot in CharacterSlots.All)
                if(slot<CharacterSlot.Identity&&slot!=CharacterSlot.Movement)actor.Register("aegis."+slot);
            actor.Register("aegis.identity");actor.Register("aegis.extension");
            Check(definition.skills.Count(s=>s.handlerId=="aegis.passive")==6,"fixture includes all six real passive core choices");
            Check(actor.CanAssign(definition),"current playable paladin accepts core traits without executable handlers");
            foreach(var handler in new[]{"aegis.Gcd1","aegis.Gcd2","aegis.Counter","aegis.identity","aegis.extension"})
            {
                actor.Remove(handler);Check(!actor.CanAssign(definition),"missing active handler still rejects: "+handler);actor.Register(handler);
            }
            var core=definition.skills.First(s=>s.handlerId=="aegis.passive");
            var oldSlots=definition.slots;
            definition.slots=oldSlots.Concat(new[]{new CharacterSlotDefinition{slot=CharacterSlot.OffGcd1,skillId=core.id}}).ToArray();
            Check(!actor.CanAssign(definition),"passive also exposed as an action cannot bypass handler validation");definition.slots=oldSlots;
            var oldLoadouts=definition.loadouts;
            definition.loadouts=oldLoadouts.Concat(new[]{new CharacterLoadoutDefinition{id="test.action",slot=CharacterSlot.OffGcd1,choices=new[]{core.id}}}).ToArray();
            Check(!actor.CanAssign(definition),"action loadout cannot bypass handler validation");definition.loadouts=oldLoadouts;
            var oldRoutes=definition.routes;
            definition.routes=oldRoutes.Concat(new[]{new CharacterRouteDefinition{id="test.route",identitySkillId=core.id}}).ToArray();
            Check(!actor.CanAssign(definition),"route action cannot bypass handler validation");definition.routes=oldRoutes;
            var oldUnlocks=definition.unlocks;
            definition.unlocks=oldUnlocks.Concat(new[]{new CharacterUnlockDefinition{id="test.unlock",slot=CharacterSlot.OffGcd1,choices=new[]{core.id}}}).ToArray();
            Check(!actor.CanAssign(definition),"reward action cannot bypass handler validation");definition.unlocks=oldUnlocks;
            definition.loadouts=oldLoadouts.Where(l=>!l.choices.Contains(core.id)).ToArray();
            Check(!actor.CanAssign(definition),"unclassified unhandled skill is not silently accepted");definition.loadouts=oldLoadouts;

            var picker=new SelectionUiFixture();RoleSelectionManager.Instance.accepted=false;picker.Click(0);
            Check(RoleSelectionManager.Instance.requested==AegisPaladinData.JobId,"real picker requests selected paladin");
            Check(picker.Pending==null&&picker.Jobs&&picker.refreshes==0,"failed local assignment releases pending state and keeps job choices available");
            RoleSelectionManager.Instance.accepted=true;picker.Click(0);
            Check(!picker.Jobs&&picker.Pending==AegisPaladinData.JobId&&picker.refreshes==1,"selection can succeed after a rejected click");
            picker.Click(0);Check(picker.RouteChoice==AegisRouteData.Bulwark,"successful assignment proceeds to route selection");
            var network=new SelectionUiFixture();network.Network();network.Click(0);
            Check(network.Pending==AegisPaladinData.JobId&&!network.Jobs&&RaidNetworkSession.Instance.requested==AegisPaladinData.JobId,"network selection still waits for host approval");
            Console.WriteLine("PASS: "+checks+" character selection regression checks using real paladin definitions and production validation/picker methods.");
            Console.WriteLine("Boundary: Unity lifecycle, rendering and live host approval are not executed.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
