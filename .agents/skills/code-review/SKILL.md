---
name: code-review
description: Review coopraid3d changes for concrete Unity lifecycle, host authority, replication, AI action and combat-state defects, especially when network or shared boss paths change.
license: MIT; see ../LICENSE.ccgs.txt
---

# Coopraid combat and network review

Follow project AGENTS.md. Review the requested change and its callers, retaining unrelated working-tree changes. A review request is read-only unless fixes were also requested. Do not launch Unity, Steam or the game, alter authored assets, merge or push. This skill does not require subagents or a separate approval ceremony.

Trace player input or AI intent through validation, host execution, damage and replication. Relevant files include RaidCombatProtocol.cs, RaidNetworkProtocol.cs, RaidCombatSession.cs, RaidReplacementAI.cs, RaidRecoverySession.cs, CharacterActor.cs and BossController.cs, plus the affected boss partials.

Inspect whichever of these invariants the change touches:

- The host validates sender/actor ownership, living state, phase, target, range, resources and action availability before mutating authoritative state. Replicas present state without independently applying damage, consuming coins or healing the boss.
- Retries, repeated requests, snapshot reapplication, disconnect/replacement and late join do not duplicate one-shot transitions, damage, resurrection, rewards or transformation health restoration.
- New action IDs, enums and snapshot fields agree between encoding, decoding and execution. Check malformed and out-of-order input handling where relevant.
- Shared transformation and damage-blocking gates cover all six bosses. Validate attack suppression without preventing intended add/core objectives or recovery after the transition.
- Unity subscriptions, presentation objects, cameras and temporary actor locks restore on normal exit and interruption. Existing project initialization and authored UI/cutscene assets remain intact.

Report defects with file/line, a concrete triggering input/state and observable consequence. Distinguish verified code behavior from hypotheses and runtime gaps. Do not demand architectural rewrites, dependency injection or file-length limits solely because the upstream template preferred them.

For network protocol changes inspect Tests/RaidCombatNetworkTests.cs and Tests/RaidNetworkTests.cs. Tests/Run-StudioChecks.ps1 -Group Network runs existing command-based checks and reads original-project references; inspect dependencies before use. Compilation and protocol tests do not verify a live multiplayer session. Use a targeted scenario test only when it meaningfully guards the behavior; no wording-matching tests or unrelated refactors.

Summarize findings first, then actual validation and unverified multiplayer scenarios. If no defect is found, state the reviewed scope and its limits rather than approving unexamined paths.

Adapted from Claude Code Game Studios code-review; source record: docs/tooling/GAME_STUDIOS_ADOPTION.md at the project root.
