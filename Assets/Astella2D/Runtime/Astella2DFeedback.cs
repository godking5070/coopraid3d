using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Astella.TwoD
{
    /// <summary>
    /// Code-built combat feedback: floating numbers, camera shake and synthesized sound effects.
    /// No audio, font or texture assets are added; everything here is released with the game.
    /// </summary>
    internal sealed class Astella2DFeedback
    {
        private sealed class Floater
        {
            public Text Label;
            public Vector3 World;
            public float Life, Total, Rise;
        }

        private const int SampleRate = 44100;
        private readonly List<Floater> _active = new List<Floater>();
        private readonly Stack<Floater> _pool = new Stack<Floater>();
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly RectTransform _layer;
        private readonly Camera _camera;
        private readonly Font _font;
        private readonly AudioSource _audio;
        private readonly System.Random _noise = new System.Random(20261123);
        private Vector3 _cameraHome;
        private float _shake, _shakeTime, _shakeTotal;

        public Astella2DFeedback(RectTransform layer, Camera camera, Font font)
        {
            _layer = layer;
            _camera = camera;
            _font = font;
            _cameraHome = camera.transform.position;
            _audio = camera.gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0;
            _audio.volume = .5f;
            BuildClips();
        }

        public float Volume { get => _audio.volume; set => _audio.volume = Mathf.Clamp01(value); }

        public void Number(Vector2 world, string text, Color color, int size, float life = .85f)
        {
            Floater floater = _pool.Count > 0 ? _pool.Pop() : NewFloater();
            floater.Label.text = text;
            floater.Label.fontSize = size;
            floater.Label.color = color;
            // Spread numbers so simultaneous hits stay readable.
            floater.World = new Vector3(world.x + UnityEngine.Random.Range(-.7f, .7f), world.y + UnityEngine.Random.Range(-.25f, .35f), 0);
            floater.Life = floater.Total = life;
            floater.Rise = 1.1f + UnityEngine.Random.Range(0, .35f);
            floater.Label.gameObject.SetActive(true);
            Place(floater);
            _active.Add(floater);
        }

        public void Shake(float strength, float time)
        {
            if (strength < _shake * (_shakeTime / Mathf.Max(.0001f, _shakeTotal))) return;
            _shake = strength;
            _shakeTime = _shakeTotal = Mathf.Max(.01f, time);
        }

        public void Play(string clip, float volume = 1, float pitchJitter = .05f)
        {
            if (!_clips.TryGetValue(clip, out AudioClip value)) throw new ArgumentException("Unknown 2D feedback clip: " + clip, nameof(clip));
            _audio.pitch = 1 + UnityEngine.Random.Range(-pitchJitter, pitchJitter);
            _audio.PlayOneShot(value, volume);
        }

        public void Tick(float dt)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Floater floater = _active[i];
                floater.Life -= dt;
                if (floater.Life <= 0)
                {
                    floater.Label.gameObject.SetActive(false);
                    _active.RemoveAt(i);
                    _pool.Push(floater);
                    continue;
                }
                floater.World.y += floater.Rise * dt * (floater.Life / floater.Total + .25f);
                Color color = floater.Label.color;
                color.a = Mathf.Clamp01(floater.Life / (floater.Total * .45f));
                floater.Label.color = color;
                Place(floater);
            }

            if (_shakeTime > 0)
            {
                _shakeTime = Mathf.Max(0, _shakeTime - dt);
                float strength = _shake * (_shakeTime / _shakeTotal);
                _camera.transform.position = _cameraHome + (Vector3)(UnityEngine.Random.insideUnitCircle * strength);
            }
            else if (_camera.transform.position != _cameraHome)
            {
                _camera.transform.position = _cameraHome;
            }
        }

        public void Clear()
        {
            foreach (Floater floater in _active)
            {
                floater.Label.gameObject.SetActive(false);
                _pool.Push(floater);
            }
            _active.Clear();
            _shakeTime = 0;
            _camera.transform.position = _cameraHome;
        }

        public void Release()
        {
            Clear();
            foreach (AudioClip clip in _clips.Values) if (clip != null) UnityEngine.Object.Destroy(clip);
            _clips.Clear();
        }

        private Floater NewFloater()
        {
            var go = new GameObject("Combat Number", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(_layer, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(260, 60);
            var label = go.GetComponent<Text>();
            label.font = _font;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(.02f, .01f, .06f, .95f);
            outline.effectDistance = new Vector2(1.6f, -1.6f);
            return new Floater { Label = label };
        }

        private void Place(Floater floater)
        {
            Vector3 screen = _camera.WorldToScreenPoint(floater.World);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_layer, screen, null, out Vector2 local))
                ((RectTransform)floater.Label.transform).anchoredPosition = local;
        }

        // ---- Synthesized sound effects ----

        private void BuildClips()
        {
            Add("hit", .08f, (t, d) => Noise() * Decay(t, 40) * .55f + Sine(170, t) * Decay(t, 30) * .5f);
            Add("heavy", .32f, (t, d) => Sine(Mathf.Lerp(140, 45, t / d), t) * Decay(t, 9) * .8f + Noise() * Decay(t, 22) * .45f);
            Add("dash", .2f, (t, d) => Noise() * Mathf.Sin(Mathf.PI * t / d) * .35f);
            Add("impact", .42f, (t, d) => LowNoise(.12f) * Decay(t, 8) * .9f + Sine(Mathf.Lerp(90, 38, t / d), t) * Decay(t, 7) * .55f);
            Add("cast", .34f, (t, d) => Sine(Mathf.Lerp(280, 620, t / d), t) * Mathf.Sin(Mathf.PI * t / d) * .22f);
            Add("hurt", .17f, (t, d) => Square(Mathf.Lerp(230, 130, t / d), t) * Decay(t, 14) * .28f);
            Add("heal", .5f, (t, d) => (Sine(660, t) * Gate(t, 0, .5f) + Sine(880, t) * Gate(t, .1f, .5f) + Sine(1320, t) * Gate(t, .2f, .5f)) * Decay(t, 5) * .2f);
            Add("guard", .36f, (t, d) => (Sine(440, t) + Sine(554, t) * .7f) * Decay(t, 7) * .22f + Noise() * Decay(t, 30) * .12f);
            Add("ult", .75f, (t, d) => (Sine(220, t) + Sine(330, t) + Sine(440, t) * .6f) * Mathf.Sin(Mathf.PI * Mathf.Min(1, t / d * 1.4f)) * .16f + LowNoise(.2f) * Decay(t, 4) * .4f);
            Add("down", .6f, (t, d) => Sine(Mathf.Lerp(330, 110, t / d), t) * Decay(t, 3.5f) * .32f);
            Add("revive", .55f, (t, d) => Sine(Mathf.Lerp(330, 990, t / d), t) * Mathf.Sin(Mathf.PI * t / d) * .26f);
            Add("ui", .06f, (t, d) => Sine(940, t) * Decay(t, 50) * .3f);
            Add("win", 1.1f, (t, d) => Chord(t, new[] { 523f, 659f, 784f, 1046f }, .16f) * Decay(t, 2.2f) * .3f);
            Add("lose", 1.1f, (t, d) => Chord(t, new[] { 392f, 330f, 262f, 196f }, .2f) * Decay(t, 2.2f) * .3f);
            Add("enrage", .7f, (t, d) => Square(Mathf.Lerp(70, 110, t / d), t) * Mathf.Sin(Mathf.PI * t / d) * .22f + LowNoise(.1f) * .25f * Decay(t, 3));
        }

        private float _lowNoise;
        private float Noise() => (float)(_noise.NextDouble() * 2 - 1);
        private float LowNoise(float smoothing) { _lowNoise += (Noise() - _lowNoise) * smoothing; return _lowNoise * 2.2f; }
        private static float Sine(float frequency, float t) => Mathf.Sin(2 * Mathf.PI * frequency * t);
        private static float Square(float frequency, float t) => Mathf.Sign(Mathf.Sin(2 * Mathf.PI * frequency * t));
        private static float Decay(float t, float rate) => Mathf.Exp(-t * rate);
        private static float Gate(float t, float from, float to) => t >= from && t < to ? 1 : 0;

        private static float Chord(float t, float[] notes, float step)
        {
            float value = 0;
            for (int i = 0; i < notes.Length; i++)
                if (t >= i * step) value += Sine(notes[i], t) * Mathf.Exp(-(t - i * step) * 4);
            return value;
        }

        private void Add(string name, float duration, Func<float, float, float> wave)
        {
            int count = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[count];
            _lowNoise = 0;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float fade = Mathf.Clamp01((duration - t) / .012f) * Mathf.Clamp01(t / .002f);
                data[i] = Mathf.Clamp(wave(t, duration) * fade, -1, 1);
            }
            var clip = AudioClip.Create("Astella2D_" + name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            _clips[name] = clip;
        }
    }
}
