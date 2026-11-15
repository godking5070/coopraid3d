using System;
using RaidRogue;

static class BossCastPresentationTests
{
    static int checks;
    static void Equal(string actual,string expected)
    {checks++;if(actual!=expected)throw new Exception("Expected '"+expected+"', got '"+actual+"'");}
    static void Main()
    {
        Equal(BossCastPresentation.Name(null,false),"");
        Equal(BossCastPresentation.Name("",false),"");
        Equal(BossCastPresentation.Name("침입자 징벌",false),"침입자 징벌");
        Equal(BossCastPresentation.Name("뒤틀린 집행 · 왼쪽 → 오른쪽 → 중심 → 외곽",false),"뒤틀린 집행");
        Equal(BossCastPresentation.Name("광장 내려찍기 · 중심 → 외곽",false),"광장 내려찍기");
        Equal(BossCastPresentation.Name("돌봉 직선 파쇄 / 옆으로!",false),"돌봉 직선 파쇄");
        Equal(BossCastPresentation.Name("황금 실의 횡단 I → II",false),"황금 실의 횡단 I");
        Equal(BossCastPresentation.Name("봉쇄 명령\n공략 설명",false),"봉쇄 명령");
        Equal(BossCastPresentation.Name("봉쇄 명령\r\n공략 설명",false),"봉쇄 명령");
        Equal(BossCastPresentation.Name("성문 쇄도 · 카운터 가능!",true),"성문 쇄도 · 카운터 가능!");
        Equal(BossCastPresentation.Name("수호핵 과부하",true),"수호핵 과부하");
        Console.WriteLine("PASS: "+checks+" boss cast presentation checks.");
    }
}
