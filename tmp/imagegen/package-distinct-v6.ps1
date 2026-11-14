$ErrorActionPreference = 'Stop'
$assetRoot = (Resolve-Path -LiteralPath 'ConceptArt/Part2/DistinctStageEffigies_v6').Path
$zipPath = Join-Path (Split-Path $assetRoot -Parent) 'DistinctStageEffigies_v6.zip'
if (Test-Path -LiteralPath $zipPath) { throw 'Versioned zip already exists; preserve it and choose a sibling version.' }
$utf8 = New-Object System.Text.UTF8Encoding($false)
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stageTitles = @('기억을 흘리는 정찰병', '끝나지 않는 진혼곡의 무희', '얼어붙은 혈관의 연금술사', '영원히 타오르는 가마의 수호자', '무너진 천구를 떠받치는 부단장')
$expectedImages = @('01-effigy.png','02-effigy.png','03-effigy.png')

$selectionMap = @(
 [pscustomobject]@{stage=1;source='../HumanoidStageEffigies_v4/Stage01/03-effigy.png';display='Stage01 가족성·화풍·인간 비율 기준';link='../HumanoidStageEffigies_v4/Stage01/03-effigy.png';identity='기억 수집 / 감각 봉인 / 모래 분신 계측: 머리·흉부·기능 장치가 다른 세 안'},
 [pscustomobject]@{stage=2;source='../ObjectBossExploration_v1/Stage02/03-effigy.png';display='Stage02 가족성·화풍·인간 비율 기준';link='../ObjectBossExploration_v1/Stage02/03-effigy.png';identity='기존 다면체 머리·V흉부 반복을 피하고 장송 연주의 관 / 음판 / 공명종추로 차별화'},
 [pscustomobject]@{stage=3;source='../HumanoidStageEffigies_v4/Stage03/01-effigy.png';display='Stage03 가족성·화풍·인간 비율 기준';link='../HumanoidStageEffigies_v4/Stage03/01-effigy.png';identity='수은 맥박 성유물 / 증류 응급치유 도구 / 생명저울: 수은 아티팩트를 명확한 기능 기관으로 강화'},
 [pscustomobject]@{stage=4;source='../HumanoidStageEffigies_v4/Stage04/02-effigy.png';display='Stage04 가족성·화풍·인간 비율 기준';link='../HumanoidStageEffigies_v4/Stage04/02-effigy.png';identity='어깨 뒤 가로축·롤러·기둥 삭제, 두꺼운 흉복갑과 큰 건틀릿의 인간형 탱커 세 안'},
 [pscustomobject]@{stage=5;source='../HumanoidStageEffigies_v4/Stage05/02-effigy.png';display='Stage05 가족성·화풍·인간 비율 기준';link='../HumanoidStageEffigies_v4/Stage05/02-effigy.png';identity='봉인 투구·흉갑·하중 외피를 다르게 설계하고 한어깨 천구와 원본 시침 창은 유지'}
)

$allImages = @()
$pendingStageWrites = @()
$allPrompts = New-Object System.Text.StringBuilder
$gallery = New-Object System.Text.StringBuilder
[void]$gallery.AppendLine('<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>아스텔라 제2부 · 외형·세부 컨셉이 다른 의례체 15안</title><style>body{margin:0;background:#171d2c;color:#f3eee7;font-family:system-ui,sans-serif}main{max-width:1600px;margin:auto;padding:32px}h1{font-size:28px}p{color:#b9c3d4;line-height:1.7}section{margin:46px 0}.grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:18px}figure{margin:0;background:#242d40;border:1px solid #38435a;border-radius:10px;overflow:hidden}img{display:block;width:100%;height:auto;aspect-ratio:3/2;object-fit:contain}figcaption{padding:16px}strong{display:block;color:#e9d299;margin-bottom:8px}a{color:#d0dfff;text-decoration:none}a:hover{text-decoration:underline}small{display:block;margin-top:12px;color:#aab7cc}@media(max-width:900px){.grid{grid-template-columns:1fr}main{padding:18px}}</style></head><body><main><h1>아스텔라 제2부 · 외형·세부 컨셉이 다른 의례체 15안</h1><p>다섯 스테이지마다 생김새와 세부 컨셉이 다른 인간형 의례체 세 안을 제작했습니다. 각 층의 기준 이미지는 가족성·화풍·인간 비율 참고이며 머리나 흉부를 그대로 고정하는 템플릿이 아닙니다. 각 안의 봉인머리, 흉부 구성, 외피와 핵심 기능 장치를 다르게 설계했습니다. 이미지를 누르면 1536 × 1024 원본 PNG가 열립니다.</p>')
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
  [void]$gallery.AppendLine(('<section><h2>{0} · {1}</h2><p>가족성 참고: {2}<br>{3}</p><div class="grid">' -f $stageFolder, $stageTitles[$stageNumber-1], $sourceCaption, $selectedIdentity))
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
    [void]$gallery.AppendLine(('<figure><a href="{0}"><img src="{0}" alt="{1}"></a><figcaption><strong>의례체 컨셉 {2}</strong>{1}<small><a href="{3}">생성 프롬프트</a> · <a href="{4}">제작 기록</a></small></figcaption></figure>' -f $relativeImage, $safeTitle, ($optionNumber+1), "$stageFolder/$id.prompt.txt", "$stageFolder/manifest.json"))
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
$index = [ordered]@{project='ASTELLA';part=2;version='DistinctStageEffigies_v6';created_date='2026-11-12';status='generation_visual_qa_and_packaging_complete';tool='built-in image_gen';stage_count=5;concepts_per_stage=3;final_image_count=15;concept_family='Three visually distinct humanoid sub-concepts per stage';user_correction='Make appearances and small concepts different, not the same body/head with only changed poses.';family_references=$selectionMap;reference_policy='Each stage base is a family, art-style and human-proportion reference, not an exact head/body lock. Sealing head, chest architecture, shell and primary artifact deliberately differ per concept. Stage theme, 7-8-head humanoid body and cel materials retained.';generation_schedule='Five stage jobs progressed in parallel: three assigned stage agents and two independent stage jobs orchestrated by the root.';stage_changes=@{Stage01='Memory collection / sense sealing / sand-double measurement, with different observation heads and measuring functions';Stage02='Avoid original faceted-head and V-torso repeat; external sound pipes / plates / resonance pendulum concepts';Stage03='Mercury pulse reliquary / emergency distillation tools / life-balance artifacts strengthened';Stage04='Remove large rear shoulder shaft, rollers and columns; increase chest/abdomen armor and heavy gauntlets for humanoid tank';Stage05='Change sealed helmet, chest armor and load-bearing shells while preserving small celestial support and original hour-hand spear'};gallery='INDEX.html';prompts='prompts-all.txt';direction='ART_DIRECTION.txt';qa='All 15 final PNGs visually inspected by the root and/or assigned stage agents for different head, chest, shell and artifact concepts, humanoid anatomy, connected parts and full-body framing. PNG decode, 1536x1024 dimensions, bytes and SHA256 verified. Individual manifests retain exact references, generation and correction records. Prior files preserved.';limits='Single-view concept illustrations. No 3D mesh, UV, rigging or animation verification.';images=$allImages}
[IO.File]::WriteAllText((Join-Path $assetRoot 'manifest.json'), ($index | ConvertTo-Json -Depth 20), $utf8)
$readme = @'
아스텔라 제2부 1~5스테이지 · 외형과 세부 컨셉이 다른 인간형 의례체 15안
완료 범위: 다섯 스테이지 × 독립 외형·세부 컨셉 세 안, PNG 15장, 모두 1536×1024.

사용자 최신 수정: 생김새도 조금씩 다르게, 컨셉도 조금씩 다르게.
동일한 머리·몸통·장치를 유지한 채 자세만 바꾸는 이전 방식을 폐기했습니다.
이번 각 층의 세 안은 봉인머리, 흉부 구성, 외피, 기능 아티팩트를 함께 구별합니다.
v4 및 해당 층의 기준 이미지는 가족성·화풍·인간 비율을 참고하는 자료이며 정확한 머리·체형·외피를 고정하는 템플릿이 아닙니다.
인간형 7~8등신, 목·어깨·갈비흉부·허리·골반·상하완·긴 허벅지/종아리를 유지합니다.
의례체이므로 실제 사람 얼굴·눈동자·표정·자연 헤어·직업 의상은 배제합니다.
상아·밝은 금·남색 큰 면, 정돈된 선과 연보라 셀 그림자를 유지합니다.

층별 새 컨셉과 수정
1층: 기억 수집 / 감각 봉인 / 모래 분신 계측.
각기 다른 관측 봉인머리·흉부 외피·손바닥 및 팔/허리 계측 기능을 갖춥니다. 몸통 범위의 온전한 모래시계와 내부 모래를 유지합니다.
2층: 기존 다면체 머리·V흉부를 세 번 반복하지 않습니다.
장송 연주와 강제 춤을 유지하며 외부 울림관 / 음판 / 공명종추로 머리·흉부와 음향 기능을 다르게 설계합니다.
3층: 수은 맥박 성유물 / 증류 응급치유 도구 / 수은 생명저울.
명확한 수은 성배·리저버·증류병·응급도구·플라스크 저울·마노미터 등 소수 큰 아티팩트를 치유/혈관/역류 기능과 연결합니다. 액면 아래는 불투명 은회색, 위는 투명 빈 유리입니다.
4층: 어깨 뒤의 거대한 가로축·롤러·지지기둥을 삭제합니다.
흉복부 갑주와 팔/허벅지 보호판을 두껍게, 장갑은 큰 헤비 건틀릿으로 강화합니다. 인간형 허리·골반·긴 다리, 직사각 계열 방패와 여덟 사슬을 유지합니다.
5층: 서로 다른 봉인 투구·흉갑·어깨 하중 외피.
한어깨의 작은 천구, 원본 긴 시침 창의 양끝과 두손 파지를 유지하며 중력 버팀·낙하 저지·구속 절단 준비 기능을 달리합니다.

INDEX.html에서 각 층 세 안과 가족성 참고 원본 정보를 비교할 수 있습니다.
가족성 참고 링크는 기존 프로젝트 파일을 가리킵니다. ZIP만 분리해 열면 참고 원본 링크는 연결되지 않을 수 있으나 새 15장과 제작 기록은 ZIP 내부에 포함됩니다.
다섯 층을 병렬 진행했습니다: 담당 에이전트 세 층과 루트가 독립 조율한 두 층 작업입니다.
제작: 내장 image_gen. 새 이미지 15장을 실제 생성하고 외형 차이·세부 컨셉·부품 연결·전신 여백을 시각 검수했습니다.
각 Stage 폴더에 정확 생성·보정 프롬프트와 참조·출처·검수 manifest.json이 있습니다.
prompts-all.txt는 전체 정확 프롬프트 모음입니다. 루트 manifest.json에 15장 크기·용량·SHA256과 상대 경로를 기록했습니다.
단일 전신 컨셉샷이며 실제 동일 3D 메시의 회전 렌더, UV, 리깅, 애니메이션 검증은 아닙니다.
기존 원본과 이전 시안은 보존했습니다.
'@
[IO.File]::WriteAllText((Join-Path $assetRoot 'README.txt'), $readme, $utf8)
$buildArchive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
  foreach ($file in (Get-ChildItem -LiteralPath $assetRoot -Recurse -File | Sort-Object FullName)) {
    $entryPath = 'DistinctStageEffigies_v6/' + $file.FullName.Substring($assetRoot.Length + 1).Replace('\','/')
    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($buildArchive, $file.FullName, $entryPath, [IO.Compression.CompressionLevel]::Optimal)
  }
} finally { $buildArchive.Dispose() }
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
  $pngEntries = @($archive.Entries | Where-Object { $_.FullName.EndsWith('.png') })
  if ($pngEntries.Count -ne 15) { throw "ZIP PNG count mismatch: $($pngEntries.Count)" }
  foreach ($asset in $allImages) {
    $entryName = 'DistinctStageEffigies_v6/' + $asset.image
    $entry = $archive.GetEntry($entryName)
    if (-not $entry) { throw "Missing ZIP asset: $entryName" }
    $stream = $entry.Open()
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $entryHash = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-','').ToLowerInvariant() } finally { $stream.Dispose(); $hasher.Dispose() }
    if ($entryHash -ne $asset.sha256) { throw "ZIP hash mismatch: $entryName" }
  }
  $zipEntries = $archive.Entries.Count
} finally { $archive.Dispose() }
[pscustomobject]@{status='100% complete';stages=5;final_pngs=15;concept_family='visually distinct humanoid sub-concepts';dimensions='1536x1024';png_hashes_verified=15;zip_png_hashes_verified=15;zip_entries=$zipEntries;zip=$zipPath;zip_bytes=(Get-Item -LiteralPath $zipPath).Length;gallery=(Join-Path $assetRoot 'INDEX.html');prompts=(Join-Path $assetRoot 'prompts-all.txt');manifest=(Join-Path $assetRoot 'manifest.json')} | ConvertTo-Json
