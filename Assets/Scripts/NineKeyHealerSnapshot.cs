using System;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    [Serializable] public sealed class NineKeyHealerCombatSnapshot
    {
        public int version = 1, route, songTarget;
        public string job = "", encore = "";
        public float safeReady, flowerMoveReady, gardenTick;
        public int[] flowerInside = Array.Empty<int>();
        public HealerResourceSnapshot resources;
        public HealerSeed[] seeds;
        public HealerHarvestSnapshot harvest;
        public HealerRequest[] requests;
        public HealerSongSnapshot song;
        public HealerLinkSnapshot link;
        public HealerSurgerySnapshot surgery;
        public HealerLayerSnapshot layers;
        public CharacterKitSnapshot effects;
    }
    public sealed partial class NineKeyHealerKit
    {
        public string Capture()
        {
            Bind();
            return JsonUtility.ToJson(new NineKeyHealerCombatSnapshot
            {
                job = Job.Id, route = Route, resources = _resources.Snapshot, seeds = _garden.Snapshot,
                harvest = _harvest.Snapshot, requests = _conductor.Snapshot, song = _song.Snapshot,
                link = _link.Snapshot, surgery = _surgery.Snapshot, layers = _layers.Snapshot,
                effects = _effects.Data.Copy(), encore = _encore, safeReady = _safeReady,
                flowerMoveReady = _flowerMoveReady, gardenTick = _gardenTick, songTarget = _songTarget,
                flowerInside = _flowerInside.ToArray()
            });
        }
        public bool Restore(string json)
        {
            Bind(); if (_runtime == null || string.IsNullOrEmpty(json)) return false;
            NineKeyHealerCombatSnapshot value;
            try { value = JsonUtility.FromJson<NineKeyHealerCombatSnapshot>(json); } catch (ArgumentException) { return false; }
            if (value == null || value.version != 1 || value.job != Job.Id || value.route != Route ||
                value.encore != "" && value.encore != "q" && value.encore != "f" ||
                !HealerTime.Nonnegative(value.safeReady) || !HealerTime.Nonnegative(value.flowerMoveReady) ||
                !HealerTime.Nonnegative(value.gardenTick) || value.gardenTick >= 1 || value.flowerInside == null ||
                value.flowerInside.Length > 8 || value.flowerInside.Any(t => t == 0) || value.flowerInside.Distinct().Count() != value.flowerInside.Length) return false;
            var resources = new NineKeyHealerResources(_runtime, Job);
            var garden = new HealerGardenState(); var harvest = new HealerHarvestState();
            var requests = new HealerConductorState(); var song = new HealerSongState(resources);
            var link = new HealerLinkState(); var surgery = new HealerSurgeryState(); var layers = new HealerLayerState();
            var effects = new CharacterEffectState();
            if (!resources.Restore(value.resources) || !garden.Restore(value.seeds, Core(1, 0) ? 2 : 3) ||
                !harvest.Restore(value.harvest) || !requests.Restore(value.requests, CharacterKit.Id(transform), Core(1, 0) ? 1 : 3) ||
                !song.Restore(value.song) || !link.Restore(value.link) || !surgery.Restore(value.surgery) ||
                !layers.Restore(value.layers) || !effects.Restore(value.effects)) return false;
            if (value.seeds.Length > 0 && (Job.index != 3 || Route != 1) || harvest.Active && (Job.index != 3 || Route != 3) ||
                value.requests.Length > 0 && (Job.index != 4 || Route != 1) || song.Active && (Job.index != 4 || Route != 3) ||
                layers.Active && (Job.index != 4 || Route != 2) || link.Active && (Job.index != 5 || Route != 1) ||
                surgery.Active && (Job.index != 5 || Route != 3)) return false;
            if (value.seeds.Any(s => s.nextGrowth > 4) || value.harvest.remaining > 16 || value.requests.Any(r => r.remaining > 10) ||
                value.song.interval > 2 || value.link.remaining > 12 || value.surgery.remaining > 12 || value.layers.remaining > 12) return false;
            if (harvest.Active && value.harvest.maximum != (Core(3, 0) ? 2 : Core(3, 1) ? 4 : 3) ||
                link.Active && value.link.branched != Core(1, 1) ||
                song.Active && (value.song.maximum != (Core(3, 0) ? 2 : 3) || value.song.interval != (Core(3, 0) ? 1.5f : 2)) ||
                value.layers.recipients.Any(r => r.layers > (Core(2, 0) ? 2 : Core(2, 1) ? 4 : 3)) ||
                value.flowerInside.Length > 0 && (Job.index != 3 || Route != 2)) return false;
            if (value.effects.effects.Any(e => !ValidHealerEffect(e)) || value.effects.effects.Count(e => e.kind == "flower") > (Core(2, 0) ? 1 : 2)) return false;
            // Commit only after every candidate validates. Restoration never casts,
            // refunds, heals, advances a timer or installs a shield on a replica.
            _resources = resources; _garden = garden; _harvest = harvest; _conductor = requests; _song = song;
            _link = link; _surgery = surgery; _layers = layers; _effects.Restore(value.effects);
            _encore = value.encore; _safeReady = value.safeReady; _flowerMoveReady = value.flowerMoveReady;
            _gardenTick = value.gardenTick; _songTarget = value.songTarget;
            _flowerInside.Clear(); _flowerInside.UnionWith(value.flowerInside); return true;
        }
        private bool ValidHealerEffect(CharacterTimedEffect effect)
        {
            if (effect.value < 0 || effect.end < 0 || effect.next < 0 || effect.interval != 1) return false;
            if (effect.kind == "flower") return Job.index == 3 && Route == 2 && effect.target == 0 &&
                (effect.id == "flower.0" || effect.id == "flower.1") && (effect.radius == 7 || effect.radius == 10);
            if (effect.target == 0 || effect.id != effect.skill + "@" + effect.target) return false;
            if (effect.kind == "reduction" || effect.kind == "speed") return effect.value <= 1;
            return effect.kind == "heal" || effect.kind == "gate" ||
                effect.kind == "cooperation" && Job.index == 5 && Route == 2 ||
                effect.kind == "directHealMark" && Job.index == 5 && Route == 3;
        }
        public string StatusText()
        {
            Bind(); if (_resources == null) return "";
            string Patient(int id) { var target = CharacterKit.Resolve(id); return target != null ? target.name : "대상 없음"; }
            if (Job.index == 3 && Route == 1) return "묘목 " + _garden.Snapshot.Length + "/" + (Core(1, 0) ? 2 : 3) + " · " + string.Join(" / ", _garden.Snapshot.Select(s => Patient(s.target) + " " + s.stage + "/3 · " + s.nextGrowth.ToString("0.0") + "초"));
            if (Job.index == 3 && Route == 2) return "계절꽃 " + Flowers.Length + "/" + (Core(2, 0) ? 1 : 2) + " · " + string.Join(" / ", Flowers.Select(f => (f.end - Now).ToString("0.0") + "초")) + (Flowers.Length >= (Core(2, 0) ? 1 : 2) ? " · 다음 설치: 가장 오래된 꽃 교체" : "");
            if (Job.index == 3 && Route == 3) return _harvest.Active ? "수액 " + _harvest.Snapshot.growth + "/" + _harvest.Snapshot.maximum + " · 수확 " + _harvest.Snapshot.remaining.ToString("0.0") + "초" : "R: 수액 저장 시작";
            if (Job.index == 4 && Route == 1) return "협연 요청 " + _conductor.Snapshot.Count(r => !r.replied) + " · 화답 " + _conductor.Replies + " · " + string.Join(" / ", _conductor.Snapshot.Select(r => Patient(r.target) + (r.replied ? " 화답 " : " 응답 대기 ") + r.remaining.ToString("0.0") + "초"));
            if (Job.index == 4 && Route == 2) return _layers.Active ? "악절 " + _layers.Snapshot.remaining.ToString("0.0") + "초 · " + string.Join(" / ", _layers.Snapshot.recipients.Select(r => Patient(r.target) + " " + r.layers + "층")) + " · R: 남은 보호 정산" : "R: 수호 악절 준비";
            if (Job.index == 4 && Route == 3) return _song.Active ? "악절 " + (_song.Snapshot.completed + 1) + "/" + _song.Snapshot.maximum + " · " + _song.Snapshot.remaining.ToString("0.0") + "초 · 공명음 1 예약" : "R: 연주 시작";
            if (Job.index == 5 && Route == 1) return _link.Active ? "연결 " + Patient(_link.Snapshot.first) + (_link.Snapshot.branched ? " / " + Patient(_link.Snapshot.second) : "") + " · " + _link.Snapshot.remaining.ToString("0.0") + "초" : "R: 혈류 연결";
            if (Job.index == 5 && Route == 2) return "배합 " + string.Join(" + ", _resources.Snapshot.ingredients.Select(c => c == 1 ? "적색" : "청색")) + " · " + _resources.Snapshot.ingredients.Length + "/3";
            if (Job.index == 5 && Route == 3) return _surgery.Active ? "다음 처치 " + (_surgery.Snapshot.nextStep == 2 ? "봉합" : "예후 관리") + " · " + _surgery.Snapshot.remaining.ToString("0.0") + "초" : "R: 안정화";
            return "루트를 선택하세요";
        }
    }
}
