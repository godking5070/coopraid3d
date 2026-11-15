param([string]$Compiler)
$ErrorActionPreference='Stop'
if(!$Compiler){$vsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe';$Compiler=(& $vsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)}
if(!$Compiler){throw 'C# compiler required.'}
$projectRoot=Split-Path $PSScriptRoot -Parent
$outDir=Join-Path ([IO.Path]::GetTempPath()) ('Astella-GuardianTempo-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($outDir)|Out-Null
$exe=Join-Path $outDir 'GuardianTempoTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$exe" (Join-Path $projectRoot 'Assets/Scripts/GuardianNormalData.cs') (Join-Path $PSScriptRoot 'GuardianTempoTests.cs')
if($LASTEXITCODE -ne 0){throw 'Guardian tempo tests failed to compile.'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Guardian tempo tests failed.'}
$taskSource=[IO.File]::ReadAllText((Join-Path $projectRoot 'Assets/Scripts/BossGuardianHard.cs'))
function Read-StoneMethod([string]$Signature){
    $start=$taskSource.IndexOf($Signature,[StringComparison]::Ordinal)
    if($start -lt 0){throw "Missing production method: $Signature"}
    $open=$taskSource.IndexOf('{',$start);$end=$open+1;$depth=1
    while($depth -gt 0 -and $end -lt $taskSource.Length){
        if($taskSource[$end] -eq '{'){$depth++}elseif($taskSource[$end] -eq '}'){$depth--};$end++
    }
    if($depth -ne 0){throw 'Incomplete stone method.'}
    $taskSource.Substring($start,$end-$start)
}
$methods=(@('private void StartHardStoneRain(', 'private void EmitStoneWave(', 'private void TickHardStoneRain(', 'private void RemoveStoneObject(', 'private void ClearHardStoneRain(') | ForEach-Object {Read-StoneMethod $_}) -join "`n"
$template=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'GuardianStoneRainAdapterTests.cs'))
$fixture=Join-Path $outDir 'GuardianStoneRainFixture.cs'
[IO.File]::WriteAllText($fixture,$template.Replace('/* ACTUAL_STONE_METHODS */',$methods),[Text.UTF8Encoding]::new($false))
$adapter=Join-Path $outDir 'GuardianStoneRainAdapterTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$adapter" (Join-Path $projectRoot 'Assets/Scripts/GuardianNormalData.cs') $fixture
if($LASTEXITCODE -ne 0){throw 'Guardian stone adapter tests failed to compile.'}
& $adapter
if($LASTEXITCODE -ne 0){throw 'Guardian stone adapter tests failed.'}
