param([string]$Compiler)
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
$taskSources = @('NineKeyDamageData.cs','NineKeyDamageResources.cs','DamageFieldState.cs','DamagePreparationState.cs','NineKeyHealerData.cs','NineKeyTankData.cs', 'IrisRescueState.cs', 'CampaignSaveData.cs', 'CampaignStorage.cs', 'LocalSettingsData.cs', 'CharacterRosterData.cs', 'CharacterRosterRules.cs', 'CharacterFrameworkData.cs', 'CharacterRuntimeState.cs', 'AegisPaladinData.cs', 'AegisRouteData.cs', 'AegisExtensionData.cs', 'CombatMetricsData.cs', 'DashMotion.cs', 'RaidRunFlow.cs', 'RaidCombatProtocol.cs', 'RaidNetworkProtocol.cs', 'RaidRecoveryState.cs', 'PartyBattleState.cs') | ForEach-Object { Join-Path $taskRoot ('Assets/Scripts/' + $_) }
& $Compiler /nologo /langversion:9.0 /target:exe /reference:System.Web.Extensions.dll "/out:$taskExe" $taskSources (Join-Path $PSScriptRoot 'CampaignSaveTests.cs') (Join-Path $PSScriptRoot 'Part1SaveTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Campaign save tests failed to compile.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Campaign save tests failed.' }

# Keep full-run phase boundaries in the same command as durable checkpoint recovery.
# Compile separately because both test suites have their own Main entry point.
$taskFlowExe = Join-Path $taskOutput 'RaidFlowTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskFlowExe" $taskSources (Join-Path $PSScriptRoot 'RaidFlowTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Raid flow tests failed to compile.' }
& $taskFlowExe
if ($LASTEXITCODE -ne 0) { throw 'Raid flow tests failed.' }

& (Join-Path $PSScriptRoot 'Run-CampaignNewRunChecks.ps1') -Compiler $Compiler
if ($LASTEXITCODE -ne 0) { throw 'New-run confirmation checks failed.' }

# Exercise the real checkpoint adapter and damage-kit registration on a fresh actor.
function Read-CampaignRestoreMethod([string]$Source,[string]$Signature,[switch]$Expression) {
    $start=$Source.IndexOf($Signature,[StringComparison]::Ordinal)
    if($start -lt 0){throw "Missing method $Signature"}
    if($Expression){$end=$Source.IndexOf(';',$start)+1;return $Source.Substring($start,$end-$start)}
    $open=$Source.IndexOf('{',$start);$end=$open+1;$depth=1
    while($depth -gt 0 -and $end -lt $Source.Length){if($Source[$end] -eq '{'){$depth++}elseif($Source[$end] -eq '}'){$depth--};$end++}
    if($depth -ne 0){throw 'Incomplete campaign adapter method'}
    $Source.Substring($start,$end-$start)
}
$taskAdapterSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/CampaignAdapters.cs'))
$taskMethods=(Read-CampaignRestoreMethod $taskAdapterSource 'internal bool RestoreCampaignBuild(')+"`n"+(Read-CampaignRestoreMethod $taskAdapterSource 'internal bool RestoreCampaignState(')
$taskKitSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageKit.cs'))
$taskAwake=Read-CampaignRestoreMethod $taskKitSource 'private void Awake('
$taskFixture=Join-Path $taskOutput 'DamageCampaignRestoreFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageCampaignRestoreTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_CAMPAIGN_RESTORE */',$taskMethods).Replace('/* ACTUAL_DAMAGE_AWAKE */',$taskAwake),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageCampaignRestoreTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe /reference:System.Web.Extensions.dll "/out:$taskAdapterExe" $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'DPS campaign restore tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'DPS campaign restore tests failed.'}

# Pending-clear continuation must restore cumulative history before recapturing
# the real settlement; pure checkpoint fixtures alone cannot exercise this path.
$taskStageSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/StageManager.cs'))
$taskCampaignSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/CampaignRuntime.cs'))
$taskSettlementTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'CampaignSettlementRestoreTests.cs'))
$taskSettlementTemplate=$taskSettlementTemplate.Replace('/* ACTUAL_SETTLEMENT_RESTORE */',(Read-CampaignRestoreMethod $taskStageSource 'internal bool RestoreSettlementFromLobby('))
$taskSettlementTemplate=$taskSettlementTemplate.Replace('/* ACTUAL_CARD_CALLBACK */',(Read-CampaignRestoreMethod $taskStageSource 'private void OnRewardCardChosenAdvanceToNextStage('))
$taskSettlementTemplate=$taskSettlementTemplate.Replace('/* ACTUAL_CARD_JOURNAL */',(Read-CampaignRestoreMethod $taskCampaignSource 'public void CardChosen('))
$taskSettlementTemplate=$taskSettlementTemplate.Replace('/* ACTUAL_SETTLEMENT_CAPTURE */',(Read-CampaignRestoreMethod $taskCampaignSource 'private void SaveSettlementSnapshot('))
$taskSettlementTemplate=$taskSettlementTemplate.Replace('/* ACTUAL_METRIC_CAPTURE */',(Read-CampaignRestoreMethod $taskCampaignSource 'private CampaignMetricRecord[] CaptureMetrics(' -Expression))
$taskSettlementTemplate=$taskSettlementTemplate.Replace('/* ACTUAL_BOUNDARY_RETRY */',(Read-CampaignRestoreMethod $taskCampaignSource 'public void RetrySave('))
$taskSettlementTemplate=$taskSettlementTemplate.Replace('/* ACTUAL_SETTLEMENT_CLEAR */',(Read-CampaignRestoreMethod $taskCampaignSource 'private void ClearSettlement('))
$taskSettlementTemplate=$taskSettlementTemplate.Replace('/* ACTUAL_SAVE_LABEL */',(Read-CampaignRestoreMethod $taskCampaignSource 'public string LastSaveLabel' -Expression))
$taskSettlementFixture=Join-Path $taskOutput 'CampaignSettlementRestoreFixture.cs'
[IO.File]::WriteAllText($taskSettlementFixture,$taskSettlementTemplate,[Text.UTF8Encoding]::new($false))
$taskSettlementExe=Join-Path $taskOutput 'CampaignSettlementRestoreTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe /reference:System.Web.Extensions.dll "/out:$taskSettlementExe" $taskSources $taskSettlementFixture
if($LASTEXITCODE -ne 0){throw 'Pending settlement restore tests failed to compile.'}
& $taskSettlementExe
if($LASTEXITCODE -ne 0){throw 'Pending settlement restore tests failed.'}
