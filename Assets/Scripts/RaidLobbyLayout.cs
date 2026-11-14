using System;
using UnityEngine;
using UnityEngine.Timeline;

namespace RaidRogue
{
    [Serializable]
    public sealed class RaidArrivalStageLayout
    {
        [Range(1, 6)] public int stage = 1;
        public Vector3 bossPosition = Vector3.zero;
        public float bossYaw = 180f;
        public Vector3 partyCenter = new Vector3(0, 1, -8);
        [Min(3)] public float bossDistance = 8f;
        [Min(1)] public float partySpacing = 2.2f;
        public Vector3 cameraPosition = new Vector3(0, 8, -15);
        public Vector3 cameraLookAt = new Vector3(0, 2, 0);
        [Min(.5f)] public float cutsceneSeconds = 3f;
        [Min(0)] public float revealSeconds = 1f;
        [Min(0)] public float afterCutsceneSeconds = .5f;
        [Tooltip("Optional boss Animator Controller. Put the arrival and defeat trigger states in this controller; leave empty to use the boss's existing Animator.")]
        public RuntimeAnimatorController bossAnimatorController;
        [Tooltip("Animator Trigger played when the boss becomes visible during the arrival cutscene.")]
        public string arrivalAnimationTrigger = "";
        [Header("Optional artist-authored presentation. Prefab owns camera and track bindings.")]
        public RaidStageCutscenePresentation presentationPrefab;
        public float Duration => RaidStageCutscenePresentation.Duration(presentationPrefab != null ? presentationPrefab.DirectorAsset : null, cutsceneSeconds);
        public float RevealAt => RaidStageCutscenePresentation.VisualAt(presentationPrefab != null ? presentationPrefab.DirectorAsset : null,
            RaidCutsceneVisualEvent.RevealBoss, revealSeconds, Duration);
    }

    [Serializable]
    public sealed class RaidDefeatStageLayout
    {
        [Range(1, 6)] public int stage = 1;
        [Min(.5f)] public float cutsceneSeconds = 2.5f;
        [Min(0)] public float departureSeconds = 1.8f;
        public Vector3 cameraPosition = new Vector3(0, 7, -12);
        public Vector3 cameraLookAt = new Vector3(0, 1.5f, 0);
        [Tooltip("Animator Trigger played after the clear is confirmed. If absent, the existing collapse effect is used.")]
        public string deathAnimationTrigger = "";
        [Header("Optional artist-authored presentation. Prefab owns camera and track bindings.")]
        public RaidStageCutscenePresentation presentationPrefab;
        public float Duration => RaidStageCutscenePresentation.Duration(presentationPrefab != null ? presentationPrefab.DirectorAsset : null, cutsceneSeconds);
        public float DepartureAt => RaidStageCutscenePresentation.VisualAt(presentationPrefab != null ? presentationPrefab.DirectorAsset : null,
            RaidCutsceneVisualEvent.HideBoss, departureSeconds, Duration);
    }

    // Optional Resources/UI/RaidLobbyLayout asset. Room geometry remains a code fallback;
    // designers can replace the three landmarks without changing gameplay code.
    [CreateAssetMenu(menuName = "RaidRogue/Lobby Layout", fileName = "RaidLobbyLayout")]
    public sealed class RaidLobbyLayout : ScriptableObject
    {
        [Header("North is world +Z; keep the door inside the north wall opening")]
        public Vector3 doorPosition = new Vector3(0, 1, 14.5f);
        public Vector3 guidePosition = new Vector3(-7, 1, 1);
        public Vector3 irisPosition = new Vector3(7, 1, 1);
        public Vector3 partyReturnPosition = new Vector3(0, 1, -7);
        [Min(1)] public float doorRange = 3.4f;
        [Min(1)] public float npcRange = 3f;
        [Header("Raid approach; field remains centered on the existing boss at world origin")]
        [Min(8)] public float approachLength = 25f;
        [Min(4)] public float approachWidth = 8f;
        [Min(1)] public float approachSpawnOffset = 2f;
        [Min(12)] public float fieldRadius = 18f;
        [Min(2)] public float rallyDistanceIntoField = 4f;
        [Min(2)] public float rallyRadius = 4f;
        [Min(1)] public float retryDistanceFromField = 3f;
        [Header("Boss arrival: stage overrides are optional; world +Z is north")]
        public RaidArrivalStageLayout[] bossArrivals;
        [Header("Boss defeat: stage overrides are optional; no new boss content")]
        public RaidDefeatStageLayout[] bossDefeats;
        [Header("NPC display and optional model prefabs")]
        public string guideName = "길잡이";
        public string irisName = "이리스";
        public GameObject guidePrefab;
        public GameObject irisPrefab;
        public GameObject doorPrefab;
    }
}
