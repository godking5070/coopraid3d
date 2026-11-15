param([string]$Compiler)
$ErrorActionPreference='Stop'
if(!$Compiler){$taskVsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe';$Compiler=(& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)}
if(!$Compiler){throw 'A C# 9+ compiler is required.'}
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskOutput=Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-NineKeyHealer-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput)|Out-Null
$taskExe=Join-Path $taskOutput 'HealerTests.exe'
$taskSources=@('NineKeyHealerData.cs','NineKeyHealerResources.cs','HealerPreparationState.cs','HealerSequenceState.cs','HealerLinkState.cs','HealerLayerState.cs','CharacterRosterData.cs','CharacterRosterRules.cs','IrisRescueState.cs','CharacterFrameworkData.cs','CharacterRuntimeState.cs','AegisPaladinData.cs','AegisRouteData.cs','AegisExtensionData.cs','CombatMetricsData.cs','DashMotion.cs') | ForEach-Object {Join-Path $taskRoot ('Assets/Scripts/'+$_)}
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" $taskSources (Join-Path $PSScriptRoot 'NineKeyHealerTests.cs')
if($LASTEXITCODE -ne 0){throw 'Healer tests failed to compile.'}
& $taskExe
if($LASTEXITCODE -ne 0){throw 'Healer tests failed.'}

$taskAdapter=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyHealerKit.cs'))
function Read-HealerMethod([string]$Signature){
    $taskStart=$taskAdapter.IndexOf($Signature,[StringComparison]::Ordinal)
    if($taskStart -lt 0){throw "Missing healer method: $Signature"}
    $taskOpen=$taskAdapter.IndexOf('{',$taskStart);$taskEnd=$taskOpen+1;$taskDepth=1
    while($taskDepth -gt 0 -and $taskEnd -lt $taskAdapter.Length){
        if($taskAdapter[$taskEnd] -eq '{'){$taskDepth++}elseif($taskAdapter[$taskEnd] -eq '}'){$taskDepth--};$taskEnd++
    }
    if($taskDepth -ne 0){throw 'Incomplete healer method'}
    $taskAdapter.Substring($taskStart,$taskEnd-$taskStart)
}
$taskMethods=(@('private bool Execute(', 'private bool Combo(', 'private bool Utility(', 'private void BasicHeal(')|ForEach-Object{Read-HealerMethod $_}) -join "`n"
$taskFixture=Join-Path $taskOutput 'HealerAdapterFixture.cs'
$taskTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'NineKeyHealerAdapterTests.cs'))
$taskAdapter=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyHealerSnapshot.cs'))
$taskSnapshotClass=Read-HealerMethod 'public sealed class NineKeyHealerCombatSnapshot'
$taskRestore=(@('public string Capture(', 'public bool Restore(', 'private bool ValidHealerEffect(')|ForEach-Object{Read-HealerMethod $_}) -join "`n"
$taskAdapter=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyHealerInput.cs'))
$taskMovement=Read-HealerMethod 'public void MovementStarted('
$taskAdapter=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyHealerRoutes.cs'))
$taskRoutes=(@('private void ResetRoutes(', 'private bool Identity(', 'private void Harvest(', 'private void Bloom(', 'private void DirectRoute(', 'public void Interrupt(', 'private void TickRoutes(')|ForEach-Object{Read-HealerMethod $_}) -join "`n"
$taskAdapter=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyHealerSupport.cs'))
$taskRoutes+="`n"+((@('private bool PlaceFlower(', 'private void TickFlowers(', 'private void FinishRoute(', 'private bool Prescribe(')|ForEach-Object{Read-HealerMethod $_}) -join "`n")
$taskAdapter=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyHealerLayers.cs'))
$taskRoutes+="`n"+((@('private bool LayerIdentity(', 'private void TickLayers(')|ForEach-Object{Read-HealerMethod $_}) -join "`n")
$taskAdapter=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyHealerKit.cs'))
$taskRoutes+="`n"+((@('public void Clear(', 'public void BeforeInput(')|ForEach-Object{Read-HealerMethod $_}) -join "`n")
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_HEALER_METHODS */',$taskMethods).Replace('/* ACTUAL_HEALER_SNAPSHOT */',$taskSnapshotClass).Replace('/* ACTUAL_HEALER_RESTORE */',$taskRestore).Replace('/* ACTUAL_HEALER_MOVEMENT */',$taskMovement).Replace('/* ACTUAL_HEALER_ROUTES */',$taskRoutes),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'HealerAdapterTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe /reference:System.Web.Extensions.dll "/out:$taskAdapterExe" $taskSources (Join-Path $taskRoot 'Assets/Scripts/CharacterEffectState.cs') $taskFixture
if($LASTEXITCODE -ne 0){throw 'Healer adapter tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'Healer adapter tests failed.'}
