using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace RaidRogue
{
    [Serializable]
    public sealed class GuardianClashJobVisual
    {
        [Header("Author clips; combat timing never waits for these")]
        public AnimationClip catchClip, holdClip, releaseClip, interruptClip;
        [Header("Paths under the actor; replace with rig sockets in the original editor")]
        public string weaponContactPath, visualRootPath;
        public Vector3 contactLocalOffset, visualOffset, visualEuler, fallbackWeaponEuler;
        [Min(0)] public float maxContactCorrection = 1.5f;
        public bool requireWeaponVisible = true;
        public GameObject catchVfx, releaseVfx;
        public AudioClip catchSound, releaseSound;
    }

    // Visual-only Playables. The host's Guardian state, not clip length or animation events,
    // decides counter consumption, pulses, release, failure and cleanup.
    internal sealed class GuardianClashClipPlayer
    {
        private PlayableGraph _graph;
        private AnimationClipPlayable _clip;
        private Animator _animator;
        private bool _rootMotion;
        public void Play(Animator animator, AnimationClip clip)
        {
            Stop();
            if (animator == null || clip == null) return;
            _animator = animator; _rootMotion = animator.applyRootMotion;
            animator.applyRootMotion = false;
            _graph = PlayableGraph.Create("Guardian clash cosmetic clip");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _clip = AnimationClipPlayable.Create(_graph, clip);
            AnimationPlayableOutput.Create(_graph, "Animator", animator).SetSourcePlayable(_clip);
            _graph.Play();
        }
        public void Loop(AnimationClip clip)
        {
            if (clip != null && clip.length > .05f && _graph.IsValid() && _clip.GetTime() >= clip.length)
                _clip.SetTime(0);
        }
        public void Stop()
        {
            if (_graph.IsValid()) _graph.Destroy();
            if (_animator != null)
            {
                _animator.applyRootMotion = _rootMotion;
                _animator.Rebind();
                _animator.Update(0);
            }
            _animator = null;
        }
    }

    public partial class BossController
    {
        [Header("Hard gargoyle clash presentation; optional authored clips")]
        [SerializeField] private AnimationClip guardianClashBossPress, guardianClashBossHold,
            guardianClashBossRelease, guardianClashBossInterrupt;
        [SerializeField] private string guardianClashBossFistPath = "FistContact";
        [SerializeField] private Vector3 guardianClashFallbackFist = new Vector3(.8f, 1.8f, 3.05f);
        [SerializeField] private GuardianClashJobVisual guardianClashAegis = new GuardianClashJobVisual
        {
            weaponContactPath = "AegisSwordAndShield/GreatShield", visualRootPath = "AegisSwordAndShield",
            contactLocalOffset = new Vector3(0, 0, .08f), fallbackWeaponEuler = new Vector3(-55, 0, 0)
        };
        [SerializeField] private GuardianClashJobVisual guardianClashBerserker = new GuardianClashJobVisual
        {
            weaponContactPath = "berserker_NaturalWeapon/Greatsword", visualRootPath = "berserker_NaturalWeapon",
            contactLocalOffset = new Vector3(0, 0, .06f), fallbackWeaponEuler = new Vector3(0, 0, 90)
        };
        [SerializeField] private GuardianClashJobVisual guardianClashDragon = new GuardianClashJobVisual
        {
            weaponContactPath = "dragon-breaker_NaturalWeapon/DragonfireGauntlet",
            visualRootPath = "dragon-breaker_NaturalWeapon"
            // Neutral placeholder only. The player chooses the authored pose and contact socket.
        };

        private readonly GuardianClashClipPlayer _clashActorClip = new GuardianClashClipPlayer();
        private readonly GuardianClashClipPlayer _clashBossClip = new GuardianClashClipPlayer();
        private int _clashVisualPhase = -1;
        private Transform _clashVisualTank, _clashWeapon, _clashVisualRoot;
        private Vector3 _clashVisualBasePosition;
        private Quaternion _clashVisualBaseRotation, _clashWeaponBaseRotation;
        private GameObject _clashFist, _clashArm;
        private Material _clashStoneMaterial;
        private GuardianClashJobVisual _clashVisualSettings;
        private bool _clashHoldClipStarted;
        private bool _clashVisualPositioned;
        private bool _clashWeaponWasActive;

        private GuardianClashJobVisual ClashJob(Transform actor)
        {
            string id = actor != null ? actor.GetComponent<CharacterActor>()?.Definition?.id : null;
            return id == AegisPaladinData.JobId ? guardianClashAegis :
                id == "job.berserker" ? guardianClashBerserker :
                id == "job.dragon-breaker" ? guardianClashDragon : null;
        }
        private Transform ClashFistAnchor()
        {
            if (string.IsNullOrEmpty(guardianClashBossFistPath)) return null;
            return (_gargoyleInstance != null ? _gargoyleInstance.transform.Find(guardianClashBossFistPath) : null)
                ?? transform.Find(guardianClashBossFistPath);
        }
        private static Animator ClashAnimator(Transform actor) => actor != null ? actor.GetComponentInChildren<Animator>(true) : null;
        private void LateUpdate()
        {
            bool active = GuardianHard && GuardianPatternNow == GuardianPattern.Transformation && _clashPhase >= 2 && _clashPhase <= 5;
            if (!active) { if (_clashVisualPhase >= 0) ClearClashPresentation(); return; }
            if (_clashVisualPhase != _clashPhase || _clashPhase == 3 &&
                (_clashVisualTank != _clashTank || _clashVisualSettings == null && ClashJob(_clashTank) != null ||
                 _clashWeapon == null && _clashVisualTank != null && _clashVisualSettings != null &&
                 !string.IsNullOrEmpty(_clashVisualSettings.weaponContactPath) &&
                 _clashVisualTank.Find(_clashVisualSettings.weaponContactPath) != null))
                EnterClashPresentation();
            if (_clashPhase == 3)
            {
                if (!_clashHoldClipStarted && _clashVisualSettings?.holdClip != null &&
                    (GuardianNow - _guardianStarted >= Mathf.Min(.7f, _clashVisualSettings.catchClip?.length ?? .7f)))
                {
                    _clashHoldClipStarted = true;
                    _clashActorClip.Play(ClashAnimator(_clashVisualTank), _clashVisualSettings.holdClip);
                }
                if (_clashHoldClipStarted) _clashActorClip.Loop(_clashVisualSettings?.holdClip);
                _clashBossClip.Loop(guardianClashBossHold);
                AlignClashContact();
            }
            else if (_clashPhase == 2) PositionFallbackFist(null);
        }
        private void EnterClashPresentation()
        {
            ClearClashPresentation();
            _clashVisualPhase = _clashPhase;
            _clashVisualTank = _clashPhase >= 3 ? _clashTank : null;
            if (_clashPhase == 2)
            {
                _clashBossClip.Play(ClashAnimator(_gargoyleInstance != null ? _gargoyleInstance.transform : transform), guardianClashBossPress);
                PositionFallbackFist(null);
            }
            else if (_clashPhase == 3 && _clashVisualTank != null)
            {
                _clashVisualSettings = ClashJob(_clashVisualTank);
                if (_clashVisualSettings == null) return;
                _clashWeapon = string.IsNullOrEmpty(_clashVisualSettings.weaponContactPath)
                    ? null : _clashVisualTank.Find(_clashVisualSettings.weaponContactPath);
                _clashVisualRoot = !string.IsNullOrEmpty(_clashVisualSettings.visualRootPath)
                    ? _clashVisualTank.Find(_clashVisualSettings.visualRootPath) : null;
                if (_clashVisualRoot == null) _clashVisualRoot = _clashWeapon?.parent;
                if (_clashVisualRoot != null)
                { _clashVisualBasePosition = _clashVisualRoot.localPosition; _clashVisualBaseRotation = _clashVisualRoot.localRotation; }
                if (_clashWeapon != null)
                {
                    _clashWeaponBaseRotation = _clashWeapon.localRotation;
                    _clashWeaponWasActive = _clashWeapon.gameObject.activeSelf;
                    if (_clashVisualSettings.requireWeaponVisible) _clashWeapon.gameObject.SetActive(true);
                }
                _clashActorClip.Play(ClashAnimator(_clashVisualTank), _clashVisualSettings.catchClip);
                _clashBossClip.Play(ClashAnimator(_gargoyleInstance != null ? _gargoyleInstance.transform : transform), guardianClashBossHold);
                PlayClashCue(_clashVisualSettings.catchSound, _clashVisualSettings.catchVfx);
                AlignClashContact();
            }
            else if (_clashPhase == 4 || _clashPhase == 5 && !_clashSucceeded)
            {
                var settings = _clashVisualSettings = ClashJob(_clashTank);
                _clashWeapon = settings != null && _clashTank != null && !string.IsNullOrEmpty(settings.weaponContactPath)
                    ? _clashTank.Find(settings.weaponContactPath) : null;
                if (_clashWeapon != null)
                {
                    _clashWeaponBaseRotation = _clashWeapon.localRotation;
                    _clashWeaponWasActive = _clashWeapon.gameObject.activeSelf;
                    if (settings.requireWeaponVisible) _clashWeapon.gameObject.SetActive(true);
                }
                if (_clashPhase == 4 || CombatRange.LivingPartyMember(_clashTank))
                    _clashActorClip.Play(ClashAnimator(_clashTank), _clashPhase == 4 ? settings?.releaseClip : settings?.interruptClip);
                _clashBossClip.Play(ClashAnimator(_gargoyleInstance != null ? _gargoyleInstance.transform : transform),
                    _clashPhase == 4 ? guardianClashBossRelease : guardianClashBossInterrupt);
                if (_clashPhase == 4 && settings != null) PlayClashCue(settings.releaseSound, settings.releaseVfx);
            }
        }
        private void AlignClashContact()
        {
            if (_clashVisualTank == null || _clashVisualSettings == null) return;
            bool authored = _clashVisualSettings.catchClip != null || _clashVisualSettings.holdClip != null;
            if (_clashVisualRoot != null && (!authored || !_clashVisualPositioned))
            {
                _clashVisualRoot.localPosition = _clashVisualBasePosition + _clashVisualSettings.visualOffset;
                _clashVisualRoot.localRotation = _clashVisualBaseRotation * Quaternion.Euler(_clashVisualSettings.visualEuler);
            }
            if (_clashWeapon != null && !authored)
                _clashWeapon.localRotation = _clashWeaponBaseRotation * Quaternion.Euler(_clashVisualSettings.fallbackWeaponEuler);
            Vector3 contact = _clashWeapon != null
                ? _clashWeapon.TransformPoint(_clashVisualSettings.contactLocalOffset)
                : _clashVisualTank.position + _clashVisualTank.forward * .6f + Vector3.up * 1.5f;
            var anchor = ClashFistAnchor();
            if (anchor != null && _clashVisualRoot != null && (!authored || !_clashVisualPositioned))
            {
                Vector3 difference = Vector3.ClampMagnitude(anchor.position - contact, _clashVisualSettings.maxContactCorrection);
                _clashVisualRoot.localPosition += _clashVisualRoot.parent.InverseTransformVector(difference);
                contact += difference;
            }
            _clashVisualPositioned = true;
            PositionFallbackFist(anchor == null ? contact : (Vector3?)null);
        }
        private void PositionFallbackFist(Vector3? contact)
        {
            if (ClashFistAnchor() != null) return;
            if (_clashFist == null)
            {
                _clashStoneMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                _clashStoneMaterial.color = new Color(.38f,.42f,.45f);
                _clashFist = GameObject.CreatePrimitive(PrimitiveType.Sphere); _clashFist.name = "Temporary gargoyle contact fist";
                var fistCollider = _clashFist.GetComponent<Collider>(); fistCollider.enabled = false; Destroy(fistCollider);
                _clashFist.transform.localScale = Vector3.one * .75f;
                _clashFist.GetComponent<Renderer>().sharedMaterial = _clashStoneMaterial;
                _clashArm = GameObject.CreatePrimitive(PrimitiveType.Cube); _clashArm.name = "Temporary gargoyle pressing arm";
                var armCollider = _clashArm.GetComponent<Collider>(); armCollider.enabled = false; Destroy(armCollider);
                _clashArm.GetComponent<Renderer>().sharedMaterial = _clashStoneMaterial;
            }
            Vector3 fist = contact ?? transform.TransformPoint(guardianClashFallbackFist);
            _clashFist.transform.position = fist;
            Vector3 shoulder = transform.TransformPoint(new Vector3(.9f, 2.8f, .2f));
            Vector3 arm = fist - shoulder;
            _clashArm.transform.position = (shoulder + fist) * .5f;
            _clashArm.transform.rotation = Quaternion.LookRotation(arm);
            _clashArm.transform.localScale = new Vector3(.5f, .5f, arm.magnitude);
        }
        private void PlayClashCue(AudioClip sound, GameObject effect)
        {
            if (sound != null && _guardianAudio != null) _guardianAudio.PlayOneShot(sound);
            if (effect != null && _clashVisualTank != null)
            {
                Vector3 point = _clashWeapon != null ? _clashWeapon.TransformPoint(_clashVisualSettings.contactLocalOffset) : _clashVisualTank.position + Vector3.up * 1.5f;
                Destroy(Instantiate(effect, point, Quaternion.identity), 2);
            }
        }
        private void ClearClashPresentation()
        {
            _clashActorClip.Stop(); _clashBossClip.Stop();
            if (_clashVisualRoot != null) { _clashVisualRoot.localPosition = _clashVisualBasePosition; _clashVisualRoot.localRotation = _clashVisualBaseRotation; }
            if (_clashWeapon != null)
            { _clashWeapon.localRotation = _clashWeaponBaseRotation; _clashWeapon.gameObject.SetActive(_clashWeaponWasActive); }
            if (_clashFist != null) { Destroy(_clashFist); _clashFist = null; }
            if (_clashArm != null) { Destroy(_clashArm); _clashArm = null; }
            if (_clashStoneMaterial != null) { Destroy(_clashStoneMaterial); _clashStoneMaterial = null; }
            _clashVisualPhase = -1; _clashVisualTank = _clashWeapon = _clashVisualRoot = null;
            _clashVisualSettings = null; _clashHoldClipStarted = _clashVisualPositioned = false;
        }
    }
}
