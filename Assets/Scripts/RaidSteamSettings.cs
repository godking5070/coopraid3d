using System;
using UnityEngine;

namespace RaidRogue
{
    [Serializable]
    public sealed class RaidSteamSettings
    {
        // Zero deliberately disables Steam. No sample App ID becomes a product setting.
        public uint appId;
        public string build = "steam-combat-1";
        public bool restartThroughSteam = true;
        public static RaidSteamSettings Load()
        {
            var asset = Resources.Load<TextAsset>("RaidSteamSettings");
            var settings = asset != null ? (JsonUtility.FromJson<RaidSteamSettings>(asset.text) ?? new RaidSteamSettings()) : new RaidSteamSettings();
            if (Application.isEditor || Debug.isDebugBuild)
                if (uint.TryParse(Environment.GetEnvironmentVariable("RAID_STEAM_APP_ID"), out uint devId) && devId != 0) settings.appId = devId;
            return settings;
        }
    }
}
