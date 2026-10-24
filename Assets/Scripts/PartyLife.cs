using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidRogue
{
    [DefaultExecutionOrder(-110)]
    public sealed partial class PartyLife : MonoBehaviour
    {
        private readonly PartyLifeState _state = new PartyLifeState();
        private PlayerController _player;
        private PartyBotAI _bot;
        private TextMesh _status;
        private Transform _body;
        private Quaternion _upright;
        public bool IsDead => _state.Dead;
        public float Hp => _player != null ? _player.currentHp : _bot != null ? _bot.currentHp : 0;
        public bool IsTank => _player != null ? _player.currentRole == PlayerRole.Guardian_Tank : _bot != null && _bot.role == PartyBotRole.Tank;
        public bool IsHealer => _player != null ? _player.currentRole == PlayerRole.Oracle_Healer : _bot != null && _bot.role == PartyBotRole.Healer;
        public static PartyLife Ensure(GameObject member) => member.GetComponent<PartyLife>() ?? member.AddComponent<PartyLife>();

        private void Awake()
        {
            _player = GetComponent<PlayerController>(); _bot = GetComponent<PartyBotAI>();
            CommonPotion.Ensure(gameObject);
            if (GetComponent<BattleResurrection>() == null) gameObject.AddComponent<BattleResurrection>();
        }

        private void Update()
        {
            if (!RaidCombatAuthority.Replica && Hp <= 0 && !IsDead) Die();
        }

        public void Die()
        {
            if (RaidCombatAuthority.Replica || !_state.Die()) return;
            GetComponent<CharacterActor>()?.OnDeath();
            var visual = GetComponent<DualVisualController>();
            _body = visual != null && visual.visual3DRoot != null ? visual.visual3DRoot.transform : null;
            if (_body != null) { _upright = _body.localRotation; _body.localRotation = Quaternion.Euler(0, 0, 75) * _upright; }
            if (_player != null) _player.OnPartyDeath();
            if (_bot != null) _bot.OnPartyDeath();
            GetComponent<BattleResurrection>()?.Cancel();
            CommonPotion.Ensure(gameObject).EndRevivalProtection();
            CombatMetricsManager.SetDead(transform, true);
            FindFirstObjectByType<BossController>()?.OnPartyMemberDied(transform);
            FloatingTextPopup.Spawn(transform.position + Vector3.up * 2f, "전투 불능 · 이리스의 코인으로 구조 가능", Color.gray, true);
        }

        public bool TryRevive(float hpFraction)
        {
            if (RaidCombatAuthority.Replica || StageManager.CombatSuspended || hpFraction <= 0 || !_state.Revive()) return false;
            if (_body != null) _body.localRotation = _upright;
            if (_player != null) _player.RestorePartyLife(hpFraction);
            if (_bot != null) _bot.RestorePartyLife(hpFraction);
            CombatMetricsManager.SetDead(transform, false);
            CommonPotion.Ensure(gameObject).GrantRevivalProtection();
            FloatingTextPopup.Spawn(transform.position + Vector3.up * 2f, "부활 · 3초 보호", new Color(0.5f, 1f, 1f), true);
            return true;
        }

        // Used only for encounter setup; ordinary revival never resets item/skill cooldowns.
        public void ResetForEncounter()
        {
            _state.Reset();
            if (_body != null) _body.localRotation = _upright;
            if (_player != null) _player.RestorePartyLife(1f, true);
            if (_bot != null) _bot.RestorePartyLife(1f, true);
            GetComponent<BattleResurrection>()?.ResetForRetry();
            var potion = CommonPotion.Ensure(gameObject);
            potion.ResetForBossRetry(); potion.ItemsBlocked = false;
            CombatMetricsManager.SetDead(transform, false);
        }

        public void ClearDeadForStagePlacement()
        {
            _state.Reset(); if (_body != null) _body.localRotation = _upright;
            CombatMetricsManager.SetDead(transform, false);
        }

        private void LateUpdate()
        {
            bool protectedNow = GetComponent<CommonPotion>().HasRevivalProtection;
            if (!IsDead && !protectedNow) { if (_status != null) _status.gameObject.SetActive(false); return; }
            if (_status == null)
            {
                var label = new GameObject("LifeStatus"); label.transform.SetParent(transform, false);
                _status = label.AddComponent<TextMesh>(); _status.fontSize = 48; _status.characterSize = 0.065f;
                _status.anchor = TextAnchor.MiddleCenter;
            }
            _status.gameObject.SetActive(true);
            _status.text = IsDead ? "전투 불능" : "부활 보호";
            _status.color = IsDead ? Color.gray : new Color(0.5f, 1f, 1f);
            _status.transform.position = transform.position + Vector3.up * 2.1f;
            if (Camera.main != null) _status.transform.rotation = Camera.main.transform.rotation;
        }

        public static List<Transform> OrderedMembers()
        {
            if (TargetingManager.Instance != null) return TargetingManager.Instance.GetOrderedPartyMembers();
            var list = new List<Transform>();
            var player = FindFirstObjectByType<PlayerController>(); if (player != null) list.Add(player.transform);
            var bots = FindObjectsByType<PartyBotAI>(FindObjectsSortMode.None);
            Array.Sort(bots, (a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (var bot in bots) list.Add(bot.transform);
            return list;
        }
    }
}
