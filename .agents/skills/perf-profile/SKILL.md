---
name: perf-profile
description: Investigate reported frame stalls, transformation overload, popup floods, or allocation hotspots in coopraid3d using code and supplied runtime evidence.
license: MIT; see ../LICENSE.ccgs.txt
---

# Coopraid performance investigation

Use the project root containing AGENTS.md and Assets/Scripts. Follow AGENTS.md, especially the prohibition on launching or focusing Unity, Steam, or the game. This skill authorizes no UI automation, asset rewriting, or external actions.

Start with the reported boss, difficulty, transition and player/AI mode. Trace the actual entry points and state changes before proposing a fix. Relevant paths include BossController.cs, Boss*Hard.cs, RaidReplacementAI.cs, PartyBotAI.cs, RaidCombatSession.cs, CharacterActor.cs, FloatingTextPopup.cs and AoETelegraph.cs.

Check for repeated creation during a state instead of once on entry, failed prefab-load retries, duplicate host/replica effects, unbounded popup/telegraph creation, per-frame searches or allocations, and missing cleanup on completion, interruption, disable and destruction. For transformation overload, compare HardTransformationActive and HpDamageBlocked with offensive AI validation and host action gates. Confirm that destructible adds remain targetable and attacks resume after the transition.

Keep static evidence separate from measurements. A reachable allocation loop is a code finding; its frame-time impact remains unmeasured without profiler evidence. Record absent profiler captures, FPS, memory, draw-call data or agreed budgets as NOT ASSESSED. Do not invent performance gains, budgets or a clean runtime verdict from compilation alone. Analyze supplied logs or bug-report ZIPs read-only; extract to a temporary directory when necessary.

Use rg for bounded source searches. Choose the relevant existing Tests/Run-*Checks.ps1 commands; ../regression-suite/SKILL.md explains the grouped runner. Favor a narrow correction that preserves damage, pattern order, timing and authored assets unless the user requested those changes. Do not introduce telemetry or new dependencies merely to complete this review.

Report concrete file/line evidence, triggering state, suspected mechanism, checks actually run and remaining runtime questions. Give the user a short original-project play sequence and expected behavior. Save a report only when requested or when it is a useful deliverable of the current task.

Adapted from Claude Code Game Studios perf-profile; pinned source and changes: [adoption record](../../../docs/tooling/GAME_STUDIOS_ADOPTION.md).
