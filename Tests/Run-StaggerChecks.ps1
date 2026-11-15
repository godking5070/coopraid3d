param([string]$Compiler)
$ErrorActionPreference='Stop'
if(!$Compiler){$staggerVsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe';$Compiler=(& $staggerVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)}
if(!$Compiler){throw 'A C# 9+ compiler is required.'}
$staggerRoot=Split-Path $PSScriptRoot -Parent
$staggerOutput=Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-Stagger-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($staggerOutput)|Out-Null
$staggerSource=[IO.File]::ReadAllText((Join-Path $staggerRoot 'Assets/Scripts/CombatCounterTarget.cs'))
function Read-StaggerMethod([string]$Signature) {
    $start=$staggerSource.IndexOf($Signature,[StringComparison]::Ordinal)
    if($start -lt 0){throw "Missing stagger method: $Signature"}
    $open=$staggerSource.IndexOf('{',$start);$end=$open+1;$depth=1
    while($depth -gt 0 -and $end -lt $staggerSource.Length){if($staggerSource[$end] -eq '{'){$depth++}elseif($staggerSource[$end] -eq '}'){$depth--};$end++}
    if($depth -ne 0){throw 'Incomplete stagger method'}
    $staggerSource.Substring($start,$end-$start)
}
$staggerMethods=(@('public void AddStagger(', 'public void ResetState(', 'public void Restore(')|ForEach-Object{Read-StaggerMethod $_}) -join "`n"
$staggerSource=[IO.File]::ReadAllText((Join-Path $staggerRoot 'Assets/Scripts/RaidRunFlow.cs'))
$staggerRaidwide=(Read-StaggerMethod 'public sealed class BossRaidwideSettings')+"`n"+(Read-StaggerMethod 'public sealed class BossRaidwideState')
$staggerTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'StaggerAdapterTests.cs'))
$staggerFixture=Join-Path $staggerOutput 'StaggerAdapterFixture.cs'
[IO.File]::WriteAllText($staggerFixture,$staggerTemplate.Replace('/* ACTUAL_STAGGER_METHODS */',$staggerMethods).Replace('/* ACTUAL_RAIDWIDE_STATE */',$staggerRaidwide),[Text.UTF8Encoding]::new($false))
$staggerExe=Join-Path $staggerOutput 'StaggerAdapterTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$staggerExe" $staggerFixture
if($LASTEXITCODE -ne 0){throw 'Stagger adapter tests failed to compile.'}
& $staggerExe
if($LASTEXITCODE -ne 0){throw 'Stagger adapter tests failed.'}
