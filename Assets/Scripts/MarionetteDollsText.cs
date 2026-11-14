namespace RaidRogue
{
    public enum MarionetteDollsVoice { Belief, Passionate, Gentle, Analytical }
    public static class MarionetteDollsText
    {
        public const string Description="열두 기사 인형은 검은 태양의 창에 찔려 두 동강 나 있고, 성녀 인형은 등에 거대한 황금 초침이 꽂힌 채 피눈물을 흘린다.";
        public static string Line(MarionetteDollsVoice voice) => voice switch
        {
            MarionetteDollsVoice.Passionate => "기분 나쁜 인형들이네! 왜 우리랑 똑같은 인형들이 전부 꼬챙이에 꿰여 있는 거야?",
            MarionetteDollsVoice.Gentle => "성녀 인형의 등에 박힌 저 초침... 보기만 해도 가슴이 찌릿해요.",
            MarionetteDollsVoice.Analytical => "창작극이 아니야. 과거에 실제로 일어났던 ‘전멸의 기록’을 재현한 소품이다.",
            _ => "열두 기사와 성녀... 우리와 이리스 양을 본뜬 인형이 왜 이런 끔찍한 모습으로..."
        };
    }
}
