namespace RaidRogue
{
    public enum JanusBarVoice { Belief,Passion,Gentle,Cold }
    public static class JanusBarText
    {
        public const string Description="시계탑 안쪽에서 원정대의 무기와 방패를 녹여 문틈을 용접한 흔적.";
        public static string Line(JanusBarVoice voice)
        {
            switch(voice)
            {
                case JanusBarVoice.Passion:return "이거 안에서 잠근 거잖아! 왜 우리가 들어오는 걸 막으려는 거지?";
                case JanusBarVoice.Gentle:return "문틈에 녹아붙은 금속들... 먼저 간 동료분들의 방패 조각이에요...";
                case JanusBarVoice.Cold:return "이 정문은 성채가 아니라 ‘격리벽’이다. 관리자는 우리 손으로 이 격리문을 부수게 만들고 있어.";
                default:return "선발대 동료들이 스스로를 저 안에 가두고 문을 용접해 막은 거란 말인가?";
            }
        }
    }
}
