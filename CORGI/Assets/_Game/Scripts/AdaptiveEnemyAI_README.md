# Adaptive Enemy AI

This setup adds a 2D enemy that learns the player's habits, lands hits, and remembers playstyle patterns across multiple new games without using NavMesh.

## Core scripts
- `PlayerMovementController` handles the new Input System callbacks and emits `PlayerAttackData`.
- `PlayerCombatController` resolves player attacks into 2D hit detection.
- `Health2D` and `Hurtbox2D` provide reusable damage and death handling.
- `EnemyController` learns short-term habits and blends them with persistent playstyle memory.
- `EnemyCombatController` lets enemies actually punish the player once they close distance or trigger a counter.
- `PlayerPatternMemoryStore` saves long-term player habits to `Application.persistentDataPath/player-playstyle-memory.json`.

## What it learns
- **Orbit / circling bias** around the enemy over a rolling memory window.
- **Repeated attack signatures** based on the player's attack direction and whether the attack happened during a sprint.
- **Long-term playstyle memory** that survives scene reloads, fresh new games, and app restarts.

## How to wire it
1. Add `PlayerMovementController` and `PlayerCombatController` to the player GameObject.
2. Add `Health2D` and `Hurtbox2D` to the player if they are not auto-added by required components.
3. Add `EnemyController` and `EnemyCombatController` to an enemy GameObject with a `Rigidbody2D`.
4. Add `Health2D` and `Hurtbox2D` to the enemy if they are not auto-added by required components.
5. Assign the enemy's `Target Player` field, or leave it empty and it will auto-find the first `PlayerMovementController`.
6. Set `Hittable Layers` on both combat controllers so each side can only strike valid targets.
7. Tune these inspector fields:
   - `Attacks Before Counter`
   - `Counter Commit Duration`
   - `Orbit Detection Threshold`
   - `Persistent Memory Weight`
   - `Persistent Orbit Record Threshold`

## Runtime behavior
- The player attack event now contains damage, range, radius, direction, and a unique attack id.
- The player attack controller performs a 2D circle cast and damages `Hurtbox2D` targets once per swing.
- The enemy samples recent movement, predicts routes, and moves to **cut off** repeated circling.
- When the player repeats the same attack signature often enough, the enemy stores it and **counters future repeats**.
- The enemy also records those habits into a persistent profile so it can remember them across multiple runs.

## Managing long-term memory
- Right-click the `EnemyController` component in the Inspector and use `Clear Persistent Player Memory` to wipe the learned profile.
- The memory file is independent from any future save-slot or new-game system, so a fresh run still starts against a familiar enemy.

## Notes
- This is a lightweight 2D melee setup built around circle casts and hurtboxes.
- If you add more attack buttons later, extend `PlayerAttackStyle` or the attack signature in `PlayerCombatTelemetry.cs`.
- If you later add animation events, you can move the cast timing from input time to the exact animation frame without changing the memory system.
