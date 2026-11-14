param([string]$Compiler)
$ErrorActionPreference='Stop'
if(!$Compiler){$taskVsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe';$Compiler=(& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)}
if(!$Compiler){throw 'A C# 9+ compiler is required.'}
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskOutput=Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-NineKeyDamage-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput)|Out-Null
$taskExe=Join-Path $taskOutput 'DamageTests.exe'
$taskSources=@('NineKeyDamageData.cs','NineKeyDamageResources.cs','DamagePreparationState.cs','DamageTacticalState.cs','DamageWindowState.cs','DamageMirrorState.cs','DamageArrowState.cs','DamageFieldState.cs','DamagePrismState.cs','DamageCombatState.cs','CharacterEffectState.cs','CharacterRosterData.cs','CharacterRosterRules.cs','IrisRescueState.cs','CharacterFrameworkData.cs','CharacterRuntimeState.cs','AegisPaladinData.cs','AegisRouteData.cs','AegisExtensionData.cs','CombatMetricsData.cs','DashMotion.cs') | ForEach-Object {Join-Path $taskRoot ('Assets/Scripts/'+$_)}
& $Compiler /nologo /langversion:9.0 /target:exe /r:System.Web.Extensions.dll "/out:$taskExe" $taskSources (Join-Path $PSScriptRoot 'NineKeyDamageTests.cs')
if($LASTEXITCODE -ne 0){throw 'DPS tests failed to compile.'}
& $taskExe
if($LASTEXITCODE -ne 0){throw 'DPS tests failed.'}

# Execute real event-gate methods with host/world seams; not text-matching tests.
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/BossDamageActionEvents.cs'))
function Read-DamageBossMethod([string]$Signature) {
    $taskStart=$taskBossSource.IndexOf($Signature,[StringComparison]::Ordinal)
    if($taskStart -lt 0){throw "Missing boss method: $Signature"}
    $taskOpen=$taskBossSource.IndexOf('{',$taskStart);$taskEnd=$taskOpen+1;$taskDepth=1
    while($taskDepth -gt 0 -and $taskEnd -lt $taskBossSource.Length){if($taskBossSource[$taskEnd] -eq '{'){$taskDepth++}elseif($taskBossSource[$taskEnd] -eq '}'){$taskDepth--};$taskEnd++}
    if($taskDepth -ne 0){throw 'Incomplete boss method'}
    $taskBossSource.Substring($taskStart,$taskEnd-$taskStart)
}
$taskMethods=(@('private void NotifyDamageCastEnded(', 'private void NotifyDamagePatternMoveEnded(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n"
$taskFixture=Join-Path $taskOutput 'DamageBossEventFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageBossEventTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_BOSS_ACTION_METHODS */',$taskMethods),[Text.UTF8Encoding]::new($false))
$taskBossExe=Join-Path $taskOutput 'DamageBossEventTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskBossExe" $taskFixture
if($LASTEXITCODE -ne 0){throw 'Boss DPS event tests failed to compile.'}
& $taskBossExe
if($LASTEXITCODE -ne 0){throw 'Boss DPS event tests failed.'}

$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageKit.cs'))
$taskMethods=Read-DamageBossMethod 'private void FinishReaperCombo('
$taskProtection=Read-DamageBossMethod 'private void ReaperProtection('
$taskFixture=Join-Path $taskOutput 'DamageReaperAdapterFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageReaperAdapterTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_REAPER_COMPLETION */',$taskMethods).Replace('/* ACTUAL_REAPER_PROTECTION */',$taskProtection),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageReaperAdapterTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'Reaper adapter tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'Reaper adapter tests failed.'}

$taskMethods=Read-DamageBossMethod 'private bool ReturnToAnchor('
$taskFixture=Join-Path $taskOutput 'DamageReturnAdapterFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageReturnAdapterTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_RETURN_METHOD */',$taskMethods),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageReturnAdapterTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'Return adapter tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'Return adapter tests failed.'}

$taskMethods=Read-DamageBossMethod 'private bool SeedInput('
$taskFixture=Join-Path $taskOutput 'DamageSeedAdapterFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageSeedAdapterTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_SEED_INPUT */',$taskMethods),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageSeedAdapterTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'Seed adapter tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'Seed adapter tests failed.'}

$taskMethods=Read-DamageBossMethod 'private bool TempestThird('
$taskMethods+="`n"+((@('public bool IsFourth(', 'private void UpdateFourthOpportunity(', 'private bool FourthStrike(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n")
$taskSavedSource=$taskBossSource
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageContext.cs'))
$taskCost=Read-DamageBossMethod 'public float CostScale('
$taskBossSource=$taskSavedSource
$taskFixture=Join-Path $taskOutput 'DamageTempestAdapterFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageTempestAdapterTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_TEMPEST_THIRD */',$taskMethods).Replace('/* ACTUAL_TEMPEST_COST */',$taskCost),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageTempestAdapterTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'Tempest third adapter tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'Tempest third adapter tests failed.'}

$taskMethods=(@('private void ScheduleDamage(', 'private void TickScheduledDamage(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n"
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageRoutes.cs'))
$taskMethods+="`n"+((@('private void DetonateGrenade(', 'private void ResolveSeal(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n")
$taskFixture=Join-Path $taskOutput 'DamageScheduledAdapterFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageScheduledAdapterTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_SCHEDULE_METHODS */',$taskMethods),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageScheduledAdapterTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe"  $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'Scheduled damage adapter tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'Scheduled damage adapter tests failed.'}

$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageKit.cs'))
$taskEffect=Read-DamageBossMethod 'private void Effect('
$taskEffect+="`n"+(Read-DamageBossMethod 'private void Mark(')
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageSupport.cs'))
$taskMethods=$taskEffect+"`n"+((@('private void SignalTempestResponse(', 'private void RespondToTempest(', 'public static void OnDirectAttack(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n")
$taskMethods=$taskMethods.Replace('GetComponent<NineKeyDamageKit>()','GetComponent<DamageTempestResponseTests>()')
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageContext.cs'))
$taskMethods+="`n"+((@('private float InstallationDuration(', 'private float TempestMarkDuration(', 'private void ProtectAfterRoot(', 'public void MovementStarted(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n")
$taskFixture=Join-Path $taskOutput 'DamageTempestResponseFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageTempestResponseTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_RESPONSE_METHODS */',$taskMethods),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageTempestResponseTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe"  $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'Tempest response tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'Tempest response tests failed.'}

$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageKit.cs'))
$taskMethods=(@('private void Effect(', 'private float TacticalDuration(', 'private bool PersonalDefense(', 'private float RoleProtectionDuration(', 'private bool RoleCounter(', 'private bool RoleStagger(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n"
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageContext.cs'))
$taskMethods+="`n"+(Read-DamageBossMethod 'private bool CounterRecovering(')
$taskFixture=Join-Path $taskOutput 'DamageAuxiliaryAdapterFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageAuxiliaryAdapterTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_AUXILIARY_METHODS */',$taskMethods),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageAuxiliaryAdapterTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe"  $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'Auxiliary adapter tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'Auxiliary adapter tests failed.'}

$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageTick.cs'))
$taskMethods=(@('private void TickArrows(', 'private bool ArrowAttack(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n"
$taskFixture=Join-Path $taskOutput 'DamageArrowAdapterFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageArrowAdapterTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_ARROW_METHOD */',$taskMethods),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageArrowAdapterTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'Arrow adapter tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'Arrow adapter tests failed.'}

$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageKit.cs'))
$taskMethods=(@('private float SplitResidualCloud(', 'private void TickResidualClouds(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n"
$taskFixture=Join-Path $taskOutput 'DamageResidualCloudFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageResidualCloudTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_CLOUD_METHODS */',$taskMethods),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageResidualCloudTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe"  $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'Residual cloud tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'Residual cloud tests failed.'}

$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageContext.cs'))
$taskMethods=(@('public bool AiWantsIdentity(', 'public CharacterSlot AiCombo(', 'private double AiGravityDistanceSquared(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n"
$taskFixture=Join-Path $taskOutput 'DamageAiFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageAiTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_AI_IDENTITY */',$taskMethods),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageAiTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'DPS AI tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'DPS AI tests failed.'}
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/RaidReplacementAI.cs'))
$taskMethods=(@('private static bool DamageCounterReady(', 'private static void FaceDamageCounter(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n"
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/CombatCounterTarget.cs'))
$taskCounterMethods=Read-DamageBossMethod 'public bool CanCounter('
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/PlayerController.cs'))
$taskFacingMethod=Read-DamageBossMethod 'public void FaceAttackTarget('
$taskFixture=Join-Path $taskOutput 'DamageAiCounterFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageAiCounterTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_AI_COUNTER */',$taskMethods).Replace('/* ACTUAL_COUNTER_ELIGIBILITY */',$taskCounterMethods).Replace('/* ACTUAL_ATTACK_FACING */',$taskFacingMethod),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageAiCounterTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'DPS AI counter tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'DPS AI counter tests failed.'}
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/PlayerController.cs'))
$taskMethods=Read-DamageBossMethod 'public float CalculateFinalDamageTaken('
$taskFixture=Join-Path $taskOutput 'DamageDefenseAdapterFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageDefenseAdapterTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_DAMAGE_DEFENSE */',$taskMethods),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageDefenseAdapterTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'DPS defense tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'DPS defense tests failed.'}
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageRoutes.cs'))
$taskMethods=(@('private bool RangerBurst(', 'private bool RangerConvergence(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n"
$taskFixture=Join-Path $taskOutput 'DamageBurstAdapterFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageBurstAdapterTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_BURST_METHOD */',$taskMethods),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageBurstAdapterTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'Ranger burst tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'Ranger burst tests failed.'}
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageTick.cs'))
$taskMethods=Read-DamageBossMethod 'private void TickGravity('
$taskFixture=Join-Path $taskOutput 'DamageGravityAdapterFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageGravityAdapterTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_GRAVITY_TICK */',$taskMethods),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageGravityAdapterTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'Gravity adapter tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'Gravity adapter tests failed.'}
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageRoutes.cs'))
$taskMethods=(@('private void QueueMemoryReplay(', 'private Transform ReplayAttack(', 'private void TickMemoryReplay(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n"
$taskFixture=Join-Path $taskOutput 'DamageReplayAdapterFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageReplayAdapterTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_REPLAY_METHODS */',$taskMethods),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageReplayAdapterTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'Memory replay tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'Memory replay tests failed.'}
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageInput.cs'))
$taskMethods=Read-DamageBossMethod 'private float? PrismObstruction('
$taskPrismInput=(@('public bool CanUsePrism(', 'private bool UsePrism(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n"
$taskFixture=Join-Path $taskOutput 'DamagePrismOcclusionFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamagePrismOcclusionTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_PRISM_OBSTRUCTION */',$taskMethods).Replace('/* ACTUAL_PRISM_INPUT */',$taskPrismInput),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamagePrismOcclusionTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'Prism occlusion tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'Prism occlusion tests failed.'}
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageKit.cs'))
$taskMethods=(@('private Transform ComboAttack(', 'private bool ComboAreaContains(', 'private void PrismFinisher(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n"
$taskFixture=Join-Path $taskOutput 'DamageComboAreaFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageComboAreaTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_COMBO_AREA */',$taskMethods),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageComboAreaTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'Combo area tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'Combo area tests failed.'}
$taskMethods=(@('private bool UseUltimate(', 'private void UltimateStrike(', 'private void TickUltimate(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n"
$taskFixture=Join-Path $taskOutput 'DamageUltimateFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageUltimateTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_ULTIMATE */',$taskMethods),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageUltimateTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'Ultimate tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'Ultimate tests failed.'}
$taskCatalogSources=$taskSources+(@('NineKeyTankData.cs','NineKeyHealerData.cs','CharacterDefinitions.cs')|ForEach-Object{Join-Path $taskRoot ('Assets/Scripts/'+$_)})
$taskCatalogExe=Join-Path $taskOutput 'DamageCatalogActivationTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskCatalogExe" $taskCatalogSources (Join-Path $PSScriptRoot 'DamageCatalogActivationTests.cs')
if($LASTEXITCODE -ne 0){throw 'Damage catalog activation tests failed to compile.'}
& $taskCatalogExe
if($LASTEXITCODE -ne 0){throw 'Damage catalog activation tests failed.'}
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/RaidConnectionUI.cs'))
$taskMethods=(@('private static string LobbyFourth(', 'private static string LobbyCounterSummary(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n"
$taskFixture=Join-Path $taskOutput 'DamageLobbyRoleFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageLobbyRoleTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_LOBBY_ROLE */',$taskMethods),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageLobbyRoleTests.exe'
$taskLobbySources=$taskSources+(@('NineKeyTankData.cs','NineKeyHealerData.cs')|ForEach-Object{Join-Path $taskRoot ('Assets/Scripts/'+$_)})
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" $taskLobbySources $taskFixture
if($LASTEXITCODE -ne 0){throw 'DPS lobby role tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'DPS lobby role tests failed.'}
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageSnapshot.cs'))
$taskMethods=Read-DamageBossMethod 'private static float MeteorCompletionDelay('
$taskFixture=Join-Path $taskOutput 'DamagePartyPreparationFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamagePartyPreparationTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_METEOR_COMPLETION_DELAY */',$taskMethods),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamagePartyPreparationTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'Party preparation tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'Party preparation tests failed.'}
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageRoutes.cs'))
$taskMethods=(@('private bool Identity(', 'private bool RangerBurst(', 'private bool RangerConvergence(', 'private void QueueMemoryReplay(', 'private Transform ReplayAttack(', 'private void TickMemoryReplay(', 'private void ResolveSeal(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n"
foreach($taskHelper in @('Spend','Enemy')) {
    $taskExpression=[regex]::Match($taskBossSource,'private bool '+$taskHelper+'\([^\r\n]+?=>[^\r\n]+?;')
    if(!$taskExpression.Success){throw "Missing identity expression helper: $taskHelper"}
    $taskMethods+="`n"+$taskExpression.Value
}
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageKit.cs'))
$taskMethods+="`n"+(Read-DamageBossMethod 'private void Effect(')
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageContext.cs'))
$taskMethods+="`n"+((@('public float EffectiveRange(', 'private float InstallationDuration(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n")
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageInput.cs'))
$taskMethods+="`n"+((@('public bool CanUsePrism(', 'private bool UsePrism(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n")
$taskFixture=Join-Path $taskOutput 'DamageIdentityAdapterFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageIdentityAdapterTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_IDENTITY_METHODS */',$taskMethods),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageIdentityAdapterTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'Identity adapter tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'Identity adapter tests failed.'}

# Both DPS hotbars share this availability method and the real target resolver.
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/CharacterFrameworkUI.cs'))
$taskHudMethod=Read-DamageBossMethod 'public static string DamageSlotUnavailable('
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/TargetingManager.cs'))
$taskTargetMethods=(@('public Transform GetLivingOffensiveTarget(', 'public bool TryResolveSkillTarget(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n"
$taskFixture=Join-Path $taskOutput 'DamageHudFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageHudTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_DAMAGE_HUD_METHOD */',$taskHudMethod).Replace('/* ACTUAL_TARGET_METHODS */',$taskTargetMethods),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageHudTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" $taskFixture
if($LASTEXITCODE -ne 0){throw 'DPS HUD tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'DPS HUD tests failed.'}

$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageKit.cs'))
$taskMethods=Read-DamageBossMethod 'private bool Attack('
$taskFixture=Join-Path $taskOutput 'DamageAttackAdapterFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageAttackAdapterTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_DAMAGE_ATTACK */',$taskMethods),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageAttackAdapterTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'DPS damage-kind auxiliary tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'DPS damage-kind auxiliary tests failed.'}

$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/PartyAiTactics.cs'))
$taskCounterPosition=Read-DamageBossMethod 'public static bool CanTakeCounterPosition('
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/BossGuardianNormal.cs'))
$taskCounterSelection=(@('public int GuardianCounterRank(', 'private static int CounterPriority(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n"
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/BossJanusNormal.cs'))
$taskJanusCounter=Read-DamageBossMethod 'public Transform JanusAiCounterTarget('
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/BossJanusMechanics.cs'))
$taskJanusCounter+="`n"+(Read-DamageBossMethod 'private bool TryJanusCounterDestination(')
$taskFixture=Join-Path $taskOutput 'DamageCounterPositionFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageCounterPositionTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_COUNTER_POSITION_POLICY */',$taskCounterPosition).Replace('/* ACTUAL_GUARDIAN_COUNTER_SELECTION */',$taskCounterSelection).Replace('/* ACTUAL_JANUS_COUNTER_PATH */',$taskJanusCounter),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageCounterPositionTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'DPS counter positioning tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'DPS counter positioning tests failed.'}

$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageInput.cs'))
$taskRequest=Read-DamageBossMethod 'public void RequestIdentity('
$taskInputLifecycle=(@('private void ResetInput(', 'public void UpdateInput(')|ForEach-Object{Read-DamageBossMethod $_}) -join "`n"
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamagePresentation.cs'))
$taskInputLifecycle+="`n"+(Read-DamageBossMethod 'private void OnDisable(')
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/AegisRouteData.cs'))
$taskGestureStep=Read-DamageBossMethod 'public int Step('
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/TargetingManager.cs'))
$taskResolver=Read-DamageBossMethod 'public Transform GetLivingOffensiveTarget('
$taskFixture=Join-Path $taskOutput 'DamageInputTargetFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageInputTargetTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_REQUEST_IDENTITY */',$taskRequest).Replace('/* ACTUAL_TARGET_RESOLVER */',$taskResolver).Replace('/* ACTUAL_INPUT_LIFECYCLE */',$taskInputLifecycle).Replace('/* ACTUAL_GESTURE_STEP */',$taskGestureStep),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageInputTargetTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" $taskFixture
if($LASTEXITCODE -ne 0){throw 'DPS identity input target tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'DPS identity input target tests failed.'}

# Run the actual actor boundary ordering with real runtime state and DPS field initialization.
$taskBossSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/CharacterActor.cs'))
$taskMethods=Read-DamageBossMethod 'public void ResetEncounter('
$taskResetSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyDamageKit.cs'))
$taskResetFields=([regex]::Match($taskResetSource,'_hands=new DamageHandsState\(\);_hands.Configure\([^\r\n]+').Value)+"`n"+([regex]::Match($taskResetSource,'_season=new DamageSeasonState\(\);_season.Reset\([^\r\n]+').Value)
if(!$taskResetFields.Contains('_season.Reset') -or !$taskResetFields.Contains('_hands.Configure')){throw 'Missing DPS reset initialization'}
$taskFixture=Join-Path $taskOutput 'DamageEncounterResetFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'DamageEncounterResetTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_RESET_ENCOUNTER */',$taskMethods).Replace('/* ACTUAL_RESET_FIELDS */',$taskResetFields),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'DamageEncounterResetTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" $taskSources $taskFixture
if($LASTEXITCODE -ne 0){throw 'DPS encounter reset tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'DPS encounter reset tests failed.'}
