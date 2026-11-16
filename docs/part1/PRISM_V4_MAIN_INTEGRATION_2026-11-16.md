# Prism V4 메인 HUD 적용 — 2026-11-16

승인된 Prism V4를 실제 메인 HUD로 연결했다. 원본 Unity 프로젝트 `C:/Astella/Astella`, 브랜치 `ingame-move-system`, 구현·Windows 빌드 소스 커밋은 `6a943258089e3e3815941d58b0214af7589d1f42`다. Unity는 원본 폴더에서만 실행했다.

## 적용 자산과 보존

- 활성 HUD: `Assets/UI/AstellaPrismV4/AppliedMain/Applied-20261116-143208-179/RaidHUD.prefab`.
- 보존한 원본: `Assets/UI/AstellaOrnate/Applied-20261106-131209-797/RaidHUD.prefab`. 원본 prefab·meta·기존 Animation/Timeline 자산을 변경하지 않았다.
- `RaidUIPresentation.hudPrefab`와 `SampleScene`이 새 복사본을 사용한다. 씬의 기존 HUD 인스턴스와 사용자 override는 비활성 상태로 보존했다. 활성 HUD는 하나다.
- 씬·prefab은 Unity Editor API로 저장했다. YAML을 직접 편집하지 않았다. 새 PNG/SVG 리소스의 첫 Unity import가 90개 meta를 표준 직렬화했으며 기존 GUID 변경은 없다.
- 씬 보존 감사에서 기존 비내장 객체 1,162개의 기존 값·참조를 보존했다. 내장 Texture 8개·Sprite 9개는 참조 ID 정규화 후 내용이 동일하다. HUD 전환 외에는 기존 코드 기본값 10개가 새로 직렬화됐으며 보스 설정 코드는 변경하지 않았다. [감사 기록](evidence/prism-v4-20261116/scene-preservation.json).

## 연결과 소유권

`AstellaPrismHUDBridge`가 기존 presenter 뒤에서 값·텍스트·상태를 복사한다. 기존 `AstellaOrnateHUD`, 클래스 자원/궁극기 presenter, 파티 presenter와 버튼·타기팅·툴팁 연결을 유지했다. 이전 표시와 데이터 참조는 숨긴 `Legacy data sources` 아래에 보존하며 계산 컴포넌트는 계속 활성화한다.

오른쪽 주 자원은 `AstellaClassJobHUD`의 읽기 전용 스냅샷을 사용한다. `.ultimate` 제외, 드래곤 브레이커 루트, 직업별 표시 단위와 탄창 용량 정책은 기존 계산을 재사용한다. 중앙 프리즘은 별도 `AstellaClassUltimateHUD`와 `AstellaPlayerHUDV3`의 충전·사용 가능 상태를 읽는다.

원래의 스킬 버튼 8개와 V, 포션, 공유 부활을 재사용했고 전체 SlotBinding 배열 순서를 보존했다. 기존 스킬·직업 아이콘을 사용한다. 프레임 상태는 기존 `AstellaSkillSlotSkin`이 소유하며, 해당 스킨이 있을 때 절차적 금색 입력 테두리는 추가하지 않는다. 브리지는 프레임 팔레트나 콜백을 변경하지 않는다.

`AstellaPrismHUDLayout`이 새 표시의 편집 루트를 기존 ID에 연결한다. 사용자 배치는 `hud-layout-prism-v4.json`에 저장하며 이전 `hud-layout.json`을 사용하지 않는다. `AstellaPrismCanvasFit`은 명시적으로 연결된 전체 composition의 배율만 맞추고 자식 배치·폰트·색상은 변경하지 않는다. 파티와 겹치던 접속 바·적 목록 및 포션 하단 경계를 복사본에서 조정했다.

실제 쿨다운 검사에서 가시 이미지에 흰색 `cooldown-mask`를 사용한 문제를 발견했다. 최종 메인은 제공된 `cooldown-overlay`를 사용하며, 아이콘 위에 짙은 부채꼴과 숫자가 표시된다.

## 검증 결과

| 검사 | 수행 결과와 경계 |
| --- | --- |
| 패키지 기준 | RGBA Sprite 45개, gauge manifest 5개, 패키지 C# 5개 및 geometry 32개 검사 통과. 새 아이콘 제작 없음. |
| UI 명령 검사 | `Run-StudioChecks.ps1 -Group UI` 6개 스크립트 통과. UI 자동 재적용 방지 48개, 실제 브리지/자원 메서드 36개, 입력 피드백 소유권 17개, 기존 배치 818개와 Prism 매핑/파일 격리 50개 포함. |
| 원본 Unity | 실제 런타임·Editor 컴파일, 새 복사본 참조/소유권 검사 350개, 기존 UI 바인딩 fixture 15개 통과. 새 checkout에서도 보존한 `UI-Production/PrismV4/main-source-bindings.json`으로 검사 가능. |
| 실제 Canvas 렌더 | HP·직업 자원·보스 HP·시전·궁극기 × 0/1/50/60/100% = 25개. 실제 D3D12 GPU readback, 고정 frame/track/texture 크기, 0% mesh 없음, 100% 전체 UV 검사 통과. 유효 그림 픽셀 212,232개, 경고·실패 0개. |
| 직접 Play 관찰 | 9개 관찰 세션, 1,113개 렌더 시점 표본. 보이는 gauge와 원본 값의 최대 차이 0, 지속 불일치 0. 이 수치는 관측한 상태에 대한 결과이며 모든 직업/패턴의 완전 검증을 뜻하지 않는다. |
| 입력·키 재설정 | 기존 툴팁, 파티 HP 영역 클릭 선택, 마우스 Q 버튼과 키보드 Q로 30초 재사용 시간 발생 확인. 설정에서 Q→J 변경 후 슬롯/원본 키가 J로 갱신되고 J로 같은 재사용 시간이 발생했다. 이동기 재사용 시간도 관찰했다. |
| 직업·루트 | 실제 선택 UI로 팔라딘, 드래곤 브레이커, 건슬링어 및 루트 변경 관찰. 주 자원 ID/최대값 `aegis.dawnlight / 100`, 드래곤 탄창 `/ 4`, 건슬링어 `/ 6`을 궁극기와 별도로 확인했다. 관측한 주 자원 현재값·궁극기 충전은 0이므로 실전 충전 완주로 집계하지 않는다. |
| 파티·전투 | Stage 1 노멀 부분 전투에서 HP 감소·전투 불능·입력 잠금 확인. 5번 구조 요청으로 플레이어 HP 0→40%, 부활 보호 상태 활성화 확인. 실제 그로기 2표본에서 1.8s→0.7s 및 채움 값이 원본과 일치했다. 여러 보스 시전명·남은 시간도 확인했다. 해당 도전은 전멸했으며 완주 성공으로 집계하지 않는다. |
| 배치·크기 | Game 뷰 1918×879와 1245×877 관찰. 새 HP 바 드래그·저장 성공, Prism 전용 파일에 `hp` 위치만 기록됐다. 별도 Windows 실행에서 J 키와 이동한 HP 배치가 복원됐다. 실제 화면 설정 1280×1024→1366×768 변경·확인과 HUD 표시를 확인했다. |
| 작성 자산 보존 | 원본 Editor Play 진입/종료 전후 2,412개 빌드 입력의 정규화 SHA256 동일을 두 차례 확인했다. UI 설정·키·배치·캠페인 시험은 격리 프로필 `prism-v4-editor-20261116`에서 수행했다. |

처음의 PreviewScene 렌더 도구는 빈 이미지도 mesh만으로 통과시키는 결함이 있었다. 이 결과는 성공에 포함하지 않았다. 최종 V3 도구는 임시 Additive Scene, Game 카메라와 전용 레이어를 사용하며 실제 픽셀 변화까지 요구한다. 이전 실패 증거는 외부 `blank-render-false-positive/`에 보존했다.

[25개 실제 채움 이미지](evidence/prism-v4-20261116/fill-validation.png) · [최종 Editor 쿨다운](evidence/prism-v4-20261116/editor-cooldown.png) · [Windows 복원 배치](evidence/prism-v4-20261116/windows-restored-layout.png) · [관찰 통계](evidence/prism-v4-20261116/live-summary.json).

Windows 이미지는 시험용 J 재설정과 사용자가 이동한 HP 위치가 복원된 상태다. 배포 prefab의 기본 HP/주 자원 정렬을 변경한 것은 아니다.

## Windows 빌드

`Tests/Build-Part1Windows.ps1 -OriginalProject C:/Astella/Astella` 성공. Unity 6000.3.26f1 Development 빌드이며 실행 파일은 `Builds/AstellaWindows/Astella.exe`다.

입력 2,412개 SHA256:

```text
ebb4944e29b472756a9d7eee2e08497526edb0c3aa18601e3efa442f0c14ffb4
```

빌드 전후 지문이 동일하다. [빌드 영수증](evidence/prism-v4-20261116/build-receipt.json)의 코드 커밋은 `6a94325`다. 후속 검증 문서·이미지만 추가한 커밋은 이 빌드 입력을 바꾸지 않는다. 실행 파일에서 기존 격리 저장 이어하기, 설정/배치 복원, 재설정한 J 입력과 짙은 쿨다운, 화면 변경 후 표시를 확인했고 종료 확인창을 거쳐 프로세스가 종료됐다.

## 확인하지 않은 범위

- 12직업의 모든 기원·특성·스킬 조합, 실전 궁극기 100% 준비/사용, 실제 보호막량 발생은 이번 Play에서 확인하지 않았다. 해당 일부 상태의 명령 검사와 실전 검증을 구분한다.
- 새 HUD를 사용한 1~6 전체 완주, 하드/극한 전체 패턴과 실제 다인 동기화·Steam 접속은 확인하지 않았다.
- FPS/GPU 시간/할당·누수는 측정하지 않았다. 이 PC의 FMOD 오디오 장치 초기화가 실패해 청취 검증도 수행하지 않았다.

## 원본 Unity에서 검토·재검사

1. 원본 프로젝트에서 `SampleScene`을 열어 Play한다. 기존 비활성 HUD를 삭제하거나 동시에 활성화하지 않는다.
2. 새 prefab의 `Prism V4` 아래에서 배치·색상·폰트·이미지를 검토한다. 프레임 상태 sprite는 각 원래 버튼의 `AstellaSkillSlotSkin`에서 편집한다. 숨긴 legacy 데이터 참조와 presenter를 끄지 않는다.
3. Edit Mode의 `Tools/Astella/Prism V4/Run Integration Checks`로 참조·씬·바인딩·실제 채움을 확인한다. GPU 검사이므로 `-nographics`로 실행하지 않는다. Play의 Ctrl+Shift+F8은 180초 읽기 전용 관찰을 시작한다.
4. 기존 디자인으로 돌아가려면 보존된 `Astella_CelestialHUD` 인스턴스를 활성화하고 Prism 인스턴스를 끈 뒤, `RaidUIPresentation.hudPrefab`도 원본 prefab으로 연결한다. Unity API/Inspector를 사용하며 YAML이나 GUID를 직접 변경하지 않는다.

원시 로그·스크린샷·시험 프로필 관련 증거는 `C:/Astella/prism-v4-main-evidence-20261116/`에 있다. 원격 반영은 현재 브랜치의 일반 push로 수행하며 자동 병합이나 강제 push를 사용하지 않는다.
