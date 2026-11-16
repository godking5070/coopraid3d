param([string]$Project = (Split-Path $PSScriptRoot -Parent))

# Architectural boundary: editor import and Play transitions must not run UI authoring.
# This checks source entry points only; it does not launch Unity or edit any assets.
$ErrorActionPreference = 'Stop'
$taskEditor = Join-Path $Project 'Assets/Scripts/Editor'
$taskChecks = 0

function Assert-AuthoringSafety([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
    $script:taskChecks++
}
function Without-Comments([string]$Source) {
    return [regex]::Replace([regex]::Replace($Source, '(?s)/\*.*?\*/', ''), '(?m)//.*$', '')
}
function Has-AutomaticEntry([string]$Source) {
    return ($Source -match '\[\s*(?:UnityEditor\.)?InitializeOnLoad(?:Method)?(?:Attribute)?\b|\[\s*(?:UnityEditor\.Callbacks\.)?DidReloadScripts\b|EditorApplication\s*\.\s*(?:update|delayCall|playModeStateChanged)\s*\+=')
}

# Scan the entire authoring directory so a new installer cannot silently evade the rule.
$taskFiles = @(Get-ChildItem -LiteralPath $taskEditor -Filter '*.cs' -File -Recurse)
foreach ($taskFile in $taskFiles) {
    $taskSource = Without-Comments ([IO.File]::ReadAllText($taskFile.FullName))
    if ($taskFile.Name -eq 'SceneAutoBuilder.cs') {
        # Its InitializeOnLoad class is harmless only while the static constructor is empty.
        $taskConstructor = [regex]::Match($taskSource, '(?s)static\s+SceneAutoBuilder\s*\(\s*\)\s*\{(?<body>.*?)\}')
        Assert-AuthoringSafety ($taskConstructor.Success -and $taskConstructor.Groups['body'].Value.Trim().Length -eq 0) 'SceneAutoBuilder must not generate a scene on editor import.'
    } elseif ($taskFile.Name -eq 'RaidUIGenerationChecks.cs') {
        # Explicit batch smoke tests may resume after a domain reload, but never normal import.
        Assert-AuthoringSafety ($taskSource.Contains('if (!Application.isBatchMode || SessionState.GetString(Key + "phase", "").Length == 0) return;')) 'UI batch check reload hook must require an explicit batch session.'
    } elseif ($taskFile.Name -eq 'AstellaPrismPlayChecks.cs') {
        # A user-started passive observer may tick; it cannot author assets or start on import.
        Assert-AuthoringSafety ($taskSource -notmatch '\[\s*(?:UnityEditor\.)?InitializeOnLoad|DidReloadScripts|static\s+AstellaPrismPlayChecks\s*\(') 'Prism observer must have no startup or reload entry.'
        Assert-AuthoringSafety ($taskSource.Contains('[MenuItem("Tools/Astella/Prism V4/Watch Live HUD %#F8")]') -and $taskSource.Contains('if (!EditorApplication.isPlaying)')) 'Prism observer must start explicitly in Play Mode.'
        Assert-AuthoringSafety (([regex]::Matches($taskSource, 'EditorApplication\s*\.\s*\w+\s*\+=').Count -eq 1) -and $taskSource.Contains('EditorApplication.update += WatchTick;')) 'Only the explicit observer tick may be registered.'
        Assert-AuthoringSafety ($taskSource -notmatch 'SaveAssets\s*\(|SaveAsPrefabAsset\s*\(|SaveScene\s*\(|SetDirty\s*\(|\.SetActive\s*\(|\.SetValue\s*\(') 'Passive Prism observer must not modify assets or gameplay presentation sources.'
        Assert-AuthoringSafety ($taskSource.Contains('EditorApplication.update -= WatchTick;') -and $taskSource.Contains('EditorApplication.timeSinceStartup >= _deadline')) 'Prism observer must detach on stop and have a time limit.'
    } else {
        Assert-AuthoringSafety (!(Has-AutomaticEntry $taskSource)) ("Automatic editor entry can modify authored UI: " + $taskFile.Name)
    }
}

# Removing startup automation must not remove the user's explicit authoring tools.
$taskMenus = @{
    'AstellaPlayerHUDV3Installer.cs' = @('Apply Player HUD v3')
    'AstellaBossHUDV4Installer.cs' = @('Apply Boss HUD v4')
    'AstellaRequestedUIResourceSwap.cs' = @('Apply All Requested UI Resources')
    'AstellaClassHubsInstaller.cs' = @('Apply Class Ultimate and Job Hubs')
    'AstellaClassHubLayoutRefinement.cs' = @('Apply Class Hub Key Layout')
    'AstellaHUDTextOutlineInstaller.cs' = @('Apply HUD Text Outlines')
    'AstellaSplitCombatHUD.cs' = @('Apply Split Combat HUD')
    'AstellaPlayHUDLayout.cs' = @('Capture Current Play HUD Layout', 'Apply Captured Play HUD Layout')
    'RaidUIAuthoring.cs' = @('Upgrade Missing Minimap and Lobby Prompts', 'Add Lobby Prompts to Active RaidHUD', 'Add Minimap to Active RaidHUD')
    'RaidUISeparation.cs' = @('Separate Editable UI Prefabs')
}
foreach ($taskName in $taskMenus.Keys) {
    $taskSource = Without-Comments ([IO.File]::ReadAllText((Join-Path $taskEditor $taskName)))
    foreach ($taskMenu in $taskMenus[$taskName]) {
        $taskPattern = '\[MenuItem\("[^"\r\n]*/' + [regex]::Escape($taskMenu) + '"\)\]'
        Assert-AuthoringSafety ($taskSource -match $taskPattern) ("Missing explicit authoring menu: " + $taskMenu)
    }
}

Write-Output "PASS: $taskChecks UI authoring entry-point checks. Import/Play hooks do not reapply UI; explicit menus remain available."
Write-Output 'Boundary: source policy inspection only; Unity import lifecycle, rendering and menu execution remain separate checks.'
