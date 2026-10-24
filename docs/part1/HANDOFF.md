# 재개 정보

- 기준: `fd06247a12e0c414afa80f049d32fc5872a41c0c`
- 브랜치: `feature/part1-playable-build`
- 재개 기준 커밋: `c6fbc5b78292e1817d856c50966cf41a9cbeb98c` (이전 작업은 커밋 완료, 재개 당시 작업 폴더 깨끗함)
- 작업 폴더: `C:/coopraid/coopraid3d-playable`
- 마지막 완료: 1~6단계 소스 확인/누락 보완 및 관련 자동 검사, 7단계 빌드 도구와 실기 절차. 최종 런타임/Editor/Player 조건 컴파일 완료.
- 현재 작업: 빌드 사전 검사의 줄바꿈 오탐 수정 및 관련 검사 13개 완료. 원본에 최신 소스가 반영되기 전까지 실제 빌드/플레이 대기. 독립적으로 가능한 후속 구현 없음.
- 관련 파일: `Tests/Build-Part1Windows.ps1`, `Tests/Part1BuildPreflight.ps1`, `Tests/Run-Part1BuildChecks.ps1`. 원본 통합 검토 파일과 실기 절차는 PLAYTEST.md.
- 코드 상태: 6스테이지 저장/공유 부활 유지, 사망 관전/결과 안내 및 전용 빌드 도구 추가. 컴파일 가능. exe 없음, 즉시 플레이 가능한 빌드를 제공한 상태가 아니다. Stage 4~6 고유 콘텐츠 없음.
- 마지막 검증: 이번 재개에서 실제 임시 파일 I/O를 사용하는 빌드 사전 검사 13개 통과. 수정한 -CheckOnly 실제 실행은 누락 24개/내용 차이 53개/추가 컴파일 입력 0개 및 원본 에디터 실행 중으로 차단. 이전 147,728 assertions와 런타임/Editor/Windows Player 조건 컴파일 기록은 유지하며 변경 없는 게임 코드는 재검사하지 않음. 실제 Unity/Steam 플레이는 미확인.
- 보존/정리 확인: 원본 기준 309개 파일 SHA256 차이 0, 원본 브랜치 및 사용자 변경 유지. 이번 PowerShell 3개 구문과 git diff --check 통과. worktree Library/Builds 및 원본 RaidPart1.exe 없음.
- 다음 실행: 사용자 변경을 보존하여 원본에 검토된 최신 소스를 반영 → 원본 Unity 컴파일 → 열린 씬 저장 → `Raid Rogue/Build Part 1 Playable (Windows)` → 원본 `Builds/Part1Windows/RaidPart1.exe` 실행 → PLAYTEST.md 1~10 순서로 실제 결과 기록. 배치 방식은 원본 에디터를 종료한 뒤 Build-Part1Windows.ps1 -CheckOnly부터 실행.
- 외부 조건: 원본 필수 소스 77개 차이, 이 중 원본 미커밋 변경과 겹치는 파일 5개. 원본 브랜치 전환/병합/사용자 변경 덮어쓰기 금지 조건을 유지하여 반영하지 않음. 원본 에디터가 열려 있으므로 배치 실행도 차단(최신 소스 반영 후 원본 빌드 메뉴 사용 가능). worktree Unity 금지. Steam App ID 0/실제 다중 계정 미확보. exe 생성 및 플레이 검증은 미실행.

현재 판정: **코드 준비 완료, Unity 실행 확인 필요**.
이어서 작업할 때 기존 기능을 다시 만들지 말고, 먼저 실기 실행 조건이 해결되었는지 확인한다. 병합/원격 푸시/Steam 배포는 수행하지 않는다.
