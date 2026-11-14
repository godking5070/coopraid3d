using System;
using System.Collections.Generic;

// The runner inserts the actual source method. Only its surrounding components are
// stubbed: these checks verify when the observer submits an action, not whether the
// host accepts it. Reward/save/ownership validation belongs to ExecuteAction.
namespace RaidRogue
{
    internal enum RaidBattlePhase { Lobby, Combat, Reward, Wiped, Victory }
    internal sealed class Transform { public bool Near; }
    internal static class Mathf { public static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value)); }
    internal static class Debug { public static void Log(string message) { } }
    internal sealed class PlayerController
    {
        public bool IsDead;
        public readonly RoguelikeCardManager cardManager = new RoguelikeCardManager();
    }
    internal enum RoguelikeCardId { First = 11 }
    internal sealed class RoguelikeCardManager
    {
        public int Applied;
        public void ApplyNetworkCard(RoguelikeCardId card) { Applied++; }
    }
    internal sealed class NetworkRaidAvatar
    {
        public readonly Transform transform = new Transform();
        public readonly PlayerController Player = new PlayerController();
        public T GetComponent<T>() where T : class => Player as T;
    }
    internal sealed class RaidApproachField
    {
        public static RaidApproachField Instance;
        public bool PortalActive;
        public bool NearPortal(Transform member) => PortalActive && member != null && member.Near;
    }
    internal sealed class StageManager
    {
        public static StageManager Instance = new StageManager();
        public int currentStage = 2;
    }
    internal sealed class CampaignRuntime
    {
        public static CampaignRuntime Instance = new CampaignRuntime();
        public int CardsRecorded;
        public void CardChosen(PlayerController player, int card, int[] offered) { CardsRecorded++; }
    }
    internal sealed class RaidRoster
    {
        internal sealed class Member { public int Id, Role; }
        public readonly List<Member> Members = new List<Member>();
    }
    internal sealed class RewardOffers
    {
        public bool OfferCards = false;
        public readonly HashSet<int> Chosen = new HashSet<int>();
        public int[] Cards(int actor) => OfferCards ? new[] { 11, 12 } : Array.Empty<int>();
        public bool Choose(int epoch, int actor, int card) => Chosen.Add(actor);
    }
    internal sealed class RaidNetworkSession
    {
        public bool AiSpectatorActive = true, Ending = false, CanStartLobby = false, AiSpectatorAutoRetry = false, AiSpectatorStopOnClear;
        public int AiSpectatorRetries = 0, AiSpectatorMaxRetries = 2, BattleEpoch = 3;
        public RaidBattlePhase BattlePhase = RaidBattlePhase.Reward;
        internal readonly Dictionary<int, NetworkRaidAvatar> _avatars = new Dictionary<int, NetworkRaidAvatar>();
        internal readonly int _localId = 1;
        internal readonly RaidRoster _roster = new RaidRoster();
        internal readonly RewardOffers _rewards = new RewardOffers();
        public int Actions, LastSkill, SkillChoices, FinishedRewards, Departures;
        public NetworkRaidAvatar LastActor;
        public Transform LastTarget;
        void BeginNetworkEncounter(int stage, bool retry) { Departures++; }
        void AutomaticAction(NetworkRaidAvatar actor, int skill, Transform target)
        { Actions++; LastSkill = skill; LastActor = actor; LastTarget = target; }
        void AutoChooseSkillRewards(int actor) { SkillChoices++; }
        void FinishRewardsIfReady() { FinishedRewards++; }

        /* ACTUAL_AI_SPECTATOR_TICK */
    }

    internal static class AiSpectatorProgressTests
    {
        static int _checks;
        static void Check(bool condition, string message)
        { _checks++; if (!condition) throw new Exception(message); }
        static RaidNetworkSession Session(bool dead = false, bool near = false, bool portal = true, bool stop = false)
        {
            var session = new RaidNetworkSession { AiSpectatorStopOnClear = stop };
            for (int id = 1; id <= 4; id++)
            {
                session._avatars.Add(id, new NetworkRaidAvatar());
                session._roster.Members.Add(new RaidRoster.Member { Id = id, Role = id });
            }
            session._avatars[1].Player.IsDead = dead;
            session._avatars[1].transform.Near = near;
            RaidApproachField.Instance = new RaidApproachField { PortalActive = portal };
            CampaignRuntime.Instance = new CampaignRuntime();
            return session;
        }
        static void Main()
        {
            try { Run(); }
            catch (Exception error)
            {
                Console.Error.WriteLine("FAIL: " + error.Message);
                Environment.Exit(1);
            }
        }
        static void Run()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            foreach (bool dead in new[] { false, true })
                foreach (bool near in new[] { false, true })
                    foreach (bool portal in new[] { false, true })
                        foreach (bool stop in new[] { false, true })
                        {
                            var session = Session(dead, near, portal, stop);
                            session.AiSpectatorTick();
                            bool expected = portal && !stop && (dead || near);
                            Check(session.Actions == (expected ? 1 : 0),
                                $"Reward action boundary: dead={dead}, near={near}, portal={portal}, stop={stop}");
                            if (expected)
                                Check(session.LastSkill == 15 && session.LastActor == session._avatars[1] &&
                                    session.LastTarget == session._avatars[1].transform,
                                    "Return uses the existing leader-owned automatic action 15");
                            Check(session.SkillChoices == 4 && session.FinishedRewards == 1,
                                "All skill rewards are handled before the return request");
                        }
            var absent = Session(true);
            RaidApproachField.Instance = null;
            absent.AiSpectatorTick();
            Check(absent.Actions == 0, "Missing field cannot return a dead leader");

            var cards = Session(true);
            cards._rewards.OfferCards = true;
            cards.AiSpectatorTick();
            Check(cards.Actions == 1 && cards._rewards.Chosen.Count == 4 && CampaignRuntime.Instance.CardsRecorded == 4,
                "A dead leader cannot prevent the four card selections and return request");
            foreach (var avatar in cards._avatars.Values)
                Check(avatar.Player.cardManager.Applied == 1, "Each offered card is applied once");

            foreach (int gate in new[] { 0, 1, 2 })
            {
                var session = Session(true);
                if (gate == 0) session.AiSpectatorActive = false;
                if (gate == 1) session.Ending = true;
                if (gate == 2) session._avatars.Remove(4);
                session.AiSpectatorTick();
                Check(session.Actions == 0 && session.SkillChoices == 0 && session.FinishedRewards == 0,
                    "Inactive/ending/incomplete session has no observer actions");
            }
            foreach (RaidBattlePhase phase in new[] { RaidBattlePhase.Combat, RaidBattlePhase.Wiped, RaidBattlePhase.Victory })
            {
                var session = Session(true, true);
                session.BattlePhase = phase;
                session.AiSpectatorTick();
                Check(session.Actions == 0, "Portal return is only submitted during Reward: " + phase);
            }
            var lobby = Session(); lobby.BattlePhase = RaidBattlePhase.Lobby; lobby.CanStartLobby = true;
            lobby.AiSpectatorTick();
            Check(lobby.Departures == 1 && lobby.Actions == 0, "Lobby departure path remains intact");
            var retry = Session(); retry.BattlePhase = RaidBattlePhase.Wiped; retry.AiSpectatorAutoRetry = true;
            retry.AiSpectatorTick();
            Check(retry.Actions == 1 && retry.LastSkill == 9 && retry.AiSpectatorRetries == 1, "Automatic wipe retry remains intact");
            retry.AiSpectatorRetries = retry.AiSpectatorMaxRetries;
            retry.AiSpectatorTick();
            Check(retry.Actions == 1, "Automatic retry limit remains enforced");
#else
            var release = Session(true, true);
            release.AiSpectatorTick();
            Check(release.Actions == 0 && release.SkillChoices == 0 && release.FinishedRewards == 0,
                "Non-development player has no AI spectator tick work");
#endif
            Console.WriteLine("PASS: " + _checks + " actual AI spectator tick checks. Host acceptance, save failure and unfinished reward rejection remain outside this fixture.");
        }
    }
}
