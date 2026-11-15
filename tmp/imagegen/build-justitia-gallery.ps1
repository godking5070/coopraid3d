$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$taskRoot=(Resolve-Path -LiteralPath 'ConceptArt\JustitiaHardEvolution_v1').Path
$utf8=[System.Text.UTF8Encoding]::new($false)
$display=Get-Content -LiteralPath (Join-Path $taskRoot 'display-notes.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$images=@();$promptBundle=[System.Text.StringBuilder]::new();$localHashes=0
foreach($tier in 1..5){
 $levelName='Level{0:D2}' -f $tier
 $levelRoot=Join-Path $taskRoot $levelName
 $levelManifest=Get-Content -LiteralPath (Join-Path $levelRoot 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
 $pngFiles=@(Get-ChildItem -LiteralPath $levelRoot -File -Filter '*.png')
 if($pngFiles.Count -ne 3 -or @($levelManifest.images).Count -ne 3){throw ('Expected3finalPNGs '+$levelName)}
 $levelPrompts=[System.Text.StringBuilder]::new()
 foreach($item in @($levelManifest.images | Sort-Object lineage)){
  $imagePath=Join-Path $levelRoot $item.image
  $hash=(Get-FileHash -LiteralPath $imagePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if($hash -ne $item.sha256){throw ('Localhash mismatch '+$imagePath)}
  $picture=[System.Drawing.Image]::FromFile($imagePath)
  try{if($picture.Width -ne 1536 -or $picture.Height -ne 1024){throw 'Wrong final dimensions'}}finally{$picture.Dispose()}
  $localHashes++
  $copy=$item | ConvertTo-Json -Depth 20 | ConvertFrom-Json
  $copy.image=$levelName+'/'+$item.image
  $copy.prompts=@($item.prompts | ForEach-Object {$levelName+'/'+$_})
  $familyIndex=@('A','B','C').IndexOf([string]$item.lineage)
  $copy | Add-Member -NotePropertyName notes_ko -NotePropertyValue $display.notes[$tier-1][$familyIndex] -Force
  $copy | Add-Member -NotePropertyName manifest -NotePropertyValue ($levelName+'/manifest.json') -Force
  $images+=$copy
  foreach($promptName in $item.prompts){
   $promptText=[System.IO.File]::ReadAllText((Join-Path $levelRoot $promptName),[System.Text.Encoding]::UTF8)
   [void]$levelPrompts.AppendLine(('=== {0}/{1} ===' -f $levelName,$promptName))
   [void]$levelPrompts.AppendLine($promptText)
   [void]$levelPrompts.AppendLine()
  }
 }
 [System.IO.File]::WriteAllText((Join-Path $levelRoot 'prompts-all.txt'),$levelPrompts.ToString(),$utf8)
 [void]$promptBundle.Append($levelPrompts.ToString())
}
if($images.Count -ne 15){throw 'Expected15finalconcepts'}
$sourceRoot=(Resolve-Path -LiteralPath 'ConceptArt\CharacterMatched\Stage5').Path
$sourceMeta=Get-Content -LiteralPath (Join-Path $sourceRoot 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
foreach($name in @('01-justitia-normal-v1.png','02-justitia-hard-v1.png')){
 $expected=@($sourceMeta.images | Where-Object {$_.file -eq $name})[0].sha256
 $actual=(Get-FileHash -LiteralPath (Join-Path $sourceRoot $name) -Algorithm SHA256).Hash.ToLowerInvariant()
 if($actual -ne $expected){throw 'Original source modified unexpectedly'}
}
function Get-JpegDataUri([string]$SourcePath,[int]$Width,[long]$Quality){
 $picture=[System.Drawing.Image]::FromFile($SourcePath)
 try{
  $height=[int][Math]::Round($Width*$picture.Height/[double]$picture.Width)
  $bitmap=[System.Drawing.Bitmap]::new($Width,$height)
  $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
  try{$graphics.Clear([System.Drawing.ColorTranslator]::FromHtml('#ede7f0'));$graphics.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic;$graphics.PixelOffsetMode=[System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality;$graphics.DrawImage($picture,0,0,$Width,$height)}finally{$graphics.Dispose()}
  $encoder=[System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object {$_.MimeType -eq 'image/jpeg'} | Select-Object -First 1
  $parameters=[System.Drawing.Imaging.EncoderParameters]::new(1);$parameters.Param[0]=[System.Drawing.Imaging.EncoderParameter]::new([System.Drawing.Imaging.Encoder]::Quality,$Quality)
  $stream=[System.IO.MemoryStream]::new()
  try{$bitmap.Save($stream,$encoder,$parameters);return 'data:image/jpeg;base64,'+[Convert]::ToBase64String($stream.ToArray())}finally{$stream.Dispose();$parameters.Dispose();$bitmap.Dispose()}
 }finally{$picture.Dispose()}
}
$referenceRoot=Join-Path $taskRoot 'References'
New-Item -ItemType Directory -Path $referenceRoot -Force | Out-Null
foreach($reference in @(@{source='01-justitia-normal-v1.png';target='normal-reference.jpg'},@{source='02-justitia-hard-v1.png';target='old-hard-reference.jpg'})){
 $uri=Get-JpegDataUri (Join-Path $sourceRoot $reference.source) 1024 80
 [System.IO.File]::WriteAllBytes((Join-Path $referenceRoot $reference.target),[Convert]::FromBase64String($uri.Substring($uri.IndexOf(',')+1)))
}
$promptCount=@(Get-ChildItem -LiteralPath $taskRoot -Recurse -File -Filter '*.prompt.txt').Count
if($promptCount -ne 20){throw ('Expected20exactinitial/correctionprompts,found '+$promptCount)}
$manifest=[pscustomobject]@{
 project='ASTELLA';part=1;stage=5;difficulty='hard';version='JustitiaHardEvolution_v1';created_date='2026-11-14';status='all_images_visual_qa_and_file_checks_complete_pending_gallery_package_audit'
 objective='Part1JustitiaHard larger volume,bizarre and grotesque;5visualmutationtiers,3conceptseach'
 tool='built-in image_gen';isInline=$false;tier_count=5;concepts_per_tier=3;final_image_count=15;generation_calls=20;exact_prompt_files=20
 tiers=$display.tiers;families=$display.families;references=[pscustomobject]@{normal='References/normal-reference.jpg';hard='References/old-hard-reference.jpg'}
 source_originals=@('../CharacterMatched/Stage5/01-justitia-normal-v1.png','../CharacterMatched/Stage5/02-justitia-hard-v1.png')
 gallery='INDEX.html';prompts='prompts-all.txt';images=$images
 progression_audit='A:bulk and asymmetrical arm/load increase,mask progressively dwarfed. B:eye/mask opening,head recess,giant torso void/jaw-door mutation. C:throne/coffin body thickens,4support legs at3,low horizontal creature at4,huge4leg tribunal at5. Every final visually reviewed against original and prior same-lineage tier.'
 image_qa='All15completebosses visually inspected;one sword,two pans,blindfold motif and cel palette. A/B2arms2legs. Ctiers1-2twoarms2legs,tiers3-5twoarms4clear support legs. Tier1framing3corrections,tier4Canatomyredesign,tier5Cframingcorrection completed.'
 file_qa='All15PNGdecoded1536x1024andSHA256matchedtiermanifests. Originalnormal/hardhashesmatchedandremainunchanged.'
 limitations='Visual art progression,not measured mesh volume or verified3Drigs;all15newconcepts pendinguserselection. Previous preferred gallery and fixed selections preserved.'
}
[System.IO.File]::WriteAllText((Join-Path $taskRoot 'manifest.json'),($manifest | ConvertTo-Json -Depth 24),$utf8)
[System.IO.File]::WriteAllText((Join-Path $taskRoot 'prompts-all.txt'),$promptBundle.ToString(),$utf8)
$template=[System.IO.File]::ReadAllText((Resolve-Path -LiteralPath 'tmp\gallery\justitia-evolution-template.html').Path,[System.Text.Encoding]::UTF8)
$json=$manifest | ConvertTo-Json -Depth 24 -Compress
$pageHtml=$template.Replace('__JUSTITIA_DATA__',$json)
$pagePath=Join-Path $taskRoot 'INDEX.html'
[System.IO.File]::WriteAllText($pagePath,$pageHtml,$utf8)
$script=[regex]::Match($pageHtml,'(?s)<script>(.*?)</script>').Groups[1].Value
[System.IO.File]::WriteAllText((Join-Path (Resolve-Path -LiteralPath 'tmp\gallery').Path 'justitia-gallery-check.js'),$script,$utf8)
$inlineName=$null;$inlineBytes=0
if($env:ORCA_CHAT_SCRATCH_DIR){
 foreach($width in @(320,280,240,200)){
  $inline=$json | ConvertFrom-Json;$inline.isInline=$true
  foreach($item in $inline.images){$item.image=Get-JpegDataUri (Join-Path $taskRoot $item.image) $width 60}
  $inline.references.normal=Get-JpegDataUri (Join-Path $referenceRoot 'normal-reference.jpg') 420 60
  $inline.references.hard=Get-JpegDataUri (Join-Path $referenceRoot 'old-hard-reference.jpg') 420 60
  $inlineTemplate=[regex]::Replace($template,'(?s)<!--PALETTE_START-->.*?<!--PALETTE_END-->','')
  $inlineHtml=$inlineTemplate.Replace('__JUSTITIA_DATA__',($inline | ConvertTo-Json -Depth 24 -Compress))
  $inlineBytes=$utf8.GetByteCount($inlineHtml)
  if($inlineBytes -lt 500000){break}
 }
 if($inlineBytes -ge 524288){throw 'Inlinevisualexceeds512KB'}
 $inlineName='justitia-hard-evolution-'+[guid]::NewGuid().ToString('N').Substring(0,8)+'.html'
 [System.IO.File]::WriteAllText((Join-Path $env:ORCA_CHAT_SCRATCH_DIR $inlineName),$inlineHtml,$utf8)
}
$readme=@'
ASTELLA Part1 Stage05 · 유스티티아 하드 변이15컨셉
5단계,단계별3개(A/B/C),총15개 신규 전신 컨셉.
A 불균형 거신 / B 공허의 판결체 / C 재판소 융합체.
INDEX.html에서5단계×3열로 비교하고,계열 필터로 같은 방향의1→5단계를 볼 수 있습니다.
Level01~05마다 최종PNG3장,정확한 초기/보정프롬프트와출처/참조/검수/SHA256manifest가 있습니다.
prompts-all.txt는15개 초기생성과5개 필요한보정의 정확한요청문 전체입니다.
References의JPEG2장은기존노멀/하드비교미리보기이며신규15PNG에포함하지않습니다.
사용자제공기획의천칭·눈가리개·대검모티브와아이보리/남색/금색/라벤더셀화풍을유지하면서몸의부피와괴이한구조를단계적으로강화했습니다.
기존노멀·하드PNG,기존선호갤러리,2부확정파일은변경하지않았습니다.
이미지는신규아트제안이며실제3D메시·리깅검증은포함하지않습니다. 최종선택은아직없습니다.
'@
[System.IO.File]::WriteAllText((Join-Path $taskRoot 'README.txt'),$readme,$utf8)
[pscustomobject]@{images=15;tiers=5;per_tier=3;exact_prompt_files=$promptCount;local_hashes_verified=$localHashes;page=$pagePath;inline_file=$inlineName;inline_bytes=$inlineBytes;status='gallery_built_pending_final_audit'} | ConvertTo-Json -Compress

