using UnityEngine;

namespace RaidRogue
{
    public partial class StageManager
    {
        private RaidStageCutscenePresentation _activeCutscene;
        private RaidBattlePhase _activeCutscenePhase;
        private int _activeCutsceneEpoch = -1;

        internal bool BeginStagePresentation(RaidBattlePhase phase, int epoch, double elapsedSeconds = 0)
        {
            if (_activeCutscene != null && _activeCutscenePhase == phase && _activeCutsceneEpoch == epoch) return true;
            EndStagePresentation();
            var field = RaidApproachField.Instance;
            if (field == null) return false;
            var prefab = phase == RaidBattlePhase.Arrival ? field.Arrival(currentStage).presentationPrefab :
                phase == RaidBattlePhase.BossClear ? field.Defeat(currentStage).presentationPrefab : null;
            if (prefab == null || boss == null) return false;
            RaidStageCutscenePresentation instance = null;
            try
            {
                instance = Instantiate(prefab, boss.transform.position, Quaternion.identity, transform);
                if (instance == null || !instance.Play(boss, player, elapsedSeconds))
                {
                    if (instance != null) Destroy(instance.gameObject);
                    return false;
                }
                if (phase == RaidBattlePhase.Arrival)
                {
                    var arrival = field.Arrival(currentStage);
                    instance.GuardArrivalFraming(boss, arrival.cameraPosition, arrival.cameraLookAt);
                }
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("Cutscene instance failed; using the existing camera fallback: " + exception);
                if (instance != null) { instance.StopAndRestore(); Destroy(instance.gameObject); }
                return false;
            }
            _activeCutscene = instance;
            _activeCutscenePhase = phase;
            _activeCutsceneEpoch = epoch;
            return true;
        }

        internal void EndStagePresentation()
        {
            if (_activeCutscene != null)
            {
                _activeCutscene.StopAndRestore();
                Destroy(_activeCutscene.gameObject);
                _activeCutscene = null;
            }
            _activeCutsceneEpoch = -1;
        }

        internal bool HasStagePresentation(RaidBattlePhase phase) => _activeCutscene != null && _activeCutscenePhase == phase;
    }
}
