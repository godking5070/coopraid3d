$ErrorActionPreference='Stop'
$janusVsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$janusCompiler=(& $janusVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
if(!$janusCompiler){throw 'C# compiler not found'}
$janusExe=Join-Path ([IO.Path]::GetTempPath()) ('JanusNormalChecks-'+[Guid]::NewGuid().ToString('N')+'.exe')
& $janusCompiler /nologo /langversion:9.0 /target:exe "/out:$janusExe" "$PSScriptRoot/../Assets/Scripts/JanusNormalData.cs" "$PSScriptRoot/JanusNormalTests.cs"
if($LASTEXITCODE -ne 0){throw 'Janus rule test compilation failed'}
& $janusExe
if($LASTEXITCODE -ne 0){throw 'Janus rule tests failed'}
