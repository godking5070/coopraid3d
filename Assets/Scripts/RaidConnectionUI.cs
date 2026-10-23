using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    public sealed class RaidConnectionUI : MonoBehaviour
    {
        private RaidNetworkSession _session;
        private GameObject _overlay, _start, _room, _bar;
        private Text _status, _barText, _roster;
        private InputField _name, _address;
        private readonly Button[] _roles = new Button[4];
        private Font _font;
        private void Start()
        {
            _session = GetComponent<RaidNetworkSession>();
            var canvas = FindFirstObjectByType<SkillHotbarUI>()?.GetComponentInParent<Canvas>() ?? FindFirstObjectByType<Canvas>();
            if (canvas == null) return;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var overlay = Box(canvas.transform, "LAN_ConnectionOverlay", Vector2.zero, Vector2.zero, new Color(0.025f, 0.04f, 0.07f, 0.97f));
            overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one; overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            _overlay = overlay.gameObject;
            Label(overlay, "4인 협동 레이드", new Vector2(0, 250), new Vector2(800, 50), 34);
            _status = Label(overlay, "", new Vector2(0, 190), new Vector2(850, 65), 20);
            _start = Rect(overlay, "StartOptions", Vector2.zero, new Vector2(800, 380)).gameObject;
            Label(_start.transform, "플레이어 이름", new Vector2(-260, 115), new Vector2(220, 40), 20);
            _name = Field(_start.transform, new Vector2(130, 115), PlayerPrefs.GetString("Raid.LAN.Name", "Player"), 16);
            Label(_start.transform, "호스트 주소 : 포트", new Vector2(-260, 60), new Vector2(220, 40), 20);
            _address = Field(_start.transform, new Vector2(130, 60), PlayerPrefs.GetString("Raid.LAN.Address", "127.0.0.1:7777"), 40);
            ButtonAt(_start.transform, "싱글플레이 · AI 파티", new Vector2(0, -20), new Vector2(560, 48), _session.SinglePlay);
            ButtonAt(_start.transform, "방 만들기 · 호스트", new Vector2(-150, -85), new Vector2(280, 48), () => Connect(true));
            ButtonAt(_start.transform, "주소로 참가 · 클라이언트", new Vector2(150, -85), new Vector2(280, 48), () => Connect(false));
            Label(_start.transform, "멀티플레이: 연결·역할·이동·대쉬 테스트\n전투·보스·카드 동기화는 다음 단계입니다.\n같은 PC: 127.0.0.1 / 같은 LAN: 호스트의 IPv4 주소", new Vector2(0, -185), new Vector2(820, 100), 18);
            _room = Rect(overlay, "RoomOptions", Vector2.zero, new Vector2(800, 380)).gameObject;
            _roster = Label(_room.transform, "", new Vector2(0, 70), new Vector2(800, 130), 22);
            for (int i = 0; i < 4; i++)
            {
                int role = i + 1;
                _roles[i] = ButtonAt(_room.transform, CombatMetricsManager.RoleName(role), new Vector2(-285 + i * 190, -50), new Vector2(180, 62), () => _session.SelectRole(role));
            }
            ButtonAt(_room.transform, "이동 테스트 · 화면으로 돌아가기", new Vector2(0, -130), new Vector2(470, 48), () => _session.MenuOpen = false);
            ButtonAt(_room.transform, "연결 종료 · 시작 화면", new Vector2(0, -195), new Vector2(470, 48), _session.Disconnect);
            var bar = Box(canvas.transform, "LAN_Status", Vector2.zero, new Vector2(390, 70), new Color(0.04f, 0.08f, 0.13f, 0.94f));
            bar.anchorMin = bar.anchorMax = new Vector2(0, 1); bar.pivot = new Vector2(0, 1); bar.anchoredPosition = new Vector2(15, -64);
            _bar = bar.gameObject;
            _barText = Label(bar, "", new Vector2(-65, 0), new Vector2(245, 60), 15);
            ButtonAt(bar, "접속 / 역할", new Vector2(125, 0), new Vector2(115, 48), () => _session.MenuOpen = true);
        }
        private void Connect(bool host)
        {
            PlayerPrefs.SetString("Raid.LAN.Name", _name.text); PlayerPrefs.SetString("Raid.LAN.Address", _address.text); PlayerPrefs.Save();
            _session.StartConnection(host, _name.text, _address.text);
        }
        private void Update()
        {
            if (_overlay == null) return;
            _overlay.SetActive(_session.MenuOpen); _bar.SetActive(!_session.MenuOpen);
            if (_session.MenuOpen) _overlay.transform.SetAsLastSibling();
            bool starting = _session.Mode == RaidSessionMode.Menu;
            _start.SetActive(starting); _room.SetActive(!starting);
            _status.text = _session.Status;
            _barText.text = _session.Mode == RaidSessionMode.Single ? "싱글플레이 · AI 파티" : $"LAN 이동 테스트 · {_session.PlayerCount}/4명\n전투 비활성 · SPACE 대쉬";
            if (starting) return;
            if (_session.Mode == RaidSessionMode.Single)
                _roster.text = "싱글플레이 진행 중\n연결 종료를 누르면 시작 화면으로 돌아갑니다.";
            else
            {
                _roster.text = $"현재 인원 {_session.PlayerCount}/4 (호스트 포함)\n";
                foreach (var avatar in _session.Avatars.Values) _roster.text += $"{avatar.DisplayName} · {avatar.RoleName}{(avatar.IsLocal ? " (나)" : "")}\n";
            }
            for (int i = 0; i < _roles.Length; i++)
            {
                NetworkRaidAvatar occupant = null;
                foreach (var avatar in _session.Avatars.Values) if (avatar.Role == i + 1) occupant = avatar;
                _roles[i].gameObject.SetActive(RaidNetworkSession.Multiplayer);
                _roles[i].interactable = _session.HasLocalAvatar && (occupant == null || occupant.IsLocal);
                _roles[i].GetComponentInChildren<Text>().text = CombatMetricsManager.RoleName(i + 1) + "\n" + (occupant != null ? occupant.DisplayName : "선택 가능");
            }
        }
        private RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchoredPosition = pos; rect.sizeDelta = size; return rect;
        }
        private RectTransform Box(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        { var rect = Rect(parent, name, pos, size); rect.gameObject.AddComponent<Image>().color = color; return rect; }
        private Text Label(Transform parent, string value, Vector2 pos, Vector2 size, int fontSize)
        {
            var text = Rect(parent, "Text", pos, size).gameObject.AddComponent<Text>(); text.font = _font; text.fontSize = fontSize;
            text.text = value; text.color = Color.white; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false; return text;
        }
        private Button ButtonAt(Transform parent, string caption, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            var rect = Box(parent, caption, pos, size, new Color(0.12f, 0.28f, 0.4f)); var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>(); button.onClick.AddListener(action); Label(rect, caption, Vector2.zero, size - Vector2.one * 8, 18); return button;
        }
        private InputField Field(Transform parent, Vector2 pos, string value, int limit)
        {
            var rect = Box(parent, "Input", pos, new Vector2(450, 44), new Color(0.12f, 0.15f, 0.2f));
            var field = rect.gameObject.AddComponent<InputField>(); field.targetGraphic = rect.GetComponent<Image>();
            field.textComponent = Label(rect, "", Vector2.zero, new Vector2(425, 40), 20); field.textComponent.alignment = TextAnchor.MiddleLeft;
            field.textComponent.supportRichText = false; field.characterLimit = limit; field.text = value; return field;
        }
        private void OnDestroy() { if (_overlay != null) Destroy(_overlay); if (_bar != null) Destroy(_bar); }
    }
}
