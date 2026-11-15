$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class Stage3PixelAudit {
 public static Rectangle[] Measure(string path) {
  int[] boundaries={0,790,1600,2100,2870,3840}; Rectangle[] results=new Rectangle[5];
  using(Bitmap original=new Bitmap(path))using(Bitmap b=new Bitmap(original.Width,original.Height,PixelFormat.Format32bppArgb)) {
   using(Graphics g=Graphics.FromImage(b))g.DrawImageUnscaled(original,0,0);
   BitmapData d=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
   byte[] pixels=new byte[d.Stride*b.Height];Marshal.Copy(d.Scan0,pixels,0,pixels.Length);
   for(int slot=0;slot<5;slot++){
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
$package=Join-Path $workspace 'TripoReady\Stage3_v1'
$zip=Join-Path $workspace 'TripoReady\astella-stage3-tripo-4views-v1.zip'
$views=@('front','left','right','back')
$groupSpecs=@(
 @{folder='Bodies/Normal';width=2048;height=1800},@{folder='Bodies/Hard';width=2048;height=1800},
 @{folder='Accessories';width=3840;height=1800},@{folder='Buildings/BelfryArch';width=1600;height=1600},
 @{folder='Buildings/Railing';width=1600;height=1600},@{folder='Buildings/WindingStation';width=1600;height=1600}
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
$slots=@(@{id='cloak-normal';x=400;height=1050},@{id='cloak-hard';x=1170;height=1050},@{id='clapper';x=1870;height=1250},@{id='drive';x=2480;height=650},@{id='chain';x=3340;height=500})
$pixels=@()
foreach($view in $views){
 $rects=[Stage3PixelAudit]::Measure((Join-Path $package ('Accessories/'+$view+'.png')))
 for($i=0;$i -lt 5;$i++){
  $r=$rects[$i];$s=$slots[$i]
  $cx=($r.Left+$r.Right)/2.0
  $centerError=[Math]::Abs($cx-$s.x);$heightError=[Math]::Abs($r.Height-$s.height);$bottomError=[Math]::Abs($r.Bottom-1600)
  if($centerError -gt 2 -or $heightError -gt 2 -or $bottomError -gt 2){throw "Rendered slot drift: $view $($s.id) $r"}
  $pixels += [ordered]@{view=$view;item=$s.id;bounds=@($r.X,$r.Y,$r.Width,$r.Height);centerX=$cx;bottomY=$r.Bottom;height=$r.Height;centerErrorPx=$centerError;heightErrorPx=$heightError;bottomErrorPx=$bottomError}
 }
}
$prompts=@(Get-ChildItem -LiteralPath (Join-Path $package 'GenerationPrompts') -File -Filter '*.txt')
if($prompts.Count -ne 43){throw 'Prompt count differs from 41 canonical views plus 2 correction prompts'}
$coverage=@()
foreach($kind in @('normal','hard','cloak-normal','cloak-hard','clapper','drive','chain','belfryarch','railing','windingstation')){
 foreach($view in $views){
  $name=$kind+'-'+$view
  $chosen=$name
  if($name -eq 'hard-front' -or $name -eq 'drive-right'){$chosen=$name+'-fixed'}
  $matching=@($prompts | Where-Object {$_.BaseName -eq $chosen})
  if($matching.Count -ne 1){throw "Missing/ambiguous selected prompt: $chosen"}
  $txt=Get-Content -LiteralPath $matching[0].FullName -Encoding UTF8 -Raw
  if($txt.Length -lt 80 -or $txt -match '(?m)^undefined$'){throw "Empty prompt: $name"}
  $coverage += $matching[0].Name
 }
}
if(-not (Test-Path -LiteralPath (Join-Path $package 'GenerationPrompts/floor-top.txt'))){throw 'Missing floor prompt'}
$sourcePairs=@(
 @('ReferenceOnly/normal-concept.png','ConceptArt/CharacterMatched/05-campanella-normal-v1.png'),
 @('ReferenceOnly/hard-concept.png','ConceptArt/CharacterMatched/06-campanella-hard-v1.png'),
 @('ReferenceOnly/map-concept.png','ConceptArt/CharacterMatched/09-silent-belfry-v1.png')
)
foreach($pair in $sourcePairs){if((Get-FileHash -LiteralPath (Join-Path $package $pair[0])).Hash -ne (Get-FileHash -LiteralPath (Join-Path $workspace $pair[1])).Hash){throw "Reference differs: $($pair[0])"}}
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try{
 $zipEntries=@($archive.Entries | Where-Object {$_.Name -ne ''});$diskFiles=@(Get-ChildItem -LiteralPath $package -Recurse -File)
 if($zipEntries.Count -ne $diskFiles.Count){throw 'ZIP/package file count differs'}
 foreach($entry in $zipEntries){
  $rel=$entry.FullName.Replace('\','/');if(-not $rel.StartsWith('Stage3_v1/')){throw "Unexpected ZIP root: $rel"}
  $disk=Join-Path $package $rel.Substring(10)
  $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
  try{$actual=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','');$expected=(Get-FileHash -LiteralPath $disk -Algorithm SHA256).Hash;if($actual -ne $expected){throw "ZIP bytes differ: $rel"}}finally{$stream.Dispose();$sha.Dispose()}
 }
}finally{$archive.Dispose()}
$report=[ordered]@{
 stage=3;reviewer='stage4_resources independent read-only cross audit';status='passed_with_declared_reference_limits';package='TripoReady/Stage3_v1';zip='TripoReady/astella-stage3-tripo-4views-v1.zip';zipFiles=$diskFiles.Count;zipBytes=(Get-Item -LiteralPath $zip).Length;zipSha256=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLower();zipEveryFileBytesMatch=$true;modelingPngCount=$images.Count;referenceCount=3;promptCount=$prompts.Count;actualImageEvidence=$images;independentRenderedSlotMeasurements=$pixels;selectedPromptCoverage=$coverage;
 visualEvidence=@('Fresh contact sheets generated from current package files in stage3-visual/bodies.png and buildings.png','Normal and Hard front PNGs opened at full resolution','All four Accessories PNGs opened directly','Floor/top.png opened directly','Original normal/hard/silent belfry concepts opened and compared','CAMPANELLA_NORMAL/HARD local design documents checked for creature identity and floor radius/plate locations');
 visualFindings=@('Both bodies retain bell plus six articulated mechanical legs, with six visible tips in front and rear; side overlap is consistent with six-leg stance','All bodies fit whole frames, no cropped yokes, leg tips or hard shell flaps','Humanoid T-pose conversion avoided; nonhumanoid identity preserved','Normal closed shell and hard open hinged shell structure follow approved concepts','Back mount plate distinguishes rear; yoke axle end view and front star direction distinguish side cameras','Five accessories coexist in each PNG in unchanged normal cloak / hard cloak / clapper / drive / chain order','Side views are narrow and correct front/rear faces are distinguished; whole sheet not mirrored','Gothic arch, three-panel rail and winding drum modules fit the belfry reference and are physically connected','Top floor has four resonance discs in quadrants consistent with documented I(-9,9),II(9,9),III(9,-9),IV(-9,-9), with radius18m stated as design reference');
 limitationsHonest=$true;limitationsObserved=@('Guide and manifest explicitly state nonhumanoid and not suitable for Mixamo humanoid rig; custom spider/mechanical rig required','Clapper, shell flaps and legs require their own joints','Three support posts and separate clapper require depth reconciliation; guide explicitly mentions this','Cloaks are neutral separated cloth shapes requiring fitting to the bell after reconstruction','Chain is intentionally a continuous rigid relief harness, not individually simulated links','AI views are not calibrated renders, and no Tripo generation/custom rigging has been performed','Floor is a layout/color guide, not seamless or PBR');
 blockingFindings=@();nonBlockingFindings=@('Minor ornament, shell curvature, side-leg overlap and support/clapper depth need final 3D reconciliation; disclosed reference-level limits');requiredChanges=@();packageModified=$false
}
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'stage3.json') -Encoding UTF8
Write-Output "Independent Stage3 PASS: $($images.Count) modeling PNGs, $($prompts.Count) prompts, 20 measured placements, $($diskFiles.Count) ZIP entries match bytes."

