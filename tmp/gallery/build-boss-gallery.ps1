$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$galleryRoot=(Resolve-Path -LiteralPath 'ConceptArt\BossSelectionGallery_v1').Path
$catalogPath=Join-Path $galleryRoot 'catalog.json'
$templatePath=(Resolve-Path -LiteralPath 'tmp\gallery\boss-selection-template.html').Path
$template=[System.IO.File]::ReadAllText($templatePath,[System.Text.Encoding]::UTF8)
$catalog=Get-Content -LiteralPath $catalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
$utf8=[System.Text.UTF8Encoding]::new($false)
$allImages=@($catalog.cards | ForEach-Object {$_.images})
if($catalog.cards.Count -ne 10 -or $allImages.Count -ne 16){throw 'Expected10stages and16images'}
$verified=0
foreach($item in $allImages){
 $sourcePath=(Resolve-Path -LiteralPath (Join-Path $galleryRoot $item.src)).Path
 $image=[System.Drawing.Image]::FromFile($sourcePath)
 try{$imageWidth=$image.Width;$imageHeight=$image.Height}finally{$image.Dispose()}
 $hash=(Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
 $metadataPath=Join-Path $galleryRoot $item.sourceManifest
 $meta=Get-Content -LiteralPath $metadataPath -Raw -Encoding UTF8 | ConvertFrom-Json
 $leaf=Split-Path -Leaf $sourcePath
 $record=@($meta.images | Where-Object {$_.file -eq $leaf -or $_.image -eq $leaf})
 $expectedHash=$null
 if($record.Count -eq 1){$expectedHash=$record[0].sha256}
 elseif($meta.image -eq $leaf){$expectedHash=$meta.sha256}
 if(!$expectedHash){throw ('Missing expected source hash '+$sourcePath)}
 if($expectedHash.ToLowerInvariant() -ne $hash){throw ('Source hash mismatch '+$sourcePath)}
 $item | Add-Member -NotePropertyName width -NotePropertyValue $imageWidth -Force
 $item | Add-Member -NotePropertyName height -NotePropertyValue $imageHeight -Force
 $item | Add-Member -NotePropertyName sha256 -NotePropertyValue $hash -Force
 $verified++
}
$catalog | Add-Member -NotePropertyName validation -NotePropertyValue '16original PNGs decoded; each SHA256 matched its source manifest. Original art files not modified.' -Force
$catalog | Add-Member -NotePropertyName status -NotePropertyValue 'assembled_pending_render_review' -Force
[System.IO.File]::WriteAllText($catalogPath,($catalog | ConvertTo-Json -Depth 20),$utf8)
$projectJson=$catalog | ConvertTo-Json -Depth 20 -Compress
$pageHtml=$template.Replace('__BOSS_DATA__',$projectJson)
$pagePath=Join-Path $galleryRoot 'INDEX.html'
[System.IO.File]::WriteAllText($pagePath,$pageHtml,$utf8)
$inlineName=$null;$inlineBytes=0
if($env:ORCA_CHAT_SCRATCH_DIR){
 try{
  $scratchRoot=$env:ORCA_CHAT_SCRATCH_DIR
  if(!(Test-Path -LiteralPath $scratchRoot)){throw 'Scratch directory unavailable'}
  $jpegEncoder=[System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object {$_.MimeType -eq 'image/jpeg'} | Select-Object -First 1
  $inlineHtml=$null
  foreach($thumbWidth in @(320,280,240,200)){
   $inlineCatalog=$projectJson | ConvertFrom-Json
   $inlineCatalog.isInline=$true
   foreach($item in @($inlineCatalog.cards | ForEach-Object {$_.images})){
    $sourcePath=(Resolve-Path -LiteralPath (Join-Path $galleryRoot $item.src)).Path
    $image=[System.Drawing.Image]::FromFile($sourcePath)
    try{
     $thumbHeight=[int][Math]::Round($thumbWidth*$image.Height/[double]$image.Width)
     $thumb=[System.Drawing.Bitmap]::new($thumbWidth,$thumbHeight)
     $graphics=[System.Drawing.Graphics]::FromImage($thumb)
     try{
      $graphics.Clear([System.Drawing.ColorTranslator]::FromHtml('#edeaf1'))
      $graphics.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
      $graphics.PixelOffsetMode=[System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
      $graphics.DrawImage($image,0,0,$thumbWidth,$thumbHeight)
     }finally{$graphics.Dispose()}
     $stream=[System.IO.MemoryStream]::new()
     $parameters=[System.Drawing.Imaging.EncoderParameters]::new(1)
     $quality=[System.Drawing.Imaging.EncoderParameter]::new([System.Drawing.Imaging.Encoder]::Quality,[long]60)
     $parameters.Param[0]=$quality
     try{
      $thumb.Save($stream,$jpegEncoder,$parameters)
      $item.src='data:image/jpeg;base64,'+[Convert]::ToBase64String($stream.ToArray())
     }finally{$stream.Dispose();$parameters.Dispose();$thumb.Dispose()}
    }finally{$image.Dispose()}
   }
   $inlineJson=$inlineCatalog | ConvertTo-Json -Depth 20 -Compress
   $inlineTemplate=[regex]::Replace($template,'(?s)<!--LOCAL_PALETTE_START-->.*?<!--LOCAL_PALETTE_END-->','')
   $inlineHtml=$inlineTemplate.Replace('__BOSS_DATA__',$inlineJson)
   $inlineBytes=$utf8.GetByteCount($inlineHtml)
   if($inlineBytes -lt 500000){break}
  }
  if($inlineBytes -ge 524288){throw 'Inline page exceeds512KB'}
  $inlineName='astella-boss-selections-'+[guid]::NewGuid().ToString('N').Substring(0,8)+'.html'
  [System.IO.File]::WriteAllText((Join-Path $scratchRoot $inlineName),$inlineHtml,$utf8)
 }catch{
  $inlineName=$null
  Write-Output ('Inline preview skipped: '+$_.Exception.Message)
 }
}
$readme=@'
아스텔라 · 선호 보스 컨셉 한 페이지
INDEX.html을 열면1부1~6스테이지의 노멀/하드와2부1~4스테이지를 함께 볼 수 있습니다.
총10스테이지,16개 원본 이미지. 부 필터, 노멀/하드 전환, 이름·특징 검색, 이미지 확대를 지원합니다.
2부1층은 선호1안,3층은 폐순환 분기관 확정본,4층은 양견갑 성문 방벽 확정본입니다.
2부2층은 선호한 외형과 파이프 목을 반영한 최신 후보이며, 최종 확정으로 표시하지 않았습니다.
1부는 기존 CharacterMatched 화풍 통일/제작 시트를 사용했습니다.
원본은 기존 위치에 그대로 두고 상대경로로 연결합니다. 이 페이지를 다른 폴더로 옮기면 원본 링크가 끊어질 수 있습니다.
catalog.json에는 각 원본 경로, 선정 근거, 상태, 크기와SHA256를 기록했습니다.
새 이미지를 생성하지 않았으며 기존 PNG는 수정하지 않았습니다.
'@
[System.IO.File]::WriteAllText((Join-Path $galleryRoot 'README.txt'),$readme,$utf8)
$scriptMatch=[regex]::Match($pageHtml,'(?s)<script>(.*?)</script>')
[System.IO.File]::WriteAllText((Join-Path (Split-Path -Parent $templatePath) 'boss-gallery-check.js'),$scriptMatch.Groups[1].Value,$utf8)
[pscustomobject]@{page=$pagePath;cards=10;images=16;source_hashes_verified=$verified;inline_file=$inlineName;inline_bytes=$inlineBytes;page_bytes=(Get-Item -LiteralPath $pagePath).Length} | ConvertTo-Json -Compress

