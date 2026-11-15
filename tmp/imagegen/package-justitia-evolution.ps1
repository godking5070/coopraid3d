$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskRoot=(Resolve-Path -LiteralPath 'ConceptArt\JustitiaHardEvolution_v1').Path
$zipPath=$taskRoot+'.zip'
if(Test-Path -LiteralPath $zipPath){throw 'ZIPalreadyexists; inspect before replacement'}
$manifestPath=Join-Path $taskRoot 'manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if(@($manifest.images).Count -ne 15){throw 'Expected15finalimages'}
foreach($tier in 1..5){
 $subset=@($manifest.images | Where-Object {$_.tier -eq $tier})
 if($subset.Count -ne 3 -or (@($subset.lineage | Sort-Object -Unique).Count -ne 3)){throw ('Tier scope mismatch '+$tier)}
 foreach($item in $subset){if((Get-FileHash -LiteralPath (Join-Path $taskRoot $item.image) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $item.sha256){throw 'Localhash mismatch'}}
}
$allPng=@(Get-ChildItem -LiteralPath $taskRoot -Recurse -File -Filter '*.png')
if($allPng.Count -ne 15){throw 'Expected exactly15finalPNGs'}
$promptCount=@(Get-ChildItem -LiteralPath $taskRoot -Recurse -File -Filter '*.prompt.txt').Count
if($promptCount -ne 20){throw 'Expected20exactprompts'}
$archive=[System.IO.Compression.ZipFile]::Open($zipPath,[System.IO.Compression.ZipArchiveMode]::Create)
try{
 foreach($file in Get-ChildItem -LiteralPath $taskRoot -File -Recurse){
  $entryName=$file.FullName.Substring($taskRoot.Length+1).Replace('\','/')
  [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$entryName,[System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
 }
}finally{$archive.Dispose()}
function Verify-ArchiveImages([string]$Path,$Items){
 $zip=[System.IO.Compression.ZipFile]::OpenRead($Path)
 try{
  $pngCount=@($zip.Entries | Where-Object {$_.FullName -match '\.png$'}).Count
  if($pngCount -ne 15){throw 'ZIPPNGscope mismatch'}
  foreach($item in $Items){
   $entry=$zip.GetEntry($item.image)
   if($null -eq $entry){throw ('MissingZIPimage '+$item.image)}
   $stream=$entry.Open();$hasher=[System.Security.Cryptography.SHA256]::Create()
   try{$hash=([BitConverter]::ToString($hasher.ComputeHash($stream))).Replace('-','').ToLowerInvariant()}finally{$stream.Dispose();$hasher.Dispose()}
   if($hash -ne $item.sha256){throw ('ZIPhashmismatch '+$item.image)}
  }
  return $zip.Entries.Count
 }finally{$zip.Dispose()}
}
$initialEntries=Verify-ArchiveImages $zipPath $manifest.images
$utf8=[System.Text.UTF8Encoding]::new($false)
$manifest.status='generation_visual_qa_gallery_and_package_complete'
$manifest | Add-Member -NotePropertyName verification -NotePropertyValue 'verification.json' -Force
$manifest | Add-Member -NotePropertyName package -NotePropertyValue '../JustitiaHardEvolution_v1.zip' -Force
[System.IO.File]::WriteAllText($manifestPath,($manifest | ConvertTo-Json -Depth 24),$utf8)
$progress=[pscustomobject]@{objective='Part1JustitiaHard5tiers3conceptseach';requested_images=15;completed_tiers=@(1,2,3,4,5);completed_images=15;remaining_images=0;status='complete';gallery='INDEX.html';package='../JustitiaHardEvolution_v1.zip'}
[System.IO.File]::WriteAllText((Join-Path $taskRoot 'progress.json'),($progress | ConvertTo-Json -Depth 10),$utf8)
$requirements=@(
 [pscustomobject]@{requirement='Part1JustitiaHard';result='proven';evidence='Originalnormal/hardStage5references supplied to all15initial generations,per-tier manifests record references.'},
 [pscustomobject]@{requirement='Five tiers,three concepts each';result='proven';evidence='5Level folders each contain3finalPNGs,15unique tier/lineage keys.'},
 [pscustomobject]@{requirement='Increased physical volume';result='proven visually';evidence='All15 individually compared against oldslimhard and precedinglineage; larger/deeper torso/pelvis/limbs,original mask progressively dwarfed.5x3matrix reviewed.'},
 [pscustomobject]@{requirement='Bizarre/grotesque mutation';result='proven visually';evidence='Aasymmetric counterweightbody,Bface/torso opening and imprisonedmask,Cfourleg→low coffin-carapace monster.Tier4C redesigned when initially too humanoid.'},
 [pscustomobject]@{requirement='Preserved bossidentity and artfamily';result='proven visually';evidence='Greatsword,two scale pans,blindfoldmask retained;ivory/navy/brass/lavendercel palette across15.'},
 [pscustomobject]@{requirement='Deliverables inspectable and saved';result='proven';evidence='15PNG1536x1024,20exactprompts,source/QA/hashmetadata,INDEXgallery andZIPPNGentryhashmatches.'}
)
$verification=[pscustomobject]@{
 status='complete';date='2026-11-14';goal='Part1JustitiaHardlargerandgrotesque5tiers3conceptseach';requirements=$requirements
 final_tiers=5;concepts_per_tier=3;final_pngs=15;dimensions='1536x1024';source_png_hashes_matched=15;zip_png_hashes_matched=15;exact_prompt_files=20
 original_normal_hard_unchanged=$true;visual_review='All15individualfinals and5row3columnChrome desktop render reviewed';rendered_rows=5;rendered_concepts=15;javascript_syntax='passed'
 corrections=@('Tier1A/B/Cframing','Tier4Cwholebodyanatomy','Tier5Cframing');new_selection_status='pendinguserchoice'
 inline_visual='justitia-hard-evolution-f366613c.html';inline_bytes=336246;zip_entries_after_final_metadata=($initialEntries+1)
}
[System.IO.File]::WriteAllText((Join-Path $taskRoot 'verification.json'),($verification | ConvertTo-Json -Depth 16),$utf8)
$template=[System.IO.File]::ReadAllText((Resolve-Path -LiteralPath 'tmp\gallery\justitia-evolution-template.html').Path,[System.Text.Encoding]::UTF8)
[System.IO.File]::WriteAllText((Join-Path $taskRoot 'INDEX.html'),$template.Replace('__JUSTITIA_DATA__',($manifest | ConvertTo-Json -Depth 24 -Compress)),$utf8)
$inlinePath=Join-Path $env:ORCA_CHAT_SCRATCH_DIR 'justitia-hard-evolution-f366613c.html'
if(Test-Path -LiteralPath $inlinePath){
 $inlineHtml=[System.IO.File]::ReadAllText($inlinePath,[System.Text.Encoding]::UTF8)
 $inlineHtml=$inlineHtml.Replace('"status":"all_images_visual_qa_and_file_checks_complete_pending_gallery_package_audit"','"status":"generation_visual_qa_gallery_and_package_complete"')
 [System.IO.File]::WriteAllText($inlinePath,$inlineHtml,$utf8)
 if((Get-Item -LiteralPath $inlinePath).Length -ge 524288){throw 'Inlinevisualtoo large'}
}
$archive=[System.IO.Compression.ZipFile]::Open($zipPath,[System.IO.Compression.ZipArchiveMode]::Update)
try{
 foreach($name in @('manifest.json','progress.json','INDEX.html','verification.json')){
  $entry=$archive.GetEntry($name);if($entry){$entry.Delete()}
  [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $taskRoot $name),$name,[System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
 }
}finally{$archive.Dispose()}
$finalEntries=Verify-ArchiveImages $zipPath $manifest.images
if($finalEntries -ne $verification.zip_entries_after_final_metadata){throw 'Finalarchivecount mismatch'}
[pscustomobject]@{status='100%complete';tiers=5;per_tier=3;final_pngs=15;dimensions='1536x1024';exact_prompt_files=20;verified_zip_pngs=15;zip_entries=$finalEntries;zip=$zipPath;zip_bytes=(Get-Item -LiteralPath $zipPath).Length;gallery=(Join-Path $taskRoot 'INDEX.html');inline_file='justitia-hard-evolution-f366613c.html'} | ConvertTo-Json -Compress

