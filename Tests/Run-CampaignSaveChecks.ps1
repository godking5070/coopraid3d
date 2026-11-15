param([string]$Compiler, [switch]$TrainingAccessOnly)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$Compiler) { throw 'A C# 9+ compiler is required.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-CampaignChecks-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskExe = Join-Path $taskOutput 'CampaignSaveTests.exe'
$taskSources = @('IrisRescueState.cs', 'CampaignSaveData.cs', 'CampaignStorage.cs', 'LocalSettingsData.cs', 'CharacterRosterData.cs', 'CharacterRosterRules.cs', 'CharacterFrameworkData.cs', 'CharacterRuntimeState.cs', 'AegisPaladinData.cs', 'AegisRouteData.cs', 'AegisExtensionData.cs', 'CombatMetricsData.cs', 'DashMotion.cs', 'RaidRunFlow.cs', 'RaidCombatProtocol.cs', 'RaidNetworkProtocol.cs', 'RaidRecoveryState.cs', 'PartyBattleState.cs') | ForEach-Object { Join-Path $taskRoot ('Assets/Scripts/' + $_) }
& $Compiler /nologo /langversion:9.0 /target:exe /reference:System.Web.Extensions.dll "/out:$taskExe" $taskSources (Join-Path $PSScriptRoot 'CampaignSaveTests.cs') (Join-Path $PSScriptRoot 'Part1SaveTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Campaign save tests failed to compile.' }
if ($TrainingAccessOnly) { & $taskExe --training-access } else { & $taskExe }
if ($LASTEXITCODE -ne 0) { throw 'Campaign save tests failed.' }
