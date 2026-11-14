param([string]$Compiler)
$ErrorActionPreference='Stop'
if(!$Compiler){$summaryVsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe';$Compiler=(& $summaryVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)}
if(!$Compiler){throw 'A C# 9+ compiler is required.'}
$summaryRoot=Split-Path $PSScriptRoot -Parent
$summaryOutput=Join-Path ([IO.Path]::GetTempPath()) ('Astella-PartyBuildSummary-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($summaryOutput)|Out-Null
$summarySources=@('RaidPartyBuildSummary.cs','NineKeyTankData.cs','NineKeyHealerData.cs','NineKeyDamageData.cs','NineKeyDamageResources.cs','DamagePreparationState.cs','DamageTacticalState.cs','DamageWindowState.cs','DamageMirrorState.cs','DamageArrowState.cs','DamageFieldState.cs','DamagePrismState.cs','DamageCombatState.cs','CharacterEffectState.cs','CharacterRosterData.cs','CharacterRosterRules.cs','IrisRescueState.cs','CharacterFrameworkData.cs','CharacterRuntimeState.cs','AegisPaladinData.cs','AegisRouteData.cs','AegisExtensionData.cs','CombatMetricsData.cs','DashMotion.cs')|ForEach-Object{Join-Path $summaryRoot ('Assets/Scripts/'+$_)}
$summaryExe=Join-Path $summaryOutput 'PartyBuildSummaryTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$summaryExe" $summarySources (Join-Path $PSScriptRoot 'PartyBuildSummaryTests.cs')
if($LASTEXITCODE -ne 0){throw 'Party build summary tests failed to compile.'}
& $summaryExe
if($LASTEXITCODE -ne 0){throw 'Party build summary tests failed.'}
