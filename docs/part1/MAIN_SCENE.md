# 아스텔라 메인 씬

게임 작업과 플레이 테스트, Windows 빌드에는 `Assets/Scenes/SampleScene.unity` 하나를 사용한다. 실제 프로젝트 경로는 `C:\Astella\Astella`다.

| 용도 | 사용하는 씬 |
| --- | --- |
| 게임 작업과 Unity Play | `Assets/Scenes/SampleScene.unity` |
| 기본 빌드 목록 | `Assets/Scenes/SampleScene.unity` |
| Astella Build Part 1 Playable | `Assets/Scenes/SampleScene.unity` |
| LAN 테스트 빌드 | `Assets/Scenes/SampleScene.unity` |
| Steam 테스트 빌드 | `Assets/Scenes/SampleScene.unity` |
| 애니메이션과 컷씬 편집 전용 | `Assets/Cutscenes/Examples/CutscenePreview.unity` |

빌드 경로의 기준 상수는 `Assets/Scripts/Editor/RaidMultiplayerBuild.cs`의 `MainScene`이다. LAN·Steam 테스트 빌드는 메인 씬을 열고 저장한 상태에서 실행한다. 메인 씬이 없거나 검증에 실패하면 빌드를 중단하며 씬을 자동 재생성하지 않는다.

기존 `Step1_TestScene`부터 `Step9_TestScene`까지의 중복 씬, 별도 `Part1_Playable` 씬과 `_Recovery`의 복구 씬 2개는 `.meta`와 함께 Assets에서 프로젝트 밖으로 이동해 보관했다. 보관 위치는 `C:\Astella\SceneBackups\main-scene-20261106-115810`이며 `manifest.json`에 원래 경로와 SHA256이 있다. 씬 생성 코드는 단계별 복사본을 다시 만들지 않는다. 메인 씬과 컷씬 미리보기 씬의 내용 및 GUID는 유지했다.

이 정리는 씬 파일과 빌드 진입 경로를 통일하는 작업이며, 노말·하드 패턴이나 1부 6스테이지 구성을 변경하지 않는다.
