param([switch]$SkipBuildings)
$ErrorActionPreference='Stop'
$stage1Helper=Join-Path $PSScriptRoot '..\Stage1_v2\Build-Sheets.ps1'
$helperText=Get-Content -LiteralPath $stage1Helper -Encoding UTF8 -Raw
$helperEnd=$helperText.IndexOf('$workspace =')
if($helperEnd -lt 1) { throw 'Missing reviewed drawing helper.' }
$helperInit=$helperText.Substring(0,$helperEnd).Replace('param([switch]$AccessoriesOnly)','')
Invoke-Expression $helperInit
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$package=Join-Path $workspace 'TripoReady\Stage4_v1'
$sources=Join-Path $PSScriptRoot 'Sources'
$views=@('front','left','right','back')
$slots=@(
  @{id='AdditionalArm';source='arm';centerX=450;height=1400;minX=70;maxX=850},
  @{id='CrossController';source='controller';centerX=1400;height=380;minX=950;maxX=1850},
  @{id='BackReelFrame';source='frame';centerX=2520;height=1080;minX=1890;maxX=3160}
)
$placements=@()
foreach($view in $views) {
 $canvas=[SheetArt]::Canvas(3200,1800);$painter=[SheetArt]::Painter($canvas)
 foreach($slot in $slots) {
  $source=Join-Path $sources ($slot.source+'-'+$view+'.png')
  $subject=[SheetArt]::Read($source)
  $rect=[SheetArt]::Place($painter,$subject,$slot.centerX,1600,$slot.height)
  if($rect.Left -lt $slot.minX -or $rect.Right -gt $slot.maxX) { throw "Accessory leaves fixed slot: $($slot.id) $view $rect" }
  $placements += [ordered]@{item=$slot.id;view=$view;centerX=$slot.centerX;height=$slot.height;baseline=1600;x=$rect.X;y=$rect.Y;width=$rect.Width;sourceBounds=@($subject.Bounds.X,$subject.Bounds.Y,$subject.Bounds.Width,$subject.Bounds.Height)}
  $subject.Dispose()
 }
 $painter.Dispose();$canvas.Save((Join-Path $package ('Accessories\'+$view+'.png')),[Drawing.Imaging.ImageFormat]::Png);$canvas.Dispose()
}
$layout=[ordered]@{canvas=@{width=3200;height=1800;background='#E8E8E8'};method='Fixed bounding-box center X, bottom Y and per-item rendered height across four camera views; no whole-sheet mirroring.';slotOrder=@('AdditionalArm','CrossController','BackReelFrame');slots=$slots;placements=$placements}
$layout | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $package 'accessory-layout.json') -Encoding UTF8
foreach($mode in @('Normal','Hard')) {
 foreach($view in $views) {
  $subject=[SheetArt]::Read((Join-Path $sources ('body-'+$mode.ToLower()+'-'+$view+'.png')))
  $canvas=[SheetArt]::Canvas(2048,1600);$painter=[SheetArt]::Painter($canvas)
  $rect=[SheetArt]::Place($painter,$subject,1024,1460,1300)
  if($rect.Left -lt 60 -or $rect.Right -gt 1988) { throw "Body clips: $mode $view $rect" }
  $painter.Dispose();$subject.Dispose();$canvas.Save((Join-Path $package ('Bodies\'+$mode+'\'+$view+'.png')),[Drawing.Imaging.ImageFormat]::Png);$canvas.Dispose()
 }
}
if(-not $SkipBuildings) {
 foreach($module in @(@{id='PuppetStage';source='stage'},@{id='AudienceBox';source='box'},@{id='FlyRig';source='rig'})) {
  foreach($view in $views) {
   $subject=[SheetArt]::Read((Join-Path $sources ('building-'+$module.source+'-'+$view+'.png')))
   $canvas=[SheetArt]::Canvas(2400,1600);$painter=[SheetArt]::Painter($canvas)
   $rect=[SheetArt]::Place($painter,$subject,1200,1460,1300)
   if($rect.Left -lt 60 -or $rect.Right -gt 2340) { throw "Building clips: $($module.id) $view $rect" }
   $painter.Dispose();$subject.Dispose();$canvas.Save((Join-Path $package ('Buildings\'+$module.id+'\'+$view+'.png')),[Drawing.Imaging.ImageFormat]::Png);$canvas.Dispose()
  }
 }
}
Copy-Item -LiteralPath (Join-Path $sources 'floor-top.png') -Destination (Join-Path $package 'Floor\top.png') -Force
Write-Output 'Rendered body and fixed accessory sheets. Building renders included unless skipped.'
