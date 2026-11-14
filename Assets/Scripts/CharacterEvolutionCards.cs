using System;
using System.Linq;
using UnityEngine;

namespace RaidRogue
{
    public static class CharacterEvolutionCards
    {
        public const int First = 7, Count = 31;
        public static readonly string[] Names =
        {
            "가벼운 성광",
            "움직이지 않는 성채",
            "초과 수혈 갑주",
            "광전사의 저주",
            "반동 흡수 개머리판",
            "용광로 폭주",
            "나무껍질 로브",
            "생명력 분여",
            "확성 공명",
            "근접 콘서트",
            "비상용 혈액팩",
            "야전 외과의",
            "그림자 유영",
            "외줄타기 암살",
            "초음속 강하",
            "대기권 돌파",
            "전술 슬링벨트",
            "양각대 거치",
            "신록의 대사 촉진 & 단궁술",
            "고독한 금욕주의 저격수",
            "에테르 유영",
            "우주의 중심",
            "원격 대낫 투영",
            "경계의 무희"
        };
        public static readonly string[] Descriptions =
        {
            "보호막 중 이동 속도 페널티 제거 · 기준 이동 +15%",
            "이동 추가 -5%p · 정지 중 피해 -25% / 넉백 면역",
            "만피 흡혈 초과량을 최대 HP 30% 보호막으로 축적",
            "방어 추가 -20% · 잃은 HP 비례 공격/흡혈 2배",
            "포격 반동 삭제 · 과열 1초",
            "과열 5초 · 과열 중 GCD 1 속도/피해 +80%",
            "아군 치유량 20%의 자기 보호막",
            "최대 HP -15% · 치유/공격 +40%",
            "음파 반경 35m · 만료된 잔여 보호막만큼 치유",
            "음파 반경 10m · 보호막/공격력 버프 +50%",
            "공격 사거리 15m · 대상 없는 공격 자원/치유 50%",
            "공격 사거리 6m · 공속/혈류 치유 +50%",
            "대쉬 후 4초 모든 공격 후방 성공",
            "정면 피해 추가 -50% · 후방 치명 피해 +60%",
            "점프 체공 반감 · 체공 중 바닥 광역 회피",
            "점프 체공 +0.3초 · 착지 피해 +70%",
            "장총 조준 중 이동 가능",
            "장총 조준 중 방향 고정 · 사거리/치명 피해 +50%",
            "근접 피해/치유/보호막 페널티 제거 · 포션 후 이동 +20%",
            "받는 치유/보호막 -20% · 근접 페널티 6m · 12m 이상 HP80% 이상 치명 피해 +50%",
            "워프 재사용 8초 · 걸으며 시전 유지",
            "시전 +0.5초 · 시전 중 대쉬 불가/피해 -40% · 마법 최종 피해 +60%",
            "근접 콤보를 원거리 거울상으로 실행",
            "원근거리 3타를 직접 번갈아 맞히면 8초 피해 +25% (2중첩)"
        };
        public static int JobIndex(string id) => id == AegisPaladinData.JobId ? 0 : CharacterRosterData.Find(id)?.index ?? -1;
        public static bool Eligible(int id, string job) => id < First || id < Count && (id - First) / 2 == JobIndex(job);
        public static bool Has(CharacterActor actor, bool maximize) => actor != null && actor.HasBuildEffect(maximize ? "evolution.maximize" : "evolution.overcome");
        public static bool StationaryBulwark(Transform unit)
        {
            var actor = unit?.GetComponent<CharacterActor>();
            var pc = unit?.GetComponent<PlayerController>();
            return actor?.Definition?.id == AegisPaladinData.JobId && Has(actor, true) && pc != null && !pc.IsMovingXZ && !pc.IsDashing && !pc.IsBeingKnockedBack && !pc.IsBeingRescued;
        }

        public static RoguelikeCardData[] Build() => Enumerable.Range(First, Count - First).Select(i => new RoguelikeCardData { id = (RoguelikeCardId)i, title = Names[i - First], description = Descriptions[i - First], categoryTag = (i - First) % 2 == 0 ? "직업 진화 · 극복" : "직업 진화 · 극대화", accentColor = (i - First) % 2 == 0 ? new Color(.25f, .8f, .65f) : new Color(.9f, .5f, .25f) }).ToArray();
    }

    public partial class RoguelikeCardManager
    {
        private bool EligibleRosterCard(RoguelikeCardData card)
        {
            var actor = player?.GetComponent<CharacterActor>();
            int id = (int)card.id;
            if (!CharacterEvolutionCards.Eligible(id, actor?.Definition?.id))
                return false;
            return id < 7 || !CharacterEvolutionCards.Has(actor, (id - 7) % 2 == 0);
        }

        public int[] CaptureCardStacks()
        {
            var result = new int[CharacterEvolutionCards.Count];
            foreach (var entry in _acquiredCardStacks)
                if ((int)entry.Key >= 0 && (int)entry.Key < result.Length)
                    result[(int)entry.Key] = entry.Value;
            return result;
        }
    }
}
