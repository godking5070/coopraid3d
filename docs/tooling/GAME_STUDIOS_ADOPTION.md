# Claude Code Game Studios 선별 도입

설치일: 2026-10-29. 이 프로젝트의 기존 `AGENTS.md`가 작업 규칙의 기준이다.

## 출처와 범위

- 저장소: https://github.com/Donchitos/Claude-Code-Game-Studios
- 고정 버전: `05ad7c8a5b8aa2aa86bbd3439ae7cf7295cac418`
- 라이선스: MIT, 원문은 `.agents/skills/LICENSE.ccgs.txt`에 보존했다.
- `skill-installer`의 GitHub 설치 도구로 아래 세 경로만 가져온 후 Codex와 프로젝트 구조에 맞게 수정했다. 전체 저장소나 Unity 패키지를 설치한 것은 아니다.

| 설치 경로 | 원본 경로 | 적용 내용 |
| --- | --- | --- |
| `.agents/skills/perf-profile/SKILL.md` | `.claude/skills/perf-profile/SKILL.md` | 성능 병목 후보와 실제 측정 구분, 변신·AI·팝업 수명 점검 |
| `.agents/skills/code-review/SKILL.md` | `.claude/skills/code-review/SKILL.md` | 구체적 결함 근거, 호스트 권한·중복 실행·복제·Unity 수명 검토 |
| `.agents/skills/regression-suite/SKILL.md` | `.claude/skills/regression-suite/SKILL.md` | 기존 보스 테스트 선택, 시나리오 누락과 플레이 미검증 구분 |

원본 `SKILL.md` SHA-256 (프로젝트용 수정 전):

```text
perf-profile     6d9d1967d68f06b2d3f4f4f8b144ea38894612d3cf9885e50c459fcfa8dc6e8b
code-review      3468d605b4627e0568c80d0732d1ab1281652641f3dede555191657c40571b8c
regression-suite 65b91a43cfc319b8f369d31f78686549a84cbfaa32ba2c125b9b3abce7f29717
```

## 프로젝트에 맞춘 변경

- Claude 전용 모델 지정, 도구 제한 문법, 셸 명령 자동 삽입과 `.claude` 설정·문서 의존성을 제거했다.
- 자동 전문가 에이전트 호출과 매번 쓰기 전 승인을 요구하는 절차를 제거했다. 기존 사용자 지시와 권한 범위를 따른다.
- 의무 GDD·스프린트·문서 생성 대신 현재 코드, `docs/part1`, `Tests`를 기준으로 삼는다.
- 데이터 없는 성능 개선율·FPS 예산·커버리지 비율을 만들지 않는다. 코드 검사·컴파일·규칙 테스트와 실제 플레이 결과를 구분한다.
- Unity·Steam·게임 실행/포커스와 화면 자동 조작을 하지 않는다. 사용자 UI·Animation·Timeline·씬·프리팹과 `.meta`를 보존한다.
- `.claude/settings.json`, hooks, 49개 에이전트, 전체 스킬 모음, `CLAUDE.md`, `project.yaml`은 도입하지 않았다. 전역 Codex 설정과 Git hooks도 변경하지 않았다.

## 사용과 검증

다음 턴부터 프로젝트 스킬을 사용할 수 있다. 필요한 경우 `perf-profile`, `code-review`, `regression-suite`를 지정하거나, `AGENTS.md` 11항의 경로로 해당 스킬을 읽는다. 스킬은 검토 절차이며 게임 런타임에 포함되지 않는다.

프로젝트 루트에서 실행한다. `-List`는 실행할 기존 검사 목록만 출력한다.

```powershell
Tests/Run-StudioChecks.ps1 -Group BossHard -List
Tests/Run-StudioChecks.ps1 -Group BossHard
Tests/Run-StudioChecks.ps1 -Group Compile -OriginalProject C:/coopraid/coopraid3d
Tests/Run-StudioChecks.ps1 -Group Network -OriginalProject C:/coopraid/coopraid3d
Tests/Run-StudioChecks.ps1 -Group Steam -OriginalProject C:/coopraid/coopraid3d
Tests/Run-StudioChecks.ps1 -Group Progression
Tests/Run-StudioChecks.ps1 -Group All -List | Format-List
```

`BossHard`는 6종 하드 규칙 검사와 공통 광역기·체력 규칙 검사를 실행한다. `Compile`은 기존 전투 통계·AI 관전 피해 검사와 런타임/에디터 관리 코드 컴파일을 실행한다. `Network`는 기존 프로토콜·전투 네트워크·Transport 관련 검사를 실행한다. `All`은 모든 검사 그룹을 명시적으로 선택한다. 좁은 수정에는 해당 개별 검사만 실행해도 된다.

2026-10-30 추가한 `Progression`은 캠페인 저장/진행 검사와 소스 사전 검사의 임시 파일 회귀검사를 실행한다. `All`은 이 그룹도 포함한다. Unity 빌드나 플레이를 실행하지 않는다.

Steam 멀티 마무리 작업에서 추가한 `Steam`은 기존 `Run-SteamChecks.ps1`을 기본 인자 그대로 실행한다. Steam 정책, fake transport, 복귀/재접속 규칙, 런 흐름, 설정·직업 검사를 포함하며 고정된 공식 Steamworks.NET SDK를 관리 코드로 컴파일한다. 이어서 기존 Network 체인(Transport SDK·전투 프로토콜·파티·포션·대시·카메라·범위·전투 통계·runtime/editor 컴파일)과 캠페인 저장/진행 검사도 실행한다. Steam 클라이언트·Unity·게임·실제 네트워크 연결은 실행하지 않는다.

`All`의 최상위 실행 목록은 보스 검사 7개, Steam 체인 1개, Part 1 소스 사전 검사 회귀검사 1개다. Steam에 이미 포함된 `Compile`·`Network`·캠페인 저장 검사는 별도로 반복 호출하지 않는다. `Progression`의 나머지 Part 1 사전 검사는 따로 유지한다. 단독 `Compile`·`Network`·`Progression` 명령과 `Run-SteamChecks.ps1`의 기존 호출 방식은 유지한다. `-List`의 `Includes` 열에서 포함 관계를 볼 수 있으며, 긴 목록은 `Format-List`로 확인한다.

컴파일러 및 기존 원본 프로젝트 참조가 필요하다. 검사 결과물은 기존 스크립트가 임시 폴더에 생성한다. runner는 실패한 검사를 표시하고 다른 선택 검사를 마친 뒤 실패로 종료한다. 원본 프로젝트의 기존 Library는 참조만 하며 새로 생성하거나 복사하지 않는다.

플레이 확인은 사용자가 원본 Unity 프로젝트에서 수행한다. 변신 전후 AI 공격 재개, 연출 오브젝트 정리, 카메라 복원, 프레임 지연, 실제 멀티플레이 동기화는 명령 검사만으로 통과 판정하지 않는다.

### 설치 직후 확인 결과 (2026-10-29)

- 스킬 3개: `skill-creator/scripts/quick_validate.py` 형식 검사 통과.
- 실행기 `-Group All -List`: 기존 검사 스크립트 9개 목록 확인.
- `-Group BossHard`: 검사 스크립트 7개, 규칙 assertion 2,008개 통과.
- `-Group Compile`: 전투 통계 1,042개·관전 피해 20개 assertion 및 런타임/에디터 관리 코드 컴파일 통과. 기존 미할당 필드·미사용 필드 경고는 남아 있다.
- 별도 임시 폴더의 가짜 자식 검사에 종료 코드 7을 주어 실행기가 실패를 표시하고 비정상 종료하는지 확인했다. 실제 검사 파일은 바꾸지 않았다.
- 네트워크 그룹은 목록만 확인했으며 이번 설치 작업에서 실행하지 않았다. 실제 플레이·프레임 성능·멀티플레이 동기화는 미검증이다.

## 업데이트

원본을 다시 설치해 프로젝트용 스킬을 덮어쓰지 않는다. 위 고정 버전과 새 버전을 별도 임시 폴더에서 비교해 필요한 내용만 반영하고, 출처 버전·해시와 라이선스를 함께 갱신한다.
