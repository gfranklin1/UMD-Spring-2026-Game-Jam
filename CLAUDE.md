# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Depth & Doubloons** — a cooperative LAN multiplayer diving game where players collect underwater loot to meet quota targets. Built in Unity 6 with URP and Netcode for GameObjects (NGO).

## Development Commands

There are no standalone build or test CLI commands — all development happens through the Unity Editor. The project solution file is `UMD-Spring-2026-Game-Jam.slnx`.

To run Unity tests via MCP: use `mcp__ai-game-developer__tests-run` (EditMode is faster for iteration).

Unity command-line build (if needed):
```
Unity.exe -batchmode -projectPath . -buildWindows64Player Build/game.exe -quit
```

The `--host` command-line arg triggers host mode at startup (used for dedicated server builds), read by `NetworkSetup.Start()`.

## Architecture

### Scene Flow

`MainMenu.unity` → user picks Host or Join → intent stored in static `NetworkLauncher` → `SampleScene.unity` loads → `NetworkSetup.Start()` reads intent and calls `StartHost()` / `StartClient()`.

- **MainMenu.unity**: No NetworkManager. Uses `MainMenuController.cs`.
- **SampleScene.unity**: Contains `NetworkManager` (with `NetworkSetup` + `NetworkHUD`), all gameplay objects.

### Networking

- **Framework**: NGO v2.11.0 over Unity Transport on port 7777 (LAN only).
- **Authority model**: Server-authoritative for all game state. Clients predict movement locally.
- **`NetworkLauncher`**: Pure static class — carries host/client intent across the scene load.
- **`NetworkSetup`**: MonoBehaviour on the NetworkManager prefab. Reads `NetworkLauncher` and initializes transport.

### Core Game Systems

| Class | Role |
|---|---|
| `QuotaManager` | Server-authoritative game loop: day/time, quota cycles, game-over. Singleton. |
| `GoldTracker` | Shared gold pool. Server-only writes via `NetworkVariable<int>`. Singleton. |
| `GameManager` | App-level singleton (DontDestroyOnLoad). Handles disconnect → return to menu. |
| `DayNightCycle` | Client-side visuals only; reads `QuotaManager.TimeOfDay01` each frame. |

Quota targets: `[100, 250, 500, 800]` gold, then `+400` per cycle. 3 real minutes = 1 in-game day.

### Player System

`PlayerController` has five states: `OnDeck`, `AtStation`, `WearingSuit`, `Underwater`, `Dead`.

Key player subsystems:
- **Oxygen**: breath-hold (30 s) or suit buffer (60 s)
- **DiveCableSystem**: tether while underwater
- **SpectatorCamera**: activated on death — orbits alive players; mouse to orbit, A/D or LMB/RMB to cycle targets
- **SpectatorHUD**: finds `SpectatorCanvas` child by name in `Awake()`; only toggles that panel, never the root

Dead players are hidden from alive players via `NetworkVariable<bool> NetworkIsDead` with an `OnValueChanged` observer.

### Interaction System

All interactables implement `IInteractable`:
```csharp
string GetPromptText(PlayerController viewer);
float  HoldDurationFor(PlayerController viewer); // 0 = instant press
void   OnInteractStart / OnInteractHold / OnInteractCancel / Release(PlayerController player);
```

`HoldDurationFor` is viewer-aware — e.g. `DivingSuitRack` returns `0f` for non-wearers viewing an occupied suit (suppresses the hold ring in the HUD).

Implementations: `DivingSuitRack`, `AirPumpStation`, `StorageChest`, `InteractableStation`.

### UI / HUD Notes

- `InteractionPromptHUD` reads `HoldDurationFor(player) > 0` to decide whether to show the radial fill ring.
- `PlayerHUD.HideForDeath()` / `ShowForRespawn()` toggle the HUD canvas and disable the component.
- `VersionTextSetter` sets the version `Text` to `Application.version` at runtime (no hardcoded string).

### Reset Flow

`QuotaManager.ResetGame()` → calls `ResetSuitRacks()` → each `DivingSuitRack.ServerForceReset()` unequips the wearer via `ForceUnequipSuitClientRpc()` and resets rack state.

### Animator Patterns

Chest animations use **Trigger** parameters (not Bool) with `writeDefaultValues = false` on idle states, so they hold their last pose without a motion clip. Interrupt transitions allow Closing→Opening mid-animation.

`Image` components used as radial fill rings must have `m_Type = 4` (Filled) — type 3 (Tiled) does not support `fillAmount`.
