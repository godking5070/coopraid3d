using System;
using System.Collections.Generic;
using System.Globalization;

namespace RaidRogue
{
    public static class RaidSteamPolicy
    {
        public const string Game = "astella";
        public const string CurrentBuild = "steam-combat-2";
        public static bool ShouldLeaveLateLobby(ulong completed, ulong current, ulong attempt) => completed != 0 && completed != current && completed != attempt;
        public static bool TryId(string text, out ulong id) => ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id != 0;
        public static ulong LaunchLobby(string[] arguments)
        {
            for (int i = 0; i + 1 < arguments.Length; i++)
                if (arguments[i] == "+connect_lobby" && TryId(arguments[i + 1].Trim('"'), out ulong id)) return id;
            return 0;
        }
        public static string Validate(IDictionary<string, string> data, string build, int protocol, ulong owner)
        {
            string identityError = ValidateHost(data, build, protocol, owner);
            if (identityError != null) return identityError;
            string Read(string key) => data.TryGetValue(key, out var value) ? value : "";
            if (Read("phase") != "lobby") return "이미 시작한 런에는 참가할 수 없습니다. 새 방에서 초대해 주세요.";
            if (Read("ready") != "1") return "호스트가 연결을 준비하고 있습니다.";
            return null;
        }
        public static string ValidateRecovery(IDictionary<string, string> data, string build, int protocol, ulong owner)
        {
            string identityError = ValidateHost(data, build, protocol, owner);
            if (identityError != null) return identityError;
            return data.TryGetValue("phase", out var phase) && phase == "closed"
                ? "원래 호스트가 종료했습니다. 호스트 이전은 지원하지 않습니다." : null;
        }
        private static string ValidateHost(IDictionary<string, string> data, string build, int protocol, ulong owner)
        {
            string Read(string key) => data.TryGetValue(key, out var value) ? value : "";
            if (Read("game") != Game) return "다른 게임의 로비이거나 만료된 초대입니다.";
            if (Read("build") != build || Read("protocol") != protocol.ToString(CultureInfo.InvariantCulture)) return "게임 빌드가 다릅니다. 같은 버전으로 업데이트하세요.";
            if (!TryId(Read("host"), out ulong host) || host != owner) return "원래 호스트가 종료한 방입니다. 호스트 이전은 지원하지 않습니다.";
            return null;
        }
    }
    public sealed class RaidInviteGate
    {
        public ulong Pending { get; private set; }
        public ulong Attempt { get; private set; }
        public bool NeedsConfirmation { get; private set; }
        public bool Request(ulong lobby, ulong current, bool occupied)
        {
            if (lobby == 0 || lobby == current || lobby == Pending || lobby == Attempt) return false;
            Pending = lobby; NeedsConfirmation = occupied; return true;
        }
        public void Confirm() => NeedsConfirmation = false;
        public void Decline() { Pending = 0; NeedsConfirmation = false; }
        public ulong Begin() { if (NeedsConfirmation || Attempt != 0) return 0; Attempt = Pending; Pending = 0; return Attempt; }
        public void Finish() => Attempt = 0;
    }
}
