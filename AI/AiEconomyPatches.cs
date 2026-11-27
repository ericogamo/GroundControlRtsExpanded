using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GroundControlRtsExpanded;

[HarmonyPatch]
internal static class IsBelowEnemyNetIncomeTargetPatch
{
    private static bool Prepare() => GroundControlReflection.IsBelowEnemyNetIncomeTargetMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.IsBelowEnemyNetIncomeTargetMethod!;

    private static bool Prefix(FactionHQ hq, ref bool __result)
    {
        AiEconomyProfile profile = AiEconomySettings.GetActiveProfile();
        if (profile.IsVanilla || hq == null)
        {
            return true;
        }

        object? eco = GroundControlReflection.GetEcoInstance();
        __result = AiEconomySettings.IsBelowEffectiveIncomeTarget(eco, hq, profile);
        return false;
    }
}

[HarmonyPatch]
internal static class GetAiStipendPerMinutePatch
{
    private static bool Prepare() => GroundControlReflection.GetAiStipendPerMinuteMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.GetAiStipendPerMinuteMethod!;

    private static bool Prefix(FactionHQ hq, FactionHQ? localHq, ref float __result)
    {
        AiEconomyProfile profile = AiEconomySettings.GetActiveProfile();
        if (profile.IsVanilla)
        {
            return true;
        }

        if (!GroundControlReflection.IsSafeAiFaction(hq, localHq))
        {
            __result = 0f;
            return false;
        }

        int otherPlayerCount = 0;
        if (profile.ChampionsPerPlayerStipend > 0f)
        {
            foreach (FactionHQ other in FactionRegistry.GetAllHQs())
            {
                if (other != null && other != hq)
                {
                    otherPlayerCount += other.GetPlayers(false).Count;
                }
            }
        }

        __result = Mathf.Max(0f, profile.ChampionsFlatStipend + profile.ChampionsPerPlayerStipend * otherPlayerCount);
        return false;
    }
}

[HarmonyPatch]
internal static class HasFreeMinePatch
{
    private static bool Prepare() => GroundControlReflection.HasFreeMineMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.HasFreeMineMethod!;

    private static bool Prefix(object __instance, FactionHQ hq, ref bool __result)
    {
        AiEconomyProfile profile = AiEconomySettings.GetActiveProfile();
        if (profile.IsVanilla)
        {
            return true;
        }

        if (profile.FreeGoldMines <= 0 || hq == null)
        {
            __result = false;
            return false;
        }

        __result = GroundControlReflection.IsChampionsMission()
            && GroundControlReflection.HoldsMostBases(hq)
            && GroundControlReflection.CountFreeMines(__instance, hq) < profile.FreeGoldMines;
        return false;
    }
}

[HarmonyPatch]
internal static class GetEnemyMineTargetPatch
{
    private static bool Prepare() => GroundControlReflection.GetEnemyMineTargetMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.GetEnemyMineTargetMethod!;

    private static void Postfix(FactionHQ hq, ref int __result)
    {
        AiEconomyProfile profile = AiEconomySettings.GetActiveProfile();
        if (!profile.IsVanilla && hq != null)
        {
            object? eco = GroundControlReflection.GetEcoInstance();
            int effectiveMax = AiEconomySettings.GetEffectiveMaxGoldMines(eco, hq, profile);
            __result = Mathf.Min(__result, effectiveMax);
        }
    }
}

[HarmonyPatch]
internal static class TryBuildEnemyMinePatch
{
    private static bool Prepare() => GroundControlReflection.TryBuildEnemyMineMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.TryBuildEnemyMineMethod!;

    private static bool Prefix(object __instance, FactionHQ hq, ref bool __result)
    {
        AiEconomyProfile profile = AiEconomySettings.GetActiveProfile();
        if (profile.IsVanilla || hq == null)
        {
            return true;
        }

        if (AiEconomySettings.IsMineBuildBlocked(__instance, hq, profile))
        {
            __result = false;
            return false;
        }

        return true;
    }

    private static void Postfix(object __instance, FactionHQ? hq, bool __result)
    {
        if (!__result
            || hq == null
            || !((Mirage.NetworkBehaviour)hq).IsServer
            || !AddonConfig.EnableTacticalQualityAi.Value
            || AiEconomySettings.GetActiveProfile().IsVanilla)
        {
            return;
        }

        if (GroundControlReflection.EcoConstructionSitesField?.GetValue(__instance) is not IList sites || sites.Count == 0)
        {
            return;
        }

        object? newestSite = sites[sites.Count - 1];
        if (newestSite == null
            || !ReferenceEquals(GroundControlReflection.SiteHqProp?.GetValue(newestSite) as FactionHQ, hq)
            || !GroundControlReflection.IsMineBuildKind(GroundControlReflection.SiteKindProp?.GetValue(newestSite))
            || GroundControlReflection.SitePositionProp?.GetValue(newestSite) is not GlobalPosition minePos)
        {
            return;
        }

        if (HasFriendlyAirDefenceNear(hq, sites, minePos, 950f))
        {
            return;
        }

        BuildingDefinition? defBuilding = GroundControlReflection.ResolveCategoryDefinitionMethod?.Invoke(null, new object?[] { BuildingType.DEF, true }) as BuildingDefinition;
        if (defBuilding == null)
        {
            return;
        }

        float defCost = GroundControlReflection.GetStructureCost(defBuilding);
        if (hq.factionFunds < defCost + 80f || !GroundControlReflection.HasAnyBlueprintRoom(hq))
        {
            return;
        }

        if (GroundControlReflection.TryFindSiteNear(__instance, hq, defBuilding, minePos, out GlobalPosition defSite, 12))
        {
            string label = GroundControlReflection.GetStructureLabel(defBuilding);
            GroundControlReflection.QueueEnemyStructureBlueprint(__instance, hq, defBuilding, defSite, defCost, label);
        }
    }

    private static bool HasFriendlyAirDefenceNear(FactionHQ hq, IList sites, GlobalPosition center, float radiusMeters)
    {
        if (hq.factionUnits != null)
        {
            foreach (PersistentID pid in hq.factionUnits)
            {
                if (!pid.TryGetUnit(out Unit unit) || unit == null || unit.disabled)
                {
                    continue;
                }

                if (unit is Building && unit.definition is BuildingDefinition bd && bd.buildingType == BuildingType.DEF
                    && FastMath.InRange(unit.GlobalPosition(), center, radiusMeters))
                {
                    return true;
                }
            }
        }

        for (int i = 0; i < sites.Count; i++)
        {
            object? cs = sites[i];
            if (cs == null
                || !ReferenceEquals(GroundControlReflection.SiteHqProp?.GetValue(cs) as FactionHQ, hq)
                || GroundControlReflection.SiteDefinitionProp?.GetValue(cs) is not BuildingDefinition bdef
                || bdef.buildingType != BuildingType.DEF
                || GroundControlReflection.SitePositionProp?.GetValue(cs) is not GlobalPosition sPos)
            {
                continue;
            }

            if (FastMath.InRange(sPos, center, radiusMeters))
            {
                return true;
            }
        }

        return false;
    }
}

[HarmonyPatch]
internal static class TryUpgradeEnemyIncomePatch
{
    private static bool Prepare() => GroundControlReflection.TryUpgradeEnemyIncomeMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.TryUpgradeEnemyIncomeMethod!;

    private static bool Prefix(object __instance, FactionHQ hq, ref bool __result)
    {
        AiEconomyProfile profile = AiEconomySettings.GetActiveProfile();
        if (profile.IsVanilla || hq == null)
        {
            return true;
        }

        if (AiEconomySettings.IsIncomeUpgradeBlocked(__instance, hq, profile))
        {
            __result = false;
            return false;
        }

        return true;
    }
}

internal static class EnemyTier3DepotHelper
{
    private const float DepotBaseRangeMeters = 3000f;

    internal static bool HasBaseNeedingTier3Depot(FactionHQ hq, out float upgradeCost)
    {
        upgradeCost = GroundControlReflection.GetDepotUpgradeCost(2);
        if (upgradeCost <= 0f)
        {
            return false;
        }

        int maxLv3Depots = Mathf.Clamp(GroundControlReflection.CountLandBases(hq), 1, 4);
        List<VehicleDepot> allDepots = SceneComponentCache.GetVehicleDepots();
        if (allDepots.Count == 0)
        {
            return false;
        }

        List<GlobalPosition> lv3Positions = new();
        List<GlobalPosition> lv2Positions = new();

        for (int i = 0; i < allDepots.Count; i++)
        {
            VehicleDepot depot = allDepots[i];
            if (depot == null || depot.disabled || !ReferenceEquals(depot.NetworkHQ, hq))
            {
                continue;
            }

            int lvl = GroundControlReflection.GetDepotLevel(depot);
            if (lvl >= 3)
            {
                lv3Positions.Add(depot.GlobalPosition());
            }
            else if (lvl == 2)
            {
                lv2Positions.Add(depot.GlobalPosition());
            }
        }

        if (lv3Positions.Count >= maxLv3Depots || lv2Positions.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < lv2Positions.Count; i++)
        {
            GlobalPosition candidatePos = lv2Positions[i];
            bool coveredByLv3 = false;
            for (int j = 0; j < lv3Positions.Count; j++)
            {
                if (FastMath.InRange(candidatePos, lv3Positions[j], DepotBaseRangeMeters))
                {
                    coveredByLv3 = true;
                    break;
                }
            }

            if (!coveredByLv3)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsReadyForTier3DepotPlanning(object? ecoInstance, FactionHQ? hq, out float upgradeCost)
    {
        upgradeCost = 0f;
        if (ecoInstance == null || hq == null || !AddonConfig.EnableTacticalQualityAi.Value)
        {
            return false;
        }

        AiEconomyProfile profile = AiEconomySettings.GetActiveProfile();
        if (profile.IsVanilla)
        {
            return false;
        }

        if (DynamicCapAndTechService.IsActive && DynamicCapAndTechService.Instance.GetSnapshot(hq).TechTier < 2)
        {
            return false;
        }

        if ((GroundControlReflection.CountGoldMines(ecoInstance, hq) < 1 && GroundControlReflection.GetHqIncomePerMinute(ecoInstance, hq) < 450f)
            || GroundControlReflection.GetEnemyNetIncomePerMinute(hq) < 350f)
        {
            return false;
        }

        return HasBaseNeedingTier3Depot(hq, out upgradeCost);
    }
}

[HarmonyPatch]
internal static class HasDepotUpgradeIncomePatch
{
    private static bool Prepare() => GroundControlReflection.EcoHasDepotUpgradeIncomeMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoHasDepotUpgradeIncomeMethod!;

    private static bool Prefix(object __instance, FactionHQ? hq, ref bool __result)
    {
        AiEconomyProfile profile = AiEconomySettings.GetActiveProfile();
        if (profile.IsVanilla || hq == null)
        {
            return true;
        }

        __result = GroundControlReflection.GetHqIncomePerMinute(__instance, hq) >= 150f;
        return false;
    }
}

[HarmonyPatch]
internal static class TryBuildEnemyDepotThrottlePatch
{
    private static bool Prepare() => GroundControlReflection.EcoTryBuildEnemyDepotMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoTryBuildEnemyDepotMethod!;

    private static bool Prefix(object __instance, FactionHQ? hq, ref int perBase, ref bool __result)
    {
        AiEconomyProfile profile = AiEconomySettings.GetActiveProfile();
        if (profile.IsVanilla || hq == null)
        {
            return true;
        }

        if (AiStrategicDirectorService.Instance.ShouldSuppressOpeningDepotSpam(__instance, hq))
        {
            perBase = 1;
            if (GroundControlReflection.CountBuildings(hq, BuildingType.DEP) >= 2)
            {
                __result = false;
                return false;
            }
        }

        return true;
    }
}

internal static class EnemyTier3NavalDockHelper
{
    internal static bool IsReadyForTier3NavalDock(object? ecoInstance, FactionHQ? hq, out float upgradeCost)
    {
        upgradeCost = 0f;
        if (ecoInstance == null || hq == null || !AddonConfig.EnableTacticalQualityAi.Value)
        {
            return false;
        }

        AiEconomyProfile profile = AiEconomySettings.GetActiveProfile();
        if (profile.IsVanilla)
        {
            return false;
        }

        if (DynamicCapAndTechService.IsActive && DynamicCapAndTechService.Instance.GetSnapshot(hq).TechTier < 3)
        {
            return false;
        }

        if (GroundControlReflection.GetNavalDockLevel(hq) != 2)
        {
            return false;
        }

        if (GroundControlReflection.GetEnemyNetIncomePerMinute(hq) < 450f)
        {
            return false;
        }

        upgradeCost = GroundControlReflection.GetNavalDockUpgradeCost(2);
        return upgradeCost > 0f;
    }
}

[HarmonyPatch]
internal static class GetEnemyBuildReservePatch
{
    private static bool Prepare() => GroundControlReflection.GetEnemyBuildReserveMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.GetEnemyBuildReserveMethod!;

    private static void Postfix(FactionHQ hq, ref bool goldMine, ref bool income, ref float __result)
    {
        if (hq == null)
        {
            return;
        }

        AiEconomyProfile profile = AiEconomySettings.GetActiveProfile();
        if (profile.IsVanilla)
        {
            return;
        }

        object? eco = GroundControlReflection.GetEcoInstance();
        if (eco == null)
        {
            return;
        }

        if (income)
        {
            bool blocked = goldMine
                ? AiEconomySettings.IsMineBuildBlocked(eco, hq, profile)
                : (GroundControlReflection.FindEnemyIncomeUpgrade(eco, hq, out _) != null
                    ? AiEconomySettings.IsIncomeUpgradeBlocked(eco, hq, profile)
                    : AiEconomySettings.IsMineBuildBlocked(eco, hq, profile));

            if (blocked)
            {
                // Release reserve immediately so blocked income builds cannot stall war or factory funds.
                __result = 0f;
                goldMine = false;
                income = false;
            }
        }

        if (AiStrategicDirectorService.Instance.ShouldSuppressOpeningDepotSpam(eco, hq)
            && !income
            && GroundControlReflection.CountBuildings(hq, BuildingType.DEP) >= 2)
        {
            if (GroundControlReflection.GetHqIncomePerMinute(eco, hq) >= 150f
                && GroundControlReflection.HasBaseWithoutUpgradedDepot(eco, hq))
            {
                __result = Mathf.Max(__result, GroundControlReflection.GetDepotUpgradeCost(1));
            }
            else if (GroundControlReflection.FindEnemyIncomeUpgrade(eco, hq, out float upgradeCost) != null
                && upgradeCost > 0f
                && !AiEconomySettings.IsIncomeUpgradeBlocked(eco, hq, profile))
            {
                __result = upgradeCost;
                goldMine = false;
                income = true;
            }
            else if (!AiEconomySettings.IsMineBuildBlocked(eco, hq, profile))
            {
                object? nextKind = GroundControlReflection.ChooseEnemyIncomeKind(eco, hq);
                bool isMine = GroundControlReflection.IsMineBuildKind(nextKind);
                float nextCost = GroundControlReflection.GetEnemyIncomeReserve(eco, hq, nextKind);
                __result = Mathf.Max(isMine ? 1f : 50f, nextCost);
                goldMine = isMine;
                income = true;
            }
        }

        if (DynamicCapAndTechService.IsActive && AddonConfig.EnableAiCityBuilding.Value && !income)
        {
            FactionCapSnapshot snap = DynamicCapAndTechService.Instance.GetSnapshot(hq);
            float gross = GroundControlReflection.GetHqIncomePerMinute(eco, hq);
            if (snap.CommandHqCount == 0 && gross >= 100f)
            {
                __result = Mathf.Max(__result, 180f);
            }
            else if (snap.CommandHqLevel == 1 && GroundControlReflection.GetBestDepotLevel(hq) >= 2 && GroundControlReflection.CountGoldMines(eco, hq) >= 1)
            {
                __result = Mathf.Max(__result, 320f);
            }
        }

        if (__result <= 0f
            && EnemyTier3DepotHelper.IsReadyForTier3DepotPlanning(eco, hq, out float l3Cost)
            && hq.factionFunds >= l3Cost * 0.45f)
        {
            __result = l3Cost;
        }
        else if (__result <= 0f
            && EnemyTier3NavalDockHelper.IsReadyForTier3NavalDock(eco, hq, out float l3DockCost)
            && hq.factionFunds >= l3DockCost * 0.50f)
        {
            __result = l3DockCost;
        }
    }
}

[HarmonyPatch]
internal static class ReviewEnemyTier3DepotPatch
{
    private static bool Prepare() => GroundControlReflection.ReviewEnemyMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.ReviewEnemyMethod!;

    private static void Postfix(object __instance, FactionHQ hq)
    {
        if (__instance == null || hq == null || !((Mirage.NetworkBehaviour)hq).IsServer)
        {
            return;
        }

        if (EnemyTier3DepotHelper.IsReadyForTier3DepotPlanning(__instance, hq, out float l3Cost)
            && GroundControlReflection.HasDepotUpgradeIncome(__instance, hq)
            && hq.factionFunds >= l3Cost)
        {
            GroundControlReflection.TryUpgradeEnemyDepot(__instance, hq);
        }

        if (EnemyTier3NavalDockHelper.IsReadyForTier3NavalDock(__instance, hq, out float l3DockCost)
            && hq.factionFunds >= l3DockCost * 1.25f)
        {
            GroundControlReflection.TryUpgradeEnemyNavalDock(__instance, hq);
        }

        AiStrategicDirectorService.Instance.LogTelemetryIfDue(__instance, hq);
    }
}

[HarmonyPatch]
internal static class GetEnemyFactoryTargetPatch
{
    private static bool Prepare() => GroundControlReflection.GetEnemyFactoryTargetMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.GetEnemyFactoryTargetMethod!;

    private static void Postfix(FactionHQ hq, ref int __result)
    {
        AiEconomyProfile profile = AiEconomySettings.GetActiveProfile();
        if (profile.IsVanilla || hq == null)
        {
            return;
        }

        object? eco = GroundControlReflection.GetEcoInstance();
        int effectiveMax = AiEconomySettings.GetEffectiveMaxFactories(eco, hq, profile);

        bool hasEconomyBase = eco != null
            && (GroundControlReflection.CountGoldMines(eco, hq) >= 1
                || GroundControlReflection.GetHqIncomePerMinute(eco, hq) >= 300f);
        if (hasEconomyBase)
        {
            int desiredTarget = 4 + Mathf.Min(2, GroundControlReflection.CountLandBases(hq));
            if (GroundControlReflection.GetNavalDockLevel(hq) > 0)
            {
                desiredTarget = Mathf.Max(desiredTarget, 6);
            }

            __result = Mathf.Max(__result, desiredTarget);
        }

        __result = Mathf.Min(__result, effectiveMax);
    }
}

[HarmonyPatch]
internal static class TryBuildEnemyFactoryPatch
{
    private static bool Prepare() => GroundControlReflection.TryBuildEnemyFactoryMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.TryBuildEnemyFactoryMethod!;

    private static bool Prefix(FactionHQ hq, ref bool __result)
    {
        AiEconomyProfile profile = AiEconomySettings.GetActiveProfile();
        if (!profile.IsVanilla && hq != null)
        {
            object? eco = GroundControlReflection.GetEcoInstance();
            int factories = GroundControlReflection.CountFactories(hq);
            int effectiveMax = AiEconomySettings.GetEffectiveMaxFactories(eco, hq, profile);
            if (factories >= effectiveMax)
            {
                __result = false;
                return false;
            }

            if (eco != null)
            {
                int goldMines = GroundControlReflection.CountGoldMines(eco, hq);
                float gross = GroundControlReflection.GetHqIncomePerMinute(eco, hq);
                if (goldMines == 0 && gross >= 150f && !AiEconomySettings.IsMineBuildBlocked(eco, hq, profile))
                {
                    __result = false;
                    return false;
                }

                int target = GroundControlReflection.GetEnemyFactoryTarget(eco, hq);
                if (factories >= target && !(hq.factionFunds >= 1500f && goldMines >= 1 && GroundControlReflection.GetBestDepotLevel(hq) >= 3))
                {
                    __result = false;
                    return false;
                }
            }
        }

        return true;
    }
}

[HarmonyPatch]
internal static class GetEnemyFactoryReservePatch
{
    private static bool Prepare() => GroundControlReflection.GetEnemyFactoryReserveMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.GetEnemyFactoryReserveMethod!;

    private static bool Prefix(FactionHQ hq, ref float __result)
    {
        AiEconomyProfile profile = AiEconomySettings.GetActiveProfile();
        if (!profile.IsVanilla && hq != null)
        {
            object? eco = GroundControlReflection.GetEcoInstance();
            int factories = GroundControlReflection.CountFactories(hq);
            int effectiveMax = AiEconomySettings.GetEffectiveMaxFactories(eco, hq, profile);
            if (factories >= effectiveMax)
            {
                __result = 0f;
                return false;
            }

            if (eco != null)
            {
                int goldMines = GroundControlReflection.CountGoldMines(eco, hq);
                float gross = GroundControlReflection.GetHqIncomePerMinute(eco, hq);
                if (goldMines == 0 && gross >= 150f && !AiEconomySettings.IsMineBuildBlocked(eco, hq, profile))
                {
                    __result = 0f;
                    return false;
                }

                int target = GroundControlReflection.GetEnemyFactoryTarget(eco, hq);
                if (factories >= target && !(hq.factionFunds >= 1500f && goldMines >= 1 && GroundControlReflection.GetBestDepotLevel(hq) >= 3))
                {
                    __result = 0f;
                    return false;
                }
            }
        }

        return true;
    }
}

[HarmonyPatch]
internal static class EnemyReviewAirFundGuardPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyReviewMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyReviewMethod!;

    private static void Postfix(object __instance, FactionHQ? hq, FactionHQ? localHq)
    {
        AiEconomyProfile profile = AiEconomySettings.GetActiveProfile();
        if (profile.IsVanilla
            || hq == null
            || !((Mirage.NetworkBehaviour)hq).IsServer
            || !GroundControlReflection.IsSafeAiFaction(hq, localHq))
        {
            return;
        }

        object? eco = GroundControlReflection.GetEcoInstance();
        float gross = GroundControlReflection.GetHqIncomePerMinute(eco, hq);
        if (gross < 250f || hq.factionFunds < 350f)
        {
            return;
        }

        object? cmdState = GroundControlReflection.TryGetCommanderState(__instance, hq);
        if (cmdState == null)
        {
            return;
        }

        float airFund = GroundControlReflection.GetStateFloat(cmdState, GroundControlReflection.StateAirFundProp);
        if (airFund < 220f)
        {
            // Keep the virtual AirFund allocation hydrated from healthy faction cash so savedBuildReserve cannot starve air launches.
            float floor = Mathf.Min(300f, hq.factionFunds * 0.35f);
            if (floor > airFund)
            {
                GroundControlReflection.SetStateFloat(cmdState, GroundControlReflection.StateAirFundProp, floor);
            }
        }
    }
}

[HarmonyPatch]
internal static class ChooseEnemyProductionPatch
{
    private static readonly List<AircraftDefinition> AirNonCricketScratch = new();
    private static readonly List<AircraftDefinition> AirAllScratch = new();
    private static readonly List<AircraftDefinition> AirUnlockedScratch = new();
    private static readonly List<AircraftDefinition> AirRunwayScratch = new();
    private static readonly List<VehicleDefinition> GroundAllScratch = new();
    private static readonly List<VehicleDefinition> GroundUnlockedScratch = new();
    private static readonly List<VehicleDefinition> GroundStage1Scratch = new();
    private static readonly List<VehicleDefinition> GroundCombatScratch = new();
    private static readonly List<VehicleDefinition> GroundHigherTierScratch = new();
    private static readonly HashSet<UnitDefinition> BuiltProductsScratch = new();

    private static PlayerThreatSnapshot activeThreatScratch;
    private static int activeFighterLines;
    private static int activeStrikeLines;
    private static int activeMbtLines;
    private static int activeIfvLines;
    private static int activeShoradLines;
    private static int activeArtLines;
    private static int activeBestDepot;

    private static bool Prepare() => GroundControlReflection.EcoChooseEnemyProductionMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoChooseEnemyProductionMethod!;

    private static bool Prefix(
        object __instance,
        FactionHQ? hq,
        List<UnitDefinition>? options,
        ref UnitDefinition? __result)
    {
        AiEconomyProfile profile = AiEconomySettings.GetActiveProfile();
        if (profile.IsVanilla
            || !AddonConfig.EnableTacticalQualityAi.Value
            || hq == null
            || options == null
            || options.Count == 0)
        {
            return true;
        }

        UnitDefinition? sample = null;
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] != null)
            {
                sample = options[i];
                break;
            }
        }

        if (sample == null)
        {
            return true;
        }

        CollectOwnedFactoryProducts(__instance, hq, BuiltProductsScratch);
        float enemyGross = GroundControlReflection.GetHqIncomePerMinute(__instance, hq);
        bool capsActive = DynamicCapAndTechService.IsActive;
        FactionCapSnapshot snap = capsActive ? DynamicCapAndTechService.Instance.GetSnapshot(hq) : default;

        if (sample is AircraftDefinition)
        {
            __result = ChooseAircraftProduction(__instance, hq, options, BuiltProductsScratch, enemyGross, capsActive, snap);
            return false;
        }

        if (sample is ShipDefinition)
        {
            __result = ChooseShipProduction(hq, options, BuiltProductsScratch, enemyGross, capsActive, snap);
            return false;
        }

        if (sample is VehicleDefinition)
        {
            __result = ChooseGroundProduction(__instance, hq, options, BuiltProductsScratch, enemyGross, capsActive, snap);
            return false;
        }

        return true;
    }

    private static void CollectOwnedFactoryProducts(object ecoInstance, FactionHQ hq, HashSet<UnitDefinition> target)
    {
        target.Clear();
        Factory[] factories = SceneComponentCache.GetFactories();
        for (int i = 0; i < factories.Length; i++)
        {
            Factory factory = factories[i];
            if (factory == null || factory.attachedUnit == null || factory.attachedUnit.disabled)
            {
                continue;
            }

            if (ReferenceEquals(factory.attachedUnit.NetworkHQ, hq) && factory.productionUnit != null)
            {
                target.Add(factory.productionUnit);
            }
        }

        if (GroundControlReflection.EcoConstructionSitesField?.GetValue(ecoInstance) is IList sites)
        {
            for (int i = 0; i < sites.Count; i++)
            {
                object? site = sites[i];
                if (site == null
                    || GroundControlReflection.SiteHqProp?.GetValue(site) is not FactionHQ siteHq
                    || !ReferenceEquals(siteHq, hq)
                    || !GroundControlReflection.IsSiteKindFactory(site))
                {
                    continue;
                }

                if (GroundControlReflection.SiteProductionProp?.GetValue(site) is UnitDefinition pendingProd)
                {
                    target.Add(pendingProd);
                }
            }
        }
    }

    private static UnitDefinition ChooseAircraftProduction(
        object ecoInstance,
        FactionHQ hq,
        List<UnitDefinition> options,
        HashSet<UnitDefinition>? builtProducts,
        float enemyGross,
        bool capsActive,
        in FactionCapSnapshot snap)
    {
        AirNonCricketScratch.Clear();
        AirAllScratch.Clear();
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] is not AircraftDefinition adef)
            {
                continue;
            }

            AirAllScratch.Add(adef);
            if (!GroundControlReflection.IsCricket(adef))
            {
                AirNonCricketScratch.Add(adef);
            }
        }

        List<AircraftDefinition> basePool = AirNonCricketScratch.Count > 0 ? AirNonCricketScratch : AirAllScratch;
        if (basePool.Count == 0)
        {
            return options[0];
        }

        AirUnlockedScratch.Clear();
        for (int i = 0; i < basePool.Count; i++)
        {
            AircraftDefinition adef = basePool[i];
            int reqTier = DynamicCapAndTechService.GetRequiredAircraftTechTier(adef);
            bool isUnlocked = !capsActive
                || (reqTier <= snap.TechTier
                    && (reqTier < 2 || snap.BestPilotAcademyLevel >= (reqTier >= 3 ? 2 : 1)));
            if (isUnlocked)
            {
                AirUnlockedScratch.Add(adef);
            }
        }

        if (AirUnlockedScratch.Count == 0 && AirNonCricketScratch.Count > 0)
        {
            for (int i = 0; i < AirAllScratch.Count; i++)
            {
                AircraftDefinition adef = AirAllScratch[i];
                int reqTier = DynamicCapAndTechService.GetRequiredAircraftTechTier(adef);
                bool isUnlocked = !capsActive
                    || (reqTier <= snap.TechTier
                        && (reqTier < 2 || snap.BestPilotAcademyLevel >= (reqTier >= 3 ? 2 : 1)));
                if (isUnlocked)
                {
                    AirUnlockedScratch.Add(adef);
                }
            }
        }

        List<AircraftDefinition> tierPool = AirUnlockedScratch.Count > 0 ? AirUnlockedScratch : basePool;
        AirRunwayScratch.Clear();
        for (int i = 0; i < tierPool.Count; i++)
        {
            if (CanOperateFromAnyOwnedBase(hq, tierPool[i]))
            {
                AirRunwayScratch.Add(tierPool[i]);
            }
        }

        List<AircraftDefinition> pool = AirRunwayScratch.Count > 0 ? AirRunwayScratch : tierPool;
        CountOwnedAirFactoryRoles(ecoInstance, hq, out activeFighterLines, out activeStrikeLines);
        activeThreatScratch = AiStrategicDirectorService.Instance.ReadPlayerThreat(hq);

        float maxAirCost = Mathf.Max(500f, Mathf.Max(enemyGross * 2.75f, hq.factionFunds * 1.15f));
        return SelectBestProductionCandidate(
            pool,
            builtProducts,
            maxAirCost,
            static def => ScoreAircraftCandidate(def),
            preferCheapestFallback: false) ?? options[0];
    }

    private static bool CanOperateFromAnyOwnedBase(FactionHQ hq, AircraftDefinition adef)
    {
        bool anyOwned = false;
        foreach (Airbase ab in hq.GetAirbases())
        {
            if (ab == null || ab.disabled)
            {
                continue;
            }

            anyOwned = true;
            if (GroundControlReflection.AirHasTakeoffRunway(ab, adef, countOverruns: false))
            {
                return true;
            }
        }

        if (anyOwned)
        {
            return false;
        }

        foreach (Airbase ab in FactionRegistry.airbaseLookup.Values)
        {
            if (ab != null
                && !ab.disabled
                && GroundControlReflection.IsLandBase(ab)
                && GroundControlReflection.AirHasTakeoffRunway(ab, adef, countOverruns: false))
            {
                return true;
            }
        }

        return true;
    }

    private static void CountOwnedAirFactoryRoles(
        object ecoInstance,
        FactionHQ hq,
        out int fighterLines,
        out int strikeLines)
    {
        fighterLines = 0;
        strikeLines = 0;

        Factory[] factories = SceneComponentCache.GetFactories();
        for (int i = 0; i < factories.Length; i++)
        {
            Factory factory = factories[i];
            if (factory == null || factory.attachedUnit == null || factory.attachedUnit.disabled)
            {
                continue;
            }

            if (!ReferenceEquals(factory.attachedUnit.NetworkHQ, hq))
            {
                continue;
            }

            if (factory.productionUnit is AircraftDefinition adef)
            {
                ClassifyAircraftLine(adef, ref fighterLines, ref strikeLines);
            }
        }

        if (GroundControlReflection.EcoConstructionSitesField?.GetValue(ecoInstance) is IList sites)
        {
            for (int i = 0; i < sites.Count; i++)
            {
                object? site = sites[i];
                if (site == null
                    || GroundControlReflection.SiteHqProp?.GetValue(site) is not FactionHQ siteHq
                    || !ReferenceEquals(siteHq, hq)
                    || !GroundControlReflection.IsSiteKindFactory(site))
                {
                    continue;
                }

                if (GroundControlReflection.SiteProductionProp?.GetValue(site) is AircraftDefinition pendingAir)
                {
                    ClassifyAircraftLine(pendingAir, ref fighterLines, ref strikeLines);
                }
            }
        }
    }

    private static void ClassifyAircraftLine(AircraftDefinition adef, ref int fighterLines, ref int strikeLines)
    {
        if (IsFighterAircraft(adef))
        {
            fighterLines++;
        }
        else if (IsStrikeAircraft(adef))
        {
            strikeLines++;
        }
    }

    private static bool IsFighterAircraft(AircraftDefinition adef)
    {
        return !GroundControlReflection.IsRotaryAirframe(adef)
            && adef.roleIdentity.antiAir >= 0.55f
            && adef.roleIdentity.antiAir >= adef.roleIdentity.antiSurface;
    }

    private static bool IsStrikeAircraft(AircraftDefinition adef)
    {
        return adef.roleIdentity.antiSurface >= 0.45f || adef.roleIdentity.antiRadar >= 0.40f;
    }

    private static float ScoreAircraftCandidate(AircraftDefinition def)
    {
        int tier = DynamicCapAndTechService.GetRequiredAircraftTechTier(def);
        float score = tier * 35f + Mathf.Min(def.value, 160f) * 0.25f;

        if (!AddonConfig.EnableAiCombinedArmsSync.Value)
        {
            return score;
        }

        bool isFighter = IsFighterAircraft(def);
        bool isStrike = IsStrikeAircraft(def);

        if (activeThreatScratch.Aircraft >= 2 && activeFighterLines == 0 && isFighter)
        {
            score += 200f;
        }
        else if (activeFighterLines == 0 && activeStrikeLines > 0 && isFighter)
        {
            score += 135f;
        }
        else if (activeStrikeLines == 0 && activeFighterLines > 0 && isStrike)
        {
            score += 135f;
        }

        if (activeThreatScratch.SamRadars >= 2 && def.roleIdentity.antiRadar >= 0.40f)
        {
            score += 65f;
        }

        return score;
    }

    private static UnitDefinition ChooseShipProduction(
        FactionHQ hq,
        List<UnitDefinition> options,
        HashSet<UnitDefinition>? builtProducts,
        float enemyGross,
        bool capsActive,
        in FactionCapSnapshot snap)
    {
        List<ShipDefinition> allShips = new();
        List<ShipDefinition> unlocked = new();
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] is not ShipDefinition sdef)
            {
                continue;
            }

            allShips.Add(sdef);
            int reqTier = DynamicCapAndTechService.GetRequiredShipTechTier(sdef);
            bool isUnlocked = !capsActive
                || (reqTier <= snap.TechTier
                    && (reqTier < 2 || snap.BestNavalCommandLevel >= (reqTier >= 3 ? 2 : 1)));
            if (isUnlocked)
            {
                unlocked.Add(sdef);
            }
        }

        if (allShips.Count == 0)
        {
            return options[0];
        }

        List<ShipDefinition> pool = unlocked.Count > 0 ? unlocked : allShips;
        float maxShipCost = Mathf.Max(1500f, Mathf.Max(enemyGross * 4.5f, hq.factionFunds * 1.35f));
        return SelectBestProductionCandidate(
            pool,
            builtProducts,
            maxShipCost,
            static def => DynamicCapAndTechService.GetRequiredShipTechTier(def),
            preferCheapestFallback: true) ?? options[0];
    }

    private static UnitDefinition ChooseGroundProduction(
        object ecoInstance,
        FactionHQ hq,
        List<UnitDefinition> options,
        HashSet<UnitDefinition>? builtProducts,
        float enemyGross,
        bool capsActive,
        in FactionCapSnapshot snap)
    {
        int bestDepot = GroundControlReflection.GetBestDepotLevel(hq);
        activeBestDepot = bestDepot;
        GroundAllScratch.Clear();
        GroundUnlockedScratch.Clear();
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] is not VehicleDefinition vdef)
            {
                continue;
            }

            GroundAllScratch.Add(vdef);
            int reqDepot = GroundControlReflection.GetRequiredDepotLevel(vdef);
            if (reqDepot <= bestDepot && (!capsActive || reqDepot < 3 || snap.TechTier >= 2))
            {
                GroundUnlockedScratch.Add(vdef);
            }
        }

        if (GroundAllScratch.Count == 0)
        {
            return options[0];
        }

        GroundStage1Scratch.Clear();
        List<VehicleDefinition> srcStage1 = GroundUnlockedScratch.Count > 0 ? GroundUnlockedScratch : GroundAllScratch;
        for (int i = 0; i < srcStage1.Count; i++)
        {
            GroundStage1Scratch.Add(srcStage1[i]);
        }

        GroundCombatScratch.Clear();
        for (int i = 0; i < GroundStage1Scratch.Count; i++)
        {
            VehicleDefinition vdef = GroundStage1Scratch[i];
            if (!GroundControlReflection.IsBuilderVehicle(vdef) && !GroundControlReflection.IsSupplyVehicle(vdef))
            {
                GroundCombatScratch.Add(vdef);
            }
        }

        CountOwnedGroundFactoryRoles(
            ecoInstance,
            hq,
            out activeMbtLines,
            out activeIfvLines,
            out activeShoradLines,
            out activeArtLines);
        activeThreatScratch = AiStrategicDirectorService.Instance.ReadPlayerThreat(hq);

        List<VehicleDefinition> stage2Pool = GroundCombatScratch.Count > 0 ? GroundCombatScratch : GroundStage1Scratch;
        List<VehicleDefinition> finalPool = stage2Pool;
        if (bestDepot >= 2)
        {
            float maxCombatValue = 0f;
            for (int i = 0; i < stage2Pool.Count; i++)
            {
                if (stage2Pool[i].value > maxCombatValue)
                {
                    maxCombatValue = stage2Pool[i].value;
                }
            }

            float minKeepValue = maxCombatValue * 0.35f;
            GroundHigherTierScratch.Clear();
            for (int i = 0; i < stage2Pool.Count; i++)
            {
                VehicleDefinition candidate = stage2Pool[i];
                bool isSpecialRole = IsMobileAaVehicle(candidate)
                    || IsCaptureIfvVehicle(candidate)
                    || candidate.vehicleType == VehicleType.ART;
                if (candidate.value >= minKeepValue || isSpecialRole)
                {
                    GroundHigherTierScratch.Add(candidate);
                }
            }

            if (GroundHigherTierScratch.Count > 0)
            {
                finalPool = GroundHigherTierScratch;
            }
        }

        float maxGroundCost = Mathf.Max(450f, Mathf.Max(enemyGross * 2.5f, hq.factionFunds * 1.15f));
        return SelectBestProductionCandidate(
            finalPool,
            builtProducts,
            maxGroundCost,
            static def => ScoreGroundCandidate(def),
            preferCheapestFallback: false) ?? options[0];
    }

    private static void CountOwnedGroundFactoryRoles(
        object ecoInstance,
        FactionHQ hq,
        out int mbtLines,
        out int ifvLines,
        out int shoradLines,
        out int artLines)
    {
        mbtLines = 0;
        ifvLines = 0;
        shoradLines = 0;
        artLines = 0;

        Factory[] factories = SceneComponentCache.GetFactories();
        for (int i = 0; i < factories.Length; i++)
        {
            Factory factory = factories[i];
            if (factory == null || factory.attachedUnit == null || factory.attachedUnit.disabled)
            {
                continue;
            }

            if (!ReferenceEquals(factory.attachedUnit.NetworkHQ, hq))
            {
                continue;
            }

            if (factory.productionUnit is VehicleDefinition vdef)
            {
                ClassifyGroundLine(vdef, ref mbtLines, ref ifvLines, ref shoradLines, ref artLines);
            }
        }

        if (GroundControlReflection.EcoConstructionSitesField?.GetValue(ecoInstance) is IList sites)
        {
            for (int i = 0; i < sites.Count; i++)
            {
                object? site = sites[i];
                if (site == null
                    || GroundControlReflection.SiteHqProp?.GetValue(site) is not FactionHQ siteHq
                    || !ReferenceEquals(siteHq, hq)
                    || !GroundControlReflection.IsSiteKindFactory(site))
                {
                    continue;
                }

                if (GroundControlReflection.SiteProductionProp?.GetValue(site) is VehicleDefinition pendingVeh)
                {
                    ClassifyGroundLine(pendingVeh, ref mbtLines, ref ifvLines, ref shoradLines, ref artLines);
                }
            }
        }
    }

    private static void ClassifyGroundLine(
        VehicleDefinition vdef,
        ref int mbtLines,
        ref int ifvLines,
        ref int shoradLines,
        ref int artLines)
    {
        if (vdef.vehicleType == VehicleType.MBT)
        {
            mbtLines++;
        }
        else if (IsCaptureIfvVehicle(vdef))
        {
            ifvLines++;
        }
        else if (IsMobileAaVehicle(vdef))
        {
            shoradLines++;
        }
        else if (vdef.vehicleType == VehicleType.ART)
        {
            artLines++;
        }
    }

    private static bool IsCaptureIfvVehicle(VehicleDefinition vdef)
    {
        return vdef.vehicleType == VehicleType.AFV || vdef.vehicleType == VehicleType.LCV;
    }

    private static bool IsMobileAaVehicle(VehicleDefinition vdef)
    {
        return vdef.vehicleType == VehicleType.AAA
            || vdef.vehicleType == VehicleType.IR_SAM
            || vdef.vehicleType == VehicleType.R_SAM
            || vdef.vehicleType == VehicleType.RDR
            || vdef.roleIdentity.antiAir >= 0.45f;
    }

    private static float ScoreGroundCandidate(VehicleDefinition def)
    {
        float score = def.value;
        if (!AddonConfig.EnableAiCombinedArmsSync.Value)
        {
            return score;
        }

        bool isMbt = def.vehicleType == VehicleType.MBT;
        bool isIfv = IsCaptureIfvVehicle(def);
        bool isAa = IsMobileAaVehicle(def);
        bool isArt = def.vehicleType == VehicleType.ART;

        if (activeThreatScratch.Aircraft >= 2 && activeShoradLines == 0 && isAa)
        {
            score += def.vehicleType == VehicleType.RDR ? 110f : 200f;
        }
        else if (activeMbtLines == 0 && isMbt)
        {
            score += 150f;
        }
        else if (activeIfvLines == 0 && (activeMbtLines > 0 || activeShoradLines > 0) && isIfv)
        {
            score += 135f;
        }
        else if (activeBestDepot >= 2 && activeMbtLines >= 1 && activeIfvLines >= 1 && activeArtLines == 0 && isArt)
        {
            score += 120f;
        }
        else if (activeShoradLines == 0 && isAa && def.vehicleType != VehicleType.RDR)
        {
            score += 75f;
        }

        return score;
    }

    private static T? SelectBestProductionCandidate<T>(
        List<T> pool,
        HashSet<UnitDefinition>? builtProducts,
        float maxCost,
        System.Func<T, float> getPriorityScore,
        bool preferCheapestFallback) where T : UnitDefinition
    {
        T? bestUnbuiltAffordable = null;
        float bestUnbuiltScore = -1f;
        float bestUnbuiltCost = -1f;

        T? bestBuiltAffordable = null;
        float bestBuiltScore = -1f;
        float bestBuiltCost = -1f;

        T? cheapestUnbuilt = null;
        float cheapestUnbuiltCost = float.MaxValue;

        T? fallback = null;
        float fallbackScore = preferCheapestFallback ? float.MaxValue : -1f;
        float fallbackCost = -1f;

        for (int i = 0; i < pool.Count; i++)
        {
            T candidate = pool[i];
            float score = getPriorityScore(candidate);
            float cost = GroundControlReflection.GetFactoryBuildCost(candidate);
            bool isBuilt = builtProducts != null && builtProducts.Contains(candidate);

            if (preferCheapestFallback)
            {
                if (fallback == null || cost < fallbackScore)
                {
                    fallback = candidate;
                    fallbackScore = cost;
                }
            }
            else if (fallback == null
                || score > fallbackScore
                || (Mathf.Approximately(score, fallbackScore) && cost > fallbackCost))
            {
                fallback = candidate;
                fallbackScore = score;
                fallbackCost = cost;
            }

            if (!isBuilt && cost < cheapestUnbuiltCost)
            {
                cheapestUnbuilt = candidate;
                cheapestUnbuiltCost = cost;
            }

            if (cost > maxCost)
            {
                continue;
            }

            if (!isBuilt)
            {
                if (score > bestUnbuiltScore || (Mathf.Approximately(score, bestUnbuiltScore) && cost > bestUnbuiltCost))
                {
                    bestUnbuiltAffordable = candidate;
                    bestUnbuiltScore = score;
                    bestUnbuiltCost = cost;
                }
            }
            else if (score > bestBuiltScore || (Mathf.Approximately(score, bestBuiltScore) && cost > bestBuiltCost))
            {
                bestBuiltAffordable = candidate;
                bestBuiltScore = score;
                bestBuiltCost = cost;
            }
        }

        return bestUnbuiltAffordable
            ?? bestBuiltAffordable
            ?? cheapestUnbuilt
            ?? fallback;
    }
}

[HarmonyPatch]
internal static class EnemyMineIncomeMultiplierPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyMineIncomeMultiplierGetter != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyMineIncomeMultiplierGetter!;

    private static void Postfix(ref float __result)
    {
        AiEconomyProfile profile = AiEconomySettings.GetActiveProfile();
        if (!profile.IsVanilla)
        {
            __result = profile.MineIncomeMultiplier;
        }
    }
}

[HarmonyPatch]
internal static class PayIncomeAdjustPatch
{
    private const float IncomeIntervalShareOfMinute = 0.25f;

    private static bool Prepare() => GroundControlReflection.PayIncomeMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.PayIncomeMethod!;

    private static void Postfix(object __instance)
    {
        AiEconomyProfile profile = AiEconomySettings.GetActiveProfile();
        if (profile.IsVanilla)
        {
            return;
        }

        // Difficulty == 1 already multiplies AI mines by CommanderSettings.EnemyMineIncomeMultiplier (patched above).
        if (GroundControlReflection.GetEnemyDifficulty() == 1 || Mathf.Abs(profile.MineIncomeMultiplier - 1f) <= 0.001f)
        {
            return;
        }

        IDictionary? incomeLevels = GroundControlReflection.GetIncomeLevels(__instance);
        if (incomeLevels == null || incomeLevels.Count == 0)
        {
            return;
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        float deltaScale = profile.MineIncomeMultiplier - 1f;

        foreach (DictionaryEntry entry in incomeLevels)
        {
            if (entry.Key is not Unit unit || unit == null || unit.disabled)
            {
                continue;
            }

            if (entry.Value is not int level || level <= 0)
            {
                continue;
            }

            FactionHQ? hq = unit.NetworkHQ;
            if (hq == null || !hq.IsServer || !GroundControlReflection.IsSafeAiFaction(hq, localHq))
            {
                continue;
            }

            object? kind = GroundControlReflection.GetIncomeKind(__instance, unit);
            if (kind == null)
            {
                continue;
            }

            float quarterIncome = GroundControlReflection.GetStageIncomePerMinute(kind, level) * IncomeIntervalShareOfMinute;
            float delta = quarterIncome * deltaScale;
            if (delta > 0f)
            {
                hq.AddFunds(delta);
            }
            else if (delta < 0f)
            {
                float clampDeduct = Mathf.Min(-delta, Mathf.Max(0f, hq.factionFunds));
                if (clampDeduct > 0f)
                {
                    hq.AddFunds(-clampDeduct);
                }
            }
        }
    }
}

[HarmonyPatch]
internal static class PrepareDuelHeadStartPatch
{
    private static bool Prepare() => GroundControlReflection.PrepareDuelMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.PrepareDuelMethod!;

    private static void Prefix(FactionHQ hq, out float __state)
    {
        __state = hq != null ? hq.factionFunds : 0f;
    }

    private static void Postfix(FactionHQ hq, float __state)
    {
        AiEconomyProfile profile = AiEconomySettings.GetActiveProfile();
        if (profile.IsVanilla || hq == null || !((Mirage.NetworkBehaviour)hq).IsServer)
        {
            return;
        }

        float normalized = Mathf.Max(0f, __state * profile.HeadStartMultiplier);
        hq.SetFunds(normalized);

        if (AddonConfig.VerboseLogging.Value && hq.faction != null)
        {
            AddonLog.Info($"[AI Economy] {hq.faction.name} headstart normalized ({profile.Name}): {normalized:0} credits (x{profile.HeadStartMultiplier:0.##}).");
        }
    }
}

[HarmonyPatch]
internal static class TryPickEnemyBuildSiteSafePatch
{
    private const float MinDispersalMeters = 180f;
    private static readonly List<Airbase> SafeBaseScratch = new();

    private static bool Prepare() => GroundControlReflection.EcoTryPickEnemyBuildSiteMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoTryPickEnemyBuildSiteMethod!;

    private static bool Prefix(FactionHQ? hq, ref GlobalPosition site, ref bool __result)
    {
        if (hq == null
            || !(hq is Mirage.NetworkBehaviour nb)
            || !nb.IsServer
            || !AddonConfig.ModEnabled.Value
            || !AddonConfig.EnableAiSmartBaseLayout.Value)
        {
            return true;
        }

        FactionHQ? enemyHq = GroundControlReflection.FindPrimaryHostileHq(hq);
        try
        {
            AiBattlefieldIntelService.Instance.GetAirbasesOrderedBySafety(hq, enemyHq, SafeBaseScratch);
            if (SafeBaseScratch.Count == 0)
            {
                return true;
            }

            float innerSpacingSq = MinDispersalMeters * MinDispersalMeters;
            float outerSpacingSq = 110f * 110f;
            object? eco = GroundControlReflection.GetEcoInstance();
            for (int ring = 0; ring < 2; ring++)
            {
                float minR = ring == 0 ? 220f : 450f;
                float maxR = ring == 0 ? 450f : 900f;
                float spacingSq = ring == 0 ? innerSpacingSq : outerSpacingSq;
                int attempts = ring == 0 ? 10 : 8;

                for (int b = 0; b < SafeBaseScratch.Count; b++)
                {
                    Airbase ab = SafeBaseScratch[b];
                    if (ab == null || ab.disabled || !GroundControlReflection.IsLandBase(ab))
                    {
                        continue;
                    }

                    Transform anchorTf = ab.center != null ? ab.center : ab.transform;
                    GlobalPosition anchor = anchorTf.GlobalPosition();
                    for (int attempt = 0; attempt < attempts; attempt++)
                    {
                        float angle = Random.Range(0f, Mathf.PI * 2f);
                        float radius = Random.Range(minR, maxR);
                        var probe = new GlobalPosition(
                            anchor.x + Mathf.Cos(angle) * radius,
                            anchor.y,
                            anchor.z + Mathf.Sin(angle) * radius);
                        GlobalPosition candidate = GroundControlReflection.SnapToTerrain(probe);
                        if (GroundControlReflection.IsBelowSeaLevel(candidate)
                            || GroundControlReflection.BuildPreviewIsOnLevelRoad(candidate, 24f)
                            || !GroundControlReflection.CanDriveBetween(anchor, candidate))
                        {
                            continue;
                        }

                        if (!HasMinOwnedBuildingSpacing(eco, hq, candidate, spacingSq))
                        {
                            continue;
                        }

                        site = candidate;
                        __result = true;
                        return false;
                    }
                }
            }

            return true;
        }
        finally
        {
            SafeBaseScratch.Clear();
        }
    }

    private static bool HasMinOwnedBuildingSpacing(object? ecoInstance, FactionHQ hq, GlobalPosition candidate, float minSpacingSq)
    {
        if (hq.factionUnits != null)
        {
            foreach (PersistentID pid in hq.factionUnits)
            {
                if (!pid.TryGetUnit(out Unit unit) || unit == null || unit.disabled || !(unit is Building))
                {
                    continue;
                }

                if (FastMath.SquareDistance(unit.GlobalPosition(), candidate) < minSpacingSq)
                {
                    return false;
                }
            }
        }

        if (ecoInstance != null && GroundControlReflection.EcoConstructionSitesField?.GetValue(ecoInstance) is IList sites)
        {
            for (int i = 0; i < sites.Count; i++)
            {
                object? pending = sites[i];
                if (pending == null
                    || GroundControlReflection.SiteHqProp?.GetValue(pending) is not FactionHQ siteHq
                    || !ReferenceEquals(siteHq, hq)
                    || GroundControlReflection.SitePositionProp?.GetValue(pending) is not GlobalPosition sitePos)
                {
                    continue;
                }

                if (FastMath.SquareDistance(sitePos, candidate) < minSpacingSq)
                {
                    return false;
                }
            }
        }

        return true;
    }
}

[HarmonyPatch]
internal static class GetEnemyDockCostOutpostPatch
{
    private static bool Prepare() => GroundControlReflection.EcoGetEnemyDockCostMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoGetEnemyDockCostMethod!;

    private static void Postfix(object __instance, FactionHQ hq, int upToLevel, ref float __result)
    {
        if (upToLevel == 1)
        {
            __result = AiCoastalOutpostService.Instance.EvaluateDockOrOutpostCost(__instance, hq, __result);
            return;
        }

        if (hq != null && AiCoastalOutpostService.Instance.IsEnabledFor(hq))
        {
            int targetLevel = Mathf.Max(upToLevel, GroundControlReflection.GetNavalDockLevel(hq) + 1);
            if (!AiCoastalOutpostService.Instance.IsEconomyReadyForNavalDockLevel(__instance, hq, targetLevel))
            {
                __result = 0f;
            }
        }
    }
}

[HarmonyPatch]
internal static class TryBuildEnemyDockOutpostPatch
{
    private static bool Prepare() => GroundControlReflection.EcoTryBuildEnemyDockMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoTryBuildEnemyDockMethod!;

    private static bool Prefix(object __instance, FactionHQ hq, int upToLevel, ref bool __result)
    {
        if (upToLevel == 1 && AiCoastalOutpostService.Instance.TryInterceptEnemyDockBuild(__instance, hq, out bool handled))
        {
            __result = handled;
            return false;
        }

        if (hq != null && AiCoastalOutpostService.Instance.IsEnabledFor(hq))
        {
            int targetLevel = Mathf.Max(1, GroundControlReflection.GetNavalDockLevel(hq) + 1);
            if (targetLevel <= upToLevel && !AiCoastalOutpostService.Instance.IsEconomyReadyForNavalDockLevel(__instance, hq, targetLevel))
            {
                __result = false;
                return false;
            }
        }

        return true;
    }
}

[HarmonyPatch]
internal static class TryUpgradeEnemyNavalDockOutpostPatch
{
    private static bool Prepare() => GroundControlReflection.EcoTryUpgradeEnemyNavalDockMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoTryUpgradeEnemyNavalDockMethod!;

    private static bool Prefix(object __instance, FactionHQ hq, ref bool __result)
    {
        if (hq != null && AiCoastalOutpostService.Instance.IsEnabledFor(hq))
        {
            int targetLevel = GroundControlReflection.GetNavalDockLevel(hq) + 1;
            if (!AiCoastalOutpostService.Instance.IsEconomyReadyForNavalDockLevel(__instance, hq, targetLevel))
            {
                __result = false;
                return false;
            }
        }

        return true;
    }
}

[HarmonyPatch]
internal static class HasGivenUpOnDockOutpostPatch
{
    private static bool Prepare() => GroundControlReflection.EcoHasGivenUpOnDockMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoHasGivenUpOnDockMethod!;

    private static void Postfix(object __instance, FactionHQ hq, ref bool __result)
    {
        if (__result && AiCoastalOutpostService.Instance.HasBuiltCoastalTowerReady(__instance, hq))
        {
            __result = false;
        }
    }
}

[HarmonyPatch]
internal static class TryFindShoreSiteOutpostPatch
{
    private static bool Prepare() => GroundControlReflection.TryFindShoreSiteMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.TryFindShoreSiteMethod!;

    private static void Postfix(
        object __instance,
        FactionHQ hq,
        BuildingDefinition dock,
        ref GlobalPosition site,
        ref bool __result)
    {
        if (!__result
            && !AiCoastalOutpostService.Instance.IsProbingVanillaShore
            && AiCoastalOutpostService.Instance.TryFindBuiltCoastalTowerDockSite(__instance, hq, dock, out GlobalPosition fallbackDock))
        {
            site = fallbackDock;
            __result = true;
        }
    }
}

[HarmonyPatch]
internal static class ReviewBuildersOutpostPatch
{
    private static bool Prepare() => GroundControlReflection.EcoReviewBuildersMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoReviewBuildersMethod!;

    private static void Postfix(object __instance)
    {
        AiCoastalOutpostService.Instance.TryAssignRemoteBuilders(__instance);
    }
}

[HarmonyPatch]
internal static class TryGetStrandedBuilderOutpostPatch
{
    private static bool Prepare() => GroundControlReflection.EcoTryGetStrandedBuilderMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoTryGetStrandedBuilderMethod!;

    private static void Postfix(
        object __instance,
        FactionHQ hq,
        ref GlobalPosition site,
        ref Unit? builder,
        ref bool __result)
    {
        if (!__result
            && AiCoastalOutpostService.Instance.TryFindStrandedRemoteBuilder(__instance, hq, out GlobalPosition foundSite, out Unit? foundBuilder)
            && foundBuilder != null)
        {
            site = foundSite;
            builder = foundBuilder;
            __result = true;
        }
    }
}

[HarmonyPatch]
internal static class IsStuckAiBuilderOutpostPatch
{
    private static bool Prepare() => GroundControlReflection.EcoIsStuckAiBuilderMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoIsStuckAiBuilderMethod!;

    private static bool Prefix(object site, Unit builder, ref bool __result)
    {
        if (AiCoastalOutpostService.Instance.ShouldSuppressStuckRemoteBuilder(site, builder))
        {
            __result = false;
            return false;
        }

        return true;
    }
}

[HarmonyPatch]
internal static class SlingEndJobOutpostPatch
{
    private static bool Prepare() => GroundControlReflection.SlingEndJobMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.SlingEndJobMethod!;

    private static void Prefix(object? job)
    {
        if (job == null)
        {
            return;
        }

        if (GameManager.gameState != GameState.SinglePlayer)
        {
            NuclearOption.Networking.NetworkManagerNuclearOption? net = NuclearOption.Networking.NetworkManagerNuclearOption.i;
            if (net == null || net.Server == null || !net.Server.Active)
            {
                return;
            }
        }

        bool carrying = GroundControlReflection.SlingJobCarryingProp?.GetValue(job) is bool c && c;
        if (carrying)
        {
            return;
        }

        Unit? load = GroundControlReflection.SlingJobLoadProp?.GetValue(job) as Unit;
        GlobalPosition drop = GroundControlReflection.SlingJobDropProp?.GetValue(job) is GlobalPosition d ? d : default;
        AiCoastalOutpostService.Instance.NotifyFailedSlingLift(GroundControlReflection.GetEcoInstance(), load, drop);
    }
}
