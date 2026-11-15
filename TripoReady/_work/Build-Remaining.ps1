$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stages=@(
 @{stage=2;inputs=25;prompts=33;zip='astella-stage2-tripo-mixamo-4views-v1.zip'},
 @{stage=3;inputs=25;prompts=43;zip='astella-stage3-tripo-4views-v1.zip'},
 @{stage=4;inputs=25;prompts=35;zip='astella-stage4-tripo-mixamo-4views-v1.zip'},
 @{stage=5;inputs=25;prompts=41;zip='astella-stage5-tripo-mixamo-4views-v1.zip'},
 @{stage=6;inputs=33;prompts=39;zip='astella-stage6-tripo-mixamo-4views-v1.zip'}
)
$auditDir=Join-Path $root 'CrossAudit'
New-Item -ItemType Directory -Force -Path $auditDir | Out-Null
$rows=@();$archiveFiles=@();$totalInputs=0;$totalPrompts=0
foreach($s in $stages){
 $folder='Stage'+$s.stage+'_v1';$dir=Join-Path $root $folder
 $sourceAudit=Join-Path $PSScriptRoot ('CrossAudit\stage'+$s.stage+'.json')
 $audit=Get-Content -LiteralPath $sourceAudit -Encoding UTF8 -Raw|ConvertFrom-Json
 if($audit.status -notmatch '^pass'){throw "Independent audit not passed: Stage$($s.stage) $($audit.status)"}
 $zipPath=Join-Path $root $s.zip;$zipHash=(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLower()
 $auditHash=$audit.zipSha256;if(-not $auditHash){$auditHash=$audit.zipCheck.sha256}
 if($zipHash -ne $auditHash){throw "Stale independent ZIP audit: Stage$($s.stage)"}
 $files=@(Get-ChildItem -LiteralPath $dir -Recurse -File)
 $allPngs=@($files|Where-Object Extension -eq '.png')
 $inputs=@($allPngs|Where-Object{$_.FullName -notlike '*\ReferenceOnly\*'})
 $prompts=@(Get-ChildItem -LiteralPath (Join-Path $dir 'GenerationPrompts') -File -Filter '*.txt')
 if($inputs.Count -ne $s.inputs -or $prompts.Count -ne $s.prompts){throw "Input/prompt inventory mismatch: Stage$($s.stage)"}
 if(($allPngs.Count-$inputs.Count) -ne 3){throw 'Reference count mismatch'}
 if(@(Get-ChildItem -LiteralPath (Join-Path $dir 'Accessories') -File).Count -ne 4){throw 'Accessories must be exactly four sheets'}
 if(@(Get-ChildItem -LiteralPath (Join-Path $dir 'Floor') -File -Filter '*.png').Count -ne 1){throw 'Expected one floor top view'}
 if(@(Get-ChildItem -LiteralPath (Join-Path $dir 'Buildings') -Directory).Count -ne 3){throw 'Expected three architecture modules'}
 $groups=@($inputs|Where-Object{$_.Directory.Name -ne 'Floor'}|Group-Object DirectoryName)
 foreach($g in $groups){if((@($g.Group.BaseName|Sort-Object)-join ',') -ne 'back,front,left,right'){throw "Incomplete four-view group: $($g.Name)"}}
 foreach($p in $inputs){$im=[Drawing.Image]::FromFile($p.FullName);if($im.Width -lt 1024 -or $im.Height -lt 1024){throw "Input resolution too low: $($p.Name)"};$im.Dispose()}
 foreach($p in $prompts){$text=Get-Content -LiteralPath $p.FullName -Encoding UTF8 -Raw;if($text.Length -lt 80){throw "Empty prompt: $($p.Name)"}}
 if(-not(Test-Path -LiteralPath (Join-Path $dir 'START_HERE_KO.txt'))){throw 'Missing Korean guide'}
 $childZip=[IO.Compression.ZipFile]::OpenRead($zipPath)
 try{
  $entries=@($childZip.Entries|Where-Object{$_.Name -ne ''})
  if($entries.Count -ne $files.Count){throw 'Child ZIP count differs from source'}
  foreach($entry in $entries){
   $entryName=$entry.FullName.Replace('\','/')
   if(-not $entryName.StartsWith($folder+'/')){throw "Unexpected child archive root: $entryName"}
   $source=Join-Path $dir $entryName.Substring($folder.Length+1)
   $expected=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
   $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
   try{$actual=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','');if($actual -ne $expected){throw "Child ZIP bytes differ: $entryName"}}finally{$stream.Dispose();$sha.Dispose()}
  }
 }finally{$childZip.Dispose()}
 $auditCopy=Join-Path $auditDir ('stage'+$s.stage+'.json');Copy-Item -LiteralPath $sourceAudit -Destination $auditCopy -Force
 foreach($f in $files){$archiveFiles += [ordered]@{source=$f.FullName;name=$folder+'/'+$f.FullName.Substring($dir.Length+1).Replace('\','/')}}
 $archiveFiles += [ordered]@{source=$auditCopy;name='CrossAudit/stage'+$s.stage+'.json'}
 $rows += [ordered]@{stage=$s.stage;inputPngCount=$inputs.Count;referencePngCount=3;promptCount=$prompts.Count;fourViewGroups=$groups.Count;stageFiles=$files.Count;independentAuditStatus=$audit.status;individualZip=$s.zip;zipSha256=$zipHash;zipEveryFileMatchesSource=$true}
 $totalInputs+=$inputs.Count;$totalPrompts+=$prompts.Count
}
if($totalInputs -ne 133 -or $totalPrompts -ne 191){throw 'Combined scope mismatch'}
$verification=[ordered]@{status='passed';scope='All remaining Part1 stages 2-6';modelingPngCount=$totalInputs;promptCount=$totalPrompts;stages=$rows;requirements=@('Every stage includes normal/hard bodies in four separate views','Janus Alpha/Omega each have normal/hard four-view bodies','All accessories together in four fixed-layout sheets per stage','Each stage has three architecture modules in four views plus one floor top view','Independent visual and pixel audits passed for all five stages','All five individual ZIPs rehashed and every entry equals current workspace source','Stage3 custom mechanical rig and Stage4 additional-arm work explicitly documented');performed=@{imageGeneration='built-in image_gen';tripoGeneration=$false;mixamoRigging=$false;gameProjectEdits=$false};limitations='AI concept turnarounds; remaining projection and DCC assembly limitations are documented in each guide.'}
$verificationPath=Join-Path $root 'stages2-6-verification.json'
$verification|ConvertTo-Json -Depth 9|Set-Content -LiteralPath $verificationPath -Encoding UTF8
foreach($name in @('START_HERE_STAGES2-6_KO.txt','stages2-6-manifest.json','stages2-6-verification.json')){$archiveFiles += [ordered]@{source=(Join-Path $root $name);name=$name}}
$combined=Join-Path $root 'astella-stages2-6-tripo-4views-v1.zip'
if(Test-Path -LiteralPath $combined){throw 'Combined version already exists; preserve it'}
$archive=[IO.Compression.ZipFile]::Open($combined,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($f in $archiveFiles){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$f.source,$f.name,[IO.Compression.CompressionLevel]::Optimal)|Out-Null}}finally{$archive.Dispose()}
$archive=[IO.Compression.ZipFile]::OpenRead($combined)
try{
 $entries=@($archive.Entries|Where-Object{$_.Name -ne ''})
 if($entries.Count -ne $archiveFiles.Count){throw 'Combined archive file count differs'}
 $byName=@{};foreach($f in $archiveFiles){$byName[$f.name]=$f.source}
 foreach($entry in $entries){
  $source=$byName[$entry.FullName];if(-not $source){throw "Unexpected master entry: $($entry.FullName)"}
  $expected=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
  $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
  try{$actual=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','');if($actual -ne $expected){throw "Master ZIP bytes differ: $($entry.FullName)"}}finally{$stream.Dispose();$sha.Dispose()}
 }
 $report=[ordered]@{status='passed';archive=$combined;files=$entries.Count;bytes=(Get-Item -LiteralPath $combined).Length;sha256=(Get-FileHash -LiteralPath $combined -Algorithm SHA256).Hash.ToLower();everyEntryEqualsCurrentSource=$true;inputPngs=133;references=15;prompts=191;independentStagesVerified=5}
 $report|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'remaining-stages-archive-verification.json') -Encoding UTF8
 $report|ConvertTo-Json -Depth 5
}finally{$archive.Dispose()}
