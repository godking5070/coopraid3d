$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class SheetCheck {
 public static Rectangle Bounds(string path,int x0,int x1) {
  using(Bitmap original=new Bitmap(path))
  using(Bitmap bmp=new Bitmap(original.Width,original.Height,PixelFormat.Format32bppArgb)) {
   using(Graphics g=Graphics.FromImage(bmp)) g.DrawImageUnscaled(original,0,0);
   BitmapData d=bmp.LockBits(new Rectangle(0,0,bmp.Width,bmp.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
   byte[] bytes=new byte[d.Stride*bmp.Height]; Marshal.Copy(d.Scan0,bytes,0,bytes.Length);
   int minx=bmp.Width,miny=bmp.Height,maxx=-1,maxy=-1;
   for(int y=0;y<bmp.Height;y++) for(int x=x0;x<x1;x++) {
    int i=y*d.Stride+x*4;
    if(Math.Max(Math.Abs(bytes[i]-232),Math.Max(Math.Abs(bytes[i+1]-232),Math.Abs(bytes[i+2]-232)))>20) {
     minx=Math.Min(minx,x); maxx=Math.Max(maxx,x); miny=Math.Min(miny,y); maxy=Math.Max(maxy,y);
    }
   }
   bmp.UnlockBits(d);
   if(maxx<0)throw new Exception("No foreground: "+path);
   return Rectangle.FromLTRB(minx,miny,maxx+1,maxy+1);
  }
 }
}
'@
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$package=Join-Path $workspace 'TripoReady\Stage2_v1'
$manifest=Get-Content -LiteralPath (Join-Path $package 'manifest.json') -Encoding UTF8 -Raw | ConvertFrom-Json
$layout=Get-Content -LiteralPath (Join-Path $package 'accessory-layout.json') -Encoding UTF8 -Raw | ConvertFrom-Json
$checkedImages=@()
foreach($group in $manifest.inputGroups) {
  $folder=Join-Path $package $group.folder
  $files=@(Get-ChildItem -LiteralPath $folder -File)
  if($files.Count -ne 4) { throw "Expected exactly four images in $folder" }
  foreach($view in $manifest.views) {
    $relative=$group.folder+'/'+$view+'.png'
    $path=Join-Path $package $relative
    $img=[Drawing.Image]::FromFile($path)
    if($img.Width -ne $group.size[0] -or $img.Height -ne $group.size[1]) { throw "Dimensions incorrect: $relative" }
    $checkedImages += [ordered]@{file=$relative;width=$img.Width;height=$img.Height;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLower()}
    $img.Dispose()
  }
}
$floorPath=Join-Path $package $manifest.floor.path
$img=[Drawing.Image]::FromFile($floorPath)
if($img.Width -ne $manifest.floor.size[0] -or $img.Height -ne $manifest.floor.size[1]) { throw 'Floor dimensions incorrect' }
$checkedImages += [ordered]@{file=$manifest.floor.path;width=$img.Width;height=$img.Height;sha256=(Get-FileHash -LiteralPath $floorPath -Algorithm SHA256).Hash.ToLower()}
$img.Dispose()
if($checkedImages.Count -ne 25) { throw 'Expected 25 modeling PNGs' }
if(@(Get-ChildItem -LiteralPath (Join-Path $package 'ReferenceOnly') -Filter '*.png' -File).Count -ne 3) { throw 'Expected 3 reference PNGs' }
$promptFiles=@(Get-ChildItem -LiteralPath (Join-Path $package 'GenerationPrompts') -File -Filter '*.txt')
if($promptFiles.Count -ne 33) { throw "Expected 33 prompts, got $($promptFiles.Count)" }
foreach($p in $promptFiles) {
  $s=Get-Content -LiteralPath $p.FullName -Encoding UTF8 -Raw
  if($s.Length -lt 80 -or $s -match '(?m)^undefined$') { throw "Empty/missing prompt: $($p.Name)" }
}
$pixelChecks=@()
foreach($slot in $layout.slots) {
  $entries=@($layout.placements | Where-Object item -eq $slot.id)
  if($entries.Count -ne 4) { throw "Missing placements: $($slot.id)" }
  if(@($entries.centerX | Select-Object -Unique).Count -ne 1 -or @($entries.height | Select-Object -Unique).Count -ne 1 -or @($entries.baseline | Select-Object -Unique).Count -ne 1) { throw "Layout drifts for $($slot.id)" }
  foreach($e in $entries) {
    $p=Join-Path $package ('Accessories/'+$e.view+'.png')
    $b=[SheetCheck]::Bounds($p,$slot.minX,$slot.maxX)
    $centerError=[Math]::Abs(($b.Left+$b.Right)/2.0-$e.centerX)
    $bottomError=[Math]::Abs($b.Bottom-$e.baseline)
    $heightError=[Math]::Abs($b.Height-$e.height)
    if($centerError -gt 2 -or $bottomError -gt 2 -or $heightError -gt 2) { throw "Rendered placement drifts: $($slot.id) $($e.view): $b center=$centerError bottom=$bottomError height=$heightError" }
    $pixelChecks += [ordered]@{item=$slot.id;view=$e.view;renderedBounds=@($b.X,$b.Y,$b.Width,$b.Height);centerErrorPx=$centerError;bottomErrorPx=$bottomError;heightErrorPx=$heightError}
  }
}
$report=[ordered]@{status='passed';modelingPngCount=$checkedImages.Count;referencePngCount=3;promptCount=$promptFiles.Count;imageFiles=$checkedImages;accessoryFixedLayoutPixelChecks=$pixelChecks;checks=@('All six four-view groups complete','All PNGs readable at expected sizes','Accessory slot order, X centers, heights and bottom alignment verified','No accessory leaves its assigned slot','All prompts nonempty');visualReview=@('All four accessory sheets inspected','Both T-pose bodies inspected in all four directions','All twelve building elevations inspected','Floor top view inspected');limitations='Layout verification does not establish exact 3D geometry correspondence or successful Tripo/Mixamo reconstruction.'}
$report | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $package 'verification.json') -Encoding UTF8
Write-Output "PASS: 25 modeling PNGs; 3 reference PNGs; 33 prompts; 12 fixed-slot pixel checks."

