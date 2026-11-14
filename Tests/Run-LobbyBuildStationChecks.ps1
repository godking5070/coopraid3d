param([string]$Compiler)
$ErrorActionPreference='Stop'
if (!$Compiler) {
    $stationVsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler=(& $stationVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$Compiler) { throw 'A C# 9+ compiler is required.' }
$stationRoot=Split-Path $PSScriptRoot -Parent
function Read-StationMember([string]$File,[string]$Signature,[switch]$Expression) {
    $source=[IO.File]::ReadAllText((Join-Path $stationRoot $File))
    $start=$source.IndexOf($Signature,[StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing station member: $Signature" }
    if ($Expression) { $end=$source.IndexOf(';',$start)+1; return $source.Substring($start,$end-$start) }
    $open=$source.IndexOf('{',$start);$end=$open+1;$depth=1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') {$depth++} elseif ($source[$end] -eq '}') {$depth--}
        $end++
    }
    if ($depth -ne 0) { throw "Incomplete station method: $Signature" }
    $source.Substring($start,$end-$start)
}
$stationTemplate=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'LobbyBuildStationTests.cs'))
$stationTemplate=$stationTemplate.Replace('/* ACTUAL_STATION_POSITION */',(Read-StationMember 'Assets/Scripts/RaidLobbyLayout.cs' 'public Vector3 BuildStationPosition =>' -Expression))
$stationTemplate=$stationTemplate.Replace('/* ACTUAL_ROOM_ACTIVE */',(Read-StationMember 'Assets/Scripts/RaidLobbyRoom.cs' 'public static bool Active =>' -Expression))
$stationTemplate=$stationTemplate.Replace('/* ACTUAL_STATION_ACCESS */',(Read-StationMember 'Assets/Scripts/RaidLobbyRoom.cs' 'public static bool CanUseBuildStation =>' -Expression))
$stationTemplate=$stationTemplate.Replace('/* ACTUAL_ROUTE_PERMISSION */',(Read-StationMember 'Assets/Scripts/AegisRouteSelectionUI.cs' 'public static bool CanEditCurrentRoute(' -Expression))
$stationTemplate=$stationTemplate.Replace('/* ACTUAL_ROUTE_OPEN */',(Read-StationMember 'Assets/Scripts/AegisRouteSelectionUI.cs' 'public static bool OpenCurrentRoutes('))
$stationTemplate=$stationTemplate.Replace('/* ACTUAL_ROUTE_BACK */',(Read-StationMember 'Assets/Scripts/AegisRouteSelectionUI.cs' 'private void Back('))
$stationTemplate=$stationTemplate.Replace('/* ACTUAL_LOADOUT_OPEN */',(Read-StationMember 'Assets/Scripts/CharacterFrameworkUI.cs' 'public bool OpenLoadoutAtStation('))
$stationOutput=Join-Path ([IO.Path]::GetTempPath()) ('Astella-LobbyBuildStation-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stationOutput)|Out-Null
$stationFixture=Join-Path $stationOutput 'LobbyBuildStationFixture.cs'
[IO.File]::WriteAllText($stationFixture,$stationTemplate,[Text.UTF8Encoding]::new($false))
$stationExe=Join-Path $stationOutput 'LobbyBuildStationTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$stationExe" $stationFixture
if ($LASTEXITCODE -ne 0) { throw 'Lobby build station tests failed to compile.' }
& $stationExe
if ($LASTEXITCODE -ne 0) { throw 'Lobby build station tests failed.' }
& (Join-Path $PSScriptRoot 'Run-PartyBuildSummaryChecks.ps1') -Compiler $Compiler
