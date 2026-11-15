param([string]$Compiler)
$ErrorActionPreference='Stop'
if(!$Compiler){$taskVsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe';$Compiler=(& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)}
if(!$Compiler){throw 'A C# 9+ compiler is required.'}
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskOutput=Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-StaggerFirst-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput)|Out-Null
function Read-Part([string]$File,[string]$Signature,[bool]$Expression=$false){
 $source=[IO.File]::ReadAllText((Join-Path $taskRoot ('Assets/Scripts/'+$File)))
 $start=$source.IndexOf($Signature,[StringComparison]::Ordinal)
 if($start -lt 0){throw "Missing $Signature in $File"}
 if($Expression){$end=$source.IndexOf(';',$start)+1}else{
  $open=$source.IndexOf('{',$start);$end=$open+1;$depth=1
  while($depth -gt 0 -and $end -lt $source.Length){if($source[$end] -eq '{'){$depth++}elseif($source[$end] -eq '}'){$depth--};$end++}
  if($depth -ne 0){throw 'Incomplete source block'}
 }
 return $source.Substring($start,$end-$start)
}
$methods='';$priorities='';$enums=''
foreach($name in @('Guardian','Flora','Campanella')){
 $file='Boss'+$name+'Normal.cs'
 $methods+=(Read-Part $file ('private bool '+$name+'RequiredStaggerPending') $true)+"`n"
 $methods+=(Read-Part $file ('public bool '+$name+'StaggerLocked') $true)+"`n"
 $methods+=(Read-Part $file ('public bool Try'+$name+'StaggerGroggy('))+"`n"
 $methods+=(Read-Part $file ('private void End'+$name+'('))+"`n"
 $flag=@{Guardian='guardian';Flora='flora';Campanella='camp'}[$name]
 $priorities+='public void Priority'+$name+'(){float now=GuardianNow;'+(Read-Part $file ('if(_'+$flag+'GeneralStagger && '+$name+'RequiredStaggerPending)'))+"}`n"
 if($name -ne 'Guardian'){
  foreach($type in @('Pattern','Step')){$enums+=(Read-Part ($name+'NormalData.cs') ('public enum '+$name+$type))+"`n"}
 }
}
$rules='static class FloraNormalRules{'+(Read-Part 'FloraNormalData.cs' 'public static bool GardenDue(' $true)+'}'+
 'static class CampanellaNormalRules{'+(Read-Part 'CampanellaNormalData.cs' 'public static bool ForceBellDue(' $true)+'}'+
 'static class CampanellaHardRules{'+(Read-Part 'CampanellaHardData.cs' 'public static float Tempo(' $true)+
 (Read-Part 'CampanellaHardData.cs' 'public static bool TransformDue(' $true)+
 (Read-Part 'CampanellaHardData.cs' 'public static bool FinalDue(' $true)+'}'
$template=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'StaggerBossFirstTests.cs'))
$fixture=Join-Path $taskOutput 'Fixture.cs'
[IO.File]::WriteAllText($fixture,$template.Replace('/* METHODS */',$methods).Replace('/* PRIORITIES */',$priorities).Replace('/* ENUMS */',$enums).Replace('/* RULES */',$rules),[Text.UTF8Encoding]::new($false))
$exe=Join-Path $taskOutput 'FirstBossTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$exe" $fixture (Join-Path $taskRoot 'Assets/Scripts/GuardianNormalData.cs') (Join-Path $taskRoot 'Assets/Scripts/FloraHardRules.cs')
if($LASTEXITCODE -ne 0){throw 'First-boss stagger tests failed to compile.'}
& $exe
if($LASTEXITCODE -ne 0){throw 'First-boss stagger tests failed.'}
