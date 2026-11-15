param([string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$Compiler) { throw 'A C# 9+ compiler is required.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-NineKeyTank-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskExe = Join-Path $taskOutput 'NineKeyTankTests.exe'
$taskSources = @('NineKeyTankState.cs','NineKeyTankData.cs','CharacterRosterData.cs','CharacterRosterRules.cs','IrisRescueState.cs','CharacterFrameworkData.cs','CharacterRuntimeState.cs','AegisPaladinData.cs','AegisRouteData.cs','AegisExtensionData.cs','CombatMetricsData.cs','DashMotion.cs') | ForEach-Object { Join-Path $taskRoot ('Assets/Scripts/' + $_) }
& $Compiler /nologo /langversion:9.0 /target:exe /reference:System.Web.Extensions.dll "/out:$taskExe" $taskSources (Join-Path $PSScriptRoot 'NineKeyTankTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Nine-key tank checks failed to compile.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Nine-key tank checks failed.' }

# Execute the real effect methods with a small deterministic combat-world seam.
$taskAdapter = [IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/NineKeyTankKit.cs'))
function Read-TankMethod([string]$Signature) {
    $taskStart = $taskAdapter.IndexOf($Signature,[StringComparison]::Ordinal)
    if ($taskStart -lt 0) { throw "Missing tank method: $Signature" }
    $taskOpen = $taskAdapter.IndexOf('{',$taskStart); $taskEnd=$taskOpen+1; $taskDepth=1
    while($taskDepth -gt 0 -and $taskEnd -lt $taskAdapter.Length) {
        if($taskAdapter[$taskEnd] -eq '{'){$taskDepth++} elseif($taskAdapter[$taskEnd] -eq '}'){$taskDepth--}; $taskEnd++
    }
    if($taskDepth -ne 0){throw 'Incomplete tank method'}
    $taskAdapter.Substring($taskStart,$taskEnd-$taskStart)
}
$taskMethods = (@('private bool Combo(', 'private bool AuxiliarySkill(', 'private bool Identity(', 'public void Interrupt(', 'public void BeforeInput(', 'private void ResolveBarrage(', 'public bool OwnsDeferredDirectHit(', 'private bool ValidEffectSnapshot(', 'public string Capture(', 'public bool Restore(') | ForEach-Object { Read-TankMethod $_ }) -join "`n"
$taskFixture = Join-Path $taskOutput 'TankAdapterFixture.cs'
$taskMethods += "`n" + ((@('private bool Execute(', 'public bool CanThreat(', 'private bool Threat(', 'public bool BeginSecondCharge(', 'public bool Release(', 'public string Unavailable(') | ForEach-Object { Read-TankMethod $_ }) -join "`n")
$taskSnapshotClass=Read-TankMethod 'public sealed class NineKeyTankCombatSnapshot'
$taskAdapter=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/RaidCombatWorld.cs'))
$taskThreatMethods=(@('public void AddCombatThreat(', 'public bool TransferCombatThreat(') | ForEach-Object { Read-TankMethod $_ }) -join "`n"
$taskAdapter=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/BossController.cs'))
$taskThreatMethods += "`n" + (Read-TankMethod 'public void SetAggroTarget(')
$taskTemplate = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'NineKeyTankAdapterTests.cs'))
[IO.File]::WriteAllText($taskFixture,$taskTemplate.Replace('/* ACTUAL_TANK_METHODS */',$taskMethods).Replace('/* ACTUAL_TANK_SNAPSHOT */',$taskSnapshotClass).Replace('/* ACTUAL_THREAT_METHODS */',$taskThreatMethods),[Text.UTF8Encoding]::new($false))
$taskAdapterExe=Join-Path $taskOutput 'TankAdapterTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe /reference:System.Web.Extensions.dll "/out:$taskAdapterExe" $taskSources (Join-Path $taskRoot 'Assets/Scripts/CharacterEffectState.cs') $taskFixture
if($LASTEXITCODE -ne 0){throw 'Tank effect adapter tests failed to compile.'}
& $taskAdapterExe
if($LASTEXITCODE -ne 0){throw 'Tank effect adapter tests failed.'}
