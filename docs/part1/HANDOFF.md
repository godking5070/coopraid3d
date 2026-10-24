# 재개 정보

- 기준: `fd06247a12e0c414afa80f049d32fc5872a41c0c`
- 브랜치: `feature/part1-playable-build`
- 이번 재개 기준 커밋: `965f2f4` (원본 소스 반영 완료). 재개 당시 HANDOFF/WORK_LOG의 빌드 진행 기록만 미커밋이었으며 그대로 이어서 갱신했다.
- 작업 폴더: `C:/coopraid/coopraid3d-playable`
- 마지막 완료: 1~6단계 코드/자동 검사에 이어 7단계 원본 Unity Windows Development 빌드 성공. 최종 빌드 2026-10-24 21:37:28 KST. 훈련장 레이아웃과 종료 시 파괴된 보스 참조 오류를 수정하고 실제 exe 종료까지 재검증했다.
- 현재 작업: 빌드·메뉴/훈련장 스모크 검사 완료. 부분 작성된 코드는 없음. 다음 작업은 정규 싱글+AI Stage 1~6 완주와 저장/이어하기/재도전 실기이다. 게임 프로세스는 종료했으며 원본 Unity는 SampleScene으로 복귀한 상태다.
- 관련 파일: `Assets/Scripts/CharacterTrainingUI.cs`, `Assets/Scripts/StageManager.cs`, 기록 문서 4개. 빌드 도구는 `Tests/Build-Part1Windows.ps1`와 `Assets/Scripts/Editor/RaidMultiplayerBuild.cs`. 상세 실기 절차는 PLAYTEST.md.
- 코드 상태: 6스테이지 저장/공유 부활 유지, 사망 관전/결과 안내 및 전용 빌드 도구 추가. 원본 Builds/Part1Windows/RaidPart1.exe 생성 및 실제 훈련장 렌더링 확인. Stage 1~6 완주/저장/재접속 실기는 미확인. Stage 4~6 고유 콘텐츠 없음.
- 마지막 검증: 원본 Unity 재컴파일/실제 Windows 빌드 성공. 1920×1080 메뉴→훈련장, 목록 스크롤/탭/접기·펼치기/수치 입력(300→450), 메뉴 복귀/Alt+F4 종료 확인. 수정 전 종료 예외 1건, 최종 PlayerSmoke-final.log 예외 0건/정상 종료. 소스 사전 비교 차이 0, git diff --check 통과. 기존 147,728개/13개/21,565개 검사는 별도 과거 자동 검사 기록이며 이번 실기와 구분한다.
- 보존/정리 확인: 원본 사용자 코드는 최신 조상 cd86fa2에 포함됨을 확인하고 반영했다. 소스 반영 시 보호 대상 249개를 보존했으며 이후 Unity 빌드가 생성/갱신한 씬·재료·설정·meta는 원본에 미커밋으로 남겼다. 기존 GUID/씬 YAML을 직접 편집하지 않았고 원본 Git 인덱스/브랜치 유지. 백업 .git/codex-backups/part1-apply-20261024T113825Z 및 part1-build-20261024T121709Z 유지. worktree Library/Builds 없음.
- 다음 실행: 원본 `Builds/Part1Windows/RaidPart1.exe` 실행 → 새 게임/싱글+AI → 직업/기원 준비 → PLAYTEST.md 1~6의 정규 완주·공유 부활·저장·이어하기 확인. 다음 LAN 2개 실행 및 실제 Steam 계정 검사. 소스 변경 없이 빌드를 다시 만들 필요는 없다.
- 외부 조건: worktree Unity 금지. Steam App ID 0/실제 다중 계정 미확보, Stage 4~6은 보스 1~3 재사용. 원본은 feature/aegis-paladin-routes / 7c57e36에 미커밋 반영 상태이며 임의로 reset/checkout하지 않는다. 최신 증거는 원본 Builds/Part1Windows의 BUILD_INFO.txt, UnityBuild-final.log, BUILD_VERIFY-final.json, PlayerSmoke-final.log.

현재 판정: **코드 준비 완료, Unity 실행 확인 필요** — Windows 빌드와 메뉴/훈련장 실행은 확인했고, 정규 완주·저장·재도전·네트워크 실기가 남았다.
이어서 작업할 때 기존 기능을 다시 만들지 말고, 먼저 실기 실행 조건이 해결되었는지 확인한다. 병합/원격 푸시/Steam 배포는 수행하지 않는다.
