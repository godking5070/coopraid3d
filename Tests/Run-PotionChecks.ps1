param([Parameter(Mandatory = $true)][string]$OriginalProject, [string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-Potion-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskExe = Join-Path $taskOutput 'PotionTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" (Join-Path $taskRoot 'Assets/Scripts/PotionState.cs') (Join-Path $taskRoot 'Assets/Scripts/CombatMetricsData.cs') (Join-Path $PSScriptRoot 'PotionTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Potion tests failed to compile.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Potion tests failed.' }
& (Join-Path $PSScriptRoot 'Run-DashChecks.ps1') -OriginalProject $OriginalProject -Compiler $Compiler
