using UnityEngine;

namespace RaidRogue
{
    public enum LocalAudioBus { Music, Effects, UI, MasterPreview }
    [RequireComponent(typeof(AudioSource))]
    public sealed class LocalAudioChannel : MonoBehaviour
    {
        public LocalAudioBus bus = LocalAudioBus.Effects;
        private AudioSource _source;
        private float _baseVolume, _lastApplied;
        private static AudioClip _preview;
        private void Awake() { _source = GetComponent<AudioSource>(); _baseVolume = _lastApplied = _source.volume; }
        private void LateUpdate()
        {
            if (_source == null) return;
            var settings = LocalSettingsRuntime.Instance?.Previewed; if (settings == null) return;
            if (!Mathf.Approximately(_source.volume, _lastApplied)) _baseVolume = _source.volume;
            float gain = bus == LocalAudioBus.MasterPreview ? 1 : bus == LocalAudioBus.Music ? settings.music : bus == LocalAudioBus.UI ? settings.ui : settings.effects;
            _source.volume = _lastApplied = _baseVolume * gain;
        }
        public static void Preview(LocalAudioBus bus)
        {
            if (_preview == null)
            {
                const int rate = 22050; var samples = new float[rate / 5];
                for (int i = 0; i < samples.Length; i++) samples[i] = .12f * Mathf.Sin(2 * Mathf.PI * 660 * i / rate) * Mathf.Sin(Mathf.PI * i / samples.Length);
                _preview = AudioClip.Create("SettingsPreviewTone", samples.Length, 1, rate, false); _preview.SetData(samples, 0);
            }
            var go = new GameObject("SettingsAudioPreview"); var audio = go.AddComponent<AudioSource>();
            audio.playOnAwake = false; audio.ignoreListenerPause = true; audio.spatialBlend = 0;
            var channel = go.AddComponent<LocalAudioChannel>(); channel.bus = bus; channel.LateUpdate();
            audio.PlayOneShot(_preview); go.AddComponent<LocalAudioPreviewLifetime>();
        }
    }
    public sealed class LocalAudioPreviewLifetime : MonoBehaviour
    {
        private float _end;
        private void Awake() { _end = Time.unscaledTime + .4f; }
        private void Update() { if (Time.unscaledTime >= _end) Destroy(gameObject); }
    }
}
