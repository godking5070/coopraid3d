param([string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$Compiler -or !(Test-Path -LiteralPath $Compiler)) { throw 'C# compiler not found.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskSource = [IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/CombatMetricsUI.cs'))
function Read-SourceMethod([string]$Signature) {
    $taskStart = $taskSource.IndexOf($Signature, [StringComparison]::Ordinal)
    if ($taskStart -lt 0) { throw "Missing source method: $Signature" }
    $taskOpen = $taskSource.IndexOf('{', $taskStart)
    $taskEnd = $taskOpen + 1
    $taskDepth = 1
    while ($taskDepth -gt 0 -and $taskEnd -lt $taskSource.Length) {
        if ($taskSource[$taskEnd] -eq '{') { $taskDepth++ }
        elseif ($taskSource[$taskEnd] -eq '}') { $taskDepth-- }
        $taskEnd++
    }
    if ($taskDepth -ne 0) { throw "Incomplete source method: $Signature" }
    return $taskSource.Substring($taskStart, $taskEnd - $taskStart)
}
$taskStart = $taskSource.IndexOf('private static CombatMetricsUI _resultsOwner;', [StringComparison]::Ordinal)
$taskEnd = $taskSource.IndexOf('private CombatMetricsManager _metrics;', [StringComparison]::Ordinal)
if ($taskStart -lt 0 -or $taskEnd -le $taskStart) { throw 'Modal source state boundaries missing.' }
$taskState = $taskSource.Substring($taskStart, $taskEnd - $taskStart)
$taskMethods = (@('private void OnDisable()', 'private void OpenResultsOverlay()', 'public void CloseResults()',
    'private void EnsureUI()', 'private Button AuthoredResultButton(', 'public void BindResultButtons()',
    'private void ShowLatestResults()', 'private void ShowRunResults()') | ForEach-Object { Read-SourceMethod $_ }) -join "`n"
$taskTemplate = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'CombatMetricsModalTests.cs'))
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-Modal-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskFixture = Join-Path $taskOutput 'ModalFixture.cs'
[IO.File]::WriteAllText($taskFixture, $taskTemplate.Replace('/* ACTUAL_MODAL_STATE */', $taskState).Replace('/* ACTUAL_MODAL_METHODS */', $taskMethods), [Text.UTF8Encoding]::new($false))
$taskExe = Join-Path $taskOutput 'ModalTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" $taskFixture
if ($LASTEXITCODE -ne 0) { throw 'Modal fixture compilation failed.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Modal lifecycle checks failed.' }
Write-Output 'Boundary: actual modal state and methods with managed visibility/frame stubs; Unity callbacks, destroyed-object semantics and input dispatch require play verification.'
