using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal static class GroundControlReflection
{
    private const BindingFlags AnyDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    // GroundControlRts types (internal in GroundControlRts.dll)
    internal static readonly Type? EconomyServiceType = AccessTools.TypeByName("GroundControlRts.CommanderEconomyService");
    internal static readonly Type? EnemyCommanderType = AccessTools.TypeByName("GroundControlRts.CommanderEnemyCommanderService");
    internal static readonly Type? CaptureServiceType = AccessTools.TypeByName("GroundControlRts.CommanderCaptureService");
    internal static readonly Type? SettingsType = AccessTools.TypeByName("GroundControlRts.CommanderSettings");
    internal static readonly Type? GameAccessType = AccessTools.TypeByName("GroundControlRts.CommanderGameAccess");
    internal static readonly Type? BuildKindType = AccessTools.TypeByName("GroundControlRts.CommanderBuildKind");
    internal static readonly Type? CommanderStateType = AccessTools.TypeByName("GroundControlRts.CommanderEnemyCommanderService+CommanderState");
    internal static readonly Type? ActiveJammerType = AccessTools.TypeByName("GroundControlRts.CommanderEnemyCommanderService+ActiveJammer");
    internal static readonly Type? AirRoleType = AccessTools.TypeByName("GroundControlRts.CommanderEnemyCommanderService+AirRole");
    internal static readonly Type? EnemyDriveType = AccessTools.TypeByName("GroundControlRts.CommanderCaptureService+EnemyDrive");
    internal static readonly Type? CommanderRadarServiceType = AccessTools.TypeByName("GroundControlRts.CommanderRadarService");
    internal static readonly Type? CommanderLogType = AccessTools.TypeByName("GroundControlRts.CommanderLog");
    internal static readonly Type? CommanderPluginType = AccessTools.TypeByName("GroundControlRts.CommanderPlugin");
    internal static readonly Type? UnitCapType = AccessTools.TypeByName("GroundControlRts.CommanderUnitCap");
    internal static readonly Type? NoticeType = AccessTools.TypeByName("GroundControlRts.CommanderNotice");
    internal static readonly Type? RepairServiceType = AccessTools.TypeByName("GroundControlRts.CommanderRepairService");
    internal static readonly Type? VictoryServiceType = AccessTools.TypeByName("GroundControlRts.CommanderVictoryService");
    internal static readonly Type? AirCommandServiceType = AccessTools.TypeByName("GroundControlRts.CommanderAirCommandService");
    internal static readonly Type? AirMissionOptionType = AccessTools.TypeByName("GroundControlRts.CommanderAirCommandService+AirMissionOption");
    internal static readonly Type? AirMissionType = AccessTools.TypeByName("GroundControlRts.CommanderAirCommandService+AirMission");
    internal static readonly Type? AirCommandModeType = AccessTools.TypeByName("GroundControlRts.CommanderAirCommandService+AirCommandMode");
    internal static readonly Type? FireNowType = AccessTools.TypeByName("GroundControlRts.CommanderFireNow");
    internal static readonly Type? InputControllerType = AccessTools.TypeByName("GroundControlRts.CommanderInputController");
    internal static readonly Type? ConstructionSiteType = AccessTools.TypeByName("GroundControlRts.CommanderEconomyService+ConstructionSite");
    internal static readonly Type? SlingLiftServiceType = AccessTools.TypeByName("GroundControlRts.CommanderSlingLiftService");
    internal static readonly Type? SlingJobType = AccessTools.TypeByName("GroundControlRts.CommanderSlingLiftService+SlingJob");
    internal static readonly Type? LeviathanServiceType = AccessTools.TypeByName("GroundControlRts.CommanderLeviathanService");
    internal static readonly Type? GodServiceType = AccessTools.TypeByName("GroundControlRts.CommanderGodService");
    internal static readonly Type? AlertServiceType = AccessTools.TypeByName("GroundControlRts.CommanderAlertService");
    internal static readonly Type? EconomyUiType = AccessTools.TypeByName("GroundControlRts.CommanderEconomyUi");
    internal static readonly Type? SupplyHeliServiceType = AccessTools.TypeByName("GroundControlRts.CommanderSupplyHeliService");
    internal static readonly Type? SelectionServiceType = AccessTools.TypeByName("GroundControlRts.CommanderSelectionService");
    internal static readonly Type? UiThemeType = AccessTools.TypeByName("GroundControlRts.CommanderUiTheme");
    internal static readonly Type? UiScaleType = AccessTools.TypeByName("GroundControlRts.CommanderUiScale");
    internal static readonly Type? WindowScaleType = AccessTools.TypeByName("GroundControlRts.CommanderWindowScale");
    internal static readonly Type? UnitPreviewType = AccessTools.TypeByName("GroundControlRts.CommanderUnitPreview");
    internal static readonly Type? UnitPreviewPartType = AccessTools.TypeByName("GroundControlRts.CommanderUnitPreview+Part");
    internal static readonly Type? BuildPreviewType = AccessTools.TypeByName("GroundControlRts.CommanderBuildPreview");
    internal static readonly Type? BuildPreviewPartType = AccessTools.TypeByName("GroundControlRts.CommanderBuildPreview+Part");
    internal static readonly Type? OverlayUiType = AccessTools.TypeByName("GroundControlRts.CommanderOverlayUi");
    internal static readonly Type? FogServiceType = AccessTools.TypeByName("GroundControlRts.CommanderFogOfWarService");
    internal static readonly Type? TerrainRouteServiceType = AccessTools.TypeByName("GroundControlRts.CommanderTerrainRouteService");
    internal static readonly Type? MoveServiceType = AccessTools.TypeByName("GroundControlRts.CommanderMoveService");
    internal static readonly Type? StanceType = AccessTools.TypeByName("GroundControlRts.CommanderStance");
    internal static readonly Type? MobileEmplacementServiceType = AccessTools.TypeByName("GroundControlRts.CommanderMobileEmplacementService");
    internal static readonly Type? SupplyStoreServiceType = AccessTools.TypeByName("GroundControlRts.CommanderSupplyStoreService");
    internal static readonly Type? NetworkType = AccessTools.TypeByName("GroundControlRts.CommanderNetwork");
    internal static readonly Type? SupplyHeliCargoMissionType = AccessTools.TypeByName("GroundControlRts.CommanderSupplyHeliService+CargoMission");
    internal static readonly Type? SupplyHeliPendingSpawnType = AccessTools.TypeByName("GroundControlRts.CommanderSupplyHeliService+PendingAircraftSpawn");
    internal static readonly Type? SupplyHeliQueuedSpawnType = AccessTools.TypeByName("GroundControlRts.CommanderSupplyHeliService+QueuedCargoSpawn");
    internal static readonly Type? NavalPurchaseServiceType = AccessTools.TypeByName("GroundControlRts.CommanderNavalPurchaseService");
    internal static readonly Type? FactionVehicleServiceType = AccessTools.TypeByName("GroundControlRts.CommanderFactionVehicleService");
    internal static readonly Type? SpawnServiceType = AccessTools.TypeByName("GroundControlRts.CommanderSpawnService");

    // CommanderEconomyService members
    internal static readonly PropertyInfo? EcoInstanceProp = AccessTools.Property(EconomyServiceType, "Instance");
    internal static readonly MethodInfo? IsBelowEnemyNetIncomeTargetMethod = AccessTools.Method(EconomyServiceType, "IsBelowEnemyNetIncomeTarget", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? GetAiStipendPerMinuteMethod = AccessTools.Method(EconomyServiceType, "GetAiStipendPerMinute", new[] { typeof(FactionHQ), typeof(FactionHQ) });
    internal static readonly MethodInfo? HasFreeMineMethod = AccessTools.Method(EconomyServiceType, "HasFreeMine", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? CountFreeMinesMethod = AccessTools.Method(EconomyServiceType, "CountFreeMines", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? CountMinesMethod = FindMethodByNameAndParamCount(EconomyServiceType, "CountMines", 2);
    internal static readonly MethodInfo? GetEnemyMineTargetMethod = AccessTools.Method(EconomyServiceType, "GetEnemyMineTarget", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? TryBuildEnemyMineMethod = AccessTools.Method(EconomyServiceType, "TryBuildEnemyMine", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? TryUpgradeEnemyIncomeMethod = AccessTools.Method(EconomyServiceType, "TryUpgradeEnemyIncome", new[] { typeof(FactionHQ), typeof(float) });
    internal static readonly MethodInfo? GetEnemyBuildReserveMethod = AccessTools.Method(EconomyServiceType, "GetEnemyBuildReserve", new[] { typeof(FactionHQ), typeof(bool).MakeByRefType(), typeof(bool).MakeByRefType() });
    internal static readonly MethodInfo? GetEnemyFactoryTargetMethod = AccessTools.Method(EconomyServiceType, "GetEnemyFactoryTarget", new[] { typeof(FactionHQ), typeof(bool) });
    internal static readonly MethodInfo? TryBuildEnemyFactoryMethod = AccessTools.Method(EconomyServiceType, "TryBuildEnemyFactory", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? GetEnemyFactoryReserveMethod = AccessTools.Method(EconomyServiceType, "GetEnemyFactoryReserve", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? CountFactoriesMethod = AccessTools.Method(EconomyServiceType, "CountFactories", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? HoldsMostBasesMethod = AccessTools.Method(EconomyServiceType, "HoldsMostBases", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? GetHqIncomePerMinuteMethod = FindMethodByNameAndParamCount(EconomyServiceType, "GetIncomePerMinute", 1);
    internal static readonly MethodInfo? GetStageIncomePerMinuteMethod = FindMethodByNameAndParamCount(EconomyServiceType, "GetIncomePerMinute", 2);
    internal static readonly MethodInfo? GetIncomeKindMethod = AccessTools.Method(EconomyServiceType, "GetIncomeKind", new[] { typeof(Unit) });
    internal static readonly MethodInfo? GetEnemyIncomePerMinuteMethod = AccessTools.Method(EconomyServiceType, "GetEnemyIncomePerMinute", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? GetEnemyNetIncomePerMinuteMethod = AccessTools.Method(EconomyServiceType, "GetEnemyNetIncomePerMinute", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? GetBestDepotLevelMethod = AccessTools.Method(EconomyServiceType, "GetBestDepotLevel", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? GetRequiredDepotLevelMethod = AccessTools.Method(EconomyServiceType, "GetRequiredDepotLevel", new[] { typeof(VehicleDefinition) });
    internal static readonly MethodInfo? PayIncomeMethod = AccessTools.Method(EconomyServiceType, "PayIncome");
    internal static readonly MethodInfo? ResolveCategoryDefinitionMethod = AccessTools.Method(EconomyServiceType, "ResolveCategoryDefinition", new[] { typeof(BuildingType), typeof(bool) });
    internal static readonly MethodInfo? EcoGetDepotLevelMethod = AccessTools.Method(EconomyServiceType, "GetDepotLevel", new[] { typeof(Unit) });
    internal static readonly MethodInfo? EcoGetDepotUpgradeCostMethod = AccessTools.Method(EconomyServiceType, "GetDepotUpgradeCost", new[] { typeof(int) });
    internal static readonly MethodInfo? EcoCountLandBasesMethod = AccessTools.Method(EconomyServiceType, "CountLandBases", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? EcoHasDepotUpgradeIncomeMethod = AccessTools.Method(EconomyServiceType, "HasDepotUpgradeIncome", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? EcoTryUpgradeEnemyDepotMethod = AccessTools.Method(EconomyServiceType, "TryUpgradeEnemyDepot", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? EcoIsHeldDefenceMethod = AccessTools.Method(EconomyServiceType, "IsHeldDefence", new[] { typeof(Unit) });
    internal static readonly MethodInfo? EcoGetDefencePriceMethod = AccessTools.Method(EconomyServiceType, "GetDefencePrice", new[] { typeof(Unit) });
    internal static readonly MethodInfo? GetFactoriesMethod = AccessTools.Method(EconomyServiceType, "GetFactories");
    internal static readonly FieldInfo? EcoIncomeLevelsField = AccessTools.Field(EconomyServiceType, "incomeLevels");
    internal static readonly MethodInfo? EcoChooseEnemyProductionMethod = AccessTools.Method(EconomyServiceType, "ChooseEnemyProduction", new[] { typeof(FactionHQ), typeof(List<UnitDefinition>) });
    internal static readonly MethodInfo? EcoChooseEnemyIncomeKindMethod = AccessTools.Method(EconomyServiceType, "ChooseEnemyIncomeKind", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? EcoFindEnemyIncomeUpgradeMethod = AccessTools.Method(EconomyServiceType, "FindEnemyIncomeUpgrade", new[] { typeof(FactionHQ), typeof(float).MakeByRefType() });
    internal static readonly MethodInfo? EcoGetNavalDockLevelHqMethod = AccessTools.Method(EconomyServiceType, "GetNavalDockLevel", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? EcoGetNavalDockUpgradeCostMethod = AccessTools.Method(EconomyServiceType, "GetNavalDockUpgradeCost", new[] { typeof(int) });
    internal static readonly MethodInfo? EcoTryUpgradeEnemyNavalDockMethod = AccessTools.Method(EconomyServiceType, "TryUpgradeEnemyNavalDock", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? EcoGetFactoryBuildCostMethod = AccessTools.Method(EconomyServiceType, "GetFactoryBuildCost", new[] { typeof(UnitDefinition) });
    internal static readonly MethodInfo? EcoIsCricketMethod = AccessTools.Method(EconomyServiceType, "IsCricket", new[] { typeof(UnitDefinition) });
    internal static readonly FieldInfo? EcoEnemyFactoryProductsField = AccessTools.Field(EconomyServiceType, "enemyFactoryProducts");
    internal static readonly MethodInfo? EcoTryBuildEnemyDepotMethod = AccessTools.Method(EconomyServiceType, "TryBuildEnemyDepot", new[] { typeof(FactionHQ), typeof(int) });
    internal static readonly MethodInfo? EcoHasBaseWithoutUpgradedDepotMethod = AccessTools.Method(EconomyServiceType, "HasBaseWithoutUpgradedDepot", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? EcoCountBuildingsMethod = AccessTools.Method(EconomyServiceType, "CountBuildings", new[] { typeof(FactionHQ), typeof(BuildingType) });
    internal static readonly MethodInfo? EcoGetEnemyIncomeReserveMethod = FindMethodByNameAndParamCount(EconomyServiceType, "GetEnemyIncomeReserve", 2);
    internal static readonly FieldInfo? EcoUpkeepPerMinuteField = AccessTools.Field(EconomyServiceType, "upkeepPerMinute");
    internal static readonly FieldInfo? EcoEnemyFactoryFundsField = AccessTools.Field(EconomyServiceType, "enemyFactoryFunds");
    internal static readonly MethodInfo? EcoGetBaseIncomePerMinuteMethod = AccessTools.Method(EconomyServiceType, "GetBaseIncomePerMinute", Type.EmptyTypes);
    internal static readonly MethodInfo? EcoGetGodOpeningReserveMethod = AccessTools.Method(EconomyServiceType, "GetGodOpeningReserve", new[] { typeof(FactionHQ) });
    internal static readonly PropertyInfo? EcoIncomeTickProgressProp = AccessTools.Property(EconomyServiceType, "IncomeTickProgress");
    internal static readonly PropertyInfo? EcoFriendlyUpkeepPerMinuteProp = AccessTools.Property(EconomyServiceType, "FriendlyUpkeepPerMinute");
    internal static readonly MethodInfo? EcoRefreshLedgerMethod = AccessTools.Method(EconomyServiceType, "RefreshLedger", Type.EmptyTypes);
    internal static readonly FieldInfo? EcoLedgerGrossField = AccessTools.Field(EconomyServiceType, "ledgerGross");
    internal static readonly FieldInfo? EcoNextLedgerAtField = AccessTools.Field(EconomyServiceType, "nextLedgerAt");
    internal static readonly MethodInfo? EcoRefreshCatalogMethod = AccessTools.Method(EconomyServiceType, "RefreshCatalog", Type.EmptyTypes);
    internal static readonly FieldInfo? EcoCatalogListField = AccessTools.Field(EconomyServiceType, "catalog");
    internal static readonly MethodInfo? AirbaseCaptureFactionMethod = AccessTools.Method(typeof(Airbase), "CaptureFaction", new[] { typeof(FactionHQ) });
    internal static readonly PropertyInfo? NetworkIsGuestProp = AccessTools.Property(NetworkType, "IsGuest");

    // CommanderEnemyCommanderService members
    internal static readonly PropertyInfo? EnemyInstanceProp = AccessTools.Property(EnemyCommanderType, "Instance");
    internal static readonly MethodInfo? IsAiFactionMethod = AccessTools.Method(EnemyCommanderType, "IsAiFaction", new[] { typeof(FactionHQ), typeof(FactionHQ) });
    internal static readonly MethodInfo? EnemyHasHumanPlayersMethod = AccessTools.Method(EnemyCommanderType, "HasHumanPlayers", new[] { typeof(FactionHQ) });
    internal static readonly PropertyInfo? EnemyBothSidesProp = AccessTools.Property(EnemyCommanderType, "BothSides");
    internal static readonly PropertyInfo? SettingsHumanTeamsProp = AccessTools.Property(SettingsType, "EnemyCommanderHumanTeams");
    internal static readonly MethodInfo? IsChallengingMethod = AccessTools.Method(EnemyCommanderType, "IsChallenging", new[] { typeof(FactionHQ) });
    internal static readonly PropertyInfo? IsChampionsMissionProp = AccessTools.Property(EnemyCommanderType, "IsChampionsMission");
    internal static readonly PropertyInfo? IsDuelMissionProp = AccessTools.Property(EnemyCommanderType, "IsDuelMission");
    internal static readonly MethodInfo? PrepareDuelMethod = AccessTools.Method(EnemyCommanderType, "PrepareDuel", new[] { typeof(FactionHQ), typeof(FactionHQ) });
    internal static readonly MethodInfo? EnemyReviewMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "Review", 4);
    internal static readonly MethodInfo? ScalePurchasesMethod = AccessTools.Method(EnemyCommanderType, "ScalePurchases", new[] { typeof(int), typeof(int), typeof(float) });
    internal static readonly MethodInfo? PickMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "Pick", 3);
    internal static readonly MethodInfo? ChooseMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "Choose", 3);
    internal static readonly MethodInfo? ChooseCaptureUnitMethod = AccessTools.Method(EnemyCommanderType, "ChooseCaptureUnit", new[] { typeof(float) });
    internal static readonly MethodInfo? ChooseReconUnitMethod = AccessTools.Method(EnemyCommanderType, "ChooseReconUnit", new[] { typeof(float) });
    internal static readonly MethodInfo? CollectCatalogMethod = AccessTools.Method(EnemyCommanderType, "CollectCatalog", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? CounterWeightMethod = AccessTools.Method(EnemyCommanderType, "CounterWeight", new[] { typeof(float), typeof(float), typeof(float), typeof(int) });
    internal static readonly MethodInfo? ChallengingAirDefenceTargetMethod = AccessTools.Method(EnemyCommanderType, "ChallengingAirDefenceTarget", new[] { typeof(int) });
    internal static readonly MethodInfo? WantsRadarHunterMethod = AccessTools.Method(EnemyCommanderType, "WantsRadarHunter", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? FindRadarToHuntMethod = AccessTools.Method(EnemyCommanderType, "FindRadarToHunt", new[] { typeof(FactionHQ), typeof(GlobalPosition), typeof(GlobalPosition).MakeByRefType() });
    internal static readonly MethodInfo? TryBuyRoleMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "TryBuyRole", 7);
    internal static readonly MethodInfo? TaskAirWingMethod = AccessTools.Method(EnemyCommanderType, "TaskAirWing", new[] { typeof(FactionHQ), typeof(FactionHQ) });
    internal static readonly MethodInfo? IsRadarHunterMethod = AccessTools.Method(EnemyCommanderType, "IsRadarHunter", new[] { typeof(Aircraft), typeof(FactionHQ) });
    internal static readonly MethodInfo? IsCombatVehicleMethod = AccessTools.Method(EnemyCommanderType, "IsCombatVehicle", new[] { typeof(VehicleDefinition) });
    internal static readonly MethodInfo? IsAirDefenceMethod = AccessTools.Method(EnemyCommanderType, "IsAirDefence", new[] { typeof(VehicleDefinition) });
    internal static readonly MethodInfo? IsRadarGuidedAirDefenceMethod = AccessTools.Method(EnemyCommanderType, "IsRadarGuidedAirDefence", new[] { typeof(VehicleDefinition) });
    internal static readonly MethodInfo? IsBuilderVehicleMethod = AccessTools.Method(EnemyCommanderType, "IsBuilderVehicle", new[] { typeof(VehicleDefinition) });
    internal static readonly MethodInfo? IsSupplyVehicleMethod = AccessTools.Method(EnemyCommanderType, "IsSupplyVehicle", new[] { typeof(VehicleDefinition) });
    internal static readonly MethodInfo? CanDriveBetweenMethod = AccessTools.Method(EnemyCommanderType, "CanDriveBetween", new[] { typeof(GlobalPosition), typeof(GlobalPosition) });
    internal static readonly MethodInfo? EnemyReviewDefenceMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "ReviewDefence", 3);
    internal static readonly MethodInfo? EnemyReviewSupportMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "ReviewSupport", 3);
    internal static readonly MethodInfo? EnemyLadsWantedMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "LadsWanted", 2);
    internal static readonly MethodInfo? EnemyIsExtraLadsMethod = AccessTools.Method(EnemyCommanderType, "IsExtraLads", new[] { typeof(Unit) });
    internal static readonly MethodInfo? EnemyChooseRingGunMethod = AccessTools.Method(EnemyCommanderType, "ChooseRingGun", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? EnemyChooseRadarSamMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "ChooseRadarSam", 2);
    internal static readonly MethodInfo? EnemyChooseSamDepotMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "ChooseSamDepot", 3);
    internal static readonly MethodInfo? EnemyChooseSamLauncherUnitMethod = AccessTools.Method(EnemyCommanderType, "ChooseSamLauncherUnit", new[] { typeof(float) });
    internal static readonly MethodInfo? EnemySiteFillRankMethod = AccessTools.Method(EnemyCommanderType, "SiteFillRank", new[] { typeof(int) });
    internal static readonly MethodInfo? EnemySamRadarTargetMethod = AccessTools.Method(EnemyCommanderType, "SamRadarTarget", new[] { typeof(int), typeof(int), typeof(int) });
    internal static readonly MethodInfo? EnemyIsAirDefenceAtMaxMethod = AccessTools.Method(EnemyCommanderType, "IsAirDefenceAtMax", Type.EmptyTypes);
    internal static readonly MethodInfo? EnemyIsAirDefenceFullMethod = AccessTools.Method(EnemyCommanderType, "IsAirDefenceFull", new[] { typeof(VehicleDefinition) });
    internal static readonly MethodInfo? EnemyIsAirDefenceShareFullMethod = AccessTools.Method(EnemyCommanderType, "IsAirDefenceShareFull", Type.EmptyTypes);
    internal static readonly MethodInfo? EnemyIsConvoyAirDefenceMethod = AccessTools.Method(EnemyCommanderType, "IsConvoyAirDefence", new[] { typeof(VehicleDefinition) });
    internal static readonly MethodInfo? EnemyBaseLacksMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "BaseLacks", 4);
    internal static readonly MethodInfo? EnemyCountOwnedMethod = AccessTools.Method(EnemyCommanderType, "CountOwned", new[] { typeof(FactionHQ), typeof(VehicleDefinition) });
    internal static readonly MethodInfo? EnemyCountSamLaunchersMethod = AccessTools.Method(EnemyCommanderType, "CountSamLaunchers", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? EnemyCountRadarReserveMethod = AccessTools.Method(EnemyCommanderType, "CountRadarReserve", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? EnemyCanFieldMethod = AccessTools.Method(EnemyCommanderType, "CanField", new[] { typeof(VehicleDepot), typeof(VehicleDefinition) });
    internal static readonly MethodInfo? EnemyFindSiteDepotMethod = AccessTools.Method(EnemyCommanderType, "FindSiteDepot", new[] { typeof(FactionHQ), typeof(GlobalPosition) });
    internal static readonly MethodInfo? EnemyCollectBaseDepotsMethod = AccessTools.Method(EnemyCommanderType, "CollectBaseDepots", new[] { typeof(FactionHQ), typeof(GlobalPosition), typeof(List<VehicleDepot>) });
    internal static readonly MethodInfo? EnemyHasSiteWantingLauncherMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "HasSiteWantingLauncher", 3);
    internal static readonly MethodInfo? EnemyChooseSamSiteMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "ChooseSamSite", 3);
    internal static readonly MethodInfo? EnemyRecruitAirDefenceMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "RecruitAirDefence", 2);
    internal static readonly MethodInfo? EnemyCountSitePartsMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "CountSiteParts", 2);
    internal static readonly MethodInfo? EnemySamSiteCountMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "SamSiteCount", 1);
    internal static readonly MethodInfo? EnemyIsSamSiteLauncherUnitMethod = AccessTools.Method(EnemyCommanderType, "IsSamSiteLauncher", new[] { typeof(Unit) });
    internal static readonly MethodInfo? EnemyIsSamSiteLauncherDefMethod = AccessTools.Method(EnemyCommanderType, "IsSamSiteLauncher", new[] { typeof(VehicleDefinition) });
    internal static readonly MethodInfo? EnemyIsRearmVehicleMethod = AccessTools.Method(EnemyCommanderType, "IsRearmVehicle", new[] { typeof(Unit) });
    internal static readonly MethodInfo? EnemyIsRadarVehicleMethod = AccessTools.Method(EnemyCommanderType, "IsRadarVehicle", new[] { typeof(Unit) });
    internal static readonly MethodInfo? EnemyGetHomeBaseMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "GetHomeBase", 2);
    internal static readonly MethodInfo? EnemyIsPostAtMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "IsPostAt", 3);
    internal static readonly MethodInfo? EnemyChooseSupplyTruckMethod = AccessTools.Method(EnemyCommanderType, "ChooseSupplyTruck", new[] { typeof(float) });
    internal static readonly MethodInfo? EnemyTryBuySamLauncherMethod = CommanderStateType != null ? AccessTools.Method(EnemyCommanderType, "TryBuySamLauncher", new[] { typeof(FactionHQ), CommanderStateType, typeof(float) }) : null;
    internal static readonly MethodInfo? EnemyDropCricketsMethod = AccessTools.Method(EnemyCommanderType, "DropCrickets", new[] { typeof(FactionHQ), typeof(bool), typeof(float) });
    internal static readonly MethodInfo? EnemyCountOwnedAirDefenceMethod = AccessTools.Method(EnemyCommanderType, "CountOwnedAirDefence", Type.EmptyTypes);
    internal static readonly FieldInfo? EnemyAirCandidatesField = AccessTools.Field(EnemyCommanderType, "airCandidates");
    internal static readonly FieldInfo? EnemySavingForField = AccessTools.Field(EnemyCommanderType, "savingFor");
    internal static readonly FieldInfo? EnemyOwnedCombatField = AccessTools.Field(EnemyCommanderType, "ownedCombat");
    internal static readonly FieldInfo? EnemyCatalogField = AccessTools.Field(EnemyCommanderType, "catalog");
    internal static readonly FieldInfo? EnemyDearLeanField = AccessTools.Field(EnemyCommanderType, "dearLean");
    internal static readonly FieldInfo? EnemyStatesField = AccessTools.Field(EnemyCommanderType, "states");
    internal static readonly FieldInfo? EnemySiteLaunchersField = AccessTools.Field(EnemyCommanderType, "siteLaunchers");
    internal static readonly FieldInfo? EnemySiteRadarsField = AccessTools.Field(EnemyCommanderType, "siteRadars");
    internal static readonly FieldInfo? EnemySiteFireControlsField = AccessTools.Field(EnemyCommanderType, "siteFireControls");
    internal static readonly FieldInfo? EnemySiteTrucksField = AccessTools.Field(EnemyCommanderType, "siteTrucks");
    internal static readonly FieldInfo? EnemyRearmRunsField = AccessTools.Field(EnemyCommanderType, "rearmRuns");
    internal static readonly FieldInfo? EnemyGuardCentresField = AccessTools.Field(EnemyCommanderType, "guardCentres");
    internal static readonly FieldInfo? EnemyGuardFieldsField = AccessTools.Field(EnemyCommanderType, "guardFields");
    internal static readonly PropertyInfo? StateWaveOutProp = AccessTools.Property(CommanderStateType, "WaveOut");
    internal static readonly PropertyInfo? StateWaveWaitSinceProp = AccessTools.Property(CommanderStateType, "WaveWaitSince");
    internal static readonly PropertyInfo? StateWaveGoalProp = AccessTools.Property(CommanderStateType, "WaveGoal");
    internal static readonly PropertyInfo? StateSamDepotProp = AccessTools.Property(CommanderStateType, "SamDepot");
    internal static readonly PropertyInfo? StateSamFundRingTurnProp = AccessTools.Property(CommanderStateType, "SamFundRingTurn");
    internal static readonly PropertyInfo? StateWantsSamSiteProp = AccessTools.Property(CommanderStateType, "WantsSamSite");
    internal static readonly PropertyInfo? StateWantsSiteRadarProp = AccessTools.Property(CommanderStateType, "WantsSiteRadar");
    internal static readonly PropertyInfo? StateWantsGuardTruckProp = AccessTools.Property(CommanderStateType, "WantsGuardTruck");
    internal static readonly PropertyInfo? StateWantsFireControlProp = AccessTools.Property(CommanderStateType, "WantsFireControl");
    internal static readonly PropertyInfo? StateSiteRadarDepotProp = AccessTools.Property(CommanderStateType, "SiteRadarDepot");
    internal static readonly PropertyInfo? StateSlotsProp = AccessTools.Property(CommanderStateType, "Slots");
    internal static readonly PropertyInfo? StateDefencePostsProp = AccessTools.Property(CommanderStateType, "DefencePosts");
    internal static readonly PropertyInfo? StateDefencePostBasesProp = AccessTools.Property(CommanderStateType, "DefencePostBases");
    internal static readonly PropertyInfo? StateDefenceBaseCountProp = AccessTools.Property(CommanderStateType, "DefenceBaseCount");
    internal static readonly PropertyInfo? StateAirShortDepotsProp = AccessTools.Property(CommanderStateType, "AirShortDepots");
    internal static readonly PropertyInfo? StateDefendersProp = AccessTools.Property(CommanderStateType, "Defenders");
    internal static readonly PropertyInfo? StateSupportProp = AccessTools.Property(CommanderStateType, "Support");
    internal static readonly PropertyInfo? StateAirFundProp = AccessTools.Property(CommanderStateType, "AirFund");
    internal static readonly PropertyInfo? StateAirSaveCapProp = AccessTools.Property(CommanderStateType, "AirSaveCap");
    internal static readonly PropertyInfo? StateSamFundProp = AccessTools.Property(CommanderStateType, "SamFund");
    internal static readonly PropertyInfo? StateMineFundProp = AccessTools.Property(CommanderStateType, "MineFund");
    internal static readonly PropertyInfo? StateReconPostsProp = AccessTools.Property(CommanderStateType, "ReconPosts");
    internal static readonly PropertyInfo? StateBatteriesProp = AccessTools.Property(CommanderStateType, "Batteries");
    internal static readonly PropertyInfo? StateBatteryTargetsProp = AccessTools.Property(CommanderStateType, "BatteryTargets");
    internal static readonly PropertyInfo? StateEcoAlarmUntilProp = AccessTools.Property(CommanderStateType, "EcoAlarmUntil");
    internal static readonly PropertyInfo? StateEcoAlarmAtProp = AccessTools.Property(CommanderStateType, "EcoAlarmAt");
    internal static readonly PropertyInfo? StateDefendingProp = AccessTools.Property(CommanderStateType, "Defending");
    internal static readonly PropertyInfo? StateWaveMembersProp = AccessTools.Property(CommanderStateType, "WaveMembers");
    internal static readonly PropertyInfo? StateNavalRallyingProp = AccessTools.Property(CommanderStateType, "NavalRallying");
    internal static readonly PropertyInfo? StateNavalWaitingSinceProp = AccessTools.Property(CommanderStateType, "NavalWaitingSince");
    internal static readonly PropertyInfo? StateNavalFundProp = AccessTools.Property(CommanderStateType, "NavalFund");
    internal static readonly PropertyInfo? StateWarFundAtProp = AccessTools.Property(CommanderStateType, "WarFundAt");
    internal static readonly PropertyInfo? StateBrokeProp = AccessTools.Property(CommanderStateType, "Broke");

    // Additional CommanderEnemyCommanderService tactical AI members
    internal static readonly MethodInfo? EnemyBuyAirframeMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "BuyAirframe", 5);
    internal static readonly MethodInfo? EnemyTryBuyPicketMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "TryBuyPicket", 4);
    internal static readonly MethodInfo? EnemyCountPicketsMethod = AccessTools.Method(EnemyCommanderType, "CountPickets", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? EnemyCountAirborneMethod = AccessTools.Method(EnemyCommanderType, "CountAirborne", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? EnemyGetAirborneLimitMethod = AccessTools.Method(EnemyCommanderType, "GetAirborneLimit", Type.EmptyTypes);
    internal static readonly MethodInfo? EnemyHasAirStockMethod = AccessTools.Method(EnemyCommanderType, "HasAirStock", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? EnemyChooseAirRoleMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "ChooseAirRole", 2);
    internal static readonly MethodInfo? EnemyKeepAntiShipCandidatesMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "KeepAntiShipCandidates", 5);
    internal static readonly MethodInfo? EnemyScoreAntiShipStoreMethod = AccessTools.Method(EnemyCommanderType, "ScoreAntiShipStore", new[] { typeof(WeaponMount), typeof(float) });
    internal static readonly MethodInfo? EnemyGetStrikeTargetMethod = AccessTools.Method(EnemyCommanderType, "GetStrikeTarget", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? EnemyPickWaveFlankMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "PickWaveFlank", 3);
    internal static readonly MethodInfo? EnemyPickPriorityTargetMethod = AccessTools.Method(EnemyCommanderType, "PickPriorityTarget", new[] { typeof(FactionHQ), typeof(FactionHQ), typeof(GlobalPosition), typeof(Aircraft) });
    internal static readonly MethodInfo? EnemyPickSalvoTargetMethod = AccessTools.Method(EnemyCommanderType, "PickSalvoTarget", new[] { typeof(FactionHQ), typeof(FactionHQ), typeof(GlobalPosition), typeof(Aircraft) });
    internal static readonly MethodInfo? EnemyCanAnyStoreDefeatMethod = AccessTools.Method(EnemyCommanderType, "CanAnyStoreDefeat", new[] { typeof(Aircraft), typeof(Unit) });
    internal static readonly MethodInfo? EnemyGetCruiseHeightMethod = AccessTools.Method(EnemyCommanderType, "GetCruiseHeight", new[] { typeof(FactionHQ), typeof(Aircraft) });
    internal static readonly MethodInfo? EnemyPickJammerTargetsMethod = AccessTools.Method(EnemyCommanderType, "PickJammerTargets", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? EnemyReviewAaScoutsMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "ReviewAaScouts", 2);
    internal static readonly MethodInfo? EnemyEnsureReconPostsMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "EnsureReconPosts", 3);
    internal static readonly MethodInfo? EnemyFindHighGroundMethod = AccessTools.Method(EnemyCommanderType, "FindHighGround", new[] { typeof(GlobalPosition) });
    internal static readonly MethodInfo? EnemyReviewArtilleryMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "ReviewArtillery", 3);
    internal static readonly MethodInfo? EnemyRaiseEcoAlarmMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "RaiseEcoAlarm", 4);
    internal static readonly MethodInfo? EnemyLocateBatteryMethod = AccessTools.Method(EnemyCommanderType, "LocateBattery", new[] { typeof(FactionHQ), typeof(PersistentID), typeof(bool) });
    internal static readonly MethodInfo? EnemyReleaseNavalWaveMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "ReleaseNavalWave", 3);
    internal static readonly MethodInfo? EnemyPinToMethod = AccessTools.Method(EnemyCommanderType, "PinTo", new[] { typeof(Unit), typeof(GlobalPosition) });
    internal static readonly MethodInfo? EnemyHoldAtMethod = AccessTools.Method(EnemyCommanderType, "HoldAt", new[] { typeof(Unit), typeof(GlobalPosition) });
    internal static readonly MethodInfo? EnemyReleaseCommandedDestinationMethod = AccessTools.Method(EnemyCommanderType, "ReleaseCommandedDestination", new[] { typeof(Unit) });
    internal static readonly MethodInfo? EnemyFindGuardTruckMethod = FindMethodByNameAndParamCount(EnemyCommanderType, "FindGuardTruck", 5);
    internal static readonly MethodInfo? EnemyCanLaunchFromMethod = AccessTools.Method(EnemyCommanderType, "CanLaunchFrom", new[] { typeof(Airbase), typeof(FactionHQ), typeof(AircraftDefinition) });
    internal static readonly PropertyInfo? StateStrikeTruckProp = AccessTools.Property(CommanderStateType, "StrikeTruck");
    internal static readonly MethodInfo? EnemyIsDefendingUnitMethod = AccessTools.Method(EnemyCommanderType, "IsDefendingUnit", new[] { typeof(Unit) });
    internal static readonly MethodInfo? EnemyIsRadarPicketMethod = AccessTools.Method(EnemyCommanderType, "IsRadarPicket", new[] { typeof(AircraftDefinition), typeof(FactionHQ) });
    internal static readonly MethodInfo? EnemySteerPicketsMethod = AccessTools.Method(EnemyCommanderType, "SteerPickets", new[] { typeof(FactionHQ), typeof(FactionHQ) });
    internal static readonly MethodInfo? EnemyIsRecruitableMethod = AccessTools.Method(EnemyCommanderType, "IsRecruitable", new[] { typeof(FactionHQ), typeof(Unit) });
    internal static readonly MethodInfo? EnemyGetAirRoleAircraftMethod = AccessTools.Method(EnemyCommanderType, "GetAirRole", new[] { typeof(Aircraft) });
    internal static readonly FieldInfo? EnemyJammersField = AccessTools.Field(EnemyCommanderType, "jammers");
    internal static readonly ConstructorInfo? ActiveJammerCtor = ActiveJammerType?.GetConstructor(AnyDeclared, null, new[] { typeof(Aircraft), typeof(WeaponStation), typeof(JammingPod), typeof(Unit) }, null);

    // CommanderCaptureService members
    internal static readonly PropertyInfo? CaptureInstanceProp = AccessTools.Property(CaptureServiceType, "Instance");
    internal static readonly MethodInfo? ReviewEnemiesCaptureMethod = AccessTools.Method(CaptureServiceType, "ReviewEnemies");
    internal static readonly MethodInfo? LaunchWaveIfFormedMethod = FindMethodByNameAndParamCount(CaptureServiceType, "LaunchWaveIfFormed", 5);
    internal static readonly MethodInfo? GetLaneFormUpMethod = AccessTools.Method(CaptureServiceType, "GetLaneFormUp", new[] { typeof(GlobalPosition), typeof(GlobalPosition), typeof(Unit) });
    internal static readonly MethodInfo? ChooseEnemyTarget4Method = FindMethodByNameAndParamCount(CaptureServiceType, "ChooseEnemyTarget", 4);
    internal static readonly MethodInfo? IsTakeableMethod = AccessTools.Method(CaptureServiceType, "IsTakeable", new[] { typeof(Airbase), typeof(FactionHQ) });
    internal static readonly MethodInfo? GetOwnerGripMethod = AccessTools.Method(CaptureServiceType, "GetOwnerGrip", new[] { typeof(Airbase) });
    internal static readonly MethodInfo? GetAirbaseLabelMethod = AccessTools.Method(CaptureServiceType, "GetAirbaseLabel", new[] { typeof(Airbase) });
    internal static readonly MethodInfo? CaptureReviewMainEffortMethod = AccessTools.Method(CaptureServiceType, "ReviewMainEffort", new[] { typeof(FactionHQ), typeof(FactionHQ) });
    internal static readonly MethodInfo? CaptureFindNearestHeldBaseMethod = AccessTools.Method(CaptureServiceType, "FindNearestHeldBase", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? CaptureReleaseDriveMethod = FindMethodByNameAndParamCount(CaptureServiceType, "ReleaseDrive", 1);
    internal static readonly MethodInfo? CaptureIsOnEnemyDriveMethod = AccessTools.Method(CaptureServiceType, "IsOnEnemyDrive", new[] { typeof(Unit) });
    internal static readonly MethodInfo? CaptureCountDefendersMethod = AccessTools.Method(CaptureServiceType, "CountDefenders", new[] { typeof(Airbase), typeof(GlobalPosition), typeof(float) });
    internal static readonly MethodInfo? CaptureGetHoldPointMethod = AccessTools.Method(CaptureServiceType, "GetHoldPoint", new[] { typeof(Airbase) });
    internal static readonly MethodInfo? CaptureGetRoadMetersMethod = AccessTools.Method(CaptureServiceType, "GetRoadMeters", new[] { typeof(Airbase), typeof(Airbase) });
    internal static readonly MethodInfo? CaptureIsSquadCandidateMethod = FindMethodByNameAndParamCount(CaptureServiceType, "IsSquadCandidate", 2);
    internal static readonly MethodInfo? CaptureGetTerritoryCenterMethod = AccessTools.Method(CaptureServiceType, "GetTerritoryCenter", new[] { typeof(FactionHQ) });
    internal static readonly FieldInfo? CaptureDrivesField = AccessTools.Field(CaptureServiceType, "drives");
    internal static readonly FieldInfo? CaptureSquadField = AccessTools.Field(CaptureServiceType, "squad");
    internal static readonly FieldInfo? CaptureStagingBasesField = AccessTools.Field(CaptureServiceType, "stagingBases");
    internal static readonly PropertyInfo? DriveCommittedProp = AccessTools.Property(EnemyDriveType, "Committed");
    internal static readonly PropertyInfo? DriveTargetProp = AccessTools.Property(EnemyDriveType, "Target");
    internal static readonly PropertyInfo? DriveOwnerProp = AccessTools.Property(EnemyDriveType, "Owner");
    internal static readonly PropertyInfo? DriveLaunchedProp = AccessTools.Property(EnemyDriveType, "Launched");
    internal static readonly PropertyInfo? DriveMarchingProp = AccessTools.Property(EnemyDriveType, "Marching");
    internal static readonly PropertyInfo? DriveFormingSinceProp = AccessTools.Property(EnemyDriveType, "FormingSince");
    internal static readonly PropertyInfo? DriveMarchSinceProp = AccessTools.Property(EnemyDriveType, "MarchSince");
    internal static readonly PropertyInfo? DriveAnnouncedProp = AccessTools.Property(EnemyDriveType, "Announced");

    // CommanderSettings & CommanderGameAccess members
    internal static readonly PropertyInfo? EnemyDifficultyProp = AccessTools.Property(SettingsType, "EnemyDifficulty");
    internal static readonly PropertyInfo? EnemyMineIncomeMultiplierProp = AccessTools.Property(SettingsType, "EnemyMineIncomeMultiplier");
    internal static readonly MethodInfo? EnemyMineIncomeMultiplierGetter = EnemyMineIncomeMultiplierProp?.GetGetMethod(true);
    internal static readonly MethodInfo? GetLocalHqMethod = AccessTools.Method(GameAccessType, "GetLocalHq");
    internal static readonly MethodInfo? SnapToTerrainMethod = AccessTools.Method(GameAccessType, "SnapToTerrain", new[] { typeof(GlobalPosition) });
    internal static readonly MethodInfo? IsBelowSeaLevelMethod = AccessTools.Method(GameAccessType, "IsBelowSeaLevel", new[] { typeof(GlobalPosition) });
    internal static readonly MethodInfo? IsLandBaseMethod = AccessTools.Method(GameAccessType, "IsLandBase", new[] { typeof(Airbase) });

    // Milestone 3 performance targets
    internal static readonly MethodInfo? ClearTargetsUsingRadarMethod = AccessTools.Method(CommanderRadarServiceType, "ClearTargetsUsingRadar", new[] { typeof(Radar) });
    internal static readonly MethodInfo? CommanderLogInfoMethod = AccessTools.Method(CommanderLogType, "LogInfo", new[] { typeof(object) });

    // Milestone 4 RTS City, Dynamic Caps, Tech-Tree & Pioneer Roads targets
    internal static readonly PropertyInfo? CommanderPluginInstanceProp = AccessTools.Property(CommanderPluginType, "Instance");
    internal static readonly PropertyInfo? IsCommanderModeActiveProp = AccessTools.Property(CommanderPluginType, "IsCommanderModeActive");
    internal static readonly MethodInfo? NoticeFailMethod = AccessTools.Method(NoticeType, "Fail", new[] { typeof(string) });
    internal static readonly PropertyInfo? RepairInstanceProp = AccessTools.Property(RepairServiceType, "Instance");
    internal static readonly MethodInfo? IsRepairUnitMethod = AccessTools.Method(RepairServiceType, "IsRepairUnit", new[] { typeof(Unit) });
    internal static readonly MethodInfo? SendHomeMethod = AccessTools.Method(RepairServiceType, "SendHome", new[] { typeof(Unit) });
    internal static readonly MethodInfo? RepairTryGetAssignedTargetMethod =
        AccessTools.Method(RepairServiceType, "TryGetAssignedTarget", new[] { typeof(Unit), typeof(Unit).MakeByRefType() });

    internal static readonly MethodInfo? UnitCapEnabledGetter = AccessTools.Property(UnitCapType, "Enabled")?.GetGetMethod(true);
    internal static readonly MethodInfo? UnitCapCapGetter = AccessTools.Property(UnitCapType, "Cap")?.GetGetMethod(true);
    internal static readonly MethodInfo? UnitCapGroundCapGetter = AccessTools.Property(UnitCapType, "GroundCap")?.GetGetMethod(true);
    internal static readonly MethodInfo? UnitCapTypeCapGetter = AccessTools.Property(UnitCapType, "TypeCap")?.GetGetMethod(true);
    internal static readonly MethodInfo? UnitCapAirDefenceCapGetter = AccessTools.Property(UnitCapType, "AirDefenceCap")?.GetGetMethod(true);
    internal static readonly MethodInfo? UnitCapBuilderCapGetter = AccessTools.Property(UnitCapType, "BuilderCap")?.GetGetMethod(true);
    internal static readonly MethodInfo? UnitCapAircraftCapGetter = AccessTools.Property(UnitCapType, "AircraftCap")?.GetGetMethod(true);
    internal static readonly MethodInfo? UnitCapShipCapGetter = AccessTools.Property(UnitCapType, "ShipCap")?.GetGetMethod(true);
    internal static readonly MethodInfo? UnitCapArtilleryCapGetter = AccessTools.Property(UnitCapType, "ArtilleryCap")?.GetGetMethod(true);
    internal static readonly MethodInfo? UnitCapIsFullMethod = AccessTools.Method(UnitCapType, "IsFull", new[] { typeof(FactionHQ), typeof(bool) });
    internal static readonly MethodInfo? UnitCapIsGroundFullMethod = AccessTools.Method(UnitCapType, "IsGroundFull", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? UnitCapIsAircraftFullMethod = AccessTools.Method(UnitCapType, "IsAircraftFull", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? UnitCapIsKindFullMethod = AccessTools.Method(UnitCapType, "IsKindFull", new[] { typeof(FactionHQ), typeof(UnitDefinition), typeof(bool) });
    internal static readonly MethodInfo? UnitCapIsAiTypeFullMethod = AccessTools.Method(UnitCapType, "IsAiTypeFull", new[] { typeof(FactionHQ), typeof(UnitDefinition) });
    internal static readonly MethodInfo? UnitCapFactoriesPerBaseMethod = AccessTools.Method(UnitCapType, "FactoriesPerBase", new[] { typeof(int) });
    internal static readonly MethodInfo? NavalGetRequiredDockLevelMethod = AccessTools.Method(NavalPurchaseServiceType, "GetRequiredDockLevel", new[] { typeof(ShipDefinition) });
    internal static readonly MethodInfo? NavalTryGetSeaLanePointMethod = FindMethodByNameAndParamCount(NavalPurchaseServiceType, "TryGetSeaLanePoint", 2);

    internal static readonly MethodInfo? BeginStructureBuildMethod = AccessTools.Method(EconomyServiceType, "BeginStructureBuild", new[] { typeof(BuildingDefinition) });
    internal static readonly MethodInfo? CancelBuildMethod = AccessTools.Method(EconomyServiceType, "CancelBuild", Type.EmptyTypes);
    internal static readonly PropertyInfo? AwaitingPlacementProp = AccessTools.Property(EconomyServiceType, "AwaitingPlacement");
    internal static readonly PropertyInfo? EcoStatusTextProp = AccessTools.Property(EconomyServiceType, "StatusText");
    internal static readonly PropertyInfo? EcoBlueprintsProp = AccessTools.Property(EconomyServiceType, "Blueprints");
    internal static readonly MethodInfo? GetStructureCostMethod = AccessTools.Method(EconomyServiceType, "GetStructureCost", new[] { typeof(BuildingDefinition) });
    internal static readonly MethodInfo? GetStructureLabelMethod = AccessTools.Method(EconomyServiceType, "GetStructureLabel", new[] { typeof(BuildingDefinition) });
    internal static readonly MethodInfo? SpawnBuildingMethod = AccessTools.Method(EconomyServiceType, "SpawnBuilding", new[] { typeof(FactionHQ), typeof(GlobalPosition), typeof(BuildingDefinition), typeof(string), typeof(bool), typeof(bool), typeof(Quaternion?) });
    internal static readonly MethodInfo? ReviewEnemyMethod = AccessTools.Method(EconomyServiceType, "ReviewEnemy", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? TryBuildEnemyEconomyMethod = AccessTools.Method(EconomyServiceType, "TryBuildEnemyEconomy", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? TryFindSiteNearMethod = AccessTools.Method(EconomyServiceType, "TryFindSiteNear", new[] { typeof(FactionHQ), typeof(BuildingDefinition), typeof(GlobalPosition), typeof(GlobalPosition).MakeByRefType(), typeof(int) });
    internal static readonly MethodInfo? TryFindEnemyBuildSiteMethod = AccessTools.Method(EconomyServiceType, "TryFindEnemyBuildSite", new[] { typeof(FactionHQ), typeof(BuildingDefinition), typeof(GlobalPosition).MakeByRefType() });
    internal static readonly MethodInfo? EcoTryPickEnemyBuildSiteMethod = FindMethodByNameAndParamCount(EconomyServiceType, "TryPickEnemyBuildSite", 2);
    internal static readonly MethodInfo? EcoIsIncomeBuildingUnitMethod = AccessTools.Method(EconomyServiceType, "IsIncomeBuildingUnit", new[] { typeof(Unit) });
    internal static readonly MethodInfo? EcoTryGetNavalDockPositionMethod = FindMethodByNameAndParamCount(EconomyServiceType, "TryGetNavalDockPosition", 2);
    internal static readonly MethodInfo? TryFindShoreSiteMethod = AccessTools.Method(EconomyServiceType, "TryFindShoreSite", new[] { typeof(FactionHQ), typeof(BuildingDefinition), typeof(GlobalPosition).MakeByRefType() });
    internal static readonly MethodInfo? QueueEnemyBlueprintMethod = FindMethodByNameAndParamCount(EconomyServiceType, "QueueEnemyBlueprint", 8);
    internal static readonly MethodInfo? HasAnyBlueprintRoomMethod = AccessTools.Method(EconomyServiceType, "HasAnyBlueprintRoom", new[] { typeof(FactionHQ) });
    internal static readonly PropertyInfo? SiteHqProp = AccessTools.Property(ConstructionSiteType, "Hq");
    internal static readonly PropertyInfo? SiteKindProp = AccessTools.Property(ConstructionSiteType, "Kind");
    internal static readonly PropertyInfo? SiteDefinitionProp = AccessTools.Property(ConstructionSiteType, "Definition");
    internal static readonly PropertyInfo? SiteProductionProp = AccessTools.Property(ConstructionSiteType, "Production");
    internal static readonly PropertyInfo? SitePositionProp = AccessTools.Property(ConstructionSiteType, "Site");
    internal static readonly MethodInfo? EcoIsBuilderMethod = AccessTools.Method(EconomyServiceType, "IsBuilder", new[] { typeof(Unit) });
    internal static readonly MethodInfo? EcoTryGiveStartingBuildersMethod = AccessTools.Method(EconomyServiceType, "TryGiveStartingBuilders", new[] { typeof(FactionHQ), typeof(VehicleDepot), typeof(int) });
    internal static readonly MethodInfo? EcoTrySpawnLocalBuilderMethod = FindMethodByNameAndParamCount(EconomyServiceType, "TrySpawnLocalBuilder", 1);
    internal static readonly MethodInfo? EcoEnsureEnemySiteBunkerMethod = AccessTools.Method(EconomyServiceType, "EnsureEnemySiteBunker", new[] { typeof(FactionHQ), typeof(GlobalPosition) });

    internal static readonly MethodInfo? TrySpendMethod = AccessTools.Method(GameAccessType, "TrySpend", new[] { typeof(FactionHQ), typeof(float) });
    internal static readonly MethodInfo? TryRaycastWorldPositionMethod = AccessTools.Method(GameAccessType, "TryRaycastWorldPosition", new[] { typeof(Vector2), typeof(GlobalPosition).MakeByRefType() });
    internal static readonly MethodInfo? TrySetDestinationMethod = AccessTools.Method(GameAccessType, "TrySetDestination", new[] { typeof(Unit), typeof(GlobalPosition) });
    internal static readonly MethodInfo? GetUnitConditionMethod = AccessTools.Method(GameAccessType, "GetUnitCondition", new[] { typeof(Unit) });
    internal static readonly MethodInfo? GetUnitLabelMethod = AccessTools.Method(GameAccessType, "GetUnitLabel", new[] { typeof(Unit) });

    internal static readonly PropertyInfo? AirInstanceProp = AccessTools.Property(AirCommandServiceType, "Instance");
    internal static readonly PropertyInfo? AirSelectedOptionProp = AccessTools.Property(AirCommandServiceType, "SelectedOption");
    internal static readonly PropertyInfo? AirMissionOptionDefinitionProp = AccessTools.Property(AirMissionOptionType, "Definition");
    internal static readonly MethodInfo? IsRotaryAirframeMethod = AccessTools.Method(AirCommandServiceType, "IsRotaryAirframe", new[] { typeof(AircraftDefinition) });
    internal static readonly MethodInfo? BuildLaunchWarningMethod = AccessTools.Method(AirCommandServiceType, "BuildLaunchWarning", Type.EmptyTypes);

    internal static readonly MethodInfo? IsPlacementArmedMethod = AccessTools.Method(InputControllerType, "IsPlacementArmed", Type.EmptyTypes);
    internal static readonly MethodInfo? TryHandlePlacementClickMethod = AccessTools.Method(InputControllerType, "TryHandlePlacementClick", new[] { typeof(Vector2), typeof(bool) });

    // Milestone 5 Sling-Lift & Air Command targets
    internal static readonly PropertyInfo? SlingLiftInstanceProp = AccessTools.Property(SlingLiftServiceType, "Instance");
    internal static readonly MethodInfo? SlingCanLiftMethod = AccessTools.Method(SlingLiftServiceType, "CanLift", new[] { typeof(Unit) });
    internal static readonly MethodInfo? SlingIsLiftingMethod = AccessTools.Method(SlingLiftServiceType, "IsLifting", new[] { typeof(Aircraft) });
    internal static readonly MethodInfo? AirSetAircraftOrderMethod = FindMethodByNameAndParamCount(AirCommandServiceType, "SetAircraftOrder", 5);
    internal static readonly MethodInfo? AirLaunchAiAircraftMethod = AccessTools.Method(AirCommandServiceType, "LaunchAiAircraft", new[] { typeof(FactionHQ), typeof(Airbase), typeof(AircraftDefinition), typeof(LiveryKey), typeof(NuclearOption.SavedMission.Loadout), typeof(float), typeof(Hangar) });
    internal static readonly PropertyInfo? AirMissionForcedTargetProp = AccessTools.Property(AirMissionType, "ForcedTarget");
    internal static readonly MethodInfo? AirPickForcedStationMethod = AirMissionType != null
        ? AccessTools.Method(AirCommandServiceType, "PickForcedStation", new[] { typeof(Aircraft), typeof(List<WeaponStation>), AirMissionType })
        : null;
    internal static readonly MethodInfo? AirAddSwarmSpreadMethod = AccessTools.Method(AirCommandServiceType, "AddSwarmSpread", new[] { typeof(Aircraft), typeof(WeaponInfo), typeof(List<Unit>), typeof(int) });
    internal static readonly MethodInfo? AirIsWeaponEligibleMethod = AirCommandModeType != null
        ? AccessTools.Method(AirCommandServiceType, "IsWeaponEligible", new[] { typeof(WeaponInfo), AirCommandModeType })
        : null;
    internal static readonly MethodInfo? AirCanDefeatArmorMethod = AccessTools.Method(AirCommandServiceType, "CanDefeatArmor", new[] { typeof(WeaponInfo), typeof(Unit) });
    internal static readonly MethodInfo? AirIsPlayerMissionMethod = AirMissionType != null
        ? AccessTools.Method(AirCommandServiceType, "IsPlayerMission", new[] { AirMissionType })
        : null;
    internal static readonly MethodInfo? AirSetAiCommittedMethod = AccessTools.Method(AirCommandServiceType, "SetAiCommitted", new[] { typeof(Aircraft), typeof(bool) });
    internal static readonly MethodInfo? AirSetAiRouteMethod = AccessTools.Method(AirCommandServiceType, "SetAiRoute", new[] { typeof(Aircraft), typeof(GlobalPosition) });
    internal static readonly MethodInfo? AirClearAiRouteMethod = AccessTools.Method(AirCommandServiceType, "ClearAiRoute", new[] { typeof(Aircraft) });
    internal static readonly MethodInfo? AirTrySetAiForcedTargetMethod = AccessTools.Method(AirCommandServiceType, "TrySetAiForcedTarget", new[] { typeof(Aircraft), typeof(Unit) });
    internal static readonly MethodInfo? AirGetForcedTargetMethod = AccessTools.Method(AirCommandServiceType, "GetForcedTarget", new[] { typeof(Aircraft) });
    internal static readonly MethodInfo? AirRetargetAiMissionMethod = FindMethodByNameAndParamCount(AirCommandServiceType, "RetargetAiMission", 4);
    internal static readonly MethodInfo? AirHasBombAmmoMethod = AccessTools.Method(AirCommandServiceType, "HasBombAmmo", new[] { typeof(Aircraft), typeof(bool) });
    internal static readonly MethodInfo? AirHasPlanePilotMethod = AccessTools.Method(AirCommandServiceType, "HasPlanePilot", new[] { typeof(Aircraft) });
    internal static readonly MethodInfo? AirHasRotaryPilotMethod = AccessTools.Method(AirCommandServiceType, "HasRotaryPilot", new[] { typeof(Aircraft) });
    internal static readonly MethodInfo? AirIsAradWeaponMethod = AccessTools.Method(AirCommandServiceType, "IsAradWeapon", new[] { typeof(WeaponInfo) });
    internal static readonly MethodInfo? AirCarriesStrategicStoreMethod = AccessTools.Method(AirCommandServiceType, "CarriesStrategicStore", new[] { typeof(Aircraft), typeof(bool) });
    internal static readonly MethodInfo? AirHasTakeoffRunwayMethod = AccessTools.Method(AirCommandServiceType, "HasTakeoffRunway", new[] { typeof(Airbase), typeof(AircraftDefinition), typeof(bool) });
    internal static readonly int AirCommandModeAradValue = AirCommandModeType != null ? Convert.ToInt32(Enum.Parse(AirCommandModeType, "Arad")) : 3;
    internal static readonly FieldInfo? AirMissionsField = AccessTools.Field(AirCommandServiceType, "missions");
    internal static readonly PropertyInfo? AirMissionModeProp = AccessTools.Property(AirMissionType, "Mode");
    internal static readonly PropertyInfo? AirMissionReturningProp = AccessTools.Property(AirMissionType, "Returning");
    internal static readonly PropertyInfo? AirMissionLandingBaseProp = AccessTools.Property(AirMissionType, "LandingBase");
    internal static readonly MethodInfo? TerrainRouteIsDrivableMethod = AccessTools.Method(TerrainRouteServiceType, "IsDrivable", new[] { typeof(GlobalPosition), typeof(GlobalPosition) });
    internal static readonly MethodInfo? BuildPreviewIsOnLevelRoadMethod = AccessTools.Method(BuildPreviewType, "IsOnLevelRoad", new[] { typeof(GlobalPosition), typeof(float) });
    internal static readonly MethodInfo? FireNowCanHurtMethod = AccessTools.Method(FireNowType, "CanHurt", new[] { typeof(WeaponStation), typeof(Unit) });
    internal static readonly MethodInfo? FireNowIsSensorStoreMethod = AccessTools.Method(FireNowType, "IsSensorStore", new[] { typeof(WeaponInfo) });

    // Milestone 6 Map / Champions start-base & Alert targets
    internal static readonly MethodInfo? TickChampionsStartMethod = AccessTools.Method(EconomyServiceType, "TickChampionsStart", Type.EmptyTypes);
    internal static readonly PropertyInfo? EcoChampionsDealtProp = AccessTools.Property(EconomyServiceType, "ChampionsDealt");
    internal static readonly FieldInfo? EcoChampionsStartBasesField = AccessTools.Field(EconomyServiceType, "ChampionsStartBases");
    internal static readonly FieldInfo? EcoGodStartBasesField = AccessTools.Field(EconomyServiceType, "GodStartBases");
    internal static readonly FieldInfo? EcoChampionKitKeysField = AccessTools.Field(EconomyServiceType, "ChampionKitKeys");
    internal static readonly MethodInfo? EcoDealRadarStationsMethod = AccessTools.Method(EconomyServiceType, "DealRadarStations", new[] { typeof(GlobalPosition), typeof(FactionHQ), typeof(FactionHQ) });
    internal static readonly MethodInfo? EcoFindBuildingMethod = AccessTools.Method(EconomyServiceType, "FindBuilding", new[] { typeof(Encyclopedia), typeof(string[]) });
    internal static readonly MethodInfo? EcoTryPlaceKitBuildingMethod = AccessTools.Method(EconomyServiceType, "TryPlaceKitBuilding", new[] { typeof(FactionHQ), typeof(BuildingDefinition), typeof(GlobalPosition), typeof(bool).MakeByRefType() });
    internal static readonly MethodInfo? EcoGiveEnemyBasesDepotsMethod = AccessTools.Method(EconomyServiceType, "GiveEnemyBasesDepots", new[] { typeof(FactionHQ) });
    internal static readonly PropertyInfo? IsLeviathanMissionProp = AccessTools.Property(LeviathanServiceType, "IsLeviathanMission");
    internal static readonly MethodInfo? IsGodBudgetMethod = AccessTools.Method(GodServiceType, "IsGodBudget", Type.EmptyTypes);
    internal static readonly PropertyInfo? AlertInstanceProp = AccessTools.Property(AlertServiceType, "Instance");
    internal static readonly MethodInfo? AlertNotifyCaptureMethod = AccessTools.Method(AlertServiceType, "NotifyCapture", new[] { typeof(string) });

    // Native BUILD UI, SupplyHeli Air Transport & Selection targets
    internal static readonly MethodInfo? EcoCatalogGetter = AccessTools.Property(EconomyServiceType, "Catalog")?.GetGetMethod(true);
    internal static readonly MethodInfo? EcoStatusTextSetter = AccessTools.Property(EconomyServiceType, "StatusText")?.GetSetMethod(true);
    internal static readonly MethodInfo? EcoUiDrawCatalogMethod = AccessTools.Method(EconomyUiType, "DrawCatalog", new[] { typeof(float), typeof(float), typeof(bool) });
    internal static readonly MethodInfo? EcoUiDrawWindowMethod = AccessTools.Method(EconomyUiType, "DrawWindow", new[] { typeof(int) });
    internal static readonly FieldInfo? EcoUiWindowRectField = AccessTools.Field(EconomyUiType, "windowRect");
    internal static readonly MethodInfo? SupplyHeliRefreshOptionsMethod = AccessTools.Method(SupplyHeliServiceType, "RefreshOptions", Type.EmptyTypes);
    internal static readonly MethodInfo? SupplyHeliGetCargoMountsMethod = AccessTools.Method(SupplyHeliServiceType, "GetCargoMounts", new[] { typeof(HardpointSet) });
    internal static readonly MethodInfo? SupplyHeliGetCargoMountLabelMethod = AccessTools.Method(SupplyHeliServiceType, "GetCargoMountLabel", new[] { typeof(WeaponMount) });
    internal static readonly MethodInfo? SupplyHeliGetDefaultCargoUnitCostMethod =
        AccessTools.Method(SupplyHeliServiceType, "GetDefaultCargoUnitCost", new[] { typeof(UnitDefinition) });
    internal static readonly MethodInfo? SupplyHeliTryGetAmmunitionCargoCapacityMethod =
        AccessTools.Method(SupplyHeliServiceType, "TryGetAmmunitionCargoCapacity", new[] { typeof(WeaponMount), typeof(float).MakeByRefType() });
    internal static readonly PropertyInfo? SelectionInstanceProp = AccessTools.Property(SelectionServiceType, "Instance");
    internal static readonly PropertyInfo? SelectionFocusedProp = AccessTools.Property(SelectionServiceType, "FocusedSelection");

    // Native theme, window scaling, previews, overlay click guard & cheat hooks
    internal static readonly MethodInfo? UiThemeEnsureMethod = AccessTools.Method(UiThemeType, "Ensure", Type.EmptyTypes);
    internal static readonly MethodInfo? UiThemeDrawSectionMethod = AccessTools.Method(UiThemeType, "DrawSection", new[] { typeof(Rect), typeof(string) });
    internal static readonly MethodInfo? UiThemeDrawBarMethod = AccessTools.Method(UiThemeType, "DrawBar", new[] { typeof(Rect), typeof(float), typeof(Color) });
    internal static readonly MethodInfo? UiThemeDrawAccentLineMethod = AccessTools.Method(UiThemeType, "DrawAccentLine", new[] { typeof(Rect) });
    internal static readonly MethodInfo? UiThemeDrawRuleMethod = AccessTools.Method(UiThemeType, "DrawRule", new[] { typeof(Rect), typeof(float) });
    internal static readonly MethodInfo? UiThemeDrawHelpButtonMethod = AccessTools.Method(UiThemeType, "DrawHelpButton", new[] { typeof(float), typeof(bool).MakeByRefType() });
    internal static readonly MethodInfo? UiThemeDrawHelpOverlayMethod = AccessTools.Method(UiThemeType, "DrawHelpOverlay", new[] { typeof(Rect), typeof(string) });
    internal static readonly MethodInfo? UiThemeCheckMethod = AccessTools.Method(UiThemeType, "Check", new[] { typeof(Rect), typeof(bool), typeof(string), typeof(GUIStyle) });
    internal static readonly MethodInfo? UiThemeStatusColorMethod = AccessTools.Method(UiThemeType, "StatusColor", new[] { typeof(float) });

    internal static readonly PropertyInfo? UiThemeWindowProp = AccessTools.Property(UiThemeType, "Window");
    internal static readonly PropertyInfo? UiThemePanelProp = AccessTools.Property(UiThemeType, "Panel");
    internal static readonly PropertyInfo? UiThemeInsetProp = AccessTools.Property(UiThemeType, "Inset");
    internal static readonly PropertyInfo? UiThemeButtonProp = AccessTools.Property(UiThemeType, "Button");
    internal static readonly PropertyInfo? UiThemePrimaryButtonProp = AccessTools.Property(UiThemeType, "PrimaryButton");
    internal static readonly PropertyInfo? UiThemeDangerButtonProp = AccessTools.Property(UiThemeType, "DangerButton");
    internal static readonly PropertyInfo? UiThemeSelectedButtonProp = AccessTools.Property(UiThemeType, "SelectedButton");
    internal static readonly PropertyInfo? UiThemeChromeButtonProp = AccessTools.Property(UiThemeType, "ChromeButton");
    internal static readonly PropertyInfo? UiThemeLabelProp = AccessTools.Property(UiThemeType, "Label");
    internal static readonly PropertyInfo? UiThemeMutedLabelProp = AccessTools.Property(UiThemeType, "MutedLabel");
    internal static readonly PropertyInfo? UiThemeWarningLabelProp = AccessTools.Property(UiThemeType, "WarningLabel");
    internal static readonly PropertyInfo? UiThemeHeaderProp = AccessTools.Property(UiThemeType, "Header");
    internal static readonly PropertyInfo? UiThemeCaptionProp = AccessTools.Property(UiThemeType, "Caption");
    internal static readonly PropertyInfo? UiThemeFigureProp = AccessTools.Property(UiThemeType, "Figure");
    internal static readonly PropertyInfo? UiThemeToggleProp = AccessTools.Property(UiThemeType, "Toggle");
    internal static readonly PropertyInfo? UiThemeAlertProp = AccessTools.Property(UiThemeType, "Alert");
    internal static readonly PropertyInfo? UiThemeToastProp = AccessTools.Property(UiThemeType, "Toast");
    internal static readonly PropertyInfo? UiThemeSkinProp = AccessTools.Property(UiThemeType, "Skin");
    internal static readonly PropertyInfo? UiThemeAccentProp = AccessTools.Property(UiThemeType, "Accent");

    internal static readonly MethodInfo? UiScaleWidthGetter = AccessTools.Property(UiScaleType, "Width")?.GetGetMethod(true);
    internal static readonly MethodInfo? UiScaleHeightGetter = AccessTools.Property(UiScaleType, "Height")?.GetGetMethod(true);
    internal static readonly MethodInfo? UiScaleScaleGetter = AccessTools.Property(UiScaleType, "Scale")?.GetGetMethod(true);
    internal static readonly MethodInfo? UiScaleBeginMethod = AccessTools.Method(UiScaleType, "Begin", Type.EmptyTypes);
    internal static readonly MethodInfo? UiScaleEndMethod = AccessTools.Method(UiScaleType, "End", new[] { typeof(Matrix4x4) });
    internal static readonly MethodInfo? UiScaleScreenToGuiMethod = AccessTools.Method(UiScaleType, "ScreenToGui", new[] { typeof(Vector2) });

    internal static readonly MethodInfo? WindowScaleWindowMethod = AccessTools.Method(WindowScaleType, "Window", new[] { typeof(int), typeof(Rect), typeof(GUI.WindowFunction), typeof(string) });
    internal static readonly MethodInfo? WindowScaleVisualMethod = AccessTools.Method(WindowScaleType, "Visual", new[] { typeof(int), typeof(Rect) });
    internal static readonly MethodInfo? WindowScaleClampMethod = AccessTools.Method(WindowScaleType, "Clamp", new[] { typeof(int), typeof(Rect) });

    internal static readonly MethodInfo? UnitPreviewGetPartsMethod = AccessTools.Method(UnitPreviewType, "GetParts", new[] { typeof(UnitDefinition) });
    internal static readonly MethodInfo? UnitPreviewDrawStillMethod = AccessTools.Method(UnitPreviewType, "DrawStill", new[] { typeof(Rect), typeof(UnitDefinition) });
    internal static readonly FieldInfo? UnitPreviewPartsField = AccessTools.Field(UnitPreviewType, "Parts");
    internal static readonly ConstructorInfo? UnitPreviewPartCtor = UnitPreviewPartType?.GetConstructor(AnyDeclared, null, new[] { typeof(Mesh), typeof(Material[]), typeof(Matrix4x4) }, null);

    internal static readonly MethodInfo? BuildPreviewGetPartsMethod = AccessTools.Method(BuildPreviewType, "GetParts", new[] { typeof(BuildingDefinition) });
    internal static readonly FieldInfo? BuildPreviewPartsField = AccessTools.Field(BuildPreviewType, "parts");
    internal static readonly ConstructorInfo? BuildPreviewPartCtor = BuildPreviewPartType?.GetConstructor(AnyDeclared, null, new[] { typeof(Mesh), typeof(Material[]), typeof(Matrix4x4) }, null);

    internal static readonly MethodInfo? OverlayContainsScreenPointMethod = AccessTools.Method(OverlayUiType, "ContainsScreenPoint", new[] { typeof(Vector2) });
    internal static readonly MethodInfo? OverlayDrawOrderRowMethod = AccessTools.Method(OverlayUiType, "DrawOrderRow", new[] { typeof(bool) });
    internal static readonly MethodInfo? OverlayTryGetUnitSystemsTargetMethod = AccessTools.Method(OverlayUiType, "TryGetUnitSystemsTarget", new[] { typeof(Unit).MakeByRefType() });
    internal static readonly MethodInfo? OverlayDrawRadarWindowMethod = AccessTools.Method(OverlayUiType, "DrawRadarWindow", new[] { typeof(int) });
    internal static readonly MethodInfo? OverlayRefreshLoadoutMethod = AccessTools.Method(OverlayUiType, "RefreshLoadout", Type.EmptyTypes);
    internal static readonly MethodInfo? OverlayDrawLoadoutMethod = AccessTools.Method(OverlayUiType, "DrawLoadout", Type.EmptyTypes);
    internal static readonly PropertyInfo? OverlayInstanceProp = AccessTools.Property(OverlayUiType, "Instance");
    internal static readonly FieldInfo? OverlaySelectionBarRectField = AccessTools.Field(OverlayUiType, "selectionBarRect");
    internal static readonly FieldInfo? OverlaySelectionMoreRectField = AccessTools.Field(OverlayUiType, "selectionMoreRect");
    internal static readonly FieldInfo? OverlaySelectionMoreVisibleField = AccessTools.Field(OverlayUiType, "selectionMoreVisible");
    internal static readonly FieldInfo? OverlayRadarWindowRectField = AccessTools.Field(OverlayUiType, "radarWindowRect");
    internal static readonly FieldInfo? OverlayRadarContentHeightField = AccessTools.Field(OverlayUiType, "radarContentHeight");
    internal static readonly FieldInfo? OverlayLoadoutUnitField = AccessTools.Field(OverlayUiType, "loadoutUnit");
    internal static readonly FieldInfo? OverlayLoadoutRowsField = AccessTools.Field(OverlayUiType, "loadoutRows");
    internal static readonly FieldInfo? OverlayLoadoutNamesField = AccessTools.Field(OverlayUiType, "loadoutNames");
    internal static readonly MethodInfo? GameAccessIsCommanderMarkerUnitMethod = AccessTools.Method(GameAccessType, "IsCommanderMarkerUnit", new[] { typeof(Unit), typeof(FactionHQ) });
    internal static readonly PropertyInfo? MoveInstanceProp = AccessTools.Property(MoveServiceType, "Instance");
    internal static readonly MethodInfo? MoveIsHoldingFireMethod = AccessTools.Method(MoveServiceType, "IsHoldingFire", new[] { typeof(Unit) });
    internal static readonly FieldInfo? MoveStancesField = AccessTools.Field(MoveServiceType, "stances");
    internal static readonly MethodInfo? MoveSetSelectionStanceMethod = StanceType != null ? AccessTools.Method(MoveServiceType, "SetSelectionStance", new[] { StanceType }) : null;
    internal static readonly MethodInfo? MoveApplyStanceMethod = StanceType != null ? AccessTools.Method(MoveServiceType, "ApplyStance", new[] { typeof(Unit), StanceType }) : null;
    internal static readonly MethodInfo? MoveRefreshOrderFlagsMethod = AccessTools.Method(MoveServiceType, "RefreshOrderFlags", Type.EmptyTypes);
    internal static readonly MethodInfo? MoveSelectionStanceGetter = AccessTools.Property(MoveServiceType, "SelectionStance")?.GetGetMethod(true);
    internal static readonly MethodInfo? MoveHasCommandableSelectionGetter = AccessTools.Property(MoveServiceType, "HasCommandableSelection")?.GetGetMethod(true);
    internal static readonly MethodInfo? MoveHasSelectedAircraftGetter = AccessTools.Property(MoveServiceType, "HasSelectedAircraft")?.GetGetMethod(true);
    internal static readonly MethodInfo? MoveCycleSelectionStanceMethod = AccessTools.Method(MoveServiceType, "CycleSelectionStance", Type.EmptyTypes);
    internal static readonly MethodInfo? RadarIsRadarGuidedLauncherMethod = AccessTools.Method(CommanderRadarServiceType, "IsRadarGuidedLauncher", new[] { typeof(Unit) });
    internal static readonly MethodInfo? RadarGetLauncherLinkShortfallMethod = AccessTools.Method(CommanderRadarServiceType, "GetLauncherLinkShortfall", new[] { typeof(Unit) });
    internal static readonly PropertyInfo? MobileEmplacementInstanceProp = AccessTools.Property(MobileEmplacementServiceType, "Instance");
    internal static readonly MethodInfo? MobileEmplacementIsMoveableTrailerMethod = AccessTools.Method(MobileEmplacementServiceType, "IsMoveableTrailer", new[] { typeof(Unit) });
    internal static readonly MethodInfo? MobileEmplacementIsTowingHaulerMethod = AccessTools.Method(MobileEmplacementServiceType, "IsTowingHauler", new[] { typeof(Unit) });
    internal static readonly MethodInfo? MobileEmplacementIsReservedHaulerMethod = AccessTools.Method(MobileEmplacementServiceType, "IsReservedHauler", new[] { typeof(Unit) });
    internal static readonly MethodInfo? MobileEmplacementIsInTowJobMethod = AccessTools.Method(MobileEmplacementServiceType, "IsInTowJob", new[] { typeof(Unit), typeof(bool).MakeByRefType() });
    internal static readonly MethodInfo? MobileEmplacementHasAvailableHaulerMethod = AccessTools.Method(MobileEmplacementServiceType, "HasAvailableHauler", new[] { typeof(Unit) });
    internal static readonly MethodInfo? MobileEmplacementBeginHitchMethod = AccessTools.Method(MobileEmplacementServiceType, "BeginHitch", Type.EmptyTypes);
    internal static readonly MethodInfo? MobileEmplacementDropTrailerMethod = AccessTools.Method(MobileEmplacementServiceType, "DropTrailer", new[] { typeof(Unit) });
    internal static readonly MethodInfo? MobileEmplacementSetStatusMethod = AccessTools.Method(MobileEmplacementServiceType, "SetStatus", new[] { typeof(string) });
    internal static readonly MethodInfo? MobileEmplacementIsTowedByMethod = AccessTools.Method(MobileEmplacementServiceType, "IsTowedBy", new[] { typeof(GroundVehicle), typeof(Transform) });
    internal static readonly MethodInfo? GameAccessSetUnitHoldPositionMethod = AccessTools.Method(GameAccessType, "SetUnitHoldPosition", new[] { typeof(Unit), typeof(bool) });
    internal static readonly MethodInfo? RepairSendRepairCrewMethod = AccessTools.Method(RepairServiceType, "SendRepairCrew", new[] { typeof(Unit) });
    internal static readonly MethodInfo? RepairHasCrewAssignedMethod = AccessTools.Method(RepairServiceType, "HasCrewAssigned", new[] { typeof(Unit) });
    internal static readonly FieldInfo? EcoBuiltUnitsField = AccessTools.Field(EconomyServiceType, "builtUnits");
    internal static readonly PropertyInfo? SelectionSelectedUnitsProp = AccessTools.Property(SelectionServiceType, "SelectedUnits");
    internal static readonly MethodInfo? EcoTickConstructionMethod = AccessTools.Method(EconomyServiceType, "TickConstruction", Type.EmptyTypes);
    internal static readonly FieldInfo? EcoConstructionSitesField = AccessTools.Field(EconomyServiceType, "constructionSites");
    internal static readonly PropertyInfo? SiteProgressProp = AccessTools.Property(ConstructionSiteType, "Progress");
    internal static readonly MethodInfo? FogHidesFactionMethod = AccessTools.Method(FogServiceType, "HidesFaction", new[] { typeof(FactionHQ) });
    internal static readonly FieldInfo? TerrainRouteHeightsField = AccessTools.Field(TerrainRouteServiceType, "Heights");
    internal static readonly FieldInfo? TerrainRouteStalledCellsField = AccessTools.Field(TerrainRouteServiceType, "StalledCells");

    // Unit cap, Economy CommitBuild, and SupplyHeli cargo gating bindings
    internal static readonly MethodInfo? EconomyCommitBuildMethod = BuildKindType != null
        ? AccessTools.Method(
            EconomyServiceType,
            "CommitBuild",
            new[] { typeof(FactionHQ), BuildKindType, typeof(BuildingDefinition), typeof(UnitDefinition), typeof(GlobalPosition), typeof(Quaternion) })
        : null;
    internal static readonly MethodInfo? SupplyHeliBeginSelectedCargoRunMethod =
        AccessTools.Method(SupplyHeliServiceType, "BeginSelectedCargoRun", Type.EmptyTypes);
    internal static readonly MethodInfo? SupplyHeliTrySpawnAtWorldPointMethod =
        AccessTools.Method(SupplyHeliServiceType, "TrySpawnAtWorldPoint", new[] { typeof(Vector2) });
    internal static readonly MethodInfo? SupplyHeliSetStatusMethod =
        AccessTools.Method(SupplyHeliServiceType, "SetStatus", new[] { typeof(string) });
    internal static readonly MethodInfo? SupplyHeliCancelTargetSelectionMethod =
        AccessTools.Method(SupplyHeliServiceType, "CancelTargetSelection", new[] { typeof(bool) });
    internal static readonly PropertyInfo? SupplyHeliSelectedAircraftProp =
        AccessTools.Property(SupplyHeliServiceType, "SelectedAircraft");
    internal static readonly FieldInfo? SupplyHeliPendingTargetSelectionField =
        AccessTools.Field(SupplyHeliServiceType, "pendingTargetSelection");
    internal static readonly Type? CargoAircraftOptionType =
        AccessTools.TypeByName("GroundControlRts.CommanderSupplyHeliService+CargoAircraftOption");
    internal static readonly PropertyInfo? CargoAircraftCargoSlotsProp =
        AccessTools.Property(CargoAircraftOptionType, "CargoSlots");
    internal static readonly PropertyInfo? CargoAircraftHardpointSetsProp =
        AccessTools.Property(CargoAircraftOptionType, "HardpointSets");
    internal static readonly Type? CargoSlotOptionType =
        AccessTools.TypeByName("GroundControlRts.CommanderSupplyHeliService+CargoSlotOption");
    internal static readonly PropertyInfo? CargoSlotSelectedMountProp =
        AccessTools.Property(CargoSlotOptionType, "SelectedMount");
    internal static readonly PropertyInfo? CargoSlotHardpointIndexProp =
        AccessTools.Property(CargoSlotOptionType, "HardpointIndex");
    internal static readonly MethodInfo? CargoSlotSelectMethod =
        AccessTools.Method(CargoSlotOptionType, "Select", new[] { typeof(int) });
    internal static readonly FieldInfo? CargoSlotSelectedIndexField =
        AccessTools.Field(CargoSlotOptionType, "selectedIndex");
    internal static readonly Type? PendingTargetSelectionType =
        AccessTools.TypeByName("GroundControlRts.CommanderSupplyHeliService+PendingTargetSelection");
    internal static readonly PropertyInfo? PendingTargetSelectionAircraftProp =
        AccessTools.Property(PendingTargetSelectionType, "Aircraft");
    internal static readonly PropertyInfo? PendingTargetSelectionLoadoutProp =
        AccessTools.Property(PendingTargetSelectionType, "Loadout");

    // SupplyStore, Network, Economy PriceOf, GameAccess ammo, and SupplyHeli dispatch bindings
    internal static readonly PropertyInfo? SupplyStoreInstanceProp = AccessTools.Property(SupplyStoreServiceType, "Instance");
    internal static readonly MethodInfo? SupplyStoreGetAmmoFractionMethod = AccessTools.Method(SupplyStoreServiceType, "GetAmmoFraction", new[] { typeof(Unit) });
    internal static readonly MethodInfo? SupplyStoreGetRearmerMethod = AccessTools.Method(SupplyStoreServiceType, "GetRearmer", new[] { typeof(Unit) });
    internal static readonly MethodInfo? SupplyStoreRefillTruckMethod = AccessTools.Method(SupplyStoreServiceType, "RefillTruck", new[] { typeof(Unit), typeof(Unit) });
    internal static readonly MethodInfo? SupplyStoreTopUpWeaponsMethod = AccessTools.Method(SupplyStoreServiceType, "TopUpWeapons", new[] { typeof(Rearmer), typeof(Unit) });
    internal static readonly MethodInfo? SupplyStoreCollectStoresMethod = AccessTools.Method(SupplyStoreServiceType, "CollectStores", new[] { typeof(FactionHQ), typeof(List<Unit>) });
    internal static readonly MethodInfo? SupplyStoreIsDumpMethod = AccessTools.Method(SupplyStoreServiceType, "IsDump", new[] { typeof(Unit) });
    internal static readonly MethodInfo? EcoPriceOfMethod = AccessTools.Method(EconomyServiceType, "PriceOf", new[] { typeof(UnitDefinition) });
    internal static readonly MethodInfo? GameAccessGetUnitAmmoMethod = AccessTools.Method(GameAccessType, "GetUnitAmmo", new[] { typeof(Unit) });
    internal static readonly MethodInfo? GameAccessSendDestinationMethod = AccessTools.Method(GameAccessType, "SendDestination", new[] { typeof(Unit), typeof(GlobalPosition), typeof(bool) });
    internal static readonly MethodInfo? NetworkRunAsMethod = AccessTools.Method(NetworkType, "RunAs", new[] { typeof(FactionHQ), typeof(Action) });
    internal static readonly MethodInfo? NetworkRunAsHostTeamMethod = AccessTools.Method(NetworkType, "RunAsHostTeam", new[] { typeof(Action) });
    internal static readonly MethodInfo? SupplyHeliHostCargoRunMethod = AccessTools.Method(SupplyHeliServiceType, "HostCargoRun", new[] { typeof(FactionHQ), typeof(string[]) });
    internal static readonly MethodInfo? SupplyHeliTryProcessQueuedMethod = AccessTools.Method(SupplyHeliServiceType, "TryProcessQueuedCargoSpawns", Type.EmptyTypes);
    internal static readonly PropertyInfo? SupplyHeliInstanceProp = AccessTools.Property(SupplyHeliServiceType, "Instance");
    internal static readonly FieldInfo? SupplyHeliAircraftOptionsField = AccessTools.Field(SupplyHeliServiceType, "aircraftOptions");
    internal static readonly FieldInfo? SupplyHeliAssignedMissionsField = AccessTools.Field(SupplyHeliServiceType, "assignedMissions");
    internal static readonly FieldInfo? SupplyHeliPendingSpawnsField = AccessTools.Field(SupplyHeliServiceType, "pendingAircraftSpawns");
    internal static readonly FieldInfo? SupplyHeliQueuedSpawnsField = AccessTools.Field(SupplyHeliServiceType, "queuedCargoSpawns");
    internal static readonly PropertyInfo? SupplyHeliSelectedAircraftIndexProp = AccessTools.Property(SupplyHeliServiceType, "SelectedAircraftIndex");
    internal static readonly PropertyInfo? SupplyHeliSelectedAirbaseProp = AccessTools.Property(SupplyHeliServiceType, "SelectedAirbase");
    internal static readonly MethodInfo? SupplyHeliSelectAircraftMethod = AccessTools.Method(SupplyHeliServiceType, "SelectAircraft", new[] { typeof(int) });
    internal static readonly FieldInfo? SupplyHeliStatusTextField = AccessTools.Field(SupplyHeliServiceType, "statusText");
    internal static readonly FieldInfo? SupplyHeliAirdropDeliveryField = AccessTools.Field(SupplyHeliServiceType, "airdropDelivery");
    internal static readonly PropertyInfo? SupplyHeliAwaitingTargetSelectionProp = AccessTools.Property(SupplyHeliServiceType, "AwaitingTargetSelection");
    internal static readonly MethodInfo? SupplyHeliCreateEmptyLoadoutMethod = AccessTools.Method(SupplyHeliServiceType, "CreateEmptyLoadout", new[] { typeof(int) });
    internal static readonly MethodInfo? SupplyHeliPlaceCargoMethod = AccessTools.Method(SupplyHeliServiceType, "PlaceCargoAndClearNonCargo", new[] { typeof(NuclearOption.SavedMission.Loadout), typeof(HardpointSet[]), typeof(int), typeof(WeaponMount) });
    internal static readonly MethodInfo? SupplyHeliGetCargoLabelMethod = AccessTools.Method(SupplyHeliServiceType, "GetCargoLabel", new[] { typeof(WeaponMount), typeof(string) });
    internal static readonly MethodInfo? SupplyHeliGetCargoCostMethod = AccessTools.Method(SupplyHeliServiceType, "GetCargoCost", new[] { typeof(AircraftDefinition), typeof(NuclearOption.SavedMission.Loadout) });
    internal static readonly MethodInfo? SupplyHeliIsAvailableAirbaseMethod = AccessTools.Method(SupplyHeliServiceType, "IsAvailableAirbase", new[] { typeof(Airbase), typeof(FactionHQ), typeof(AircraftDefinition) });
    internal static readonly MethodInfo? SupplyHeliSpawnCargoRunMethod = FindMethodByNameAndParamCount(SupplyHeliServiceType, "SpawnCargoRun", 10);
    internal static readonly MethodInfo? SupplyHeliCargoMountSupportsAirdropMethod = AccessTools.Method(SupplyHeliServiceType, "CargoMountSupportsAirdrop", new[] { typeof(WeaponMount) });
    internal static readonly MethodInfo? SupplyHeliTrySpawnCargoRunAtAirbaseMethod = SupplyHeliQueuedSpawnType != null
        ? AccessTools.Method(SupplyHeliServiceType, "TrySpawnCargoRunAtAirbase", new[] { SupplyHeliQueuedSpawnType, typeof(Airbase), typeof(FactionHQ) })
        : null;
    internal static readonly MethodInfo? SlingEndJobMethod = SlingJobType != null
        ? AccessTools.Method(SlingLiftServiceType, "EndJob", new[] { SlingJobType })
        : null;
    internal static readonly PropertyInfo? SlingJobHeloProp = AccessTools.Property(SlingJobType, "Helo");
    internal static readonly PropertyInfo? SlingJobLoadProp = AccessTools.Property(SlingJobType, "Load");
    internal static readonly PropertyInfo? SlingJobCarryingProp = AccessTools.Property(SlingJobType, "Carrying");
    internal static readonly PropertyInfo? SlingJobDropProp = AccessTools.Property(SlingJobType, "Drop");
    internal static readonly PropertyInfo? CargoAircraftDefProp = AccessTools.Property(CargoAircraftOptionType, "Definition");
    internal static readonly PropertyInfo? CargoAircraftLabelProp = AccessTools.Property(CargoAircraftOptionType, "Label");
    internal static readonly PropertyInfo? CargoAircraftRotaryProp = AccessTools.Property(CargoAircraftOptionType, "Rotary");
    internal static readonly PropertyInfo? CargoSlotMountsProp = AccessTools.Property(CargoSlotOptionType, "Mounts");
    internal static readonly PropertyInfo? CargoMissionHqProp = AccessTools.Property(SupplyHeliCargoMissionType, "Hq");
    internal static readonly PropertyInfo? CargoMissionTargetProp = AccessTools.Property(SupplyHeliCargoMissionType, "Target");
    internal static readonly PropertyInfo? CargoMissionTargetOverrideProp = AccessTools.Property(SupplyHeliCargoMissionType, "TargetOverrideActive");
    internal static readonly PropertyInfo? PendingSpawnHqProp = AccessTools.Property(SupplyHeliPendingSpawnType, "Hq");
    internal static readonly PropertyInfo? PendingSpawnTargetProp = AccessTools.Property(SupplyHeliPendingSpawnType, "Target");
    internal static readonly PropertyInfo? QueuedSpawnHqProp = AccessTools.Property(SupplyHeliQueuedSpawnType, "Hq");
    internal static readonly PropertyInfo? QueuedSpawnTargetProp = AccessTools.Property(SupplyHeliQueuedSpawnType, "Target");

    private static readonly object? BuildKindStructureValue = BuildKindType != null ? Enum.Parse(BuildKindType, "Structure") : null;
    private static readonly object? BuildKindMineValue = BuildKindType != null ? Enum.Parse(BuildKindType, "Mine") : null;
    private static readonly object? BuildKindFactoryValue = BuildKindType != null ? Enum.Parse(BuildKindType, "Factory") : null;
    private static readonly object? BuildKindNavalDockValue = BuildKindType != null ? Enum.Parse(BuildKindType, "NavalDock") : null;
    private static readonly object? BuildKindGasTerminalValue = BuildKindType != null ? Enum.Parse(BuildKindType, "GasTerminal") : null;

    // Coastal outpost & long-range builder logistics bindings
    internal static readonly FieldInfo? EcoShoreSearchFailedField = AccessTools.Field(EconomyServiceType, "shoreSearchFailed");
    internal static readonly FieldInfo? EcoPreviewField = AccessTools.Field(EconomyServiceType, "preview");
    internal static readonly FieldInfo? EcoBuilderRestingField = AccessTools.Field(EconomyServiceType, "builderResting");
    internal static readonly PropertyInfo? EcoNavalDockBuildCostProp = AccessTools.Property(EconomyServiceType, "NavalDockBuildCost");
    internal static readonly MethodInfo? EcoResolveDefinitionMethod = BuildKindType != null
        ? AccessTools.Method(EconomyServiceType, "ResolveDefinition", new[] { BuildKindType })
        : null;
    internal static readonly MethodInfo? EcoGetEnemyDockCostMethod = AccessTools.Method(EconomyServiceType, "GetEnemyDockCost", new[] { typeof(FactionHQ), typeof(int) });
    internal static readonly MethodInfo? EcoTryBuildEnemyDockMethod = AccessTools.Method(EconomyServiceType, "TryBuildEnemyDock", new[] { typeof(FactionHQ), typeof(int) });
    internal static readonly MethodInfo? EcoTryBuildEnemyNavalDockMethod = AccessTools.Method(EconomyServiceType, "TryBuildEnemyNavalDock", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? EcoHasGivenUpOnDockMethod = AccessTools.Method(EconomyServiceType, "HasGivenUpOnDock", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? EcoReviewBuildersMethod = AccessTools.Method(EconomyServiceType, "ReviewBuilders", Type.EmptyTypes);
    internal static readonly MethodInfo? EcoTryGetStrandedBuilderMethod = AccessTools.Method(EconomyServiceType, "TryGetStrandedBuilder", new[] { typeof(FactionHQ), typeof(GlobalPosition).MakeByRefType(), typeof(Unit).MakeByRefType() });
    internal static readonly MethodInfo? EcoCollectIdleBuildersMethod = AccessTools.Method(EconomyServiceType, "CollectIdleBuilders", new[] { typeof(FactionHQ), typeof(List<Unit>) });
    internal static readonly MethodInfo? EcoHasBusyBuilderNearMethod = AccessTools.Method(EconomyServiceType, "HasBusyBuilderNear", new[] { typeof(FactionHQ), typeof(GlobalPosition) });
    internal static readonly MethodInfo? EcoSendBuilderMethod = FindMethodByNameAndParamCount(EconomyServiceType, "SendBuilder", 2);
    internal static readonly MethodInfo? EcoIsWaterMethod = AccessTools.Method(EconomyServiceType, "IsWater", new[] { typeof(GlobalPosition), typeof(Vector3), typeof(float) });
    internal static readonly MethodInfo? EcoIsOpenWaterMethod = AccessTools.Method(EconomyServiceType, "IsOpenWater", new[] { typeof(GlobalPosition), typeof(Vector3), typeof(float) });
    internal static readonly MethodInfo? EcoReachesSeaMethod = AccessTools.Method(EconomyServiceType, "ReachesSea", new[] { typeof(GlobalPosition) });
    internal static readonly MethodInfo? EcoCountBlueprintsMethod = FindMethodByNameAndParamCount(EconomyServiceType, "CountBlueprints", 2);
    internal static readonly MethodInfo? EcoIsNearBuiltTowerMethod = AccessTools.Method(EconomyServiceType, "IsNearBuiltTower", new[] { typeof(FactionHQ), typeof(GlobalPosition) });
    internal static readonly PropertyInfo? SiteBuildersProp = AccessTools.Property(ConstructionSiteType, "Builders");
    internal static readonly PropertyInfo? SiteNextBuilderSpawnAtProp = AccessTools.Property(ConstructionSiteType, "NextBuilderSpawnAt");
    internal static readonly MethodInfo? BuildPreviewIsSiteAllowedMethod = AccessTools.Method(BuildPreviewType, "IsSiteAllowed", new[] { typeof(BuildingDefinition), typeof(GlobalPosition), typeof(FactionHQ), typeof(string).MakeByRefType(), typeof(bool), typeof(Quaternion?) });
    internal static readonly MethodInfo? BuildPreviewIsShorelineMethod = AccessTools.Method(BuildPreviewType, "IsShoreline", new[] { typeof(GlobalPosition) });
    internal static readonly MethodInfo? BuildPreviewIsControlTowerMethod = AccessTools.Method(BuildPreviewType, "IsControlTower", new[] { typeof(BuildingDefinition) });
    internal static readonly MethodInfo? BuildPreviewIsControlTowerUnitMethod = AccessTools.Method(BuildPreviewType, "IsControlTowerUnit", new[] { typeof(Unit) });
    internal static readonly MethodInfo? SettingsBuildRadiusKmGetter = AccessTools.Property(SettingsType, "BuildRadiusKm")?.GetGetMethod(true);
    internal static readonly MethodInfo? GameAccessFindRepairVehicleDefinitionMethod = AccessTools.Method(GameAccessType, "FindRepairVehicleDefinition", new[] { typeof(FactionHQ) });
    internal static readonly MethodInfo? MoveClearOrderMethod = AccessTools.Method(MoveServiceType, "ClearOrder", new[] { typeof(Unit) });
    internal static readonly MethodInfo? AirBuildSlingLoadoutMethod = AccessTools.Method(AirCommandServiceType, "BuildSlingLoadout", new[] { typeof(AircraftDefinition) });
    internal static readonly MethodInfo? AirCanAiFlyMethod = AccessTools.Method(AirCommandServiceType, "CanAiFly", new[] { typeof(AircraftDefinition) });
    internal static readonly MethodInfo? EcoIsStuckAiBuilderMethod = FindMethodByNameAndParamCount(EconomyServiceType, "IsStuckAiBuilder", 2);
    internal static readonly MethodInfo? EcoIsSupplyTruckMethod = AccessTools.Method(EconomyServiceType, "IsSupplyTruck", new[] { typeof(VehicleDefinition) });
    internal static readonly MethodInfo? FactionVehicleCanPurchaseMethod = AccessTools.Method(FactionVehicleServiceType, "CanPurchase", new[] { typeof(VehicleDefinition), typeof(FactionHQ) });
    internal static readonly MethodInfo? GameAccessTryGetLocalVehicleDefinitionsMethod = AccessTools.Method(GameAccessType, "TryGetLocalVehicleDefinitions", new[] { typeof(List<VehicleDefinition>) });
    internal static readonly MethodInfo? SpawnServiceRefreshFactionVehiclesMethod = AccessTools.Method(SpawnServiceType, "RefreshFactionVehicleDefinitions", Type.EmptyTypes);
    internal static readonly MethodInfo? SpawnServiceRefreshVehiclesMethod = AccessTools.Method(SpawnServiceType, "RefreshVehicleDefinitions", Type.EmptyTypes);
    internal static readonly FieldInfo? SpawnServiceVehicleDefinitionsField = AccessTools.Field(SpawnServiceType, "vehicleDefinitions");
    internal static readonly FieldInfo? VictoryHomeBasesField = AccessTools.Field(VictoryServiceType, "HomeBases");
    internal static readonly MethodInfo? RepairHoldLeftBehindUnitsMethod = AccessTools.Method(RepairServiceType, "HoldLeftBehindUnits", new[] { typeof(FactionHQ), typeof(FactionHQ) });
    internal static readonly MethodInfo? RepairSweepStandbyMethod = AccessTools.Method(RepairServiceType, "SweepStandby", new[] { typeof(FactionHQ), typeof(bool) });
    internal static readonly FieldInfo? RepairGuestFactionsField = AccessTools.Field(RepairServiceType, "guestFactions");
    internal static readonly MethodInfo? EcoTickStartingBuildersMethod = AccessTools.Method(EconomyServiceType, "TickStartingBuilders", Type.EmptyTypes);
    internal static readonly MethodInfo? EcoPlaceBaseDepotsMethod = AccessTools.Method(EconomyServiceType, "PlaceBaseDepots", new[] { typeof(FactionHQ), typeof(Airbase), typeof(BuildingDefinition), typeof(BuildingDefinition), typeof(int) });
    internal static readonly FieldInfo? EcoVehicleDepotKeysField = AccessTools.Field(EconomyServiceType, "VehicleDepotKeys");

    private static bool bindingsValidated;

    internal static void ValidateAllBindings()
    {
        if (bindingsValidated)
        {
            return;
        }

        bindingsValidated = true;
        var checks = new (string Name, object? Member)[]
        {
            ("CommanderEconomyService", EconomyServiceType),
            ("CommanderEconomyService.Instance", EcoInstanceProp),
            ("CommanderEconomyService.IsBelowEnemyNetIncomeTarget", IsBelowEnemyNetIncomeTargetMethod),
            ("CommanderEconomyService.GetAiStipendPerMinute", GetAiStipendPerMinuteMethod),
            ("CommanderEconomyService.HasFreeMine", HasFreeMineMethod),
            ("CommanderEconomyService.CountFreeMines", CountFreeMinesMethod),
            ("CommanderEconomyService.CountMines(2)", CountMinesMethod),
            ("CommanderEconomyService.GetEnemyMineTarget", GetEnemyMineTargetMethod),
            ("CommanderEconomyService.TryBuildEnemyMine", TryBuildEnemyMineMethod),
            ("CommanderEconomyService.TryUpgradeEnemyIncome", TryUpgradeEnemyIncomeMethod),
            ("CommanderEconomyService.GetEnemyBuildReserve", GetEnemyBuildReserveMethod),
            ("CommanderEconomyService.GetEnemyFactoryTarget", GetEnemyFactoryTargetMethod),
            ("CommanderEconomyService.TryBuildEnemyFactory", TryBuildEnemyFactoryMethod),
            ("CommanderEconomyService.GetEnemyFactoryReserve", GetEnemyFactoryReserveMethod),
            ("CommanderEconomyService.CountFactories", CountFactoriesMethod),
            ("CommanderEconomyService.HoldsMostBases", HoldsMostBasesMethod),
            ("CommanderEconomyService.GetIncomePerMinute(FactionHQ)", GetHqIncomePerMinuteMethod),
            ("CommanderEconomyService.GetIncomePerMinute(Kind,lvl)", GetStageIncomePerMinuteMethod),
            ("CommanderEconomyService.GetIncomeKind", GetIncomeKindMethod),
            ("CommanderEconomyService.GetEnemyIncomePerMinute", GetEnemyIncomePerMinuteMethod),
            ("CommanderEconomyService.GetEnemyNetIncomePerMinute", GetEnemyNetIncomePerMinuteMethod),
            ("CommanderEconomyService.GetBestDepotLevel", GetBestDepotLevelMethod),
            ("CommanderEconomyService.GetRequiredDepotLevel", GetRequiredDepotLevelMethod),
            ("CommanderEconomyService.PayIncome", PayIncomeMethod),
            ("CommanderEconomyService.RefreshLedger", EcoRefreshLedgerMethod),
            ("CommanderEconomyService.ledgerGross", EcoLedgerGrossField),
            ("CommanderEconomyService.nextLedgerAt", EcoNextLedgerAtField),
            ("Airbase.CaptureFaction(FactionHQ)", AirbaseCaptureFactionMethod),
            ("CommanderEconomyService.ResolveCategoryDefinition", ResolveCategoryDefinitionMethod),
            ("CommanderEconomyService.GetDepotLevel", EcoGetDepotLevelMethod),
            ("CommanderEconomyService.GetDepotUpgradeCost", EcoGetDepotUpgradeCostMethod),
            ("CommanderEconomyService.CountLandBases", EcoCountLandBasesMethod),
            ("CommanderEconomyService.HasDepotUpgradeIncome", EcoHasDepotUpgradeIncomeMethod),
            ("CommanderEconomyService.TryUpgradeEnemyDepot", EcoTryUpgradeEnemyDepotMethod),
            ("CommanderEconomyService.incomeLevels", EcoIncomeLevelsField),
            ("CommanderEconomyService.ChooseEnemyProduction", EcoChooseEnemyProductionMethod),
            ("CommanderEconomyService.ChooseEnemyIncomeKind", EcoChooseEnemyIncomeKindMethod),
            ("CommanderEconomyService.FindEnemyIncomeUpgrade", EcoFindEnemyIncomeUpgradeMethod),
            ("CommanderEconomyService.GetNavalDockLevel(FactionHQ)", EcoGetNavalDockLevelHqMethod),
            ("CommanderEconomyService.GetNavalDockUpgradeCost", EcoGetNavalDockUpgradeCostMethod),
            ("CommanderEconomyService.TryUpgradeEnemyNavalDock", EcoTryUpgradeEnemyNavalDockMethod),
            ("CommanderEconomyService.GetFactoryBuildCost", EcoGetFactoryBuildCostMethod),
            ("CommanderEconomyService.IsCricket", EcoIsCricketMethod),
            ("CommanderEconomyService.enemyFactoryProducts", EcoEnemyFactoryProductsField),
            ("CommanderEconomyService.TryBuildEnemyDepot", EcoTryBuildEnemyDepotMethod),
            ("CommanderEconomyService.HasBaseWithoutUpgradedDepot", EcoHasBaseWithoutUpgradedDepotMethod),
            ("CommanderEconomyService.CountBuildings", EcoCountBuildingsMethod),
            ("CommanderEconomyService.GetEnemyIncomeReserve(2)", EcoGetEnemyIncomeReserveMethod),
            ("CommanderUnitCap.FactoriesPerBase", UnitCapFactoriesPerBaseMethod),
            ("CommanderNavalPurchaseService", NavalPurchaseServiceType),
            ("CommanderNavalPurchaseService.GetRequiredDockLevel", NavalGetRequiredDockLevelMethod),
            ("CommanderEnemyCommanderService", EnemyCommanderType),
            ("CommanderEnemyCommanderService.Instance", EnemyInstanceProp),
            ("CommanderEnemyCommanderService.TryBuySamLauncher", EnemyTryBuySamLauncherMethod),
            ("CommanderEnemyCommanderService.DropCrickets", EnemyDropCricketsMethod),
            ("CommanderEnemyCommanderService.CountOwnedAirDefence", EnemyCountOwnedAirDefenceMethod),
            ("CommanderEnemyCommanderService.airCandidates", EnemyAirCandidatesField),
            ("CommanderEnemyCommanderService.savingFor", EnemySavingForField),
            ("CommanderEnemyCommanderService.ownedCombat", EnemyOwnedCombatField),
            ("CommanderState.AirFund", StateAirFundProp),
            ("CommanderState.AirSaveCap", StateAirSaveCapProp),
            ("CommanderState.SamFund", StateSamFundProp),
            ("CommanderState.MineFund", StateMineFundProp),
            ("CommanderEnemyCommanderService.IsAiFaction", IsAiFactionMethod),
            ("CommanderEnemyCommanderService.IsChallenging", IsChallengingMethod),
            ("CommanderEnemyCommanderService.IsChampionsMission", IsChampionsMissionProp),
            ("CommanderEnemyCommanderService.IsDuelMission", IsDuelMissionProp),
            ("CommanderEnemyCommanderService.PrepareDuel", PrepareDuelMethod),
            ("CommanderEnemyCommanderService.Review", EnemyReviewMethod),
            ("CommanderEnemyCommanderService.ScalePurchases", ScalePurchasesMethod),
            ("CommanderEnemyCommanderService.Pick", PickMethod),
            ("CommanderEnemyCommanderService.Choose", ChooseMethod),
            ("CommanderEnemyCommanderService.ChooseCaptureUnit", ChooseCaptureUnitMethod),
            ("CommanderEnemyCommanderService.ChooseReconUnit", ChooseReconUnitMethod),
            ("CommanderEnemyCommanderService.CollectCatalog", CollectCatalogMethod),
            ("CommanderEnemyCommanderService.CounterWeight", CounterWeightMethod),
            ("CommanderEnemyCommanderService.ChallengingAirDefenceTarget", ChallengingAirDefenceTargetMethod),
            ("CommanderEnemyCommanderService.WantsRadarHunter", WantsRadarHunterMethod),
            ("CommanderEnemyCommanderService.FindRadarToHunt", FindRadarToHuntMethod),
            ("CommanderEnemyCommanderService.TryBuyRole", TryBuyRoleMethod),
            ("CommanderEnemyCommanderService.TaskAirWing", TaskAirWingMethod),
            ("CommanderEnemyCommanderService.IsRadarHunter", IsRadarHunterMethod),
            ("CommanderEnemyCommanderService.IsCombatVehicle", IsCombatVehicleMethod),
            ("CommanderEnemyCommanderService.IsAirDefence", IsAirDefenceMethod),
            ("CommanderEnemyCommanderService.IsRadarGuidedAirDefence", IsRadarGuidedAirDefenceMethod),
            ("CommanderEnemyCommanderService.IsBuilderVehicle", IsBuilderVehicleMethod),
            ("CommanderEnemyCommanderService.IsSupplyVehicle", IsSupplyVehicleMethod),
            ("CommanderEnemyCommanderService.CanDriveBetween", CanDriveBetweenMethod),
            ("CommanderEnemyCommanderService.ReviewDefence", EnemyReviewDefenceMethod),
            ("CommanderEnemyCommanderService.ReviewSupport", EnemyReviewSupportMethod),
            ("CommanderEnemyCommanderService.LadsWanted", EnemyLadsWantedMethod),
            ("CommanderEnemyCommanderService.IsExtraLads", EnemyIsExtraLadsMethod),
            ("CommanderEnemyCommanderService.ChooseRingGun", EnemyChooseRingGunMethod),
            ("CommanderEnemyCommanderService.ChooseRadarSam", EnemyChooseRadarSamMethod),
            ("CommanderEnemyCommanderService.ChooseSamDepot", EnemyChooseSamDepotMethod),
            ("CommanderEnemyCommanderService.ChooseSamLauncherUnit", EnemyChooseSamLauncherUnitMethod),
            ("CommanderEnemyCommanderService.SiteFillRank", EnemySiteFillRankMethod),
            ("CommanderEnemyCommanderService.SamRadarTarget", EnemySamRadarTargetMethod),
            ("CommanderEnemyCommanderService.IsAirDefenceAtMax", EnemyIsAirDefenceAtMaxMethod),
            ("CommanderEnemyCommanderService.IsAirDefenceFull", EnemyIsAirDefenceFullMethod),
            ("CommanderEnemyCommanderService.IsAirDefenceShareFull", EnemyIsAirDefenceShareFullMethod),
            ("CommanderEnemyCommanderService.IsConvoyAirDefence", EnemyIsConvoyAirDefenceMethod),
            ("CommanderEnemyCommanderService.BaseLacks", EnemyBaseLacksMethod),
            ("CommanderEnemyCommanderService.CountOwned", EnemyCountOwnedMethod),
            ("CommanderEnemyCommanderService.CountSamLaunchers", EnemyCountSamLaunchersMethod),
            ("CommanderEnemyCommanderService.CountRadarReserve", EnemyCountRadarReserveMethod),
            ("CommanderEnemyCommanderService.CanField", EnemyCanFieldMethod),
            ("CommanderEnemyCommanderService.FindSiteDepot", EnemyFindSiteDepotMethod),
            ("CommanderEnemyCommanderService.CollectBaseDepots", EnemyCollectBaseDepotsMethod),
            ("CommanderEnemyCommanderService.catalog", EnemyCatalogField),
            ("CommanderEnemyCommanderService.dearLean", EnemyDearLeanField),
            ("CommanderEnemyCommanderService.states", EnemyStatesField),
            ("CommanderEnemyCommanderService.siteLaunchers", EnemySiteLaunchersField),
            ("CommanderEnemyCommanderService.guardCentres", EnemyGuardCentresField),
            ("CommanderEnemyCommanderService.guardFields", EnemyGuardFieldsField),
            ("CommanderState.WaveOut", StateWaveOutProp),
            ("CommanderState.WaveWaitSince", StateWaveWaitSinceProp),
            ("CommanderState.WaveGoal", StateWaveGoalProp),
            ("CommanderState.SamDepot", StateSamDepotProp),
            ("CommanderState.SamFundRingTurn", StateSamFundRingTurnProp),
            ("CommanderState.WantsSamSite", StateWantsSamSiteProp),
            ("CommanderState.WantsSiteRadar", StateWantsSiteRadarProp),
            ("CommanderState.DefencePosts", StateDefencePostsProp),
            ("CommanderState.DefenceBaseCount", StateDefenceBaseCountProp),
            ("CommanderState.AirShortDepots", StateAirShortDepotsProp),
            ("CommanderState.Defenders", StateDefendersProp),
            ("CommanderState.Support", StateSupportProp),
            ("CommanderCaptureService", CaptureServiceType),
            ("CommanderCaptureService.Instance", CaptureInstanceProp),
            ("CommanderCaptureService.ReviewEnemies", ReviewEnemiesCaptureMethod),
            ("CommanderCaptureService.LaunchWaveIfFormed", LaunchWaveIfFormedMethod),
            ("CommanderCaptureService.GetLaneFormUp", GetLaneFormUpMethod),
            ("CommanderCaptureService.ChooseEnemyTarget(4)", ChooseEnemyTarget4Method),
            ("CommanderCaptureService.IsTakeable", IsTakeableMethod),
            ("CommanderCaptureService.GetOwnerGrip", GetOwnerGripMethod),
            ("CommanderCaptureService.GetAirbaseLabel", GetAirbaseLabelMethod),
            ("CommanderCaptureService.drives", CaptureDrivesField),
            ("CommanderCaptureService.squad", CaptureSquadField),
            ("EnemyDrive.Committed", DriveCommittedProp),
            ("EnemyDrive.Target", DriveTargetProp),
            ("EnemyDrive.Owner", DriveOwnerProp),
            ("CommanderSettings.EnemyDifficulty", EnemyDifficultyProp),
            ("CommanderSettings.EnemyMineIncomeMultiplier", EnemyMineIncomeMultiplierGetter),
            ("CommanderGameAccess.GetLocalHq", GetLocalHqMethod),
            ("CommanderGameAccess.SnapToTerrain", SnapToTerrainMethod),
            ("CommanderGameAccess.IsBelowSeaLevel", IsBelowSeaLevelMethod),
            ("CommanderGameAccess.IsLandBase", IsLandBaseMethod),
            ("CommanderRadarService", CommanderRadarServiceType),
            ("CommanderRadarService.ClearTargetsUsingRadar", ClearTargetsUsingRadarMethod),
            ("CommanderLog", CommanderLogType),
            ("CommanderLog.LogInfo", CommanderLogInfoMethod),
            ("CommanderEconomyService.GetFactories", GetFactoriesMethod),
            ("CommanderPlugin", CommanderPluginType),
            ("CommanderUnitCap", UnitCapType),
            ("CommanderNotice", NoticeType),
            ("CommanderRepairService", RepairServiceType),
            ("CommanderAirCommandService", AirCommandServiceType),
            ("CommanderAirCommandService+AirMissionOption", AirMissionOptionType),
            ("CommanderInputController", InputControllerType),
            ("CommanderEconomyService+ConstructionSite", ConstructionSiteType),
            ("CommanderPlugin.Instance", CommanderPluginInstanceProp),
            ("CommanderPlugin.IsCommanderModeActive", IsCommanderModeActiveProp),
            ("CommanderNotice.Fail", NoticeFailMethod),
            ("CommanderRepairService.Instance", RepairInstanceProp),
            ("CommanderRepairService.IsRepairUnit", IsRepairUnitMethod),
            ("CommanderRepairService.SendHome", SendHomeMethod),
            ("CommanderUnitCap.Enabled", UnitCapEnabledGetter),
            ("CommanderUnitCap.Cap", UnitCapCapGetter),
            ("CommanderUnitCap.GroundCap", UnitCapGroundCapGetter),
            ("CommanderUnitCap.AirDefenceCap", UnitCapAirDefenceCapGetter),
            ("CommanderUnitCap.AircraftCap", UnitCapAircraftCapGetter),
            ("CommanderUnitCap.ShipCap", UnitCapShipCapGetter),
            ("CommanderUnitCap.ArtilleryCap", UnitCapArtilleryCapGetter),
            ("CommanderUnitCap.IsFull", UnitCapIsFullMethod),
            ("CommanderUnitCap.IsGroundFull", UnitCapIsGroundFullMethod),
            ("CommanderUnitCap.IsAircraftFull", UnitCapIsAircraftFullMethod),
            ("CommanderUnitCap.IsKindFull", UnitCapIsKindFullMethod),
            ("CommanderEconomyService.BeginStructureBuild", BeginStructureBuildMethod),
            ("CommanderEconomyService.CancelBuild", CancelBuildMethod),
            ("CommanderEconomyService.AwaitingPlacement", AwaitingPlacementProp),
            ("CommanderEconomyService.StatusText", EcoStatusTextProp),
            ("CommanderEconomyService.Blueprints", EcoBlueprintsProp),
            ("CommanderEconomyService.GetStructureCost", GetStructureCostMethod),
            ("CommanderEconomyService.GetStructureLabel", GetStructureLabelMethod),
            ("CommanderEconomyService.SpawnBuilding", SpawnBuildingMethod),
            ("CommanderEconomyService.ReviewEnemy", ReviewEnemyMethod),
            ("CommanderEconomyService.TryBuildEnemyEconomy", TryBuildEnemyEconomyMethod),
            ("CommanderEconomyService.TryFindSiteNear", TryFindSiteNearMethod),
            ("CommanderEconomyService.TryFindEnemyBuildSite", TryFindEnemyBuildSiteMethod),
            ("CommanderEconomyService.TryFindShoreSite", TryFindShoreSiteMethod),
            ("CommanderEconomyService.QueueEnemyBlueprint", QueueEnemyBlueprintMethod),
            ("CommanderEconomyService.HasAnyBlueprintRoom", HasAnyBlueprintRoomMethod),
            ("CommanderEconomyService.CommitBuild", EconomyCommitBuildMethod),
            ("CommanderSupplyHeliService.BeginSelectedCargoRun", SupplyHeliBeginSelectedCargoRunMethod),
            ("CommanderSupplyHeliService.TrySpawnAtWorldPoint", SupplyHeliTrySpawnAtWorldPointMethod),
            ("CommanderSupplyHeliService.SetStatus", SupplyHeliSetStatusMethod),
            ("CommanderSupplyHeliService.CancelTargetSelection", SupplyHeliCancelTargetSelectionMethod),
            ("CommanderSupplyHeliService.SelectedAircraft", SupplyHeliSelectedAircraftProp),
            ("CommanderSupplyHeliService.pendingTargetSelection", SupplyHeliPendingTargetSelectionField),
            ("ConstructionSite.Hq", SiteHqProp),
            ("ConstructionSite.Definition", SiteDefinitionProp),
            ("CommanderGameAccess.TrySpend", TrySpendMethod),
            ("CommanderGameAccess.TryRaycastWorldPosition", TryRaycastWorldPositionMethod),
            ("CommanderGameAccess.TrySetDestination", TrySetDestinationMethod),
            ("CommanderGameAccess.GetUnitCondition", GetUnitConditionMethod),
            ("CommanderGameAccess.GetUnitLabel", GetUnitLabelMethod),
            ("CommanderAirCommandService.Instance", AirInstanceProp),
            ("CommanderAirCommandService.SelectedOption", AirSelectedOptionProp),
            ("AirMissionOption.Definition", AirMissionOptionDefinitionProp),
            ("CommanderAirCommandService.IsRotaryAirframe", IsRotaryAirframeMethod),
            ("CommanderAirCommandService.BuildLaunchWarning", BuildLaunchWarningMethod),
            ("CommanderInputController.IsPlacementArmed", IsPlacementArmedMethod),
            ("CommanderInputController.TryHandlePlacementClick", TryHandlePlacementClickMethod),
            ("CommanderSlingLiftService", SlingLiftServiceType),
            ("CommanderSlingLiftService.Instance", SlingLiftInstanceProp),
            ("CommanderSlingLiftService.CanLift", SlingCanLiftMethod),
            ("CommanderSlingLiftService.IsLifting", SlingIsLiftingMethod),
            ("CommanderAirCommandService.SetAircraftOrder", AirSetAircraftOrderMethod),
            ("CommanderAirCommandService.LaunchAiAircraft", AirLaunchAiAircraftMethod),
            ("CommanderAirCommandService+AirMission", AirMissionType),
            ("CommanderAirCommandService+AirCommandMode", AirCommandModeType),
            ("CommanderFireNow", FireNowType),
            ("AirMission.ForcedTarget", AirMissionForcedTargetProp),
            ("CommanderAirCommandService.PickForcedStation", AirPickForcedStationMethod),
            ("CommanderAirCommandService.AddSwarmSpread", AirAddSwarmSpreadMethod),
            ("CommanderAirCommandService.IsWeaponEligible", AirIsWeaponEligibleMethod),
            ("CommanderAirCommandService.CanDefeatArmor", AirCanDefeatArmorMethod),
            ("CommanderAirCommandService.IsPlayerMission", AirIsPlayerMissionMethod),
            ("CommanderFireNow.CanHurt", FireNowCanHurtMethod),
            ("CommanderFireNow.IsSensorStore", FireNowIsSensorStoreMethod),
            ("CommanderLeviathanService", LeviathanServiceType),
            ("CommanderGodService", GodServiceType),
            ("CommanderAlertService", AlertServiceType),
            ("CommanderEconomyService.TickChampionsStart", TickChampionsStartMethod),
            ("CommanderEconomyService.ChampionsDealt", EcoChampionsDealtProp),
            ("CommanderEconomyService.ChampionsStartBases", EcoChampionsStartBasesField),
            ("CommanderEconomyService.GodStartBases", EcoGodStartBasesField),
            ("CommanderEconomyService.ChampionKitKeys", EcoChampionKitKeysField),
            ("CommanderEconomyService.DealRadarStations", EcoDealRadarStationsMethod),
            ("CommanderEconomyService.FindBuilding", EcoFindBuildingMethod),
            ("CommanderEconomyService.TryPlaceKitBuilding", EcoTryPlaceKitBuildingMethod),
            ("CommanderEconomyService.GiveEnemyBasesDepots", EcoGiveEnemyBasesDepotsMethod),
            ("CommanderLeviathanService.IsLeviathanMission", IsLeviathanMissionProp),
            ("CommanderGodService.IsGodBudget", IsGodBudgetMethod),
            ("CommanderAlertService.Instance", AlertInstanceProp),
            ("CommanderAlertService.NotifyCapture", AlertNotifyCaptureMethod),
            ("CommanderEconomyUi", EconomyUiType),
            ("CommanderSupplyHeliService", SupplyHeliServiceType),
            ("CommanderSelectionService", SelectionServiceType),
            ("CommanderEconomyService.Catalog", EcoCatalogGetter),
            ("CommanderEconomyService.set_StatusText", EcoStatusTextSetter),
            ("CommanderEconomyUi.DrawCatalog", EcoUiDrawCatalogMethod),
            ("CommanderEconomyUi.DrawWindow", EcoUiDrawWindowMethod),
            ("CommanderEconomyUi.windowRect", EcoUiWindowRectField),
            ("CommanderSupplyHeliService.RefreshOptions", SupplyHeliRefreshOptionsMethod),
            ("CommanderSupplyHeliService.GetCargoMounts", SupplyHeliGetCargoMountsMethod),
            ("CommanderSupplyHeliService.GetCargoMountLabel", SupplyHeliGetCargoMountLabelMethod),
            ("CommanderSupplyHeliService.GetDefaultCargoUnitCost", SupplyHeliGetDefaultCargoUnitCostMethod),
            ("CommanderSupplyHeliService.TryGetAmmunitionCargoCapacity", SupplyHeliTryGetAmmunitionCargoCapacityMethod),
            ("CommanderSelectionService.Instance", SelectionInstanceProp),
            ("CommanderSelectionService.FocusedSelection", SelectionFocusedProp),
            ("CommanderUiTheme", UiThemeType),
            ("CommanderUiScale", UiScaleType),
            ("CommanderWindowScale", WindowScaleType),
            ("CommanderUnitPreview", UnitPreviewType),
            ("CommanderUnitPreview+Part", UnitPreviewPartType),
            ("CommanderBuildPreview", BuildPreviewType),
            ("CommanderBuildPreview+Part", BuildPreviewPartType),
            ("CommanderOverlayUi", OverlayUiType),
            ("CommanderFogOfWarService", FogServiceType),
            ("CommanderUiTheme.Ensure", UiThemeEnsureMethod),
            ("CommanderUiTheme.DrawSection", UiThemeDrawSectionMethod),
            ("CommanderUiTheme.DrawBar", UiThemeDrawBarMethod),
            ("CommanderUiTheme.DrawAccentLine", UiThemeDrawAccentLineMethod),
            ("CommanderUiTheme.DrawRule", UiThemeDrawRuleMethod),
            ("CommanderUiTheme.DrawHelpButton", UiThemeDrawHelpButtonMethod),
            ("CommanderUiTheme.DrawHelpOverlay", UiThemeDrawHelpOverlayMethod),
            ("CommanderUiTheme.Check", UiThemeCheckMethod),
            ("CommanderUiTheme.StatusColor", UiThemeStatusColorMethod),
            ("CommanderUiTheme.Window", UiThemeWindowProp),
            ("CommanderUiTheme.Panel", UiThemePanelProp),
            ("CommanderUiTheme.Inset", UiThemeInsetProp),
            ("CommanderUiTheme.Button", UiThemeButtonProp),
            ("CommanderUiTheme.PrimaryButton", UiThemePrimaryButtonProp),
            ("CommanderUiTheme.DangerButton", UiThemeDangerButtonProp),
            ("CommanderUiTheme.SelectedButton", UiThemeSelectedButtonProp),
            ("CommanderUiTheme.ChromeButton", UiThemeChromeButtonProp),
            ("CommanderUiTheme.Label", UiThemeLabelProp),
            ("CommanderUiTheme.MutedLabel", UiThemeMutedLabelProp),
            ("CommanderUiTheme.WarningLabel", UiThemeWarningLabelProp),
            ("CommanderUiTheme.Header", UiThemeHeaderProp),
            ("CommanderUiTheme.Caption", UiThemeCaptionProp),
            ("CommanderUiTheme.Figure", UiThemeFigureProp),
            ("CommanderUiTheme.Toggle", UiThemeToggleProp),
            ("CommanderUiTheme.Alert", UiThemeAlertProp),
            ("CommanderUiTheme.Toast", UiThemeToastProp),
            ("CommanderUiTheme.Skin", UiThemeSkinProp),
            ("CommanderUiTheme.Accent", UiThemeAccentProp),
            ("CommanderUiScale.Width", UiScaleWidthGetter),
            ("CommanderUiScale.Height", UiScaleHeightGetter),
            ("CommanderUiScale.Scale", UiScaleScaleGetter),
            ("CommanderUiScale.Begin", UiScaleBeginMethod),
            ("CommanderUiScale.End", UiScaleEndMethod),
            ("CommanderUiScale.ScreenToGui", UiScaleScreenToGuiMethod),
            ("CommanderWindowScale.Window", WindowScaleWindowMethod),
            ("CommanderWindowScale.Visual", WindowScaleVisualMethod),
            ("CommanderWindowScale.Clamp", WindowScaleClampMethod),
            ("CommanderUnitPreview.GetParts", UnitPreviewGetPartsMethod),
            ("CommanderUnitPreview.DrawStill", UnitPreviewDrawStillMethod),
            ("CommanderUnitPreview.Parts", UnitPreviewPartsField),
            ("CommanderBuildPreview.GetParts", BuildPreviewGetPartsMethod),
            ("CommanderBuildPreview.parts", BuildPreviewPartsField),
            ("CommanderOverlayUi.ContainsScreenPoint", OverlayContainsScreenPointMethod),
            ("CommanderEconomyService.TickConstruction", EcoTickConstructionMethod),
            ("CommanderEconomyService.constructionSites", EcoConstructionSitesField),
            ("ConstructionSite.Progress", SiteProgressProp),
            ("CommanderFogOfWarService.HidesFaction", FogHidesFactionMethod),
            ("CommanderTerrainRouteService.Heights", TerrainRouteHeightsField),
            ("CommanderTerrainRouteService.StalledCells", TerrainRouteStalledCellsField),
            ("CommanderMoveService", MoveServiceType),
            ("CommanderStance", StanceType),
            ("CommanderOverlayUi.Instance", OverlayInstanceProp),
            ("CommanderOverlayUi.selectionBarRect", OverlaySelectionBarRectField),
            ("CommanderOverlayUi.selectionMoreRect", OverlaySelectionMoreRectField),
            ("CommanderOverlayUi.selectionMoreVisible", OverlaySelectionMoreVisibleField),
            ("CommanderMoveService.Instance", MoveInstanceProp),
            ("CommanderMoveService.IsHoldingFire", MoveIsHoldingFireMethod),
            ("CommanderMoveService.stances", MoveStancesField),
            ("CommanderMoveService.SetSelectionStance", MoveSetSelectionStanceMethod),
            ("CommanderMoveService.ApplyStance", MoveApplyStanceMethod),
            ("CommanderMoveService.RefreshOrderFlags", MoveRefreshOrderFlagsMethod),
            ("CommanderRepairService.SendRepairCrew", RepairSendRepairCrewMethod),
            ("CommanderRepairService.HasCrewAssigned", RepairHasCrewAssignedMethod),
            ("CommanderEconomyService.builtUnits", EcoBuiltUnitsField),
            ("CommanderSelectionService.SelectedUnits", SelectionSelectedUnitsProp),
            ("CommanderMobileEmplacementService", MobileEmplacementServiceType),
            ("CommanderGameAccess.IsCommanderMarkerUnit", GameAccessIsCommanderMarkerUnitMethod),
            ("CommanderOverlayUi.DrawOrderRow", OverlayDrawOrderRowMethod),
            ("CommanderMoveService.get_SelectionStance", MoveSelectionStanceGetter),
            ("CommanderMoveService.get_HasCommandableSelection", MoveHasCommandableSelectionGetter),
            ("CommanderMoveService.get_HasSelectedAircraft", MoveHasSelectedAircraftGetter),
            ("CommanderMoveService.CycleSelectionStance", MoveCycleSelectionStanceMethod),
            ("CommanderRadarService.IsRadarGuidedLauncher", RadarIsRadarGuidedLauncherMethod),
            ("CommanderRadarService.GetLauncherLinkShortfall", RadarGetLauncherLinkShortfallMethod),
            ("CommanderMobileEmplacementService.Instance", MobileEmplacementInstanceProp),
            ("CommanderMobileEmplacementService.IsMoveableTrailer", MobileEmplacementIsMoveableTrailerMethod),
            ("CommanderMobileEmplacementService.IsTowingHauler", MobileEmplacementIsTowingHaulerMethod),
            ("CommanderMobileEmplacementService.IsReservedHauler", MobileEmplacementIsReservedHaulerMethod),
            ("CommanderMobileEmplacementService.IsInTowJob", MobileEmplacementIsInTowJobMethod),
            ("CommanderMobileEmplacementService.HasAvailableHauler", MobileEmplacementHasAvailableHaulerMethod),
            ("CommanderMobileEmplacementService.BeginHitch", MobileEmplacementBeginHitchMethod),
            ("CommanderMobileEmplacementService.DropTrailer", MobileEmplacementDropTrailerMethod),
            ("CommanderMobileEmplacementService.SetStatus", MobileEmplacementSetStatusMethod),
            ("CommanderEconomyService.IsHeldDefence", EcoIsHeldDefenceMethod),
            ("CommanderEconomyService.GetDefencePrice", EcoGetDefencePriceMethod),
            ("CommanderOverlayUi.TryGetUnitSystemsTarget", OverlayTryGetUnitSystemsTargetMethod),
            ("CommanderOverlayUi.DrawRadarWindow", OverlayDrawRadarWindowMethod),
            ("CommanderOverlayUi.radarWindowRect", OverlayRadarWindowRectField),
            ("CommanderOverlayUi.radarContentHeight", OverlayRadarContentHeightField),
            ("CommanderOverlayUi.RefreshLoadout", OverlayRefreshLoadoutMethod),
            ("CommanderOverlayUi.DrawLoadout", OverlayDrawLoadoutMethod),
            ("CommanderOverlayUi.loadoutUnit", OverlayLoadoutUnitField),
            ("CommanderOverlayUi.loadoutRows", OverlayLoadoutRowsField),
            ("CommanderOverlayUi.loadoutNames", OverlayLoadoutNamesField),
            ("CommanderSupplyStoreService", SupplyStoreServiceType),
            ("CommanderNetwork", NetworkType),
            ("CommanderSupplyHeliService+CargoMission", SupplyHeliCargoMissionType),
            ("CommanderSupplyHeliService+PendingAircraftSpawn", SupplyHeliPendingSpawnType),
            ("CommanderSupplyHeliService+QueuedCargoSpawn", SupplyHeliQueuedSpawnType),
            ("CommanderEnemyCommanderService.HasSiteWantingLauncher", EnemyHasSiteWantingLauncherMethod),
            ("CommanderEnemyCommanderService.ChooseSamSite", EnemyChooseSamSiteMethod),
            ("CommanderEnemyCommanderService.RecruitAirDefence", EnemyRecruitAirDefenceMethod),
            ("CommanderEnemyCommanderService.CountSiteParts", EnemyCountSitePartsMethod),
            ("CommanderEnemyCommanderService.SamSiteCount", EnemySamSiteCountMethod),
            ("CommanderEnemyCommanderService.IsSamSiteLauncher(Unit)", EnemyIsSamSiteLauncherUnitMethod),
            ("CommanderEnemyCommanderService.IsSamSiteLauncher(Def)", EnemyIsSamSiteLauncherDefMethod),
            ("CommanderEnemyCommanderService.IsRearmVehicle", EnemyIsRearmVehicleMethod),
            ("CommanderEnemyCommanderService.IsRadarVehicle", EnemyIsRadarVehicleMethod),
            ("CommanderEnemyCommanderService.GetHomeBase", EnemyGetHomeBaseMethod),
            ("CommanderEnemyCommanderService.IsPostAt", EnemyIsPostAtMethod),
            ("CommanderEnemyCommanderService.ChooseSupplyTruck", EnemyChooseSupplyTruckMethod),
            ("CommanderEnemyCommanderService.siteRadars", EnemySiteRadarsField),
            ("CommanderEnemyCommanderService.siteFireControls", EnemySiteFireControlsField),
            ("CommanderEnemyCommanderService.siteTrucks", EnemySiteTrucksField),
            ("CommanderEnemyCommanderService.rearmRuns", EnemyRearmRunsField),
            ("CommanderState.WantsGuardTruck", StateWantsGuardTruckProp),
            ("CommanderState.WantsFireControl", StateWantsFireControlProp),
            ("CommanderState.SiteRadarDepot", StateSiteRadarDepotProp),
            ("CommanderState.Slots", StateSlotsProp),
            ("CommanderState.DefencePostBases", StateDefencePostBasesProp),
            ("CommanderSupplyStoreService.Instance", SupplyStoreInstanceProp),
            ("CommanderSupplyStoreService.GetAmmoFraction", SupplyStoreGetAmmoFractionMethod),
            ("CommanderSupplyStoreService.GetRearmer", SupplyStoreGetRearmerMethod),
            ("CommanderSupplyStoreService.RefillTruck", SupplyStoreRefillTruckMethod),
            ("CommanderSupplyStoreService.TopUpWeapons", SupplyStoreTopUpWeaponsMethod),
            ("CommanderSupplyStoreService.CollectStores", SupplyStoreCollectStoresMethod),
            ("CommanderSupplyStoreService.IsDump", SupplyStoreIsDumpMethod),
            ("CommanderEconomyService.PriceOf", EcoPriceOfMethod),
            ("CommanderGameAccess.GetUnitAmmo", GameAccessGetUnitAmmoMethod),
            ("CommanderNetwork.RunAs", NetworkRunAsMethod),
            ("CommanderSupplyHeliService.Instance", SupplyHeliInstanceProp),
            ("CommanderSupplyHeliService.aircraftOptions", SupplyHeliAircraftOptionsField),
            ("CommanderSupplyHeliService.assignedMissions", SupplyHeliAssignedMissionsField),
            ("CommanderSupplyHeliService.pendingAircraftSpawns", SupplyHeliPendingSpawnsField),
            ("CommanderSupplyHeliService.queuedCargoSpawns", SupplyHeliQueuedSpawnsField),
            ("CommanderSupplyHeliService.SelectedAircraftIndex", SupplyHeliSelectedAircraftIndexProp),
            ("CommanderSupplyHeliService.SelectedAirbase", SupplyHeliSelectedAirbaseProp),
            ("CommanderSupplyHeliService.SelectAircraft", SupplyHeliSelectAircraftMethod),
            ("CommanderSupplyHeliService.statusText", SupplyHeliStatusTextField),
            ("CommanderSupplyHeliService.airdropDelivery", SupplyHeliAirdropDeliveryField),
            ("CommanderSupplyHeliService.AwaitingTargetSelection", SupplyHeliAwaitingTargetSelectionProp),
            ("CommanderSupplyHeliService.CreateEmptyLoadout", SupplyHeliCreateEmptyLoadoutMethod),
            ("CommanderSupplyHeliService.PlaceCargoAndClearNonCargo", SupplyHeliPlaceCargoMethod),
            ("CommanderSupplyHeliService.GetCargoLabel", SupplyHeliGetCargoLabelMethod),
            ("CommanderSupplyHeliService.GetCargoCost", SupplyHeliGetCargoCostMethod),
            ("CommanderSupplyHeliService.IsAvailableAirbase", SupplyHeliIsAvailableAirbaseMethod),
            ("CommanderSupplyHeliService.SpawnCargoRun(10)", SupplyHeliSpawnCargoRunMethod),
            ("CargoAircraftOption.Definition", CargoAircraftDefProp),
            ("CargoAircraftOption.Label", CargoAircraftLabelProp),
            ("CargoAircraftOption.Rotary", CargoAircraftRotaryProp),
            ("CargoSlotOption.Mounts", CargoSlotMountsProp),
            ("CargoMission.Hq", CargoMissionHqProp),
            ("CargoMission.Target", CargoMissionTargetProp),
            ("CargoMission.TargetOverrideActive", CargoMissionTargetOverrideProp),
            ("PendingAircraftSpawn.Hq", PendingSpawnHqProp),
            ("PendingAircraftSpawn.Target", PendingSpawnTargetProp),
            ("QueuedCargoSpawn.Hq", QueuedSpawnHqProp),
            ("QueuedCargoSpawn.Target", QueuedSpawnTargetProp),
            ("CommanderEnemyCommanderService+ActiveJammer", ActiveJammerType),
            ("CommanderEnemyCommanderService+AirRole", AirRoleType),
            ("CommanderEnemyCommanderService.BuyAirframe(5)", EnemyBuyAirframeMethod),
            ("CommanderEnemyCommanderService.TryBuyPicket(4)", EnemyTryBuyPicketMethod),
            ("CommanderEnemyCommanderService.CountPickets", EnemyCountPicketsMethod),
            ("CommanderEnemyCommanderService.CountAirborne", EnemyCountAirborneMethod),
            ("CommanderEnemyCommanderService.GetAirborneLimit", EnemyGetAirborneLimitMethod),
            ("CommanderEnemyCommanderService.HasAirStock", EnemyHasAirStockMethod),
            ("CommanderEnemyCommanderService.ChooseAirRole(2)", EnemyChooseAirRoleMethod),
            ("CommanderEnemyCommanderService.KeepAntiShipCandidates(5)", EnemyKeepAntiShipCandidatesMethod),
            ("CommanderEnemyCommanderService.ScoreAntiShipStore", EnemyScoreAntiShipStoreMethod),
            ("CommanderEnemyCommanderService.GetStrikeTarget", EnemyGetStrikeTargetMethod),
            ("CommanderEnemyCommanderService.PickWaveFlank(3)", EnemyPickWaveFlankMethod),
            ("CommanderEnemyCommanderService.PickPriorityTarget", EnemyPickPriorityTargetMethod),
            ("CommanderEnemyCommanderService.PickSalvoTarget", EnemyPickSalvoTargetMethod),
            ("CommanderEnemyCommanderService.CanAnyStoreDefeat", EnemyCanAnyStoreDefeatMethod),
            ("CommanderEnemyCommanderService.GetCruiseHeight", EnemyGetCruiseHeightMethod),
            ("CommanderEnemyCommanderService.PickJammerTargets", EnemyPickJammerTargetsMethod),
            ("CommanderEnemyCommanderService.ReviewAaScouts(2)", EnemyReviewAaScoutsMethod),
            ("CommanderEnemyCommanderService.EnsureReconPosts(3)", EnemyEnsureReconPostsMethod),
            ("CommanderEnemyCommanderService.FindHighGround", EnemyFindHighGroundMethod),
            ("CommanderEnemyCommanderService.ReviewArtillery(3)", EnemyReviewArtilleryMethod),
            ("CommanderEnemyCommanderService.RaiseEcoAlarm(4)", EnemyRaiseEcoAlarmMethod),
            ("CommanderEnemyCommanderService.LocateBattery", EnemyLocateBatteryMethod),
            ("CommanderEnemyCommanderService.ReleaseNavalWave(3)", EnemyReleaseNavalWaveMethod),
            ("CommanderEnemyCommanderService.PinTo", EnemyPinToMethod),
            ("CommanderEnemyCommanderService.HoldAt", EnemyHoldAtMethod),
            ("CommanderEnemyCommanderService.ReleaseCommandedDestination", EnemyReleaseCommandedDestinationMethod),
            ("CommanderEnemyCommanderService.IsDefendingUnit", EnemyIsDefendingUnitMethod),
            ("CommanderEnemyCommanderService.IsRadarPicket", EnemyIsRadarPicketMethod),
            ("CommanderEnemyCommanderService.SteerPickets", EnemySteerPicketsMethod),
            ("CommanderEnemyCommanderService.IsRecruitable", EnemyIsRecruitableMethod),
            ("CommanderEnemyCommanderService.GetAirRole(Aircraft)", EnemyGetAirRoleAircraftMethod),
            ("CommanderEnemyCommanderService.jammers", EnemyJammersField),
            ("ActiveJammer.ctor", ActiveJammerCtor),
            ("CommanderState.ReconPosts", StateReconPostsProp),
            ("CommanderState.Batteries", StateBatteriesProp),
            ("CommanderState.BatteryTargets", StateBatteryTargetsProp),
            ("CommanderState.EcoAlarmUntil", StateEcoAlarmUntilProp),
            ("CommanderState.EcoAlarmAt", StateEcoAlarmAtProp),
            ("CommanderState.Defending", StateDefendingProp),
            ("CommanderState.WaveMembers", StateWaveMembersProp),
            ("CommanderState.NavalRallying", StateNavalRallyingProp),
            ("CommanderState.NavalWaitingSince", StateNavalWaitingSinceProp),
            ("CommanderCaptureService.ReviewMainEffort", CaptureReviewMainEffortMethod),
            ("CommanderCaptureService.FindNearestHeldBase", CaptureFindNearestHeldBaseMethod),
            ("CommanderCaptureService.ReleaseDrive(1)", CaptureReleaseDriveMethod),
            ("CommanderCaptureService.IsOnEnemyDrive", CaptureIsOnEnemyDriveMethod),
            ("CommanderCaptureService.CountDefenders", CaptureCountDefendersMethod),
            ("CommanderCaptureService.GetHoldPoint", CaptureGetHoldPointMethod),
            ("CommanderCaptureService.GetRoadMeters", CaptureGetRoadMetersMethod),
            ("CommanderCaptureService.IsSquadCandidate(2)", CaptureIsSquadCandidateMethod),
            ("CommanderCaptureService.GetTerritoryCenter", CaptureGetTerritoryCenterMethod),
            ("CommanderCaptureService.stagingBases", CaptureStagingBasesField),
            ("EnemyDrive.Launched", DriveLaunchedProp),
            ("EnemyDrive.Marching", DriveMarchingProp),
            ("EnemyDrive.FormingSince", DriveFormingSinceProp),
            ("EnemyDrive.MarchSince", DriveMarchSinceProp),
            ("CommanderEconomyService.TryPickEnemyBuildSite(2)", EcoTryPickEnemyBuildSiteMethod),
            ("CommanderEconomyService.IsIncomeBuildingUnit", EcoIsIncomeBuildingUnitMethod),
            ("CommanderEconomyService.TryGetNavalDockPosition(2)", EcoTryGetNavalDockPositionMethod),
            ("CommanderAirCommandService.SetAiCommitted", AirSetAiCommittedMethod),
            ("CommanderAirCommandService.SetAiRoute", AirSetAiRouteMethod),
            ("CommanderAirCommandService.ClearAiRoute", AirClearAiRouteMethod),
            ("CommanderAirCommandService.TrySetAiForcedTarget", AirTrySetAiForcedTargetMethod),
            ("CommanderAirCommandService.GetForcedTarget", AirGetForcedTargetMethod),
            ("CommanderAirCommandService.RetargetAiMission(4)", AirRetargetAiMissionMethod),
            ("CommanderAirCommandService.HasBombAmmo", AirHasBombAmmoMethod),
            ("CommanderAirCommandService.HasPlanePilot(Aircraft)", AirHasPlanePilotMethod),
            ("CommanderAirCommandService.HasRotaryPilot(Aircraft)", AirHasRotaryPilotMethod),
            ("CommanderAirCommandService.IsAradWeapon", AirIsAradWeaponMethod),
            ("CommanderAirCommandService.CarriesStrategicStore", AirCarriesStrategicStoreMethod),
            ("CommanderAirCommandService.missions", AirMissionsField),
            ("AirMission.Mode", AirMissionModeProp),
            ("AirMission.Returning", AirMissionReturningProp),
            ("AirMission.LandingBase", AirMissionLandingBaseProp),
            ("AirMission.ForcedTarget", AirMissionForcedTargetProp),
            ("CommanderAirCommandService.HasTakeoffRunway", AirHasTakeoffRunwayMethod),
            ("CommanderNavalPurchaseService.TryGetSeaLanePoint(2)", NavalTryGetSeaLanePointMethod),
            ("CommanderTerrainRouteService", TerrainRouteServiceType),
            ("CommanderTerrainRouteService.IsDrivable", TerrainRouteIsDrivableMethod),
            ("CommanderBuildPreview.IsOnLevelRoad", BuildPreviewIsOnLevelRoadMethod),
            ("CommanderSettings", SettingsType),
            ("CommanderSettings.EnemyMineIncomeMultiplierProp", EnemyMineIncomeMultiplierProp),
            ("CommanderGameAccess", GameAccessType),
            ("CommanderBuildKind", BuildKindType),
            ("CommanderBuildKind.Structure", BuildKindStructureValue),
            ("CommanderBuildKind.Mine", BuildKindMineValue),
            ("CommanderBuildKind.Factory", BuildKindFactoryValue),
            ("CommanderState", CommanderStateType),
            ("CommanderState.StrikeTruck", StateStrikeTruckProp),
            ("CommanderCaptureService+EnemyDrive", EnemyDriveType),
            ("EnemyDrive.Announced", DriveAnnouncedProp),
            ("CommanderRepairService.TryGetAssignedTarget", RepairTryGetAssignedTargetMethod),
            ("CommanderUnitCap.TypeCap", UnitCapTypeCapGetter),
            ("CommanderUnitCap.BuilderCap", UnitCapBuilderCapGetter),
            ("CommanderUnitCap.IsAiTypeFull", UnitCapIsAiTypeFullMethod),
            ("CommanderEnemyCommanderService.FindGuardTruck", EnemyFindGuardTruckMethod),
            ("CommanderEnemyCommanderService.CanLaunchFrom", EnemyCanLaunchFromMethod),
            ("CommanderEconomyService.IsBuilder", EcoIsBuilderMethod),
            ("CommanderEconomyService.TryGiveStartingBuilders", EcoTryGiveStartingBuildersMethod),
            ("CommanderEconomyService.TrySpawnLocalBuilder", EcoTrySpawnLocalBuilderMethod),
            ("CommanderEconomyService.EnsureEnemySiteBunker", EcoEnsureEnemySiteBunkerMethod),
            ("ConstructionSite.Kind", SiteKindProp),
            ("ConstructionSite.Production", SiteProductionProp),
            ("ConstructionSite.Site", SitePositionProp),
            ("CommanderUnitPreview+Part.ctor", UnitPreviewPartCtor),
            ("CommanderBuildPreview+Part.ctor", BuildPreviewPartCtor),
            ("CommanderSupplyHeliService+CargoAircraftOption", CargoAircraftOptionType),
            ("CargoAircraftOption.CargoSlots", CargoAircraftCargoSlotsProp),
            ("CargoAircraftOption.HardpointSets", CargoAircraftHardpointSetsProp),
            ("CommanderSupplyHeliService+CargoSlotOption", CargoSlotOptionType),
            ("CargoSlotOption.SelectedMount", CargoSlotSelectedMountProp),
            ("CargoSlotOption.HardpointIndex", CargoSlotHardpointIndexProp),
            ("CommanderSupplyHeliService+PendingTargetSelection", PendingTargetSelectionType),
            ("PendingTargetSelection.Aircraft", PendingTargetSelectionAircraftProp),
            ("PendingTargetSelection.Loadout", PendingTargetSelectionLoadoutProp),
            ("CommanderBuildKind.NavalDock", BuildKindNavalDockValue),
            ("CommanderBuildKind.GasTerminal", BuildKindGasTerminalValue),
            ("CommanderEconomyService.shoreSearchFailed", EcoShoreSearchFailedField),
            ("CommanderEconomyService.preview", EcoPreviewField),
            ("CommanderEconomyService.builderResting", EcoBuilderRestingField),
            ("CommanderEconomyService.NavalDockBuildCost", EcoNavalDockBuildCostProp),
            ("CommanderEconomyService.ResolveDefinition", EcoResolveDefinitionMethod),
            ("CommanderEconomyService.GetEnemyDockCost", EcoGetEnemyDockCostMethod),
            ("CommanderEconomyService.TryBuildEnemyDock", EcoTryBuildEnemyDockMethod),
            ("CommanderEconomyService.TryBuildEnemyNavalDock", EcoTryBuildEnemyNavalDockMethod),
            ("CommanderEconomyService.HasGivenUpOnDock", EcoHasGivenUpOnDockMethod),
            ("CommanderEconomyService.ReviewBuilders", EcoReviewBuildersMethod),
            ("CommanderEconomyService.TryGetStrandedBuilder", EcoTryGetStrandedBuilderMethod),
            ("CommanderEconomyService.CollectIdleBuilders", EcoCollectIdleBuildersMethod),
            ("CommanderEconomyService.HasBusyBuilderNear", EcoHasBusyBuilderNearMethod),
            ("CommanderEconomyService.SendBuilder(2)", EcoSendBuilderMethod),
            ("CommanderEconomyService.IsWater", EcoIsWaterMethod),
            ("CommanderEconomyService.IsOpenWater", EcoIsOpenWaterMethod),
            ("CommanderEconomyService.ReachesSea", EcoReachesSeaMethod),
            ("CommanderEconomyService.CountBlueprints(2)", EcoCountBlueprintsMethod),
            ("CommanderEconomyService.IsNearBuiltTower", EcoIsNearBuiltTowerMethod),
            ("ConstructionSite.Builders", SiteBuildersProp),
            ("ConstructionSite.NextBuilderSpawnAt", SiteNextBuilderSpawnAtProp),
            ("CommanderBuildPreview.IsSiteAllowed", BuildPreviewIsSiteAllowedMethod),
            ("CommanderBuildPreview.IsShoreline", BuildPreviewIsShorelineMethod),
            ("CommanderBuildPreview.IsControlTower", BuildPreviewIsControlTowerMethod),
            ("CommanderBuildPreview.IsControlTowerUnit", BuildPreviewIsControlTowerUnitMethod),
            ("CommanderSettings.get_BuildRadiusKm", SettingsBuildRadiusKmGetter),
            ("CommanderGameAccess.FindRepairVehicleDefinition", GameAccessFindRepairVehicleDefinitionMethod),
            ("CommanderMoveService.ClearOrder", MoveClearOrderMethod),
            ("CommanderAirCommandService.BuildSlingLoadout", AirBuildSlingLoadoutMethod),
            ("CommanderAirCommandService.CanAiFly", AirCanAiFlyMethod),
            ("CommanderEconomyService.IsStuckAiBuilder(2)", EcoIsStuckAiBuilderMethod),
            ("CommanderEconomyService.upkeepPerMinute", EcoUpkeepPerMinuteField),
            ("CommanderEconomyService.enemyFactoryFunds", EcoEnemyFactoryFundsField),
            ("CommanderEconomyService.GetBaseIncomePerMinute", EcoGetBaseIncomePerMinuteMethod),
            ("CommanderEconomyService.GetGodOpeningReserve", EcoGetGodOpeningReserveMethod),
            ("CommanderEconomyService.IncomeTickProgress", EcoIncomeTickProgressProp),
            ("CommanderEconomyService.FriendlyUpkeepPerMinute", EcoFriendlyUpkeepPerMinuteProp),
            ("CommanderState.NavalFund", StateNavalFundProp),
            ("CommanderState.WarFundAt", StateWarFundAtProp),
            ("CommanderState.Broke", StateBrokeProp),
            ("CommanderNetwork.IsGuest", NetworkIsGuestProp),
            ("CommanderVictoryService", VictoryServiceType),
            ("CommanderVictoryService.HomeBases", VictoryHomeBasesField),
            ("CommanderRepairService.HoldLeftBehindUnits", RepairHoldLeftBehindUnitsMethod),
            ("CommanderRepairService.SweepStandby", RepairSweepStandbyMethod),
            ("CommanderRepairService.guestFactions", RepairGuestFactionsField),
            ("CommanderEconomyService.TickStartingBuilders", EcoTickStartingBuildersMethod),
            ("CommanderEconomyService.PlaceBaseDepots", EcoPlaceBaseDepotsMethod),
            ("CommanderEconomyService.VehicleDepotKeys", EcoVehicleDepotKeysField),
            ("CommanderFactionVehicleService", FactionVehicleServiceType),
            ("CommanderFactionVehicleService.CanPurchase", FactionVehicleCanPurchaseMethod),
            ("CommanderMobileEmplacementService.IsTowedBy", MobileEmplacementIsTowedByMethod),
            ("CommanderGameAccess.SetUnitHoldPosition", GameAccessSetUnitHoldPositionMethod),
            ("CommanderGameAccess.TryGetLocalVehicleDefinitions", GameAccessTryGetLocalVehicleDefinitionsMethod),
            ("CommanderEconomyService.IsSupplyTruck", EcoIsSupplyTruckMethod),
            ("CommanderSpawnService.RefreshFactionVehicleDefinitions", SpawnServiceRefreshFactionVehiclesMethod),
            ("CommanderSpawnService.RefreshVehicleDefinitions", SpawnServiceRefreshVehiclesMethod),
            ("CommanderSupplyHeliService.CargoMountSupportsAirdrop", SupplyHeliCargoMountSupportsAirdropMethod),
            ("CommanderSupplyHeliService.TrySpawnCargoRunAtAirbase", SupplyHeliTrySpawnCargoRunAtAirbaseMethod),
            ("CommanderSlingLiftService+SlingJob", SlingJobType),
            ("CommanderSlingLiftService.EndJob", SlingEndJobMethod),
        };

        int present = 0;
        for (int i = 0; i < checks.Length; i++)
        {
            if (checks[i].Member != null)
            {
                present++;
            }
            else
            {
                AddonLog.Warn($"[AI] Missing GroundControlRts binding: {checks[i].Name}");
            }
        }

        AddonLog.Info($"[AI] Reflection bindings verified: {present}/{checks.Length}");
    }

    internal static object? GetEcoInstance() => EcoInstanceProp?.GetValue(null);
    internal static object? GetEnemyInstance() => EnemyInstanceProp?.GetValue(null);
    internal static object? GetCaptureInstance() => CaptureInstanceProp?.GetValue(null);

    internal static FactionHQ? GetLocalHq()
        => GetLocalHqMethod?.Invoke(null, null) as FactionHQ;

    internal static int GetEnemyDifficulty()
        => EnemyDifficultyProp?.GetValue(null) is int d ? d : 0;

    internal static bool IsChampionsMission()
        => IsChampionsMissionProp?.GetValue(null) is bool b && b;

    internal static bool IsDuelMission()
        => IsDuelMissionProp?.GetValue(null) is bool b && b;

    internal static bool IsAiFaction(FactionHQ? hq, FactionHQ? localHq)
        => hq != null && IsAiFactionMethod?.Invoke(null, new object?[] { hq, localHq }) is bool b && b;

    internal static bool HasHumanPlayers(FactionHQ? hq)
    {
        if (hq == null)
        {
            return false;
        }

        if (EnemyHasHumanPlayersMethod != null)
        {
            return EnemyHasHumanPlayersMethod.Invoke(null, new object?[] { hq }) is bool b && b;
        }

        return hq.GetPlayers(false).Count > 0;
    }

    internal static bool IsBothSidesMode()
        => EnemyBothSidesProp?.GetValue(null) is bool b && b;

    internal static bool IsHumanTeamAiEnabled()
        => SettingsHumanTeamsProp?.GetValue(null) is bool b && b;

    internal static bool IsSafeAiFaction(FactionHQ? hq, FactionHQ? localHq)
    {
        if (hq == null || !IsAiFaction(hq, localHq))
        {
            return false;
        }

        if (HasHumanPlayers(hq) && !IsHumanTeamAiEnabled() && !IsBothSidesMode())
        {
            return false;
        }

        return true;
    }

    internal static FactionHQ? FindPrimaryHostileHq(FactionHQ? ownHq)
    {
        if (ownHq == null)
        {
            return null;
        }

        FactionHQ? localHq = GetLocalHq();
        if (localHq != null && !ReferenceEquals(localHq, ownHq) && localHq.faction != null)
        {
            return localHq;
        }

        FactionHQ? fallback = null;
        foreach (FactionHQ cand in FactionRegistry.GetAllHQs())
        {
            if (cand == null || ReferenceEquals(cand, ownHq) || cand.faction == null)
            {
                continue;
            }

            if (HasHumanPlayers(cand))
            {
                return cand;
            }

            fallback ??= cand;
        }

        return fallback;
    }

    internal static int CountMines(object? ecoInstance, FactionHQ hq)
        => ecoInstance != null && CountMinesMethod?.Invoke(ecoInstance, new object?[] { hq, null }) is int n ? n : 0;

    internal static int CountGoldMines(object? ecoInstance, FactionHQ hq)
    {
        if (ecoInstance == null || hq == null || CountMinesMethod == null)
        {
            return 0;
        }

        if (BuildKindMineValue != null)
        {
            return CountMinesMethod.Invoke(ecoInstance, new object?[] { hq, BuildKindMineValue }) is int n ? n : 0;
        }

        return CountMines(ecoInstance, hq);
    }

    internal static int GetDepotLevel(Unit? depot)
        => depot != null && EcoGetDepotLevelMethod?.Invoke(null, new object?[] { depot }) is int lvl ? lvl : 1;

    internal static float GetDepotUpgradeCost(int currentLevel)
        => EcoGetDepotUpgradeCostMethod?.Invoke(null, new object?[] { currentLevel }) is float cost
            ? cost
            : (currentLevel < 2 ? 300f : 1000f);

    internal static int CountLandBases(FactionHQ? hq)
        => hq != null && EcoCountLandBasesMethod?.Invoke(null, new object?[] { hq }) is int n ? n : 0;

    internal static bool HasDepotUpgradeIncome(object? ecoInstance, FactionHQ? hq)
        => ecoInstance != null && hq != null && EcoHasDepotUpgradeIncomeMethod?.Invoke(ecoInstance, new object?[] { hq }) is bool ok && ok;

    internal static bool TryUpgradeEnemyDepot(object? ecoInstance, FactionHQ? hq)
        => ecoInstance != null && hq != null && EcoTryUpgradeEnemyDepotMethod?.Invoke(ecoInstance, new object?[] { hq }) is bool ok && ok;

    internal static int CountFreeMines(object? ecoInstance, FactionHQ hq)
        => ecoInstance != null && CountFreeMinesMethod?.Invoke(ecoInstance, new object?[] { hq }) is int n ? n : 0;

    internal static int CountFactories(FactionHQ hq)
        => CountFactoriesMethod?.Invoke(null, new object?[] { hq }) is int n ? n : 0;

    internal static int GetEnemyFactoryTarget(object? ecoInstance, FactionHQ? hq, bool allowWait = false)
        => ecoInstance != null && hq != null && GetEnemyFactoryTargetMethod?.Invoke(ecoInstance, new object?[] { hq, allowWait }) is int n ? n : 4;

    internal static bool CargoMountSupportsAirdrop(WeaponMount? mount)
        => mount != null && SupplyHeliCargoMountSupportsAirdropMethod?.Invoke(null, new object?[] { mount }) is bool b && b;

    internal static bool HoldsMostBases(FactionHQ hq)
        => HoldsMostBasesMethod?.Invoke(null, new object?[] { hq }) is bool b && b;

    internal static float GetHqIncomePerMinute(object? ecoInstance, FactionHQ hq)
        => ecoInstance != null && GetHqIncomePerMinuteMethod?.Invoke(ecoInstance, new object?[] { hq }) is float v ? v : 0f;

    internal static float GetEnemyNetIncomePerMinute(FactionHQ hq)
        => GetEnemyNetIncomePerMinuteMethod?.Invoke(null, new object?[] { hq }) is float v ? v : 0f;

    internal static float GetStageIncomePerMinute(object kind, int level)
        => GetStageIncomePerMinuteMethod?.Invoke(null, new object?[] { kind, level }) is float v ? v : 0f;

    internal static object? GetIncomeKind(object ecoInstance, Unit unit)
        => GetIncomeKindMethod?.Invoke(ecoInstance, new object?[] { unit });

    internal static IDictionary? GetIncomeLevels(object ecoInstance)
        => EcoIncomeLevelsField?.GetValue(ecoInstance) as IDictionary;

    internal static object? ChooseEnemyIncomeKind(object? ecoInstance, FactionHQ hq)
        => ecoInstance != null && hq != null ? EcoChooseEnemyIncomeKindMethod?.Invoke(ecoInstance, new object?[] { hq }) : null;

    internal static bool IsMineBuildKind(object? kind)
        => kind != null && BuildKindMineValue != null && Equals(kind, BuildKindMineValue);

    internal static float GetEnemyIncomeReserve(object? ecoInstance, FactionHQ? hq, object? kind)
    {
        if (ecoInstance == null || hq == null || kind == null || EcoGetEnemyIncomeReserveMethod == null)
        {
            return IsMineBuildKind(kind) ? 600f : 160f;
        }

        return EcoGetEnemyIncomeReserveMethod.Invoke(ecoInstance, new object?[] { hq, kind }) is float reserve && reserve > 0f
            ? reserve
            : (IsMineBuildKind(kind) ? 600f : 160f);
    }

    internal static Unit? FindEnemyIncomeUpgrade(object? ecoInstance, FactionHQ hq, out float cost)
    {
        cost = 0f;
        if (ecoInstance == null || hq == null || EcoFindEnemyIncomeUpgradeMethod == null)
        {
            return null;
        }

        object?[] args = new object?[] { hq, 0f };
        Unit? result = EcoFindEnemyIncomeUpgradeMethod.Invoke(ecoInstance, args) as Unit;
        cost = args[1] is float c ? c : 0f;
        return result;
    }

    internal static int GetNavalDockLevel(FactionHQ? hq)
        => hq != null && EcoGetNavalDockLevelHqMethod?.Invoke(null, new object?[] { hq }) is int lvl ? lvl : 0;

    internal static float GetNavalDockUpgradeCost(int currentLevel)
        => EcoGetNavalDockUpgradeCostMethod?.Invoke(null, new object?[] { currentLevel }) is float cost
            ? cost
            : (currentLevel >= 2 ? 900f : 450f);

    internal static bool TryUpgradeEnemyNavalDock(object? ecoInstance, FactionHQ? hq)
        => ecoInstance != null && hq != null && EcoTryUpgradeEnemyNavalDockMethod?.Invoke(ecoInstance, new object?[] { hq }) is bool ok && ok;

    internal static float GetFactoryBuildCost(UnitDefinition? production)
        => production != null && EcoGetFactoryBuildCostMethod?.Invoke(null, new object?[] { production }) is float cost ? cost : 250f;

    internal static bool IsCricket(UnitDefinition? def)
        => def != null && EcoIsCricketMethod?.Invoke(null, new object?[] { def }) is bool b && b;

    internal static HashSet<UnitDefinition>? GetEnemyFactoryProducts(object? ecoInstance)
        => ecoInstance != null ? EcoEnemyFactoryProductsField?.GetValue(ecoInstance) as HashSet<UnitDefinition> : null;

    internal static int GetRequiredDockLevel(ShipDefinition? def)
        => def != null && NavalGetRequiredDockLevelMethod?.Invoke(null, new object?[] { def }) is int lvl ? lvl : 0;

    internal static bool HasBaseWithoutUpgradedDepot(object? ecoInstance, FactionHQ? hq)
        => ecoInstance != null && hq != null && EcoHasBaseWithoutUpgradedDepotMethod?.Invoke(ecoInstance, new object?[] { hq }) is bool b && b;

    internal static int CountBuildings(FactionHQ? hq, BuildingType type)
        => hq != null && EcoCountBuildingsMethod?.Invoke(null, new object?[] { hq, type }) is int n ? n : 0;

    internal static float GetEnemyBuildReserve(FactionHQ? hq, out bool goldMine, out bool income)
    {
        goldMine = false;
        income = false;
        if (hq == null || GetEnemyBuildReserveMethod == null)
        {
            return 0f;
        }

        object?[] args = new object?[] { hq, false, false };
        object? res = GetEnemyBuildReserveMethod.Invoke(null, args);
        goldMine = args[1] is bool gm && gm;
        income = args[2] is bool inc && inc;
        return res is float f ? f : 0f;
    }

    internal static int GetEnemyOwnedCombat(object? enemyInstance)
        => enemyInstance != null && EnemyOwnedCombatField?.GetValue(enemyInstance) is int n ? n : 0;

    internal static int GetEnemyOwnedAirDefence(object? enemyInstance)
        => enemyInstance != null && EnemyCountOwnedAirDefenceMethod?.Invoke(enemyInstance, null) is int n ? n : 0;

    internal static AircraftDefinition? GetEnemySavingFor(object? enemyInstance)
        => enemyInstance != null ? EnemySavingForField?.GetValue(enemyInstance) as AircraftDefinition : null;

    internal static IList? GetEnemyAirCandidates(object? enemyInstance)
        => enemyInstance != null ? EnemyAirCandidatesField?.GetValue(enemyInstance) as IList : null;

    internal static bool GetStateBool(object? state, PropertyInfo? prop)
        => state != null && prop?.GetValue(state) is bool b && b;

    internal static string GetEnemySavingForName(object? enemyInstance)
        => GetEnemySavingFor(enemyInstance)?.unitName ?? string.Empty;

    internal static float GetBaseIncomePerMinute()
        => EcoGetBaseIncomePerMinuteMethod?.Invoke(null, null) is float v ? v : 0f;

    internal static float GetAiStipendPerMinute(FactionHQ? hq, FactionHQ? localHq)
        => hq != null && GetAiStipendPerMinuteMethod?.Invoke(null, new object?[] { hq, localHq }) is float v ? v : 0f;

    internal static float GetEnemyMineIncomeMultiplier()
        => EnemyMineIncomeMultiplierGetter?.Invoke(null, null) is float v ? Mathf.Max(0f, v) : 1f;

    internal static float GetFactionUpkeepPerMinute(object? ecoInstance, FactionHQ? hq, bool isLocal)
    {
        if (ecoInstance == null || hq == null)
        {
            return 0f;
        }

        if (EcoUpkeepPerMinuteField?.GetValue(ecoInstance) is IDictionary map && map.Contains(hq) && map[hq] is float upkeep)
        {
            return Mathf.Max(0f, upkeep);
        }

        if (isLocal && EcoFriendlyUpkeepPerMinuteProp?.GetValue(ecoInstance) is float friendlyUpkeep)
        {
            return Mathf.Max(0f, friendlyUpkeep);
        }

        return Mathf.Max(0f, GetHqIncomePerMinute(ecoInstance, hq) - GetEnemyNetIncomePerMinute(hq));
    }

    internal static float GetEnemyFactoryReserve(object? ecoInstance, FactionHQ? hq)
        => ecoInstance != null && hq != null && GetEnemyFactoryReserveMethod?.Invoke(ecoInstance, new object?[] { hq }) is float v
            ? Mathf.Max(0f, v)
            : 0f;

    internal static float GetEnemyFactoryFundReadOnly(object? ecoInstance, FactionHQ? hq, float grossPerMin, float factoryReserve)
    {
        if (ecoInstance == null || hq == null || factoryReserve <= 0f)
        {
            return 0f;
        }

        if (EcoEnemyFactoryFundsField?.GetValue(ecoInstance) is not IDictionary map || !map.Contains(hq))
        {
            return 0f;
        }

        object? boxed = map[hq];
        if (boxed is not ValueTuple<float, float> entry)
        {
            return 0f;
        }

        float fund = entry.Item1;
        float at = entry.Item2;
        if (at > 0f)
        {
            fund += grossPerMin * 0.25f * (Mathf.Max(0f, Time.time - at) / 60f);
        }

        return Mathf.Clamp(fund, 0f, Mathf.Min(factoryReserve, Mathf.Max(0f, hq.factionFunds)));
    }

    internal static float GetGodOpeningReserve(FactionHQ? hq)
        => hq != null && EcoGetGodOpeningReserveMethod?.Invoke(null, new object?[] { hq }) is float v ? Mathf.Max(0f, v) : 0f;

    internal static float GetIncomeTickProgress(object? ecoInstance)
        => ecoInstance != null && EcoIncomeTickProgressProp?.GetValue(ecoInstance) is float p ? p : -1f;

    internal static bool IsNetworkGuest()
        => NetworkIsGuestProp?.GetValue(null) is bool b && b;

    internal static float GetStateFloat(object? state, PropertyInfo? prop)
        => state != null && prop?.GetValue(state) is float v ? v : 0f;

    internal static void SetStateFloat(object? state, PropertyInfo? prop, float value)
    {
        if (state != null && prop != null)
        {
            prop.SetValue(state, value);
        }
    }

    internal static object? TryGetCommanderState(object? enemyInstance, FactionHQ? hq)
    {
        if (enemyInstance == null || hq == null)
        {
            return null;
        }

        return EnemyStatesField?.GetValue(enemyInstance) is IDictionary states && states.Contains(hq)
            ? states[hq]
            : null;
    }

    internal static int GetBestDepotLevel(FactionHQ? hq)
        => hq != null && GetBestDepotLevelMethod?.Invoke(null, new object?[] { hq }) is int lvl ? lvl : 1;

    internal static int GetRequiredDepotLevel(VehicleDefinition def)
        => GetRequiredDepotLevelMethod?.Invoke(null, new object?[] { def }) is int lvl ? lvl : 1;

    internal static bool IsCombatVehicle(VehicleDefinition def)
        => IsCombatVehicleMethod?.Invoke(null, new object?[] { def }) is bool b && b;

    internal static bool IsAirDefence(VehicleDefinition def)
        => IsAirDefenceMethod?.Invoke(null, new object?[] { def }) is bool b && b;

    internal static bool IsRadarGuidedAirDefence(VehicleDefinition def)
        => IsRadarGuidedAirDefenceMethod?.Invoke(null, new object?[] { def }) is bool b && b;

    internal static bool IsBuilderVehicle(VehicleDefinition def)
        => IsBuilderVehicleMethod?.Invoke(null, new object?[] { def }) is bool b && b;

    internal static bool IsSupplyVehicle(VehicleDefinition def)
        => IsSupplyVehicleMethod?.Invoke(null, new object?[] { def }) is bool b && b;

    internal static bool IsConvoyAirDefence(VehicleDefinition def)
        => EnemyIsConvoyAirDefenceMethod?.Invoke(null, new object?[] { def }) is bool b && b;

    internal static bool EnemyBaseLacks(object? enemyInstance, FactionHQ hq, object state, VehicleDefinition def, int perBase)
        => enemyInstance != null && EnemyBaseLacksMethod?.Invoke(enemyInstance, new object?[] { hq, state, def, perBase }) is bool b && b;

    internal static int EnemyCountOwned(FactionHQ hq, VehicleDefinition def)
        => EnemyCountOwnedMethod?.Invoke(null, new object?[] { hq, def }) is int n ? n : 0;

    internal static int EnemyCountSamLaunchers(object? enemyInstance, FactionHQ hq)
        => enemyInstance != null && EnemyCountSamLaunchersMethod?.Invoke(enemyInstance, new object?[] { hq }) is int n ? n : 0;

    internal static int EnemyCountRadarReserve(object? enemyInstance, FactionHQ hq)
        => enemyInstance != null && EnemyCountRadarReserveMethod?.Invoke(enemyInstance, new object?[] { hq }) is int n ? n : 0;

    internal static bool EnemyCanField(VehicleDepot? depot, VehicleDefinition? def)
        => depot != null && def != null && EnemyCanFieldMethod?.Invoke(null, new object?[] { depot, def }) is bool b && b;

    internal static VehicleDepot? EnemyFindSiteDepot(FactionHQ hq, GlobalPosition site)
        => EnemyFindSiteDepotMethod?.Invoke(null, new object?[] { hq, site }) as VehicleDepot;

    internal static void EnemyCollectBaseDepots(FactionHQ hq, GlobalPosition centre, List<VehicleDepot> into)
        => EnemyCollectBaseDepotsMethod?.Invoke(null, new object?[] { hq, centre, into });

    internal static VehicleDefinition? EnemyChooseSamLauncherUnit(object? enemyInstance, float budget)
        => enemyInstance != null ? EnemyChooseSamLauncherUnitMethod?.Invoke(enemyInstance, new object?[] { budget }) as VehicleDefinition : null;

    internal static bool IsRadarHunter(Aircraft aircraft, FactionHQ hq)
        => IsRadarHunterMethod?.Invoke(null, new object?[] { aircraft, hq }) is bool b && b;

    internal static Unit? FindRadarToHunt(FactionHQ hq, GlobalPosition from, out GlobalPosition at)
    {
        at = default;
        if (FindRadarToHuntMethod == null)
        {
            return null;
        }

        object?[] args = { hq, from, default(GlobalPosition) };
        Unit? result = FindRadarToHuntMethod.Invoke(null, args) as Unit;
        if (args[2] is GlobalPosition foundAt)
        {
            at = foundAt;
        }

        return result;
    }

    internal static bool CanDriveBetween(GlobalPosition from, GlobalPosition to)
        => CanDriveBetweenMethod?.Invoke(null, new object?[] { from, to }) is bool b && b;

    internal static bool IsLandBase(Airbase? airbase)
        => airbase != null && IsLandBaseMethod?.Invoke(null, new object?[] { airbase }) is bool b && b;

    internal static bool IsTakeable(Airbase? airbase, FactionHQ hq)
        => airbase != null && IsTakeableMethod?.Invoke(null, new object?[] { airbase, hq }) is bool b && b;

    internal static float GetOwnerGrip(Airbase? airbase)
        => airbase != null && GetOwnerGripMethod?.Invoke(null, new object?[] { airbase }) is float g ? g : 0f;

    internal static string GetAirbaseLabel(Airbase? airbase)
        => GetAirbaseLabelMethod?.Invoke(null, new object?[] { airbase }) as string ?? "Base";

    internal static GlobalPosition SnapToTerrain(GlobalPosition pos)
        => SnapToTerrainMethod?.Invoke(null, new object?[] { pos }) is GlobalPosition snapped ? snapped : pos;

    internal static void InvalidateTerrainRouteCache()
    {
        if (TerrainRouteHeightsField?.GetValue(null) is IDictionary heights)
        {
            heights.Clear();
        }

        if (TerrainRouteStalledCellsField?.GetValue(null) is IDictionary stalled)
        {
            stalled.Clear();
        }
    }

    internal static bool IsBelowSeaLevel(GlobalPosition pos)
        => IsBelowSeaLevelMethod?.Invoke(null, new object?[] { pos }) is bool b && b;

    internal static bool IsCommanderModeActive()
    {
        object? plugin = CommanderPluginInstanceProp?.GetValue(null);
        return plugin != null && IsCommanderModeActiveProp?.GetValue(plugin) is bool active && active;
    }

    internal static void NoticeFail(string message)
        => NoticeFailMethod?.Invoke(null, new object?[] { message });

    internal static bool TrySpend(FactionHQ? hq, float amount)
        => hq != null && TrySpendMethod?.Invoke(null, new object?[] { hq, amount }) is bool ok && ok;

    internal static bool TryRaycastWorldPosition(Vector2 screenPosition, out GlobalPosition position)
    {
        position = default;
        if (TryRaycastWorldPositionMethod == null)
        {
            return false;
        }

        object?[] args = { screenPosition, default(GlobalPosition) };
        if (TryRaycastWorldPositionMethod.Invoke(null, args) is bool ok && ok && args[1] is GlobalPosition hit)
        {
            position = hit;
            return true;
        }

        return false;
    }

    internal static bool TrySetDestination(Unit? unit, GlobalPosition destination)
        => unit != null && TrySetDestinationMethod?.Invoke(null, new object?[] { unit, destination }) is bool ok && ok;

    internal static bool TrySetAiDestination(Unit? unit, GlobalPosition destination)
    {
        if (unit == null)
        {
            return false;
        }

        if (GameAccessSendDestinationMethod != null)
        {
            return GameAccessSendDestinationMethod.Invoke(null, new object?[] { unit, destination, false }) is bool ok && ok;
        }

        return TrySetDestination(unit, destination);
    }

    internal static float GetUnitCondition(Unit? unit)
        => unit != null && GetUnitConditionMethod?.Invoke(null, new object?[] { unit }) is float cond ? cond : 1f;

    internal static string GetUnitLabel(Unit? unit)
        => unit != null ? GetUnitLabelMethod?.Invoke(null, new object?[] { unit }) as string ?? unit.unitName ?? "Unit" : "Unit";

    internal static bool CanAirWeaponDefeatArmor(WeaponInfo? info, Unit? target)
        => info != null && AirCanDefeatArmorMethod?.Invoke(null, new object?[] { info, target }) is bool b && b;

    internal static bool IsFireNowSensorStore(WeaponInfo? info)
        => info != null && FireNowIsSensorStoreMethod?.Invoke(null, new object?[] { info }) is bool b && b;

    internal static bool IsPlayerAirMission(object? mission)
        => mission != null && AirIsPlayerMissionMethod?.Invoke(null, new object?[] { mission }) is bool b && b;

    internal static bool IsRepairUnit(Unit? unit)
        => unit != null && IsRepairUnitMethod?.Invoke(null, new object?[] { unit }) is bool b && b;

    internal static void SendRepairUnitHome(Unit? unit)
    {
        if (unit == null)
        {
            return;
        }

        object? repairInstance = RepairInstanceProp?.GetValue(null);
        if (repairInstance != null)
        {
            SendHomeMethod?.Invoke(repairInstance, new object?[] { unit });
        }
    }

    internal static void BeginStructureBuild(BuildingDefinition definition)
    {
        object? eco = GetEcoInstance();
        if (eco != null)
        {
            BeginStructureBuildMethod?.Invoke(eco, new object?[] { definition });
        }
    }

    internal static void CancelEconomyBuild()
    {
        object? eco = GetEcoInstance();
        if (eco != null)
        {
            CancelBuildMethod?.Invoke(eco, null);
        }
    }

    internal static bool IsEconomyAwaitingPlacement()
    {
        object? eco = GetEcoInstance();
        return eco != null && AwaitingPlacementProp?.GetValue(eco) is bool b && b;
    }

    internal static string GetEconomyStatusText()
    {
        object? eco = GetEcoInstance();
        return eco != null ? EcoStatusTextProp?.GetValue(eco) as string ?? string.Empty : string.Empty;
    }

    internal static IReadOnlyList<object>? GetActiveBlueprints()
    {
        object? eco = GetEcoInstance();
        if (eco == null || EcoBlueprintsProp?.GetValue(eco) is not IEnumerable list)
        {
            return null;
        }

        var result = new List<object>();
        foreach (object item in list)
        {
            if (item != null)
            {
                result.Add(item);
            }
        }

        return result;
    }

    internal static bool TryGetBlueprintInfo(object site, out FactionHQ? hq, out BuildingDefinition? definition)
    {
        hq = SiteHqProp?.GetValue(site) as FactionHQ;
        definition = SiteDefinitionProp?.GetValue(site) as BuildingDefinition;
        return hq != null && definition != null;
    }

    internal static bool HasAnyBlueprintRoom(FactionHQ? hq)
        => hq != null && HasAnyBlueprintRoomMethod?.Invoke(null, new object?[] { hq }) is bool ok && ok;

    internal static bool TryFindSiteNear(
        object? ecoInstance,
        FactionHQ hq,
        BuildingDefinition definition,
        GlobalPosition anchor,
        out GlobalPosition site,
        int attempts = 16)
    {
        site = default;
        if (ecoInstance == null || TryFindSiteNearMethod == null)
        {
            return false;
        }

        object?[] args = { hq, definition, anchor, default(GlobalPosition), attempts };
        if (TryFindSiteNearMethod.Invoke(ecoInstance, args) is bool ok && ok && args[3] is GlobalPosition found)
        {
            site = found;
            return true;
        }

        return false;
    }

    internal static bool TryFindEnemyBuildSite(
        object? ecoInstance,
        FactionHQ hq,
        BuildingDefinition definition,
        ref GlobalPosition site)
    {
        if (ecoInstance == null || TryFindEnemyBuildSiteMethod == null)
        {
            return false;
        }

        object?[] args = { hq, definition, site };
        bool ok = TryFindEnemyBuildSiteMethod.Invoke(ecoInstance, args) is bool b && b;
        if (args[2] is GlobalPosition found)
        {
            site = found;
        }

        return ok;
    }

    internal static bool TryFindShoreSite(
        object? ecoInstance,
        FactionHQ hq,
        BuildingDefinition dockDefinition,
        out GlobalPosition site)
    {
        site = default;
        if (ecoInstance == null || TryFindShoreSiteMethod == null)
        {
            return false;
        }

        object?[] args = { hq, dockDefinition, default(GlobalPosition) };
        if (TryFindShoreSiteMethod.Invoke(ecoInstance, args) is bool ok && ok && args[2] is GlobalPosition found)
        {
            site = found;
            return true;
        }

        return false;
    }

    internal static bool QueueEnemyStructureBlueprint(
        object? ecoInstance,
        FactionHQ hq,
        BuildingDefinition definition,
        GlobalPosition position,
        float cost,
        string label)
    {
        if (ecoInstance == null || QueueEnemyBlueprintMethod == null || BuildKindStructureValue == null)
        {
            return false;
        }

        object?[] args = { hq, BuildKindStructureValue, definition, null, position, cost, label, null };
        return QueueEnemyBlueprintMethod.Invoke(ecoInstance, args) is bool ok && ok;
    }

    internal static bool IsRotaryAirframe(AircraftDefinition? def)
        => def != null && IsRotaryAirframeMethod?.Invoke(null, new object?[] { def }) is bool b && b;

    internal static AircraftDefinition? GetSelectedAirOptionDefinition(object? airCommandService)
    {
        if (airCommandService == null || AirSelectedOptionProp == null || AirMissionOptionDefinitionProp == null)
        {
            return null;
        }

        object? selectedOption = AirSelectedOptionProp.GetValue(airCommandService);
        return selectedOption != null
            ? AirMissionOptionDefinitionProp.GetValue(selectedOption) as AircraftDefinition
            : null;
    }

    internal static int GetConfiguredArtilleryCap()
        => UnitCapArtilleryCapGetter?.Invoke(null, null) is int cap ? cap : 0;

    internal static bool CanSlingLift(Unit? unit)
        => unit != null && SlingCanLiftMethod?.Invoke(null, new object?[] { unit }) is bool ok && ok;

    internal static bool IsSlingLifting(Aircraft? aircraft)
    {
        if (aircraft == null)
        {
            return false;
        }

        object? inst = SlingLiftInstanceProp?.GetValue(null);
        return inst != null && SlingIsLiftingMethod?.Invoke(inst, new object?[] { aircraft }) is bool ok && ok;
    }

    internal static bool SetAircraftMoveOrder(Aircraft? aircraft, GlobalPosition point)
    {
        if (aircraft == null || AirSetAircraftOrderMethod == null)
        {
            return false;
        }

        object? inst = AirInstanceProp?.GetValue(null);
        if (inst == null)
        {
            return false;
        }

        AirSetAircraftOrderMethod.Invoke(inst, new object?[] { aircraft, point, null, false, null });
        return true;
    }

    internal static bool TryLaunchAiAircraft(
        FactionHQ hq,
        Airbase airbase,
        AircraftDefinition definition,
        LiveryKey livery,
        NuclearOption.SavedMission.Loadout loadout,
        float fuel,
        Hangar? hangar = null)
    {
        if (hq == null || airbase == null || definition == null || AirLaunchAiAircraftMethod == null)
        {
            return false;
        }

        return AirLaunchAiAircraftMethod.Invoke(null, new object?[] { hq, airbase, definition, livery, loadout, fuel, hangar }) is bool ok && ok;
    }

    internal static bool IsLeviathanMission()
        => IsLeviathanMissionProp?.GetValue(null) is bool b && b;

    internal static bool IsGodBudget()
        => IsGodBudgetMethod?.Invoke(null, null) is bool b && b;

    internal static void NotifyAlert(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        object? inst = AlertInstanceProp?.GetValue(null);
        if (inst != null && AlertNotifyCaptureMethod != null)
        {
            AlertNotifyCaptureMethod.Invoke(inst, new object?[] { text });
            return;
        }

        NoticeFail(text);
    }

    internal static bool GetChampionsDealt(object? ecoInstance)
    {
        if (ecoInstance == null)
        {
            return false;
        }

        if (EcoChampionsDealtProp != null && EcoChampionsDealtProp.GetValue(ecoInstance) is bool propVal)
        {
            return propVal;
        }

        FieldInfo? legacyField = AccessTools.Field(EconomyServiceType, "championsDealt");
        return legacyField?.GetValue(ecoInstance) is bool fieldVal && fieldVal;
    }

    internal static void SetChampionsDealt(object? ecoInstance, bool value)
    {
        if (ecoInstance == null)
        {
            return;
        }

        if (EcoChampionsDealtProp != null)
        {
            EcoChampionsDealtProp.SetValue(ecoInstance, value);
            return;
        }

        AccessTools.Field(EconomyServiceType, "championsDealt")?.SetValue(ecoInstance, value);
    }

    internal static string[] GetChampionsStartBases(bool godBudget)
    {
        FieldInfo? field = godBudget ? EcoGodStartBasesField : EcoChampionsStartBasesField;
        return field?.GetValue(null) as string[] ?? Array.Empty<string>();
    }

    internal static string[] GetChampionKitKeys()
        => EcoChampionKitKeysField?.GetValue(null) as string[] ?? Array.Empty<string>();

    internal static void DealRadarStations(GlobalPosition home, FactionHQ localHq, FactionHQ enemyHq)
        => EcoDealRadarStationsMethod?.Invoke(null, new object?[] { home, localHq, enemyHq });

    internal static BuildingDefinition? FindBuildingByKey(Encyclopedia? enc, string key)
    {
        if (enc == null || string.IsNullOrEmpty(key) || EcoFindBuildingMethod == null)
        {
            return null;
        }

        return EcoFindBuildingMethod.Invoke(null, new object?[] { enc, new[] { key } }) as BuildingDefinition;
    }

    internal static bool TryPlaceKitBuilding(
        object? ecoInstance,
        FactionHQ hq,
        BuildingDefinition definition,
        GlobalPosition anchor,
        out bool byRoad)
    {
        byRoad = false;
        if (ecoInstance == null || EcoTryPlaceKitBuildingMethod == null)
        {
            return false;
        }

        object?[] args = { hq, definition, anchor, false };
        bool ok = EcoTryPlaceKitBuildingMethod.Invoke(ecoInstance, args) is bool b && b;
        if (args[3] is bool roadFlag)
        {
            byRoad = roadFlag;
        }

        return ok;
    }

    internal static void GiveEnemyBasesDepots(object? ecoInstance, FactionHQ enemyHq)
    {
        if (ecoInstance != null && enemyHq != null)
        {
            EcoGiveEnemyBasesDepotsMethod?.Invoke(ecoInstance, new object?[] { enemyHq });
        }
    }

    internal static void SetEconomyStatusText(string text)
    {
        object? eco = GetEcoInstance();
        if (eco != null)
        {
            EcoStatusTextSetter?.Invoke(eco, new object?[] { text ?? string.Empty });
        }
    }

    internal static Rect GetEconomyUiWindowRect(object? ecoUiInstance)
        => ecoUiInstance != null && EcoUiWindowRectField?.GetValue(ecoUiInstance) is Rect rect ? rect : default;

    internal static Unit? GetFocusedSelection()
    {
        object? inst = SelectionInstanceProp?.GetValue(null);
        return inst != null ? SelectionFocusedProp?.GetValue(inst) as Unit : null;
    }

    internal static IReadOnlyList<Unit>? GetSelectedUnits()
    {
        object? inst = SelectionInstanceProp?.GetValue(null);
        return inst != null ? SelectionSelectedUnitsProp?.GetValue(inst) as IReadOnlyList<Unit> : null;
    }

    internal static bool TryGetSelectionAnchorRect(out Rect selectionRect, out float topY)
    {
        selectionRect = default;
        topY = 0f;

        object? overlay = OverlayInstanceProp?.GetValue(null);
        if (overlay == null || OverlaySelectionBarRectField?.GetValue(overlay) is not Rect barRect || barRect.width <= 10f)
        {
            return false;
        }

        selectionRect = barRect;
        topY = barRect.y;

        if (OverlaySelectionMoreVisibleField?.GetValue(overlay) is bool moreVisible &&
            moreVisible &&
            OverlaySelectionMoreRectField?.GetValue(overlay) is Rect moreRect &&
            moreRect.width > 10f)
        {
            topY = Mathf.Min(topY, moreRect.y);
        }

        return true;
    }

    internal static bool IsUnitHoldingFire(Unit? unit)
        => unit != null && MoveIsHoldingFireMethod?.Invoke(null, new object?[] { unit }) is bool holding && holding;

    internal static void SetUnitHoldFireStance(Unit? unit, bool holdFire)
    {
        if (unit == null || StanceType == null)
        {
            return;
        }

        object? moveInst = MoveInstanceProp?.GetValue(null);
        if (moveInst == null)
        {
            return;
        }

        try
        {
            object stanceVal = Enum.Parse(StanceType, holdFire ? "HoldFire" : "FreeEngage");
            if (MoveApplyStanceMethod != null)
            {
                MoveApplyStanceMethod.Invoke(moveInst, new object?[] { unit, stanceVal });
            }
            else if (MoveStancesField?.GetValue(moveInst) is IDictionary dict)
            {
                if (holdFire)
                {
                    dict[unit] = stanceVal;
                }
                else
                {
                    dict.Remove(unit);
                }
            }

            MoveRefreshOrderFlagsMethod?.Invoke(moveInst, null);
        }
        catch (Exception ex)
        {
            AddonLog.Debug($"[AI] Failed to set hold-fire stance on '{unit.unitName}': {ex.Message}");
        }
    }

    internal static bool TrySendRepairCrew(Unit? building)
    {
        if (building == null || RepairSendRepairCrewMethod == null)
        {
            return false;
        }

        object? repairInst = RepairInstanceProp?.GetValue(null);
        return repairInst != null && RepairSendRepairCrewMethod.Invoke(repairInst, new object?[] { building }) is bool ok && ok;
    }

    internal static bool HasRepairCrewAssigned(Unit? building)
    {
        if (building == null || RepairHasCrewAssignedMethod == null)
        {
            return false;
        }

        object? repairInst = RepairInstanceProp?.GetValue(null);
        return repairInst != null && RepairHasCrewAssignedMethod.Invoke(repairInst, new object?[] { building }) is bool assigned && assigned;
    }

    internal static void RemoveFromEconomyBuiltUnits(Unit? unit)
    {
        if (unit == null)
        {
            return;
        }

        object? eco = GetEcoInstance();
        if (eco != null && EcoBuiltUnitsField?.GetValue(eco) is ICollection<Unit> builtSet)
        {
            builtSet.Remove(unit);
        }
    }

    internal static void AddToEconomyBuiltUnits(Unit? unit)
    {
        if (unit == null)
        {
            return;
        }

        object? eco = GetEcoInstance();
        if (eco != null && EcoBuiltUnitsField?.GetValue(eco) is ICollection<Unit> builtSet)
        {
            builtSet.Add(unit);
        }
    }

    internal static void SetMobileEmplacementStatus(string text)
    {
        object? inst = MobileEmplacementInstanceProp?.GetValue(null);
        if (inst != null && MobileEmplacementSetStatusMethod != null)
        {
            MobileEmplacementSetStatusMethod.Invoke(inst, new object?[] { text ?? string.Empty });
        }
    }

    internal static void SetUnitHoldPosition(Unit? unit, bool hold)
    {
        if (unit != null && GameAccessSetUnitHoldPositionMethod != null)
        {
            GameAccessSetUnitHoldPositionMethod.Invoke(null, new object?[] { unit, hold });
        }
    }

    internal static bool GetMoveHasCommandableSelection(object? moveService)
        => moveService != null && MoveHasCommandableSelectionGetter?.Invoke(moveService, null) is bool b && b;

    internal static bool GetMoveHasSelectedAircraft(object? moveService)
        => moveService != null && MoveHasSelectedAircraftGetter?.Invoke(moveService, null) is bool b && b;

    internal static void InvokeMoveSetSelectionStance(object? moveService, bool holdFire)
    {
        if (moveService == null || StanceType == null || MoveSetSelectionStanceMethod == null)
        {
            return;
        }

        object stanceVal = Enum.Parse(StanceType, holdFire ? "HoldFire" : "FreeEngage");
        MoveSetSelectionStanceMethod.Invoke(moveService, new object?[] { stanceVal });
    }

    internal static bool IsVanillaUnitCapEnabled()
        => UnitCapEnabledGetter?.Invoke(null, null) is bool enabled && enabled;

    internal static (int TotalCap, int GroundCap, int AirCap, int ShipCap) GetVanillaCaps()
    {
        int totalCap = UnitCapCapGetter?.Invoke(null, null) is int tc ? tc : 0;
        int groundCap = UnitCapGroundCapGetter?.Invoke(null, null) is int gc ? gc : 0;
        int airCap = UnitCapAircraftCapGetter?.Invoke(null, null) is int ac ? ac : 0;
        int shipCap = UnitCapShipCapGetter?.Invoke(null, null) is int sc ? sc : 0;
        return (totalCap, groundCap, airCap, shipCap);
    }

    internal static void InvokeEconomyCancelBuild(object? ecoInstance)
    {
        if (ecoInstance != null && CancelBuildMethod != null)
        {
            CancelBuildMethod.Invoke(ecoInstance, null);
        }
    }

    internal static void InvokeSupplyHeliSetStatus(object? supplyService, string text)
    {
        if (supplyService != null && SupplyHeliSetStatusMethod != null)
        {
            SupplyHeliSetStatusMethod.Invoke(supplyService, new object?[] { text ?? string.Empty });
        }
    }

    internal static void InvokeSupplyHeliCancelTargetSelection(object? supplyService, bool showStatus)
    {
        if (supplyService != null && SupplyHeliCancelTargetSelectionMethod != null)
        {
            SupplyHeliCancelTargetSelectionMethod.Invoke(supplyService, new object?[] { showStatus });
        }
    }

    internal static void CollectSupplyHeliSelectedMounts(object? supplyService, List<WeaponMount> results)
    {
        results.Clear();
        if (supplyService == null || SupplyHeliSelectedAircraftProp == null || CargoAircraftCargoSlotsProp == null)
        {
            return;
        }

        object? selectedAircraft = SupplyHeliSelectedAircraftProp.GetValue(supplyService);
        if (selectedAircraft == null || CargoAircraftCargoSlotsProp.GetValue(selectedAircraft) is not IList slots)
        {
            return;
        }

        HardpointSet[]? sets = CargoAircraftHardpointSetsProp?.GetValue(selectedAircraft) as HardpointSet[];
        for (int i = 0; i < slots.Count; i++)
        {
            object? slot = slots[i];
            if (slot == null)
            {
                continue;
            }

            if (CargoSlotSelectedMountProp?.GetValue(slot) is not WeaponMount mount)
            {
                continue;
            }

            int repeat = 1;
            if (sets != null && CargoSlotHardpointIndexProp?.GetValue(slot) is int hpIdx && hpIdx >= 0 && hpIdx < sets.Length)
            {
                int hpCount = sets[hpIdx]?.hardpoints?.Count ?? 0;
                if (hpCount > 1)
                {
                    repeat = hpCount;
                }
            }

            for (int r = 0; r < repeat; r++)
            {
                results.Add(mount);
            }
        }
    }

    internal static void CollectSupplyHeliPendingLoadoutMounts(object? supplyService, List<WeaponMount> results)
    {
        results.Clear();
        if (supplyService == null || SupplyHeliPendingTargetSelectionField == null || PendingTargetSelectionLoadoutProp == null)
        {
            return;
        }

        object? pending = SupplyHeliPendingTargetSelectionField.GetValue(supplyService);
        if (pending == null)
        {
            return;
        }

        if (PendingTargetSelectionLoadoutProp.GetValue(pending) is not NuclearOption.SavedMission.Loadout loadout ||
            loadout.weapons == null)
        {
            return;
        }

        object? pendingAircraft = PendingTargetSelectionAircraftProp?.GetValue(pending);
        HardpointSet[]? sets = pendingAircraft != null
            ? CargoAircraftHardpointSetsProp?.GetValue(pendingAircraft) as HardpointSet[]
            : null;

        for (int i = 0; i < loadout.weapons.Count; i++)
        {
            WeaponMount? mount = loadout.weapons[i];
            if (mount == null)
            {
                continue;
            }

            int repeat = 1;
            if (sets != null && i < sets.Length)
            {
                int hpCount = sets[i]?.hardpoints?.Count ?? 0;
                if (hpCount > 1)
                {
                    repeat = hpCount;
                }
            }

            for (int r = 0; r < repeat; r++)
            {
                results.Add(mount);
            }
        }
    }

    internal static int EnemySamSiteCount(object? state)
        => state != null && EnemySamSiteCountMethod?.Invoke(null, new object?[] { state }) is int n ? n : 1;

    internal static bool EnemyIsSamSiteLauncher(Unit? unit)
        => unit != null && EnemyIsSamSiteLauncherUnitMethod?.Invoke(null, new object?[] { unit }) is bool b && b;

    internal static bool EnemyIsSamSiteLauncher(VehicleDefinition? def)
        => def != null && EnemyIsSamSiteLauncherDefMethod?.Invoke(null, new object?[] { def }) is bool b && b;

    internal static bool EnemyIsRearmVehicle(Unit? unit)
        => unit != null && EnemyIsRearmVehicleMethod?.Invoke(null, new object?[] { unit }) is bool b && b;

    internal static bool EnemyIsRadarVehicle(Unit? unit)
        => unit != null && EnemyIsRadarVehicleMethod?.Invoke(null, new object?[] { unit }) is bool b && b;

    internal static GlobalPosition EnemyGetHomeBase(object? state, Unit? unit)
        => state != null && unit != null && EnemyGetHomeBaseMethod?.Invoke(null, new object?[] { state, unit }) is GlobalPosition pos
            ? pos
            : default;

    internal static bool EnemyIsPostAt(object? state, int post, GlobalPosition baseCentre)
        => state != null && EnemyIsPostAtMethod?.Invoke(null, new object?[] { state, post, baseCentre }) is bool b && b;

    internal static VehicleDefinition? EnemyChooseSupplyTruck(object? enemyInstance, float budget)
        => enemyInstance != null ? EnemyChooseSupplyTruckMethod?.Invoke(enemyInstance, new object?[] { budget }) as VehicleDefinition : null;

    internal static float GetUnitAmmo(Unit? unit)
        => unit != null && GameAccessGetUnitAmmoMethod?.Invoke(null, new object?[] { unit }) is float v ? v : -1f;

    internal static float GetSupplyAmmoFraction(Unit? unit)
        => unit != null && SupplyStoreGetAmmoFractionMethod?.Invoke(null, new object?[] { unit }) is float v ? v : 0f;

    internal static Rearmer? GetRearmer(Unit? unit)
        => unit != null ? SupplyStoreGetRearmerMethod?.Invoke(null, new object?[] { unit }) as Rearmer : null;

    internal static string RefillTruck(Unit? truck, Unit? store)
    {
        object? inst = SupplyStoreInstanceProp?.GetValue(null);
        if (inst == null || truck == null || store == null)
        {
            return string.Empty;
        }

        return SupplyStoreRefillTruckMethod?.Invoke(inst, new object?[] { truck, store }) as string ?? string.Empty;
    }

    internal static int TopUpWeapons(Rearmer? rearmer, Unit? unit)
        => rearmer != null && unit != null && SupplyStoreTopUpWeaponsMethod?.Invoke(null, new object?[] { rearmer, unit }) is int n ? n : 0;

    internal static void CollectSupplyStores(FactionHQ? hq, List<Unit> into)
    {
        into.Clear();
        if (hq == null)
        {
            return;
        }

        SupplyStoreCollectStoresMethod?.Invoke(null, new object?[] { hq, into });
    }

    internal static bool IsSupplyDump(Unit? unit)
        => unit != null && SupplyStoreIsDumpMethod?.Invoke(null, new object?[] { unit }) is bool b && b;

    internal static float EcoPriceOf(UnitDefinition? def)
        => def != null && EcoPriceOfMethod?.Invoke(null, new object?[] { def }) is float price ? price : (def != null ? def.value : 0f);

    internal static void NetworkRunAs(FactionHQ hq, Action action)
    {
        if (NetworkRunAsMethod != null)
        {
            NetworkRunAsMethod.Invoke(null, new object?[] { hq, action });
        }
    }

    internal static void NetworkRunAsHostTeam(Action action)
    {
        if (action == null)
        {
            return;
        }

        if (NetworkRunAsHostTeamMethod != null)
        {
            NetworkRunAsHostTeamMethod.Invoke(null, new object?[] { action });
        }
        else
        {
            action();
        }
    }

    internal static bool TryGetSupplyHeliAmmoCapacity(WeaponMount? mount, out float capacity)
    {
        capacity = 0f;
        if (mount == null || SupplyHeliTryGetAmmunitionCargoCapacityMethod == null)
        {
            return false;
        }

        object?[] args = { mount, 0f };
        bool ok = SupplyHeliTryGetAmmunitionCargoCapacityMethod.Invoke(null, args) is bool b && b;
        if (args[1] is float c)
        {
            capacity = c;
        }

        return ok;
    }

    internal static object? GetAirInstance() => AirInstanceProp?.GetValue(null);

    internal static int EnemyCountPickets(FactionHQ hq)
        => hq != null && EnemyCountPicketsMethod?.Invoke(null, new object?[] { hq }) is int n ? n : 0;

    internal static int EnemyCountAirborne(FactionHQ hq)
        => hq != null && EnemyCountAirborneMethod?.Invoke(null, new object?[] { hq }) is int n ? n : 0;

    internal static int EnemyGetAirborneLimit()
        => EnemyGetAirborneLimitMethod?.Invoke(null, null) is int n ? n : 30;

    internal static bool EnemyHasAirStock(FactionHQ hq)
        => hq != null && EnemyHasAirStockMethod?.Invoke(null, new object?[] { hq }) is bool b && b;

    internal static float EnemyTryBuyPicket(object? enemyInstance, FactionHQ hq, object? state, float budget, out bool available)
    {
        available = false;
        if (enemyInstance == null || hq == null || state == null || EnemyTryBuyPicketMethod == null)
        {
            return 0f;
        }

        object?[] args = { hq, state, budget, false };
        float spent = EnemyTryBuyPicketMethod.Invoke(enemyInstance, args) is float v ? v : 0f;
        if (args[3] is bool avail)
        {
            available = avail;
        }

        return spent;
    }

    internal static bool EnemyIsRadarPicket(AircraftDefinition? def, FactionHQ? hq)
        => def != null && hq != null && EnemyIsRadarPicketMethod?.Invoke(null, new object?[] { def, hq }) is bool b && b;

    internal static int EnemyGetAirRoleInt(Aircraft? aircraft)
        => aircraft != null && EnemyGetAirRoleAircraftMethod?.Invoke(null, new object?[] { aircraft }) is object roleObj
            ? Convert.ToInt32(roleObj)
            : 0;

    internal static bool EnemyCanAnyStoreDefeat(Aircraft? aircraft, Unit? target)
        => aircraft != null && target != null && EnemyCanAnyStoreDefeatMethod?.Invoke(null, new object?[] { aircraft, target }) is bool b && b;

    internal static GlobalPosition EnemyFindHighGround(GlobalPosition around)
        => EnemyFindHighGroundMethod?.Invoke(null, new object?[] { around }) is GlobalPosition pos ? pos : SnapToTerrain(around);

    internal static void EnemyPinTo(Unit? unit, GlobalPosition spot)
    {
        if (unit != null && EnemyPinToMethod != null)
        {
            EnemyPinToMethod.Invoke(null, new object?[] { unit, spot });
        }
    }

    internal static void EnemyHoldAt(Unit? unit, GlobalPosition spot)
    {
        if (unit != null && EnemyHoldAtMethod != null)
        {
            EnemyHoldAtMethod.Invoke(null, new object?[] { unit, spot });
        }
    }

    internal static void EnemyReleaseCommandedDestination(Unit? unit)
    {
        if (unit != null && EnemyReleaseCommandedDestinationMethod != null)
        {
            EnemyReleaseCommandedDestinationMethod.Invoke(null, new object?[] { unit });
        }
    }

    internal static bool EnemyIsDefendingUnit(Unit? unit)
        => unit != null && EnemyIsDefendingUnitMethod?.Invoke(null, new object?[] { unit }) is bool b && b;

    internal static GlobalPosition CaptureGetTerritoryCenter(FactionHQ hq)
        => hq != null && CaptureGetTerritoryCenterMethod?.Invoke(null, new object?[] { hq }) is GlobalPosition pos ? pos : default;

    internal static GlobalPosition CaptureGetHoldPoint(Airbase? airbase)
        => airbase != null && CaptureGetHoldPointMethod?.Invoke(null, new object?[] { airbase }) is GlobalPosition pos
            ? pos
            : (airbase != null && airbase.center != null ? airbase.center.GlobalPosition() : default);

    internal static float CaptureGetRoadMeters(Airbase? from, Airbase? to)
        => from != null && to != null && CaptureGetRoadMetersMethod?.Invoke(null, new object?[] { from, to }) is float m
            ? m
            : (from != null && to != null && from.center != null && to.center != null
                ? FastMath.Distance(from.center.GlobalPosition(), to.center.GlobalPosition())
                : float.MaxValue);

    internal static int CaptureCountDefenders(Airbase? target, GlobalPosition hold, float ring)
        => target != null && CaptureCountDefendersMethod?.Invoke(null, new object?[] { target, hold, ring }) is int n ? n : 0;

    internal static void CaptureReleaseDrive(object? drive)
    {
        if (drive != null && CaptureReleaseDriveMethod != null)
        {
            CaptureReleaseDriveMethod.Invoke(null, new object?[] { drive });
        }
    }

    internal static bool CaptureIsOnEnemyDrive(Unit? unit)
    {
        object? inst = GetCaptureInstance();
        return inst != null && unit != null && CaptureIsOnEnemyDriveMethod?.Invoke(inst, new object?[] { unit }) is bool b && b;
    }

    internal static bool EcoIsIncomeBuildingUnit(Unit? unit)
        => unit != null && EcoIsIncomeBuildingUnitMethod?.Invoke(null, new object?[] { unit }) is bool b && b;

    internal static bool EcoTryGetNavalDockPosition(FactionHQ? hq, out GlobalPosition pos)
    {
        pos = default;
        if (hq == null || EcoTryGetNavalDockPositionMethod == null)
        {
            return false;
        }

        object?[] args = { hq, default(GlobalPosition) };
        if (EcoTryGetNavalDockPositionMethod.Invoke(null, args) is bool ok && ok && args[1] is GlobalPosition found)
        {
            pos = found;
            return true;
        }

        return false;
    }

    internal static bool AirHasPlanePilot(Aircraft? aircraft)
        => aircraft != null && AirHasPlanePilotMethod?.Invoke(null, new object?[] { aircraft }) is bool b && b;

    internal static bool AirHasRotaryPilot(Aircraft? aircraft)
        => aircraft != null && AirHasRotaryPilotMethod?.Invoke(null, new object?[] { aircraft }) is bool b && b;

    internal static bool AirHasBombAmmo(Aircraft? aircraft, bool glideOnly)
        => aircraft != null && AirHasBombAmmoMethod?.Invoke(null, new object?[] { aircraft, glideOnly }) is bool b && b;

    internal static bool AirIsAradWeapon(WeaponInfo? info)
        => info != null && AirIsAradWeaponMethod?.Invoke(null, new object?[] { info }) is bool b && b;

    internal static bool AirCarriesStrategicStore(Aircraft? aircraft, bool includeNuclear = false)
        => aircraft != null && AirCarriesStrategicStoreMethod?.Invoke(null, new object?[] { aircraft, includeNuclear }) is bool b && b;

    internal static Unit? AirGetForcedTarget(Aircraft? aircraft)
    {
        object? inst = GetAirInstance();
        return inst != null && aircraft != null ? AirGetForcedTargetMethod?.Invoke(inst, new object?[] { aircraft }) as Unit : null;
    }

    internal static void AirSetAiCommitted(Aircraft? aircraft, bool committed)
    {
        object? inst = GetAirInstance();
        if (inst != null && aircraft != null && AirSetAiCommittedMethod != null)
        {
            AirSetAiCommittedMethod.Invoke(inst, new object?[] { aircraft, committed });
        }
    }

    internal static void AirSetAiRoute(Aircraft? aircraft, GlobalPosition point)
    {
        object? inst = GetAirInstance();
        if (inst != null && aircraft != null && AirSetAiRouteMethod != null)
        {
            AirSetAiRouteMethod.Invoke(inst, new object?[] { aircraft, point });
        }
    }

    internal static void AirClearAiRoute(Aircraft? aircraft)
    {
        object? inst = GetAirInstance();
        if (inst != null && aircraft != null && AirClearAiRouteMethod != null)
        {
            AirClearAiRouteMethod.Invoke(inst, new object?[] { aircraft });
        }
    }

    internal static bool AirTrySetAiForcedTarget(Aircraft? aircraft, Unit? target)
    {
        object? inst = GetAirInstance();
        return inst != null && aircraft != null && target != null
            && AirTrySetAiForcedTargetMethod?.Invoke(inst, new object?[] { aircraft, target }) is bool b && b;
    }

    internal static void AirClearAiForcedTarget(Aircraft? aircraft)
    {
        object? mission = GetAirMission(aircraft);
        if (mission != null)
        {
            AirMissionForcedTargetProp?.SetValue(mission, null);
        }
    }

    internal static bool AirIsMissionReturning(Aircraft? aircraft)
    {
        object? mission = GetAirMission(aircraft);
        return mission != null && AirMissionReturningProp?.GetValue(mission) is bool b && b;
    }

    internal static Airbase? AirGetMissionLandingBase(Aircraft? aircraft)
    {
        object? mission = GetAirMission(aircraft);
        return mission != null ? AirMissionLandingBaseProp?.GetValue(mission) as Airbase : null;
    }

    internal static int AirGetMissionModeInt(Aircraft? aircraft)
    {
        object? mission = GetAirMission(aircraft);
        object? mode = mission != null ? AirMissionModeProp?.GetValue(mission) : null;
        return mode != null ? Convert.ToInt32(mode) : -1;
    }

    private static object? GetAirMission(Aircraft? aircraft)
    {
        if (aircraft == null)
        {
            return null;
        }

        object? inst = GetAirInstance();
        if (inst != null
            && AirMissionsField?.GetValue(inst) is IDictionary missions
            && missions.Contains(aircraft))
        {
            return missions[aircraft];
        }

        return null;
    }

    internal static bool AirHasCombatAmmo(Aircraft? aircraft)
    {
        if (aircraft == null)
        {
            return false;
        }

        List<WeaponStation>? stations = ((Unit)aircraft).weaponStations;
        if (stations == null)
        {
            return false;
        }

        for (int i = 0; i < stations.Count; i++)
        {
            WeaponStation? ws = stations[i];
            if (ws != null && !ws.Cargo && ws.Ammo > 0)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool EnemyCanLaunchFrom(Airbase? airbase, FactionHQ? hq, AircraftDefinition? definition)
        => airbase != null && hq != null && definition != null
            && EnemyCanLaunchFromMethod?.Invoke(null, new object?[] { airbase, hq, definition }) is bool b && b;

    internal static bool AirHasTakeoffRunway(Airbase? airbase, AircraftDefinition? definition, bool countOverruns = true)
        => airbase != null && definition != null
            && AirHasTakeoffRunwayMethod?.Invoke(null, new object?[] { airbase, definition, countOverruns }) is bool b && b;

    internal static bool EcoIsBuilder(Unit? unit)
        => unit != null && EcoIsBuilderMethod?.Invoke(null, new object?[] { unit }) is bool b && b;

    internal static void EcoEnsureEnemySiteBunker(FactionHQ? hq, GlobalPosition site)
    {
        object? eco = GetEcoInstance();
        if (eco != null && hq != null && EcoEnsureEnemySiteBunkerMethod != null)
        {
            EcoEnsureEnemySiteBunkerMethod.Invoke(eco, new object?[] { hq, site });
        }
    }

    internal static bool RepairHasAssignedTarget(Unit? crew)
    {
        object? repair = RepairInstanceProp?.GetValue(null);
        if (repair == null || crew == null || RepairTryGetAssignedTargetMethod == null)
        {
            return false;
        }

        object?[] args = { crew, null };
        return RepairTryGetAssignedTargetMethod.Invoke(repair, args) is bool ok && ok;
    }

    internal static bool IsSiteKindFactory(object? site)
        => site != null && BuildKindFactoryValue != null && Equals(SiteKindProp?.GetValue(site), BuildKindFactoryValue);

    internal static void CaptureClearDriveTarget(object? drive)
    {
        if (drive == null)
        {
            return;
        }

        DriveTargetProp?.SetValue(drive, null);
        DriveOwnerProp?.SetValue(drive, null);
        DriveAnnouncedProp?.SetValue(drive, 0);
        DriveMarchSinceProp?.SetValue(drive, 0f);
    }

    internal static bool AirRetargetAiMission(Aircraft? aircraft, int modeInt, GlobalPosition center, float radius)
    {
        object? inst = GetAirInstance();
        if (inst == null || aircraft == null || AirRetargetAiMissionMethod == null || AirCommandModeType == null)
        {
            return false;
        }

        object modeVal = Enum.ToObject(AirCommandModeType, modeInt);
        return AirRetargetAiMissionMethod.Invoke(inst, new object?[] { aircraft, modeVal, center, radius }) is bool b && b;
    }

    internal static bool NavalTryGetSeaLanePoint(GlobalPosition position, out GlobalPosition lanePoint)
    {
        lanePoint = default;
        if (NavalTryGetSeaLanePointMethod == null)
        {
            return false;
        }

        object?[] args = { position, default(GlobalPosition) };
        if (NavalTryGetSeaLanePointMethod.Invoke(null, args) is bool ok && ok && args[1] is GlobalPosition found)
        {
            lanePoint = found;
            return true;
        }

        return false;
    }

    internal static bool TerrainRouteIsDrivable(GlobalPosition from, GlobalPosition to)
        => TerrainRouteIsDrivableMethod?.Invoke(null, new object?[] { from, to }) is bool b && b;

    internal static bool BuildPreviewIsOnLevelRoad(GlobalPosition target, float clearance)
        => BuildPreviewIsOnLevelRoadMethod?.Invoke(null, new object?[] { target, clearance }) is bool b && b;

    internal static void ClearTargetsUsingRadar(Radar? radar)
    {
        if (radar != null && ClearTargetsUsingRadarMethod != null)
        {
            ClearTargetsUsingRadarMethod.Invoke(null, new object?[] { radar });
        }
    }

    internal static float EcoGetNavalDockBuildCost()
        => EcoNavalDockBuildCostProp?.GetValue(null) is float c ? c : 600f;

    internal static float GetSettingsBuildRadiusKm()
        => SettingsBuildRadiusKmGetter?.Invoke(null, null) is float r && r > 0.1f ? r : 2.5f;

    internal static float EcoGetGasTerminalTier3Income()
        => BuildKindGasTerminalValue != null ? GetStageIncomePerMinute(BuildKindGasTerminalValue, 3) : 75f;

    internal static BuildingDefinition? EcoResolveNavalDockDefinition(object? ecoInstance)
        => ecoInstance != null && BuildKindNavalDockValue != null
            ? EcoResolveDefinitionMethod?.Invoke(ecoInstance, new object?[] { BuildKindNavalDockValue }) as BuildingDefinition
            : null;

    internal static float GetStructureCost(BuildingDefinition? def)
        => def != null && GetStructureCostMethod?.Invoke(null, new object?[] { def }) is float c ? c : 750f;

    internal static string GetStructureLabel(BuildingDefinition? def)
        => def != null && GetStructureLabelMethod?.Invoke(null, new object?[] { def }) is string s && !string.IsNullOrWhiteSpace(s)
            ? s
            : (def?.unitName ?? "Structure");

    internal static void EcoClearShoreSearchFailed(object? ecoInstance, FactionHQ? hq)
    {
        if (ecoInstance != null && hq != null && EcoShoreSearchFailedField?.GetValue(ecoInstance) is Dictionary<FactionHQ, int> map)
        {
            map.Remove(hq);
        }
    }

    internal static object? EcoGetPreview(object? ecoInstance)
        => ecoInstance != null ? EcoPreviewField?.GetValue(ecoInstance) : null;

    internal static Dictionary<Unit, float>? EcoGetBuilderResting(object? ecoInstance)
        => ecoInstance != null ? EcoBuilderRestingField?.GetValue(ecoInstance) as Dictionary<Unit, float> : null;

    internal static void EcoCollectIdleBuilders(FactionHQ? hq, List<Unit> into)
    {
        into.Clear();
        if (hq != null && EcoCollectIdleBuildersMethod != null)
        {
            EcoCollectIdleBuildersMethod.Invoke(null, new object?[] { hq, into });
        }
    }

    internal static bool EcoHasBusyBuilderNear(object? ecoInstance, FactionHQ? hq, GlobalPosition site)
        => ecoInstance != null && hq != null && EcoHasBusyBuilderNearMethod?.Invoke(ecoInstance, new object?[] { hq, site }) is bool b && b;

    internal static void EcoSendBuilder(object? site, Unit? builder)
    {
        if (site != null && builder != null && EcoSendBuilderMethod != null)
        {
            EcoSendBuilderMethod.Invoke(null, new object?[] { site, builder });
        }
    }

    internal static int EcoCountNavalDockBlueprints(FactionHQ? hq)
        => hq != null && BuildKindNavalDockValue != null && EcoCountBlueprintsMethod?.Invoke(null, new object?[] { hq, BuildKindNavalDockValue }) is int n ? n : 0;

    internal static bool EcoIsWater(GlobalPosition anchor, Vector3 direction, float distance)
        => EcoIsWaterMethod?.Invoke(null, new object?[] { anchor, direction, distance }) is bool b && b;

    internal static bool EcoIsOpenWater(GlobalPosition anchor, Vector3 direction, float distance)
        => EcoIsOpenWaterMethod?.Invoke(null, new object?[] { anchor, direction, distance }) is bool b && b;

    internal static bool EcoReachesSea(GlobalPosition start)
        => EcoReachesSeaMethod?.Invoke(null, new object?[] { start }) is bool b && b;

    internal static bool EcoIsNearBuiltTower(FactionHQ? hq, GlobalPosition target)
        => hq != null && EcoIsNearBuiltTowerMethod?.Invoke(null, new object?[] { hq, target }) is bool b && b;

    internal static bool IsSiteKindNavalDock(object? site)
        => site != null && BuildKindNavalDockValue != null && Equals(SiteKindProp?.GetValue(site), BuildKindNavalDockValue);

    internal static List<Unit>? SiteGetBuilders(object? site)
        => site != null ? SiteBuildersProp?.GetValue(site) as List<Unit> : null;

    internal static float SiteGetNextBuilderSpawnAt(object? site)
        => site != null && SiteNextBuilderSpawnAtProp?.GetValue(site) is float t ? t : 0f;

    internal static void SiteSetNextBuilderSpawnAt(object? site, float nextSpawnAt)
    {
        if (site != null && SiteNextBuilderSpawnAtProp != null)
        {
            SiteNextBuilderSpawnAtProp.SetValue(site, nextSpawnAt);
        }
    }

    internal static bool BuildPreviewIsSiteAllowed(
        object? previewObj,
        BuildingDefinition? definition,
        GlobalPosition target,
        FactionHQ? hq,
        out string reason,
        bool navalDock = false,
        Quaternion? rotation = null)
    {
        reason = string.Empty;
        if (previewObj == null || definition == null || BuildPreviewIsSiteAllowedMethod == null)
        {
            return false;
        }

        object?[] args = { definition, target, hq, string.Empty, navalDock, rotation };
        bool allowed = BuildPreviewIsSiteAllowedMethod.Invoke(previewObj, args) is bool ok && ok;
        reason = args[3] as string ?? string.Empty;
        return allowed;
    }

    internal static bool BuildPreviewIsShoreline(GlobalPosition target)
        => BuildPreviewIsShorelineMethod?.Invoke(null, new object?[] { target }) is bool b && b;

    internal static bool BuildPreviewIsControlTower(BuildingDefinition? def)
        => def != null && BuildPreviewIsControlTowerMethod?.Invoke(null, new object?[] { def }) is bool b && b;

    internal static bool BuildPreviewIsControlTowerUnit(Unit? unit)
        => unit != null && BuildPreviewIsControlTowerUnitMethod?.Invoke(null, new object?[] { unit }) is bool b && b;

    internal static VehicleDefinition? FindRepairVehicleDefinition(FactionHQ? hq)
        => GameAccessFindRepairVehicleDefinitionMethod?.Invoke(null, new object?[] { hq }) as VehicleDefinition;

    internal static bool EcoTryGiveStartingBuilders(object? ecoInstance, FactionHQ? hq, VehicleDepot? depot, int count)
        => ecoInstance != null && hq != null && depot != null && EcoTryGiveStartingBuildersMethod?.Invoke(ecoInstance, new object?[] { hq, depot, count }) is bool ok && ok;

    internal static bool UnitCapIsGroundFull(FactionHQ? hq)
        => hq != null && UnitCapIsGroundFullMethod?.Invoke(null, new object?[] { hq }) is bool b && b;

    internal static void MoveClearOrder(Unit? unit)
    {
        if (unit == null || MoveClearOrderMethod == null)
        {
            return;
        }

        object? inst = MoveInstanceProp?.GetValue(null);
        if (inst != null)
        {
            MoveClearOrderMethod.Invoke(inst, new object?[] { unit });
        }
    }

    internal static bool AirHasSlingLoadout(AircraftDefinition? def)
        => def != null && AirBuildSlingLoadoutMethod?.Invoke(null, new object?[] { def }) != null;

    internal static bool AirCanAiFly(AircraftDefinition? def)
        => def != null && AirCanAiFlyMethod?.Invoke(null, new object?[] { def }) is bool b && b;

    internal static void SetVictoryHomeBase(FactionHQ? hq, Airbase? airbase)
    {
        if (hq == null || airbase == null)
        {
            return;
        }

        if (VictoryHomeBasesField?.GetValue(null) is System.Collections.IDictionary homeBases)
        {
            homeBases[hq] = airbase;
        }
    }

    internal static void EnsureBaseDepots(object? ecoInstance, FactionHQ? hq, Airbase? airbase, int depotsWanted = 2)
    {
        if (ecoInstance == null
            || hq == null
            || airbase == null
            || EcoPlaceBaseDepotsMethod == null
            || Encyclopedia.i == null)
        {
            return;
        }

        string[] keys = EcoVehicleDepotKeysField?.GetValue(null) as string[] ?? new[] { "VehicleDepot1" };
        BuildingDefinition? depot = EcoFindBuildingMethod?.Invoke(null, new object?[] { Encyclopedia.i, keys }) as BuildingDefinition;
        BuildingDefinition? bunker = ResolveCategoryDefinitionMethod?.Invoke(null, new object?[] { BuildingType.AMMO, true }) as BuildingDefinition;
        EcoPlaceBaseDepotsMethod.Invoke(ecoInstance, new object?[] { hq, airbase, depot, bunker, depotsWanted });
    }

    internal static void RemoveGuestFaction(object? repairInstance, FactionHQ? hq)
    {
        if (repairInstance == null || hq == null)
        {
            return;
        }

        if (RepairGuestFactionsField?.GetValue(repairInstance) is HashSet<FactionHQ> set)
        {
            set.Remove(hq);
        }
    }

    private static MethodInfo? FindMethodByNameAndParamCount(Type? type, string name, int paramCount)
    {
        if (type == null)
        {
            return null;
        }

        MethodInfo[] methods = type.GetMethods(AnyDeclared);
        for (int i = 0; i < methods.Length; i++)
        {
            if (string.Equals(methods[i].Name, name, StringComparison.Ordinal)
                && methods[i].GetParameters().Length == paramCount)
            {
                return methods[i];
            }
        }

        return null;
    }
}
