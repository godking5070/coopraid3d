param(
    [string]$OriginalProject = (Split-Path $PSScriptRoot -Parent),
    [string]$Unity,
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
$taskSource = (Resolve-Path (Split-Path $PSScriptRoot -Parent)).Path
$taskOriginal = (Resolve-Path -LiteralPath $OriginalProject).Path
if (Test-Path -LiteralPath (Join-Path $taskOriginal '.git') -PathType Leaf) {
    throw 'Unity must never run in a worktree. Pass the original Unity project.'
}
if (!(Test-Path -LiteralPath (Join-Path $taskOriginal '.git') -PathType Container)) {
    throw 'The original Git project directory is required.'
}
$taskVersionText = Get-Content -LiteralPath (Join-Path $taskOriginal 'ProjectSettings/ProjectVersion.txt') -Raw
$taskVersion = [regex]::Match($taskVersionText, 'm_EditorVersion: (\S+)').Groups[1].Value
if (!$Unity) { $Unity = Join-Path ${env:ProgramFiles} "Unity/Hub/Editor/$taskVersion/Editor/Unity.exe" }
$taskProblems = [System.Collections.Generic.List[string]]::new()
if (!(Test-Path -LiteralPath $Unity)) { $taskProblems.Add("Unity editor not found: $Unity") }
# Read-only comparison. Never copy, stash, reset, switch branches, or alter user source files.
. (Join-Path $PSScriptRoot 'Part1BuildPreflight.ps1')
$taskDifference = Get-Part1SourceDifferences -SourceProject $taskSource -OriginalProject $taskOriginal
if ($taskDifference.Count -gt 0) {
    $taskProblems.Add("Original source: $($taskDifference.Missing.Count) missing, $($taskDifference.Changed.Count) changed, $($taskDifference.Unexpected.Count) additional compile inputs. Review/apply the playable branch before building. No source was changed.")
    $taskDifference.Missing | Select-Object -First 8 | ForEach-Object { Write-Output "Missing: $_" }
    $taskDifference.Changed | Select-Object -First 8 | ForEach-Object { Write-Output "Changed: $_" }
    $taskDifference.Unexpected | Select-Object -First 8 | ForEach-Object { Write-Output "Additional (preserved; needs review): $_" }
}
$taskEditors = @(Get-CimInstance Win32_Process -Filter "name = 'Unity.exe'" | Where-Object {
    $_.CommandLine -and $_.CommandLine.Replace('/', '\').IndexOf($taskOriginal.Replace('/', '\'), [StringComparison]::OrdinalIgnoreCase) -ge 0
})
if ($taskEditors.Count -gt 0) { $taskProblems.Add('Original Unity editor is open. Use its build menu after source review, or close it before this batch command.') }
$taskOutput = Join-Path $taskOriginal 'Builds/AstellaWindows'
Write-Output "Original: $taskOriginal"
Write-Output "Unity: $Unity"
Write-Output "Output: $(Join-Path $taskOutput 'Astella.exe')"
if ($taskProblems.Count -gt 0) {
    foreach ($taskProblem in $taskProblems) { Write-Output "BLOCKED: $taskProblem" }
    if ($CheckOnly) { exit 2 }
    throw 'Preflight failed; Unity was not launched.'
}
if ($CheckOnly) { Write-Output 'PASS: read-only preflight. No build or gameplay executed.'; exit 0 }
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskLog = Join-Path $taskOutput 'UnityBuild.log'
if ($taskOriginal.Contains('"') -or $taskLog.Contains('"')) { throw 'Unsupported quote in path.' }
$taskArguments = '-batchmode -quit -projectPath "' + $taskOriginal + '" -executeMethod RaidRogue.EditorTools.RaidMultiplayerBuild.BuildPartOne -logFile "' + $taskLog + '"'
$taskStarted = [DateTime]::UtcNow
$taskProcess = Start-Process -FilePath $Unity -ArgumentList $taskArguments -WindowStyle Hidden -PassThru
$taskProcess.WaitForExit()
if ($taskProcess.ExitCode -ne 0) { throw "Unity build failed ($($taskProcess.ExitCode)); inspect $taskLog" }
if (!(Test-Path -LiteralPath (Join-Path $taskOutput 'Astella.exe')) -or !(Test-Path -LiteralPath (Join-Path $taskOutput 'BUILD_INFO.txt'))) {
    throw "Unity did not produce the expected executable and build receipt. Inspect $taskLog"
}
if ((Get-Item -LiteralPath (Join-Path $taskOutput 'BUILD_INFO.txt')).LastWriteTimeUtc -lt $taskStarted) {
    throw 'The build receipt is from an older build. Do not report the old executable as this build result.'
}
Write-Output "PASS: Windows executable built at $taskOutput. Gameplay and Steam connections still require the manual checklist."
