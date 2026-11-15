param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
$taskOldData = Join-Path $env:USERPROFILE 'AppData/LocalLow/DefaultCompany/coopraid3d'
$taskNewData = Join-Path $env:USERPROFILE 'AppData/LocalLow/DefaultCompany/아스텔라'
if (Test-Path -LiteralPath $taskOldData) {
    if (Test-Path -LiteralPath $taskNewData) { throw 'New save directory already exists; refusing to merge or overwrite progress.' }
    if (!$CheckOnly) {
        Copy-Item -LiteralPath $taskOldData -Destination $taskNewData -Recurse
        foreach ($taskFile in Get-ChildItem -LiteralPath $taskOldData -Recurse -File) {
            $taskRelative = $taskFile.FullName.Substring($taskOldData.Length).TrimStart('\')
            $taskDestination = Join-Path $taskNewData $taskRelative
            if ((Get-FileHash -LiteralPath $taskFile.FullName).Hash -ne (Get-FileHash -LiteralPath $taskDestination).Hash) { throw 'Save copy verification failed; originals preserved.' }
        }
    }
    Write-Output 'Local saves/settings: old folder preserved, new product folder prepared.'
}
foreach ($taskBase in @('HKCU:\Software\DefaultCompany', 'HKCU:\Software\Unity\UnityEditor\DefaultCompany')) {
    $taskOldKey = Join-Path $taskBase 'coopraid3d'
    $taskNewKey = Join-Path $taskBase '아스텔라'
    if (!(Test-Path -LiteralPath $taskOldKey)) { continue }
    if (Test-Path -LiteralPath $taskNewKey) { throw 'New PlayerPrefs key already exists; refusing to overwrite preferences.' }
    if (!$CheckOnly) {
        $taskRegistry = Get-Item -LiteralPath $taskOldKey
        New-Item -Path $taskNewKey -Force | Out-Null
        foreach ($taskValueName in $taskRegistry.GetValueNames()) {
            $taskValue = $taskRegistry.GetValue($taskValueName, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            New-ItemProperty -LiteralPath $taskNewKey -Name $taskValueName -Value $taskValue -PropertyType $taskRegistry.GetValueKind($taskValueName) | Out-Null
        }
        $taskMigratedKey = Get-Item -LiteralPath $taskNewKey
        if ($taskMigratedKey.GetValueNames().Count -ne $taskRegistry.GetValueNames().Count) { throw 'PlayerPrefs count verification failed; old key preserved.' }
    }
    Write-Output 'PlayerPrefs namespace prepared; old key preserved.'
}
Write-Output $(if ($CheckOnly) { 'PASS: read-only local data migration preflight.' } else { 'PASS: local data copied with original files and preferences preserved.' })
