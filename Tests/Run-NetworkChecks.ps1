param([Parameter(Mandatory = $true)][string]$OriginalProject, [string]$TransportPackage, [string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$TransportPackage) {
    $TransportPackage = Get-ChildItem (Join-Path $OriginalProject 'Library/PackageCache') -Directory -Filter 'com.unity.transport@*' | Select-Object -First 1 -ExpandProperty FullName
}
if (!$TransportPackage -or !(Test-Path (Join-Path $TransportPackage 'Runtime'))) { throw 'Pass -TransportPackage with an extracted official com.unity.transport 2.7.4 package, or import it in the original Unity project first.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-Network-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskExe = Join-Path $taskOutput 'NetworkTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" (Join-Path $taskRoot 'Assets/Scripts/RaidNetworkProtocol.cs') (Join-Path $taskRoot 'Assets/Scripts/DashMotion.cs') (Join-Path $PSScriptRoot 'RaidNetworkTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Network test compilation failed.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Network logic tests failed.' }

# Compile the real official transport sources against the original project's references.
# This checks APIs without launching Unity or importing/copying Library into this worktree.
[xml]$taskProject = Get-Content (Join-Path $OriginalProject 'Assembly-CSharp.csproj') -Raw
$taskDefines = $taskProject.SelectSingleNode("//*[local-name()='DefineConstants']").InnerText
$taskDll = Join-Path $taskOutput 'Unity.Networking.Transport.dll'
$taskLines = @('/nologo', '/nostdlib+', '/unsafe', '/langversion:9.0', '/target:library', '/utf8output', "/define:$taskDefines;HOSTNAME_RESOLUTION_AVAILABLE;DONT_FRAGMENT_BIT_AVAILABLE", "/out:`"$taskDll`"")
foreach ($taskReference in $taskProject.SelectNodes("//*[local-name()='Reference']/*[local-name()='HintPath']")) {
    $taskPath = $taskReference.InnerText
    if ([IO.Path]::GetFileName($taskPath) -eq 'Unity.Networking.Transport.dll') { continue }
    if (![IO.Path]::IsPathRooted($taskPath)) { $taskPath = Join-Path $OriginalProject $taskPath }
    $taskLines += "/reference:`"$taskPath`""
}
foreach ($taskFile in (rg --files (Join-Path $TransportPackage 'Runtime') -g '*.cs')) { $taskLines += "`"$taskFile`"" }
$taskRsp = Join-Path $taskOutput 'transport.rsp'
[IO.File]::WriteAllLines($taskRsp, $taskLines, [Text.UTF8Encoding]::new($false))
& $Compiler /noconfig "@$taskRsp"
if ($LASTEXITCODE -ne 0) { throw 'Official Transport managed compilation failed.' }
$taskPreviousReference = $env:RAIDROGUE_TRANSPORT_REFERENCE
try {
    $env:RAIDROGUE_TRANSPORT_REFERENCE = $taskDll
    & (Join-Path $PSScriptRoot 'Run-PartyBattleChecks.ps1') -OriginalProject $OriginalProject -Compiler $Compiler
} finally { $env:RAIDROGUE_TRANSPORT_REFERENCE = $taskPreviousReference }
