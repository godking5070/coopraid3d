$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$spec=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'spec.json') -Encoding UTF8 -Raw | ConvertFrom-Json
foreach($view in @('left','right','back')) {
 $sheet=Get-Content -LiteralPath (Join-Path $PSScriptRoot ('AccessorySheet-'+$view+'.json')) -Encoding UTF8 -Raw | ConvertFrom-Json
 $image=[Drawing.Bitmap]::FromFile($sheet.source)
 for($i=0;$i -lt 6;$i++) {
  $col=$i%3;$row=[int][Math]::Floor($i/3)
  $x0=[int][Math]::Floor($col*$image.Width/3.0);$x1=[int][Math]::Floor(($col+1)*$image.Width/3.0)
  $y0=[int][Math]::Floor($row*$image.Height/2.0);$y1=[int][Math]::Floor(($row+1)*$image.Height/2.0)
  $rect=New-Object Drawing.Rectangle($x0,$y0,($x1-$x0),($y1-$y0))
  $tile=$image.Clone($rect,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $dir=Join-Path $PSScriptRoot 'SheetTiles'
  New-Item -ItemType Directory -Force -Path $dir | Out-Null
  $tilePath=Join-Path $dir ($spec.slots[$i].id+'-'+$view+'.png')
  $tile.Save($tilePath,[Drawing.Imaging.ImageFormat]::Png);$tile.Dispose()
  [ordered]@{id=$spec.slots[$i].id;view=$view;source=$tilePath;prompt=$sheet.prompt;reference=$sheet.reference;generatedSheet=$sheet.source;crop=@($x0,$y0,($x1-$x0),($y1-$y0))} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $PSScriptRoot ($spec.slots[$i].id+'-'+$view+'.json')) -Encoding UTF8
 }
 $image.Dispose()
}
Write-Output 'Split source sheet cells for deterministic reassembly; individual tiles remain internal.'
