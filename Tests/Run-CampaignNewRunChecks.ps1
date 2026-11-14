param([string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$Compiler -or !(Test-Path -LiteralPath $Compiler)) { throw 'C# compiler not found.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskSource = [IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/CampaignUI.cs'))
$taskStart = $taskSource.IndexOf('private void RequestNewCampaign(Action start)', [StringComparison]::Ordinal)
$taskEnd = $taskSource.IndexOf('internal void ConfirmCampaignExit(', [StringComparison]::Ordinal)
if ($taskStart -lt 0 -or $taskEnd -le $taskStart) { throw 'New-run source boundaries missing.' }
$taskMethod = $taskSource.Substring($taskStart, $taskEnd - $taskStart)
$taskTemplate = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'CampaignNewRunTests.cs'))
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-NewRun-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskFixture = Join-Path $taskOutput 'NewRunFixture.cs'
[IO.File]::WriteAllText($taskFixture, $taskTemplate.Replace('/* ACTUAL_NEW_RUN_METHOD */', $taskMethod), [Text.UTF8Encoding]::new($false))
$taskExe = Join-Path $taskOutput 'NewRunTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" $taskFixture
if ($LASTEXITCODE -ne 0) { throw 'New-run fixture compilation failed.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'New-run confirmation checks failed.' }
