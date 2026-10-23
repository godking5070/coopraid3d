param([Parameter(Mandatory = $true)][string]$OriginalProject, [string]$SteamPackage, [string]$TransportPackage, [string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$SteamPackage) {
    $SteamPackage = Get-ChildItem (Join-Path $OriginalProject 'Library/PackageCache') -Directory -Filter 'com.rlabrecque.steamworks.net@*' | Select-Object -First 1 -ExpandProperty FullName
}
if (!$SteamPackage -or !(Test-Path (Join-Path $SteamPackage 'Runtime/Steam.cs'))) { throw 'Pass -SteamPackage with the official Steamworks.NET 2025.164.1 package, or import it in the original Unity project.' }
$taskPackageInfo = Get-Content (Join-Path $SteamPackage 'package.json') -Raw | ConvertFrom-Json
if ($taskPackageInfo.version -ne '2025.164.1') { throw 'Expected the pinned Steamworks.NET 2025.164.1 release.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-Steam-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskExe = Join-Path $taskOutput 'SteamTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" (Join-Path $taskRoot 'Assets/Scripts/RaidSteamPolicy.cs') (Join-Path $taskRoot 'Assets/Scripts/RaidNetworkProtocol.cs') (Join-Path $taskRoot 'Assets/Scripts/DashMotion.cs') (Join-Path $PSScriptRoot 'RaidSteamTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Steam policy test compilation failed.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Steam policy tests failed.' }
$taskAdapterExe = Join-Path $taskOutput 'SteamTransportTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskAdapterExe" (Join-Path $taskRoot 'Assets/Scripts/RaidTransport.cs') (Join-Path $taskRoot 'Assets/Scripts/RaidSteamTransport.cs') (Join-Path $taskRoot 'Assets/Scripts/RaidNetworkProtocol.cs') (Join-Path $taskRoot 'Assets/Scripts/DashMotion.cs') (Join-Path $taskRoot 'Assets/Scripts/RaidRecoveryState.cs') (Join-Path $PSScriptRoot 'RaidSteamTransportTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Steam transport test compilation failed.' }
& $taskAdapterExe
if ($LASTEXITCODE -ne 0) { throw 'Steam transport tests failed.' }
$taskRecoveryExe = Join-Path $taskOutput 'RecoveryTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskRecoveryExe" (Join-Path $taskRoot 'Assets/Scripts/RaidRecoveryState.cs') (Join-Path $taskRoot 'Assets/Scripts/RaidCombatProtocol.cs') (Join-Path $taskRoot 'Assets/Scripts/CharacterFrameworkData.cs') (Join-Path $taskRoot 'Assets/Scripts/CharacterRuntimeState.cs') (Join-Path $taskRoot 'Assets/Scripts/RaidNetworkProtocol.cs') (Join-Path $taskRoot 'Assets/Scripts/DashMotion.cs') (Join-Path $PSScriptRoot 'RaidRecoveryTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Recovery test compilation failed.' }
& $taskRecoveryExe
if ($LASTEXITCODE -ne 0) { throw 'Recovery tests failed.' }
$taskFlowExe = Join-Path $taskOutput 'FlowTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskFlowExe" (Join-Path $taskRoot 'Assets/Scripts/RaidRunFlow.cs') (Join-Path $taskRoot 'Assets/Scripts/PartyBattleState.cs') (Join-Path $taskRoot 'Assets/Scripts/RaidRecoveryState.cs') (Join-Path $taskRoot 'Assets/Scripts/RaidCombatProtocol.cs') (Join-Path $taskRoot 'Assets/Scripts/CharacterFrameworkData.cs') (Join-Path $taskRoot 'Assets/Scripts/CharacterRuntimeState.cs') (Join-Path $taskRoot 'Assets/Scripts/RaidNetworkProtocol.cs') (Join-Path $taskRoot 'Assets/Scripts/DashMotion.cs') (Join-Path $PSScriptRoot 'RaidFlowTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Run flow test compilation failed.' }
& $taskFlowExe
if ($LASTEXITCODE -ne 0) { throw 'Run flow tests failed.' }
$taskSettingsExe = Join-Path $taskOutput 'SettingsTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskSettingsExe" (Join-Path $taskRoot 'Assets/Scripts/LocalSettingsData.cs') (Join-Path $PSScriptRoot 'LocalSettingsTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Local settings test compilation failed.' }
$taskKeyboardSource = rg --files (Join-Path $OriginalProject 'Library/PackageCache') -g 'Keyboard.cs' | Where-Object { $_ -match 'com.unity.inputsystem' } | Select-Object -First 1
if (!$taskKeyboardSource) { throw 'Installed Input System Keyboard source is required for control path validation.' }
& $taskSettingsExe $taskKeyboardSource
if ($LASTEXITCODE -ne 0) { throw 'Local settings tests failed.' }
$taskCharacterExe = Join-Path $taskOutput 'CharacterFrameworkTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskCharacterExe" (Join-Path $taskRoot 'Assets/Scripts/CharacterFrameworkData.cs') (Join-Path $taskRoot 'Assets/Scripts/CharacterRuntimeState.cs') (Join-Path $taskRoot 'Assets/Scripts/DashMotion.cs') (Join-Path $taskRoot 'Assets/Scripts/RaidCombatProtocol.cs') (Join-Path $taskRoot 'Assets/Scripts/RaidNetworkProtocol.cs') (Join-Path $PSScriptRoot 'CharacterFrameworkTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Character framework test compilation failed.' }
& $taskCharacterExe
if ($LASTEXITCODE -ne 0) { throw 'Character framework tests failed.' }
# Compile the actual pinned SDK, without loading native Steam or running Unity.
[xml]$taskProject = Get-Content (Join-Path $OriginalProject 'Assembly-CSharp.csproj') -Raw
$taskDefines = $taskProject.SelectSingleNode("//*[local-name()='DefineConstants']").InnerText
$taskDll = Join-Path $taskOutput 'com.rlabrecque.steamworks.net.dll'
$taskLines = @('/nologo', '/nostdlib+', '/langversion:9.0', '/target:library', '/utf8output', "/define:$taskDefines", "/out:`"$taskDll`"")
foreach ($taskReference in $taskProject.SelectNodes("//*[local-name()='Reference']/*[local-name()='HintPath']")) {
    $taskPath = $taskReference.InnerText
    if ([IO.Path]::GetFileName($taskPath) -match 'Steamworks|com.rlabrecque') { continue }
    if (![IO.Path]::IsPathRooted($taskPath)) { $taskPath = Join-Path $OriginalProject $taskPath }
    $taskLines += "/reference:`"$taskPath`""
}
foreach ($taskFile in (rg --files (Join-Path $SteamPackage 'Runtime') -g '*.cs')) { $taskLines += "`"$taskFile`"" }
$taskRsp = Join-Path $taskOutput 'steam.rsp'
[IO.File]::WriteAllLines($taskRsp, $taskLines, [Text.UTF8Encoding]::new($false))
& $Compiler /noconfig "@$taskRsp"
if ($LASTEXITCODE -ne 0) { throw 'Official Steamworks.NET managed compilation failed.' }
$taskPreviousReference = $env:RAIDROGUE_STEAM_REFERENCE
try {
    $env:RAIDROGUE_STEAM_REFERENCE = $taskDll
    & (Join-Path $PSScriptRoot 'Run-NetworkChecks.ps1') -OriginalProject $OriginalProject -TransportPackage $TransportPackage -Compiler $Compiler
} finally { $env:RAIDROGUE_STEAM_REFERENCE = $taskPreviousReference }
