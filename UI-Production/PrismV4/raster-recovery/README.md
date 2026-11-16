# Prism V4 페인팅 정정본

현재 PNG는 승인 시안 `02-continuous-fill.png`를 참조해 내장 `image_gen` 편집으로 새로 만든 투명 아틀라스에서 분리했습니다. 원래 PSD·SVG·레이어를 복구한 것이 아닙니다. 생성 요청의 정확한 문장은 `prompt.txt`에 보존합니다.

공통 원본은 `painted-transparent-atlas.png`입니다. 별도 스킬·역할 아이콘과 문자는 없으며, 키·이름·수치는 게임 UI가 표시합니다. 중앙 자수정은 HUD 장식으로 유지합니다.

`Extract-Frames.ps1`은 실제 페인팅 프레임·배경·수정·빈 배지를 기계적으로 분리합니다. 배지가 가린 짧은 상단 테두리는 이웃한 동일 페인팅 테두리로 연결했고, 수정에 가려진 배경은 깨끗한 옆면 픽셀을 연장합니다. `frames-extraction.json`에 마스크 좌표와 이 처리의 경계를 기록합니다.

`Extract-Gauges.ps1`은 같은 아틀라스의 게이지 프레임·전체 채움·트랙·보스 끝 보석을 분리하고, 기존 캔버스 및 채움 좌표에 맞춥니다. `gauge-raster-report.json`은 동일한 트랙·채움·마스크 알파와 양쪽 삼각 끝 검사를 기록합니다.

재현 순서는 다음과 같습니다. 프로젝트 루트에서 실행합니다.

```powershell
& UI-Production/PrismV4/raster-recovery/Extract-Frames.ps1
& UI-Production/PrismV4/raster-recovery/Extract-Gauges.ps1
node UI-Production/PrismV4/raster-recovery/update-painted-manifest.cjs
node UI-Production/PrismV4/finalize-manifest.cjs
node UI-Production/PrismV4/verify-pack.cjs
node UI-Production/PrismV4/raster-recovery/build-painted-previews.cjs
node UI-Production/PrismV4/build-review.cjs
```

미리보기 렌더러는 상위 `package-lock.json`의 `@resvg/resvg-js`를 사용합니다. 최초 환경에서는 `npm ci --prefix UI-Production/PrismV4`로 설치합니다. 실제 PNG 파일을 읽어 미리보기를 만들며 과거 SVG를 렌더링하지 않습니다.

`Assets/UI/AstellaPrismV4/Source/**/*.svg`는 초기 구조·좌표를 참고하기 위한 과거 벡터입니다. 페인팅 PNG를 재현하는 현재 소스가 아닙니다. `generate-prism.cjs`는 아틀라스가 있을 때 실행을 거부해 현재 그림을 보호합니다.

이전 벡터 PNG 백업 `previous-structural/`, `original-vector-gauges/`와 중복 원본 `painted-gauges-source.png`는 로컬에 보존하되 Git과 전달 패키지에서 제외합니다. Unity `.meta` GUID는 기존 것을 유지합니다.
