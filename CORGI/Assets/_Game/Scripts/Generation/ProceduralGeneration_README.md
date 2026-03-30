# Procedural Generation + Adaptive Difficulty

This project now includes a runtime procedural-generation system for the `*Gen*` scene flow.

## What it does

- Generates levels inside colliders on the `Boundary` layer
- Randomly places:
  - environment objects
  - enemies
  - an end-of-level goal
- Measures player completion time per level
- Persists player attack/playstyle patterns through `PlayerPatternMemoryStore`
- Raises difficulty for later generated levels using:
  - completed level count
  - average completion time
  - dominant attack-pattern ratio
  - orbit bias memory

## Main scripts

- `Generation/SpawnCatalog.cs`
- `Generation/LevelGenerationConfig.cs`
- `Generation/RuntimeLevelGenerator.cs`
- `Generation/RuntimeLevelGeneratorBootstrap.cs`
- `Generation/LevelCompletionTrigger.cs`

## Default assets

Loaded automatically from `Resources/Generation/`:

- `DefaultSpawnCatalog.asset`
- `DefaultLevelGenerationConfig.asset`

## Asset-driven setup

### `SpawnCatalog`
Controls:
- player prefab
- enemy prefab list
- environment prefab list
- end goal prefab
- boundary layer mask
- placement blocking layers
- spawned object layers

### `LevelGenerationConfig`
Controls:
- base difficulty
- object/enemy counts
- spacing rules
- player-to-goal distance
- player-to-enemy distance
- adaptive difficulty multipliers
- whether a new level is generated automatically on goal reach

## Boundary rules

The generator uses colliders on the `Boundary` layer as legal generation space.

If none are found, it creates a runtime boundary from scene renderers as a fallback so generation can still function in the existing `Test_Forest Gen` scene.

## Persistence

Adaptive progression is saved into `PlayerPlaystyleProfile` through `PlayerPatternMemoryStore`.
Persisted metrics include:
- total generated levels
- total completed levels
- total / last / best completion time
- last / highest generated difficulty
- remembered attack patterns
- orbit bias tendencies

## Scene behavior

The bootstrap runs automatically after scene load for scenes whose name contains `Gen`.
That means `Test_Forest Gen.unity` will auto-create the generator if the default assets are available.

## Notes

- The current default catalog uses the existing `Square` prefab for player/object visuals and `Triangle` for enemies.
- If you assign richer prefabs later, the same generator will use them instead.
- Goal visuals fall back to a generated ring if no goal prefab is assigned.

