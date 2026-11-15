param([string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$Compiler -or !(Test-Path -LiteralPath $Compiler)) { throw 'A C# 9+ compiler is required.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskSource = [IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/CharacterActor.cs'))
function Read-ActionMethod([string]$Signature) {
    $taskStart = $taskSource.IndexOf($Signature, [StringComparison]::Ordinal)
    if ($taskStart -lt 0) { throw "Missing method: $Signature" }
    $taskOpen = $taskSource.IndexOf('{', $taskStart, [StringComparison]::Ordinal)
    $taskEnd = $taskOpen + 1; $taskDepth = 1
    while ($taskDepth -gt 0 -and $taskEnd -lt $taskSource.Length) {
        if ($taskSource[$taskEnd] -eq '{') { $taskDepth++ }
        elseif ($taskSource[$taskEnd] -eq '}') { $taskDepth-- }
        $taskEnd++
    }
    if ($taskDepth -ne 0) { throw 'Incomplete action method.' }
    $taskSource.Substring($taskStart, $taskEnd - $taskStart)
}
$taskMethods = (@('private bool Apply(', 'public bool ResolveComboResource(') | ForEach-Object { Read-ActionMethod $_ }) -join "`n"
$taskTemplate = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'ActionExecutionTests.cs'))
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-ActionExecution-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskFixture = Join-Path $taskOutput 'ActionExecutionFixture.cs'
[IO.File]::WriteAllText($taskFixture, $taskTemplate.Replace('/* ACTUAL_ACTION_METHODS */', $taskMethods), [Text.UTF8Encoding]::new($false))
$taskExe = Join-Path $taskOutput 'ActionExecutionTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" (Join-Path $taskRoot 'Assets/Scripts/CombatActionRules.cs') $taskFixture
if ($LASTEXITCODE -ne 0) { throw 'Action execution tests failed to compile.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Action execution tests failed.' }
