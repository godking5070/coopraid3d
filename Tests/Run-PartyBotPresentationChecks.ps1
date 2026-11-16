param([string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$Compiler -or !(Test-Path -LiteralPath $Compiler)) { throw 'C# compiler not found.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskSource = [IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/PartyBotAI.cs'))
function Read-BotMethod([string]$Signature) {
    $taskStart = $taskSource.IndexOf($Signature, [StringComparison]::Ordinal)
    if ($taskStart -lt 0) { throw "Missing method: $Signature" }
    $taskOpen = $taskSource.IndexOf('{', $taskStart)
    $taskEnd = $taskOpen + 1; $taskDepth = 1
    while ($taskDepth -gt 0 -and $taskEnd -lt $taskSource.Length) {
        if ($taskSource[$taskEnd] -eq '{') { $taskDepth++ }
        elseif ($taskSource[$taskEnd] -eq '}') { $taskDepth-- }
        $taskEnd++
    }
    if ($taskDepth -ne 0) { throw 'Incomplete bot method.' }
    $taskSource.Substring($taskStart, $taskEnd - $taskStart)
}
$taskTemplate = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'PartyBotPresentationTests.cs'))
$taskTemplate = $taskTemplate.Replace('/* ACTUAL_OVERHEAD_NAME */', (Read-BotMethod 'private string OverheadDisplayName('))
$taskTemplate = $taskTemplate.Replace('/* ACTUAL_OVERHEAD_UPDATE */', (Read-BotMethod 'private void UpdateOverheadHpBar(')).Replace('/* ACTUAL_LATE_UPDATE */', (Read-BotMethod 'private void LateUpdate('))
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-BotPresentation-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskFixture = Join-Path $taskOutput 'PartyBotPresentationFixture.cs'
[IO.File]::WriteAllText($taskFixture, $taskTemplate, [Text.UTF8Encoding]::new($false))
$taskExe = Join-Path $taskOutput 'PartyBotPresentationTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" $taskFixture
if ($LASTEXITCODE -ne 0) { throw 'Bot presentation fixture compilation failed.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Bot presentation checks failed.' }
