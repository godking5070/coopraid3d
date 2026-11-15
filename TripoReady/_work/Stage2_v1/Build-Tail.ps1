
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$package=Join-Path $workspace 'TripoReady\Stage2_v1'
$sourceDir=Join-Path $PSScriptRoot 'Sources'
$views=@('front','left','right','back')
$slots=@(
 @{id='shears';centerX=450;height=1400;minX=50;maxX=950},
 @{id='spring';centerX=1775;height=1250;minX=1100;maxX=2400},
 @{id='key';centerX=3000;height=350;minX=2450;maxX=3550}
)
$placements=@()
foreach($view in $views){
 $canvas=[SheetArt]::Canvas(3600,1800);$g=[SheetArt]::Painter($canvas)
 foreach($slot in $slots){
  $subject=[SheetArt]::Read((Join-Path $sourceDir ($slot.id+'-'+$view+'.png')))
  $rect=[SheetArt]::Place($g,$subject,$slot.centerX,1600,$slot.height)
  if($rect.Left -lt $slot.minX -or $rect.Right -gt $slot.maxX){throw "Accessory overflow: $($slot.id) $view $rect"}
  $placements += [ordered]@{item=$slot.id;view=$view;centerX=$slot.centerX;baseline=1600;height=$slot.height;x=$rect.X;y=$rect.Y;width=$rect.Width}
  $subject.Dispose()
 }
 $g.Dispose();$canvas.Save((Join-Path $package ('Accessories\'+$view+'.png')),[Drawing.Imaging.ImageFormat]::Png);$canvas.Dispose()
}
[ordered]@{canvas=@{width=3600;height=1800;background='#E8E8E8'};slotOrder=@('shears','spring','key');slots=$slots;placements=$placements;method='Deterministic per-object bounding-box center X, height and baseline. Camera view changes only; complete sheet never mirrored.'} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $package 'accessory-layout.json') -Encoding UTF8
foreach($mode in @('Normal','Hard')){
 foreach($view in $views){
  $subject=[SheetArt]::Read((Join-Path $sourceDir ($mode.ToLower()+'-'+$view+'.png')))
  $canvas=[SheetArt]::Canvas(2048,1600);$g=[SheetArt]::Painter($canvas)
  $rect=[SheetArt]::Place($g,$subject,1024,1460,1300)
  if($rect.Left -lt 40 -or $rect.Right -gt 2008){throw "Body clipped: $mode $view"}
  $g.Dispose();$subject.Dispose();$canvas.Save((Join-Path $package ('Bodies\'+$mode+'\'+$view+'.png')),[Drawing.Imaging.ImageFormat]::Png);$canvas.Dispose()
 }
}
foreach($item in @(@{id='entrance';folder='EntranceBay';height=1300;baseline=1460},@{id='balcony';folder='BalconyStairs';height=600;baseline=1160},@{id='vent';folder='VentCabinet';height=1300;baseline=1460})){
 $folder=Join-Path $package ('Buildings\'+$item.folder);New-Item -ItemType Directory -Force -Path $folder | Out-Null
 foreach($view in $views){
  $subject=[SheetArt]::Read((Join-Path $sourceDir ($item.id+'-'+$view+'.png')))
  $canvas=[SheetArt]::Canvas(1600,1600);$g=[SheetArt]::Painter($canvas)
  $rect=[SheetArt]::Place($g,$subject,800,$item.baseline,$item.height)
  if($rect.Left -lt 40 -or $rect.Right -gt 1560){throw "Building clipped: $($item.id) $view $rect"}
  $g.Dispose();$subject.Dispose();$canvas.Save((Join-Path $folder ($view+'.png')),[Drawing.Imaging.ImageFormat]::Png);$canvas.Dispose()
 }
}
Copy-Item -LiteralPath (Join-Path $sourceDir 'floor-top.png') -Destination (Join-Path $package 'Floor\top.png') -Force
Write-Output 'Built 25 modeling PNGs with fixed accessory slot layout.'

