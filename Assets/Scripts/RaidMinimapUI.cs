using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    // Local presentation only. The map never selects targets or sends network input.
    public sealed class RaidMinimapUI : MonoBehaviour
    {
        [Header("Editable prefab references")]
        [SerializeField] private RectTransform panel;
        public RectTransform Panel=>panel;
        [SerializeField] private RectTransform mapArea;
        [SerializeField] private Image playerMarker;
        [SerializeField] private Image bossMarker;
        [SerializeField] private Image partyMarkerTemplate;
        [SerializeField] private Image addMarkerTemplate;
        [SerializeField] private Text stageLabel;
        [SerializeField] private Image approachPath;
        [SerializeField] private Image raidField;
        [Header("Presentation")]
        [SerializeField] private Color partyColor = new Color(.28f, .83f, 1f);
        [SerializeField] private Color bossColor = new Color(1f, .28f, .28f);
        [SerializeField] private Color addColor = new Color(1f, .49f, .38f);
        [SerializeField] private Color deadColor = Color.gray;
        [SerializeField] private Color selectedColor = new Color(1f, .89f, .36f);
        [SerializeField, Range(8f, 40f)] private float fallbackArenaRadius = 18f;
        [SerializeField, Range(.05f, .5f)] private float refreshSeconds = .1f;

        private readonly List<Image> _partyMarkers = new List<Image>();
        private readonly List<Image> _addMarkers = new List<Image>();
        private float _nextRefresh;
        private PlayerController _player;
        private int _shownStage = int.MinValue;
        private int _shownStageMax;
        private bool _shownLobby;
        private bool _shownTraining;
        private float _mapOriginZ;

        private void OnEnable() { _nextRefresh = 0; }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + refreshSeconds;
            Refresh();
        }

        private void Refresh()
        {
            if (panel == null || mapArea == null || playerMarker == null || bossMarker == null) return;
            var session = RaidNetworkSession.Instance;
            var stage = StageManager.Instance;
            bool training = CharacterTrainingSession.Active;
            bool lobby = RaidLobbyRoom.Active;
            var phase = RaidNetworkSession.Multiplayer ? session.BattlePhase : stage != null ? stage.Progress.Phase : RaidBattlePhase.Lobby;
            bool show = session != null && !session.Ending && !session.MenuOpen &&
                (training || (session.Mode != RaidSessionMode.Menu && session.Mode != RaidSessionMode.Training &&
                    (lobby || phase != RaidBattlePhase.Lobby) && phase != RaidBattlePhase.Victory && phase != RaidBattlePhase.Closed));
            show=show||LocalHUDLayoutController.IsEditing;
            if (panel.gameObject.activeSelf != show) panel.gameObject.SetActive(show);
            if (!show) return;

            if (_player == null) _player = stage != null ? stage.player : FindFirstObjectByType<PlayerController>();
            float radius = _player != null ? Mathf.Max(1f, _player.arenaBoundaryRadius) : fallbackArenaRadius;
            var approach = !lobby && !training ? RaidApproachField.Instance : null;
            bool showApproach = approach != null && approach.Showing;
            _mapOriginZ = showApproach ? (approach.StartZ + approach.FieldRadius) * .5f : 0;
            if (showApproach) radius = Mathf.Max(approach.FieldRadius - _mapOriginZ, _mapOriginZ - approach.StartZ) + 1;
            UpdateApproachMap(approach, showApproach, radius);
            var selected = TargetingManager.Instance != null ? TargetingManager.Instance.CurrentTarget : null;
            var local = _player != null ? _player.transform : null;
            Place(playerMarker, local, radius, true);
            if (local != null)
            {
                // North is +Z. Rotating the camera does not rotate the map or change facing.
                var facing = local.forward;
                var rotation = Quaternion.Euler(0, 0, -Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg);
                if (playerMarker.rectTransform.localRotation != rotation) playerMarker.rectTransform.localRotation = rotation;
            }
            var boss = stage != null ? stage.boss : null;
            Place(bossMarker, !lobby && boss != null && (training || approach == null || approach.BossVisible) && CombatRange.LivingEnemy(boss.transform) ? boss.transform : null, radius, true);
            SetColor(bossMarker, boss != null && selected == boss.transform ? selectedColor : bossColor);

            var members = PartyLife.OrderedMembers();
            int partyCount = 0;
            if (partyMarkerTemplate != null)
                foreach (var member in members)
                {
                    if (member == null || member == local) continue;
                    var marker = Acquire(_partyMarkers, partyMarkerTemplate, partyCount++);
                    Place(marker, member, radius, true);
                    SetColor(marker, member == selected ? selectedColor : PartyAlive(member) ? partyColor : deadColor);
                }
            HideUnused(_partyMarkers, partyCount);

            int addCount = 0;
            var spawner = BossAddSpawner.Instance;
            if (addMarkerTemplate != null && spawner != null)
                foreach (var add in spawner.activeAdds)
                {
                    if (add == null || !CombatRange.LivingEnemy(add.transform)) continue;
                    var marker = Acquire(_addMarkers, addMarkerTemplate, addCount++);
                    Place(marker, add.transform, radius, true);
                    SetColor(marker, selected == add.transform ? selectedColor : addColor);
                }
            var trainingSession = training ? CharacterTrainingSession.Instance : null;
            if (addMarkerTemplate != null && trainingSession != null)
                foreach (var add in trainingSession.ExtraTargets)
                {
                    if (add == null || !CombatRange.LivingEnemy(add.transform)) continue;
                    var marker = Acquire(_addMarkers, addMarkerTemplate, addCount++);
                    Place(marker, add.transform, radius, true);
                    SetColor(marker, selected == add.transform ? selectedColor : addColor);
                }
            HideUnused(_addMarkers, addCount);
            if (stageLabel != null && (training != _shownTraining || lobby != _shownLobby ||
                (stage != null ? stage.currentStage : 0) != _shownStage ||
                (stage != null ? stage.maxStage : 0) != _shownStageMax))
            {
                _shownTraining = training;
                _shownLobby = lobby;
                _shownStage = stage != null ? stage.currentStage : 0;
                _shownStageMax = stage != null ? stage.maxStage : 0;
                stageLabel.text = training ? "TRAINING" : lobby ? "대기실" : stage != null ? "STAGE " + _shownStage + " / " + _shownStageMax : "";
            }
        }

        private static void SetColor(Image image, Color color)
        {
            if (image.color != color) image.color = color;
        }

        private static bool PartyAlive(Transform member)
        {
            if (member == null || !member.gameObject.activeInHierarchy) return false;
            // Remote avatars on the host have neither PlayerController nor PartyBotAI.
            // Their authoritative/replicated combat state is still available here.
            var avatar = member.GetComponent<NetworkRaidAvatar>();
            if (avatar != null && avatar.State != null) return !avatar.State.dead;
            return CombatRange.LivingPartyMember(member);
        }

        private Image Acquire(List<Image> markers, Image template, int index)
        {
            while (markers.Count <= index)
            {
                var marker = Instantiate(template, template.transform.parent);
                marker.name = template.name + " " + (markers.Count + 1);
                marker.raycastTarget = false;
                markers.Add(marker);
                if (playerMarker != null) playerMarker.transform.SetAsLastSibling();
            }
            var result = markers[index];
            result.gameObject.SetActive(true);
            return result;
        }

        private static void HideUnused(List<Image> markers, int count)
        {
            for (int i = count; i < markers.Count; i++)
                if (markers[i] != null) markers[i].gameObject.SetActive(false);
        }

        private void Place(Image marker, Transform actor, float radius, bool visible)
        {
            if (marker == null) return;
            visible &= actor != null;
            if (marker.gameObject.activeSelf != visible) marker.gameObject.SetActive(visible);
            if (!visible) return;
            float halfWidth = Mathf.Max(1f, mapArea.rect.width * .5f);
            float halfHeight = Mathf.Max(1f, mapArea.rect.height * .5f);
            Vector2 world = new Vector2(actor.position.x, actor.position.z - _mapOriginZ);
            Vector2 point = Vector2.ClampMagnitude(world / radius, 1f);
            Vector2 position = new Vector2(point.x * halfWidth, point.y * halfHeight);
            if (marker.rectTransform.anchoredPosition != position) marker.rectTransform.anchoredPosition = position;
        }

        private void UpdateApproachMap(RaidApproachField approach, bool visible, float radius)
        {
            if (visible && (approachPath == null || raidField == null))
            {
                // Decorative fallback for an older editable HUD prefab. Existing authored shapes win.
                if (approachPath == null) approachPath = MapShape("Approach path", new Color(.3f, .43f, .39f, .7f));
                if (raidField == null) raidField = MapShape("Raid field", new Color(.3f, .38f, .47f, .7f));
            }
            if (approachPath == null || raidField == null) return;
            approachPath.gameObject.SetActive(visible);
            raidField.gameObject.SetActive(visible);
            if (!visible) return;
            float sx = mapArea.rect.width / (radius * 2);
            float sy = mapArea.rect.height / (radius * 2);
            float pathEnd = -approach.FieldRadius + 1;
            float pathMid = (approach.StartZ + pathEnd) * .5f;
            approachPath.rectTransform.sizeDelta = new Vector2(approach.Width * sx, (pathEnd - approach.StartZ) * sy);
            approachPath.rectTransform.anchoredPosition = new Vector2(0, (pathMid - _mapOriginZ) * sy);
            raidField.rectTransform.sizeDelta = new Vector2(approach.FieldRadius * 2 * sx, approach.FieldRadius * 2 * sy);
            raidField.rectTransform.anchoredPosition = new Vector2(0, -_mapOriginZ * sy);
        }

        private Image MapShape(string name, Color color)
        {
            var rect = Rect(name, mapArea, Vector2.zero);
            var result = rect.gameObject.AddComponent<Image>();
            result.color = color; result.raycastTarget = false;
            result.transform.SetAsFirstSibling();
            return result;
        }

        private void OnValidate()
        {
            if (panel != null && (mapArea == null || playerMarker == null || bossMarker == null ||
                                  partyMarkerTemplate == null || addMarkerTemplate == null))
                Debug.LogWarning("Minimap needs panel, map area, player/boss markers and party/add templates.", this);
        }

        // Used by the optional editor command and only as a fallback for older HUD prefabs.
        public static RaidMinimapUI CreateDefault(Transform hud)
        {
            var panel = Rect("Minimap", hud, new Vector2(216, 244));
            panel.anchorMin = panel.anchorMax = new Vector2(1, 1);
            panel.pivot = new Vector2(1, 1);
            // Reserve the right column for settings, training controls and combat metrics,
            // and stay below the centered target frame.
            panel.anchoredPosition = new Vector2(-470, -250);
            var background = panel.gameObject.AddComponent<Image>();
            background.color = new Color(.035f, .055f, .09f, .87f); background.raycastTarget = false;
            var map = Rect("Arena (north up)", panel, new Vector2(184, 184));
            map.anchorMin = map.anchorMax = new Vector2(.5f, 1f);
            map.pivot = new Vector2(.5f, 1f);
            map.anchoredPosition = new Vector2(0, -27);
            var mapImage = map.gameObject.AddComponent<Image>();
            mapImage.color = new Color(.13f, .18f, .23f, .85f); mapImage.raycastTarget = false;
            // The controller must stay active while the menu hides the visual panel.
            var minimap = hud.gameObject.AddComponent<RaidMinimapUI>();
            minimap.panel = panel; minimap.mapArea = map;
            minimap.playerMarker = Dot("Player heading", map, new Vector2(13, 13), new Color(1f, .92f, .48f));
            var facingTip = Dot("Facing tip", minimap.playerMarker.transform, new Vector2(5, 6), new Color(1f, .92f, .48f));
            facingTip.rectTransform.anchoredPosition = new Vector2(0, 10);
            minimap.bossMarker = Dot("Boss", map, new Vector2(15, 15), minimap.bossColor);
            minimap.partyMarkerTemplate = Dot("Party marker template", map, new Vector2(10, 10), minimap.partyColor);
            minimap.addMarkerTemplate = Dot("Add marker template", map, new Vector2(9, 9), minimap.addColor);
            minimap.partyMarkerTemplate.gameObject.SetActive(false);
            minimap.addMarkerTemplate.gameObject.SetActive(false);
            var north = Label("North", panel, "N", new Vector2(24, 22), new Vector2(0, -5));
            north.alignment = TextAnchor.MiddleCenter;
            minimap.stageLabel = Label("Stage", panel, "STAGE 1 / 6", new Vector2(190, 24), new Vector2(0, -215));
            minimap.stageLabel.alignment = TextAnchor.MiddleCenter;
            minimap.playerMarker.transform.SetAsLastSibling();
            foreach (var graphic in panel.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            return minimap;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.sizeDelta = size; return rect;
        }

        private static Image Dot(string name, Transform parent, Vector2 size, Color color)
        {
            var rect = Rect(name, parent, size);
            var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false;
            return image;
        }

        private static Text Label(string name, Transform parent, string value, Vector2 size, Vector2 position)
        {
            var rect = Rect(name, parent, size);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.pivot = new Vector2(.5f, 1f); rect.anchoredPosition = position;
            var label = rect.gameObject.AddComponent<Text>(); label.text = value;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (label.font == null) label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = 15; label.color = new Color(.92f, .92f, .8f); label.raycastTarget = false;
            return label;
        }
    }
}
