param([string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $taskVsWhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
    $Compiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$Compiler) { throw 'A C# 9+ compiler is required.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path ([IO.Path]::GetTempPath()) ('RaidBugChecks-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskExe = Join-Path $taskOutput 'DevBugReportTests.exe'
try {
    & $Compiler /nologo /define:DEVELOPMENT_BUILD /langversion:9.0 /target:exe /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/out:$taskExe" (Join-Path $taskRoot 'Assets/Scripts/DevBugReportArchive.cs') (Join-Path $PSScriptRoot 'DevBugReportTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Diagnostic ZIP tests failed to compile.' }
    & $taskExe
    if ($LASTEXITCODE -ne 0) { throw 'Diagnostic ZIP tests failed.' }
    $taskRelease = Join-Path $taskOutput 'DiagnosticRelease.dll'
    & $Compiler /nologo /langversion:9.0 /target:library "/out:$taskRelease" (Join-Path $taskRoot 'Assets/Scripts/DevBugReportArchive.cs') (Join-Path $taskRoot 'Assets/Scripts/DevBugReportUI.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Release preprocessor check failed to compile.' }
    $taskCheckSource = Join-Path $taskOutput 'CheckRelease.cs'
    [IO.File]::WriteAllText($taskCheckSource, 'using System; using System.Reflection; class CheckRelease { static int Main(string[] args) { return Assembly.LoadFrom(args[0]).GetTypes().Length == 0 ? 0 : 1; } }')
    $taskCheckExe = Join-Path $taskOutput 'CheckRelease.exe'
    & $Compiler /nologo /target:exe "/out:$taskCheckExe" $taskCheckSource
    if ($LASTEXITCODE -ne 0) { throw 'Release reflection check failed to compile.' }
    & $taskCheckExe $taskRelease
    if ($LASTEXITCODE -ne 0) { throw 'Diagnostic entrypoint leaked into release assembly.' }
    Write-Output 'PASS: development-only diagnostic types absent without development defines.'
} finally {
    $resolved = [IO.Path]::GetFullPath($taskOutput)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^RaidBugChecks-[0-9a-f]{32}$') { throw 'Unsafe fixture cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
