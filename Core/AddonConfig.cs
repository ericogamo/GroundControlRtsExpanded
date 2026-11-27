using BepInEx.Configuration;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal static class AddonConfig
{
    internal static ConfigEntry<bool> ModEnabled { get; private set; } = null!;
    internal static ConfigEntry<bool> VerboseLogging { get; private set; } = null!;

    // 02 AI Economy
    internal static ConfigEntry<string> EconomyPreset { get; private set; } = null!;
    internal static ConfigEntry<float> AiNetIncomeTarget { get; private set; } = null!;
    internal static ConfigEntry<int> AiMaxGoldMines { get; private set; } = null!;
    internal static ConfigEntry<float> AiHeadStartMultiplier { get; private set; } = null!;
    internal static ConfigEntry<float> AiChampionsFlatStipend { get; private set; } = null!;
    internal static ConfigEntry<int> AiFreeGoldMines { get; private set; } = null!;
    internal static ConfigEntry<float> AiMineIncomeMultiplier { get; private set; } = null!;
    internal static ConfigEntry<int> AiMaxFactories { get; private set; } = null!;
    internal static ConfigEntry<float> AiIncomeCapMultiplier { get; private set; } = null!;
    internal static ConfigEntry<KeyboardShortcut> EconomyTrackerKey { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableCapturedBaseIncome { get; private set; } = null!;
    internal static ConfigEntry<float> CapturedBaseIncomePerMinute { get; private set; } = null!;
    internal static ConfigEntry<float> BaseCaptureBounty { get; private set; } = null!;

    // 03 Tactical AI
    internal static ConfigEntry<bool> EnableTacticalQualityAi { get; private set; } = null!;
    internal static ConfigEntry<float> QualityDearLean { get; private set; } = null!;
    internal static ConfigEntry<int> MaxGroundBuysPerReview { get; private set; } = null!;
    internal static ConfigEntry<bool> SeadBeforeStrike { get; private set; } = null!;
    internal static ConfigEntry<int> BlockCheapSpamAtDepotLevel { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableLossAdaptation { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableAiSupplyLogistics { get; private set; } = null!;
    internal static ConfigEntry<float> AiSamRearmThreshold { get; private set; } = null!;
    internal static ConfigEntry<float> AiDefenderRearmThreshold { get; private set; } = null!;
    internal static ConfigEntry<float> AiTruckPreferredRangeMeters { get; private set; } = null!;
    internal static ConfigEntry<float> AiTruckMaxRangeMeters { get; private set; } = null!;
    internal static ConfigEntry<bool> AiHeliSupplyEnabled { get; private set; } = null!;
    internal static ConfigEntry<float> AiHeliSupplyCooldownSeconds { get; private set; } = null!;
    internal static ConfigEntry<int> AiMaxConcurrentSupplyHelis { get; private set; } = null!;
    internal static ConfigEntry<float> AiHeliSupplyMinReserveFunds { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableAiAwacsPickets { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableAiCombinedArmsSync { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableAiUnitPreservation { get; private set; } = null!;
    internal static ConfigEntry<float> AiRetreatHealthFraction { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableAiArtilleryShootAndScoot { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableAiRearQrf { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableAiRadarEmcon { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableAiAmbushEmcon { get; private set; } = null!;
    internal static ConfigEntry<float> AiRadarBlinkSeconds { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableAiSmartBaseLayout { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableAiCoastalOutpost { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableHangarSpawnSafety { get; private set; } = null!;
    internal static ConfigEntry<float> HangarAiPlayerSafetySeconds { get; private set; } = null!;
    internal static ConfigEntry<float> HangarSafetyClearanceMeters { get; private set; } = null!;

    // 04 Performance
    internal static ConfigEntry<bool> EnableAntiLag { get; private set; } = null!;
    internal static ConfigEntry<bool> IdleTurretThrottle { get; private set; } = null!;
    internal static ConfigEntry<bool> SuppressLogSpam { get; private set; } = null!;
    internal static ConfigEntry<KeyboardShortcut> ShowPerfOverlayKey { get; private set; } = null!;
    internal static ConfigEntry<bool> AdaptiveDetectorThrottle { get; private set; } = null!;
    internal static ConfigEntry<int> MaxDetectorLoSPerFrame { get; private set; } = null!;
    internal static ConfigEntry<float> WreckCleanupSeconds { get; private set; } = null!;

    // 05 RTS City & Caps
    internal static ConfigEntry<bool> EnableDynamicCaps { get; private set; } = null!;
    internal static ConfigEntry<int> StartGroundCap { get; private set; } = null!;
    internal static ConfigEntry<int> StartAirCap { get; private set; } = null!;
    internal static ConfigEntry<int> StartNavalCap { get; private set; } = null!;
    internal static ConfigEntry<bool> RequirePhysicalConstruction { get; private set; } = null!;
    internal static ConfigEntry<KeyboardShortcut> CityMenuKey { get; private set; } = null!;
    internal static ConfigEntry<KeyboardShortcut> RoadBuildKey { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableAiCityBuilding { get; private set; } = null!;
    internal static ConfigEntry<float> RoadCostPer100m { get; private set; } = null!;
    internal static ConfigEntry<float> BridgeCostPer100m { get; private set; } = null!;
    internal static ConfigEntry<float> MaxRoadGradePercent { get; private set; } = null!;

    // 06 SAM & ABM
    internal static ConfigEntry<KeyboardShortcut> SamSiteMenuKey { get; private set; } = null!;
    internal static ConfigEntry<float> AbmMissileCost { get; private set; } = null!;
    internal static ConfigEntry<float> AbmProductionSeconds { get; private set; } = null!;
    internal static ConfigEntry<int> AbmMaxStockPerSite { get; private set; } = null!;
    internal static ConfigEntry<bool> AllowAiAbmSite { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableMdsBridge { get; private set; } = null!;
    internal static ConfigEntry<bool> EnforceContractorFactionLock { get; private set; } = null!;
    internal static ConfigEntry<float> TarantulaMaxSlingMassKg { get; private set; } = null!;
    internal static ConfigEntry<bool> EnableAiMdsDeployment { get; private set; } = null!;
    internal static ConfigEntry<int> AiMaxMdsSites { get; private set; } = null!;

    // 07 Models & Maps
    internal static ConfigEntry<KeyboardShortcut> ReloadModelsKey { get; private set; } = null!;
    internal static ConfigEntry<bool> UseCustomModels { get; private set; } = null!;
    internal static ConfigEntry<bool> ApplyMilitaryTint { get; private set; } = null!;
    internal static ConfigEntry<string> PreferredStartBase { get; private set; } = null!;
    internal static ConfigEntry<bool> DuelOppositeAirbaseStart { get; private set; } = null!;

    // 08 Singleplayer Cheats
    internal static ConfigEntry<bool> EnableSingleplayerCheats { get; private set; } = null!;
    internal static ConfigEntry<KeyboardShortcut> CheatMenuKey { get; private set; } = null!;
    internal static ConfigEntry<string> HostTestCheatSteamId { get; private set; } = null!;

    internal static void Initialize(ConfigFile config)
    {
        // 01 General
        ModEnabled = config.Bind(
            "01 General",
            "ModEnabled",
            true,
            "Enable Ground Control RTS Expanded.");

        VerboseLogging = config.Bind(
            "01 General",
            "VerboseLogging",
            false,
            "Write detailed debug diagnostics to BepInEx/LogOutput.log.");

        // 02 AI Economy
        EconomyPreset = config.Bind(
            "02 AI Economy",
            "EconomyPreset",
            "Fair",
            new ConfigDescription(
                "AI economy preset: Fair (no cheats), BalancedPlus (slight AI edge), Custom (uses sliders below), Vanilla (GC defaults).",
                new AcceptableValueList<string>("Fair", "BalancedPlus", "Custom", "Vanilla")));

        AiNetIncomeTarget = config.Bind(
            "02 AI Economy",
            "AiNetIncomeTarget",
            500f,
            new ConfigDescription(
                "Guaranteed minimum net income/min after upkeep that the AI can always build toward (GC default was 2000).",
                new AcceptableValueRange<float>(50f, 2000f)));

        AiMaxGoldMines = config.Bind(
            "02 AI Economy",
            "AiMaxGoldMines",
            8,
            new ConfigDescription(
                "Hard cap on total Gold Mines owned by an AI faction.",
                new AcceptableValueRange<int>(0, 20)));

        AiHeadStartMultiplier = config.Bind(
            "02 AI Economy",
            "AiHeadStartMultiplier",
            1.0f,
            new ConfigDescription(
                "Opening balance multiplier for AI commanders (1.0 = equal to player).",
                new AcceptableValueRange<float>(0.5f, 3.0f)));

        AiChampionsFlatStipend = config.Bind(
            "02 AI Economy",
            "AiChampionsFlatStipend",
            0f,
            new ConfigDescription(
                "Extra flat credits/min stipend paid to AI in Champions mode (0 = fair).",
                new AcceptableValueRange<float>(0f, 500f)));

        AiFreeGoldMines = config.Bind(
            "02 AI Economy",
            "AiFreeGoldMines",
            0,
            new ConfigDescription(
                "Free (0-cost) gold mines granted to the AI at match start.",
                new AcceptableValueRange<int>(0, 4)));

        AiMineIncomeMultiplier = config.Bind(
            "02 AI Economy",
            "AiMineIncomeMultiplier",
            1.0f,
            new ConfigDescription(
                "Income multiplier applied to AI income buildings (1.0 = equal to player).",
                new AcceptableValueRange<float>(0.5f, 2.0f)));

        AiMaxFactories = config.Bind(
            "02 AI Economy",
            "AiMaxFactories",
            16,
            new ConfigDescription(
                "Base maximum number of active unit factories an AI commander can maintain (scales up dynamically with player growth in Fair/BalancedPlus).",
                new AcceptableValueRange<int>(1, 32)));

        AiIncomeCapMultiplier = config.Bind(
            "02 AI Economy",
            "AiIncomeCapMultiplier",
            1.05f,
            new ConfigDescription(
                "Dynamic AI income target/cap multiplier vs player gross income (1.05 = player + 5%, 0 = disabled).",
                new AcceptableValueRange<float>(0f, 3.0f)));

        EconomyTrackerKey = config.Bind(
            "02 AI Economy",
            "EconomyTrackerKey",
            new KeyboardShortcut(KeyCode.E, KeyCode.LeftShift),
            "Open or close the Live Economy & Spending Budget Tracker.");

        EnableCapturedBaseIncome = config.Bind(
            "02 AI Economy",
            "EnableCapturedBaseIncome",
            true,
            "Award credits for captured airbases (one-time capture bounty plus ongoing passive income/min per captured base beyond the starting home base).");

        CapturedBaseIncomePerMinute = config.Bind(
            "02 AI Economy",
            "CapturedBaseIncomePerMinute",
            50f,
            new ConfigDescription(
                "Ongoing passive credits/min granted for each additional capturable airbase held beyond a faction's starting home base.",
                new AcceptableValueRange<float>(0f, 300f)));

        BaseCaptureBounty = config.Bind(
            "02 AI Economy",
            "BaseCaptureBounty",
            150f,
            new ConfigDescription(
                "One-time credits bounty awarded when a faction captures a neutral or enemy airbase during active play (300s anti-flip cooldown per base/faction).",
                new AcceptableValueRange<float>(0f, 1000f)));

        // 03 Tactical AI
        EnableTacticalQualityAi = config.Bind(
            "03 Tactical AI",
            "EnableTacticalQualityAi",
            true,
            "Enable Quality-over-Quantity AI doctrine, counter-picking, and coordinated waves.");

        QualityDearLean = config.Bind(
            "03 Tactical AI",
            "QualityDearLean",
            0.9f,
            new ConfigDescription(
                "How strongly the AI prefers higher-tier units over cheap spam (0 = cheap, 1 = elite).",
                new AcceptableValueRange<float>(0f, 1f)));

        MaxGroundBuysPerReview = config.Bind(
            "03 Tactical AI",
            "MaxGroundBuysPerReview",
            3,
            new ConfigDescription(
                "Cap on ground vehicles purchased per 30s AI review.",
                new AcceptableValueRange<int>(1, 10)));

        SeadBeforeStrike = config.Bind(
            "03 Tactical AI",
            "SeadBeforeStrike",
            true,
            "Prioritize SEAD/EW suppression before sending bombers into active SAM/ABM coverage.");

        BlockCheapSpamAtDepotLevel = config.Bind(
            "03 Tactical AI",
            "BlockCheapSpamAtDepotLevel",
            2,
            new ConfigDescription(
                "Block lowest-tier cheap combat vehicle spam once AI reaches this depot level (0 = disabled).",
                new AcceptableValueRange<int>(0, 3)));

        EnableLossAdaptation = config.Bind(
            "03 Tactical AI",
            "EnableLossAdaptation",
            true,
            "Track stalled AI pushes with high losses and rotate flank or target base.");

        EnableAiSupplyLogistics = config.Bind(
            "03 Tactical AI",
            "EnableAiSupplyLogistics",
            true,
            "Enable proactive AI rearming via munitions trucks and supply helicopters.");

        AiSamRearmThreshold = config.Bind(
            "03 Tactical AI",
            "AiSamRearmThreshold",
            0.60f,
            new ConfigDescription(
                "Ammo fraction below which AI SAM launchers request truck or helicopter resupply.",
                new AcceptableValueRange<float>(0.15f, 0.90f)));

        AiDefenderRearmThreshold = config.Bind(
            "03 Tactical AI",
            "AiDefenderRearmThreshold",
            0.45f,
            new ConfigDescription(
                "Ammo fraction below which non-SAM AI defenders request truck resupply.",
                new AcceptableValueRange<float>(0.15f, 0.85f)));

        AiTruckPreferredRangeMeters = config.Bind(
            "03 Tactical AI",
            "AiTruckPreferredRangeMeters",
            6000f,
            new ConfigDescription(
                "Maximum road distance (m) within which a loaded supply truck is preferred over a supply helicopter.",
                new AcceptableValueRange<float>(1500f, 20000f)));

        AiTruckMaxRangeMeters = config.Bind(
            "03 Tactical AI",
            "AiTruckMaxRangeMeters",
            12000f,
            new ConfigDescription(
                "Maximum road distance (m) for cross-base AI supply truck fallback dispatches.",
                new AcceptableValueRange<float>(3000f, 30000f)));

        AiHeliSupplyEnabled = config.Bind(
            "03 Tactical AI",
            "AiHeliSupplyEnabled",
            true,
            "Allow AI factions to launch rotary supply helicopters with ammunition containers when stations lack truck access.");

        AiHeliSupplyCooldownSeconds = config.Bind(
            "03 Tactical AI",
            "AiHeliSupplyCooldownSeconds",
            180f,
            new ConfigDescription(
                "Minimum cooldown (seconds) between supply helicopter launches for the same station.",
                new AcceptableValueRange<float>(45f, 600f)));

        AiMaxConcurrentSupplyHelis = config.Bind(
            "03 Tactical AI",
            "AiMaxConcurrentSupplyHelis",
            2,
            new ConfigDescription(
                "Maximum simultaneous active/pending AI supply helicopter runs per faction.",
                new AcceptableValueRange<int>(1, 6)));

        AiHeliSupplyMinReserveFunds = config.Bind(
            "03 Tactical AI",
            "AiHeliSupplyMinReserveFunds",
            80f,
            new ConfigDescription(
                "Minimum faction credits reserve kept after paying for an AI supply helicopter mission.",
                new AcceptableValueRange<float>(0f, 500f)));

        EnableAiAwacsPickets = config.Bind(
            "03 Tactical AI",
            "EnableAiAwacsPickets",
            true,
            "Allow AI commanders to field AWACS/EW radar pickets scaled by tech tier and air cap.");

        EnableAiCombinedArmsSync = config.Bind(
            "03 Tactical AI",
            "EnableAiCombinedArmsSync",
            true,
            "Synchronize AI ground drives, SEAD, air strikes, and artillery onto coordinated operational objectives.");

        EnableAiUnitPreservation = config.Bind(
            "03 Tactical AI",
            "EnableAiUnitPreservation",
            true,
            "Pull heavily damaged high-value AI combat vehicles back toward Logistics Camps, depots, or engineers for repair.");

        AiRetreatHealthFraction = config.Bind(
            "03 Tactical AI",
            "AiRetreatHealthFraction",
            0.45f,
            new ConfigDescription(
                "Health fraction threshold below which high-value AI vehicles tactically retreat for repairs.",
                new AcceptableValueRange<float>(0.20f, 0.65f)));

        EnableAiArtilleryShootAndScoot = config.Bind(
            "03 Tactical AI",
            "EnableAiArtilleryShootAndScoot",
            true,
            "Relocate AI artillery and ballistic launchers after firing salvos or under counter-battery threat.");

        EnableAiRearQrf = config.Bind(
            "03 Tactical AI",
            "EnableAiRearQrf",
            true,
            "Dispatch nearby AI ground vehicles and gunship helicopters to intercept rear-area economy raids.");

        EnableAiRadarEmcon = config.Bind(
            "03 Tactical AI",
            "EnableAiRadarEmcon",
            true,
            "Enable ARAD incoming-missile blink and neighbor radar handoff on vanilla AI radars.");

        EnableAiAmbushEmcon = config.Bind(
            "03 Tactical AI",
            "EnableAiAmbushEmcon",
            true,
            "Keep front vanilla AI SAM radars silent when EW/AWACS coverage exists until hostile air enters engagement range.");

        AiRadarBlinkSeconds = config.Bind(
            "03 Tactical AI",
            "AiRadarBlinkSeconds",
            8f,
            new ConfigDescription(
                "Duration (seconds) an AI radar stays off during an ARAD evasion blink.",
                new AcceptableValueRange<float>(4f, 18f)));

        EnableAiSmartBaseLayout = config.Bind(
            "03 Tactical AI",
            "EnableAiSmartBaseLayout",
            true,
            "Prefer safest rear bases, threat-facing defence orientation, and dispersed placement for AI city/economy/MDS buildings.");

        EnableAiCoastalOutpost = config.Bind(
            "03 Tactical AI",
            "EnableAiCoastalOutpost",
            true,
            "Plan and build a coastal forward outpost (Control Tower + Naval Dock) when an AI faction holds no bases with shoreline access.");

        EnableHangarSpawnSafety = config.Bind(
            "03 Tactical AI",
            "EnableHangarSpawnSafety",
            true,
            "Lock hangar bays against player spawns while an AI aircraft is spawning or rolling out, and block spawns into physically occupied or wrecked bays.");

        HangarAiPlayerSafetySeconds = config.Bind(
            "03 Tactical AI",
            "HangarAiPlayerSafetySeconds",
            12f,
            new ConfigDescription(
                "Minimum seconds a hangar bay stays locked for player spawns after an AI aircraft spawns in it (automatically extended until the AI aircraft clears the bay).",
                new AcceptableValueRange<float>(0f, 60f)));

        HangarSafetyClearanceMeters = config.Bind(
            "03 Tactical AI",
            "HangarSafetyClearanceMeters",
            48f,
            new ConfigDescription(
                "Minimum horizontal distance (m) a hangar's own spawned AI aircraft must clear before a player may spawn in that bay.",
                new AcceptableValueRange<float>(20f, 120f)));

        // 04 Performance
        EnableAntiLag = config.Bind(
            "04 Performance",
            "EnableAntiLag",
            true,
            "Enable large-battle (600+ units) performance optimizations.");

        IdleTurretThrottle = config.Bind(
            "04 Performance",
            "IdleTurretThrottle",
            true,
            "Scale idle turret target search intervals and pre-filter range before Physics.Linecast.");

        SuppressLogSpam = config.Bind(
            "04 Performance",
            "SuppressLogSpam",
            true,
            "Rate-limit repetitive Ground Control Info log messages to prevent disk stalls.");

        ShowPerfOverlayKey = config.Bind(
            "04 Performance",
            "ShowPerfOverlayKey",
            new KeyboardShortcut(KeyCode.F9),
            "Toggle compact performance & unit counter overlay.");

        AdaptiveDetectorThrottle = config.Bind(
            "04 Performance",
            "AdaptiveDetectorThrottle",
            true,
            "Slow down visual TargetDetector scans on non-SAM ground vehicles and ground tracking RPC updates in large battles (200+ units).");

        MaxDetectorLoSPerFrame = config.Bind(
            "04 Performance",
            "MaxDetectorLoSPerFrame",
            256,
            new ConfigDescription(
                "Maximum DetectorManager LoS/radar raycasts scheduled per frame in big battles (overflow defers to next frame; 0 = unlimited).",
                new AcceptableValueRange<int>(0, 2048)));

        WreckCleanupSeconds = config.Bind(
            "04 Performance",
            "WreckCleanupSeconds",
            120f,
            new ConfigDescription(
                "Despawn dead non-scene ground vehicle wrecks on the server after this many seconds (0 = disabled).",
                new AcceptableValueRange<float>(0f, 600f)));

        // 05 RTS City & Caps
        EnableDynamicCaps = config.Bind(
            "05 RTS City & Caps",
            "EnableDynamicCaps",
            true,
            "Start with low unit caps and expand them by building RTS city/base infrastructure.");

        StartGroundCap = config.Bind(
            "05 RTS City & Caps",
            "StartGroundCap",
            15,
            new ConfigDescription(
                "Initial ground unit cap before building Barracks / Logistics Camp.",
                new AcceptableValueRange<int>(4, 100)));

        StartAirCap = config.Bind(
            "05 RTS City & Caps",
            "StartAirCap",
            2,
            new ConfigDescription(
                "Initial active aircraft cap before building Pilot Academy.",
                new AcceptableValueRange<int>(0, 20)));

        StartNavalCap = config.Bind(
            "05 RTS City & Caps",
            "StartNavalCap",
            2,
            new ConfigDescription(
                "Initial naval vessel cap before building Naval Command.",
                new AcceptableValueRange<int>(0, 20)));

        RequirePhysicalConstruction = config.Bind(
            "05 RTS City & Caps",
            "RequirePhysicalConstruction",
            true,
            "Enforce physical construction by M12 Jackknife engineers or heavy-lift helicopter kits (no instant direct build).");

        CityMenuKey = config.Bind(
            "05 RTS City & Caps",
            "CityMenuKey",
            new KeyboardShortcut(KeyCode.C, KeyCode.LeftShift),
            "Open or close the RTS City, Dynamic Caps & Tech-Tree panel.");

        RoadBuildKey = config.Bind(
            "05 RTS City & Caps",
            "RoadBuildKey",
            new KeyboardShortcut(KeyCode.R, KeyCode.LeftShift),
            "Toggle Pioneer Road & Bridge waypoint planner in RTS mode.");

        EnableAiCityBuilding = config.Bind(
            "05 RTS City & Caps",
            "EnableAiCityBuilding",
            true,
            "Allow AI commanders to construct and upgrade Command HQ, Barracks, Pilot Academy, Logistics Camp, and Naval Command.");

        RoadCostPer100m = config.Bind(
            "05 RTS City & Caps",
            "RoadCostPer100m",
            25f,
            new ConfigDescription(
                "Credits cost per 100 m of pioneer road.",
                new AcceptableValueRange<float>(5f, 250f)));

        BridgeCostPer100m = config.Bind(
            "05 RTS City & Caps",
            "BridgeCostPer100m",
            75f,
            new ConfigDescription(
                "Credits cost per 100 m of pioneer bridge span over water or steep terrain.",
                new AcceptableValueRange<float>(10f, 500f)));

        MaxRoadGradePercent = config.Bind(
            "05 RTS City & Caps",
            "MaxRoadGradePercent",
            30f,
            new ConfigDescription(
                "Maximum allowed terrain slope (%) for land road segments before requiring a bridge or reroute.",
                new AcceptableValueRange<float>(10f, 60f)));

        // 06 SAM & ABM
        SamSiteMenuKey = config.Bind(
            "06 SAM & ABM",
            "SamSiteMenuKey",
            new KeyboardShortcut(KeyCode.B, KeyCode.LeftShift),
            "Open or close the custom SAM / ABM Site Builder panel.");

        AbmMissileCost = config.Bind(
            "06 SAM & ABM",
            "AbmMissileCost",
            200f,
            new ConfigDescription(
                "Credits cost per produced ABM interceptor missile.",
                new AcceptableValueRange<float>(50f, 1000f)));

        AbmProductionSeconds = config.Bind(
            "06 SAM & ABM",
            "AbmProductionSeconds",
            90f,
            new ConfigDescription(
                "Base seconds required for a level-1 ABM Interceptor Plant to produce one interceptor.",
                new AcceptableValueRange<float>(15f, 300f)));

        AbmMaxStockPerSite = config.Bind(
            "06 SAM & ABM",
            "AbmMaxStockPerSite",
            8,
            new ConfigDescription(
                "Maximum interceptor missiles stored in an ABM Defense Site.",
                new AcceptableValueRange<int>(2, 24)));

        AllowAiAbmSite = config.Bind(
            "06 SAM & ABM",
            "AllowAiAbmSite",
            true,
            "Allow the AI commander to build up to one Tier-3 ABM Defense Site in the late game.");

        EnableMdsBridge = config.Bind(
            "06 SAM & ABM",
            "EnableMdsBridge",
            true,
            "Attach RTS Economy, Dynamic Caps, Logistics (M12 Jackknife & VL-49 Tarantula), ABM Stockpile, and AI SEAD Threat bridges when MDSFramework is installed.");

        EnforceContractorFactionLock = config.Bind(
            "06 SAM & ABM",
            "EnforceContractorFactionLock",
            true,
            "Restrict faction-affiliated contractor packs to their home faction (e.g. Vanguard Defence strictly Boscali / West only; Eastern contractors Primeva only).");

        TarantulaMaxSlingMassKg = config.Bind(
            "06 SAM & ABM",
            "TarantulaMaxSlingMassKg",
            8500f,
            new ConfigDescription(
                "Maximum sling-load kit weight (kg) a VL-49 Tarantula can transport.",
                new AcceptableValueRange<float>(2000f, 20000f)));

        EnableAiMdsDeployment = config.Bind(
            "06 SAM & ABM",
            "EnableAiMdsDeployment",
            true,
            "Allow AI commanders to deploy faction-appropriate MDS modular defence systems using fair funds, tech tiers, caps, and physical logistics.");

        AiMaxMdsSites = config.Bind(
            "06 SAM & ABM",
            "AiMaxMdsSites",
            4,
            new ConfigDescription(
                "Maximum live MDS modular defence sites per AI faction.",
                new AcceptableValueRange<int>(0, 12)));

        // 07 Models & Maps
        ReloadModelsKey = config.Bind(
            "07 Models & Maps",
            "ReloadModelsKey",
            KeyboardShortcut.Empty,
            "Optional hotkey to hot-reload Models/manifest and .glb/.obj files.");

        UseCustomModels = config.Bind(
            "07 Models & Maps",
            "UseCustomModels",
            true,
            "Load custom .glb/.obj models from Models/ (uses procedural fallback meshes when disabled).");

        ApplyMilitaryTint = config.Bind(
            "07 Models & Maps",
            "ApplyMilitaryTint",
            true,
            "Blend subtle military olive/slate tint onto custom models to match Nuclear Option.");

        PreferredStartBase = config.Bind(
            "07 Models & Maps",
            "PreferredStartBase",
            string.Empty,
            "Optional airbase name override for Duel & Champions start base (leave empty for automatic full airbase on opposite sides).");

        DuelOppositeAirbaseStart = config.Bind(
            "07 Models & Maps",
            "DuelOppositeAirbaseStart",
            true,
            "In Ground Control Duel, relocate Player and AI from small highway strips to full airbases on opposite sides of the map.");

        // 08 Singleplayer Cheats
        EnableSingleplayerCheats = config.Bind(
            "08 Singleplayer Cheats",
            "EnableSingleplayerCheats",
            true,
            "Enable Singleplayer-only sandbox & testing cheats (strictly disabled in Multiplayer).");

        CheatMenuKey = config.Bind(
            "08 Singleplayer Cheats",
            "CheatMenuKey",
            new KeyboardShortcut(KeyCode.F10, KeyCode.LeftShift),
            "Open or close the Singleplayer Cheat menu.");

        HostTestCheatSteamId = config.Bind(
            "08 Singleplayer Cheats",
            "HostTestCheatSteamId",
            string.Empty,
            "Optional Steam64 ID allowed to use the Host-only test cheat menu in a self-hosted Multiplayer test lobby (leave empty for Singleplayer-only).");
    }
}
