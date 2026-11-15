$ErrorActionPreference = 'Stop'
$taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$taskCompiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
if (!$taskCompiler) { throw 'C# compiler not found' }
$taskExe = Join-Path ([IO.Path]::GetTempPath()) ('FloraNormalChecks-' + [Guid]::NewGuid().ToString('N') + '.exe')
& $taskCompiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" "$PSScriptRoot/../Assets/Scripts/FloraNormalData.cs" "$PSScriptRoot/FloraNormalTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Flora rule test compilation failed' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Flora rule tests failed' }
