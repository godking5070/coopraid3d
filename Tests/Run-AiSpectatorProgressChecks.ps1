param([string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$Compiler -or !(Test-Path -LiteralPath $Compiler)) { throw 'C# compiler not found.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskSource = [IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/RaidAiSpectatorSession.cs'))
$taskSignature = 'internal void AiSpectatorTick()'
$taskStart = $taskSource.IndexOf($taskSignature, [StringComparison]::Ordinal)
if ($taskStart -lt 0) { throw 'AiSpectatorTick source method not found.' }
$taskOpen = $taskSource.IndexOf('{', $taskStart)
$taskDepth = 1
$taskEnd = $taskOpen + 1
while ($taskDepth -gt 0 -and $taskEnd -lt $taskSource.Length) {
    if ($taskSource[$taskEnd] -eq '{') { $taskDepth++ }
    elseif ($taskSource[$taskEnd] -eq '}') { $taskDepth-- }
    $taskEnd++
}
if ($taskDepth -ne 0) { throw 'AiSpectatorTick source method is incomplete.' }
$taskMethod = $taskSource.Substring($taskStart, $taskEnd - $taskStart)
$taskTemplate = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'AiSpectatorProgressTests.cs'))
$taskMarker = '/* ACTUAL_AI_SPECTATOR_TICK */'
if (!$taskTemplate.Contains($taskMarker)) { throw 'AiSpectatorTick test insertion marker missing.' }
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-AiSpectatorProgress-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskFixture = Join-Path $taskOutput 'AiSpectatorProgressFixture.cs'
[IO.File]::WriteAllText($taskFixture, $taskTemplate.Replace($taskMarker, $taskMethod), [Text.UTF8Encoding]::new($false))
foreach ($taskDefine in @('DEVELOPMENT_BUILD', 'UNITY_EDITOR', 'NO_DEVELOPMENT')) {
    $taskExe = Join-Path $taskOutput ($taskDefine + '.exe')
    & $Compiler /nologo /langversion:9.0 /target:exe "/define:$taskDefine" "/out:$taskExe" $taskFixture
    if ($LASTEXITCODE -ne 0) { throw "AI spectator progress fixture compilation failed ($taskDefine)." }
    & $taskExe
    if ($LASTEXITCODE -ne 0) { throw "AI spectator progress checks failed ($taskDefine)." }
}
Write-Output "Fixture source and executables: $taskOutput"
Write-Output 'Boundary: runs the original AiSpectatorTick body with narrow component stubs. No Unity, movement, save commit or authoritative action execution is exercised.'
