# Auto Terra Factions

[中文](README.zh-Hans.md)

A tModLoader mod in which up to 6 AI factions play Terraria by vanilla rules while you spectate or play along.

## Features

- AI-controlled NPCs gather resources, craft items, build bases and upgrade their equipment. Each has its own weapon preference.
- Their goal is to defeat every boss up to and including the Moon Lord.
- Settings and NPC data are saved with the world.
- Single-player only.

> All testing during development used 2 factions of 6 characters each. Choosing too many characters may cause lag, or make progression permanently impossible once the world's resources run out.

## Enabling the mod

Open a world, go to **Settings → Mod Configuration → Auto Terra Factions**, and set **Enable bots** to On.

## Installing from source

Requires Terraria and tModLoader from Steam.

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/).
2. Clone this repository and link it into tModLoader's ModSources folder (macOS example):
   ```bash
   ln -s "$(pwd)" "$HOME/Library/Application Support/Terraria/tModLoader/ModSources/AutoTerraMod"
   ```
3. Build in game with **Workshop → Develop Mods → Build + Reload**, or close the game and build from the command line (this also packs the `.tmod`):
   ```bash
   tools/build.sh
   ```
4. Enable Auto Terra Factions in the Mods menu.

## Controls

| Default key | Action |
| --- | --- |
| `[` / `]` | Follow the previous / next bot |
| `/` | Toggle the free camera |
| `Esc` | While spectating, open or close the followed bot's inventory (read-only) |
| `P` | Switch between spectating and playing in person |

Keys can be changed under Settings → Controls → Mod Controls. The spectator panel shows the current bindings, each faction's status and the world's boss progress.

## Code layout

```
Common/
  Bosses/       boss catalogue, readiness, summoning rituals, credit
  Bots/         bot body actions, movement, vitals, decision making, watchdog
  Bots/Tasks/   utility-AI tasks: fighting, fleeing, gathering, crafting, building, storage...
  Building/     base layout, blueprints, housing checks, storage
  Factions/     factions and their shared knowledge
  Navigation/   2-tile-wide A* pathfinding and path execution
  Net/          syncing bots to clients
  Planning/     progression goals, recursive recipe planning, resource sources, gear evaluation
  Spectating/   spectator camera and UI mirroring
  Systems/      world lifecycle, vanilla hooks, performance stats, status reports, headless test mode
  UI/           spectator panel and read-only inventory
  World/        world scans, landmarks and tile queries
Content/NPCs/   the NPC body of each bot
tools/headless/ headless server test scripts
```

## Development and testing

Type `/autoterra logging on` in chat to write diagnostics to `client.log` / `server.log`, and `/autoterra logging off` to stop. The log then contains:

- `perf over 10s`: time spent per subsystem
- `status (...)`: every 30 seconds, each bot's position, task, plan and how long it has been stalled
- `watchdog: trapped`: a stuck bot's movement state and an ASCII map of the tiles around it

Headless tests run without a game client:

```bash
tools/headless/run.sh 300           # run the AutoTest world for 300 seconds
tools/headless/run.sh 300 --fresh   # delete and regenerate the AutoTest world
AUTOTERRA_SCENARIO=boss tools/headless/run.sh 300
```

The script sets `AUTOTERRA_HEADLESS=1`, which keeps the dedicated server updating the world with no player connected. Diagnostic logging is always on in headless mode. The log is `tModLoader-Logs/server.log` in the tModLoader install folder.
