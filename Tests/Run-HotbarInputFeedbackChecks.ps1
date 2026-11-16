param([string]$Compiler, [string]$SourceFile)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$Compiler -or !(Test-Path -LiteralPath $Compiler)) { throw 'C# compiler not found.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
if (!$SourceFile) { $SourceFile = Join-Path $taskRoot 'Assets/Scripts/SkillHotbarUI.cs' }
$taskSource = [IO.File]::ReadAllText($SourceFile)
$taskStart = $taskSource.IndexOf('internal sealed class HotbarInputFeedback', [StringComparison]::Ordinal)
if ($taskStart -lt 0) { throw 'Missing actual hotbar feedback helper.' }
$taskOpen = $taskSource.IndexOf('{', $taskStart)
$taskEnd = $taskOpen + 1; $taskDepth = 1
while ($taskDepth -gt 0 -and $taskEnd -lt $taskSource.Length) {
    if ($taskSource[$taskEnd] -eq '{') { $taskDepth++ }
    elseif ($taskSource[$taskEnd] -eq '}') { $taskDepth-- }
    $taskEnd++
}
if ($taskDepth -ne 0) { throw 'Incomplete actual hotbar feedback helper.' }
$taskTemplate = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'HotbarInputFeedbackTests.cs'))
$taskTemplate = $taskTemplate.Replace('/* ACTUAL_FEEDBACK */', $taskSource.Substring($taskStart, $taskEnd - $taskStart))
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-HotbarFeedback-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskFixture = Join-Path $taskOutput 'HotbarInputFeedbackFixture.cs'
[IO.File]::WriteAllText($taskFixture, $taskTemplate, [Text.UTF8Encoding]::new($false))
$taskExe = Join-Path $taskOutput 'HotbarInputFeedbackTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" $taskFixture
if ($LASTEXITCODE -ne 0) { throw 'Hotbar input feedback fixture compilation failed.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Hotbar input feedback checks failed.' }
