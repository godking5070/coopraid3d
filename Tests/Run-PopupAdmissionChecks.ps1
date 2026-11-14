param([string]$Compiler)
$ErrorActionPreference='Stop'
if(!$Compiler){
    $taskVsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $Compiler=(& $taskVsWhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe' | Select-Object -First 1)
}
if(!$Compiler -or !(Test-Path -LiteralPath $Compiler)){throw 'C# compiler not found.'}
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskSource=[IO.File]::ReadAllText((Join-Path $taskRoot 'Assets/Scripts/FloatingTextPopup.cs'))
function Read-ActualMethod([string]$Signature){
    $taskStart=$taskSource.IndexOf($Signature,[StringComparison]::Ordinal)
    if($taskStart -lt 0){throw "Missing method $Signature"}
    $taskOpen=$taskSource.IndexOf('{',$taskStart);$taskEnd=$taskOpen+1;$taskDepth=1
    while($taskDepth -gt 0 -and $taskEnd -lt $taskSource.Length){
        if($taskSource[$taskEnd] -eq '{'){$taskDepth++}
        elseif($taskSource[$taskEnd] -eq '}'){$taskDepth--}
        $taskEnd++
    }
    if($taskDepth -ne 0){throw 'Incomplete method'}
    $taskSource.Substring($taskStart,$taskEnd-$taskStart)
}
$taskStart=$taskSource.IndexOf('private static int _spawnFrame',[StringComparison]::Ordinal)
$taskEnd=$taskSource.IndexOf('public static FloatingTextPopup SpawnDamage(',[StringComparison]::Ordinal)
if($taskStart -lt 0 -or $taskEnd -le $taskStart){throw 'State boundaries missing'}
$taskFixture=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'PopupAdmissionTests.cs'))
$taskFixture=$taskFixture.Replace('/* ACTUAL_STATE */',$taskSource.Substring($taskStart,$taskEnd-$taskStart)).Replace('/* ACTUAL_SPAWN */',(Read-ActualMethod 'public static FloatingTextPopup Spawn(')).Replace('/* ACTUAL_PLACE_NOTICE */',(Read-ActualMethod 'private void PlaceNotice('))
$taskOutput=Join-Path ([IO.Path]::GetTempPath()) ('RaidRogue-Popup-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($taskOutput)|Out-Null
$taskFile=Join-Path $taskOutput 'PopupFixture.cs';$taskExe=Join-Path $taskOutput 'PopupTests.exe'
[IO.File]::WriteAllText($taskFile,$taskFixture,[Text.UTF8Encoding]::new($false))
& $Compiler /nologo /langversion:9.0 /target:exe "/out:$taskExe" $taskFile
if($LASTEXITCODE -ne 0){throw 'Popup fixture compilation failed'}
& $taskExe
if($LASTEXITCODE -ne 0){throw 'Popup admission checks failed'}
Write-Output 'Boundary: actual admission, notice allocation and cleanup methods with rendering/network stubs; no Unity rendering or live network verification.'
