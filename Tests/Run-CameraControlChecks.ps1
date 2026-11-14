param([Parameter(Mandatory = $true)][string]$OriginalProject, [string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-Camera-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskExe = Join-Path $taskOutput 'CameraControlTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" (Join-Path $taskRoot 'Assets/Scripts/CameraControlMath.cs') (Join-Path $PSScriptRoot 'CameraControlTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Camera tests failed to compile.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Camera tests failed.' }
& (Join-Path $PSScriptRoot 'Run-MeleeRangeChecks.ps1') -OriginalProject $OriginalProject -Compiler $Compiler
