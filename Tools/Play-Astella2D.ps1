param([string]$UnityEditor)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (Test-Path -LiteralPath (Join-Path $projectRoot '.git') -PathType Leaf) {
    throw 'Open Unity only in the original project, not in a Git worktree.'
}
if (!$UnityEditor) {
    $versionText = Get-Content -LiteralPath (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') -Raw
    $version = [regex]::Match($versionText, 'm_EditorVersion:\s*(\S+)').Groups[1].Value
    $UnityEditor = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$version/Editor/Unity.exe"
}
if (!(Test-Path -LiteralPath $UnityEditor)) { throw "Unity Editor not found: $UnityEditor" }
$openEditors = @(Get-Process -Name Unity -ErrorAction SilentlyContinue)
if ($openEditors.Count -gt 0) {
    Write-Output 'Unity is already open. In the original project choose Astella > 2D > Play Game.'
    return
}
$arguments = @('-projectPath', ('"' + $projectRoot + '"'), '-executeMethod', 'Astella.TwoD.Editor.Astella2DEditor.Play2D')
Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Normal
