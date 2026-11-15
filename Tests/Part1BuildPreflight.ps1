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

# Normalize text line endings/BOM; binary assets are hashed byte-for-byte.
function Get-Part1ProjectFingerprint {
    param([Parameter(Mandatory = $true)][string]$Project)
    $fingerprintRoot = (Resolve-Path -LiteralPath $Project).Path.TrimEnd('\', '/')
    $fingerprintFiles = @(& rg --files (Join-Path $fingerprintRoot 'Assets') (Join-Path $fingerprintRoot 'Packages') (Join-Path $fingerprintRoot 'ProjectSettings'))
    if ($LASTEXITCODE -gt 1) { throw 'Cannot enumerate project build inputs.' }
    $textExtensions = @('.cs', '.meta', '.unity', '.prefab', '.asset', '.mat', '.inputactions', '.wlt', '.json', '.txt', '.asmdef', '.asmref', '.rsp', '.shader', '.hlsl', '.cginc', '.shadersubgraph', '.shadergraph', '.anim', '.controller', '.playable', '.overridecontroller')
    $fingerprintEntries = [System.Collections.Generic.List[object]]::new()
    $fingerprintHash = [Security.Cryptography.SHA256]::Create()
    try {
        foreach ($fingerprintFile in $fingerprintFiles) {
            $fingerprintRelative = $fingerprintFile.Substring($fingerprintRoot.Length + 1).Replace([IO.Path]::DirectorySeparatorChar, '/')
            if ([IO.Path]::GetExtension($fingerprintFile).ToLowerInvariant() -in $textExtensions) {
                try {
                    $fingerprintText = [IO.File]::ReadAllText($fingerprintFile, [Text.UTF8Encoding]::new($false, $true))
                    if ($fingerprintText.IndexOf([char]0) -ge 0) { throw [Text.DecoderFallbackException]::new('Binary serialized asset') }
                    $fingerprintBytes = [Text.Encoding]::UTF8.GetBytes($fingerprintText.Replace("`r`n", "`n"))
                    $fingerprintDigest = [BitConverter]::ToString($fingerprintHash.ComputeHash($fingerprintBytes)).Replace('-', '').ToLowerInvariant()
                } catch [Text.DecoderFallbackException] {
                    $fingerprintDigest = (Get-FileHash -LiteralPath $fingerprintFile -Algorithm SHA256).Hash.ToLowerInvariant()
                }
            } else { $fingerprintDigest = (Get-FileHash -LiteralPath $fingerprintFile -Algorithm SHA256).Hash.ToLowerInvariant() }
            $fingerprintEntries.Add([pscustomobject]@{ path = $fingerprintRelative; sha256 = $fingerprintDigest })
        }
        $fingerprintLines = [string[]]@($fingerprintEntries | ForEach-Object { $_.path + "`t" + $_.sha256 + "`n" })
        [Array]::Sort($fingerprintLines, [StringComparer]::Ordinal)
        $fingerprintDigest = [BitConverter]::ToString($fingerprintHash.ComputeHash([Text.Encoding]::UTF8.GetBytes(($fingerprintLines -join '')))).Replace('-', '').ToLowerInvariant()
        [pscustomobject]@{ sha256 = $fingerprintDigest; count = $fingerprintEntries.Count; files = @($fingerprintEntries.ToArray()) }
    } finally { $fingerprintHash.Dispose() }
}
