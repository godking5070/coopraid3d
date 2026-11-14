param([string]$Compiler)
$ErrorActionPreference='Stop'
if(!$Compiler){$practiceVsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe';$Compiler=(& $practiceVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)}
if(!$Compiler){throw 'A C# 9+ compiler is required.'}
$practiceRoot=Split-Path $PSScriptRoot -Parent
$practiceOutput=Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-LobbyPractice-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($practiceOutput)|Out-Null
function Read-PracticeMember([string]$File,[string]$Signature,[switch]$Expression) {
    $source=[IO.File]::ReadAllText((Join-Path $practiceRoot $File))
    $start=$source.IndexOf($Signature,[StringComparison]::Ordinal)
    if($start -lt 0){throw "Missing practice member: $Signature"}
    if($Expression){$end=$source.IndexOf(';',$start)+1;return $source.Substring($start,$end-$start)}
    $open=$source.IndexOf('{',$start);$end=$open+1;$depth=1
    while($depth -gt 0 -and $end -lt $source.Length){if($source[$end] -eq '{'){$depth++}elseif($source[$end] -eq '}'){$depth--};$end++}
    if($depth -ne 0){throw "Incomplete practice member: $Signature"}
    $source.Substring($start,$end-$start)
}
$practiceTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'LobbyPracticeTests.cs'))
$practiceTemplate=$practiceTemplate.Replace('/* ROOM_ACTIVE */',(Read-PracticeMember 'Assets/Scripts/RaidLobbyRoom.cs' 'public static bool Active =>' -Expression))
$practiceTemplate=$practiceTemplate.Replace('/* ROOM_PRACTICE */',(Read-PracticeMember 'Assets/Scripts/RaidLobbyRoom.cs' 'public static bool PracticeActive =>' -Expression))
$practiceTemplate=$practiceTemplate.Replace('/* LIVING_ENEMY */',(Read-PracticeMember 'Assets/Scripts/CombatRange.cs' 'public static bool LivingEnemy('))
$practiceTemplate=$practiceTemplate.Replace('/* APPLY_DAMAGE */',(Read-PracticeMember 'Assets/Scripts/BossAddSpawner.cs' 'private float ApplyAddHpDamage('))
$practiceTemplate=$practiceTemplate.Replace('/* COMBAT_ID */',(Read-PracticeMember 'Assets/Scripts/RaidCombatSession.cs' 'public int CombatId('))
$practiceTemplate=$practiceTemplate.Replace('/* COMBAT_TARGET */',(Read-PracticeMember 'Assets/Scripts/RaidCombatSession.cs' 'public Transform CombatTarget('))
$practiceFixture=Join-Path $practiceOutput 'LobbyPracticeFixture.cs'
[IO.File]::WriteAllText($practiceFixture,$practiceTemplate,[Text.UTF8Encoding]::new($false))
$practiceExe=Join-Path $practiceOutput 'LobbyPracticeTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$practiceExe" $practiceFixture
if($LASTEXITCODE -ne 0){throw 'Lobby practice adapters failed to compile.'}
& $practiceExe
if($LASTEXITCODE -ne 0){throw 'Lobby practice adapters failed.'}
& (Join-Path $PSScriptRoot 'Run-LobbyBuildStationChecks.ps1') -Compiler $Compiler
