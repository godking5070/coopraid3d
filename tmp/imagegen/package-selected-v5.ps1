$ErrorActionPreference = 'Stop'
$assetRoot = (Resolve-Path -LiteralPath 'ConceptArt/Part2/SelectedEffigyVariations_v5').Path
$zipPath = Join-Path (Split-Path $assetRoot -Parent) 'SelectedEffigyVariations_v5.zip'
if (Test-Path -LiteralPath $zipPath) { throw 'Versioned zip already exists; preserve it and choose a sibling version.' }
$utf8 = New-Object System.Text.UTF8Encoding($false)
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stageTitles = @('기억을 흘리는 정찰병', '끝나지 않는 진혼곡의 무희', '얼어붙은 혈관의 연금술사', '영원히 타오르는 가마의 수호자', '무너진 천구를 떠받치는 부단장')
$expectedImages = @('01-effigy.png','02-effigy.png','03-effigy.png')

$selectionMap = @(
 [pscustomobject]@{stage=1;source='../HumanoidStageEffigies_v4/Stage01/03-effigy.png';display='v4 Stage01 의례체 3안';link='../HumanoidStageEffigies_v4/Stage01/03-effigy.png';identity='원형 방사셔터 투구, 긴 정찰 체형, 가슴 모래시계'},
 [pscustomobject]@{stage=2;source='.orca/drops/스크린샷 2026-11-12 오전 10.56.41.png';display='사용자 첨부 2026-11-12 오전 10.56.41 스크린샷';link='';identity='original ObjectBossExploration_v1/Stage02/03-effigy.png와 동일 선택 컨셉: 다면체 봉인머리, 긴 V흉부, 등 태엽, 외부 음판'},
 [pscustomobject]@{stage=3;source='../HumanoidStageEffigies_v4/Stage03/01-effigy.png';display='v4 Stage03 의례체 1안';link='../HumanoidStageEffigies_v4/Stage03/01-effigy.png';identity='짧은 백금 의료베일, 긴 치유 체형, 가슴 맥박과 등 수은관'},
 [pscustomobject]@{stage=4;source='../HumanoidStageEffigies_v4/Stage04/02-effigy.png';display='v4 Stage04 의례체 2안';link='../HumanoidStageEffigies_v4/Stage04/02-effigy.png';identity='네모 환기두건, 넓은 인간형 수호 체형, 직사각 방패, 여덟 사슬'},
 [pscustomobject]@{stage=5;source='../HumanoidStageEffigies_v4/Stage05/02-effigy.png';display='v4 Stage05 의례체 2안';link='../HumanoidStageEffigies_v4/Stage05/02-effigy.png';identity='세겹 타원 봉인투구, 한어깨 천구, 인간형 한무릎 체형, 원본 시침 창 유지'}
)

$allImages = @()
$pendingStageWrites = @()
$allPrompts = New-Object System.Text.StringBuilder
$gallery = New-Object System.Text.StringBuilder
[void]$gallery.AppendLine('<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>아스텔라 제2부 · 선택 의례체 기반 15변주</title><style>body{margin:0;background:#171d2c;color:#f3eee7;font-family:system-ui,sans-serif}main{max-width:1600px;margin:auto;padding:32px}h1{font-size:28px}p{color:#b9c3d4;line-height:1.7}section{margin:46px 0}.grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:18px}figure{margin:0;background:#242d40;border:1px solid #38435a;border-radius:10px;overflow:hidden}img{display:block;width:100%;height:auto;aspect-ratio:3/2;object-fit:contain}figcaption{padding:16px}strong{display:block;color:#e9d299;margin-bottom:8px}a{color:#d0dfff;text-decoration:none}a:hover{text-decoration:underline}small{display:block;margin-top:12px;color:#aab7cc}@media(max-width:900px){.grid{grid-template-columns:1fr}main{padding:18px}}</style></head><body><main><h1>아스텔라 제2부 · 선택 의례체 기반 15변주</h1><p>사용자가 고른 다섯 스테이지의 정확한 선택안을 바탕으로 각각 세 변주를 제작했습니다. 선택안의 머리·인간형 체형·핵심 장치·화풍을 보존하고 외피 분할, 기능 배치와 서사 자세를 바꿨습니다. 각 초기 생성에 해당 층 선택 원본을 입력했습니다. 이미지를 누르면 1536 × 1024 원본 PNG가 열립니다.</p>')
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
  $selected = $selectionMap[$stageNumber-1]
  $selectedDisplay = [Net.WebUtility]::HtmlEncode($selected.display)
  $selectedIdentity = [Net.WebUtility]::HtmlEncode($selected.identity)
  $sourceCaption = $selectedDisplay
  if ($selected.link) { $sourceCaption = '<a href="' + $selected.link + '">' + $selectedDisplay + '</a>' }
  [void]$gallery.AppendLine(('<section><h2>{0} · {1}</h2><p>선택 원본: {2}<br>{3}</p><div class="grid">' -f $stageFolder, $stageTitles[$stageNumber-1], $sourceCaption, $selectedIdentity))
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
    [void]$gallery.AppendLine(('<figure><a href="{0}"><img src="{0}" alt="{1}"></a><figcaption><strong>선택안 변주 {2}</strong>{1}<small><a href="{3}">생성 프롬프트</a> · <a href="{4}">제작 기록</a></small></figcaption></figure>' -f $relativeImage, $safeTitle, ($optionNumber+1), "$stageFolder/$id.prompt.txt", "$stageFolder/manifest.json"))
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
$index = [ordered]@{project='ASTELLA';part=2;version='SelectedEffigyVariations_v5';created_date='2026-11-12';status='generation_visual_qa_and_packaging_complete';tool='built-in image_gen';stage_count=5;concepts_per_stage=3;final_image_count=15;concept_family='Three direct variations per user-selected stage effigy';user_request='Preserve each chosen concept and create three meaningful variations per stage in parallel.';selected_sources=$selectionMap;reference_policy='The exact selected image for that stage was visually inspected and supplied in every initial generation. Head, human body proportions, core shell/mechanisms and style retained. Other stage selections and v2/v3 were not used as substitute templates.';generation_schedule='Five stage jobs progressed in parallel: three assigned stage agents and two independent stage jobs orchestrated by the root.';variation_scope='Shell segmentation, small functional placement and narrative pose; not a new head/body design or different creature. Stage05 original clock-hand spear retained.';gallery='INDEX.html';prompts='prompts-all.txt';direction='ART_DIRECTION.txt';qa='All 15 final PNGs visually inspected by the root and/or assigned stage agents for direct selected-base fidelity, connections and full-body framing. Full PNG decode, 1536x1024 dimensions, bytes and SHA256 verified. Individual manifests retain exact references, generation and correction records. Prior files preserved.';limits='Single-view concept illustrations. No 3D mesh, UV, rigging or animation verification.';images=$allImages}
[IO.File]::WriteAllText((Join-Path $assetRoot 'manifest.json'), ($index | ConvertTo-Json -Depth 20), $utf8)
$readme = @'
아스텔라 제2부 1~5스테이지 · 선택 의례체 기반 15변주
완료 범위: 다섯 선택안 × 새 변주 세 안, PNG 15장, 모두 1536×1024.

사용자가 고른 정확한 원본을 기준으로 머리·인간형 체형·핵심 외피와 장치·화풍을 보존했습니다.
전면 재설계 대신 외피 분할, 기능 장치 배치와 서사 자세를 의미 있게 바꿨습니다.
각 초기 생성에 해당 층의 정확한 선택 원본을 입력했습니다. 다른 층 선택안이나 v2/v3를 대신 넣지 않았습니다.
다섯 층을 병렬 진행했습니다: 담당 에이전트 세 층과 루트가 독립 조율한 두 층 작업입니다.

선택 매핑 (경로는 이 폴더 기준)
1층: ../HumanoidStageEffigies_v4/Stage01/03-effigy.png
원형 방사셔터 관측투구, 긴 인간형 정찰 체형과 가슴의 온전한 모래시계 유지.
2층: 사용자 첨부 .orca/drops/스크린샷 2026-11-12 오전 10.56.41.png
original ObjectBossExploration_v1/Stage02/03-effigy.png와 동일한 선택 컨셉.
다면체 봉인머리, 긴 V흉부·골반판, 분절 사지, 등 태엽과 외부 음판 유지.
3층: ../HumanoidStageEffigies_v4/Stage03/01-effigy.png
짧은 백금 의료베일, 긴 치유 체형, 가슴 맥박과 등 수은관 유지.
4층: ../HumanoidStageEffigies_v4/Stage04/02-effigy.png
네모 환기두건, 넓은 인간형 수호 체형, 직사각 방패와 여덟 사슬 유지.
5층: ../HumanoidStageEffigies_v4/Stage05/02-effigy.png
세겹 타원 봉인투구, 한어깨 천구와 인간형 한무릎 체형 유지.
원본의 긴 축·짧고 넓은 시계바늘 창끝·반대쪽 천구환/별 마감의 창을 유지하고 두손의 창 파지 관계를 보존.

INDEX.html에서 각 층의 선택 원본 정보와 세 변주를 비교하고 새 원본 PNG를 열 수 있습니다.
1·3·4·5층 선택 원본 링크는 기존 프로젝트 파일을 가리킵니다. ZIP만 분리해 열면 선택 원본 링크는 연결되지 않을 수 있으나 새 변주 15장과 제작 기록은 ZIP 내부에 포함됩니다.
2층 스크린샷은 원본 경로를 텍스트로 기록했습니다.

제작: 내장 image_gen. 새 이미지 15장을 실제 생성하고 선택안 충실도·부품 연결·전신 여백을 시각 검수했습니다.
각 Stage 폴더에 정확 생성·보정 프롬프트와 참조·출처·검수 기록 manifest.json이 있습니다.
prompts-all.txt는 전체 정확 프롬프트 모음입니다. 루트 manifest.json에 15장 크기·용량·SHA256과 상대 경로를 기록했습니다.
단일 전신 컨셉샷이며 실제 동일 3D 메시의 회전 렌더, UV, 리깅, 애니메이션 검증은 아닙니다.
선택 원본과 이전 시안은 보존했습니다.
'@
[IO.File]::WriteAllText((Join-Path $assetRoot 'README.txt'), $readme, $utf8)
$buildArchive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
  foreach ($file in (Get-ChildItem -LiteralPath $assetRoot -Recurse -File | Sort-Object FullName)) {
    $entryPath = 'SelectedEffigyVariations_v5/' + $file.FullName.Substring($assetRoot.Length + 1).Replace('\','/')
    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($buildArchive, $file.FullName, $entryPath, [IO.Compression.CompressionLevel]::Optimal)
  }
} finally { $buildArchive.Dispose() }
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
  $pngEntries = @($archive.Entries | Where-Object { $_.FullName.EndsWith('.png') })
  if ($pngEntries.Count -ne 15) { throw "ZIP PNG count mismatch: $($pngEntries.Count)" }
  foreach ($asset in $allImages) {
    $entryName = 'SelectedEffigyVariations_v5/' + $asset.image
    $entry = $archive.GetEntry($entryName)
    if (-not $entry) { throw "Missing ZIP asset: $entryName" }
    $stream = $entry.Open()
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $entryHash = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-','').ToLowerInvariant() } finally { $stream.Dispose(); $hasher.Dispose() }
    if ($entryHash -ne $asset.sha256) { throw "ZIP hash mismatch: $entryName" }
  }
  $zipEntries = $archive.Entries.Count
} finally { $archive.Dispose() }
[pscustomobject]@{status='100% complete';stages=5;final_pngs=15;concept_family='selected-base effigy variations';dimensions='1536x1024';png_hashes_verified=15;zip_png_hashes_verified=15;zip_entries=$zipEntries;zip=$zipPath;zip_bytes=(Get-Item -LiteralPath $zipPath).Length;gallery=(Join-Path $assetRoot 'INDEX.html');prompts=(Join-Path $assetRoot 'prompts-all.txt');manifest=(Join-Path $assetRoot 'manifest.json')} | ConvertTo-Json
