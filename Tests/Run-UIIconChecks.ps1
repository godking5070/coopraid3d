param([string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$Compiler -or !(Test-Path -LiteralPath $Compiler)) { throw 'C# compiler not found.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-UIIcon-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskExe = Join-Path $taskOutput 'UIIconTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" (Join-Path $taskRoot 'Assets/Scripts/RaidUIIcon.cs') (Join-Path $PSScriptRoot 'UIIconTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'UI icon fixture compilation failed.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'UI icon checks failed.' }
Write-Output 'Boundary: actual RaidUIIcon source with managed UI stubs; no Unity rendering or lifecycle verification.'
