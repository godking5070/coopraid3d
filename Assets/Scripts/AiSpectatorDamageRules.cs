using System;
using System.Globalization;

namespace RaidRogue
{
    // Pure boundary rules shared by the developer UI and the authoritative HP sinks.
    public static class AiSpectatorDamageRules
    {
        public const int DefaultPercent = 100;
        public const int MaximumPercent = 500;
        public static int Clamp(int percent) => Math.Max(0, Math.Min(MaximumPercent, percent));
        public static int Step(int percent, int percentagePoints) => Clamp(percent + percentagePoints);
        public static float Scale(float damage, int percent) => damage * Clamp(percent) / 100f;
        public static float Resolve(float damage,int percent,bool spectatorSession,bool aiSource,bool alreadyScaled=false) =>
            spectatorSession && aiSource && !alreadyScaled ? Scale(damage,percent) : damage;
        public static float BaseFromConfirmed(float confirmed, int percent) => percent > 0 ? confirmed * 100f / percent : 0f;
        public static bool TryParse(string text, out int percent)
        {
            percent = DefaultPercent;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < 0 || value > MaximumPercent)
                return false;
            percent = value;
            return true;
        }
    }
}
