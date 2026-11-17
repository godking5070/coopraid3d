# 이미지 표시 파일 갱신

`INDEX.html`은 폴더 안의 표시용 JPEG를 사용합니다. 큰 PNG를 카드마다 직접 로드하지 않으며, 확대에는 원래 해상도의 JPEG를 사용합니다. **원본 PNG 열기**는 그대로 PNG를 엽니다. PNG 원화는 편집하거나 압축하지 않습니다.

- `thumbnails/<id>.jpg`: 카드용 640px JPEG.
- `previews/<id>.jpg`: 확대용 원본 해상도 JPEG.
- `previews/manifest.json`: 원본 및 JPEG의 해시·크기·품질.
- `originals/`: 이전부터 모음에 보관한 PNG.
- `originals/current/`: 다른 컨셉 폴더에 있던 최신 PNG의 동일 바이트 복사본. 이미지 표시와 원본 링크가 모음 바깥 경로에 의존하지 않게 합니다.

원화가 바뀌면 `catalog.json` 해당 이미지의 `src`, `sha256`, 크기·출처를 먼저 최신 상태로 맞추고 실행합니다.

```powershell
node ConceptArt/BossOverview/refresh-display-assets.cjs
```

이 도구는 Node, `playwright-core`, Chrome이 필요합니다. 현재 작업 환경의 기존 Playwright 설치를 자동으로 찾습니다. 다른 Chrome 경로는 `CHROME_PATH`로 지정할 수 있습니다. 갱신하면 JPEG·해시 기록·카탈로그·HTML의 썸네일을 함께 맞춥니다. 입력 이미지의 해시가 카탈로그와 다르면 중단합니다.

`thumbnailSrc`와 `displaySrc`는 `previewSourceSha256`이 현재 원화의 `sha256`과 같을 때만 사용합니다. 원화만 교체된 경우 이전 미리보기를 새 원화로 오인해 표시하지 않습니다. 원화 변경 후에는 위 도구를 실행하고 이미지·확대 화면을 확인해야 합니다.

뷰어는 JPEG 로드 실패 시 PNG로 넘어가며, 모두 실패하면 안내와 재시도를 표시합니다. 정적 썸네일은 JavaScript 없이도 보이도록 유지합니다. 모음 폴더를 다른 곳에 복사할 때는 `INDEX.html`만 떼어내지 말고 폴더 전체를 함께 옮깁니다.
