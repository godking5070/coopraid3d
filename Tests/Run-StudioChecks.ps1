param(
    [ValidateSet('BossNormal', 'BossHard', 'Stagger', 'Characters', 'Input', 'UI', 'Diagnostics', 'Compile', 'Network', 'Steam', 'Progression', 'All')]
    [string]$Group = 'BossHard',
    [string]$OriginalProject = (Split-Path $PSScriptRoot -Parent),
    [string]$ReportDirectory,
    [switch]$List
)

# Command checks only. No editor, game, Steam, watcher, or agent is launched.
$ErrorActionPreference = 'Stop'
$taskChecks = [System.Collections.Generic.List[object]]::new()
function Add-StudioCheck([string]$Name, [bool]$NeedsOriginal = $false, [string]$Includes = '') {
    if (!(@($taskChecks | Where-Object { $_.Name -eq $Name }).Count)) {
        $taskChecks.Add([pscustomobject]@{ Name = $Name; NeedsOriginal = $NeedsOriginal; Includes = $Includes })
    }
}
if ($Group -in @('BossNormal', 'All')) {
    foreach ($taskBoss in @('Guardian', 'Flora', 'Campanella', 'Marionette', 'Justitia', 'Janus')) {
        Add-StudioCheck "Run-${taskBoss}NormalChecks.ps1"
    }
    Add-StudioCheck 'Run-GuardianTempoChecks.ps1'
    Add-StudioCheck 'Run-BossCastPresentationChecks.ps1'
}
if ($Group -in @('BossHard', 'All')) {
    foreach ($taskBoss in @('Guardian', 'Flora', 'Campanella', 'Marionette', 'Justitia', 'Janus')) {
        Add-StudioCheck "Run-${taskBoss}HardChecks.ps1"
    }
    Add-StudioCheck 'Run-BossRaidwideChecks.ps1'
}
if ($Group -in @('Stagger', 'All')) {
    foreach ($taskName in @('Stagger', 'StaggerBossFirst', 'StaggerBossLast')) { Add-StudioCheck "Run-${taskName}Checks.ps1" }
}
if ($Group -in @('Characters', 'All')) {
    # Steam -> Compile already includes Paladin/selection. Standalone Characters needs it too.
    if ($Group -ne 'All') { Add-StudioCheck 'Run-NineKeyPaladinChecks.ps1' }
    foreach ($taskName in @('Tank', 'Healer', 'Damage')) { Add-StudioCheck "Run-NineKey${taskName}Checks.ps1" }
}
if ($Group -in @('Input', 'All')) { Add-StudioCheck 'Run-ClickMoveChecks.ps1' }
if ($Group -in @('UI', 'All')) {
    Add-StudioCheck 'Run-HUDLayoutChecks.ps1'
    Add-StudioCheck 'Run-LobbyPracticeChecks.ps1'
    Add-StudioCheck 'Run-LobbyBuildStationChecks.ps1' $false 'Includes PartyBuildSummary; no rendering or live host session.'
}
if ($Group -in @('Diagnostics', 'All')) {
    Add-StudioCheck 'Run-DevBugReportChecks.ps1'
    Add-StudioCheck 'Run-BugReportWatchChecks.ps1'
}
if ($Group -eq 'Compile') { Add-StudioCheck 'Run-CombatMetricsChecks.ps1' $true }
if ($Group -eq 'Network') { Add-StudioCheck 'Run-NetworkChecks.ps1' $true }
if ($Group -in @('Steam', 'All')) {
    Add-StudioCheck 'Run-SteamChecks.ps1' $true 'Includes Network, SDKs, Compile, Paladin/selection, campaign IO, flow, settings, roster, training, movement, metrics and modal checks; no live connection.'
}
if ($Group -eq 'Progression') { Add-StudioCheck 'Run-CampaignSaveChecks.ps1' }
if ($Group -in @('Progression', 'All')) {
    Add-StudioCheck 'Run-ResumeScenarioChecks.ps1'
    Add-StudioCheck 'Run-Part1BuildChecks.ps1'
}

if ($List) {
    $taskChecks | ForEach-Object {
        [pscustomobject]@{ Script = (Join-Path $PSScriptRoot $_.Name); OriginalProject = $(if ($_.NeedsOriginal) { $OriginalProject } else { '' }); Includes = $_.Includes }
    }
    return
}
if (!$ReportDirectory) { $ReportDirectory = Join-Path ([IO.Path]::GetTempPath()) ('Astella-Checks-' + [Guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath (Join-Path $ReportDirectory 'results.json')) { throw 'Choose a new report directory; existing evidence must be preserved.' }
[IO.Directory]::CreateDirectory($ReportDirectory) | Out-Null
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskCommit = (& git -C $taskRoot rev-parse HEAD).Trim()
$taskResults = [System.Collections.Generic.List[object]]::new()
$taskFailures = [System.Collections.Generic.List[string]]::new()
$taskStarted = [DateTime]::UtcNow
Write-Output "Check evidence: $ReportDirectory"
foreach ($taskCheck in $taskChecks) {
    $taskScript = Join-Path $PSScriptRoot $taskCheck.Name
    $taskLog = Join-Path $ReportDirectory ($taskCheck.Name + '.log')
    $taskArgs = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $taskScript)
    if ($taskCheck.NeedsOriginal) { $taskArgs += @('-OriginalProject', $OriginalProject) }
    $taskWatch = [Diagnostics.Stopwatch]::StartNew()
    $taskExit = -1
    $taskError = ''
    Write-Output "RUN: $($taskCheck.Name)"
    try {
        if (!(Test-Path -LiteralPath $taskScript -PathType Leaf)) { throw "Missing check: $taskScript" }
        $taskPreviousPreference = $ErrorActionPreference
        try {
            # Native tools may write successful progress to stderr. Their exit code
            # determines failure; preserve both streams in the evidence log.
            $ErrorActionPreference = 'Continue'
            & powershell.exe @taskArgs *> $taskLog
            $taskExit = $LASTEXITCODE
        } finally { $ErrorActionPreference = $taskPreviousPreference }
        if ($taskExit -ne 0) { throw "Check exited with code $taskExit" }
        Write-Output "PASS: $($taskCheck.Name)"
    } catch {
        $taskError = $_.Exception.Message
        $taskFailures.Add($taskCheck.Name)
        Write-Output "FAIL: $($taskCheck.Name): $taskError"
        if (Test-Path -LiteralPath $taskLog) { Get-Content -LiteralPath $taskLog -Tail 25 }
    }
    $taskWatch.Stop()
    $taskResults.Add([pscustomobject]@{
        script = $taskCheck.Name; exitCode = $taskExit; seconds = [math]::Round($taskWatch.Elapsed.TotalSeconds, 2)
        log = $taskLog; error = $taskError
        scriptSha256 = $(if (Test-Path -LiteralPath $taskScript) { (Get-FileHash -LiteralPath $taskScript).Hash } else { '' })
    })
    [pscustomobject]@{
        version = 1; group = $Group; sourceProject = $taskRoot; sourceCommit = $taskCommit
        startedUtc = $taskStarted.ToString('O'); updatedUtc = [DateTime]::UtcNow.ToString('O')
        selected = $taskChecks.Count; finished = $taskResults.Count; failures = @($taskFailures.ToArray())
        results = @($taskResults.ToArray()); runtimePlayVerified = $false; liveSteamVerified = $false
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $ReportDirectory 'results.json') -Encoding UTF8
}
if ($taskFailures.Count -gt 0) { throw "Studio checks failed: $($taskFailures -join ', '). Evidence: $ReportDirectory" }
Write-Output "PASS: $($taskChecks.Count) selected check scripts ($Group). Evidence: $ReportDirectory. Runtime play remains unverified."
