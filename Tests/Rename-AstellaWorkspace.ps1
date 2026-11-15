param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
# One-time local rename. Never starts or stops Unity, copies Library, or deletes data.
$taskOldParent = 'C:\coopraid'
$taskNewParent = 'C:\Astella'
$taskOldProject = Join-Path $taskOldParent 'coopraid3d'
$taskNewProject = Join-Path $taskNewParent 'Astella'
if (!(Test-Path -LiteralPath (Join-Path $taskOldProject '.git') -PathType Container)) { throw 'Original main repository required.' }
if ((Get-Item -LiteralPath $taskOldParent).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Source must be a real directory.' }
$taskPlanPath = Join-Path $taskNewParent 'AstellaRenamePlan.json'
if ((Test-Path -LiteralPath $taskNewProject) -and !(Test-Path -LiteralPath $taskPlanPath)) { throw 'Unplanned destination already exists; refusing to overwrite.' }
$taskEditors = @(Get-CimInstance Win32_Process -Filter "name = 'Unity.exe'" | Where-Object {
    $_.CommandLine -and $_.CommandLine.Replace('/', '\').IndexOf($taskOldParent, [StringComparison]::OrdinalIgnoreCase) -ge 0
})
if ($taskEditors.Count) { throw 'Save and close Unity before renaming.' }
if (Test-Path -LiteralPath $taskPlanPath) {
    $taskPlan = Get-Content -LiteralPath $taskPlanPath -Raw | ConvertFrom-Json
    $taskWorktrees = @($taskPlan.worktrees)
} else {
    $taskWorktrees = @(& git -C $taskOldProject worktree list --porcelain | Where-Object { $_.StartsWith('worktree ') } | ForEach-Object { $_.Substring(9) })
    if ($LASTEXITCODE) { throw 'Cannot enumerate worktrees.' }
}
foreach ($taskTree in $taskWorktrees) {
    $taskAbsolute = [IO.Path]::GetFullPath($taskTree)
    if (!$taskAbsolute.StartsWith($taskOldParent + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Worktree outside expected parent; manual review required.' }
    $taskExpectedLeaf = (Split-Path $taskAbsolute -Leaf) -replace '^coopraid3d', 'Astella'
    if (!(Test-Path -LiteralPath $taskAbsolute -PathType Container) -and !(Test-Path -LiteralPath (Join-Path $taskNewParent $taskExpectedLeaf))) { throw 'Worktree missing; repair before renaming.' }
}
$taskHead = & git -C $taskOldProject rev-parse HEAD
if ($LASTEXITCODE) { throw 'Cannot read main HEAD.' }
Write-Output "Main project: $taskOldProject -> $taskNewProject"
Write-Output "Git worktrees to preserve and repair: $($taskWorktrees.Count)"
if ($CheckOnly) { Write-Output 'PASS: rename preflight; no paths changed.'; exit 0 }
# Absolute paths are validated above; all moves stay in the two named parent directories.
if (!(Test-Path -LiteralPath $taskNewParent)) { [IO.Directory]::CreateDirectory($taskNewParent) | Out-Null }
if (!(Test-Path -LiteralPath $taskPlanPath)) {
    @{worktrees=$taskWorktrees; head=$taskHead} | ConvertTo-Json | Set-Content -LiteralPath $taskPlanPath -Encoding UTF8
}
$taskRenamedTrees = @()
foreach ($taskTree in $taskWorktrees) {
    $taskLeaf = Split-Path $taskTree -Leaf
    $taskDestinationLeaf = if ($taskLeaf -eq 'coopraid3d') { 'Astella' } else { $taskLeaf -replace '^coopraid3d-', 'Astella-' }
    $taskCurrent = Join-Path $taskOldParent $taskLeaf
    $taskDestination = Join-Path $taskNewParent $taskDestinationLeaf
    if (!(Test-Path -LiteralPath $taskDestination)) {
        Write-Output "Moving: $taskCurrent -> $taskDestination"
        try { [IO.Directory]::Move($taskCurrent, $taskDestination) }
        catch {
            Write-Output "LOCKED: $taskCurrent"
            Write-Output 'Restart Windows, then run this script from a fresh PowerShell at C:/ before opening Codex, Unity Hub or editors.'
            Write-Output 'No files were deleted or overwritten. Completed moves can be resumed by running this script again.'
            throw
        }
    } elseif ((Test-Path -LiteralPath $taskCurrent) -and !((Get-Item -LiteralPath $taskCurrent).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Both real source and destination exist; refusing to merge.'
    }
    if ($taskLeaf -eq 'coopraid3d' -and !(Test-Path -LiteralPath $taskOldProject)) {
        New-Item -ItemType Junction -Path $taskOldProject -Target $taskNewProject | Out-Null
    }
    & git -C $taskNewProject worktree repair $taskDestination
    if ($LASTEXITCODE) { throw 'Worktree repair failed; rerun after reviewing paths.' }
    $taskRenamedTrees += $taskDestination
}
& git -C $taskNewProject worktree repair @taskRenamedTrees
if ($LASTEXITCODE) { throw 'Git repair failed; preserve files and inspect paths.' }
if ((& git -C $taskNewProject rev-parse HEAD) -ne $taskHead) { throw 'Main HEAD changed unexpectedly.' }
# Existing generated reference lists are reused by command compilation, without regenerating Library.
foreach ($taskGenerated in Get-ChildItem -LiteralPath $taskNewProject -File | Where-Object { $_.Extension -in '.csproj', '.sln', '.slnx' }) {
    $taskText = [IO.File]::ReadAllText($taskGenerated.FullName)
    $taskText = $taskText.Replace('C:\coopraid\coopraid3d', $taskNewProject).Replace('C:/coopraid/coopraid3d', $taskNewProject.Replace('\','/'))
    [IO.File]::WriteAllText($taskGenerated.FullName, $taskText, [Text.UTF8Encoding]::new($false))
}
$taskOldSolution = Join-Path $taskNewProject 'coopraid3d.sln'
if (Test-Path -LiteralPath $taskOldSolution) { Move-Item -LiteralPath $taskOldSolution -Destination (Join-Path $taskNewProject 'Astella.sln') }
# Keep this active Codex session and old local tool references working. Real data lives at the new path.
if (!(Test-Path -LiteralPath $taskOldParent)) { New-Item -ItemType Directory -Path $taskOldParent | Out-Null }
if (!(Test-Path -LiteralPath $taskOldProject)) { New-Item -ItemType Junction -Path $taskOldProject -Target $taskNewProject | Out-Null }
foreach ($taskTree in $taskRenamedTrees) {
    & git -C $taskTree rev-parse --show-toplevel | Out-Null
    if ($LASTEXITCODE) { throw "Worktree verification failed: $taskTree" }
}
Write-Output 'PASS: project and worktrees renamed, Git repaired, generated references updated. Old main path is a compatibility junction.'
