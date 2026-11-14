namespace RaidRogue
{
    public enum JustitiaLawVoice { Belief, Passion, Gentle, Cold }
    public static class JustitiaLawText
    {
        public const string Article="제1조: 정지된 세계에서 새로운 에너지는 창조되지 않는다. 잠든 자의 심장을 뛰게 하려면, 깨어 있는 다른 자의 심장 박동을 그만큼 갉아먹어야 한다.";
        public static string Line(JustitiaLawVoice voice)
        {
            switch(voice)
            {
                case JustitiaLawVoice.Passion:return "잠든 녀석들을 깨우려면 다른 누군가의 심장을 갉아먹어야 한다고?!";
                case JustitiaLawVoice.Gentle:return "우리가 동료분들을 깨울 때마다, 어딘가에서 다른 누군가가 생명을 잃고 있었다는 뜻인가요?";
                case JustitiaLawVoice.Cold:return "제로섬 시스템이군. 성소의 관이 열리는 대가로 시계탑 내부의 누군가가 착취당하고 있다.";
                default:return "설마 우리가 회수한 불씨가 단순한 에너지가 아니었던 건가?";
            }
        }
    }
}
