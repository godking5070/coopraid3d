namespace RaidRogue
{
    public enum FloraMemorialVoice { Belief, Passion, Gentle, Calculating }
    public static class FloraMemorialText
    {
        public static string Line(FloraMemorialVoice voice)
        {
            switch(voice)
            {
                case FloraMemorialVoice.Passion:return "성소에 있던 꽃이잖아! 먼저 간 네 명이 살아있는 게 아니라고?";
                case FloraMemorialVoice.Gentle:return "이 온실은 괴물의 소굴이 아니라, 누군가를 기리기 위해 만든 정원이었군요.";
                case FloraMemorialVoice.Calculating:return "선발대는 이미 사망했군. 관리자가 진실을 숨기고 있어.";
                default:return "선발대 네 명은 정찰 중인 게 아니라... 이미 추모의 대상이었던 건가?";
            }
        }
    }
}
