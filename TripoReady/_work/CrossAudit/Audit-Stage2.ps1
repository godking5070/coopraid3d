$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class Stage2PixelAudit {
 public static Rectangle[] Measure(string path) {
  int[] boundaries={0,1000,2400,3600}; Rectangle[] results=new Rectangle[3];
  using(Bitmap original=new Bitmap(path))using(Bitmap b=new Bitmap(original.Width,original.Height,PixelFormat.Format32bppArgb)) {
   using(Graphics g=Graphics.FromImage(b))g.DrawImageUnscaled(original,0,0);
   BitmapData d=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
   byte[] pixels=new byte[d.Stride*b.Height];Marshal.Copy(d.Scan0,pixels,0,pixels.Length);
   for(int slot=0;slot<3;slot++){
    int minx=b.Width,miny=b.Height,maxx=-1,maxy=-1;
    for(int y=0;y<b.Height;y++)for(int x=boundaries[slot];x<boundaries[slot+1];x++){
     int i=y*d.Stride+x*4;
     if(Math.Max(Math.Abs(pixels[i]-232),Math.Max(Math.Abs(pixels[i+1]-232),Math.Abs(pixels[i+2]-232)))>24){
      minx=Math.Min(minx,x);miny=Math.Min(miny,y);maxx=Math.Max(maxx,x);maxy=Math.Max(maxy,y);
     }
    }
    if(maxx<0)throw new Exception("Missing slot subject "+slot+" "+path);
    results[slot]=Rectangle.FromLTRB(minx,miny,maxx+1,maxy+1);
   }
   b.UnlockBits(d);
  }
  return results;
 }
}
'@
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$package=Join-Path $workspace 'TripoReady\Stage2_v1'
$zip=Join-Path $workspace 'TripoReady\astella-stage2-tripo-mixamo-4views-v1.zip'
$views=@('front','left','right','back')
$groupSpecs=@(
 @{folder='Bodies/Normal';width=2048;height=1600},@{folder='Bodies/Hard';width=2048;height=1600},
 @{folder='Accessories';width=3600;height=1800},@{folder='Buildings/EntranceBay';width=1600;height=1600},
 @{folder='Buildings/BalconyStairs';width=1600;height=1600},@{folder='Buildings/VentCabinet';width=1600;height=1600}
)
$images=@()
foreach($group in $groupSpecs){
 $dir=Join-Path $package $group.folder
 $entries=@(Get-ChildItem -LiteralPath $dir -File)
 if($entries.Count -ne 4){throw "Expected four files: $($group.folder)"}
 foreach($view in $views){
  $rel=$group.folder+'/'+$view+'.png';$path=Join-Path $package $rel
  $im=[Drawing.Image]::FromFile($path)
  if($im.Width -ne $group.width -or $im.Height -ne $group.height){throw "Wrong actual dimensions: $rel"}
  $images += [ordered]@{file=$rel;width=$im.Width;height=$im.Height;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLower()}
  $im.Dispose()
 }
}
$floor=Join-Path $package 'Floor/top.png';$im=[Drawing.Image]::FromFile($floor)
if($im.Width -ne 1254 -or $im.Height -ne 1254){throw 'Wrong floor dimensions'}
$images += [ordered]@{file='Floor/top.png';width=$im.Width;height=$im.Height;sha256=(Get-FileHash -LiteralPath $floor -Algorithm SHA256).Hash.ToLower()};$im.Dispose()
$slots=@(@{id='shears';x=450;height=1400},@{id='spring';x=1775;height=1250},@{id='key';x=3000;height=350})
$pixels=@()
foreach($view in $views){
 $rects=[Stage2PixelAudit]::Measure((Join-Path $package ('Accessories/'+$view+'.png')))
 for($i=0;$i -lt 3;$i++){
  $r=$rects[$i];$s=$slots[$i]
  $cx=($r.Left+$r.Right)/2.0
  $centerError=[Math]::Abs($cx-$s.x);$heightError=[Math]::Abs($r.Height-$s.height);$bottomError=[Math]::Abs($r.Bottom-1600)
  if($centerError -gt 2 -or $heightError -gt 2 -or $bottomError -gt 2){throw "Rendered slot drift: $view $($s.id) $r"}
  $pixels += [ordered]@{view=$view;item=$s.id;bounds=@($r.X,$r.Y,$r.Width,$r.Height);centerX=$cx;bottomY=$r.Bottom;height=$r.Height;centerErrorPx=$centerError;heightErrorPx=$heightError;bottomErrorPx=$bottomError}
 }
}
$prompts=@(Get-ChildItem -LiteralPath (Join-Path $package 'GenerationPrompts') -File -Filter '*.txt')
if($prompts.Count -ne 33){throw 'Prompt count differs from 33 selected source images'}
$coverage=@()
foreach($kind in @('normal','hard','shears','spring','key','entrance','balcony','vent')){
 foreach($view in $views){
  $name=$kind+'-'+$view
  $matching=@($prompts | Where-Object {$_.BaseName -eq $name -or $_.BaseName -eq ($name+'-retry')})
  if($matching.Count -ne 1){throw "Missing/ambiguous selected prompt: $name"}
  $txt=Get-Content -LiteralPath $matching[0].FullName -Encoding UTF8 -Raw
  if($txt.Length -lt 80 -or $txt -match '(?m)^undefined$'){throw "Empty prompt: $name"}
  $coverage += $matching[0].Name
 }
}
if(-not (Test-Path -LiteralPath (Join-Path $package 'GenerationPrompts/floor-top.txt'))){throw 'Missing floor prompt'}
$sourcePairs=@(
 @('ReferenceOnly/normal-concept.png','ConceptArt/CharacterMatched/03-flora-normal-v1.png'),
 @('ReferenceOnly/hard-concept.png','ConceptArt/CharacterMatched/04-flora-hard-v1.png'),
 @('ReferenceOnly/map-concept.png','ConceptArt/CharacterMatched/08-glasshouse-v1.png')
)
foreach($pair in $sourcePairs){if((Get-FileHash -LiteralPath (Join-Path $package $pair[0])).Hash -ne (Get-FileHash -LiteralPath (Join-Path $workspace $pair[1])).Hash){throw "Reference differs: $($pair[0])"}}
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try{
 $zipEntries=@($archive.Entries | Where-Object {$_.Name -ne ''});$diskFiles=@(Get-ChildItem -LiteralPath $package -Recurse -File)
 if($zipEntries.Count -ne $diskFiles.Count){throw 'ZIP/package file count differs'}
 foreach($entry in $zipEntries){
  $rel=$entry.FullName.Replace('\','/');if(-not $rel.StartsWith('Stage2_v1/')){throw "Unexpected ZIP root: $rel"}
  $disk=Join-Path $package $rel.Substring(10)
  $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
  try{$actual=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','');$expected=(Get-FileHash -LiteralPath $disk -Algorithm SHA256).Hash;if($actual -ne $expected){throw "ZIP bytes differ: $rel"}}finally{$stream.Dispose();$sha.Dispose()}
 }
}finally{$archive.Dispose()}
$report=[ordered]@{
 stage=2;reviewer='stage4_resources independent read-only cross audit';status='passed_with_declared_reference_limits';package='TripoReady/Stage2_v1';zip='TripoReady/astella-stage2-tripo-mixamo-4views-v1.zip';zipFiles=$diskFiles.Count;zipBytes=(Get-Item -LiteralPath $zip).Length;zipSha256=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLower();zipEveryFileBytesMatch=$true;modelingPngCount=$images.Count;referenceCount=3;promptCount=$prompts.Count;actualImageEvidence=$images;independentRenderedSlotMeasurements=$pixels;promptCoverage=$coverage;
 visualEvidence=@('Fresh contact sheets generated from current package files in stage2-visual/bodies.png and buildings.png','All four Accessories PNGs opened directly','Floor/top.png opened directly','Original normal/hard/glasshouse concepts opened and compared');
 visualFindings=@('Normal and hard are full-body empty-hand T-poses with no cropped hands/feet','Left faces screen right; right faces screen left; back shows hair/back mounts rather than front flower','Approved Flora gardener cap, bob, porcelain joints, glass flower core and split apron preserved','Hard chest plates retained; major spring blades separated as modelable attachment modules','All three accessories coexist in each PNG in unchanged left/middle/right order','Spring and key camera-dependent rear/side faces are distinct; complete sheet not mirrored','Entrance, balcony stairs and vent structures align with glasshouse architecture; vent side pipe follows front/right/back placement','Balcony stair side projections and decorative proportions are concept-level approximations, not a measured mesh');
 limitationsHonest=$true;limitationsObserved=@('Guide explicitly discloses AI-view reference geometry differences, no measured 3D source mesh','Guide discloses no actual Tripo generation or Mixamo rigging','Back spring blade mirrored assembly and joints required','Floor is top-view layout/color guide, not seamless or PBR material');
 blockingFindings=@();nonBlockingFindings=@('Small decorative/curve/projection variations need final 3D reconciliation, already disclosed');requiredChanges=@();packageModified=$false
}
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'stage2.json') -Encoding UTF8
Write-Output "Independent Stage2 PASS: $($images.Count) modeling PNGs, $($prompts.Count) prompts, 12 measured placements, $($diskFiles.Count) ZIP entries match bytes."
