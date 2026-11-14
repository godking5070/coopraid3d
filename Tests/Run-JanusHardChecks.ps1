$ErrorActionPreference='Stop'
$janusHardVsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$janusHardCompiler=(& $janusHardVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
if(!$janusHardCompiler){throw 'C# compiler not found'}
$janusHardExe=Join-Path ([IO.Path]::GetTempPath()) ('JanusHardChecks-'+[Guid]::NewGuid().ToString('N')+'.exe')
& $janusHardCompiler /nologo /langversion:9.0 /target:exe "/out:$janusHardExe" "$PSScriptRoot/../Assets/Scripts/JanusNormalData.cs" "$PSScriptRoot/../Assets/Scripts/JanusHardData.cs" "$PSScriptRoot/JanusHardTests.cs"
if($LASTEXITCODE -ne 0){throw 'Janus hard rule test compilation failed'}
& $janusHardExe
if($LASTEXITCODE -ne 0){throw 'Janus hard rule tests failed'}
