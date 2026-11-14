param([string]$Compiler)
$ErrorActionPreference='Stop'
if(!$Compiler){$vsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe';$Compiler=(& $vsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)}
if(!$Compiler){throw 'C# compiler required.'}
$projectRoot=Split-Path $PSScriptRoot -Parent
$outDir=Join-Path ([IO.Path]::GetTempPath()) ('Astella-ClickMovement-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($outDir)|Out-Null
$exe=Join-Path $outDir 'ClickMoveTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$exe" (Join-Path $projectRoot 'Assets/Scripts/ClickMoveState.cs') (Join-Path $projectRoot 'Assets/Scripts/LocalSettingsData.cs') (Join-Path $PSScriptRoot 'ClickMoveTests.cs')
if($LASTEXITCODE -ne 0){throw 'Click movement checks failed to compile.'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Click movement checks failed.'}
$settingsExe=Join-Path $outDir 'LocalSettingsTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$settingsExe" (Join-Path $projectRoot 'Assets/Scripts/LocalSettingsData.cs') (Join-Path $PSScriptRoot 'LocalSettingsTests.cs')
if($LASTEXITCODE -ne 0){throw 'Settings regression failed to compile.'}
& $settingsExe
if($LASTEXITCODE -ne 0){throw 'Settings regression failed.'}
