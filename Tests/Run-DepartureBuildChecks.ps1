param([string]$Compiler)
$ErrorActionPreference='Stop'
if(!$Compiler){$departureVsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe';$Compiler=(& $departureVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe'|Select-Object -First 1)}
if(!$Compiler){throw 'C# 9+ compiler required.'}
$departureRoot=Split-Path $PSScriptRoot -Parent
$departureOutput=Join-Path ([IO.Path]::GetTempPath()) ('Astella-Departure-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($departureOutput)|Out-Null
$departureSource=[IO.File]::ReadAllText((Join-Path $departureRoot 'Assets/Scripts/CharacterActor.cs'))
$departureStart=$departureSource.IndexOf('public void ResetEncounter(',[StringComparison]::Ordinal)
if($departureStart -lt 0){throw 'Production reset method missing.'}
$departureOpen=$departureSource.IndexOf('{',$departureStart);$departureEnd=$departureOpen+1;$departureDepth=1
while($departureDepth -gt 0 -and $departureEnd -lt $departureSource.Length){if($departureSource[$departureEnd] -eq '{'){$departureDepth++}elseif($departureSource[$departureEnd] -eq '}'){$departureDepth--};$departureEnd++}
if($departureDepth -ne 0){throw 'Incomplete production reset method.'}
$departureBody=$departureSource.Substring($departureStart,$departureEnd-$departureStart)
function Read-DepartureMethod([string]$Source,[string]$Signature) {
    $start=$Source.IndexOf($Signature,[StringComparison]::Ordinal);if($start -lt 0){throw "Missing $Signature"}
    $open=$Source.IndexOf('{',$start);$end=$open+1;$depth=1
    while($depth -gt 0 -and $end -lt $Source.Length){if($Source[$end] -eq '{'){$depth++}elseif($Source[$end] -eq '}'){$depth--};$end++}
    if($depth -ne 0){throw 'Incomplete boundary method'}
    $Source.Substring($start,$end-$start)
}
$departureBotSource=[IO.File]::ReadAllText((Join-Path $departureRoot 'Assets/Scripts/PartyBotAI.cs'))
$departureWorldSource=[IO.File]::ReadAllText((Join-Path $departureRoot 'Assets/Scripts/RaidCombatWorld.cs'))
$departureBotCapture=$departureWorldSource.Substring($departureWorldSource.IndexOf('public partial class PartyBotAI'))
$departureInterval=[regex]::Match($departureBotCapture,'private float ActionInterval =>[^;]+;').Value
if(!$departureInterval){throw 'Native action interval missing'}
$departureBotBody=$departureInterval+"`n"+(Read-DepartureMethod $departureBotCapture 'public void CaptureNetworkCombat(')+"`n"+(Read-DepartureMethod $departureBotSource 'public void RestorePartyLife(')
$departureFixture=Join-Path $departureOutput 'DepartureBuildFixture.cs'
[IO.File]::WriteAllText($departureFixture,[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DepartureBuildTests.cs')).Replace('/* ACTUAL_RESET_ENCOUNTER */',$departureBody).Replace('/* ACTUAL_BOT_BOUNDARY */',$departureBotBody),[Text.UTF8Encoding]::new($false))
$departureSources=@('NineKeyDamageData.cs','NineKeyDamageResources.cs','DamageFieldState.cs','DamagePreparationState.cs','NineKeyHealerData.cs','NineKeyTankData.cs','IrisRescueState.cs','CampaignSaveData.cs','CharacterRosterData.cs','CharacterRosterRules.cs','CharacterFrameworkData.cs','CharacterRuntimeState.cs','AegisPaladinData.cs','AegisRouteData.cs','AegisExtensionData.cs','CombatMetricsData.cs','DashMotion.cs')|ForEach-Object{Join-Path $departureRoot ('Assets/Scripts/'+$_)}
$departureExe=Join-Path $departureOutput 'DepartureBuildTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$departureExe" $departureSources $departureFixture
if($LASTEXITCODE -ne 0){throw 'Departure build checks failed to compile.'}
& $departureExe
if($LASTEXITCODE -ne 0){throw 'Departure build checks failed.'}
