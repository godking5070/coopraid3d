---
name: regression-suite
description: Select and run existing coopraid3d boss/combat regression checks after relevant fixes, audit scenario gaps, and separate command-based results from user play verification.
license: MIT; see ../LICENSE.ccgs.txt
---

# Coopraid boss regression checks

Follow project AGENTS.md. No Unity/Steam/game launch, UI automation, Library creation/copy, YAML asset editing, automatic merge or push. Existing tests compile standalone C# into temporary paths; compilation checks may read references from the original project's existing Library and csproj files.

Use the current user request, changed code and docs/part1 specifications as the scenario baseline. Use Tests/*Tests.cs and Tests/Run-*Checks.ps1 as the actual inventory. A matching test filename is not proof of scenario coverage: read its assertions and source list when deciding what it verifies. Do not require upstream GDDs, sprint files, project.yaml or smoke screenshots to perform this project's checks.

From the project root:

- Tests/Run-StudioChecks.ps1 -Group BossHard: all six hard boss logic suites plus shared raidwide/health rules.
- Tests/Run-StudioChecks.ps1 -Group Compile -OriginalProject C:/coopraid/coopraid3d: combat metrics, spectator damage and managed runtime/editor compilation using existing original-project references.
- Tests/Run-StudioChecks.ps1 -Group Network -OriginalProject C:/coopraid/coopraid3d: existing protocol, combat network and transport-related checks. This is command-based validation, not live multiplayer.
- Tests/Run-StudioChecks.ps1 -Group Steam -OriginalProject C:/coopraid/coopraid3d: Steam policy, fake transport, recovery/reconnect, run flow, settings and character checks, plus official Steamworks.NET managed compilation and the existing Network/Compile and campaign-save dependency chains. It does not launch Steam or open real network connections.
- Tests/Run-StudioChecks.ps1 -Group Progression: campaign save/progression checks and source-parity preflight fixtures. No actual Unity build is started.
- Add -List to inspect a group's script paths and included dependency chains without executing them. All covers BossHard, Steam (including Network/Compile and campaign-save/flow checks), and the Part 1 build-preflight fixtures. It avoids separately repeating groups already included by Steam; standalone group commands retain their dependencies. For a narrow change, prefer its individual existing script instead of automatically running every group.

The runner fails if any selected child fails and prints each result. Missing compilers, references or packages are NOT RUN prerequisites, never a pass. Preserve failures and report their relation to the change; do not quarantine, skip or rewrite a failing assertion just to get a green result.

For boss transitions, examine one-shot entry/exit, transformation healing, AI attack pause/resume, counter groggy duration, interrupted cleanup and replica behavior where relevant. Preserve pattern order and existing tuning unless requested. Identify missing high-value scenario tests; do not add tests that only mirror implementation or verify reversible documentation/UI edits.

Report exact commands/groups and results; do not compute coverage percentages without an explicit scenario denominator. Passing rule tests does not demonstrate rendered telegraph cleanup, camera placement, frame-time performance or live sync. Give the user a short play sequence in the original Unity project for those gaps and describe the expected result. Test failures and missing prerequisites remain visible in the report.

Adapted from Claude Code Game Studios regression-suite; source record: docs/tooling/GAME_STUDIOS_ADOPTION.md at the project root.
