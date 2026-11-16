# Oxanium 숫자 적용 — 2026-11-16

사용자가 고른 대안 3, **Oxanium Medium**을 명시적으로 연결된 HUD 숫자 18곳에 적용했다. 마루부리 제목·보스명·시전명과 본문·단축키는 유지했다. 더 두꺼운 아웃라인은 [크기별 A/B/C 비교안](OXANIUM_OUTLINE_VARIANTS_2026-11-16.md)으로 준비했으며 아직 게임 효과를 변경하지 않았다.

## 적용

- 활성 HUD를 `Assets/UI/AstellaPrismV4/Typography/Oxanium-20261116-201718-685/RaidHUD.prefab`으로 복사하고 profile·SampleScene의 활성 참조를 갱신했다. view 프리팹은 변경하지 않았다.
- 플레이어 체력·오른쪽 주 자원·보스 체력 비율·시전 시간·궁극기 비율/쿨타임·스킬 쿨타임·파티 체력 비율에만 적용한다. 한글이 섞인 이름·설명·상태·단축키는 선택하지 않는다.
- 실제 정적 Medium 파일과 FontStyle.Normal을 사용했다. 숫자와 `.,/%+-s:—` 글리프를 확인했다. 원본 TTF와 대응 OFL 고지를 각각 Fonts와 StreamingAssets/ThirdPartyLicenses에 추가했다.
- 글자 크기·내용·색상·정렬·기존 Outline/Shadow와 모든 RectTransform을 보존했다. 다른 Text 740개의 직렬화 스냅샷도 동일하다. 이전 HUD 프리팹 SHA256은 전후 동일하다.
- 숫자의 현재 값과 대표 최대값을 TextGenerator로 검사했다. 체력 `99,999 / 99,999`, 자원 `100 / 100`, 비율 `100%`, 시전 `99.9s`, 쿨타임 `999`가 각 기존 텍스트 영역 안에 들어온다. 크기를 줄이는 자동 보정은 추가하지 않았다.

명시적 메뉴는 `Tools/Astella/Apply Oxanium Numerals (Preserved Copy)`와 `Tools/Astella/Validate Oxanium Numerals`다. import·Play 자동 실행 훅과 새 런타임 writer는 없다. 씬·프리팹 YAML을 직접 편집하거나 기존 GUID를 교체하지 않았다.

## 검증

| 검사 | 실제 결과 |
| --- | --- |
| Unity | 원본 프로젝트에서 TTF import·C# 컴파일·적용·바인딩·글리프·폭 검사 통과 |
| 보존 | 숫자 18개 외 Text 740개, 전체 배치·Outline·Shadow, 마루부리 제목 및 원본 프리팹 해시 보존 |
| 명령 | authoring 안전 검사 60개, 실제 Prism bridge 42개·hotbar 소유권 17개 통과 |
| 직접 Play | 격리 프로필에서 방 만들기 → 혼자 시작 → 팔라딘·기원 선택 → 대기실. 체력 `3,000`, 자원 `0 / 100`, 비율 `100%`, Q 입력 후 `30` 쿨타임 표시 확인 |
| 씬 감사 | 기존 비내장 문서 1,167개 중 1,165개 동일. 기존 HUD 비활성화와 새 root 등록만 변경, 새 instance 1개 추가. 기존 문서 삭제 없음. 내장 Texture 8개·Sprite 9개 내용 동일 |

새 HUD instance 설정은 GUID·이름을 제외하면 이전 instance와 동일하다. 게임플레이·월드·컷씬 문서 변경은 없다. 재컴파일 중 도메인 재로드 지연이 있었으나 완료됐고, 이번 로그에 C# 오류·관련 예외는 없었다.

## 증거와 범위

- [적용 보고서·필드별 폭·효과 보존](evidence/oxanium-outline-20261116/oxanium-application-report.json)
- [씬 읽기 전용 감사](evidence/oxanium-outline-20261116/scene-preservation-audit.json)
- [실제 Play Q 쿨타임과 숫자 표시](evidence/oxanium-outline-20261116/oxanium-play-q.png)
- [아웃라인 A/B/C 시안](evidence/oxanium-outline-20261116/oxanium-outline-scaled-presets-20261116.png)

크기별 아웃라인은 실제 폰트의 WPF 출력 비교이며 Unity effectDistance와 같은 수치가 아니다. A/B/C 적용·전 해상도·전체 직업과 보스·실제 다중 접속·새 Windows 빌드는 이번 검증 범위 밖이다.
