# Repository working agreement

## Mandatory source of truth

Before changing game simulation, prediction, combat, enemy AI, map navigation, rhythm execution,
ghost visualization, snapshots, or hashes, read:

1. `docs/shared/PREDICTION_CONTRACT.md`
2. `docs/shared/ENEMY_SYSTEM.md` when enemy AI, projectiles, Drop, Boost, or flying movement is involved
3. `docs/shared/OPTIMIZATION.md` when changing prediction performance or enemy LOD

`docs/shared/PREDICTION_CONTRACT.md` is the highest-priority implementation contract.
Do not silently implement a conflicting behavior. If a requested change conflicts with it:

1. identify the conflict;
2. update the contract version and affected rules;
3. update Snapshot and WorldHash when Sim state changes;
4. add or update regression tests;
5. then change code.

Historical documents and superseded ADR sections are not implementation authority.

## Incomplete game code

Prediction work must not wait for unfinished game features. Develop against the interfaces and
FakeSim rules in `PREDICTION_CONTRACT.md`. Keep game-specific behavior behind adapters.
Do not invent final enemy AI, map links, aerial lunge rules, or combat timings that the contract
marks as draft or TBD.

## Required verification

For relevant changes:

- run Unity compilation;
- run deterministic Snapshot/WorldHash tests;
- verify no prediction candidate loop calls runtime NavMesh directly;
- verify Perfect/Good/Miss boundary behavior;
- update contract checklists when a contracted item is completed.

