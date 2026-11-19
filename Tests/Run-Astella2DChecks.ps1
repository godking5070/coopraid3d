param([string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (Test-Path -LiteralPath $taskVsWhere) {
        $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
    }
}
if (!$Compiler) { throw 'A C# 9+ compiler is required; pass -Compiler.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('Astella-2D-Geometry-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskExe = Join-Path $taskOutput 'Astella2DGeometryTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" `
    (Join-Path $taskRoot 'Assets/Astella2D/Runtime/Astella2DCombatRules.cs') `
    (Join-Path $PSScriptRoot 'Astella2DGeometryTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Astella 2D geometry checks failed to compile.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Astella 2D geometry checks failed.' }
