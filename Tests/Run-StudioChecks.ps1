param(
    [ValidateSet('BossHard', 'Compile', 'Network', 'Steam', 'Progression', 'All')]
    [string]$Group = 'BossHard',
    [string]$OriginalProject = (Split-Path $PSScriptRoot -Parent),
    [switch]$List
)

# Only run existing command-based checks. Never launch Unity, Steam or the game.
$ErrorActionPreference = 'Stop'
$taskChecks = @()
if ($Group -in @('BossHard', 'All')) {
    foreach ($taskBoss in @('Guardian', 'Flora', 'Campanella', 'Marionette', 'Justitia', 'Janus', 'BossRaidwide')) {
        $taskName = if ($taskBoss -eq 'BossRaidwide') { 'Run-BossRaidwideChecks.ps1' } else { "Run-${taskBoss}HardChecks.ps1" }
        $taskChecks += [pscustomobject]@{ Name = $taskName; NeedsOriginal = $false }
    }
}
if ($Group -eq 'Compile') {
    $taskChecks += [pscustomobject]@{ Name = 'Run-CombatMetricsChecks.ps1'; NeedsOriginal = $true }
}
if ($Group -eq 'Network') {
    $taskChecks += [pscustomobject]@{ Name = 'Run-NetworkChecks.ps1'; NeedsOriginal = $true }
}
if ($Group -in @('Steam', 'All')) {
    # This existing chain also runs Network, Compile and CampaignSave/flow checks.
    # Run it intact so standalone Steam and All retain the same SDK/dependency validation.
    $taskChecks += [pscustomobject]@{ Name = 'Run-SteamChecks.ps1'; NeedsOriginal = $true }
}
if ($Group -eq 'Progression') {
    $taskChecks += [pscustomobject]@{ Name = 'Run-CampaignSaveChecks.ps1'; NeedsOriginal = $false }
}
if ($Group -in @('Progression', 'All')) {
    $taskChecks += [pscustomobject]@{ Name = 'Run-Part1BuildChecks.ps1'; NeedsOriginal = $false }
}

$taskFailures = @()
foreach ($taskCheck in $taskChecks) {
    $taskScript = Join-Path $PSScriptRoot $taskCheck.Name
    $taskArgs = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $taskScript)
    if ($taskCheck.NeedsOriginal) { $taskArgs += @('-OriginalProject', $OriginalProject) }
    if ($List) {
        $taskIncludes = if ($taskCheck.Name -eq 'Run-SteamChecks.ps1') {
            'Steam policy, fake transport, recovery, flow, settings, character, campaign save; Network -> Compile'
        } elseif ($taskCheck.Name -eq 'Run-NetworkChecks.ps1') {
            'Network protocol, combat network, Transport SDK; party/potion/dash/camera/range -> Compile'
        } elseif ($taskCheck.Name -eq 'Run-CampaignSaveChecks.ps1') {
            'Campaign save, Part 1 save, run flow'
        } else { '' }
        [pscustomobject]@{ Script = $taskScript; OriginalProject = $(if ($taskCheck.NeedsOriginal) { $OriginalProject } else { '' }); Includes = $taskIncludes }
        continue
    }
    Write-Output "RUN: $($taskCheck.Name)"
    try {
        if (!(Test-Path -LiteralPath $taskScript -PathType Leaf)) { throw "Missing check: $taskScript" }
        # A child shell isolates preferences and exit codes between existing suites.
        & powershell.exe @taskArgs
        if ($LASTEXITCODE -ne 0) { throw "Check exited with code $LASTEXITCODE" }
        Write-Output "PASS: $($taskCheck.Name)"
    } catch {
        $taskFailures += $taskCheck.Name
        Write-Output "FAIL: $($taskCheck.Name): $($_.Exception.Message)"
    }
}
if ($taskFailures.Count -gt 0) { throw "Studio checks failed: $($taskFailures -join ', ')" }
if (!$List) { Write-Output "PASS: $($taskChecks.Count) selected check scripts ($Group). Runtime play remains unverified." }
