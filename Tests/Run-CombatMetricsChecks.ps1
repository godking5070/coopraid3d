param(
    [Parameter(Mandatory = $true)][string]$OriginalProject,
    [string]$Compiler
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$Compiler -or !(Test-Path -LiteralPath $Compiler)) { throw 'Pass -Compiler with the path to a C# 9+ csc.exe.' }
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-Step10-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
Write-Output "Validation output: $taskOutput"

$taskTestExe = Join-Path $taskOutput 'CombatMetricsTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskTestExe" (Join-Path $taskRoot 'Assets/Scripts/CombatMetricsData.cs') (Join-Path $PSScriptRoot 'CombatMetricsTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Metrics tests did not compile.' }
& $taskTestExe
if ($LASTEXITCODE -ne 0) { throw 'Metrics tests failed.' }

# Read the original editor-generated reference lists. No editor process, Library copy,
# Unity import, package restore, or generated project is needed in the worktree.
$taskSources = @(& rg --files (Join-Path $taskRoot 'Assets') -g '*.cs')
foreach ($taskAssembly in @('Assembly-CSharp', 'Assembly-CSharp-Editor')) {
    [xml]$taskProject = Get-Content -LiteralPath (Join-Path $OriginalProject "$taskAssembly.csproj") -Raw
    $taskDefines = $taskProject.SelectSingleNode("//*[local-name()='DefineConstants']").InnerText
    $taskLines = @('/nologo', '/nostdlib+', '/langversion:9.0', '/target:library', '/utf8output', "/define:$taskDefines", "/out:`"$(Join-Path $taskOutput "$taskAssembly.dll")`"")
    foreach ($taskReference in $taskProject.SelectNodes("//*[local-name()='Reference']/*[local-name()='HintPath']")) {
        $taskRefPath = $taskReference.InnerText
        if (![IO.Path]::IsPathRooted($taskRefPath)) { $taskRefPath = Join-Path $OriginalProject $taskRefPath }
        if ([IO.Path]::GetFileName($taskRefPath) -eq 'Assembly-CSharp.dll') { continue }
        if ($env:RAIDROGUE_STEAM_REFERENCE -and [IO.Path]::GetFileName($taskRefPath) -eq 'com.rlabrecque.steamworks.net.dll') { continue }
        if ($env:RAIDROGUE_TRANSPORT_REFERENCE -and [IO.Path]::GetFileName($taskRefPath) -eq 'Unity.Networking.Transport.dll') { continue }
        if (!(Test-Path -LiteralPath $taskRefPath)) { throw "Missing original project reference: $taskRefPath" }
        $taskLines += "/reference:`"$taskRefPath`""
    }
    if ($env:RAIDROGUE_TRANSPORT_REFERENCE) { $taskLines += "/reference:`"$env:RAIDROGUE_TRANSPORT_REFERENCE`"" }
    if ($env:RAIDROGUE_STEAM_REFERENCE) { $taskLines += "/reference:`"$env:RAIDROGUE_STEAM_REFERENCE`"" }
    $taskEditor = $taskAssembly -eq 'Assembly-CSharp-Editor'
    if ($taskEditor) { $taskLines += "/reference:`"$(Join-Path $taskOutput 'Assembly-CSharp.dll')`"" }
    foreach ($taskSource in $taskSources) {
        if (($taskSource -match '[/\\]Editor[/\\]') -eq $taskEditor) { $taskLines += "`"$taskSource`"" }
    }
    $taskResponse = Join-Path $taskOutput "$taskAssembly.rsp"
    [IO.File]::WriteAllLines($taskResponse, $taskLines, [Text.UTF8Encoding]::new($false))
    & $Compiler /noconfig "@$taskResponse"
    if ($LASTEXITCODE -ne 0) { throw "$taskAssembly compilation failed." }
    Write-Output "PASS: $taskAssembly managed compilation."
}
