# 숫자만 아웃라인 강화 — 2026-11-16

사용자의 후속 지시에 따라 **Oxanium 숫자 18곳만 진한 남보라 아웃라인을 추가**했다. 이름·제목·시전명의 기존 얇은 효과와 마루부리 서체는 유지했다. 이전 A/B/C 시안의 두꺼운 제목 효과는 적용하지 않았다.

현재 HUD는 `Assets/UI/AstellaPrismV4/Typography/NumericOutline-20261116-203333-618/RaidHUD.prefab`이다. 기존 활성 프리팹을 복사하고 Unity API로 숫자 대상의 Outline만 추가했다. 원본 SHA256이 동일하고 다른 Text 740개, 비대상 Outline/Shadow, 숫자의 폰트·스타일·내용·크기·색·배치, 모든 RectTransform 보존 검사를 통과했다. 바인딩과 마루부리 제목 검사도 다시 통과했다.

숫자의 논리적 글자 크기에 7.5%를 곱해 `effectDistance=(d,-d)`로 작성한다. 색은 `#131022`, 기존 글자 알파를 따른다. Canvas와 부모 배율에 따라 글자와 선이 함께 커지도록 하며 런타임 writer는 추가하지 않는다.

| 논리적 fontSize | effectDistance의 축별 크기 |
| --- | ---: |
| 15 | 1.125 |
| 20 | 1.5 |
| 25 | 1.875 |
| 28 | 2.1 |
| 32 | 2.4 |

이는 이전 B 균형형의 크기 비례 방향을 구현한 값이다. uGUI의 대각선 글리프 복제 방식과 Canvas 배율 때문에 WPF 시안의 출력 픽셀 두께와 같은 수치는 아니다. 현재 숫자 대상에는 이전 Outline/Shadow가 없었고, 이름·제목의 기존 효과는 변경 대상에서 제외했다.

명시적 적용 메뉴는 `Tools/Astella/Apply Strong Numeric Outlines (Preserved Copy)`, 검사는 `Tools/Astella/Validate Numeric Outlines`다. 기존 숫자 폰트 적용 메뉴는 효과를 보존하는 동작을 유지한다. 자동 import·Play 적용 훅은 없다. 씬·프리팹 YAML 직접 편집과 기존 GUID 교체는 하지 않았다.

## 검사와 증거

- 원본 Unity에서 C# 컴파일·글리프·폭·참조·숫자 전용 효과 검사 통과. UI authoring 안전 60개, 실제 Prism bridge 42개·hotbar 소유권 17개 통과.
- 격리 프로필의 실제 Play에서 방 만들기 → 혼자 시작 → 팔라딘·기원 → 대기실 → Q 입력을 확인했다. 체력·자원·비율·쿨타임 숫자의 외곽선과 이름·제목의 얇은 표시를 직접 확인했다. 관련 C# 오류·예외는 없었다.
- 씬 읽기 전용 감사 통과: 기존 비내장 문서 1,168개 중 1,166개 동일. 기존 HUD 비활성화·새 root 등록, instance 1개 추가만 허용했다. 기존 문서 삭제 없음. 내장 Texture 8개·Sprite 9개 내용 동일. 게임플레이·월드·컷씬 문서 변경 없음.

[실제 Play 적용 전후](evidence/numeric-outline-only-20261116/numeric-only-before-after.png)는 각 전체 스크린샷의 동일 영역을 원본 픽셀 크기 그대로 잘라 배치했다. 글자나 게임 화면의 내용은 수정하지 않았다.

- [실제 Play 전체 화면](evidence/numeric-outline-only-20261116/numeric-outline-play-q.png)
- [적용 결과·숫자별 전후 효과·보존 검사](evidence/numeric-outline-only-20261116/numeric-outline-application-report.json)
- [씬 감사](evidence/numeric-outline-only-20261116/scene-preservation-audit.json)
- [적용 전 배율·기존 얇은 제목 효과 조사](evidence/numeric-outline-only-20261116/calibration-source.json)

새 Windows 빌드, 다른 해상도와 전체 직업·보스·실제 멀티플레이는 이번 확인 범위에 포함하지 않았다.
