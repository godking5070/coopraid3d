param([string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$Compiler -or !(Test-Path -LiteralPath $Compiler)) { throw 'C# compiler not found.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskJobSource = [IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/AstellaClassJobHUD.cs'))
function Read-JobMethod([string]$Signature) {
    $taskStart = $taskJobSource.IndexOf($Signature, [StringComparison]::Ordinal)
    if ($taskStart -lt 0) { throw "Missing job HUD method: $Signature" }
    $taskOpen = $taskJobSource.IndexOf('{', $taskStart)
    $taskEnd = $taskOpen + 1; $taskDepth = 1
    while ($taskDepth -gt 0 -and $taskEnd -lt $taskJobSource.Length) {
        if ($taskJobSource[$taskEnd] -eq '{') { $taskDepth++ }
        elseif ($taskJobSource[$taskEnd] -eq '}') { $taskDepth-- }
        $taskEnd++
    }
    if ($taskDepth -ne 0) { throw 'Incomplete job HUD method.' }
    $taskJobSource.Substring($taskStart, $taskEnd - $taskStart)
}
$taskPropertyStart = $taskJobSource.IndexOf('public bool HasResource', [StringComparison]::Ordinal)
$taskPropertyEnd = $taskJobSource.IndexOf('private CharacterActor', $taskPropertyStart, [StringComparison]::Ordinal)
if ($taskPropertyStart -lt 0 -or $taskPropertyEnd -le $taskPropertyStart) { throw 'Missing readonly job data snapshot.' }
$taskTemplate = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'AstellaPrismBridgeTests.cs'))
$taskTemplate = $taskTemplate.Replace('/* ACTUAL_JOB_PROPERTIES */', $taskJobSource.Substring($taskPropertyStart, $taskPropertyEnd - $taskPropertyStart))
$taskTemplate = $taskTemplate.Replace('/* ACTUAL_JOB_LATE */', (Read-JobMethod 'private void LateUpdate('))
$taskTemplate = $taskTemplate.Replace('/* ACTUAL_JOB_DISABLE */', (Read-JobMethod 'private void OnDisable('))
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-PrismBridge-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskFixture = Join-Path $taskOutput 'AstellaPrismBridgeFixture.cs'
[IO.File]::WriteAllText($taskFixture, $taskTemplate, [Text.UTF8Encoding]::new($false))
$taskExe = Join-Path $taskOutput 'AstellaPrismBridgeTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" (Join-Path $taskRoot 'Assets/Scripts/AstellaPrismHUDBridge.cs') $taskFixture
if ($LASTEXITCODE -ne 0) { throw 'Prism bridge fixture compilation failed.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Prism bridge checks failed.' }
& (Join-Path $PSScriptRoot 'Run-HotbarInputFeedbackChecks.ps1') -Compiler $Compiler
if ($LASTEXITCODE -ne 0) { throw 'Hotbar input feedback checks failed.' }
