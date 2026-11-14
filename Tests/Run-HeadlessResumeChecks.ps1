param(
    [string]$OriginalProject = (Split-Path $PSScriptRoot -Parent),
    [ValidateSet('LAN', 'AI', 'SaveResume')][string]$Mode = 'LAN',
    [ValidateSet('normal', 'hard', 'both')][string]$Difficulty = 'both',
    [ValidateSet(1, 6)][int]$ThroughStage = 1,
    [ValidateSet(2, 4)][int]$Players = 4,
    [ValidateRange(30, 1800)][int]$TimeoutSeconds = 900,
    [string]$ReportDirectory
)
$ErrorActionPreference = 'Stop'
$headlessOriginal = (Resolve-Path -LiteralPath $OriginalProject).Path
if (!(Test-Path -LiteralPath (Join-Path $headlessOriginal '.git') -PathType Container)) { throw 'Only the original project build may be used.' }
if (![string]::IsNullOrWhiteSpace($env:RAID_STEAM_APP_ID) -and $env:RAID_STEAM_APP_ID -ne '0') { throw 'Clear the Steam override for isolated LAN/AI checks.' }
$headlessBuild = Join-Path $headlessOriginal 'Builds/AstellaWindows'
$headlessExe = Join-Path $headlessBuild 'Astella.exe'
$headlessReceipt = Get-Content -LiteralPath (Join-Path $headlessBuild 'BUILD_RECEIPT.json') -Encoding UTF8 -Raw | ConvertFrom-Json
if (!$headlessReceipt.inputsPreserved) { throw 'The build has unreviewed input changes.' }
foreach ($headlessArtifact in $headlessReceipt.artifacts) {
    $headlessArtifactPath = Join-Path $headlessBuild $headlessArtifact.path
    if (!(Test-Path -LiteralPath $headlessArtifactPath) -or (Get-FileHash -LiteralPath $headlessArtifactPath).Hash -ne $headlessArtifact.sha256) { throw "Build artifact changed: $headlessArtifactPath" }
}
. (Join-Path $PSScriptRoot 'Part1BuildPreflight.ps1')
$headlessFingerprint = Get-Part1ProjectFingerprint -Project (Split-Path $PSScriptRoot -Parent)
if ($headlessFingerprint.sha256 -ne $headlessReceipt.inputSha256) { throw 'Rebuild the exact current source/assets before running integration checks.' }
$headlessScenario = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot 'Scenarios/Part1-FourPlayer.json')).Path
$headlessProjectSettings = [IO.File]::ReadAllText((Join-Path $headlessOriginal 'ProjectSettings/ProjectSettings.asset'))
function Read-HeadlessProductField([string]$Name) {
    $value = [regex]::Match($headlessProjectSettings, ('(?m)^  ' + $Name + ': (.+)$')).Groups[1].Value.Trim()
    if (!$value) { throw "Missing player setting: $Name" }
    if ($value.StartsWith('"')) { return ($value | ConvertFrom-Json) }
    return $value
}
$headlessNormalRoot = Join-Path (Join-Path (Join-Path (Split-Path ([Environment]::GetFolderPath('LocalApplicationData')) -Parent) 'LocalLow') (Read-HeadlessProductField 'companyName')) (Read-HeadlessProductField 'productName')
function Get-HeadlessNormalDataFingerprint([switch]$EngineOnly) {
    $lines = [System.Collections.Generic.List[string]]::new()
    $details = [System.Collections.Generic.List[object]]::new()
    if (Test-Path -LiteralPath $headlessNormalRoot) {
        foreach ($entry in (Get-ChildItem -LiteralPath $headlessNormalRoot -Force | Where-Object { $_.Name -ne 'test-profiles' })) {
            # Unity/ contains engine-owned analytics/shader diagnostics. Record its
            # changes separately; never confuse them with player save/settings data.
            if (($entry.Name -eq 'Unity') -ne [bool]$EngineOnly) { continue }
            $files = $(if ($entry.PSIsContainer) { @(Get-ChildItem -LiteralPath $entry.FullName -File -Recurse -Force) } else { @($entry) })
            foreach ($file in $files) {
                $relative = $file.FullName.Substring($headlessNormalRoot.Length + 1)
                $digest = (Get-FileHash -LiteralPath $file.FullName).Hash
                $lines.Add($relative + "`t" + $digest + "`n")
                $details.Add([pscustomobject]@{ path = $relative; sha256 = $digest })
            }
        }
    }
    $sorted = [string[]]$lines.ToArray(); [Array]::Sort($sorted, [StringComparer]::Ordinal)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { [pscustomobject]@{ count = $lines.Count; sha256 = [BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes(($sorted -join '')))).Replace('-', '').ToLowerInvariant(); files = @($details.ToArray()) } }
    finally { $hasher.Dispose() }
}
if (!$ReportDirectory) { $ReportDirectory = Join-Path ([IO.Path]::GetTempPath()) ('Astella-Headless-' + [Guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath (Join-Path $ReportDirectory 'results.json')) { throw 'Use a new report directory to preserve existing evidence.' }
[IO.Directory]::CreateDirectory($ReportDirectory) | Out-Null
$ReportDirectory = (Resolve-Path -LiteralPath $ReportDirectory).Path
if ($Mode -eq 'SaveResume' -and $ThroughStage -ne 1) { throw 'SaveResume checks the first regular checkpoint; use AI for complete combat runs.' }
$headlessCases = $(if ($Mode -eq 'LAN') { @('lan') } elseif ($Mode -eq 'SaveResume') {
    foreach ($headlessDiff in $(if ($Difficulty -eq 'both') { @('normal', 'hard') } else { @($Difficulty) })) { 'single-save-' + $headlessDiff; 'single-resume-' + $headlessDiff }
} elseif ($Difficulty -eq 'both') { @('normal', 'hard') } else { @($Difficulty) })
$headlessSharedProfiles = @{ normal = ('resume-' + [Guid]::NewGuid().ToString('N').Substring(0, 16)); hard = ('resume-' + [Guid]::NewGuid().ToString('N').Substring(0, 16)) }
$headlessResults = [System.Collections.Generic.List[object]]::new()
foreach ($headlessCase in $headlessCases) {
    $headlessCaseDirectory = Join-Path $ReportDirectory ('case-' + [Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($headlessCaseDirectory) | Out-Null
    $headlessProcesses = [System.Collections.Generic.List[object]]::new()
    $headlessCaseResults = @()
    $headlessFailure = ''
    $headlessNormalBefore = Get-HeadlessNormalDataFingerprint
    $headlessEngineBefore = Get-HeadlessNormalDataFingerprint -EngineOnly
    foreach ($headlessUserFile in $headlessNormalBefore.files) {
        $headlessBackupPath = Join-Path (Join-Path $headlessCaseDirectory 'ordinary-data-before') $headlessUserFile.path
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($headlessBackupPath)) | Out-Null
        Copy-Item -LiteralPath (Join-Path $headlessNormalRoot $headlessUserFile.path) -Destination $headlessBackupPath
    }
    $headlessPort = Get-Random -Minimum 20000 -Maximum 50000
    $headlessActualDifficulty = $(if ($Mode -eq 'AI') { $headlessCase } elseif ($Mode -eq 'SaveResume') { ($headlessCase -split '-')[-1] } else { 'normal' })
    $headlessProfile = $(if ($Mode -eq 'SaveResume') { $headlessSharedProfiles[$headlessActualDifficulty] } else { 'resume-' + [Guid]::NewGuid().ToString('N').Substring(0, 16) })
    Write-Output "RUN: $Mode $headlessCase players=$Players through=$ThroughStage evidence=$headlessCaseDirectory"
    try {
        $headlessCount = $(if ($Mode -eq 'LAN') { $Players } else { 1 })
        for ($headlessRole = 1; $headlessRole -le $headlessCount; $headlessRole++) {
            $headlessKind = $(if ($Mode -eq 'AI') { 'ai' } elseif ($Mode -eq 'SaveResume') { $headlessCase.Substring(0, $headlessCase.LastIndexOf('-')) } elseif ($headlessRole -eq 1) { 'lan-host' } else { 'lan-client' })
            $headlessLog = Join-Path $headlessCaseDirectory ("role-$headlessRole.log")
            foreach ($headlessPath in @($headlessLog, $headlessCaseDirectory, $headlessScenario)) { if ($headlessPath.Contains('"')) { throw 'Quotes are not supported in check paths.' } }
            $headlessArguments = '-batchmode -nographics -logFile "' + $headlessLog + '" --raid-test-profile=' + $headlessProfile + '-r' + $headlessRole +
                ' --astella-check=' + $headlessKind + ' --astella-role=' + $headlessRole + ' --astella-players=' + $Players +
                ' --astella-through=' + $ThroughStage + ' --astella-timeout=' + $TimeoutSeconds + ' --astella-difficulty=' + $headlessActualDifficulty +
                ' --astella-address=127.0.0.1:' + $headlessPort + ' --astella-case-dir="' + $headlessCaseDirectory + '" --astella-scenario="' + $headlessScenario + '"'
            $headlessProcess = Start-Process -FilePath $headlessExe -ArgumentList $headlessArguments -WindowStyle Hidden -PassThru
            $headlessProcesses.Add([pscustomobject]@{ process = $headlessProcess; role = $headlessRole; log = $headlessLog; startedUtc = [DateTime]::UtcNow.ToString('O') })
            if ($Mode -eq 'LAN' -and $headlessRole -eq 1) {
                $headlessListenUntil = [DateTime]::UtcNow.AddSeconds(40)
                while (!(Test-Path -LiteralPath (Join-Path $headlessCaseDirectory 'role-1.started'))) {
                    if ($headlessProcess.HasExited) { throw 'Host exited before listener startup; inspect role-1.log.' }
                    if ([DateTime]::UtcNow -gt $headlessListenUntil) { throw 'Host listener startup timed out.' }
                    Start-Sleep -Milliseconds 200
                }
            }
        }
        $headlessDeadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds + 45)
        do {
            $headlessCaseResults = @()
            foreach ($headlessEntry in $headlessProcesses) {
                $headlessResultPath = Join-Path $headlessCaseDirectory ("role-$($headlessEntry.role).json")
                if (Test-Path -LiteralPath $headlessResultPath) {
                    try { $headlessCaseResults += Get-Content -LiteralPath $headlessResultPath -Encoding UTF8 -Raw | ConvertFrom-Json } catch { continue }
                } elseif ($headlessEntry.process.HasExited) { throw "Role $($headlessEntry.role) exited without a receipt; inspect $($headlessEntry.log)" }
            }
            if (@($headlessCaseResults | Where-Object { !$_.success }).Count -gt 0) { throw (($headlessCaseResults | Where-Object { !$_.success } | ForEach-Object { $_.reason }) -join '; ') }
            if ($headlessCaseResults.Count -eq $headlessCount) { break }
            if ([DateTime]::UtcNow -gt $headlessDeadline) { throw 'Headless integration receipt timeout.' }
            Start-Sleep -Milliseconds 250
        } while ($true)
    } catch { $headlessFailure = $_.Exception.Message }
    finally {
        [IO.File]::WriteAllText((Join-Path $headlessCaseDirectory 'STOP'), [DateTime]::UtcNow.ToString('O'))
        foreach ($headlessEntry in $headlessProcesses) {
            if (!$headlessEntry.process.WaitForExit(10000)) {
                # This is the exact process object created above, never a name-wide kill.
                $headlessLive = Get-Process -Id $headlessEntry.process.Id -ErrorAction SilentlyContinue
                if ($headlessLive -and [IO.Path]::GetFullPath($headlessLive.Path) -eq [IO.Path]::GetFullPath($headlessExe)) { $headlessEntry.process.Kill(); $headlessEntry.process.WaitForExit() }
                if (!$headlessFailure) { $headlessFailure = 'A check process failed to stop normally.' }
            }
            if ($headlessEntry.process.ExitCode -ne 0 -and !$headlessFailure) { $headlessFailure = "Role $($headlessEntry.role) exited $($headlessEntry.process.ExitCode)." }
            if (Test-Path -LiteralPath $headlessEntry.log) {
                $headlessEngineErrors = Select-String -LiteralPath $headlessEntry.log -Pattern '^NullReferenceException|^MissingReferenceException|^ArgumentException|^InvalidOperationException|^Assertion failed|^Crash!!!'
                if ($headlessEngineErrors -and !$headlessFailure) { $headlessFailure = 'Engine errors found in the complete process log.' }
            }
        }
    }
    $headlessNormalAfter = Get-HeadlessNormalDataFingerprint
    if ($Mode -eq 'SaveResume' -and $headlessCase.StartsWith('single-resume-') -and $headlessCaseResults.Count -eq 1 -and $headlessResults.Count -gt 0) {
        $headlessPreviousRun = $headlessResults[$headlessResults.Count - 1].members[0]
        $headlessResumedRun = $headlessCaseResults[0]
        if ($headlessPreviousRun.runId -ne $headlessResumedRun.runId -or $headlessPreviousRun.profileRoot -ne $headlessResumedRun.profileRoot -or $headlessPreviousRun.processSessionId -eq $headlessResumedRun.processSessionId -or $headlessPreviousRun.attemptId -eq $headlessResumedRun.attemptId) { $headlessFailure = 'Resume did not reuse the saved run in a fresh process and fresh attempt.' }
    }
    $headlessEngineAfter = Get-HeadlessNormalDataFingerprint -EngineOnly
    $headlessDataPreserved = $headlessNormalBefore.sha256 -eq $headlessNormalAfter.sha256
    if (!$headlessDataPreserved -and !$headlessFailure) { $headlessFailure = 'Ordinary user data changed during the check; inspect fingerprints before continuing.' }
    foreach ($headlessMember in $headlessCaseResults) {
        if ([string]::IsNullOrWhiteSpace($headlessMember.profileRoot)) { $headlessFailure = 'Runtime failed before isolated-profile validation; inspect its reason and log.'; continue }
        try {
            if (![IO.Path]::GetFullPath($headlessMember.profileRoot).StartsWith([IO.Path]::GetFullPath((Join-Path $headlessNormalRoot 'test-profiles')) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                $headlessFailure = 'Runtime test profile does not match the protected persistent-data root.'
            }
        } catch { $headlessFailure = 'Invalid runtime test profile path; inspect the preserved UTF-8 receipt.' }
    }
    $headlessProcessEvidence = @($headlessProcesses | ForEach-Object { [pscustomobject]@{ role = $_.role; processId = $_.process.Id; exitCode = $_.process.ExitCode; startedUtc = $_.startedUtc; log = $_.log } })
    $headlessResults.Add([pscustomobject]@{ case = $headlessCase; directory = $headlessCaseDirectory; success = !$headlessFailure; error = $headlessFailure; members = $headlessCaseResults; processes = $headlessProcessEvidence; ordinaryDataBefore = $headlessNormalBefore; ordinaryDataAfter = $headlessNormalAfter; ordinaryDataFilesPreserved = $headlessDataPreserved; engineDiagnosticsBefore = $headlessEngineBefore; engineDiagnosticsAfter = $headlessEngineAfter; engineDiagnosticsChanged = ($headlessEngineBefore.sha256 -ne $headlessEngineAfter.sha256) })
    [pscustomobject]@{
        version = 1; sourceCommit = $headlessReceipt.sourceCommit; inputSha256 = $headlessReceipt.inputSha256
        scenarioPath = $headlessScenario; scenarioSha256 = (Get-FileHash -LiteralPath $headlessScenario).Hash; runnerSha256 = (Get-FileHash -LiteralPath $PSCommandPath).Hash
        mode = $Mode; targetStage = $ThroughStage; players = $Players; completedUtc = [DateTime]::UtcNow.ToString('O')
        cases = @($headlessResults.ToArray()); humanInputVerified = $false; steamVerified = $false; gpuPerformanceVerified = $false
    } | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $ReportDirectory 'results.json') -Encoding UTF8
    if ($headlessFailure) { throw "Headless $headlessCase failed: $headlessFailure. Evidence: $headlessCaseDirectory" }
    Write-Output "PASS: $Mode $headlessCase. Evidence: $headlessCaseDirectory"
}
Write-Output "PASS: isolated headless checks. Evidence: $ReportDirectory. Human input, Steam, remote-PC latency and GPU performance remain unverified."
