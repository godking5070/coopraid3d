$ErrorActionPreference = 'Stop'
$taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$taskCompiler = (& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
$taskExe = Join-Path ([IO.Path]::GetTempPath()) ('GuardianChecks-' + [Guid]::NewGuid().ToString('N') + '.exe')
& $taskCompiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" "$PSScriptRoot/../Assets/Scripts/GuardianNormalData.cs" "$PSScriptRoot/GuardianNormalTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Guardian test compilation failed' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Guardian tests failed' }
