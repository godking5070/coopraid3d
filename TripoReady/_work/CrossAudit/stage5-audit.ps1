param([switch]$SkipArchive)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class Stage5Pixels {
 public static int[] Measure(string path,int x0,int x1,int y0,int y1) {
  using(Bitmap orig=new Bitmap(path))
  using(Bitmap bmp=new Bitmap(orig.Width,orig.Height,PixelFormat.Format32bppArgb)) {
   using(Graphics g=Graphics.FromImage(bmp))g.DrawImageUnscaled(orig,0,0);
   BitmapData d=bmp.LockBits(new Rectangle(0,0,bmp.Width,bmp.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
   byte[] bytes=new byte[d.Stride*bmp.Height];Marshal.Copy(d.Scan0,bytes,0,bytes.Length);
   int b0=bytes[0],g0=bytes[1],r0=bytes[2],minx=bmp.Width,miny=bmp.Height,maxx=-1,maxy=-1,count=0;
   for(int y=y0;y<y1;y++) for(int x=x0;x<x1;x++){
    int i=y*d.Stride+x*4;int dist=Math.Max(Math.Abs(bytes[i]-b0),Math.Max(Math.Abs(bytes[i+1]-g0),Math.Abs(bytes[i+2]-r0)));
    if(dist>20 && bytes[i+3]>200){minx=Math.Min(minx,x);maxx=Math.Max(maxx,x);miny=Math.Min(miny,y);maxy=Math.Max(maxy,y);count++;}
   }
   bmp.UnlockBits(d);if(count==0)throw new Exception("No foreground in region: "+path);
   return new int[]{minx,miny,maxx+1,maxy+1,count};
  }
 }
}
'@
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$package=Join-Path $root 'TripoReady/Stage5_v1'
$views=@('front','left','right','back')
$groups=@(
 @{folder='Bodies/Normal';w=2048;h=1600},@{folder='Bodies/Hard';w=2048;h=1600},@{folder='Accessories';w=3600;h=2600},
 @{folder='Buildings/JudgeDais';w=1600;h=1600},@{folder='Buildings/WindowWallBay';w=1600;h=1600},@{folder='Buildings/JuryBench';w=1600;h=1600}
)
$images=@()
foreach($group in $groups){
 $folder=Join-Path $package $group.folder
 if(@(Get-ChildItem -LiteralPath $folder -File).Count -ne 4){throw "Wrong four-view count: $folder"}
 foreach($v in $views){
  $relative=$group.folder+'/'+$v+'.png';$p=Join-Path $package $relative;$im=[Drawing.Image]::FromFile($p)
  if($im.Width -ne $group.w -or $im.Height -ne $group.h){throw "Wrong dimensions: $relative"}
  $m=[Stage5Pixels]::Measure($p,0,$im.Width,0,$im.Height)
  if($m[0] -lt 10 -or $m[1] -lt 10 -or $m[2] -gt $im.Width-10 -or $m[3] -gt $im.Height-10){throw "Subject clipping: $relative"}
  $images += [ordered]@{file=$relative;width=$im.Width;height=$im.Height;contentBounds=@($m[0],$m[1],$m[2],$m[3]);sha256=(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLower()};$im.Dispose()
 }
}
$floor=Join-Path $package 'Floor/top.png';$im=[Drawing.Bitmap]::FromFile($floor)
if($im.Width -ne 1536 -or $im.Height -ne 1024){throw 'Wrong floor aspect'}
$images += [ordered]@{file='Floor/top.png';width=$im.Width;height=$im.Height;sha256=(Get-FileHash -LiteralPath $floor -Algorithm SHA256).Hash.ToLower()}
$floorRuns=@()
foreach($y in @(256,512,768)){
 $current='';$start=700
 for($x=700;$x -lt 836;$x++){
  $c=$im.GetPixel($x,$y);$kind='stone'
  if($c.R -lt 180 -and $c.G -lt 170){$kind='navy'}elseif($c.R -gt 175 -and $c.G -gt 140 -and $c.B -lt 195 -and $c.R-$c.B -gt 35){$kind='gold'}
  if($kind -ne $current){if($current -ne ''){$floorRuns += [ordered]@{row=$y;kind=$current;startX=$start;endXExclusive=$x;widthPx=$x-$start}};$current=$kind;$start=$x}
 }
 $floorRuns += [ordered]@{row=$y;kind=$current;startX=$start;endXExclusive=836;widthPx=836-$start}
}
$im.Dispose()
$slots=@(
 @{id='sword-normal';x0=0;x1=1000;y0=0;y1=1350;cx=500;height=1100;bottom=1250},
 @{id='sword-hard';x0=1000;x1=2000;y0=0;y1=1350;cx=1500;height=1100;bottom=1250},
 @{id='scale-normal';x0=2000;x1=3600;y0=0;y1=1350;cx=2825;height=600;bottom=1200},
 @{id='scale-hard';x0=0;x1=2200;y0=1400;y1=2600;cx=1100;height=900;bottom=2450},
 @{id='weight';x0=2200;x1=3600;y0=1400;y1=2600;cx=2800;height=140;bottom=2450}
)
$pixels=@()
foreach($v in $views){$p=Join-Path $package ('Accessories/'+$v+'.png');foreach($s in $slots){
 $m=[Stage5Pixels]::Measure($p,$s.x0,$s.x1,$s.y0,$s.y1);$cx=($m[0]+$m[2])/2.0;$h=$m[3]-$m[1]
 $dx=[Math]::Abs($cx-$s.cx);$dh=[Math]::Abs($h-$s.height);$dy=[Math]::Abs($m[3]-$s.bottom)
 if($dx -gt 2 -or $dh -gt 2 -or $dy -gt 2){throw "Fixed-slot raster mismatch: $($s.id) $v"}
 $pixels += [ordered]@{item=$s.id;view=$v;foregroundBounds=@($m[0],$m[1],$m[2],$m[3]);width=$m[2]-$m[0];centerX=$cx;height=$h;baseline=$m[3];centerErrorPx=$dx;heightErrorPx=$dh;baselineErrorPx=$dy}
}}
function Measure-PanRimRun($view,$x0,$x1,$y0,$y1){
 $im=[Drawing.Bitmap]::FromFile((Join-Path $package ('Accessories/'+$view+'.png')))
 $max=0;$atY=0;$start=0;$end=0
 try{for($y=$y0;$y -lt $y1;$y++){
  $run=-1
  for($x=$x0;$x -le $x1;$x++){
   $solid=$false
   if($x -lt $x1){$c=$im.GetPixel($x,$y);$dist=[Math]::Max([Math]::Abs($c.R-232),[Math]::Max([Math]::Abs($c.G-232),[Math]::Abs($c.B-232)));$solid=$dist -gt 20}
   if($solid){if($run -lt 0){$run=$x}}elseif($run -ge 0){
    $len=$x-$run;if($len -gt $max){$max=$len;$atY=$y;$start=$run;$end=$x};$run=-1
   }
  }
 }}finally{$im.Dispose()}
 return [ordered]@{view=$view;panRimContinuousWidthPx=$max;row=$atY;startX=$start;endXExclusive=$end}
}
$panRuns=@(
 (Measure-PanRimRun 'front' 400 1000 2310 2450),
 (Measure-PanRimRun 'left' 950 1280 2170 2360),
 (Measure-PanRimRun 'right' 950 1280 2170 2360),
 (Measure-PanRimRun 'back' 1300 1900 2310 2450)
)
$weightFit=@()
foreach($pan in $panRuns){
 $w=$pixels|Where-Object{($_.item -eq 'weight') -and ($_.view -eq $pan.view)}
 if($w.width -ge $pan.panRimContinuousWidthPx){throw "Weight does not fit pan in $($pan.view)"}
 $weightFit += [ordered]@{view=$pan.view;weightMaxWidthPx=$w.width;hardLowPanRimWidthPx=$pan.panRimContinuousWidthPx;panRimMeasurementRow=$pan.row;ratio=[Math]::Round($w.width/([double]$pan.panRimContinuousWidthPx),3);outerRimClearancePx=$pan.panRimContinuousWidthPx-$w.width;threeDimensionalClearanceStillRequiresMeshCheck=$true}
}
$prompts=@()
foreach($key in @('normal','hard','sword-normal','sword-hard','scale-normal','scale-hard','weight','dais','wall','jury')){foreach($v in $views){$prompts += $key+'-'+$v+'.txt'}}
$prompts += 'floor-top.txt'
$actual=@(Get-ChildItem -LiteralPath (Join-Path $package 'GenerationPrompts') -File)
if($actual.Count -ne 41){throw 'Wrong prompt count'}
foreach($n in $prompts){$s=Get-Content -LiteralPath (Join-Path $package ('GenerationPrompts/'+$n)) -Encoding UTF8 -Raw;if($s.Length -lt 80 -or $s -match '(?m)^undefined$'){throw "Prompt absent/empty: $n"}}
$refs=@()
foreach($pair in @(@('normal-concept.png','01-justitia-normal-v1.png'),@('hard-concept.png','02-justitia-hard-v1.png'),@('map-concept.png','03-fortress-tribunal-v1.png'))){
 $a=(Get-FileHash -LiteralPath (Join-Path $package ('ReferenceOnly/'+$pair[0])) -Algorithm SHA256).Hash
 $b=(Get-FileHash -LiteralPath (Join-Path $root ('ConceptArt/CharacterMatched/Stage5/'+$pair[1])) -Algorithm SHA256).Hash
 if($a -ne $b){throw 'Reference copy mismatch'};$refs += [ordered]@{file=$pair[0];sameBytesAsOriginal=$true;sha256=$a.ToLower()}
}
if($SkipArchive){$weightFit|ConvertTo-Json -Depth 6;Write-Output 'Independent updated PNG/prompt/reference/20-slot checks PASS';exit 0}
$zip=Join-Path $root 'TripoReady/astella-stage5-tripo-mixamo-4views-v1.zip';$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try{
 $entries=@($archive.Entries|Where-Object{$_.Name -ne ''});$files=@(Get-ChildItem -LiteralPath $package -Recurse -File)
 if($entries.Count -ne 73 -or $files.Count -ne 73){throw 'Wrong ZIP count'}
 foreach($e in $entries){$r=$e.FullName.Replace('\','/');if(-not $r.StartsWith('Stage5_v1/')){throw 'Unexpected ZIP root'}
  $expected=(Get-FileHash -LiteralPath (Join-Path $package $r.Substring(10)) -Algorithm SHA256).Hash;$stream=$e.Open();$sha=[Security.Cryptography.SHA256]::Create()
  try{$actualHash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','');if($actualHash -ne $expected){throw "ZIP bytes mismatch: $r"}}finally{$stream.Dispose();$sha.Dispose()}
 }
 $zipCheck=[ordered]@{entries=$entries.Count;everyEntryMatchesSourceBytes=$true;bytes=(Get-Item -LiteralPath $zip).Length;sha256=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLower()}
}finally{$archive.Dispose()}
$report=[ordered]@{auditor='stage3_resources';stage=5;status='numeric checks passed; visual audit pending';independentOfProducerVerification=$true;modelingImageCount=$images.Count;imageChecks=$images;referenceChecks=$refs;expectedPromptCount=$prompts.Count;actualPromptCount=$actual.Count;allExpectedPromptsPresentAndNonempty=$true;fixedSlotPixelChecks=$pixels;weightFit=$weightFit;floorScanlineRuns=$floorRuns;zipCheck=$zipCheck}
$report|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'stage5.json') -Encoding UTF8
Write-Output 'Independent numeric Stage5 checks passed.'
$weightFit|ConvertTo-Json -Depth 4
$floorRuns|ConvertTo-Json -Depth 4
function Review($folders,$name){
 $canvas=New-Object Drawing.Bitmap(1600,($folders.Count*400));$g=[Drawing.Graphics]::FromImage($canvas);$g.Clear([Drawing.Color]::FromArgb(232,232,232));$g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic;$font=New-Object Drawing.Font('Arial',12)
 for($row=0;$row -lt $folders.Count;$row++){$col=0;foreach($v in $views){
  $im=[Drawing.Image]::FromFile((Join-Path $package ($folders[$row]+'/'+$v+'.png')));$scale=[Math]::Min(390.0/$im.Width,365.0/$im.Height);$w=[int]($im.Width*$scale);$h=[int]($im.Height*$scale)
  $r=New-Object Drawing.Rectangle(($col*400+[int]((400-$w)/2)),($row*400+30+[int]((365-$h)/2)),$w,$h);$g.DrawImage($im,$r);$g.DrawString(($folders[$row]+' / '+$v),$font,[Drawing.Brushes]::Black,($col*400+5),($row*400+5));$im.Dispose();$col++
 }}
 $font.Dispose();$g.Dispose();$canvas.Save((Join-Path $PSScriptRoot $name),[Drawing.Imaging.ImageFormat]::Png);$canvas.Dispose()
}
Review @('Bodies/Normal','Bodies/Hard') 'stage5-bodies.png'
Review @('Buildings/JudgeDais','Buildings/WindowWallBay','Buildings/JuryBench') 'stage5-buildings.png'

