# 개발 오류 보고 ZIP 감시

기존 개발 빌드의 **오류 보고 묶음 내보내기**가 만든 최종 `BugReport_*.zip`을 처리한다. 기본 입력 폴더는 `%LOCALAPPDATA%\RaidRogue\BugReports`다. 감시와 처리는 로컬 PC에서만 이루어지며 업로드, 게임 저장, Steam 호출, Unity 실행을 하지 않는다. ZIP이 생성되었다고 현재 채팅이 자동 재개되는 것은 아니다.

PowerShell에서 원본 `C:\Astella\Astella`로 이동한 뒤 다음 명령을 사용한다. 자동 감시는 선택 기능이며 재개 검사에서 감시기나 Codex 작업을 시작하지 않는다.

```powershell
python Tests/Watch-BugReports.py scan
python Tests/Watch-BugReports.py status
python Tests/Watch-BugReports.py watch --agent
```

`scan`은 기존 ZIP까지 한 번 조사한다. `watch --agent`는 **열어 둔 PowerShell에서만** 3초 간격으로 새 ZIP을 찾고, 검사가 끝난 보고서마다 설치된 `codex exec`를 순서대로 한 번 실행한다. 첫 시작에서 과거 ZIP을 제외하려면 `watch --agent --new-only`를 사용한다. 감시 폴더를 바꾸려면 모든 명령에 `--inbox "C:\원하는\폴더"`를 붙인다. 감시를 마칠 때는 다른 PowerShell에서 `python Tests/Watch-BugReports.py stop`을 실행한다. 실행 중인 Codex 작업이 끝나면 감시가 종료된다. 중단 뒤 같은 `watch` 명령을 다시 실행하면 누락 ZIP을 다시 찾는다. 동일 내용의 ZIP은 파일명이 달라도 SHA-256으로 한 건만 기록한다.

감시와 Codex 실행의 상태는 `.utmp/bug-report-queue/queue.json`, 분석 사본·이벤트·마지막 답변은 `.utmp/bug-report-queue/reports/<SHA256>/`에 남는다. 이 폴더는 Git에서 제외되며 테스트 저장 폴더와 분리된다. `status`로 **대기 / 분석 중 / 완료 / 추가 자료 필요 / 실행 환경 필요 / 처리 실패** 상태와 다음 행동을 확인한다. 중단된 Codex 작업은 안전하게 `추가 자료 필요`가 되므로 현재 diff와 결과 파일을 확인한 뒤 `python Tests/Watch-BugReports.py retry --report <ID 또는 SHA256>`로 명시적으로 재개한다. 손상 ZIP은 최대 세 번 검사하고 그 뒤 자동 재시도를 멈춘다. 에이전트 오류도 명시적 재시도만 허용한다. 실제 재현·수정·검증을 확인한 뒤 `python Tests/Watch-BugReports.py complete --report <ID> --note "확인한 빌드와 재검증 결과"`로 완료 기록을 남긴다.

감시기는 ZIP 이름·안정된 크기·CRC·허용된 파일명·압축 해제 크기·manifest 크기와 SHA-256을 확인한다. 상위 경로 이탈, 절대 경로, 링크, 중복 항목, 비정상 압축비를 거부한다. 보고서·로그·테스트 저장 사본만 추출하고 원본 ZIP이나 게임 저장을 바꾸지 않는다. 저장 JSON은 **분석 자료**이며 게임의 일반 프로필로 불러오지 않는다. 보고서 안의 명령도 실행하지 않는다.

`--agent`는 공식 `codex exec`를 `workspace-write` 범위로 실행한다. 승인 우회 옵션을 사용하지 않는다. 각 작업에는 20분 제한이 있고, 작업 공간에서는 보고서 한 건씩만 처리한다. Codex가 종료 코드 0을 반환해도 Unity 실제 플레이와 저장 복구가 확인된 것은 아니므로 대기열에는 `실행 환경 필요`로 남긴다. `codex-result.txt`, 변경 diff, 검사 결과를 확인해 별도로 완료를 판단한다. 코드 변경은 자동 병합·푸시·배포되지 않는다. 에이전트가 게임 재현을 해야 한다면 반드시 별도 개발 테스트 프로필을 사용해야 하며, 프로필·외부 기록 차단을 확인할 수 없으면 실행을 하지 않도록 지시한다. 감시기 자체는 게임을 실행하지 않아 일반 저장·영구 진행·Steam 업적을 갱신하지 않는다.

현재 채팅 세션만으로 PC가 꺼져 있거나 Codex/PowerShell이 닫힌 뒤 계속 감시할 수 없다. Windows 시작 작업이나 서비스는 설치하지 않았다. 사용자가 `watch --agent`를 실행하고 해당 PowerShell과 PC를 유지해야 한다. Codex 인증·권한·Unity 에디터 상태에 따라 수정이나 검증이 중단될 수 있으며, 이 경우 `status`와 보고서 폴더에 상태를 남긴다. 원본 Unity에서의 빌드·실제 플레이와 4인 Steam 확인은 자동 완료로 간주하지 않는다.
