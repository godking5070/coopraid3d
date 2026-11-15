$ErrorActionPreference = 'Stop'
$assetRoot = (Resolve-Path -LiteralPath 'ConceptArt/Part2/HumanoidStageEffigies_v4').Path
$zipPath = Join-Path (Split-Path $assetRoot -Parent) 'HumanoidStageEffigies_v4.zip'
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
[void]$gallery.AppendLine('<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>아스텔라 제2부 · 인간형 스테이지 의례체 15안</title><style>body{margin:0;background:#171d2c;color:#f3eee7;font-family:system-ui,sans-serif}main{max-width:1600px;margin:auto;padding:32px}h1{font-size:28px}p{color:#b9c3d4;line-height:1.7}section{margin:46px 0}.grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:18px}figure{margin:0;background:#242d40;border:1px solid #38435a;border-radius:10px;overflow:hidden}img{display:block;width:100%;height:auto;aspect-ratio:3/2;object-fit:contain}figcaption{padding:16px}strong{display:block;color:#e9d299;margin-bottom:8px}a{color:#d0dfff;text-decoration:none}a:hover{text-decoration:underline}small{display:block;margin-top:12px;color:#aab7cc}@media(max-width:900px){.grid{grid-template-columns:1fr}main{padding:18px}}</style></head><body><main><h1>아스텔라 제2부 · 인간형 스테이지 의례체 15안</h1><p>사용자의 최신 교정인 인간형 체형을 반영해, 1~5스테이지 의례체를 각각 세 안씩 새로 제작했습니다. 약 7~8등신의 머리·목·흉부·허리·골반·상하완·긴 허벅지와 종아리를 분명히 유지하고, 층별 개성은 외피·자세·몸통 범위의 기관과 등 장치로 표현합니다. 거대한 묘비·가마통·성배·건물이 신체가 된 이전 방향은 폐기했습니다. 이미지를 누르면 1536 × 1024 원본 PNG가 열립니다.</p>')
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
$index = [ordered]@{project='ASTELLA';part=2;version='HumanoidStageEffigies_v4';created_date='2026-11-12';status='generation_visual_qa_and_packaging_complete';tool='built-in image_gen';stage_count=5;concepts_per_stage=3;final_image_count=15;concept_family='Humanoid stage-specific sealed ritual bodies';user_correction='Body shape should be humanoid like the previously liked concept, while preserving each stage identity.';human_proportion_rule='About 7 to 8 heads tall, readable head/neck/shoulders/ribcage/waist/pelvis/upper arms/forearms/hands/long thighs/calves/feet. v3 giant tombstone, barrel, chalice and architectural object bodies rejected.';proportion_visual_reference='../ObjectBossExploration_v1/Stage02/03-effigy.png';reference_policy='Original favored Stage02 viewed to understand human proportions only, not copied across stages. Initial generation reference images are actual PartI CharacterMatched assets. No v2/v3 designs supplied as templates.';stage_identity=@{Stage01='Human-proportioned memory scout, sealed observation head and chest-sized intact hourglass';Stage02='Human-proportioned forced funeral dancer, back winding drive and external sound organs';Stage03='Slender humanoid healing effigy with integrated rear mercury circulation';Stage04='Broad-shouldered humanoid furnace guardian, distinct waist and long legs, shield and eight chains';Stage05='Human-proportioned veteran effigy with shoulder celestial supports and hour-hand spear'};differentiation='Each stage uses distinct shell design, pose, torso-scale organs, rear mechanisms and material rhythm without replacing the human silhouette.';gallery='INDEX.html';prompts='prompts-all.txt';direction='ART_DIRECTION.txt';qa='All 15 final PNGs visually inspected by the root and/or assigned stage agents. Full PNG decode, 1536x1024 dimensions, bytes and SHA256 verified. Individual stage manifests retain exact reference, generation and correction records. Existing earlier concepts preserved; user selection is not assumed.';limits='Single-view concept illustrations. No 3D mesh, UV, rigging or animation verification.';images=$allImages}
[IO.File]::WriteAllText((Join-Path $assetRoot 'manifest.json'), ($index | ConvertTo-Json -Depth 20), $utf8)
$readme = @'
아스텔라 제2부 1~5스테이지 · 인간형 스테이지 의례체 15종
완료 범위: 다섯 스테이지 × 인간형 의례체 세 안, PNG 15장, 모두 1536×1024.

사용자 최신 교정: "아까 내가 좋다고 했던 것처럼 체형은 인간형이면 좋겠어."
각 스테이지 고유 개성과 명확한 인간형 체형을 함께 유지합니다.
약 7~8등신 머리·목·두 어깨·갈비흉부·허리·골반·상하완·손·긴 허벅지/종아리·발이 분명한 의례체입니다.
이전 v3의 거대한 묘비·가마통·성배·건물이 몸 자체가 된 체형과 짧은 주각 다리를 폐기했습니다.
층의 개성은 인간형 외피, 자세, 몸통 범위 안에 융합된 기관과 등 장치에서 표현합니다. 실제 사람 얼굴·눈동자·헤어·직업 의상은 배제합니다.

ObjectBossExploration_v1/Stage02/03-effigy.png는 좋아한 인간 실루엣의 비율을 눈으로 이해하는 시각 기준입니다.
다이아몬드 머리·V자 흉부·골반 날개·음악 장치를 다른 스테이지에 복제하지 않습니다.
초기 생성의 이미지 입력은 실제 1부 CharacterMatched PNG를 사용했습니다. v2/v3 디자인은 입력하지 않았습니다.
공통 화풍은 얇고 정돈된 선, 상아·밝은 금·남색 큰 면과 연보라 셀 그림자입니다.

1층: 인간형 기억 정찰병, 봉인된 관측 머리, 흉부 범위의 온전한 모래시계와 내부 모래.
2층: 인간형 강제 무희, 등 태엽·시계 캠·외부 음향기관, 비극적 장송 연주와 저항.
3층: 인간형 치유 봉인체, 가슴·허리·골반과 긴 다리, 등에 융합된 은빛 수은 순환관.
4층: 넓은 어깨와 두꺼운 팔, 분명한 허리·골반·긴 사람 다리의 가마 수호자, 방패와 여덟 사슬.
5층: 긴 사람 다리와 허리·골반이 있는 부단장 성상, 어깨/등 천구 지지부와 시침 창.

INDEX.html에서 각 스테이지 세 안을 비교하고 원본을 열 수 있습니다.
제작: 내장 image_gen. 새 이미지 15장을 실제 생성하고 시각 검수했습니다.
각 Stage 폴더에 정확 생성·보정 프롬프트와 참조·출처·검수 기록 manifest.json이 있습니다.
prompts-all.txt는 전체 정확 프롬프트 모음입니다. 루트 manifest.json에 최종 15장의 크기·용량·SHA256과 상대 경로를 기록했습니다.
본 자료는 단일 전신 컨셉샷이며 실제 동일 3D 메시의 회전 렌더, UV, 리깅, 애니메이션 검증은 아닙니다.
이전 시안은 보존했고 새 15안의 선택·승인은 사용자에게 남겼습니다.
'@
[IO.File]::WriteAllText((Join-Path $assetRoot 'README.txt'), $readme, $utf8)
$buildArchive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
  foreach ($file in (Get-ChildItem -LiteralPath $assetRoot -Recurse -File | Sort-Object FullName)) {
    $entryPath = 'HumanoidStageEffigies_v4/' + $file.FullName.Substring($assetRoot.Length + 1).Replace('\','/')
    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($buildArchive, $file.FullName, $entryPath, [IO.Compression.CompressionLevel]::Optimal)
  }
} finally { $buildArchive.Dispose() }
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
  $pngEntries = @($archive.Entries | Where-Object { $_.FullName.EndsWith('.png') })
  if ($pngEntries.Count -ne 15) { throw "ZIP PNG count mismatch: $($pngEntries.Count)" }
  foreach ($asset in $allImages) {
    $entryName = 'HumanoidStageEffigies_v4/' + $asset.image
    $entry = $archive.GetEntry($entryName)
    if (-not $entry) { throw "Missing ZIP asset: $entryName" }
    $stream = $entry.Open()
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $entryHash = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-','').ToLowerInvariant() } finally { $stream.Dispose(); $hasher.Dispose() }
    if ($entryHash -ne $asset.sha256) { throw "ZIP hash mismatch: $entryName" }
  }
  $zipEntries = $archive.Entries.Count
} finally { $archive.Dispose() }
[pscustomobject]@{status='100% complete';stages=5;final_pngs=15;concept_family='humanoid stage-specific ritual bodies';dimensions='1536x1024';png_hashes_verified=15;zip_png_hashes_verified=15;zip_entries=$zipEntries;zip=$zipPath;zip_bytes=(Get-Item -LiteralPath $zipPath).Length;gallery=(Join-Path $assetRoot 'INDEX.html');prompts=(Join-Path $assetRoot 'prompts-all.txt');manifest=(Join-Path $assetRoot 'manifest.json')} | ConvertTo-Json
