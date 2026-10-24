using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidRogue
{
    [Serializable]
    public sealed class CharacterTimedEffect
    {
        public string id = "", kind = "", skill = "";
        public int target, ticks;
        public float end, next, interval = 1, value, aux, x, y, z, yaw, radius;
        public CharacterTimedEffect Copy() => (CharacterTimedEffect)MemberwiseClone();
    }

    [Serializable]
    public sealed class CharacterKitSnapshot
    {
        public int version = 1, lastCombo, clones, notes;
        public string lastSkill = "", lastHeal = "";
        public int lastTarget;
        public float lastPower, returnEnd, returnX, returnY, returnZ, regen;
        public CharacterTimedEffect[] effects = Array.Empty<CharacterTimedEffect>();
        public CharacterKitSnapshot Copy()
        {
            var c = (CharacterKitSnapshot)MemberwiseClone();
            c.effects = effects.Select(e => e.Copy()).ToArray();
            return c;
        }
    }

    public sealed class CharacterEffectState
    {
        public CharacterKitSnapshot Data = new CharacterKitSnapshot();
        public CharacterTimedEffect Find(string id, float now) => Array.Find(Data.effects, e => e.id == id && e.end > now);
        public void Put(CharacterTimedEffect effect)
        {
            Data.effects = Data.effects.Where(e => e.id != effect.id).Concat(new[] { effect }).ToArray();
        }

        public void Remove(string id) => Data.effects = Data.effects.Where(e => e.id != id).ToArray();
        public void Clear() => Data = new CharacterKitSnapshot();
        public static int TakeTicks(CharacterTimedEffect effect, float now)
        {
            if (effect.interval <= 0 || effect.next > effect.end || now < effect.next)
                return 0;
            int count = (int)Math.Floor((Math.Min(now, effect.end) - effect.next) / effect.interval + .0001f) + 1;
            count = Math.Max(0, Math.Min(256, count));
            effect.next += count * effect.interval;
            effect.ticks += count;
            return count;
        }

        public bool Restore(CharacterKitSnapshot data)
        {
            if (data == null || data.version != 1 || data.effects == null || data.effects.Length > 256 || data.effects.Any(e => e == null || string.IsNullOrEmpty(e.id) || !CharacterDefinitionValidation.Finite(e.end) || !CharacterDefinitionValidation.Finite(e.next) || !CharacterDefinitionValidation.Finite(e.value) || !CharacterDefinitionValidation.Finite(e.aux) || e.interval <= 0 || !CharacterDefinitionValidation.Finite(e.interval)))
                return false;
            if (data.effects.Select(e => e.id).Distinct().Count() != data.effects.Length)
                return false;
            if (data.lastSkill == null || data.lastHeal == null || data.lastCombo < 0 || data.lastCombo > 2 || data.clones < 0 || data.clones > 3 || data.notes < 0 || data.notes > 63 || !Finite(data.lastPower, data.returnEnd, data.returnX, data.returnY, data.returnZ, data.regen) || data.lastPower < 0 || data.regen < 0)
                return false;
            if (data.effects.Any(e => e.id.Length > 256 || e.kind == null || e.skill == null || e.kind.Length > 64 || e.skill.Length > 256 || e.ticks < 0 || e.radius < 0 || !Finite(e.x, e.y, e.z, e.yaw, e.radius)))
                return false;
            Data = data.Copy();
            return true;
        }

        private static bool Finite(params float[] values) => values.All(CharacterDefinitionValidation.Finite);
        public static float ReducedDamage(float damage, IEnumerable<float> reductions)
        {
            foreach (float r in reductions)
                damage *= 1 - Math.Max(0, Math.Min(1, r));
            return Math.Max(0, damage);
        }

        public static float Lifesteal(float confirmed, float fraction, float missing) => Math.Max(0, Math.Min(missing, confirmed * Math.Max(0, fraction)));
        public static bool Alternating(int previous, int current) => previous != 0 && previous != current;
    }
}
