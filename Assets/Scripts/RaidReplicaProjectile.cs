using UnityEngine;

namespace RaidRogue
{
    public partial class RangedBotProjectile
    {
        public RaidCombatMechanic CaptureNetworkMechanic()
        {
            float now = RaidNetworkSession.Instance.ServerTime;
            return new RaidCombatMechanic { id = GetInstanceID(), kind = 7, target = RaidCombatAuthority.Id(_targetBoss != null ? _targetBoss.transform : null),
                x = transform.position.x, y = transform.position.y, z = transform.position.z, speed = _speed, start = now, resolve = now + _lifetime, end = now + _lifetime };
        }
    }
    // Presentation only: no source actor, damage amount, hit callback or combat event is replayed.
    public sealed class RaidReplicaProjectile : MonoBehaviour
    {
        private RaidCombatMechanic _state;
        private Transform _target;
        private Material _material;
        public static RaidReplicaProjectile Create()
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere); obj.name = "NetworkProjectile"; obj.transform.localScale = Vector3.one * .38f;
            var collider = obj.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
            var view = obj.AddComponent<RaidReplicaProjectile>();
            view._material = new Material(Shader.Find("Sprites/Default")) { color = new Color(1, .55f, .12f, 1) };
            obj.GetComponent<Renderer>().sharedMaterial = view._material; return view;
        }
        public void Apply(RaidCombatMechanic state, Transform target, float serverTime)
        { _state = state; _target = target; Render(serverTime); }
        private void Update() { if (_state != null && RaidNetworkSession.Instance != null) Render(RaidNetworkSession.Instance.ServerTime); }
        private void Render(float time)
        {
            var renderer = GetComponent<Renderer>(); renderer.enabled = time < _state.end && _target != null;
            if (!renderer.enabled) return;
            Vector3 start = new Vector3(_state.x, _state.y, _state.z), end = new Vector3(_target.position.x, 1.35f, _target.position.z);
            transform.position = Vector3.MoveTowards(start, end, Mathf.Max(0, time - _state.start) * _state.speed);
        }
        private void OnDestroy() { if (_material != null) Destroy(_material); }
    }
}
