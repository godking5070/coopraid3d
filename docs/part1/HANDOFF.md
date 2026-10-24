# 재개 정보

- 기준: `fd06247a12e0c414afa80f049d32fc5872a41c0c`
- 브랜치: `feature/part1-playable-build`
- 작업 폴더: `C:/coopraid/coopraid3d-playable`
- 마지막 완료: 1~6단계 소스 확인/누락 보완 및 관련 자동 검사, 7단계 빌드 도구와 실기 절차. 최종 런타임/Editor/Player 조건 컴파일 완료.
- 현재 작업: 진행 중인 수정 없음. 검토 가능한 로컬 변경을 커밋하고 외부 실행 환경 대기.
- 관련 파일: 변경 파일별 상세는 WORK_LOG 마지막 항목. 핵심 QuarterViewCamera, ResurrectionSlotUI, IrisRescue, RaidMultiplayerBuild, SceneAutoBuilder, Tests/Build-Part1Windows.ps1.
- 코드 상태: 6스테이지 저장/공유 부활 유지, 사망 관전/결과 안내 및 전용 빌드 도구 추가. 컴파일 가능. exe 없음, 즉시 플레이 가능한 빌드를 제공한 상태가 아니다. Stage 4~6 고유 콘텐츠 없음.
- 마지막 검증: 147,728 assertions, 최종 런타임/Editor/Windows Player 조건 컴파일, diff 공백 검사 통과. 원본 309개 해시 차이 0. worktree Library/Builds 없음. 실제 Unity/Steam 플레이는 미확인.
- 다음 실행: 사용자 변경을 보존하여 원본에 검토된 최신 소스를 반영 → 원본 Unity 컴파일 → 열린 씬 저장 → `Raid Rogue/Build Part 1 Playable (Windows)` → 원본 `Builds/Part1Windows/RaidPart1.exe` 실행 → PLAYTEST.md 1~10 순서로 실제 결과 기록. 배치 방식은 원본 에디터를 종료한 뒤 Build-Part1Windows.ps1 -CheckOnly부터 실행.
- 외부 조건: 실제 빌드 사전 검사에서 원본 필수 소스 87개 차이/원본 에디터 실행 중 확인. 최신 소스 안전 반영 없이 빌드 불가. worktree Unity 금지. Steam App ID 0/실제 다중 계정 미확보. exe 생성 및 플레이 검증은 미실행.

현재 판정: **코드 준비 완료, Unity 실행 확인 필요**.
이어서 작업할 때 기존 기능을 다시 만들지 말고, 먼저 실기 실행 조건이 해결되었는지 확인한다. 병합/원격 푸시/Steam 배포는 수행하지 않는다.
