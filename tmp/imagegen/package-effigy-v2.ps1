$ErrorActionPreference = 'Stop'
$assetRoot = (Resolve-Path -LiteralPath 'ConceptArt/Part2/EffigyExploration_v2').Path
$zipPath = Join-Path (Split-Path $assetRoot -Parent) 'EffigyExploration_v2.zip'
if (Test-Path -LiteralPath $zipPath) { throw 'Versioned zip already exists; preserve it and choose a sibling version.' }
$utf8 = New-Object System.Text.UTF8Encoding($false)
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stageTitles = @('기억을 흘리는 정찰병', '끝나지 않는 진혼곡의 무희', '얼어붙은 혈관의 연금술사', '영원히 타오르는 가마의 수호자', '무너진 천구를 떠받치는 부단장')
$expectedImages = @('01-effigy.png','02-effigy.png','03-effigy.png')
$allImages = @()
$pendingStageWrites = @()
$allPrompts = New-Object System.Text.StringBuilder
$gallery = New-Object System.Text.StringBuilder
[void]$gallery.AppendLine('<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>아스텔라 제2부 · 의례체 추가 15안</title><style>body{margin:0;background:#171d2c;color:#f3eee7;font-family:system-ui,sans-serif}main{max-width:1600px;margin:auto;padding:32px}h1{font-size:28px}p{color:#b9c3d4;line-height:1.7}section{margin:46px 0}.grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:18px}figure{margin:0;background:#242d40;border:1px solid #38435a;border-radius:10px;overflow:hidden}img{display:block;width:100%;height:auto;aspect-ratio:3/2;object-fit:contain}figcaption{padding:16px}strong{display:block;color:#e9d299;margin-bottom:8px}a{color:#d0dfff;text-decoration:none}a:hover{text-decoration:underline}small{display:block;margin-top:12px;color:#aab7cc}@media(max-width:900px){.grid{grid-template-columns:1fr}main{padding:18px}}</style></head><body><main><h1>아스텔라 제2부 · 의례체 추가 15안</h1><p>1~5스테이지마다 새로운 의례체 세 안을 제작했습니다. 사용자가 선호한 이전 2스테이지 의례체의 봉인된 기하학 머리, 긴 분절 사지, 큰 외피와 노출 연결 구조를 발전시키고 각 층의 기능과 자세를 달리했습니다. 이미지를 누르면 1536 × 1024 원본 PNG가 열립니다.</p>')
for ($stageNumber = 1; $stageNumber -le 5; $stageNumber++) {
  $stageFolder = 'Stage{0:d2}' -f $stageNumber
  $stagePath = Join-Path $assetRoot $stageFolder
  $manifestPath = Join-Path $stagePath 'manifest.json'
  $stageManifest = [IO.File]::ReadAllText($manifestPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
  $assets = @($stageManifest.images | Sort-Object image)
  if ($assets.Count -ne 3) { throw "Expected 3 images in $stageFolder manifest" }
  $actualFiles = @(Get-ChildItem -LiteralPath $stagePath -File -Filter '*.png' | Sort-Object Name)
  if ($actualFiles.Count -ne 3) { throw "Expected exactly 3 final PNGs in $stageFolder" }
  $stagePromptBundle = New-Object System.Text.StringBuilder
  [void]$gallery.AppendLine(('<section><h2>{0} · {1}</h2><div class="grid">' -f $stageFolder, $stageTitles[$stageNumber-1]))
  for ($optionNumber = 0; $optionNumber -lt 3; $optionNumber++) {
    $asset = $assets[$optionNumber]
    if ($asset.image -ne $expectedImages[$optionNumber]) { throw "Unexpected asset name in $stageFolder" }
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
    if ($prompts.Count -lt 1) { throw "Missing exact prompt: $imagePath" }
    foreach ($prompt in $prompts) {
      $content = [IO.File]::ReadAllText($prompt.FullName, [Text.Encoding]::UTF8)
      if ([String]::IsNullOrWhiteSpace($content)) { throw "Empty prompt: $($prompt.FullName)" }
      [void]$stagePromptBundle.AppendLine("===== $($prompt.Name) =====`r`n$content`r`n")
      [void]$allPrompts.AppendLine("===== $stageFolder/$($prompt.Name) =====`r`n$content`r`n")
    }
    $relativeImage = "$stageFolder/$($asset.image)"
    $allImages += [pscustomobject]@{stage=$stageNumber;id=$id;title_ko=$asset.title_ko;image=$relativeImage;width=$width;height=$height;bytes=$imageInfo.Length;sha256=$sha;prompts=@($prompts | ForEach-Object { "$stageFolder/$($_.Name)" });manifest="$stageFolder/manifest.json"}
    $safeTitle = [Net.WebUtility]::HtmlEncode($asset.title_ko)
    [void]$gallery.AppendLine(('<figure><a href="{0}"><img src="{0}" alt="{1}"></a><figcaption><strong>의례체 {2}안</strong>{1}<small><a href="{3}">생성 프롬프트</a> · <a href="{4}">제작 기록</a></small></figcaption></figure>' -f $relativeImage, $safeTitle, ($optionNumber+1), "$stageFolder/$id.prompt.txt", "$stageFolder/manifest.json"))
  }
  [void]$gallery.AppendLine('</div></section>')
  $stageManifest | Add-Member -MemberType NoteProperty -Name file_validation -Value 'Three PNGs decoded at 1536x1024; bytes and SHA256 computed and existing hashes matched.' -Force
  $pendingStageWrites += [pscustomobject]@{path=$manifestPath;json=($stageManifest | ConvertTo-Json -Depth 20);promptPath=(Join-Path $stagePath 'prompts-all.txt');promptText=$stagePromptBundle.ToString()}
}
if ($allImages.Count -ne 15) { throw 'Expected 15 final images' }
[void]$gallery.AppendLine('<p>제작: 내장 image_gen. 단일 전신 컨셉샷이며 실제 3D 메시나 회전 렌더는 아닙니다. <a href="prompts-all.txt">전체 프롬프트</a> · <a href="ART_DIRECTION.txt">공통 아트 방향</a></p></main></body></html>')
if ([regex]::Matches($gallery.ToString(),'<img src=').Count -ne 15) { throw 'Gallery image count mismatch' }
foreach ($write in $pendingStageWrites) {
  [IO.File]::WriteAllText($write.path, $write.json, $utf8)
  [IO.File]::WriteAllText($write.promptPath, $write.promptText, $utf8)
}
[IO.File]::WriteAllText((Join-Path $assetRoot 'INDEX.html'), $gallery.ToString(), $utf8)
[IO.File]::WriteAllText((Join-Path $assetRoot 'prompts-all.txt'), $allPrompts.ToString(), $utf8)
$index = [ordered]@{project='ASTELLA';part=2;version='EffigyExploration_v2';created_date='2026-11-11';status='generation_visual_qa_and_packaging_complete';tool='built-in image_gen';stage_count=5;concepts_per_stage=3;final_image_count=15;concept_family='Additional sealed ritual effigies only';preferred_reference='../ObjectBossExploration_v1/Stage02/03-effigy.png';gallery='INDEX.html';prompts='prompts-all.txt';direction='ART_DIRECTION.txt';qa='All 15 final PNGs visually inspected by the root and/or assigned stage agents. Full PNG decode, 1536x1024 dimensions, bytes and SHA256 verified. Individual stage manifests retain generation, reference and targeted correction records. Existing earlier concepts preserved; user selection is not assumed.';limits='Single-view concept illustrations. No 3D mesh, UV, rigging or animation verification.';images=$allImages}
[IO.File]::WriteAllText((Join-Path $assetRoot 'manifest.json'), ($index | ConvertTo-Json -Depth 20), $utf8)
$readme = @'
아스텔라 제2부 1~5스테이지 · 의례체 추가 비교안 15종
완료 범위: 다섯 스테이지 × 새로운 의례체 세 안, PNG 15장, 모두 1536×1024.

INDEX.html에서 스테이지별 세 안을 나란히 비교하고 이미지를 눌러 원본을 열 수 있습니다.
주요 선호 기준은 이전 ObjectBossExploration_v1/Stage02/03-effigy.png입니다. 봉인된 기하학 머리, 긴 분절 사지, 큰 외피와 노출 연결 구조를 발전시켰습니다.
모든 새 안은 인간 초상을 배제한 석상·비인격적 의례 인형이며 각 층의 기능, 질량, 자세를 달리했습니다.
1부 CharacterMatched의 상아·밝은 금·남색 색면, 얇고 정돈된 선과 연보라 셀 그림자를 이어갑니다.

1층: 온전한 모래시계와 내부의 황금 모래, 봉인된 관측과 기억 계측.
2층: 강제 춤과 진혼곡, 등 태엽 구동과 외부 음향기관.
3층: 등/척추 순환기관의 은빛 수은, 생명력 역류와 마지막 치유.
4층: 용광로 하중, 보호 방벽과 여덟 구속사슬.
5층: 무너진 천구를 받치는 의례체와 시침 창.

제작: 내장 image_gen. 모든 새 이미지를 실제 생성하고 시각 검수했습니다.
각 Stage 폴더에 개별 정확 생성·보정 프롬프트와 참조·출처·검수 기록 manifest.json이 있습니다.
prompts-all.txt는 정확 프롬프트 모음입니다. 루트 manifest.json에 최종 15장의 크기·용량·SHA256과 상대 경로를 기록했습니다.
본 산출물은 단일 전신 컨셉샷이며 실제 동일 3D 메시의 회전 렌더, UV, 리깅, 애니메이션 검증은 아닙니다.
이전 시안은 보존했으며 새 15안의 선택·승인은 사용자에게 남겼습니다.
'@
[IO.File]::WriteAllText((Join-Path $assetRoot 'README.txt'), $readme, $utf8)
$buildArchive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
  foreach ($file in (Get-ChildItem -LiteralPath $assetRoot -Recurse -File | Sort-Object FullName)) {
    $entryPath = 'EffigyExploration_v2/' + $file.FullName.Substring($assetRoot.Length + 1).Replace('\','/')
    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($buildArchive, $file.FullName, $entryPath, [IO.Compression.CompressionLevel]::Optimal)
  }
} finally { $buildArchive.Dispose() }
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
  $pngEntries = @($archive.Entries | Where-Object { $_.FullName.EndsWith('.png') })
  if ($pngEntries.Count -ne 15) { throw "ZIP PNG count mismatch: $($pngEntries.Count)" }
  foreach ($asset in $allImages) {
    $entryName = 'EffigyExploration_v2/' + $asset.image
    $entry = $archive.GetEntry($entryName)
    if (-not $entry) { throw "Missing ZIP asset: $entryName" }
    $stream = $entry.Open()
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $entryHash = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-','').ToLowerInvariant() } finally { $stream.Dispose(); $hasher.Dispose() }
    if ($entryHash -ne $asset.sha256) { throw "ZIP hash mismatch: $entryName" }
  }
  $zipEntries = $archive.Entries.Count
} finally { $archive.Dispose() }
[pscustomobject]@{status='100% complete';stages=5;final_pngs=15;concept_family='additional ritual effigies';dimensions='1536x1024';png_hashes_verified=15;zip_png_hashes_verified=15;zip_entries=$zipEntries;zip=$zipPath;zip_bytes=(Get-Item -LiteralPath $zipPath).Length;gallery=(Join-Path $assetRoot 'INDEX.html');prompts=(Join-Path $assetRoot 'prompts-all.txt');manifest=(Join-Path $assetRoot 'manifest.json')} | ConvertTo-Json
