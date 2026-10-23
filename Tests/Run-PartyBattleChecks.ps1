param([Parameter(Mandatory = $true)][string]$OriginalProject, [string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-PartyBattle-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskExe = Join-Path $taskOutput 'PartyBattleTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" (Join-Path $taskRoot 'Assets/Scripts/PartyBattleState.cs') (Join-Path $taskRoot 'Assets/Scripts/PotionState.cs') (Join-Path $taskRoot 'Assets/Scripts/CombatMetricsData.cs') (Join-Path $PSScriptRoot 'PartyBattleTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Party battle tests failed to compile.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Party battle tests failed.' }
& (Join-Path $PSScriptRoot 'Run-PotionChecks.ps1') -OriginalProject $OriginalProject -Compiler $Compiler
