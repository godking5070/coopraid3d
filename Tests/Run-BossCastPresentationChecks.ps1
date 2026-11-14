param([string]$Compiler)
$ErrorActionPreference='Stop'
if(!$Compiler){$vsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe';$Compiler=(& $vsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)}
if(!$Compiler){throw 'C# compiler required.'}
$projectRoot=Split-Path $PSScriptRoot -Parent
$outDir=Join-Path ([IO.Path]::GetTempPath()) ('Astella-BossCast-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($outDir)|Out-Null
$exe=Join-Path $outDir 'BossCastPresentationTests.exe'
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$exe" (Join-Path $projectRoot 'Assets/Scripts/BossCastPresentation.cs') (Join-Path $PSScriptRoot 'BossCastPresentationTests.cs')
if($LASTEXITCODE -ne 0){throw 'Boss cast presentation checks failed to compile.'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Boss cast presentation checks failed.'}
