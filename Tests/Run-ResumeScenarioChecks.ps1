param([string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $scenarioVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $scenarioVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$Compiler) { throw 'A C# 9+ compiler is required.' }
$scenarioRoot = Split-Path $PSScriptRoot -Parent
$scenarioOutput = Join-Path ([IO.Path]::GetTempPath()) ('Astella-ResumeParty-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scenarioOutput) | Out-Null
$scenarioExe = Join-Path $scenarioOutput 'ResumeScenarioTests.exe'
$scenarioSources = @('NineKeyDamageData.cs', 'NineKeyDamageResources.cs', 'DamageFieldState.cs', 'DamagePreparationState.cs', 'NineKeyHealerData.cs', 'NineKeyTankData.cs', 'IrisRescueState.cs', 'CampaignSaveData.cs', 'CampaignStorage.cs', 'LocalSettingsData.cs', 'CharacterRosterData.cs', 'CharacterRosterRules.cs', 'CharacterFrameworkData.cs', 'CharacterRuntimeState.cs', 'AegisPaladinData.cs', 'AegisRouteData.cs', 'AegisExtensionData.cs', 'CombatMetricsData.cs', 'DashMotion.cs', 'RaidRunFlow.cs', 'RaidCombatProtocol.cs', 'RaidNetworkProtocol.cs', 'RaidRecoveryState.cs', 'PartyBattleState.cs') | ForEach-Object { Join-Path $scenarioRoot ('Assets/Scripts/' + $_) }
& $Compiler /nologo /langversion:9.0 /target:exe /reference:System.Web.Extensions.dll "/out:$scenarioExe" $scenarioSources (Join-Path $PSScriptRoot 'ResumeScenarioTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Fixed-party scenario checks failed to compile.' }
& $scenarioExe (Join-Path $PSScriptRoot 'Scenarios/Part1-FourPlayer.json') $scenarioOutput
if ($LASTEXITCODE -ne 0) { throw 'Fixed-party scenario checks failed.' }
