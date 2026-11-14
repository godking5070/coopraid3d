# Read-only source parity check. Unity/Git may use CRLF or LF, with or without a UTF-8 BOM.
# Preserve all other text (including case and whitespace); never copy or normalize files on disk.
function Get-Part1SourceDifferences {
    param([Parameter(Mandatory = $true)][string]$SourceProject,
          [Parameter(Mandatory = $true)][string]$OriginalProject)
    $sourceRoot = (Resolve-Path -LiteralPath $SourceProject).Path.TrimEnd('\', '/')
    $originalRoot = (Resolve-Path -LiteralPath $OriginalProject).Path.TrimEnd('\', '/')
    $extensions = @('-g', '*.cs', '-g', '*.asmdef', '-g', '*.asmref', '-g', '*.rsp')
    $sourceFiles = @(& rg --files (Join-Path $sourceRoot 'Assets') @extensions)
    if ($LASTEXITCODE -gt 1) { throw 'Cannot enumerate candidate source files.' }
    $originalFiles = @(& rg --files (Join-Path $originalRoot 'Assets') @extensions)
    if ($LASTEXITCODE -gt 1) { throw 'Cannot enumerate original source files.' }
    $relativeFiles = @($sourceFiles | ForEach-Object { $_.Substring($sourceRoot.Length + 1).Replace('\', '/') })
    $relativeFiles += @('Assets/Resources/RaidSteamSettings.json', 'Packages/manifest.json',
        'Packages/packages-lock.json', 'ProjectSettings/ProjectVersion.txt')
    $missing = [System.Collections.Generic.List[string]]::new()
    $changed = [System.Collections.Generic.List[string]]::new()
    $unexpected = [System.Collections.Generic.List[string]]::new()
    foreach ($relative in ($relativeFiles | Sort-Object -Unique)) {
        $source = Join-Path $sourceRoot $relative
        $original = Join-Path $originalRoot $relative
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Candidate is incomplete: $relative" }
        if (!(Test-Path -LiteralPath $original -PathType Leaf)) { $missing.Add($relative); continue }
        $expectedText = [IO.File]::ReadAllText($source).Replace("`r`n", "`n")
        $actualText = [IO.File]::ReadAllText($original).Replace("`r`n", "`n")
        if ($expectedText -cne $actualText) { $changed.Add($relative) }
    }
    foreach ($file in $originalFiles) {
        $relative = $file.Substring($originalRoot.Length + 1).Replace('\', '/')
        if ($relativeFiles -notcontains $relative) { $unexpected.Add($relative) }
    }
    [pscustomobject]@{ Missing = $missing.ToArray(); Changed = $changed.ToArray();
        Unexpected = @($unexpected.ToArray() | Sort-Object); Count = $missing.Count + $changed.Count + $unexpected.Count }
}
