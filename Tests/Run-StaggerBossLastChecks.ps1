param([string]$Compiler)
$ErrorActionPreference='Stop'
if(!$Compiler){$taskVsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe';$Compiler=(& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe'|Select-Object -First 1)}
if(!$Compiler){throw 'C# 9 compiler required'}
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskOutput=Join-Path ([IO.Path]::GetTempPath()) ('StaggerBossLast-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput)|Out-Null
function Read-Body([string]$Source,[string]$Signature,[bool]$BodyOnly=$false){
 $start=$Source.IndexOf($Signature,[StringComparison]::Ordinal);if($start -lt 0){throw "Missing $Signature"}
 $open=$Source.IndexOf('{',$start);$end=$open+1;$depth=1
 while($depth -gt 0 -and $end -lt $Source.Length){if($Source[$end] -eq '{'){$depth++}elseif($Source[$end] -eq '}'){$depth--};$end++}
 if($depth -ne 0){throw "Unclosed $Signature"}
 if($BodyOnly){return $Source.Substring($open,$end-$open)}
 $Source.Substring($start,$end-$start)
}
$taskMethods=''
foreach($name in @('Marionette','Justitia','Janus')){
 $source=[IO.File]::ReadAllText((Join-Path $taskRoot ('Assets/Scripts/Boss'+$name+'Normal.cs')))
 foreach($signature in @(('public bool '+$name+'StaggerLocked =>'),('private bool '+$name+'StaggerPriorityDue =>'))){
  $start=$source.IndexOf($signature,[StringComparison]::Ordinal);if($start -lt 0){throw "Missing $signature"}
  $end=$source.IndexOf(';',$start);$taskMethods+=$source.Substring($start,$end-$start+1)+"`n"
 }
 $taskMethods+=(Read-Body $source ('public bool Try'+$name+'StaggerGroggy('))+"`n"
 $taskMethods+=(Read-Body $source ('private void End'+$name+'('))+"`n"
 $prefix=if($name -eq 'Marionette'){'mar'}elseif($name -eq 'Justitia'){'just'}else{'janus'}
 $method=if($name -eq 'Marionette'){'TickMar'}elseif($name -eq 'Justitia'){'TickJust'}else{'TickJanus'}
 $taskMethods+='private void '+$method+'(float dt)'+(Read-Body $source ('else if(_'+$prefix+'Step=='+$name+'Step.Groggy)') $true)+"`n"
}
$fixture=Join-Path $taskOutput 'StaggerBossLastFixture.cs'
$template=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'StaggerBossLastTests.cs'))
[IO.File]::WriteAllText($fixture,$template.Replace('/* ACTUAL_METHODS */',$taskMethods),[Text.UTF8Encoding]::new($false))
$sources=@('MarionetteNormalData.cs','MarionetteHardData.cs','JustitiaNormalData.cs','JustitiaHardData.cs','JanusNormalData.cs','JanusHardData.cs')|ForEach-Object{Join-Path $taskRoot ('Assets/Scripts/'+$_)}
$exe=Join-Path $taskOutput 'StaggerBossLastTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$exe" $sources $fixture
if($LASTEXITCODE -ne 0){throw 'Last-three boss stagger fixture failed to compile'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Last-three boss stagger fixture failed'}
