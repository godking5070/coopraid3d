$ErrorActionPreference = 'Stop'
$taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$taskCompiler = & $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1
if (!$taskCompiler) { throw 'C# compiler not found.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskExe = Join-Path ([IO.Path]::GetTempPath()) ('BossRaidwideChecks-' + [Guid]::NewGuid().ToString('N') + '.exe')
$taskSources = @('RaidRunFlow', 'PartyBattleState', 'RaidRecoveryState', 'RaidCombatProtocol', 'CharacterFrameworkData', 'CharacterRuntimeState', 'RaidNetworkProtocol', 'DashMotion') | ForEach-Object { Join-Path $taskRoot ('Assets/Scripts/' + $_ + '.cs') }
& $taskCompiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" $taskSources (Join-Path $PSScriptRoot 'BossRaidwideTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Raidwide tests failed to compile.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Raidwide tests failed.' }
