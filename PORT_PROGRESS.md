# SDD ledger — plan: docs/superpowers/plans/2026-11-23-dynamons-unity-port.md

- Ruling: Keep all implementation under `unity/` in this non-Git workspace; worktree/commit tooling does not apply. The ledger and direct review replace commit ranges.
- Ruling: No Unity Editor or C# SDK was found; no installation or player build. Record source/data checks separately from C# compilation and Play Mode.
- Pre-flight 1→2: normalized JSON + resources feed typed world/game data; preserve ID/range ordering and keep resources independent of the Web at runtime.
- Pre-flight 2→3: typed monster/move rows and encounter specs feed the battle engine; health/ATK/DEF/AIM overrides are encounter-scoped.
- Pre-flight 2/3→4: save DTOs and battle outcome feed progression; team identity persists across encounters; one terminal commit per encounter.
- Pre-flight 1–4→5: views consume the imported content, flow and ordered battle events without mutating rules or browser storage.
- Web baseline before port: `npm --prefix web test` → 228 passing, 0 failures.
- Task 1: import and Unity project metadata implemented; verifier RED missing snapshot → GREEN 37 species, 59 moves, 21 items, 57 nodes and 499 required runtime assets (792 copied). Editor import not run.
- Task 2: typed data, world rules and save DTOs implemented; menu rules check written but execution awaits the Editor. Web rules remain the reference for parity.
- Task 3: C# turn engine, damage, AI, capture, statuses, cooldowns, Mega and four passives implemented at source level. Damage/inventory/cooldown/shield/forced-switch checks are supplied through the Editor menu; not executed without a compatible toolchain.
- Task 4: pure progression and separate JSON save implemented; Editor checks supplied for repeat entry/claim and corrupt/failed save. Runtime compilation and save execution remain unverified here.
- Task 5: native splash, starter selection, dialogue, map walk/drag, preview, battle/replay, result, inventory, team/storage, shop, Training tiers, Arena and Starter Event screens implemented; README includes the Editor smoke checklist. Mouse/touch, editor import/compile and Play Mode remain unverified.
- The independent read-only reviewer reported no critical issue established by inspection. Follow-ups at source level: durable backup recovery and Retry Save, modal input blocking, ordered opponent swap replay, daily rollover refresh, and expanded import tests. The Node check reproduced and rejects disconnected/gated-unreachable map fixtures; it also verifies captain/dialog/arena/shop assets and entry GUIDs. Editor-only assertions are written but not executed.
- Ruling: Keep Web's interrupted-combat policy (costs already persisted are not refunded; fight is not resumed). Recovery may roll back to a previous save if the primary is corrupt. Original particle timelines and exact bitmap metrics are not fully reproduced; native cues/replay are present but their presentation still needs Editor validation.
- No Unity installation, player build or export was performed.
