# 훈련소 제거와 폰트 조사 — 2026-11-16

별도 훈련소 모드와 진입 UI를 제거했다. 대기실 허수아비는 현재 빌드의 연습 기능으로 유지했다. [폰트 추천과 비교 이미지](PRISM_FONT_RECOMMENDATION_2026-11-16.md)는 조사 단계이며 게임 서체를 교체하지 않았다. 후속 요청의 짙은 아웃라인은 비교 시안과 적용 지침에 반영한다.

## 변경

- 훈련소 전용 Session/UI/Rules/Adapters 4개 스크립트와 대응 `.meta`, 전용 테스트·실행 등록을 제거했다. 먼저 삭제할 스크립트 GUID의 씬·프리팹 참조가 없음을 확인했다.
- 캐릭터·보스·전투 기록·진행·파티·미니맵의 훈련소 예외를 정리했다. 대기실 연습, 권한 검사, 저장 정책, 일반 전투의 공략 배너 비표시는 유지한다.
- `RaidSessionMode`는 삭제한 8번을 재사용하지 않고 `AiSpectator = 9`를 명시해 나머지 기존 값을 유지했다.
- 개인 저장의 `enteredRegularBattle`은 호환성을 위해 보존한다. 훈련소 해금 판정과 해당 플래그 신규 기록은 제거했다. 저장 버전과 클리어·챕터 해금 정책은 유지한다.
- 현재 painted HUD와 connection 프리팹을 `Assets/UI/AstellaPrismV4/WithoutTraining/Applied-20261116-192153-353/`로 복사했다. Unity API로 훈련소 카드 2개, 퇴역 참조 9개, 액션 2개를 제거하고 오래된 LAN 도움말 2개를 수정했다. profile과 SampleScene의 활성 HUD를 연결했다.
- 이전 authored 프리팹·비활성 씬 인스턴스는 보존했다. 원본 프리팹 2개의 SHA256은 적용 전후 동일하다. 기존 GUID 교체와 씬·프리팹 YAML 직접 편집은 하지 않았다.

명시적 편집 도구는 `Tools/Astella/Remove Retired Training Room (Preserved Copies)` / Ctrl+Shift+F11이다. Play 중 실행과 자동 실행 훅은 없다. 활성 profile·scene 검사는 `Tools/Astella/Validate Training Room Removal`로 실행한다. 이전 보존 복사본에는 당시 UI가 남을 수 있다.

## 검증

실행 명령은 `Tests/Run-StudioChecks.ps1 -Group UI -OriginalProject C:/Astella/Astella`, `Tests/Run-CombatMetricsChecks.ps1 -OriginalProject C:/Astella/Astella`, `Tests/Run-BossCastPresentationChecks.ps1`, `Tests/Run-CampaignSaveChecks.ps1` 및 기존 무력화 검사 묶음이다.

| 구분 | 실제 결과 |
| --- | --- |
| UI | 6개 스크립트 통과. authoring·Prism·hotbar·배치·대기실 연습·빌드 조율 포함 |
| 전투·컴파일 | 액션 실행 28개, 전투 규칙 4,047개, 기록 1,048개 및 중첩 검사 통과. 전체 런타임·에디터 C# 어셈블리 컴파일 통과 |
| 저장 | 캠페인 저장 41,863개 및 복구·새 런·피해 복원·정산 검사 통과 |
| 보스 표시·무력화 | 시전 이름 11개, 공유 어댑터 32개 및 보스별 93개·106개 통과 |
| Unity | 원본 프로젝트에서 import·컴파일·복사·바인딩 검사·씬 저장 성공. 재실행 후 관련 예외·C# 오류 없음 |
| 직접 Play | 격리 프로필 `training-removal-20261116`: 방 만들기 → 혼자 시작 → 팔라딘 → 기원 → 대기실. Q 방어 쿨타임, 허수아비 선택·거리 판정·1번 스킬 쿨타임 확인 |
| HUD 관찰 | 대기실 48개 샘플, 지속 불일치 0개. Q 쿨타임 일치 19개, 1번 쿨타임 일치 3개 |

씬 읽기 전용 감사에서는 기존 비내장 문서 1,165개 중 1,163개가 동일하고 이전 HUD 비활성화와 SceneRoots의 새 HUD 등록만 달라졌다. 새 prefab instance 1개를 추가했고 기존 문서는 삭제하지 않았다. 내장 Texture 8개·Sprite 9개는 ID가 재생성됐지만 내용은 동일하다. 게임플레이·월드·컷씬 문서 변경은 없었다.

## 증거와 범위

- [Unity 적용 결과·원본 해시](evidence/training-font-20261116/removal-report.json)
- [씬 보존 감사](evidence/training-font-20261116/scene-preservation-audit.json)
- [에디터 카드 제거 화면](evidence/training-font-20261116/editor-menu-after-removal.png)
- [Play 대기실 Q 쿨타임](evidence/training-font-20261116/play-lobby-cooldown.png)
- [UI 명령 결과](evidence/training-font-20261116/ui-check-results.json)
- [관찰한 명령 결과·산출물 지문](evidence/training-font-20261116/observed-command-results.json)
- [Play 관찰 요약](evidence/training-font-20261116/live-summary.json)

UI 원시 로그는 외부 `C:/Astella/training-removal-checks-20261116/ui-retired/`에 있다. 전체 컴파일·저장 stdout은 별도 원시 로그로 저장하지 않았다. observed 결과 문서는 실행 당시 출력과 산출물 지문을 기록한 요약이다.

첫 적용은 오래된 LAN 도움말에 훈련소 문구가 남아 검증 단계에서 중단·복구됐다. 정확한 기존 문구만 수정한 후 재실행해 통과했다. 실패 초안은 외부 증거 폴더에 보존했고 최종 적용 자산에는 포함하지 않았다.

이번 변경 후 Windows 플레이어를 새로 빌드하지 않았다. 실제 Steam/LAN 다중 접속, 전 직업·루트 및 6스테이지 완주, 성능·오디오는 이번 검증 범위 밖이다. 후보 폰트의 Unity import·화면별 배치·빌드 결과도 아직 검증하지 않았다.
