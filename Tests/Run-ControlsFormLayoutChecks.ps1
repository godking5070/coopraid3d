param([string]$Compiler)
$ErrorActionPreference = 'Stop'
if (!$Compiler) {
    $layoutVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler = (& $layoutVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if (!$Compiler) { throw 'A C# 9+ compiler is required.' }
$layoutRoot = Split-Path $PSScriptRoot -Parent
$layoutSource = [IO.File]::ReadAllText((Join-Path $layoutRoot 'Assets/Scripts/LocalCameraSettingsUI.cs'))
$layoutStart = $layoutSource.IndexOf('private static void ExtendControlsForm(', [StringComparison]::Ordinal)
if ($layoutStart -lt 0) { throw 'Controls form extension method is missing.' }
$layoutOpen = $layoutSource.IndexOf('{', $layoutStart)
$layoutEnd = $layoutOpen + 1; $layoutDepth = 1
while ($layoutDepth -gt 0 -and $layoutEnd -lt $layoutSource.Length) {
    if ($layoutSource[$layoutEnd] -eq '{') { $layoutDepth++ }
    elseif ($layoutSource[$layoutEnd] -eq '}') { $layoutDepth-- }
    $layoutEnd++
}
if ($layoutDepth -ne 0) { throw 'Controls form extension method is incomplete.' }
$layoutTemplate = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'ControlsFormLayoutTests.cs'))
$layoutTemplate = $layoutTemplate.Replace('/* ACTUAL_CONTROLS_FORM_EXTENSION */', $layoutSource.Substring($layoutStart, $layoutEnd - $layoutStart))
$layoutOutput = Join-Path ([IO.Path]::GetTempPath()) ('Astella-ControlsForm-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($layoutOutput) | Out-Null
$layoutFixture = Join-Path $layoutOutput 'ControlsFormFixture.cs'
[IO.File]::WriteAllText($layoutFixture, $layoutTemplate, [Text.UTF8Encoding]::new($false))
$layoutExe = Join-Path $layoutOutput 'ControlsFormLayoutTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$layoutExe" (Join-Path $layoutRoot 'Assets/Scripts/LocalSettingsData.cs') (Join-Path $layoutRoot 'Assets/Scripts/RaidUIForm.cs') $layoutFixture
if ($LASTEXITCODE -ne 0) { throw 'Controls form layout tests failed to compile.' }
& $layoutExe
if ($LASTEXITCODE -ne 0) { throw 'Controls form layout tests failed.' }
