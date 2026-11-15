using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    // An editable child of the active RaidHUD. Runtime only changes text and visibility.
    public sealed class RaidLobbyUI : MonoBehaviour
    {
        [SerializeField] private Text interactionHint;
        [SerializeField] private Text dialogue;
        private float _dialogueUntil;

        public void Hint(string message)
        {
            if (interactionHint == null) return;
            interactionHint.gameObject.SetActive(!string.IsNullOrEmpty(message));
            if (!string.IsNullOrEmpty(message)) interactionHint.text = message;
        }
        public void Say(string message)
        {
            if (dialogue == null) return;
            dialogue.text = message;
            dialogue.gameObject.SetActive(true);
            _dialogueUntil = Time.unscaledTime + 6f;
        }
        public void Hide()
        {
            Hint(null);
            if (dialogue != null) dialogue.gameObject.SetActive(false);
        }
        private void Update()
        {
            if (dialogue != null && dialogue.gameObject.activeSelf && Time.unscaledTime >= _dialogueUntil)
                dialogue.gameObject.SetActive(false);
        }
        public static RaidLobbyUI CreateDefault(Transform parent)
        {
            var root = new GameObject("Lobby Prompts", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = Vector2.zero; rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = rootRect.offsetMax = Vector2.zero;
            var view = root.AddComponent<RaidLobbyUI>();
            view.interactionHint = Label(root.transform, "Interaction Hint", -160, 22);
            view.dialogue = Label(root.transform, "NPC Dialogue", -215, 20);
            view.Hide();
            return view;
        }
        private static Text Label(Transform parent, string name, float y, int size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
            rect.pivot = new Vector2(.5f, 1); rect.anchoredPosition = new Vector2(0, y);
            rect.sizeDelta = new Vector2(920, 52);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size; text.color = Color.white; text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            return text;
        }
    }
}
