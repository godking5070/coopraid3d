[CmdletBinding()]
param(
    [string]$UnityEditorRoot = 'C:/Program Files/Unity/Hub/Editor/6000.3.26f1',
    [string]$UGuiAssemblyDirectory = 'C:/Astella/Astella/Library/ScriptAssemblies'
)

$ErrorActionPreference = 'Stop'
$prismWorkspace = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$prismEditorData = Join-Path $UnityEditorRoot 'Editor/Data'
$prismMono = Join-Path $prismEditorData 'MonoBleedingEdge/bin/mono.exe'
$prismCompiler = Join-Path $prismEditorData 'MonoBleedingEdge/lib/mono/4.5/csc.exe'
$prismUi = Join-Path $UGuiAssemblyDirectory 'UnityEngine.UI.dll'
$prismUiEditor = Join-Path $UGuiAssemblyDirectory 'UnityEditor.UI.dll'
foreach ($prismRequired in @($prismMono, $prismCompiler, $prismUi, $prismUiEditor)) {
    if (!(Test-Path -LiteralPath $prismRequired)) { throw "Required existing reference missing: $prismRequired" }
}

# Only the static compiler and Mono are run. Do not open Unity or create/copy a project Library.
# Generated assemblies stay outside the repository, in a fresh task-owned temporary directory.
$prismOutput = Join-Path $env:TEMP ('astella-prism-v4-check-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($prismOutput) | Out-Null
$prismAssembly = Join-Path $prismOutput 'PrismV4.Handoff.dll'
$prismHarnessAssembly = Join-Path $prismOutput 'GaugeGeometryCheck.exe'
$prismEngineDirectory = Join-Path $prismEditorData 'Managed/UnityEngine'
$prismReferences = @(Get-ChildItem -LiteralPath $prismEngineDirectory -Filter '*.dll' | ForEach-Object FullName)
$prismReferences += @(Get-ChildItem -LiteralPath (Join-Path $prismEditorData 'NetStandard/ref/2.1.0') -Filter '*.dll' | ForEach-Object FullName)
$prismReferences += $prismUi, $prismUiEditor
$prismReferenceArguments = @($prismReferences | ForEach-Object { '-r:' + $_ })
$prismSourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $prismWorkspace 'Assets/UI/AstellaPrismV4/Runtime'), (Join-Path $prismWorkspace 'Assets/UI/AstellaPrismV4/Editor') -Filter '*.cs' | Sort-Object FullName | ForEach-Object FullName)

$prismCompileArguments = @('-nologo', '-nostdlib', '-target:library', '-define:UNITY_EDITOR', ('-out:' + $prismAssembly))
$prismCompileArguments += $prismReferenceArguments
$prismCompileArguments += $prismSourceFiles
$prismCompileLog = @(& $prismMono $prismCompiler @prismCompileArguments 2>&1)
if ($LASTEXITCODE -ne 0) { throw ($prismCompileLog -join [Environment]::NewLine) }

$prismHarnessArguments = @('-nologo', '-nostdlib', '-target:exe', ('-out:' + $prismHarnessAssembly), ('-r:' + $prismAssembly))
$prismHarnessArguments += $prismReferenceArguments
$prismHarnessArguments += (Join-Path $PSScriptRoot 'GaugeGeometryCheck.cs')
$prismHarnessCompileLog = @(& $prismMono $prismCompiler @prismHarnessArguments 2>&1)
if ($LASTEXITCODE -ne 0) { throw ($prismHarnessCompileLog -join [Environment]::NewLine) }

$prismPreviousMonoPath = $env:MONO_PATH
try {
    $env:MONO_PATH = $prismEngineDirectory + ';' + $UGuiAssemblyDirectory + ';' + $prismOutput
    $prismGeometryLog = @(& $prismMono $prismHarnessAssembly 2>&1)
    if ($LASTEXITCODE -ne 0) { throw ($prismGeometryLog -join [Environment]::NewLine) }
}
finally { $env:MONO_PATH = $prismPreviousMonoPath }

$prismEvidence = [ordered]@{
    passed = $true
    completedUtc = [DateTime]::UtcNow.ToString('o')
    unityEditorReference = $UnityEditorRoot
    uguiAssemblyReference = $UGuiAssemblyDirectory
    sourceFiles = @($prismSourceFiles | ForEach-Object { $_.Substring($prismWorkspace.Length + 1).Replace('\', '/') })
    sourceSha256 = @($prismSourceFiles | ForEach-Object { [ordered]@{ path = $_.Substring($prismWorkspace.Length + 1).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() } })
    geometryCases = 64
    geometryOutput = (($prismGeometryLog -join [Environment]::NewLine).Trim())
    binariesTracked = $false
    unityEditorLaunched = $false
    playModeRun = $false
    limitations = @('Static C# API compilation and pure geometry only.', 'No Unity import, prefab menu execution, Canvas/GPU rendering or live gameplay binding validation.')
}
$prismEvidencePath = Join-Path $PSScriptRoot 'last-run.json'
[System.IO.File]::WriteAllText($prismEvidencePath, ($prismEvidence | ConvertTo-Json -Depth 6) + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding $false))
Write-Output ('PASS: compiled ' + $prismSourceFiles.Count + ' Runtime/Editor C# sources against installed Unity references.')
Write-Output $prismEvidence.geometryOutput
Write-Output ('Evidence: ' + $prismEvidencePath)
