namespace RaidRogue
{
    public enum CampanellaCloakVoice { Belief, Passionate, Gentle, Analytical }
    public static class CampanellaCloakText
    {
        public static string Line(CampanellaCloakVoice voice) => voice switch
        {
            CampanellaCloakVoice.Passionate => "누가 종을 안 울리게 하려고 망토까지 찢어서 쑤셔 넣었네!",
            CampanellaCloakVoice.Gentle => "망토마다 피와 땀이 배어 있어요... 이 종이 울리면 안 되는 이유가 있었던 거예요.",
            CampanellaCloakVoice.Analytical => "찬탈자가 부순 게 아니라 원정대 측에서 봉인한 흔적이다.",
            _ => "자정을 알리는 종소리를 막기 위해 동료들이 자신의 망토로 틀어막아 두었군."
        };
    }
}
