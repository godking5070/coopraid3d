param([string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$Compiler -or !(Test-Path -LiteralPath $Compiler)) { throw 'A C# 9+ compiler is required.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-ActionRules-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskExe = Join-Path $taskOutput 'CombatActionRulesTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" (Join-Path $taskRoot 'Assets/Scripts/CombatActionRules.cs') (Join-Path $PSScriptRoot 'CombatActionRulesTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Combat action rules tests failed to compile.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Combat action rules tests failed.' }
