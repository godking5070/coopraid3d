param([string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$Compiler -or !(Test-Path -LiteralPath $Compiler)) { throw 'C# compiler not found.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskSource = [IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/TargetingManager.cs'))
function Read-TargetMethod([string]$Signature) {
    $taskStart = $taskSource.IndexOf($Signature, [StringComparison]::Ordinal)
    if ($taskStart -lt 0) { throw "Missing method: $Signature" }
    $taskOpen = $taskSource.IndexOf('{', $taskStart)
    $taskEnd = $taskOpen + 1; $taskDepth = 1
    while ($taskDepth -gt 0 -and $taskEnd -lt $taskSource.Length) {
        if ($taskSource[$taskEnd] -eq '{') { $taskDepth++ }
        elseif ($taskSource[$taskEnd] -eq '}') { $taskDepth-- }
        $taskEnd++
    }
    if ($taskDepth -ne 0) { throw 'Incomplete target method.' }
    $taskSource.Substring($taskStart, $taskEnd - $taskStart)
}
$taskStart = $taskSource.IndexOf('private Transform _attackTarget', [StringComparison]::Ordinal)
$taskEnd = $taskSource.IndexOf('private LineRenderer _outerSelectionRing;', $taskStart, [StringComparison]::Ordinal)
if ($taskStart -lt 0 -or $taskEnd -le $taskStart) { throw 'Target state boundaries missing.' }
$taskMethods = (@('public Transform GetLivingOffensiveTarget(', 'public bool TryResolveSkillTarget(', 'public void ForgetRemovedTarget(', 'public void SetTarget(', 'public void ClearTarget(') | ForEach-Object { Read-TargetMethod $_ }) -join "`n"
$taskTemplate = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'TargetSelectionTests.cs'))
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-Targets-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskFixture = Join-Path $taskOutput 'TargetFixture.cs'
[IO.File]::WriteAllText($taskFixture, $taskTemplate.Replace('/* ACTUAL_TARGET_STATE */', $taskSource.Substring($taskStart, $taskEnd - $taskStart)).Replace('/* ACTUAL_TARGET_METHODS */', $taskMethods), [Text.UTF8Encoding]::new($false))
$taskExe = Join-Path $taskOutput 'TargetTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" $taskFixture
if ($LASTEXITCODE -ne 0) { throw 'Target fixture compilation failed.' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Target selection checks failed.' }
