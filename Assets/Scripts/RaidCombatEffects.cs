using System.Text;
using UnityEngine;

namespace RaidRogue
{
    public sealed partial class RaidNetworkSession
    {
        public void BroadcastEffect(Vector3 position, Vector3 target, Color color, string text, bool large, int kind, bool important = false)
        {
            if (!IsHost) return;
            if (text != null && text.Length > 120) text = text.Substring(0, 120);
            var effect = new RaidCombatEffect { id = ++_effectId, epoch = BattleEpoch, x = position.x, y = position.y, z = position.z,
                tx = target.x, ty = target.y, tz = target.z, r = color.r, g = color.g, b = color.b, text = text, large = large, kind = kind, important = important };
            var packet = new RaidPacket { Kind = RaidPacketKind.CombatEffect, Bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(effect)) };
            foreach (var peer in _peers) if (peer.Id != 0 && peer.CloseAt == 0) Send(peer.Connection, packet, true);
        }
        private static void PresentNetworkEffect(RaidCombatEffect effect)
        {
            Vector3 from = new Vector3(effect.x, effect.y, effect.z); Color color = new Color(effect.r, effect.g, effect.b);
            if (effect.kind == 0) FloatingTextPopup.Spawn(from, effect.text, color, effect.large, effect.important);
            else
            {
                var obj = new GameObject("ReplicatedCombatEffect"); var line = obj.AddComponent<LineRenderer>();
                line.positionCount = 2; line.SetPosition(0, from); line.SetPosition(1, new Vector3(effect.tx, effect.ty, effect.tz));
                line.startWidth = .16f; line.endWidth = .03f; line.startColor = line.endColor = color;
                line.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
                obj.AddComponent<RaidTransientVisual>();
            }
        }
    }
    // Presentation only. It has no health/damage callback even if a packet is replayed.
    public sealed class RaidTransientVisual : MonoBehaviour
    {
        private float _end;
        private void Awake() { _end = Time.unscaledTime + .22f; }
        private void Update() { if (Time.unscaledTime >= _end) Destroy(gameObject); }
        private void OnDestroy() { var line = GetComponent<LineRenderer>(); if (line != null) Destroy(line.sharedMaterial); }
    }
}
