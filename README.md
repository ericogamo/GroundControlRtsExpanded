# GroundControlRtsExpanded

RTS gameplay, AI commander, air-defence station, supply logistics, ABM, road & performance expansion addon for **Nuclear Option** (BepInEx 5).

## Prerequisites
- **Nuclear Option** with **BepInEx 5**
- Base mod **GroundControlRts** (`BepInEx\plugins\GroundControlRts\GroundControlRts.dll`)
- Optional soft-dependency integration with [`MDSFramework`](https://github.com/ericogamo/MDSFramework) & [`VanguardDefence`](https://github.com/ericogamo/VanguardDefence)

## Installation
### Via NOMM (Nuclear Option Mod Manager)
Install `GroundControlRtsExpanded` directly inside NOMM (auto-updates via NOMNOM).

### Manual Release ZIP
1. Download `GroundControlRtsExpanded-v0.1.1.zip` from [Releases](https://github.com/ericogamo/GroundControlRtsExpanded/releases/latest).
2. Extract directly into `Nuclear Option\BepInEx\plugins\GroundControlRtsExpanded\` (containing `GroundControlRtsExpanded.dll` and `Models/`).

### From Git Clone
```powershell
git clone https://github.com/ericogamo/GroundControlRtsExpanded.git
cd GroundControlRtsExpanded
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

## Multiplayer & Fairness
All RTS economy, AI commander, ABM, and supply logistics logic runs under host/server authority. Optional quality-of-life cheats in `SingleplayerCheatService` are strictly locked to `GameState.SinglePlayer` and disabled in multiplayer.

## Build from Source
```powershell
dotnet build GroundControlRtsExpanded.csproj -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Nuclear Option"
```

## License, Assets & AI Disclosure
- **Code License:** [MIT License](LICENSE)
- **3D Models:** Original Blender assets (`CC0 1.0`), documented in [Models/CREDITS.md](Models/CREDITS.md). No ripped game assets are redistributed.
- **AI Assistance:** Created with substantial agentic/generative AI assistance (`isAiAssisted: True`).
