$ErrorActionPreference = 'Stop'
$assetRoot = (Resolve-Path -LiteralPath 'ConceptArt/Part2/DevelopedSelections_v7').Path
$zipPath = Join-Path (Split-Path $assetRoot -Parent) 'DevelopedSelections_v7.zip'
if (Test-Path -LiteralPath $zipPath) { throw 'Versioned zip already exists; preserve it and choose a sibling version.' }
$utf8 = New-Object System.Text.UTF8Encoding($false)
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stageTitles = @('기억을 흘리는 정찰병', '끝나지 않는 진혼곡의 무희', '얼어붙은 혈관의 연금술사', '영원히 타오르는 가마의 수호자', '무너진 천구를 떠받치는 부단장')
$expectedImages = @('01-effigy.png','02-effigy.png','03-effigy.png')

$selectionMap = @(
 [pscustomobject]@{stage=1;source='../DistinctStageEffigies_v6/Stage01/02-effigy.png';display='v6 Stage01 선택 2안';link='../DistinctStageEffigies_v6/Stage01/02-effigy.png';identity='봉인마스크·비대칭 갈비·모래시계·캘리퍼 계승, 기억/광학/분신 위상 아티팩트 추가'},
 [pscustomobject]@{stage=2;source='../DistinctStageEffigies_v6/Stage02/01-effigy.png';display='v6 Stage02 선택 1안';link='../DistinctStageEffigies_v6/Stage02/01-effigy.png';identity='타원 봉인면·파이프 목·긴 갈비 외형 유지, 옆뒤 대형 원통/행거를 컴팩트 등 공명 기관으로 교체'},
 [pscustomobject]@{stage=3;source='../DistinctStageEffigies_v6/Stage03/03-effigy.png';display='v6 Stage03 선택 3안';link='../DistinctStageEffigies_v6/Stage03/03-effigy.png';identity='원형 의료계기면·백금 베일·반달 늑골 유지, 두 플라스크 저울과 손 U자 봉 완전 교체'},
 [pscustomobject]@{stage=4;source='../DistinctStageEffigies_v6/Stage04/03-effigy.png';display='v6 Stage04 선택 3안';link='../DistinctStageEffigies_v6/Stage04/03-effigy.png';identity='쐐기 투구·중갑·대형 건틀릿 유지, 대형 방패를 어깨에 지지 결합. 가로축·롤러 부활 없음'},
 [pscustomobject]@{stage=5;source='../DistinctStageEffigies_v6/Stage05/03-effigy.png';display='v6 Stage05 선택 3안';link='../DistinctStageEffigies_v6/Stage05/03-effigy.png';identity='긴 봉인 바이저·별 흉갑·원본 시침 창 유지, 애매한 후방 행성/토성환/막대를 하중 받는 천구 성유물로 교체'}
)

$allImages = @()
$pendingStageWrites = @()
$allPrompts = New-Object System.Text.StringBuilder
$gallery = New-Object System.Text.StringBuilder
[void]$gallery.AppendLine('<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>아스텔라 제2부 · 선택안 아티팩트 심화 15안</title><style>body{margin:0;background:#171d2c;color:#f3eee7;font-family:system-ui,sans-serif}main{max-width:1600px;margin:auto;padding:32px}h1{font-size:28px}p{color:#b9c3d4;line-height:1.7}section{margin:46px 0}.grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:18px}figure{margin:0;background:#242d40;border:1px solid #38435a;border-radius:10px;overflow:hidden}img{display:block;width:100%;height:auto;aspect-ratio:3/2;object-fit:contain}figcaption{padding:16px}strong{display:block;color:#e9d299;margin-bottom:8px}a{color:#d0dfff;text-decoration:none}a:hover{text-decoration:underline}small{display:block;margin-top:12px;color:#aab7cc}@media(max-width:900px){.grid{grid-template-columns:1fr}main{padding:18px}}</style></head><body><main><h1>아스텔라 제2부 · 선택안 아티팩트 심화 15안</h1><p>사용자가 v6에서 고른 다섯 선택안의 좋아한 외형을 계승하고, 요청한 아티팩트 추가·교체·어깨 방패를 중심으로 각 세 안씩 심화했습니다. 선택안의 머리·인간형 체형·핵심 외피와 창은 유지하며 기능 성유물, 후방 구조와 결합 방식을 달리했습니다. 싫어한 장치는 작은 장식 수정으로 반복하지 않고 기능부터 교체했습니다. 이미지를 누르면 1536 × 1024 원본 PNG가 열립니다.</p>')
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
    [void]$gallery.AppendLine(('<figure><a href="{0}"><img src="{0}" alt="{1}"></a><figcaption><strong>선택안 심화 {2}</strong>{1}<small><a href="{3}">생성 프롬프트</a> · <a href="{4}">제작 기록</a></small></figcaption></figure>' -f $relativeImage, $safeTitle, ($optionNumber+1), "$stageFolder/$id.prompt.txt", "$stageFolder/manifest.json"))
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
$index = [ordered]@{project='ASTELLA';part=2;version='DevelopedSelections_v7';created_date='2026-11-12';status='generation_visual_qa_and_packaging_complete';tool='built-in image_gen';stage_count=5;concepts_per_stage=3;final_image_count=15;concept_family='Three functional artifact developments per selected v6 effigy';user_request='Develop selected Stage01 option02 with artifacts; Stage02 option01 keep pipe neck and replace disliked rear artifact; Stage03 option03 replace disliked rear balance and hand tool; Stage04 option03 add large shoulder shield; Stage05 option03 replace ambiguous rear artifact.';selected_sources=$selectionMap;reference_policy='The exact selected v6 stage image was visually inspected and supplied in each initial generation. Preferred head, humanoid proportions, core shell and Stage05 original spear retained. Disliked artifacts replaced at function and support level.';generation_schedule='Five stage jobs progressed in parallel: three assigned stage agents and two independent stage jobs orchestrated by the root.';stage_changes=@{Stage01='Add memory capsules / remote optical survey / sand-double phase anchor to selected mask, ribs, hourglass and caliper';Stage02='Keep pipe neck and selected silhouette, replace bent side pipes/large cylinders/hanger with compact crescent, spinal or layered resonance body';Stage03='Remove two-flask shoulder balance and U-shaped hand manometer entirely; replace with compact integrated mercury circuits and palm/wrist or small relic tools';Stage04='Keep heavy armor and enlarged gauntlet; attach large shoulder shield with clear load-bearing mount, no rear transverse shaft/roller/column';Stage05='Keep long angular visor, cracked star chest and original two-ended hour-hand spear; replace vague rear planet/Saturn rings/rods with integrated celestial load relics'};variation_scope='Three meaningful artifact/support/shell arrangements, not the same mechanism in three poses. Human anatomy, stage narrative and cel materials remain.';gallery='INDEX.html';prompts='prompts-all.txt';direction='ART_DIRECTION.txt';qa='All 15 final PNGs visually inspected by the root and/or assigned stage agents for selected-base family, requested artifact removal/replacement/addition, physical attachments and full-body framing. PNG decode, 1536x1024 dimensions, bytes and SHA256 verified. Individual manifests retain exact references, generation and correction records. Prior files preserved.';limits='Single-view concept illustrations. No 3D mesh, UV, rigging or animation verification.';images=$allImages}
[IO.File]::WriteAllText((Join-Path $assetRoot 'manifest.json'), ($index | ConvertTo-Json -Depth 20), $utf8)
$readme = @'
아스텔라 제2부 1~5스테이지 · 선택안 아티팩트 심화 15안
완료 범위: 다섯 v6 선택안 × 심화 세 안, PNG 15장, 모두 1536×1024.

사용자 선택은 바로 이전 DistinctStageEffigies_v6의 1층 2안 / 2층 1안 / 3층 3안 / 4층 3안 / 5층 3안입니다.
좋아한 봉인머리·인간형 체형·핵심 외피는 계승하고 요청한 아티팩트를 추가·교체했습니다.
각 초기 생성에 해당 층의 정확한 v6 선택 원본을 입력했습니다.
싫어한 장치를 조금만 꾸며 반복하지 않고 기능과 본체에 붙는 위치·지지·순환 구조를 재설계했습니다.
각 층 세 안은 같은 장치를 가진 포즈 복제가 아니라 서로 다른 기능 성유물·후방 구조·외피 배치입니다.
모든 안은 명확한 인간형 7~8등신, 정돈된 윤곽과 상아·금·남색 큰 면, 연보라 셀 그림자를 유지합니다.

1층 선택: ../DistinctStageEffigies_v6/Stage01/02-effigy.png
세로 겹타원 봉인마스크·상부 잠금·비대칭 갈비·가슴 모래시계·전완 캘리퍼를 유지합니다.
기억 캡슐/손바닥 인장, 원거리 광학 계측기, 분신 위상 고정기로 아티팩트 기능을 다르게 추가했습니다.
2층 선택: ../DistinctStageEffigies_v6/Stage02/01-effigy.png
좋아한 타원 봉인면·얕은 두건·오르간 파이프 목·긴 갈비 흉부와 사지를 유지합니다.
싫어한 옆뒤 구부러진 파이프 끝 큰 원통과 행거를 제거하고 컴팩트 초승 공명성유물/척추 오르골/겹공명 등판으로 교체합니다.
3층 선택: ../DistinctStageEffigies_v6/Stage03/03-effigy.png
원형 의료계기면·짧은 백금 베일·반달 늑골·긴 치유 체형을 유지합니다.
양어깨 뒤 두 플라스크 저울과 손의 U자 마노미터 봉은 완전 삭제합니다.
밀착 경맥 아치/폐순환 저장조/척추 순환판과 손바닥·손목 회로 또는 작은 밀폐 성유물로 대체합니다.
4층 선택: ../DistinctStageEffigies_v6/Stage04/03-effigy.png
쐐기 봉인투구·비대칭 중갑·대형 건틀릿을 유지하고 큰 방패를 어깨에 지지 결합합니다.
세로 견갑방패/접이식 양견갑 방벽/곡면 어깨 열차폐 방식으로 구분합니다.
어깨 뒤 가로축·롤러·지지기둥은 부활시키지 않습니다. 팔은 별도로 읽히며 여덟 사슬을 유지합니다.
5층 선택: ../DistinctStageEffigies_v6/Stage05/03-effigy.png
긴 각진 바이저·갈라진 별 흉갑·인간형 전사 비율·원본 시침 창 양끝과 두손 파지를 유지합니다.
애매한 어깨 뒤 행성공·토성환·가느다란 막대는 부서진 천구반구/낙하 천구 구속환/붕괴 성도 봉인판으로 완전 교체합니다.
천구의 하중이 견갑·척추·골반이나 창 결속부로 실제 전달되도록 소수 큰 부품으로 정리합니다.

INDEX.html에서 각 선택 원본과 세 심화안을 비교할 수 있습니다.
선택 원본 링크는 기존 프로젝트 파일을 가리킵니다. ZIP을 분리해 열면 원본 링크는 연결되지 않을 수 있으나 새 15장과 제작 기록은 ZIP 내부에 포함됩니다.
다섯 층을 병렬 진행했습니다: 담당 에이전트 세 층과 루트가 독립 조율한 두 층 작업입니다.
제작: 내장 image_gen. 모든 새 이미지를 생성하고 요청 반영·부품 연결·전신 여백을 시각 검수했습니다.
각 Stage 폴더에 정확 생성·보정 프롬프트와 참조·출처·검수 manifest.json이 있습니다.
prompts-all.txt는 전체 정확 프롬프트 모음입니다. 루트 manifest.json에 15장 크기·용량·SHA256과 상대 경로를 기록했습니다.
단일 전신 컨셉샷이며 실제 동일 3D 메시의 회전 렌더, UV, 리깅, 애니메이션 검증은 아닙니다.
기존 선택 원본과 이전 시안은 보존했습니다.
'@
[IO.File]::WriteAllText((Join-Path $assetRoot 'README.txt'), $readme, $utf8)
$buildArchive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
  foreach ($file in (Get-ChildItem -LiteralPath $assetRoot -Recurse -File | Sort-Object FullName)) {
    $entryPath = 'DevelopedSelections_v7/' + $file.FullName.Substring($assetRoot.Length + 1).Replace('\','/')
    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($buildArchive, $file.FullName, $entryPath, [IO.Compression.CompressionLevel]::Optimal)
  }
} finally { $buildArchive.Dispose() }
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
  $pngEntries = @($archive.Entries | Where-Object { $_.FullName.EndsWith('.png') })
  if ($pngEntries.Count -ne 15) { throw "ZIP PNG count mismatch: $($pngEntries.Count)" }
  foreach ($asset in $allImages) {
    $entryName = 'DevelopedSelections_v7/' + $asset.image
    $entry = $archive.GetEntry($entryName)
    if (-not $entry) { throw "Missing ZIP asset: $entryName" }
    $stream = $entry.Open()
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $entryHash = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-','').ToLowerInvariant() } finally { $stream.Dispose(); $hasher.Dispose() }
    if ($entryHash -ne $asset.sha256) { throw "ZIP hash mismatch: $entryName" }
  }
  $zipEntries = $archive.Entries.Count
} finally { $archive.Dispose() }
[pscustomobject]@{status='100% complete';stages=5;final_pngs=15;concept_family='selected-concept artifact developments';dimensions='1536x1024';png_hashes_verified=15;zip_png_hashes_verified=15;zip_entries=$zipEntries;zip=$zipPath;zip_bytes=(Get-Item -LiteralPath $zipPath).Length;gallery=(Join-Path $assetRoot 'INDEX.html');prompts=(Join-Path $assetRoot 'prompts-all.txt');manifest=(Join-Path $assetRoot 'manifest.json')} | ConvertTo-Json
