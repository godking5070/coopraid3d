param(
    [string]$OriginalProject = (Split-Path $PSScriptRoot -Parent),
    [ValidateSet('LAN', 'AI')][string]$Mode = 'LAN',
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
function Get-HeadlessNormalDataFingerprint {
    $lines = [System.Collections.Generic.List[string]]::new()
    if (Test-Path -LiteralPath $headlessNormalRoot) {
        foreach ($entry in (Get-ChildItem -LiteralPath $headlessNormalRoot -Force | Where-Object { $_.Name -ne 'test-profiles' })) {
            $files = $(if ($entry.PSIsContainer) { @(Get-ChildItem -LiteralPath $entry.FullName -File -Recurse -Force) } else { @($entry) })
            foreach ($file in $files) { $lines.Add($file.FullName.Substring($headlessNormalRoot.Length) + "`t" + (Get-FileHash -LiteralPath $file.FullName).Hash + "`n") }
        }
    }
    $sorted = [string[]]$lines.ToArray(); [Array]::Sort($sorted, [StringComparer]::Ordinal)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { [pscustomobject]@{ count = $lines.Count; sha256 = [BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes(($sorted -join '')))).Replace('-', '').ToLowerInvariant() } }
    finally { $hasher.Dispose() }
}
if (!$ReportDirectory) { $ReportDirectory = Join-Path ([IO.Path]::GetTempPath()) ('Astella-Headless-' + [Guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath (Join-Path $ReportDirectory 'results.json')) { throw 'Use a new report directory to preserve existing evidence.' }
[IO.Directory]::CreateDirectory($ReportDirectory) | Out-Null
$ReportDirectory = (Resolve-Path -LiteralPath $ReportDirectory).Path
$headlessCases = $(if ($Mode -eq 'LAN') { @('lan') } elseif ($Difficulty -eq 'both') { @('normal', 'hard') } else { @($Difficulty) })
$headlessResults = [System.Collections.Generic.List[object]]::new()
foreach ($headlessCase in $headlessCases) {
    $headlessCaseDirectory = Join-Path $ReportDirectory ('case-' + [Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($headlessCaseDirectory) | Out-Null
    $headlessProcesses = [System.Collections.Generic.List[object]]::new()
    $headlessCaseResults = @()
    $headlessFailure = ''
    $headlessNormalBefore = Get-HeadlessNormalDataFingerprint
    $headlessPort = Get-Random -Minimum 20000 -Maximum 50000
    $headlessProfile = 'resume-' + [Guid]::NewGuid().ToString('N').Substring(0, 16)
    Write-Output "RUN: $Mode $headlessCase players=$Players through=$ThroughStage evidence=$headlessCaseDirectory"
    try {
        $headlessCount = $(if ($Mode -eq 'LAN') { $Players } else { 1 })
        for ($headlessRole = 1; $headlessRole -le $headlessCount; $headlessRole++) {
            $headlessKind = $(if ($Mode -eq 'AI') { 'ai' } elseif ($headlessRole -eq 1) { 'lan-host' } else { 'lan-client' })
            $headlessLog = Join-Path $headlessCaseDirectory ("role-$headlessRole.log")
            foreach ($headlessPath in @($headlessLog, $headlessCaseDirectory, $headlessScenario)) { if ($headlessPath.Contains('"')) { throw 'Quotes are not supported in check paths.' } }
            $headlessArguments = '-batchmode -nographics -logFile "' + $headlessLog + '" --raid-test-profile=' + $headlessProfile + '-r' + $headlessRole +
                ' --astella-check=' + $headlessKind + ' --astella-role=' + $headlessRole + ' --astella-players=' + $Players +
                ' --astella-through=' + $ThroughStage + ' --astella-timeout=' + $TimeoutSeconds + ' --astella-difficulty=' + $(if ($Mode -eq 'AI') { $headlessCase } else { 'normal' }) +
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
    $headlessResults.Add([pscustomobject]@{ case = $headlessCase; directory = $headlessCaseDirectory; success = !$headlessFailure; error = $headlessFailure; members = $headlessCaseResults; ordinaryDataBefore = $headlessNormalBefore; ordinaryDataAfter = $headlessNormalAfter; ordinaryDataFilesPreserved = $headlessDataPreserved })
    [pscustomobject]@{
        version = 1; sourceCommit = $headlessReceipt.sourceCommit; inputSha256 = $headlessReceipt.inputSha256
        mode = $Mode; targetStage = $ThroughStage; players = $Players; completedUtc = [DateTime]::UtcNow.ToString('O')
        cases = @($headlessResults.ToArray()); humanInputVerified = $false; steamVerified = $false; gpuPerformanceVerified = $false
    } | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $ReportDirectory 'results.json') -Encoding UTF8
    if ($headlessFailure) { throw "Headless $headlessCase failed: $headlessFailure. Evidence: $headlessCaseDirectory" }
    Write-Output "PASS: $Mode $headlessCase. Evidence: $headlessCaseDirectory"
}
Write-Output "PASS: isolated headless checks. Evidence: $ReportDirectory. Human input, Steam, remote-PC latency and GPU performance remain unverified."
