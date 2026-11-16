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

It compiles all five package Runtime/Editor C# files against the installed Unity API and then runs `GaugeGeometryCheck.cs`. The 32 cases call the same `PrismV4Gauge.BuildClipPolygon` method used by the renderer: boss/cast/player/party ratios 0, .001, .01, .5, .6, .99 and 1; central ultimate vertical ratios 0/.5/1; and NaN. Checks cover empty/full geometry, source-space moving fronts, monotonic reveal area and bounded UV coordinates.

`last-run.json` records the actual outcome and source hashes. These checks do not validate Unity import, menu execution, serialized prefab output, Canvas/GPU rendering, pointer interaction or live game bindings. Pixel alpha and source-resource checks are separately provided by `../verify-pack.cjs`.
