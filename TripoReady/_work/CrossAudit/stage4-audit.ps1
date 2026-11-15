$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class CrossAuditPixels {
 public static int[] Measure(string path,int x0,int x1) {
  using(Bitmap orig=new Bitmap(path))
  using(Bitmap bmp=new Bitmap(orig.Width,orig.Height,PixelFormat.Format32bppArgb)) {
   using(Graphics g=Graphics.FromImage(bmp))g.DrawImageUnscaled(orig,0,0);
   BitmapData d=bmp.LockBits(new Rectangle(0,0,bmp.Width,bmp.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
   byte[] bytes=new byte[d.Stride*bmp.Height];Marshal.Copy(d.Scan0,bytes,0,bytes.Length);
   int b0=bytes[0],g0=bytes[1],r0=bytes[2],minx=bmp.Width,miny=bmp.Height,maxx=-1,maxy=-1,count=0;
   for(int y=0;y<bmp.Height;y++) for(int x=x0;x<x1;x++) {
    int i=y*d.Stride+x*4;
    int dist=Math.Max(Math.Abs(bytes[i]-b0),Math.Max(Math.Abs(bytes[i+1]-g0),Math.Abs(bytes[i+2]-r0)));
    if(dist>20 && bytes[i+3]>200) {minx=Math.Min(minx,x);maxx=Math.Max(maxx,x);miny=Math.Min(miny,y);maxy=Math.Max(maxy,y);count++;}
   }
   bmp.UnlockBits(d);
   if(count==0)throw new Exception("No foreground pixels: "+path);
   return new int[]{minx,miny,maxx+1,maxy+1,count,r0,g0,b0};
  }
 }
}
'@
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$package=Join-Path $root 'TripoReady\Stage4_v1'
$views=@('front','left','right','back')
$groups=@(
 @{folder='Bodies/Normal';w=2048;h=1600},@{folder='Bodies/Hard';w=2048;h=1600},
 @{folder='Accessories';w=3200;h=1800},
 @{folder='Buildings/PuppetStage';w=2400;h=1600},@{folder='Buildings/AudienceBox';w=2400;h=1600},@{folder='Buildings/FlyRig';w=2400;h=1600}
)
$checks=@()
foreach($group in $groups){
 $folder=Join-Path $package $group.folder
 if(@(Get-ChildItem -LiteralPath $folder -File).Count -ne 4){throw "Not exactly four images: $folder"}
 foreach($view in $views){
  $relative=$group.folder+'/'+$view+'.png';$p=Join-Path $package $relative;$im=[Drawing.Image]::FromFile($p)
  if($im.Width -ne $group.w -or $im.Height -ne $group.h){throw "Unexpected PNG dimensions: $relative"}
  $bounds=[CrossAuditPixels]::Measure($p,0,$im.Width)
  if($bounds[0] -lt 10 -or $bounds[1] -lt 10 -or $bounds[2] -gt $im.Width-10 -or $bounds[3] -gt $im.Height-10){throw "Subject clips canvas: $relative"}
  $checks += [ordered]@{file=$relative;width=$im.Width;height=$im.Height;contentBounds=@($bounds[0],$bounds[1],$bounds[2],$bounds[3]);sha256=(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLower()}
  $im.Dispose()
 }
}
$im=[Drawing.Image]::FromFile((Join-Path $package 'Floor/top.png'));if($im.Width -ne 1254 -or $im.Height -ne 1254){throw 'Floor dimensions incorrect'}
$checks += [ordered]@{file='Floor/top.png';width=$im.Width;height=$im.Height;sha256=(Get-FileHash -LiteralPath (Join-Path $package 'Floor/top.png') -Algorithm SHA256).Hash.ToLower()};$im.Dispose()
$slotPlans=@(
 @{id='AdditionalArm';a=0;b=900;cx=450;h=1400},
 @{id='CrossController';a=900;b=1870;cx=1400;h=380},
 @{id='BackReelFrame';a=1870;b=3200;cx=2520;h=1080}
)
$slotChecks=@()
foreach($view in $views){
 $p=Join-Path $package ('Accessories/'+$view+'.png')
 foreach($slot in $slotPlans){
  $m=[CrossAuditPixels]::Measure($p,$slot.a,$slot.b)
  $cx=($m[0]+$m[2])/2.0;$h=$m[3]-$m[1]
  $dx=[Math]::Abs($cx-$slot.cx);$dh=[Math]::Abs($h-$slot.h);$dy=[Math]::Abs($m[3]-1600)
  if($dx -gt 2 -or $dh -gt 2 -or $dy -gt 2){throw "Slot drift: $($slot.id) $view cx=$cx height=$h bottom=$($m[3])"}
  $slotChecks += [ordered]@{item=$slot.id;view=$view;foregroundBounds=@($m[0],$m[1],$m[2],$m[3]);centerX=$cx;height=$h;baseline=$m[3];centerErrorPx=$dx;heightErrorPx=$dh;baselineErrorPx=$dy}
 }
}
$expectedPrompts=@()
foreach($mode in @('normal','hard')){foreach($v in $views){$expectedPrompts += 'body-'+$mode+'-'+$v+'.txt'}}
foreach($item in @('arm','controller','frame')){foreach($v in $views){$expectedPrompts += $item+'-'+$v+'.txt'}}
foreach($item in @('stage','box','rig')){foreach($v in $views){$expectedPrompts += 'building-'+$item+'-'+$v+'.txt'}}
$expectedPrompts += @('floor-top.txt','body-normal-back-fixed.txt','body-hard-back-fixed.txt')
$actualPrompts=@(Get-ChildItem -LiteralPath (Join-Path $package 'GenerationPrompts') -File)
if($actualPrompts.Count -ne 35){throw 'Prompt count incorrect'}
foreach($name in $expectedPrompts){$s=Get-Content -LiteralPath (Join-Path $package ('GenerationPrompts/'+$name)) -Encoding UTF8 -Raw;if($s.Length -lt 80 -or $s -match '(?m)^undefined$'){throw "Missing/empty prompt: $name"}}
$referenceMatches=@()
foreach($pair in @(@('normal-concept-do-not-upload.png','01-marionette-normal-v1.png'),@('hard-concept-do-not-upload.png','02-marionette-hard-v1.png'),@('map-concept-do-not-upload.png','03-opera-theatre-v1.png'))){
 $a=(Get-FileHash -LiteralPath (Join-Path $package ('ReferenceOnly/'+$pair[0])) -Algorithm SHA256).Hash
 $b=(Get-FileHash -LiteralPath (Join-Path $root ('ConceptArt/CharacterMatched/Stage4/'+$pair[1])) -Algorithm SHA256).Hash
 if($a -ne $b){throw 'Original reference differs'}
 $referenceMatches += [ordered]@{file=$pair[0];sameBytesAsOriginal=$true;sha256=$a.ToLower()}
}
$zip=Join-Path $root 'TripoReady/astella-stage4-tripo-mixamo-4views-v1.zip'
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try{
 $entries=@($archive.Entries|Where-Object{$_.Name -ne ''});$files=@(Get-ChildItem -LiteralPath $package -Recurse -File)
 if($entries.Count -ne 67 -or $files.Count -ne 67){throw "ZIP/root count differs from67: $($entries.Count),$($files.Count)"}
 foreach($e in $entries){
  $r=$e.FullName.Replace('\','/');if(-not $r.StartsWith('Stage4_v1/')){throw "Unexpected ZIP root $r"}
  $p=Join-Path $package $r.Substring(10);$expected=(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash
  $stream=$e.Open();$sha=[Security.Cryptography.SHA256]::Create()
  try{$actual=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','');if($actual -ne $expected){throw "ZIP bytes mismatch $r"}}finally{$stream.Dispose();$sha.Dispose()}
 }
 $zipCheck=[ordered]@{file='TripoReady/astella-stage4-tripo-mixamo-4views-v1.zip';entryCount=$entries.Count;everyEntryMatchesSourceBytes=$true;bytes=(Get-Item -LiteralPath $zip).Length;sha256=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLower()}
}finally{$archive.Dispose()}
$report=[ordered]@{auditor='stage3_resources';stage=4;status='numeric checks passed; visual audit pending';independentOfProducerVerification=$true;modelingImageCount=$checks.Count;imageChecks=$checks;referenceChecks=$referenceMatches;expectedPromptCount=$expectedPrompts.Count;actualPromptCount=$actualPrompts.Count;allExpectedPromptsPresentAndNonempty=$true;fixedSlotPixelChecks=$slotChecks;zipCheck=$zipCheck}
$report|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'stage4.json') -Encoding UTF8
Write-Output 'Independent numeric audit PASS: 25 PNG,35 prompt,3 refs,12 pixel slots,67 ZIP entries with identical bytes.'
function Review($folders,$name){
 $canvas=New-Object Drawing.Bitmap(1600,($folders.Count*400));$g=[Drawing.Graphics]::FromImage($canvas);$g.Clear([Drawing.Color]::FromArgb(232,232,232));$g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic;$font=New-Object Drawing.Font('Arial',12)
 for($row=0;$row -lt $folders.Count;$row++){$col=0;foreach($view in $views){
  $im=[Drawing.Image]::FromFile((Join-Path $package ($folders[$row]+'/'+$view+'.png')));$scale=[Math]::Min(390.0/$im.Width,365.0/$im.Height);$w=[int]($im.Width*$scale);$h=[int]($im.Height*$scale)
  $r=New-Object Drawing.Rectangle(($col*400+[int]((400-$w)/2)),($row*400+30+[int]((365-$h)/2)),$w,$h);$g.DrawImage($im,$r);$g.DrawString(($folders[$row]+' / '+$view),$font,[Drawing.Brushes]::Black,($col*400+5),($row*400+5));$im.Dispose();$col++
 }}
 $font.Dispose();$g.Dispose();$canvas.Save((Join-Path $PSScriptRoot $name),[Drawing.Imaging.ImageFormat]::Png);$canvas.Dispose()
}
Review @('Bodies/Normal','Bodies/Hard') 'stage4-bodies.png'
Review @('Buildings/PuppetStage','Buildings/AudienceBox','Buildings/FlyRig') 'stage4-buildings.png'

