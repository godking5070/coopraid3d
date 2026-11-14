$ErrorActionPreference = 'Stop'
$assetRoot = (Resolve-Path -LiteralPath 'ConceptArt/Part2/ObjectBossExploration_v1').Path
$utf8 = New-Object System.Text.UTF8Encoding($false)
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stageTitles = @('기억을 흘리는 정찰병', '끝나지 않는 진혼곡의 무희', '얼어붙은 혈관의 연금술사', '영원히 타오르는 가마의 수호자', '무너진 천구를 떠받치는 부단장')
$typeNames = @{'01-monster' = '1안 · 비인간 오브젝트'; '02-hybrid' = '2안 · 인체 흔적의 융합체'; '03-effigy' = '3안 · 봉인 석상 / 의례체'}
$allImages = @()
$allPrompts = New-Object System.Text.StringBuilder
$gallery = New-Object System.Text.StringBuilder
[void]$gallery.AppendLine('<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>아스텔라 제2부 보스 · 15개 비교안</title><style>body{margin:0;background:#171d2c;color:#f3eee7;font-family:system-ui,sans-serif}main{max-width:1600px;margin:auto;padding:32px}h1{font-size:28px}p{color:#b9c3d4;line-height:1.6}section{margin:46px 0}.grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:18px}figure{margin:0;background:#242d40;border:1px solid #38435a;border-radius:10px;overflow:hidden}img{display:block;width:100%;height:auto;aspect-ratio:3/2;object-fit:contain}figcaption{padding:16px}strong{display:block;color:#e9d299;margin-bottom:8px}a{color:#d0dfff;text-decoration:none}a:hover{text-decoration:underline}small{display:block;margin-top:12px;color:#aab7cc}@media(max-width:900px){.grid{grid-template-columns:1fr}main{padding:18px}}</style></head><body><main><h1>아스텔라 제2부 · 1~5스테이지 보스 15안</h1><p>1부 보스의 조각상·인형·살아 있는 오브젝트 조형과 셀 채색을 기준으로 제작한 전신 컨셉샷입니다. 이미지를 누르면 원본 PNG가 열립니다. 각 그림은 1536 × 1024이며, 세부 프롬프트와 보정 기록을 함께 담았습니다.</p>')
for ($stageNumber = 1; $stageNumber -le 5; $stageNumber++) {
  $stageFolder = 'Stage{0:d2}' -f $stageNumber
  $stagePath = Join-Path $assetRoot $stageFolder
  $manifestPath = Join-Path $stagePath 'manifest.json'
  $stageManifest = [IO.File]::ReadAllText($manifestPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
  if ($stageManifest.images) { $assets = @($stageManifest.images) }
  elseif ($stageManifest.assets) { $assets = @($stageManifest.assets) }
  else { $assets = @($stageManifest.concepts) }
  if ($assets.Count -ne 3) { throw "Expected 3 assets in $stageFolder" }
  $stagePromptBundle = New-Object System.Text.StringBuilder
  [void]$gallery.AppendLine(('<section><h2>{0} · {1}</h2><div class="grid">' -f $stageFolder, $stageTitles[$stageNumber-1]))
  foreach ($asset in $assets) {
    $imagePath = Join-Path $stagePath $asset.image
    $imageInfo = Get-Item -LiteralPath $imagePath
    $bitmap = [Drawing.Image]::FromFile($imagePath)
    try { $width = $bitmap.Width; $height = $bitmap.Height } finally { $bitmap.Dispose() }
    if ($width -ne 1536 -or $height -ne 1024) { throw "Unexpected dimensions: $imagePath" }
    $sha = (Get-FileHash -LiteralPath $imagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($asset.sha256 -and $asset.sha256 -ne $sha) { throw "Manifest hash mismatch: $imagePath" }
    if ($asset.bytes -and $asset.bytes -ne $imageInfo.Length) { throw "Manifest bytes mismatch: $imagePath" }
    $asset | Add-Member -MemberType NoteProperty -Name width -Value $width -Force
    $asset | Add-Member -MemberType NoteProperty -Name height -Value $height -Force
    $asset | Add-Member -MemberType NoteProperty -Name bytes -Value $imageInfo.Length -Force
    $asset | Add-Member -MemberType NoteProperty -Name sha256 -Value $sha -Force
    $id = [IO.Path]::GetFileNameWithoutExtension($asset.image)
    $prompts = @(Get-ChildItem -LiteralPath $stagePath -File | Where-Object { $_.Name.StartsWith($id + '.') -and $_.Name.EndsWith('.prompt.txt') } | Sort-Object Name)
    if ($prompts.Count -lt 1) { throw "Missing prompt: $imagePath" }
    foreach ($prompt in $prompts) {
      $content = [IO.File]::ReadAllText($prompt.FullName, [Text.Encoding]::UTF8)
      [void]$stagePromptBundle.AppendLine("===== $($prompt.Name) =====`r`n$content`r`n")
      [void]$allPrompts.AppendLine("===== $stageFolder/$($prompt.Name) =====`r`n$content`r`n")
    }
    $relativeImage = "$stageFolder/$($asset.image)"
    $allImages += [pscustomobject]@{stage=$stageNumber;id=$id;title_ko=$asset.title_ko;image=$relativeImage;width=$width;height=$height;bytes=$imageInfo.Length;sha256=$sha;prompts=@($prompts | ForEach-Object { "$stageFolder/$($_.Name)" });manifest="$stageFolder/manifest.json"}
    $safeTitle = [Net.WebUtility]::HtmlEncode($asset.title_ko)
    [void]$gallery.AppendLine(('<figure><a href="{0}"><img src="{0}" alt="{1}"></a><figcaption><strong>{2}</strong>{1}<small><a href="{3}">생성 프롬프트</a> · <a href="{4}">제작 기록</a></small></figcaption></figure>' -f $relativeImage, $safeTitle, $typeNames[$id], "$stageFolder/$id.prompt.txt", "$stageFolder/manifest.json"))
  }
  [void]$gallery.AppendLine('</div></section>')
  [IO.File]::WriteAllText((Join-Path $stagePath 'prompts-all.txt'), $stagePromptBundle.ToString(), $utf8)
  [IO.File]::WriteAllText($manifestPath, ($stageManifest | ConvertTo-Json -Depth 15), $utf8)
}
if ($allImages.Count -ne 15) { throw 'Expected 15 final images' }
[void]$gallery.AppendLine('<p>제작: 내장 image_gen. 본 자료는 비교용 컨셉 일러스트이며 실제 3D 메시나 회전 렌더가 아닙니다. <a href="prompts-all.txt">전체 프롬프트</a> · <a href="ART_DIRECTION.txt">공통 아트 방향</a></p></main></body></html>')
[IO.File]::WriteAllText((Join-Path $assetRoot 'INDEX.html'), $gallery.ToString(), $utf8)
[IO.File]::WriteAllText((Join-Path $assetRoot 'prompts-all.txt'), $allPrompts.ToString(), $utf8)
$index = [ordered]@{project='ASTELLA';part=2;version='ObjectBossExploration_v1';created_date='2026-11-11';status='generation_visual_qa_and_packaging_complete';tool='built-in image_gen';stage_count=5;concepts_per_stage=3;final_image_count=15;gallery='INDEX.html';prompts='prompts-all.txt';direction='ART_DIRECTION.txt';qa='All 15 final PNGs visually inspected by the root agent and/or stage agents. PNG decode, 1536x1024 dimensions, bytes and SHA256 verified. Stage01 framing and head support, Stage02 surplus leg and hand margin, Stage04 eight-chain distribution and Stage05 top-ring margin were corrected. Existing earlier concepts preserved. User selection is not assumed.';limits='Single-view concept illustrations. No 3D mesh, UV, rigging or animation verification.';images=$allImages}
[IO.File]::WriteAllText((Join-Path $assetRoot 'manifest.json'), ($index | ConvertTo-Json -Depth 15), $utf8)
$readme = @'
아스텔라 제2부 1~5스테이지 보스 · 새 비교안 15종
완료 범위: 5스테이지 × 3안, PNG 15장, 모두 1536×1024.

INDEX.html을 열면 스테이지마다 세 안을 비교할 수 있습니다.
01-monster: 인간 형태가 사라진 살아 있는 오브젝트.
02-hybrid: 손·팔다리의 흔적이 장치/석재에 잠식된 융합체.
03-effigy: 인간 초상 대신 봉인된 석상/비인격적 의례체.

1부 CharacterMatched의 실제 이미지에서 큰 조각 면, 고정된 표면, 비인간 비례와 연결 구조, 상아·금·남색 색면과 연보라 셀 그림자를 참고했습니다.
1층 모래시계는 온전하고 모래는 내부에만 흐릅니다. 2층은 외부 음향부와 태엽 구동, 3층은 등/순환기관의 은빛 수은, 4층은 방벽과 여덟 사슬, 5층은 천구 하중과 시침 창을 구분했습니다.

제작 모드: 내장 image_gen. 모든 이미지를 실제 생성하고 시각 검수했습니다.
각 Stage 폴더에 정확한 개별 생성·보정 프롬프트와 manifest.json이 있습니다.
prompts-all.txt는 전체 프롬프트 모음입니다. 루트 manifest.json에는 15장 크기·용량·SHA256과 상대 경로를 기록했습니다.
이번 산출물은 단일 전신 컨셉샷이며 실제 3D 메시, UV, 리깅, 애니메이션 검증 자료는 아닙니다.
이전 시안은 보존했고, 새 15안의 선택/승인을 사용자 대신 확정하지 않았습니다.
'@
[IO.File]::WriteAllText((Join-Path $assetRoot 'README.txt'), $readme, $utf8)
$currentPath = (Resolve-Path -LiteralPath 'ConceptArt/Part2/CURRENT_ART_DIRECTION.txt').Path
$previousDirection = [IO.File]::ReadAllText($currentPath, [Text.Encoding]::UTF8)
$latestDirection = @'
2026-11-11 최신 작업 기준 — 아래 이전 기록보다 이 단락을 우선한다.
사용자 수정: 인간형의 플레이어 캐릭터 같은 개성을 없애고 1부 CharacterMatched의 실제 보스들을 참고해 제2부 Stage1~5를 각각 3안씩 병렬 제작.
오브젝트 자체가 몸인 비인간 괴물 / 인체의 손·관절 흔적만 융합된 장치체 / 인간 초상이 아닌 봉인 석상·의례체의 세 방향이다. 실제 사람 얼굴·눈동자·자연 헤어·직업 코스튬을 중심에 두지 않는다. 이전 인간형 시안의 승인/선호는 이번 비교안의 참조 방향으로 자동 승계하지 않는다.
완료: ConceptArt/Part2/ObjectBossExploration_v1/Stage01~Stage05의 PNG 15장. 내장 image_gen 생성·시각 검수·크기/해시 확인·프롬프트와 제작 기록 정리 완료. INDEX.html로 비교하고 prompts-all.txt에서 전체 프롬프트를 확인한다.
Stage1 온전한 모래시계, Stage2 외부 음향부와 강제 태엽 구동(몸속 금색 오르골 실린더 전시 없음), Stage3 등/순환기관의 불투명 은빛 수은, Stage4 방벽과 여덟 사슬(좌4 우4), Stage5 천구 지지와 시침 창을 유지한다.
사용자 지시 "100퍼 되면 멈춰"에 따라 요청된 15안 완료 후 추가 스테이지·하드·4면 제작으로 확장하지 않는다. 새 비교안의 선택은 사용자에게 남긴다. 실제 3D 검증 완료로 기록하지 않는다.

이전 작업 기록 (시간순 보존)

'@
if (-not $previousDirection.StartsWith('2026-11-11 최신 작업 기준')) { [IO.File]::WriteAllText($currentPath, $latestDirection + $previousDirection, $utf8) }
$zipPath = Join-Path (Split-Path $assetRoot -Parent) 'ObjectBossExploration_v1.zip'
if (Test-Path -LiteralPath $zipPath) { throw 'Versioned zip already exists; choose a sibling version.' }
$buildArchive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
  foreach ($file in (Get-ChildItem -LiteralPath $assetRoot -Recurse -File | Sort-Object FullName)) {
    $entryPath = 'ObjectBossExploration_v1/' + $file.FullName.Substring($assetRoot.Length + 1).Replace('\','/')
    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($buildArchive, $file.FullName, $entryPath, [IO.Compression.CompressionLevel]::Optimal)
  }
} finally { $buildArchive.Dispose() }
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
  $pngEntries = @($archive.Entries | Where-Object { $_.FullName.EndsWith('.png') })
  if ($pngEntries.Count -ne 15) { throw "ZIP PNG count mismatch: $($pngEntries.Count)" }
  foreach ($asset in $allImages) {
    $entryName = 'ObjectBossExploration_v1/' + $asset.image
    $entry = $archive.GetEntry($entryName)
    if (-not $entry) { throw "Missing ZIP asset: $entryName" }
    $stream = $entry.Open()
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $entryHash = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-','').ToLowerInvariant() } finally { $stream.Dispose(); $hasher.Dispose() }
    if ($entryHash -ne $asset.sha256) { throw "ZIP hash mismatch: $entryName" }
  }
  $zipEntries = $archive.Entries.Count
} finally { $archive.Dispose() }
$galleryText = [IO.File]::ReadAllText((Join-Path $assetRoot 'INDEX.html'), [Text.Encoding]::UTF8)
if ([regex]::Matches($galleryText,'<img src=').Count -ne 15) { throw 'Gallery image count mismatch' }
[pscustomobject]@{status='100% complete';stages=5;final_pngs=15;dimensions='1536x1024';png_hashes_verified=15;zip_png_hashes_verified=15;zip_entries=$zipEntries;zip=$zipPath;zip_bytes=(Get-Item -LiteralPath $zipPath).Length;gallery=(Join-Path $assetRoot 'INDEX.html');prompts=(Join-Path $assetRoot 'prompts-all.txt')} | ConvertTo-Json
