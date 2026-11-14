$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Part1BuildPreflight.ps1')
$taskFixture = Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-BuildPreflight-' + [Guid]::NewGuid().ToString('N'))
$taskCandidate = Join-Path $taskFixture 'candidate'
$taskOriginal = Join-Path $taskFixture 'original'
$script:taskChecks = 0
function Assert-BuildCheck([bool]$Condition, [string]$Message) {
    $script:taskChecks++
    if (!$Condition) { throw $Message }
}
function Write-Fixture([string]$Root, [string]$Relative, [string]$Value, [bool]$Bom = $false) {
    $path = Join-Path $Root $Relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    [IO.File]::WriteAllText($path, $Value, [Text.UTF8Encoding]::new($Bom))
}
try {
    foreach ($relative in @('Assets/Scripts/Actor.cs', 'Assets/Resources/RaidSteamSettings.json',
        'Packages/manifest.json', 'Packages/packages-lock.json', 'ProjectSettings/ProjectVersion.txt')) {
        Write-Fixture $taskCandidate $relative "first`nsecond`n"
        Write-Fixture $taskOriginal $relative "first`r`nsecond`r`n" $true
    }
    $before = (Get-FileHash -LiteralPath (Join-Path $taskOriginal 'Assets/Scripts/Actor.cs')).Hash
    $result = Get-Part1SourceDifferences $taskCandidate $taskOriginal
    Assert-BuildCheck ($result.Count -eq 0) 'CRLF/LF and UTF8 BOM must not block equivalent source.'
    Assert-BuildCheck ((Get-FileHash -LiteralPath (Join-Path $taskOriginal 'Assets/Scripts/Actor.cs')).Hash -eq $before) 'Comparison must not rewrite original bytes.'

    Write-Fixture $taskOriginal 'Assets/Scripts/Actor.cs' "First`r`nsecond`r`n"
    $result = Get-Part1SourceDifferences $taskCandidate $taskOriginal
    Assert-BuildCheck ($result.Changed -contains 'Assets/Scripts/Actor.cs') 'Case differences must be detected.'
    Write-Fixture $taskOriginal 'Assets/Scripts/Actor.cs' "first `nsecond`n"
    $result = Get-Part1SourceDifferences $taskCandidate $taskOriginal
    Assert-BuildCheck ($result.Count -eq 1) 'Whitespace beyond line endings must be retained.'
    Write-Fixture $taskOriginal 'Assets/Scripts/Actor.cs' "first`nsecond`n"

    Write-Fixture $taskCandidate 'Assets/Runtime/NewActor.cs' 'class NewActor {}'
    Write-Fixture $taskOriginal 'Assets/Plugins/UserExtra.cs' 'class UserExtra {}'
    $result = Get-Part1SourceDifferences $taskCandidate $taskOriginal
    Assert-BuildCheck ($result.Missing -contains 'Assets/Runtime/NewActor.cs') 'Required scripts outside Scripts must be checked.'
    Assert-BuildCheck ($result.Unexpected -contains 'Assets/Plugins/UserExtra.cs') 'Additional original compile inputs require review, never deletion.'
    Assert-BuildCheck ($result.Count -eq 2) 'Missing and additional inputs are distinct.'
    Assert-BuildCheck (Test-Path -LiteralPath (Join-Path $taskOriginal 'Assets/Plugins/UserExtra.cs')) 'User files remain intact.'

    Write-Fixture $taskCandidate 'Assets/Runtime/Game.asmdef' '{"name":"Game"}'
    Write-Fixture $taskOriginal 'Assets/Runtime/Game.asmdef' '{"name":"Other"}'
    Write-Fixture $taskOriginal 'Assets/csc.rsp' '-unsafe'
    Write-Fixture $taskOriginal 'ProjectSettings/ProjectVersion.txt' 'm_EditorVersion: wrong'
    $result = Get-Part1SourceDifferences $taskCandidate $taskOriginal
    Assert-BuildCheck ($result.Changed -contains 'Assets/Runtime/Game.asmdef') 'Assembly definitions must be compared.'
    Assert-BuildCheck ($result.Unexpected -contains 'Assets/csc.rsp') 'Additional compiler response files must be reported.'
    Assert-BuildCheck ($result.Changed -contains 'ProjectSettings/ProjectVersion.txt') 'Unity version mismatch must be reported.'
    Write-Fixture $taskOriginal 'Assets/Runtime/NewActor.cs.meta' 'guid: user-owned'
    $next = Get-Part1SourceDifferences $taskCandidate $taskOriginal
    Assert-BuildCheck ($next.Count -eq $result.Count) 'Meta ownership is not rewritten by source parity checks.'
    Assert-BuildCheck ([IO.File]::ReadAllText((Join-Path $taskOriginal 'Assets/Runtime/NewActor.cs.meta')) -eq 'guid: user-owned') 'Original meta remains intact.'
    Write-Output "PASS: $script:taskChecks build preflight checks; real temporary file IO, no Unity or build execution."
} finally {
    # Delete only this test's verified unique temporary directory, using one shell.
    $resolved = [IO.Path]::GetFullPath($taskFixture)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notmatch '^RaidRogue-BuildPreflight-[0-9a-f]{32}$') { throw 'Unsafe fixture cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
