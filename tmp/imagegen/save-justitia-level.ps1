param([Parameter(Mandatory=$true)][ValidateRange(1,5)][int]$Tier)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$taskRoot=(Resolve-Path -LiteralPath 'ConceptArt\JustitiaHardEvolution_v1').Path
$levelName='Level{0:D2}' -f $Tier
$levelRoot=Join-Path $taskRoot $levelName
$manifestPath=Join-Path $levelRoot 'manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$utf8=[System.Text.UTF8Encoding]::new($false)
if(@($manifest.images).Count -ne 3){throw 'Expected3 images per tier'}
foreach($item in $manifest.images){
 $target=Join-Path $levelRoot $item.image
 if(Test-Path -LiteralPath $target){throw ('Refusing existing image '+$target)}
 Copy-Item -LiteralPath $item.source -Destination $target
 $picture=[System.Drawing.Image]::FromFile($target)
 try{$imageWidth=$picture.Width;$imageHeight=$picture.Height}finally{$picture.Dispose()}
 if($imageWidth -ne 1536 -or $imageHeight -ne 1024){throw 'Unexpected dimensions'}
 $item | Add-Member -NotePropertyName width -NotePropertyValue $imageWidth -Force
 $item | Add-Member -NotePropertyName height -NotePropertyValue $imageHeight -Force
 $item | Add-Member -NotePropertyName bytes -NotePropertyValue (Get-Item -LiteralPath $target).Length -Force
 $item | Add-Member -NotePropertyName sha256 -NotePropertyValue (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -Force
}
$manifest.status='generation_visual_qa_and_file_validation_complete'
[System.IO.File]::WriteAllText($manifestPath,($manifest | ConvertTo-Json -Depth 18),$utf8)
$completed=@()
foreach($number in 1..5){$path=Join-Path $taskRoot ('Level{0:D2}\manifest.json' -f $number);if(Test-Path -LiteralPath $path){$level=Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json;if($level.status -eq 'generation_visual_qa_and_file_validation_complete'){$completed+=$number}}}
$progress=[pscustomobject]@{objective='JustitiaHard5tiers3conceptsEach';requested_images=15;completed_tiers=$completed;completed_images=($completed.Count*3);remaining_images=(15-$completed.Count*3);status=$(if($completed.Count -eq 5){'all_images_complete_pending_final_audit'}else{'in_progress'})}
[System.IO.File]::WriteAllText((Join-Path $taskRoot 'progress.json'),($progress | ConvertTo-Json -Depth 10),$utf8)
$progress | ConvertTo-Json -Compress
