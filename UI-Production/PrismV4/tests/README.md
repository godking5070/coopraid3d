# Prism V4 static checks

From the production repository root, in PowerShell:

```powershell
& ./UI-Production/PrismV4/tests/Run-StaticChecks.ps1
```

On another machine, supply existing installed Unity and uGUI reference locations:

```powershell
& ./UI-Production/PrismV4/tests/Run-StaticChecks.ps1 `
  -UnityEditorRoot 'C:/Program Files/Unity/Hub/Editor/6000.3.26f1' `
  -UGuiAssemblyDirectory 'C:/YourExistingProject/Library/ScriptAssemblies'
```

The runner reads the existing `UnityEngine.UI.dll` and `UnityEditor.UI.dll`. It does not open Unity, build or copy a project Library, change the referenced project, enter Play mode, or create prefab/scene files. Compiled binaries go to a fresh OS temporary directory and are not part of the handoff source.

It compiles all five package Runtime/Editor C# files against the installed Unity API and then runs `GaugeGeometryCheck.cs`. The 64 cases call the same geometry and sprite-mapping methods used by the renderer. They include the original 32 boss/cast/player/party ratio, vertical-ultimate and NaN cases; 30 trimmed-sprite/atlas padding cases; and two preserve-aspect/pivot cases. The harness also reflects the actual installed `SpriteMeshType` enum values.

The trimmed-sprite regression checks match the visible bounds of ordinary uGUI `Image.GetDrawingDimensions`, map the visible rectangle exactly to `OuterUV`, and prevent sampling neighboring atlas sprites. A representative boss sprite trimmed from a 96px canvas must occupy its intended 30px inner height in a 72px UI rect, rather than stretching the trimmed fill over the entire rect. The ratio polygon and alpha-tip geometry remain in original full-sprite coordinates.

`last-run.json` records the actual outcome and source hashes. These checks do not validate Unity import, menu execution, serialized prefab output, Canvas/GPU rendering, pointer interaction or live game bindings. Pixel alpha and source-resource checks are separately provided by `../verify-pack.cjs`.
