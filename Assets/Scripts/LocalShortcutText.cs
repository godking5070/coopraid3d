using UnityEngine;
using UnityEngine.UI;

namespace RaidRogue
{
    // A real MonoScript and serialized source template must survive prefab save/editor restart.
    public sealed class LocalShortcutText : MonoBehaviour
    {
        private Text _text;
        [SerializeField, TextArea] private string _template;
        private int _revision = -1;
        public string Template => _template;
        public void SetTemplate(string template) { _template = template; _revision = -1; }
        private void Awake() { _text = GetComponent<Text>(); }
        public static void Set(Text text, string template)
        {
            if (text == null) return;
            if (!Application.isPlaying) { text.text = LocalInputBindings.SkillText(template); return; }
            var label = text.GetComponent<LocalShortcutText>() ?? text.gameObject.AddComponent<LocalShortcutText>();
            label._text = text; label.SetTemplate(template); label.Refresh();
        }
        private void LateUpdate() => Refresh();
        private void Refresh()
        {
            if (_text == null || _template == null || _revision == LocalInputBindings.Revision) return;
            _text.text = LocalInputBindings.SkillText(_template); _revision = LocalInputBindings.Revision;
        }
    }
}
