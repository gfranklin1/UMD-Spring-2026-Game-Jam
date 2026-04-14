# Depth & Doubloons

> A cooperative LAN multiplayer diving game — plunge beneath the waves, haul up treasure, and meet the quota before time runs out.

![Unity 6](https://img.shields.io/badge/Unity-6000.4.1f1-black?logo=unity)
![URP](https://img.shields.io/badge/Render%20Pipeline-URP%2017.4.0-blue)
![NGO](https://img.shields.io/badge/Netcode%20for%20GameObjects-2.11.0-green)
![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey)

---

## About

**Depth & Doubloons** is a cooperative LAN multiplayer game for 2–4 players, built for the UMD Spring 2026 Game Jam. Your crew sails a creaking ship across a procedurally generated ocean. Divers suit up and descend on tethered cables to scavenge gold from the seabed while a topside crew manages the air pump, winch, and helm. At the end of each quota cycle a merchant sails in — sell your loot, upgrade your gear, then do it all again with higher stakes.

Miss the quota and the game is over. Keep the diver alive.

---

## Gameplay Overview

### The Loop

Each quota **cycle** spans 3 in-game days (3 real minutes each). Gold targets escalate across cycles:

| Cycle | Target |
|-------|--------|
| 1 | 100g |
| 2 | 250g |
| 3 | 500g |
| 4 | 800g |
| 5+ | +400g per cycle |

When the final day ends the **Merchant** arrives. Sell collected loot, spend gold on upgrades, and hope you cleared the target. Failure ends the run.

### Roles

**Diver** — Suits up at the rack, descends the ladder, and explores the seabed to pick up loot. Manages their own oxygen and cable slack.

**Pump Operator** — Cranks the air pump (hold `Space`) to keep the diver's suit topped up. Neglect the pump and the diver starts drowning.

**Winch Operator** — Controls the diver's comms rope (`Space` = reel in, `Ctrl` = pay out). Shorter rope pulls the diver back toward the ship; longer rope gives more range.

**Helmsman** — Mans the ship's wheel to reposition the vessel over new dive sites. The ship is wind-driven — steer with `A/D` to angle into productive waters. Drop the anchor to hold position.

Roles are fluid; one player can cover multiple stations between dives.

### Player States

```
OnDeck ──► AtStation ──► WearingSuit ──► Underwater
                                              │
                                           Dead ──► Spectating
```

Death activates a free-look spectator camera that orbits surviving crew members (`A/D` or `LMB/RMB` to cycle targets). Surviving the cycle revives dead players.

---

## Features

- **Server-authoritative LAN multiplayer** via Netcode for GameObjects over Unity Transport (port 7777)
- **Dual cable tether system** — air hose (30 m) and comms rope (25 m, dynamically reeled) with Verlet physics and procedural tube mesh rendering
- **Oxygen mechanics** — 30 s breath hold without a suit; 60 s suit buffer delivered by the pump operator; drain scales with depth and movement speed
- **Procedurally generated seabed** — 40 m streaming chunks, domain-warped Simplex noise terrain, terracing and canyon features, seeded per-session for client agreement
- **9 loot item types** — Coin, Gold Bar, Gold Pile, Artifact, Chest, Bottle, Cannon Ball, Shell, Skull, Sword
- **Full ship simulation** — wind-driven speed (Perlin noise gusts), yaw steering, wave-following buoyancy at 4 hull points, catenary anchor rope with state machine
- **Upgrade shop** — Suit capacity, air tube efficiency, cable length extension, flashlight
- **Merchant system** — Animated NPC vessel appears at cycle end for selling loot and purchasing upgrades
- **Quota & game-over system** — escalating gold targets, quota-fail and crew-wipe end conditions, host-only restart
- **Spectator camera** — orbit alive players after death with `A/D` cycling
- **Day/night cycle** — client-side sky visuals driven by server time
- **Persistent settings** — sensitivity, master volume, resolution, fullscreen (PlayerPrefs)
- **Reusable Settings & Controls panels** — available from both the main menu and the in-game pause menu
- **Low-poly ocean** with Gerstner GPU waves, LOD rings, and foam simulation

---

## How to Play

### Starting a Session

| Role | Steps |
|------|-------|
| **Host** | Launch the game → click **Host** → share your LAN IP with others |
| **Client** | Launch the game → click **Join** → enter the host's IP → click **Connect** |

The host's IP is displayed in the top-right corner of the HUD in-game.

### Controls

| Action | Key |
|--------|-----|
| Move | `W A S D` |
| Look | Mouse |
| Interact / Hold interact | `E` (tap or hold) |
| Sprint | `Left Shift` |
| Pause menu | `Esc` |
| **Pump Operator** — crank pump | `Space` (hold) |
| **Winch Operator** — reel in rope | `Space` (hold) |
| **Winch Operator** — pay out rope | `Ctrl` (hold) |
| **Helmsman** — steer ship | `A / D` (while at helm) |
| **Spectator** — cycle target | `A / D` or `LMB / RMB` |
| **Diver** — remove diving boots | `F` (1 s hold) |

### Tips

- The comms rope (25 m) is shorter than the air hose (30 m) — the winch is the real movement limit.
- Have the winch operator pay out rope before the diver descends to maximize range.
- Oxygen drain increases ~5% per metre of depth and ~8% per m/s of movement — slow and steady conserves air.
- Drop the anchor before all crew leaves topside, or the ship will drift.
- The Storage Chest on deck holds shared loot — deposit items so the group can sell them together at the merchant.
- Dead players revive automatically when a quota cycle is cleared.

---

## Architecture & Development

### Tech Stack

| Layer | Technology |
|-------|-----------|
| Engine | Unity 6 (6000.4.1f1) |
| Render Pipeline | Universal Render Pipeline (URP) 17.4.0 |
| Networking | Netcode for GameObjects 2.11.0 |
| Transport | Unity Transport (LAN, port 7777) |
| Input | Unity Input System 1.19.0 |
| Physics | Built-in 3D (CharacterController + custom Verlet ropes) |
| IDE | JetBrains Rider / Visual Studio |

### Scene Flow

```
MainMenu.unity
    │  (user clicks Host or Join → intent stored in static NetworkLauncher)
    ▼
GameScene.unity
    │  NetworkSetup.Start() reads NetworkLauncher → calls StartHost() / StartClient()
    │  QuotaManager initialises day timer
    ▼
  Gameplay loop  ──►  Quota cycle ends  ──►  Merchant appears
                                                    │
                              Quota met ◄───────────┤
                           (advance cycle)          │
                                                    ▼
                                              Quota failed
                                           (GameOverUI shown)
```

`MainMenu.unity` has **no** `NetworkManager`. All NGO setup lives in `GameScene.unity`.

### Networking Model

- **Authority:** Server-authoritative for all game state; clients predict local movement only.
- **NetworkVariables** carry shared state (gold pool, player names, oxygen, rope length, game time).
- **ServerRpcs** let clients request state changes (pick up loot, add gold, steer ship).
- **ClientRpcs** push server decisions back to all clients (force-unequip suit, loot despawn).
- Offline/single-player testing is supported via an `IsNetworked` flag that falls back to local mirrors.

### Building

**Windows standalone (from Editor or CLI):**
```bash
Unity.exe -batchmode -projectPath . -buildWindows64Player Build/game.exe -quit
```

**Dedicated / headless host:**
```bash
Unity.exe -batchmode -projectPath . -buildWindows64Player Build/server.exe --host -quit
```
The `--host` flag is read by `NetworkSetup.Start()` to automatically start as host.

**Running Unity tests via MCP:**
```
mcp__ai-game-developer__tests-run  (EditMode for fast iteration)
```

**Solution file:** `UMD-Spring-2026-Game-Jam.slnx`

---

## Project Structure

```
Assets/
├── Animation/          Diver, Chest, and Merchant animation controllers & clips
├── Audio/              SFX (footsteps, swim, ping) + background music
├── Fonts/              UI typefaces
├── Icons/              9 loot item icons (PNG)
├── Materials/          Rope, loot, base colour, and ocean materials
├── Models/
│   ├── Diver/          Player character FBX
│   ├── Fish/           5 fish variants + shark
│   ├── Kenney Pirates/ 65+ CC0 nautical / pirate 3D models
│   └── Loot/           Treasure item meshes
├── Prefabs/
│   ├── Loot/           9 loot item prefabs
│   ├── Managers/       NetworkManager, QuotaManager, SeabedManager, etc.
│   ├── UI/             SettingsPanel, ControlsPanel, GameOverCanvas, etc.
│   └── [Ship, Stations, Player, Ocean, Anchor…]
├── Scenes/
│   ├── MainMenu.unity
│   ├── GameScene.unity
│   ├── IconBooth.unity     (icon generation utility)
│   └── MerchantDemo.unity  (merchant system test)
├── Scripts/
│   ├── Game/           QuotaManager, GameManager, DayNightCycle, MerchantManager
│   ├── Player/         PlayerController, DiveCableSystem, PlayerCamera, oxygen, HUDs
│   ├── Ship/           ShipMovement, ShipBuoyancy, HelmStation, WinchStation, AnchorSystem
│   ├── Interaction/    IInteractable interface + all implementations
│   ├── Networking/     NetworkSetup, NetworkLauncher, PlayerSpawnManager
│   ├── Ocean/          SeabedManager, OceanWaves, LootSpawner, SimplexNoise
│   ├── Fish/           FishSpawner, FishSchool
│   ├── Inventory/      PlayerInventory, ItemData, LootPickup, LootRegistry
│   ├── Upgrades/       Upgrade base + Suit, Cable, AirTube, Flashlight
│   ├── Rope/           VerletRope, TubeMeshGenerator, RopeEnvironment
│   ├── UI/             All HUD, menu, and panel scripts
│   ├── Settings/       SettingsManager (PlayerPrefs wrapper)
│   └── Rendering/      UnderwaterFogFeature (URP Render Feature)
├── Shaders/            Custom URP ocean and underwater shaders
└── Textures/           Diver, water normal maps, foam noise
```

---

## Credits

### Development Team
UMD Spring 2026 Game Jam team.

### Third-Party Assets

| Asset | Author | License |
|-------|--------|---------|
| [Kenney Pirate Pack](https://kenney.nl/assets/pirate-kit) | Kenney | CC0 1.0 |
| Fish Alive (DenysAlmaral) | DenysAlmaral | Unity Asset Store EULA |
| Rondo Alla Turca (Mozart) | KLICKAUD | Royalty-free |
| Footstep SFX | marb7e (Freesound) | CC BY 4.0 |
| Grains/Crackling SFX | valentinpetiteau (Freesound) | CC0 1.0 |
