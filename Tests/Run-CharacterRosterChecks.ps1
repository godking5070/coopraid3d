param([string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$Compiler) { throw 'A C# 9+ compiler is required; pass -Compiler.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-Extensions-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskExe = Join-Path $taskOutput 'CharacterRosterTests.exe'
$taskSources = @('CharacterRosterData.cs', 'CharacterRosterRules.cs', 'IrisRescueState.cs', 'CharacterEffectState.cs', 'CharacterFrameworkData.cs', 'CharacterRuntimeState.cs', 'AegisPaladinData.cs', 'AegisRouteData.cs', 'AegisExtensionData.cs', 'CombatMetricsData.cs', 'DashMotion.cs', 'RaidRunFlow.cs', 'RaidCombatProtocol.cs', 'RaidNetworkProtocol.cs', 'PartyBattleState.cs') | ForEach-Object { Join-Path $taskRoot ('Assets/Scripts/' + $_) }
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" $taskSources (Join-Path $PSScriptRoot 'CharacterRosterTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Character roster tests failed to compile.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Character roster tests failed.' }
