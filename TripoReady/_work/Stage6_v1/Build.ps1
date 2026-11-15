param([switch]$RequireComplete)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$shared=Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\Stage1_v2\Build-Sheets.ps1') -Encoding UTF8 -Raw
$match=[regex]::Match($shared,"(?s)-TypeDefinition @'\r?\n(.*?)\r?\n'@")
if(-not $match.Success) { throw 'Drawing helper unavailable' }
$helperPath=Join-Path $PSScriptRoot 'SheetArt.cs'
$match.Groups[1].Value | Set-Content -LiteralPath $helperPath -Encoding UTF8
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition $match.Groups[1].Value
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$package=Join-Path $workspace 'TripoReady\Stage6_v1'
$spec=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'spec.json') -Encoding UTF8 -Raw | ConvertFrom-Json
$views=@('front','left','right','back')
function Read-Record($id,$view) {
 $path=Join-Path $PSScriptRoot ($id+'-'+$view+'.json')
 $corrected=Join-Path $PSScriptRoot ($id+'-'+$view+'-corrected.json')
 if(Test-Path -LiteralPath $corrected){$path=$corrected}
 if(-not(Test-Path -LiteralPath $path)) { if($RequireComplete){throw "Missing $id $view"};return $null }
 return (Get-Content -LiteralPath $path -Encoding UTF8 -Raw | ConvertFrom-Json)
}
function Normalize-One($id,$view,$folder,$height,$baseline) {
 $rec=Read-Record $id $view
 if($null -eq $rec){return}
 $dir=Join-Path $package $folder
 New-Item -ItemType Directory -Force -Path $dir | Out-Null
 $rawDir=Join-Path $PSScriptRoot ('Sources\'+$id)
 New-Item -ItemType Directory -Force -Path $rawDir | Out-Null
 Copy-Item -LiteralPath $rec.source -Destination (Join-Path $rawDir ($view+'.png')) -Force
 $subject=[SheetArt]::Read($rec.source)
 $canvas=[SheetArt]::Canvas(2048,1600);$g=[SheetArt]::Painter($canvas)
 $rect=[SheetArt]::Place($g,$subject,1024,$baseline,$height)
 if($rect.Left -lt 40 -or $rect.Right -gt 2008){throw "Clipped $id $view $rect"}
 $g.Dispose();$subject.Dispose()
 $canvas.Save((Join-Path $dir ($view+'.png')),[Drawing.Imaging.ImageFormat]::Png);$canvas.Dispose()
}
foreach($b in $spec.bodies){foreach($v in $views){Normalize-One $b.id $v $b.folder 1250 1460}}
foreach($b in $spec.buildings){foreach($v in $views){Normalize-One $b.id $v $b.folder $b.height $b.baseline}}
$placements=@()
foreach($v in $views){
 $complete=$true
 foreach($slot in $spec.slots){if($null -eq (Read-Record $slot.id $v)){$complete=$false}}
 if(-not $complete){continue}
 $canvas=[SheetArt]::Canvas(3200,2400);$g=[SheetArt]::Painter($canvas)
 foreach($slot in $spec.slots){
  $rec=Read-Record $slot.id $v
  $rawDir=Join-Path $PSScriptRoot ('Sources\'+$slot.id)
  New-Item -ItemType Directory -Force -Path $rawDir | Out-Null
  Copy-Item -LiteralPath $rec.source -Destination (Join-Path $rawDir ($v+'.png')) -Force
  $subject=[SheetArt]::Read($rec.source)
  $rect=[SheetArt]::Place($g,$subject,$slot.centerX,$slot.baseline,$slot.height)
  if($rect.Left -lt $slot.minX -or $rect.Right -gt $slot.maxX){throw "Outside fixed slot $($slot.id) $v $rect"}
  $placements += [ordered]@{item=$slot.id;view=$v;centerX=$slot.centerX;baseline=$slot.baseline;height=$slot.height;bounds=@($rect.X,$rect.Y,$rect.Width,$rect.Height)}
  $subject.Dispose()
 }
 $g.Dispose();$canvas.Save((Join-Path $package ('Accessories\'+$v+'.png')),[Drawing.Imaging.ImageFormat]::Png);$canvas.Dispose()
}
[ordered]@{canvas=@{width=3200;height=2400;background='#E8E8E8'};method='Fixed bounding-box centerX, fixed per-item height, fixed baseline. Each view retains slot order. No whole-sheet mirroring.';slots=$spec.slots;placements=$placements} | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $package 'accessory-layout.json') -Encoding UTF8
Write-Output "Built available Stage6 groups; accessory placements=$($placements.Count)."
