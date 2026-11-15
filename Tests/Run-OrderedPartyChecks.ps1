param([string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$Compiler -or !(Test-Path -LiteralPath $Compiler)) { throw 'C# compiler not found.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskSource = [IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/RaidNetworkSession.cs'))
$taskStart = $taskSource.IndexOf('public List<Transform> OrderedParty()', [StringComparison]::Ordinal)
if ($taskStart -lt 0) { throw 'OrderedParty method not found.' }
$taskOpen = $taskSource.IndexOf('{', $taskStart)
$taskEnd = $taskOpen + 1; $taskDepth = 1
while ($taskDepth -gt 0 -and $taskEnd -lt $taskSource.Length) {
    if ($taskSource[$taskEnd] -eq '{') { $taskDepth++ }
    elseif ($taskSource[$taskEnd] -eq '}') { $taskDepth-- }
    $taskEnd++
}
if ($taskDepth -ne 0) { throw 'Incomplete OrderedParty method.' }
$taskMethod = $taskSource.Substring($taskStart, $taskEnd - $taskStart)
$taskTemplate = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'OrderedPartyTests.cs'))
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-OrderedParty-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskFixture = Join-Path $taskOutput 'OrderedPartyFixture.cs'
[IO.File]::WriteAllText($taskFixture, $taskTemplate.Replace('/* ACTUAL_ORDERED_PARTY */', $taskMethod), [Text.UTF8Encoding]::new($false))
$taskExe = Join-Path $taskOutput 'OrderedPartyTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" $taskFixture
if ($LASTEXITCODE -ne 0) { throw 'Ordered party fixture compilation failed.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Ordered party checks failed.' }
