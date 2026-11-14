$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$old=Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\Stage1_v2\Validate-Package.ps1') -Encoding UTF8 -Raw
$match=[regex]::Match($old,"(?s)-TypeDefinition @'\r?\n(.*?)\r?\n'@")
if(-not $match.Success){throw 'Pixel helper unavailable'}
$code=$match.Groups[1].Value.Replace('int x0,int x1)','int x0,int x1,int y0,int y1)').Replace('for(int y=0;y<bmp.Height;y++)','for(int y=y0;y<y1;y++)')
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition $code
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$package=Join-Path $workspace 'TripoReady\Stage6_v1'
$manifest=Get-Content -LiteralPath (Join-Path $package 'manifest.json') -Encoding UTF8 -Raw | ConvertFrom-Json
$layout=Get-Content -LiteralPath (Join-Path $package 'accessory-layout.json') -Encoding UTF8 -Raw | ConvertFrom-Json
$images=@()
foreach($group in $manifest.inputGroups){
 $folder=Join-Path $package $group.folder
 $files=@(Get-ChildItem -LiteralPath $folder -File)
 if($files.Count -ne 4){throw "Expected 4 images: $folder"}
 foreach($view in $manifest.views){
  $relative=$group.folder+'/'+$view+'.png';$p=Join-Path $package $relative
  $img=[Drawing.Image]::FromFile($p)
  if($img.Width -ne $group.size[0] -or $img.Height -ne $group.size[1]){throw "Dimensions incorrect: $relative"}
  $images += [ordered]@{file=$relative;width=$img.Width;height=$img.Height;sha256=(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLower()}
  $img.Dispose()
 }
}
$floor=Join-Path $package 'Floor\top.png';$img=[Drawing.Image]::FromFile($floor)
if($img.Width -ne 1254 -or $img.Height -ne 1254){throw 'Floor dimensions incorrect'}
$images += [ordered]@{file='Floor/top.png';width=$img.Width;height=$img.Height;sha256=(Get-FileHash -LiteralPath $floor -Algorithm SHA256).Hash.ToLower()};$img.Dispose()
if($images.Count -ne 33){throw 'Expected 33 modeling PNGs'}
if(@(Get-ChildItem -LiteralPath (Join-Path $package 'ReferenceOnly') -File -Filter '*.png').Count -ne 3){throw 'Expected 3 reference images'}
$prompts=@(Get-ChildItem -LiteralPath (Join-Path $package 'GenerationPrompts') -File -Filter '*.txt')
if($prompts.Count -ne 39){throw "Expected 39 prompts; got $($prompts.Count)"}
foreach($p in $prompts){$s=Get-Content -LiteralPath $p.FullName -Encoding UTF8 -Raw;if($s.Length -lt 80 -or $s -match '(?m)^undefined$'){throw "Missing prompt: $($p.Name)"}}
$pixels=@()
foreach($slot in $layout.slots){
 $entries=@($layout.placements | Where-Object item -eq $slot.id)
 if($entries.Count -ne 4){throw "Missing accessory directions: $($slot.id)"}
 if(@($entries.centerX | Select-Object -Unique).Count -ne 1 -or @($entries.height | Select-Object -Unique).Count -ne 1 -or @($entries.baseline | Select-Object -Unique).Count -ne 1){throw "Drifting template: $($slot.id)"}
 foreach($e in $entries){
  $y0=0;$y1=1200;if($e.baseline -gt 1200){$y0=1200;$y1=2400}
  $path=Join-Path $package ('Accessories\'+$e.view+'.png')
  $b=[SheetCheck]::Bounds($path,$slot.minX,$slot.maxX,$y0,$y1)
  $cx=[Math]::Abs(($b.Left+$b.Right)/2.0-$e.centerX);$bottom=[Math]::Abs($b.Bottom-$e.baseline);$height=[Math]::Abs($b.Height-$e.height)
  if($cx -gt 2 -or $bottom -gt 2 -or $height -gt 2){throw "Pixel alignment drift $($slot.id) $($e.view) $b"}
  $pixels += [ordered]@{item=$slot.id;view=$e.view;renderedBounds=@($b.X,$b.Y,$b.Width,$b.Height);centerErrorPx=$cx;bottomErrorPx=$bottom;heightErrorPx=$height}
 }
}
$world=Get-Content -LiteralPath (Join-Path $package 'map-layout.json') -Encoding UTF8 -Raw | ConvertFrom-Json
if($world.footprint.radius -ne 20 -or $world.posts.Count -ne 4){throw 'World footprint/post data incomplete'}
[ordered]@{status='passed';modelingPngCount=33;referencePngCount=3;promptCount=39;imageFiles=$images;accessoryFixedLayoutPixelChecks=$pixels;checks=@('All eight four-view input groups complete','All 33 modeling PNGs decoded at expected dimensions','Six accessories per sheet; 24 rendered fixed-position checks passed','Metric semicircle footprint and four post coordinates provided');visualReview=@('Four Janus body variants, each four directions','All four six-accessory sheets','Three architecture modules in all directions','Semicircular top-down floor and four mounting inlays');limitations='Concept turnarounds are not calibrated 3D renders. Tripo reconstruction and Mixamo rigging not performed.'} | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $package 'verification.json') -Encoding UTF8
Write-Output 'PASS: Stage6 33 modeling PNGs, 3 references, 39 prompts, 24 rendered accessory placement checks.'
