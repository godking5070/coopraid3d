$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskRoot=(Resolve-Path -LiteralPath 'ConceptArt\Part2\MajesticStage02to06_v9').Path
$taskZipPath=$taskRoot+'.zip'
if(Test-Path -LiteralPath $taskZipPath){throw 'ZIP already exists'}
$utf8=[System.Text.UTF8Encoding]::new($false)
$allImages=@()
$allPromptText=[System.Text.StringBuilder]::new()
$cards=@()
$stageDescriptions=@{
 2='파이프 목과 봉인면 유지. 큰 초승 공명판, 외부 태엽 캠과 팔 구속 장치.'
 3='의료계기 가면과 여성 체형 유지. 큰 폐순환 수은 성유물과 흉부·팔 순환계.'
 4='두꺼운 중갑과 거대 건틀릿. 견갑·허리 지지대로 연결된 대형 어깨 방패와 8사슬.'
 5='원본 시침 창과 별 흉갑 유지. 어깨·척추·골반으로 하중을 받는 부서진 천구 성도.'
 6='봉인 베일 가면, 열린 시계 심장, 여섯 초침 날개. 왼팔 칼날을 붙잡는 인간 오른손.'
}
foreach($stageNumber in 2..6){
 $stageName='Stage{0:D2}' -f $stageNumber
 $stageRoot=Join-Path $taskRoot $stageName
 $stageManifestPath=Join-Path $stageRoot 'manifest.json'
 $stageManifest=Get-Content -LiteralPath $stageManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
 if(@($stageManifest.images).Count -ne 1){throw ('Wrong image count '+$stageName)}
 $item=$stageManifest.images[0]
 $pngPath=Join-Path $stageRoot $item.image
 if(!(Test-Path -LiteralPath $pngPath)){Copy-Item -LiteralPath $item.source -Destination $pngPath}
 $picture=[System.Drawing.Image]::FromFile($pngPath)
 try{$imageWidth=$picture.Width;$imageHeight=$picture.Height}finally{$picture.Dispose()}
 if($imageWidth -ne 1536 -or $imageHeight -ne 1024){throw ('Wrong dimensions '+$stageName)}
 $imageBytes=(Get-Item -LiteralPath $pngPath).Length
 $imageHash=(Get-FileHash -LiteralPath $pngPath -Algorithm SHA256).Hash.ToLowerInvariant()
 if($item.PSObject.Properties['sha256'] -and $item.sha256 -ne $imageHash){throw ('Existing hash mismatch '+$stageName)}
 $item | Add-Member -NotePropertyName width -NotePropertyValue $imageWidth -Force
 $item | Add-Member -NotePropertyName height -NotePropertyValue $imageHeight -Force
 $item | Add-Member -NotePropertyName bytes -NotePropertyValue $imageBytes -Force
 $item | Add-Member -NotePropertyName sha256 -NotePropertyValue $imageHash -Force
 $stageManifest | Add-Member -NotePropertyName status -NotePropertyValue 'generation_visual_qa_and_file_validation_complete' -Force
 $stageManifest | Add-Member -NotePropertyName file_validation -NotePropertyValue 'One final PNG decoded at1536x1024; bytes and SHA256 verified. Final ZIP PNG hash also compared by root packaging.' -Force
 [System.IO.File]::WriteAllText($stageManifestPath,($stageManifest | ConvertTo-Json -Depth 20),$utf8)
 $stagePromptText=[System.Text.StringBuilder]::new()
 foreach($promptName in $item.prompts){
  $promptPath=Join-Path $stageRoot $promptName
  [void]$stagePromptText.AppendLine(('=== {0}/{1} ===' -f $stageName,$promptName))
  [void]$stagePromptText.AppendLine([System.IO.File]::ReadAllText($promptPath,[System.Text.Encoding]::UTF8))
  [void]$stagePromptText.AppendLine()
 }
 [System.IO.File]::WriteAllText((Join-Path $stageRoot 'prompts-all.txt'),$stagePromptText.ToString(),$utf8)
 [void]$allPromptText.Append($stagePromptText.ToString())
 $allImages+= [pscustomobject]@{
  stage=$stageNumber;title_ko=$item.title_ko;image=($stageName+'/'+$item.image);width=$imageWidth;height=$imageHeight;bytes=$imageBytes;sha256=$imageHash
  prompts=@($item.prompts | ForEach-Object {$stageName+'/'+$_});manifest=($stageName+'/manifest.json');visual_qa=$item.visual_qa
 }
 $safeTitle=[System.Net.WebUtility]::HtmlEncode([string]$item.title_ko)
 $safeDescription=[System.Net.WebUtility]::HtmlEncode([string]$stageDescriptions[$stageNumber])
 $cards+=@"
<article><a href="$stageName/$($item.image)"><img src="$stageName/$($item.image)" alt="Stage $stageNumber $safeTitle"></a><section><span class="eyebrow">STAGE $stageNumber</span><h2>$safeTitle</h2><p>$safeDescription</p><a href="$stageName/$($item.image)">원본 PNG</a> · <a href="$stageName/prompts-all.txt">프롬프트</a></section></article>
"@
}
if($allImages.Count -ne 5){throw 'Expected5 final images'}
[System.IO.File]::WriteAllText((Join-Path $taskRoot 'prompts-all.txt'),$allPromptText.ToString(),$utf8)
$rootManifest=[pscustomobject]@{
 project='ASTELLA';part=2;version='MajesticStage02to06_v9';created_date='2026-11-12';status='generation_visual_qa_and_packaging_complete'
 user_request='User likes Stage01Majesty_v8 option01 and requested one developed boss concept each for Stages02,03,04,05,06 in five parallel jobs.'
 selected_presence_reference='../Stage01Majesty_v8/01-effigy.png';tool='built-in image_gen';stage_count=5;concepts_per_stage=1;final_image_count=5
 schedule='Five overlapping stage jobs: Stage02/03/04 delegated to three stage agents; Stage05/06 generated concurrently by root.'
 reference_policy='The user-liked Stage01 was used for cel materials and major-boss presence only. Each stage retains its own selected identity and narrative motifs. No Stage01 sand/hourglass motif copied into other bosses.'
 gallery='INDEX.html';prompts='prompts-all.txt';direction='ART_DIRECTION.txt';images=$allImages
 validation='All5 images visually inspected by root and stage owners as applicable; complete bodies and major attachments checked,1536x1024 PNG decode and local SHA256 verified, final ZIP PNG entry hashes matched. Stage05 frame widened, Stage06 wrong sand background removed.'
 limits='Single-view concept illustrations. No3D mesh/UV/rigging validation. Earlier versions preserved. Five new images pending user selection.'
}
[System.IO.File]::WriteAllText((Join-Path $taskRoot 'manifest.json'),($rootManifest | ConvertTo-Json -Depth 20),$utf8)
$html=@"
<!doctype html>
<html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>ASTELLA · 2~6스테이지 위엄 강화</title>
<style>
body{margin:0;background:#171b2b;color:#f2eade;font-family:"Malgun Gothic",sans-serif}main{max-width:1650px;margin:auto;padding:36px 24px}h1{font-size:29px;line-height:1.4}p{line-height:1.7;color:#cac7d7}a{color:#edcc86}nav{display:flex;gap:24px;flex-wrap:wrap;margin:22px 0 32px}.grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:24px}article{background:#24283a;border:1px solid #47445b;border-radius:12px;overflow:hidden}article img{display:block;width:100%;height:auto}section{padding:22px}h2{font-size:20px;margin:8px 0 12px}.eyebrow{font-size:12px;letter-spacing:2px;color:#e2c585}footer{margin:30px 0;color:#aaa7b7;font-size:13px;line-height:1.7}@media(min-width:1500px){.grid{grid-template-columns:repeat(3,minmax(0,1fr))}}@media(max-width:900px){.grid{grid-template-columns:1fr}main{padding:24px 16px}}
</style><main>
<h1>2~6스테이지 · 묵직한 체급과 큰 성유물</h1>
<p>좋아하신 1스테이지 1안의 체급과 화풍을 기준으로, 각 층의 고유 장치와 서사를 유지한 전신 콘셉트입니다. 스테이지별 한 장씩, 총 다섯 장입니다.</p>
<nav><a href="../MajesticStage02to06_v9.zip">전체 ZIP</a><a href="prompts-all.txt">전체 생성·보정 프롬프트</a><a href="manifest.json">파일·검수 기록</a></nav>
<div class="grid">$($cards -join [Environment]::NewLine)</div>
<footer>내장 image_gen · 각1536×1024 PNG · 전신·장치 결합부·프레임 확인 · 기존 버전 보존.</footer></main></html>
"@
[System.IO.File]::WriteAllText((Join-Path $taskRoot 'INDEX.html'),$html,$utf8)
$readme=@'
ASTELLA Part2 · MajesticStage02to06_v9
사용자가 좋아한 Stage01Majesty_v8/01-effigy.png의 위엄과 체급을 기준으로 2~6스테이지를 각1장씩 병렬 제작했습니다.
INDEX.html에서 다섯 이미지를 비교하고 각 카드의 원본 PNG를 열 수 있습니다.
각 Stage 폴더: 01-boss.png, 정확한 생성·보정 프롬프트, prompts-all.txt, source/reference/QA/hash를 담은 manifest.json.
내장 image_gen으로 생성·필요한 보정 후 프로젝트 폴더로 복사. 모두1536x1024 PNG.
2층 파이프 목/공명·태엽,3층 수은 순환/치유,4층 어깨 방패/중갑/8사슬,5층 천구하중/원본시침창,6층6초침날개/시계심장/칼날과오른손.
기존 원본과 이전 버전은 보존했습니다. 단일 시점 콘셉트 이미지이며 실제3D 모델·리깅 검증은 포함하지 않습니다.
'@
[System.IO.File]::WriteAllText((Join-Path $taskRoot 'README.txt'),$readme,$utf8)
$archive=[System.IO.Compression.ZipFile]::Open($taskZipPath,[System.IO.Compression.ZipArchiveMode]::Create)
try{
 foreach($file in Get-ChildItem -LiteralPath $taskRoot -File -Recurse){
  $entryName=$file.FullName.Substring($taskRoot.Length+1).Replace('\','/')
  [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$entryName,[System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
 }
}finally{$archive.Dispose()}
$archive=[System.IO.Compression.ZipFile]::OpenRead($taskZipPath)
try{
 if(@($archive.Entries | Where-Object {$_.FullName -match '\.png$'}).Count -ne 5){throw 'ZIP must contain exactly5 PNG'}
 foreach($item in $allImages){
  $entry=$archive.GetEntry($item.image);if($null -eq $entry){throw ('Missing zip entry '+$item.image)}
  $entryStream=$entry.Open();$hasher=[System.Security.Cryptography.SHA256]::Create()
  try{$entryHash=([BitConverter]::ToString($hasher.ComputeHash($entryStream))).Replace('-','').ToLowerInvariant()}finally{$entryStream.Dispose();$hasher.Dispose()}
  if($entryHash -ne $item.sha256){throw ('ZIP PNG hash mismatch '+$item.image)}
 }
 $entryCount=$archive.Entries.Count
}finally{$archive.Dispose()}
[pscustomobject]@{status='100% complete';stages=5;final_pngs=5;dimensions='1536x1024';verified_zip_hashes=5;zip_entries=$entryCount;zip=$taskZipPath;zip_bytes=(Get-Item -LiteralPath $taskZipPath).Length;gallery=(Join-Path $taskRoot 'INDEX.html')} | ConvertTo-Json -Compress

