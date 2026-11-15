$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class Stage6PixelAudit {
 public static Rectangle[] Measure(string path) {
  int[] boundaries={0,1100,2150,3200};Rectangle[] results=new Rectangle[6];
  using(Bitmap original=new Bitmap(path))using(Bitmap b=new Bitmap(original.Width,original.Height,PixelFormat.Format32bppArgb)) {
   using(Graphics g=Graphics.FromImage(b))g.DrawImageUnscaled(original,0,0);
   BitmapData d=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
   byte[] pixels=new byte[d.Stride*b.Height];Marshal.Copy(d.Scan0,pixels,0,pixels.Length);
   for(int slot=0;slot<6;slot++){
    int col=slot%3,row=slot/3,minx=b.Width,miny=b.Height,maxx=-1,maxy=-1;
    for(int y=row*1200;y<(row+1)*1200;y++)for(int x=boundaries[col];x<boundaries[col+1];x++){
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
$package=Join-Path $workspace 'TripoReady\Stage6_v1'
$zip=Join-Path $workspace 'TripoReady\astella-stage6-tripo-mixamo-4views-v1.zip'
$views=@('front','left','right','back')
$groupSpecs=@(
 @{folder='Bodies/Normal/Alpha';width=2048;height=1600},@{folder='Bodies/Normal/Omega';width=2048;height=1600},
 @{folder='Bodies/Hard/Alpha';width=2048;height=1600},@{folder='Bodies/Hard/Omega';width=2048;height=1600},
 @{folder='Accessories';width=3200;height=2400},@{folder='Buildings/GrandGate';width=2048;height=1600},
 @{folder='Buildings/BoundaryRail';width=2048;height=1600},@{folder='Buildings/WinchPost';width=2048;height=1600}
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
$slots=@(
 @{id='ShieldNormal';x=550;height=960;baseline=1120},@{id='SpearNormal';x=1600;height=1020;baseline=1120},@{id='CapeNormal';x=2650;height=900;baseline=1120},
 @{id='ShieldHard';x=550;height=960;baseline=2260},@{id='HalberdHard';x=1600;height=1020;baseline=2260},@{id='CapeHard';x=2650;height=900;baseline=2260}
)
$pixels=@()
foreach($view in $views){
 $rects=[Stage6PixelAudit]::Measure((Join-Path $package ('Accessories/'+$view+'.png')))
 for($i=0;$i -lt 6;$i++){
  $r=$rects[$i];$s=$slots[$i]
  $cx=($r.Left+$r.Right)/2.0
  $centerError=[Math]::Abs($cx-$s.x);$heightError=[Math]::Abs($r.Height-$s.height);$bottomError=[Math]::Abs($r.Bottom-$s.baseline)
  if($centerError -gt 2 -or $heightError -gt 2 -or $bottomError -gt 2){throw "Rendered slot drift: $view $($s.id) $r"}
  $pixels += [ordered]@{view=$view;item=$s.id;bounds=@($r.X,$r.Y,$r.Width,$r.Height);centerX=$cx;bottomY=$r.Bottom;height=$r.Height;centerErrorPx=$centerError;heightErrorPx=$heightError;bottomErrorPx=$bottomError}
 }
}
$prompts=@(Get-ChildItem -LiteralPath (Join-Path $package 'GenerationPrompts') -File -Filter '*.txt')
if($prompts.Count -ne 39){throw 'Expected39 prompts'}
$coverage=@()
foreach($kind in @('AlphaNormal','OmegaNormal','AlphaHard','OmegaHard','GrandGate','BoundaryRail','WinchPost')){
 foreach($view in $views){$coverage += $kind+'-'+$view+'.txt'}
}
foreach($prop in @('ShieldNormal','SpearNormal','CapeNormal','ShieldHard','HalberdHard','CapeHard')){$coverage += $prop+'-front.txt'}
$coverage += @('accessory-sheet-left.txt','accessory-sheet-right.txt','accessory-sheet-back.txt','Floor-top.txt','SpearNormal-back-corrected.txt')
foreach($name in $coverage){
 $path=Join-Path $package ('GenerationPrompts/'+$name)
 if(-not(Test-Path -LiteralPath $path)){throw "Missing prompt: $name"}
 $txt=Get-Content -LiteralPath $path -Encoding UTF8 -Raw
 if($txt.Length -lt 80 -or $txt -match '(?m)^undefined$'){throw "Empty prompt: $name"}
}
$world=Get-Content -LiteralPath (Join-Path $package 'map-layout.json') -Encoding UTF8 -Raw | ConvertFrom-Json
if($world.footprint.shape -ne 'semicircle' -or $world.footprint.radius -ne 20 -or $world.footprint.diameter -ne 40 -or $world.footprint.validSide -ne 'Z <= 0'){throw 'World footprint contradicts design'}
$expectedPosts=@('-12,0,-8','-5,0,-15','5,0,-15','12,0,-8')
if($world.posts.Count -ne 4){throw 'Need exactly four post placements'}
for($i=0;$i -lt 4;$i++){if(($world.posts[$i].position -join ',') -ne $expectedPosts[$i]){throw "World post coordinates differ: $i"}}
if(($world.initialBossPositions.alpha -join ',') -ne '-5,1,-4' -or ($world.initialBossPositions.omega -join ',') -ne '5,1,-4'){throw 'Initial twin boss positions differ'}
$guide=Get-Content -LiteralPath (Join-Path $package 'START_HERE_KO.txt') -Encoding UTF8 -Raw
if($guide -notmatch '\uD53D\uC140' -or $guide -notmatch '\uC5ED\uC0B0' -or $guide -notmatch '\uC2E4\uD589\uD558\uC9C0'){throw 'Required accuracy/execution limitations missing'}
$sourcePairs=@(
 @('ReferenceOnly/normal-concept-do-not-upload.png','ConceptArt/CharacterMatched/Stage6/01-janus-normal-v1.png'),
 @('ReferenceOnly/hard-concept-do-not-upload.png','ConceptArt/CharacterMatched/Stage6/02-janus-hard-v1.png'),
 @('ReferenceOnly/map-concept-do-not-upload.png','ConceptArt/CharacterMatched/Stage6/03-clocktower-gate-v1.png')
)
foreach($pair in $sourcePairs){if((Get-FileHash -LiteralPath (Join-Path $package $pair[0])).Hash -ne (Get-FileHash -LiteralPath (Join-Path $workspace $pair[1])).Hash){throw "Reference differs: $($pair[0])"}}
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try{
 $zipEntries=@($archive.Entries | Where-Object {$_.Name -ne ''});$diskFiles=@(Get-ChildItem -LiteralPath $package -Recurse -File)
 if($zipEntries.Count -ne $diskFiles.Count){throw 'ZIP/package file count differs'}
 foreach($entry in $zipEntries){
  $rel=$entry.FullName.Replace('\','/');if(-not $rel.StartsWith('Stage6_v1/')){throw "Unexpected ZIP root: $rel"}
  $disk=Join-Path $package $rel.Substring(10)
  $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
  try{$actual=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','');$expected=(Get-FileHash -LiteralPath $disk -Algorithm SHA256).Hash;if($actual -ne $expected){throw "ZIP bytes differ: $rel"}}finally{$stream.Dispose();$sha.Dispose()}
 }
}finally{$archive.Dispose()}
$report=[ordered]@{
 stage=6;reviewer='stage4_resources independent read-only cross audit';status='passed_with_declared_reference_limits';package='TripoReady/Stage6_v1';zip='TripoReady/astella-stage6-tripo-mixamo-4views-v1.zip';zipFiles=$diskFiles.Count;zipBytes=(Get-Item -LiteralPath $zip).Length;zipSha256=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLower();zipEveryFileBytesMatch=$true;modelingPngCount=$images.Count;referenceCount=3;promptCount=$prompts.Count;actualImageEvidence=$images;independentRenderedSlotMeasurements=$pixels;promptCoverage=$coverage;worldPostCoordinates=$expectedPosts;worldRadius=20;worldDiameter=40;
 visualEvidence=@('Fresh contact sheets generated from current package files in stage6-visual/bodies.png and buildings.png','All four actual 3200x2400 Accessories PNGs opened directly','Floor/top.png opened directly','Original normal/hard/tower-gate concept images opened and compared','JANUS_NORMAL.md checked for40m semicircle, post locations and alpha/omega starts');
 visualFindings=@('All four twin/difficulty bodies are whole-body empty-handed T-poses, no capes or weapons','Left faces screen right, right faces screen left, rear helmet/back mount distinguish the back; hands and feet inside frame','Alpha bulky armor and Omega slim spear-knight identity preserved across normal/hard','Hard opened chest spring mechanisms and darker visors preserved','Six accessories coexist in every sheet in same three columns and two rows;24 independent pixel measures match literal centers/heights/row baselines','Normal spear ring is visibly open through-hole on both front and corrected rear','Hard shield extension slabs are physically connected by rear horizontal supports and vertical rails; side profiles retain attached structure','Grand gate, repeating rail and one replicable winch post match approved environment','Floor is semicircular with straight northern edge and four post inlays; explicit world coordinates match original design document');
 limitationsHonest=$true;limitationsObserved=@('AI concept turnarounds, not calibrated mesh renders','No actual Tripo upload/generation or Mixamo rigging performed','Capes need fitting or cloth/bone work and hard shield sliders need DCC setup','Floor inlay pixels are approximate; map-layout.json is authoritative for meters','Small bolt, cloth fold and curve differences remain normal reference-level reconciliation');
 blockingFindings=@();nonBlockingFindings=@('Floor inlay spacing and slight roof/armor projection differences are approximate, explicitly disclosed');requiredChanges=@();packageModified=$false
}
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'stage6.json') -Encoding UTF8
Write-Output "Independent Stage6 PASS: $($images.Count) modeling PNGs, $($prompts.Count) prompts,24 measured placements,$($diskFiles.Count) ZIP entries match bytes."

