param([string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$Compiler) { throw 'A C# 9+ compiler is required.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
function Read-SelectionMethod([string]$File, [string]$Signature) {
    $taskText = [IO.File]::ReadAllText((Join-Path $taskRoot $File))
    $taskStart = $taskText.IndexOf($Signature, [StringComparison]::Ordinal)
    if ($taskStart -lt 0) { throw "Missing method: $Signature" }
    $taskOpen = $taskText.IndexOf('{', $taskStart, [StringComparison]::Ordinal)
    $taskEnd = $taskOpen + 1; $taskDepth = 1
    while ($taskDepth -gt 0 -and $taskEnd -lt $taskText.Length) {
        if ($taskText[$taskEnd] -eq '{') { $taskDepth++ }
        elseif ($taskText[$taskEnd] -eq '}') { $taskDepth-- }
        $taskEnd++
    }
    if ($taskDepth -ne 0) { throw 'Incomplete selection method.' }
    $taskText.Substring($taskStart, $taskEnd - $taskStart)
}
$taskTemplate = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'CharacterSelectionTests.cs'))
$taskTemplate = $taskTemplate.Replace('/* ACTUAL_HANDLER_VALIDATION */', (Read-SelectionMethod 'Assets/Scripts/CharacterActor.cs' 'private bool HasRequiredSkillHandlers('))
$taskTemplate = $taskTemplate.Replace('/* ACTUAL_PICKER_SELECT */', (Read-SelectionMethod 'Assets/Scripts/AegisRouteSelectionUI.cs' 'private void Select('))
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('Astella-CharacterSelection-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskFixture = Join-Path $taskOutput 'CharacterSelectionFixture.cs'
[IO.File]::WriteAllText($taskFixture, $taskTemplate, [Text.UTF8Encoding]::new($false))
$taskExe = Join-Path $taskOutput 'CharacterSelectionTests.exe'
$taskSources = @('CharacterFrameworkData.cs','CharacterRuntimeState.cs','AegisPaladinData.cs','AegisRouteData.cs','AegisExtensionData.cs','CombatActionRules.cs','CombatMetricsData.cs','CombatRangeMath.cs','DashMotion.cs') | ForEach-Object { Join-Path $taskRoot ('Assets/Scripts/' + $_) }
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" $taskSources $taskFixture
if ($LASTEXITCODE -ne 0) { throw 'Character selection tests failed to compile.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Character selection tests failed.' }
