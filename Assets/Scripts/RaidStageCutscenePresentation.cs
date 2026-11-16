using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace RaidRogue
{
    public enum RaidCutsceneBindingTarget { CameraAnimator, BossAnimator, BossTransform, LocalPlayerAnimator, ExplicitComponent, ExplicitGameObject, JanusOmegaAnimator, JanusOmegaTransform }

    [Serializable]
    public sealed class RaidCutsceneTrackBinding
    {
        [Tooltip("Drag a track from the Timeline asset, not a Hierarchy object.")]
        public TrackAsset track;
        public RaidCutsceneBindingTarget target;
        [Tooltip("Only used for ExplicitComponent; should be a component inside this prefab.")]
        public Component explicitComponent;
        [Tooltip("Only used for ExplicitGameObject (for example a Timeline Activation Track).")]
        public GameObject explicitGameObject;
    }

    // Cosmetic only. StageManager and the host retain all combat, reward and skip authority.
    // This prefab is instantiated at the boss anchor so camera clips can use local coordinates.
    public sealed class RaidStageCutscenePresentation : MonoBehaviour
    {
        private struct LocalPose
        {
            public Transform parent;
            public Vector3 position, scale;
            public Quaternion rotation;
        }
        private readonly Dictionary<Transform,LocalPose> _bossPoses = new Dictionary<Transform,LocalPose>();
        private bool _restoring;

        private void CaptureBossPose(Transform target,bool hierarchy)
        {
            if(target==null)return;
            var transforms=hierarchy?target.GetComponentsInChildren<Transform>(true):new[]{target};
            foreach(var node in transforms)
                if(!_bossPoses.ContainsKey(node))_bossPoses.Add(node,new LocalPose
                {parent=node.parent,position=node.localPosition,rotation=node.localRotation,scale=node.localScale});
        }
        private void RestoreBossPoses()
        {
            foreach(var pair in _bossPoses)
            {
                var node=pair.Key;var pose=pair.Value;
                if(node==null||node.parent!=pose.parent)continue;
                node.localPosition=pose.position;node.localRotation=pose.rotation;node.localScale=pose.scale;
            }
            _bossPoses.Clear();
        }
        [SerializeField] private PlayableDirector director;
        [SerializeField] private Camera presentationCamera;
        [SerializeField] private Animator cameraAnimator;
        [SerializeField] private RaidCutsceneTrackBinding[] bindings = Array.Empty<RaidCutsceneTrackBinding>();
        private Camera _playerCamera;
        private bool _playerCameraWasEnabled;
        private bool _playing;
        private BossController _framingBoss;
        private Renderer[] _framingRenderers;
        private Vector3 _fallbackPosition, _fallbackLookAt;
        private bool _usingFallbackCamera;

        // Keep an authored shot until the revealed boss leaves it. Animation and timing still run.
        public void GuardArrivalFraming(BossController boss, Vector3 fallbackPosition, Vector3 fallbackLookAt)
        {
            _framingBoss = boss; _fallbackPosition = fallbackPosition; _fallbackLookAt = fallbackLookAt;
            var visual = boss != null ? boss.dualVisual : null;
            var root = visual != null ? (ViewModeManager.Is2DViewActive ? visual.visual2DRoot : visual.visual3DRoot) : null;
            if (ViewModeManager.Is2DViewActive)
                _framingRenderers = root != null ? root.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            else
                // Dedicated encounter meshes may be siblings of the placeholder visual root.
                // Mesh-only collection excludes particle/line telegraphs and 2D tokens.
                _framingRenderers = boss != null ? boss.GetComponentsInChildren<MeshRenderer>(true) : Array.Empty<MeshRenderer>();
        }
        private void LateUpdate()
        {
            if (!_playing || _framingBoss == null || _playerCamera == null || !_playerCameraWasEnabled ||
                RaidApproachField.Instance?.BossVisible != true || presentationCamera == null) return;
            // Boss animation can move the visible mesh away from the combat root.
            // Only model renderers contribute; telegraphs and world labels are excluded.
            var bounds = new Bounds(_framingBoss.transform.position + Vector3.up * 1.5f, Vector3.one);
            bool found = false;
            foreach (var renderer in _framingRenderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    renderer.GetComponent<TextMesh>() != null || renderer.GetComponentInParent<AoETelegraph>() != null) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            bool inFrame = true;
            for (int corner = 0; corner < 8 && inFrame; corner++)
            {
                var offset = Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1 : 1,
                    (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                var point = presentationCamera.WorldToViewportPoint(bounds.center + offset);
                inFrame = CameraControlMath.SubjectInViewport(point.x, point.y, point.z);
            }
            if (!_usingFallbackCamera && inFrame) return;
            var view = QuarterViewCamera.LocalCamera;
            if (view == null) return;
            var cameraOffset = _fallbackPosition - _fallbackLookAt;
            if (cameraOffset.sqrMagnitude < .01f) cameraOffset = new Vector3(0, 8, -15);
            float halfFov = Mathf.Atan(Mathf.Tan(view.UnityCamera.fieldOfView * Mathf.Deg2Rad * .5f) *
                Mathf.Min(1, Mathf.Max(.1f, view.UnityCamera.aspect)));
            float cameraDistance = Mathf.Max(cameraOffset.magnitude, bounds.extents.magnitude / Mathf.Sin(halfFov) * 1.15f);
            presentationCamera.enabled = false;
            view.SetArrivalCamera(bounds.center + cameraOffset.normalized * cameraDistance, bounds.center, true);
            _playerCamera.enabled = true;
            _usingFallbackCamera = true;
        }

        public TimelineAsset DirectorAsset => director != null ? director.playableAsset as TimelineAsset : null;
        public bool HasBossAnimationTrack
        {
            get
            {
                foreach (var binding in bindings)
                    if (binding != null && (binding.target == RaidCutsceneBindingTarget.BossAnimator || binding.target == RaidCutsceneBindingTarget.JanusOmegaAnimator) && binding.track != null)
                        return true;
                return false;
            }
        }
        public static Animator EnsureBossAnimator(BossController boss, RuntimeAnimatorController controller = null)
        {
            if (boss == null) return null;
            var animator = boss.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                var visual = boss.transform.Find("[Visual_3D]");
                animator = (visual != null ? visual.gameObject : boss.gameObject).AddComponent<Animator>();
            }
            if (controller != null && animator.runtimeAnimatorController != controller)
                animator.runtimeAnimatorController = controller;
            return animator;
        }
        public bool HasRequiredReferences => director != null && presentationCamera != null && DirectorAsset != null &&
            !double.IsNaN(DirectorAsset.duration) && !double.IsInfinity(DirectorAsset.duration) &&
            DirectorAsset.duration >= .5 && DirectorAsset.duration <= 120;
        public string ConfigurationError
        {
            get
            {
                if (!HasRequiredReferences) return "PlayableDirector, Camera, or finite Timeline (0.5–120s) is missing";
                foreach (var binding in bindings)
                {
                    if (binding == null || binding.track == null) return "an explicit track binding is empty";
                    bool onTimeline = false;
                    foreach (var track in DirectorAsset.GetOutputTracks())
                        if (track == binding.track) { onTimeline = true; break; }
                    if (!onTimeline) return "a binding points to a different Timeline";
                    if (binding.target == RaidCutsceneBindingTarget.CameraAnimator && cameraAnimator == null)
                        return "Camera Animator binding has no camera Animator";
                    if (binding.target == RaidCutsceneBindingTarget.ExplicitComponent && binding.explicitComponent == null)
                        return "an ExplicitComponent binding is empty";
                    if (binding.target == RaidCutsceneBindingTarget.ExplicitGameObject && binding.explicitGameObject == null)
                        return "an ExplicitGameObject binding is empty";
                }
                return null;
            }
        }
        public static float Duration(TimelineAsset asset, float fallback)
        {
            double seconds = asset != null ? asset.duration : 0;
            return !double.IsNaN(seconds) && !double.IsInfinity(seconds) && seconds >= .5 && seconds <= 120
                ? (float)seconds : Mathf.Max(.5f, fallback);
        }

        public static float VisualAt(TimelineAsset asset, RaidCutsceneVisualEvent visualEvent, float fallback, float duration)
        {
            if (asset != null && asset.markerTrack != null)
                foreach (var marker in asset.markerTrack.GetMarkers())
                    if (marker is RaidCutsceneVisualMarker visual && visual.visualEvent == visualEvent)
                        return Mathf.Clamp((float)visual.time, 0, duration);
            return Mathf.Clamp(fallback, 0, duration);
        }

        public bool Play(BossController boss, PlayerController localPlayer, double elapsedSeconds)
        {
            if (_playing || ConfigurationError != null) return false;
            var asset = DirectorAsset;
            if (asset == null) return false;
            try
            {
                director.playableAsset = asset;
                director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
                foreach (var binding in bindings)
                {
                    UnityEngine.Object target = null;
                    switch (binding.target)
                    {
                        case RaidCutsceneBindingTarget.CameraAnimator: target = cameraAnimator; break;
                        case RaidCutsceneBindingTarget.BossAnimator: target = EnsureBossAnimator(boss); break;
                        case RaidCutsceneBindingTarget.BossTransform: target = boss != null ? boss.transform : null; break;
                        case RaidCutsceneBindingTarget.JanusOmegaAnimator:
                            target = boss != null && boss.JanusOmegaTransform != null ? boss.JanusOmegaTransform.GetComponentInChildren<Animator>(true) : null;
                            break;
                        case RaidCutsceneBindingTarget.JanusOmegaTransform: target = boss != null ? boss.JanusOmegaTransform : null; break;
                        case RaidCutsceneBindingTarget.LocalPlayerAnimator: target = localPlayer != null ? localPlayer.GetComponentInChildren<Animator>() : null; break;
                        case RaidCutsceneBindingTarget.ExplicitComponent: target = binding.explicitComponent; break;
                        case RaidCutsceneBindingTarget.ExplicitGameObject: target = binding.explicitGameObject; break;
                    }
                    if (target == null)
                    {
                        Debug.LogWarning($"Cutscene track '{binding.track.name}' has no {binding.target} target; using fallback.");
                        RestoreBossPoses();
                        return false;
                    }
                    if(binding.target==RaidCutsceneBindingTarget.BossAnimator || binding.target==RaidCutsceneBindingTarget.JanusOmegaAnimator)
                        CaptureBossPose(((Animator)target).transform,true);
                    else if(binding.target==RaidCutsceneBindingTarget.BossTransform || binding.target==RaidCutsceneBindingTarget.JanusOmegaTransform)
                        CaptureBossPose((Transform)target,false);
                    director.SetGenericBinding(binding.track, target);
                }
                var playerView = QuarterViewCamera.LocalCamera;
                _playerCamera = playerView != null ? playerView.GetComponent<Camera>() : null;
                _playerCameraWasEnabled = _playerCamera != null && _playerCamera.enabled;
                playerView?.ClearArrivalCamera();
                if (_playerCamera != null) _playerCamera.enabled = false;
                presentationCamera.enabled = true;
                director.time = Math.Max(0, Math.Min(asset.duration, elapsedSeconds));
                director.Play();
                director.time = Math.Max(0, Math.Min(asset.duration, elapsedSeconds));
                director.Evaluate();
                _playing = true;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Cutscene presentation failed; restoring gameplay camera: {exception}");
                StopAndRestore();
                return false;
            }
        }

        public void StopAndRestore()
        {
            if(_restoring)return;
            _restoring=true;
            _playing = false;
            try
            {
                if (director != null) director.Stop();
            }
            finally
            {
                // Revert only this presentation's captured pose, never authored assets or
                // hard-coded zeroes. The combat root and visual hierarchy stay aligned.
                RestoreBossPoses();
                if (_usingFallbackCamera) QuarterViewCamera.LocalCamera?.ClearArrivalCamera();
                _usingFallbackCamera = false; _framingBoss = null; _framingRenderers = null;
                if (presentationCamera != null) presentationCamera.enabled = false;
                if (_playerCamera != null) _playerCamera.enabled = _playerCameraWasEnabled;
                _restoring=false;
            }
        }

        private void OnDisable() { if (_playing) StopAndRestore(); }
        private void OnDestroy() { if (_playing) StopAndRestore(); }
    }
}
