# 2026-11-20 검증 기록

## 수행 완료

- 캐릭터 컨셉 브랜치 `9929b6d61218864098b9bf05c4f25114e024039e`의 13개 atlas와 반입한 PNG의 SHA256 일치.
- 프레임 규격 128×144, 전체 시트 512×288, 8방향·발 피벗 확인. 원본에 걷기·공격 전용 프레임은 없음.
- 새 배경과 투명 보스 이미지 육안 확인, 제작 기록의 SHA256 일치, 보스 PNG 실제 알파 채널 확인.
- 원·도넛·캡슐·부채꼴 교차와 타원 제한의 순수 C# 검사 84개 통과.
- 현재 런타임 C# 237개와 에디터 C# 55개 managed 컴파일 성공. 신규 2D 코드 포함, 오류 0개. 기존 코드의 미할당/미사용 필드 경고만 발생.
- 입력·사거리·쿨타임·피해·장판 종료·승패·재시작 경로 코드 검토. AI의 도넛/직선 회피를 실제 안전 후보점 검사로 수정하고, 일회성 Mesh 해제 및 중복 객체의 공유 Sprite 캐시 해제를 수정.
- 실행용 PowerShell 구문 검사, 새 파일 metadata의 GUID 충돌·누락 검사, 기존 metadata 보존 확인.
- 기존 3D 씬·프리팹·ProjectSettings·Packages의 tracked 변경 없음. 기존 미추적 `modeling/`은 별도 작업으로 보존.

## 실제 Unity 실행 시도와 차단

원본 `C:/Astella/Astella`의 Unity 6000.3.26f1을 graphics 활성 batchmode로 실행해 `Astella.TwoD.Editor.Astella2DEditor.RunChecks`를 호출했다. 에디터가 `No valid Unity Editor license`로 반환 코드 198을 내고 종료했다. 이 실패는 스크립트 컴파일·씬 생성·Play에 진입하기 전에 발생했다.

설치된 Unity Hub 4.0.1은 Windows MSIX 앱이다. Hub를 열었으며, 실제 실행을 이어가려면 사용자 계정의 Unity 라이선스 활성화가 필요하다. 계정·토큰·라이선스 파일은 수정하지 않았다. Unity 공식 절차: https://docs.unity.com/en-us/hub/manage-license

로그와 managed 검사 출력은 이 PC의 `C:/Astella/2d-evidence-20261120/`에 있다. 이 디렉터리는 저장소 자산이나 다른 PC의 실행 의존성이 아니다.

## 아직 검증되지 않은 항목

- Unity import 및 새 씬 생성, 실제 Play의 입력·이동·대시·공격·피해·승패·재시작.
- 실제 화면 렌더와 해상도 변경, 13종 전체 표시·파티 행동·최종 승리와 축복 선택.
- Windows 실행 파일 빌드와 실제 실행.
- Steam/LAN 멀티플레이와 기존 3D의 직업별 전체 스킬·보스·캠페인은 현재 2D 구현에 포함되지 않음.

라이선스 활성화 후 README의 명령 또는 `Astella > 2D > Play Game`으로 원본 프로젝트에서 계속 검증한다. 실제 Play 검증을 완료하기 전까지 managed 검사 통과를 게임 실행 성공으로 해석하지 않는다.
