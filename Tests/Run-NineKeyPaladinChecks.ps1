param([string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$Compiler) { throw 'A C# 9+ compiler is required.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-NineKeyPaladin-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskExe = Join-Path $taskOutput 'NineKeyPaladinTests.exe'
$taskWorld = [IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/RaidCombatWorld.cs'))
$taskStart = $taskWorld.IndexOf('public bool TransferCombatThreat(', [StringComparison]::Ordinal)
$taskEnd = $taskWorld.IndexOf('public RaidCombatEnemy CaptureNetworkCombat()', $taskStart, [StringComparison]::Ordinal)
if ($taskStart -lt 0 -or $taskEnd -le $taskStart) { throw 'Threat method source boundaries missing.' }
$taskFixture = Join-Path $taskOutput 'NineKeyPaladinFixture.cs'
$taskTemplate = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'NineKeyPaladinTests.cs'))
$taskPaladin = [IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/AegisPaladin.cs'))
$taskPolicyStart = $taskPaladin.IndexOf('private static readonly CombatComboResourcePolicy NineKeyComboResource=', [StringComparison]::Ordinal)
$taskPolicyEnd = $taskPaladin.IndexOf('public string RouteId', $taskPolicyStart, [StringComparison]::Ordinal)
if ($taskPolicyStart -lt 0 -or $taskPolicyEnd -le $taskPolicyStart) { throw 'Combo policy boundaries missing.' }
$taskTemplate = $taskTemplate.Replace('/* ACTUAL_COMBO_POLICIES */', $taskPaladin.Substring($taskPolicyStart, $taskPolicyEnd-$taskPolicyStart))
[IO.File]::WriteAllText($taskFixture, $taskTemplate.Replace('/* ACTUAL_THREAT_METHODS */', $taskWorld.Substring($taskStart, $taskEnd-$taskStart)), [Text.UTF8Encoding]::new($false))
$taskSources = @('CharacterFrameworkData.cs', 'CharacterRuntimeState.cs', 'AegisPaladinData.cs', 'AegisRouteData.cs', 'AegisExtensionData.cs', 'CombatActionRules.cs', 'CombatMetricsData.cs', 'CombatRangeMath.cs', 'DashMotion.cs') | ForEach-Object { Join-Path $taskRoot ('Assets/Scripts/' + $_) }
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" $taskSources $taskFixture
if ($LASTEXITCODE -ne 0) { throw 'Nine-key paladin tests failed to compile.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Nine-key paladin tests failed.' }
& (Join-Path $PSScriptRoot 'Run-CharacterSelectionChecks.ps1') -Compiler $Compiler
