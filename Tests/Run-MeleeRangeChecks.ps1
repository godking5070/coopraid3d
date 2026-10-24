param([Parameter(Mandatory = $true)][string]$OriginalProject, [string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-Step11-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskExe = Join-Path $taskOutput 'MeleeRangeTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" (Join-Path $taskRoot 'Assets/Scripts/CombatRangeMath.cs') (Join-Path $PSScriptRoot 'MeleeRangeTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Melee range tests failed to compile.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Melee range tests failed.' }
# Also compile all runtime/editor scripts against the original Unity references and run Step 10 regressions.
& (Join-Path $PSScriptRoot 'Run-CombatMetricsChecks.ps1') -OriginalProject $OriginalProject -Compiler $Compiler
