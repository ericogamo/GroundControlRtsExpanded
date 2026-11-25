# GroundControlRtsExpanded

RTS gameplay, AI commander, air-defence station, supply logistics, ABM, road & performance expansion addon for **Nuclear Option** (BepInEx 5).

## Prerequisites
- **Nuclear Option** with **BepInEx 5**
- Base mod **GroundControlRts** (`BepInEx\plugins\GroundControlRts\GroundControlRts.dll`)
- Optional soft-dependency integration with [`MDSFramework`](https://github.com/ericogamo/MDSFramework) & [`VanguardDefence`](https://github.com/ericogamo/VanguardDefence)

## Quick Install / Update (Prebuilt DLL + Models included)
```powershell
git clone https://github.com/ericogamo/GroundControlRtsExpanded.git
cd GroundControlRtsExpanded
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

## Build from Source
```powershell
dotnet build GroundControlRtsExpanded.csproj -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Nuclear Option"
```
