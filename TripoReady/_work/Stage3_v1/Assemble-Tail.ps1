$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$package=Join-Path $workspace 'TripoReady\Stage3_v1'
$sourceDir=Join-Path $PSScriptRoot 'Sources'
$views=@('front','left','right','back')
$groups=@(
 @{key='normal';folder='Bodies\Normal';canvasW=2048;canvasH=1800;centerX=1024;baseline=1650;height=1500},
 @{key='hard';folder='Bodies\Hard';canvasW=2048;canvasH=1800;centerX=1024;baseline=1650;height=1500},
 @{key='belfryarch';folder='Buildings\BelfryArch';canvasW=1600;canvasH=1600;centerX=800;baseline=1460;height=1320},
 @{key='railing';folder='Buildings\Railing';canvasW=1600;canvasH=1600;centerX=800;baseline=1100;height=420},
 @{key='windingstation';folder='Buildings\WindingStation';canvasW=1600;canvasH=1600;centerX=800;baseline=1380;height=1000}
)
$placed=@()
foreach($group in $groups) {
 foreach($view in $views) {
  $key=$group.key+'-'+$view
  if($key -eq 'hard-front'){$key='hard-front-fixed'}
  $src=Join-Path $sourceDir ($key+'.png')
  $subject=[SheetArt]::Read($src)
  $canvas=[SheetArt]::Canvas($group.canvasW,$group.canvasH)
  $painter=[SheetArt]::Painter($canvas)
  $rect=[SheetArt]::Place($painter,$subject,$group.centerX,$group.baseline,$group.height)
  if($rect.Left -lt 40 -or $rect.Right -gt $group.canvasW-40 -or $rect.Top -lt 40){throw "Asset clipped: $key $rect"}
  $placed += [ordered]@{group=$group.key;view=$view;canvas=@($group.canvasW,$group.canvasH);centerX=$group.centerX;baseline=$group.baseline;height=$group.height;rect=@($rect.X,$rect.Y,$rect.Width,$rect.Height);source=$key+'.png'}
  $painter.Dispose();$subject.Dispose()
  $canvas.Save((Join-Path $package ($group.folder+'\'+$view+'.png')),[Drawing.Imaging.ImageFormat]::Png)
  $canvas.Dispose()
 }
}
$slots=@(
 @{id='cloak-normal';centerX=400;height=1050;minX=30;maxX=770},
 @{id='cloak-hard';centerX=1170;height=1050;minX=800;maxX=1540},
 @{id='clapper';centerX=1870;height=1250;minX=1650;maxX=2090},
 @{id='drive';centerX=2480;height=650;minX=2120;maxX=2840},
 @{id='chain';centerX=3340;height=500;minX=2910;maxX=3770}
)
$accessoryPlacements=@()
foreach($view in $views) {
 $canvas=[SheetArt]::Canvas(3840,1800);$painter=[SheetArt]::Painter($canvas)
 foreach($slot in $slots) {
  $sourceKey=$slot.id+'-'+$view
  if($sourceKey -eq 'drive-right'){$sourceKey='drive-right-fixed'}
  $src=Join-Path $sourceDir ($sourceKey+'.png')
  $subject=[SheetArt]::Read($src)
  $rect=[SheetArt]::Place($painter,$subject,$slot.centerX,1600,$slot.height)
  if($rect.Left -lt $slot.minX -or $rect.Right -gt $slot.maxX){throw "Accessory outside fixed slot: $($slot.id) $view $rect"}
  $accessoryPlacements += [ordered]@{item=$slot.id;view=$view;centerX=$slot.centerX;baseline=1600;height=$slot.height;x=$rect.X;y=$rect.Y;width=$rect.Width;sourceBounds=@($subject.Bounds.X,$subject.Bounds.Y,$subject.Bounds.Width,$subject.Bounds.Height)}
  $subject.Dispose()
 }
 $painter.Dispose()
 $canvas.Save((Join-Path $package ('Accessories\'+$view+'.png')),[Drawing.Imaging.ImageFormat]::Png);$canvas.Dispose()
}
[ordered]@{canvas=@{width=3840;height=1800;background='#E8E8E8'};slotOrder=@('cloak-normal','cloak-hard','clapper','drive','chain');method='Deterministic slot placement. Same per-item bounding-box horizontal center, bottom baseline and fixed height in each view. No whole-sheet mirroring.';slots=$slots;placements=$accessoryPlacements}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $package 'accessory-layout.json') -Encoding UTF8
$placed|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $package 'image-alignment.json') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $sourceDir 'floor-top.png') -Destination (Join-Path $package 'Floor\top.png') -Force
Write-Output 'Created Stage3: 8 boss bodies, 4 fixed accessory sheets, 12 architectural views, 1 floor.'

