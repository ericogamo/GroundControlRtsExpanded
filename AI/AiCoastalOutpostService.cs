using System;
using System.Collections;
using System.Collections.Generic;
using Mirage;
using NuclearOption.Networking;
using RoadPathfinding;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal sealed class AiCoastalOutpostService : IAddonResetSession
{
    private const float BaseWaterCheckCooldownSeconds = 15f;
    private const float PlanScanCooldownSeconds = 20f;
    private const float StaleBlueprintBlockSeconds = 300f;
    private const float BlockedRadiusMeters = 600f;
    private const float SeaLaneDedupCellMeters = 1500f;
    private const int MaxSeaSamples = 18;
    private const float ShoreMarchStepMeters = 250f;
    private const float MaxCoastMarchMeters = 28000f;
    private const float MinDockTowerSpacingMeters = 75f;
    private const float MaxDockTowerSpacingMeters = 850f;
    private const float TowerSearchRadiusMeters = 950f;
    private const float RemoteBuilderAirThresholdMeters = 1500f;

    private static readonly float[] CoastFanAnglesDeg = { 0f, 35f, -35f };
    private static readonly float[] ShoreBackoffsMeters = { 10f, 35f, 60f, 85f };
    private static readonly float[] TowerInwardAnglesDeg = { 0f, 25f, -25f, 55f, -55f, 90f, -90f };
    private static readonly float[] TowerRadiiMeters = { 140f, 230f, 340f, 480f, 650f };
    private static readonly float[] FineDockBackoffsMeters = { 0f, 20f, 40f, 60f };

    private sealed class CoastalOutpostState
    {
        internal bool KnownBaseHasWater;
        internal int CheckedBaseCount = -1;
        internal float NextBaseWaterCheckAt;

        internal bool HasPlan;
        internal GlobalPosition PlannedTowerSite;
        internal GlobalPosition PlannedDockSite;
        internal bool PlanDriveable;
        internal float NextPlanScanAt;

        internal bool HasActiveTrackedBlueprint;
        internal GlobalPosition ActiveBlueprintSite;
        internal float ActiveBlueprintQueuedAt;

        internal readonly List<GlobalPosition> BlockedSites = new();
    }

    internal static AiCoastalOutpostService Instance { get; } = new();

    internal bool IsProbingVanillaShore { get; private set; }

    private readonly Dictionary<FactionHQ, CoastalOutpostState> states = new();
    private readonly Dictionary<Unit, float> failedLiftBuildersUntil = new();
    private readonly List<Unit> staleFailedLiftScratch = new();
    private readonly Dictionary<(int CellX, int CellZ), int> failedLiftSiteCounts = new();
    private readonly List<Airbase> ownBasesScratch = new(16);
    private readonly List<(GlobalPosition Pos, float OwnDistSq, int BaseIndex)> seaCandidateScratch = new(128);
    private readonly HashSet<(int, int)> seaCellScratch = new();
    private readonly List<Unit> idleBuilderScratch = new(16);
    private readonly List<GlobalPosition> ownAnchorsScratch = new(16);

    private AiCoastalOutpostService()
    {
    }

    public void ResetSession()
    {
        states.Clear();
        failedLiftBuildersUntil.Clear();
        failedLiftSiteCounts.Clear();
        ownBasesScratch.Clear();
        seaCandidateScratch.Clear();
        seaCellScratch.Clear();
        idleBuilderScratch.Clear();
        ownAnchorsScratch.Clear();
        IsProbingVanillaShore = false;
    }

    private CoastalOutpostState GetOrCreateState(FactionHQ hq)
    {
        if (!states.TryGetValue(hq, out CoastalOutpostState state))
        {
            state = new CoastalOutpostState();
            states[hq] = state;
        }

        return state;
    }

    internal bool IsEnabledFor(FactionHQ? hq)
    {
        if (!AddonConfig.ModEnabled.Value
            || !AddonConfig.EnableTacticalQualityAi.Value
            || !AddonConfig.EnableAiCoastalOutpost.Value
            || hq == null
            || hq.faction == null
            || !(hq is NetworkBehaviour nb)
            || !nb.IsServer)
        {
            return false;
        }

        if (AiEconomySettings.GetActiveProfile().IsVanilla)
        {
            return false;
        }

        return GroundControlReflection.IsSafeAiFaction(hq, GroundControlReflection.GetLocalHq());
    }

    internal float EvaluateDockOrOutpostCost(object ecoInstance, FactionHQ? hq, float vanillaCost)
    {
        if (!IsEnabledFor(hq) || hq == null)
        {
            return vanillaCost;
        }

        if (GroundControlReflection.GetNavalDockLevel(hq) > 0
            || GroundControlReflection.EcoCountNavalDockBlueprints(hq) > 0)
        {
            return 0f;
        }

        if (!IsEconomyReadyForNavalDockLevel(ecoInstance, hq, 1))
        {
            return 0f;
        }

        BuildingDefinition? dockDef = GroundControlReflection.EcoResolveNavalDockDefinition(ecoInstance);
        if (dockDef == null)
        {
            return vanillaCost;
        }

        if (HasBaseWithWater(ecoInstance, hq, dockDef))
        {
            GroundControlReflection.EcoClearShoreSearchFailed(ecoInstance, hq);
            return vanillaCost;
        }

        if (TryFindBuiltCoastalTowerDockSite(ecoInstance, hq, dockDef, out _))
        {
            GroundControlReflection.EcoClearShoreSearchFailed(ecoInstance, hq);
            return IsEconomyReadyForOutpost(ecoInstance, hq)
                ? GroundControlReflection.EcoGetNavalDockBuildCost()
                : 0f;
        }

        if (CountControlTowerBlueprints(ecoInstance, hq) > 0)
        {
            return 0f;
        }

        if (!IsEconomyReadyForOutpost(ecoInstance, hq))
        {
            return 0f;
        }

        BuildingDefinition? towerDef = ResolveControlTowerDefinition();
        if (towerDef == null)
        {
            return 0f;
        }

        if (TryEnsureCoastalOutpostPlan(ecoInstance, hq, towerDef, dockDef, out _, out _))
        {
            return GroundControlReflection.GetStructureCost(towerDef);
        }

        return 0f;
    }

    internal bool TryInterceptEnemyDockBuild(object ecoInstance, FactionHQ? hq, out bool buildResult)
    {
        buildResult = false;
        if (!IsEnabledFor(hq) || hq == null)
        {
            return false;
        }

        if (GroundControlReflection.GetNavalDockLevel(hq) > 0)
        {
            return false;
        }

        if (GroundControlReflection.EcoCountNavalDockBlueprints(hq) > 0)
        {
            buildResult = false;
            return true;
        }

        if (!IsEconomyReadyForNavalDockLevel(ecoInstance, hq, 1))
        {
            buildResult = false;
            return true;
        }

        BuildingDefinition? dockDef = GroundControlReflection.EcoResolveNavalDockDefinition(ecoInstance);
        if (dockDef == null)
        {
            return false;
        }

        if (HasBaseWithWater(ecoInstance, hq, dockDef))
        {
            GroundControlReflection.EcoClearShoreSearchFailed(ecoInstance, hq);
            return false;
        }

        if (TryFindBuiltCoastalTowerDockSite(ecoInstance, hq, dockDef, out _))
        {
            GroundControlReflection.EcoClearShoreSearchFailed(ecoInstance, hq);
            return false;
        }

        if (CountControlTowerBlueprints(ecoInstance, hq) > 0)
        {
            buildResult = false;
            return true;
        }

        if (!IsEconomyReadyForOutpost(ecoInstance, hq))
        {
            buildResult = false;
            return true;
        }

        BuildingDefinition? towerDef = ResolveControlTowerDefinition();
        if (towerDef == null
            || !TryEnsureCoastalOutpostPlan(ecoInstance, hq, towerDef, dockDef, out GlobalPosition towerSite, out GlobalPosition dockSite))
        {
            buildResult = false;
            return true;
        }

        float towerCost = GroundControlReflection.GetStructureCost(towerDef);
        if (hq.factionFunds < towerCost)
        {
            buildResult = false;
            return true;
        }

        string label = GroundControlReflection.GetStructureLabel(towerDef);
        if (GroundControlReflection.QueueEnemyStructureBlueprint(ecoInstance, hq, towerDef, towerSite, towerCost, label))
        {
            CoastalOutpostState state = GetOrCreateState(hq);
            state.HasActiveTrackedBlueprint = true;
            state.ActiveBlueprintSite = towerSite;
            state.ActiveBlueprintQueuedAt = Time.time;
            GroundControlReflection.EcoClearShoreSearchFailed(ecoInstance, hq);

            AddonLog.Info(
                $"[AI Outpost] {hq.faction?.name ?? "AI"} queued coastal {label} at " +
                $"({towerSite.x:0}, {towerSite.z:0}) to unlock Naval Dock at ({dockSite.x:0}, {dockSite.z:0}).");

            buildResult = true;
            return true;
        }

        buildResult = false;
        return true;
    }

    internal bool HasBuiltCoastalTowerReady(object ecoInstance, FactionHQ? hq)
    {
        if (!IsEnabledFor(hq) || hq == null)
        {
            return false;
        }

        BuildingDefinition? dockDef = GroundControlReflection.EcoResolveNavalDockDefinition(ecoInstance);
        return dockDef != null && TryFindBuiltCoastalTowerDockSite(ecoInstance, hq, dockDef, out _);
    }

    internal void OnControlTowerBuilt(object? ecoInstance, FactionHQ? hq, Unit? unit)
    {
        if (!IsEnabledFor(hq) || hq == null || unit == null || !GroundControlReflection.BuildPreviewIsControlTowerUnit(unit))
        {
            return;
        }

        CoastalOutpostState state = GetOrCreateState(hq);
        state.HasActiveTrackedBlueprint = false;
        state.NextBaseWaterCheckAt = 0f;
        GroundControlReflection.EcoClearShoreSearchFailed(ecoInstance ?? GroundControlReflection.GetEcoInstance(), hq);

        AddonLog.Info(
            $"[AI Outpost] {hq.faction?.name ?? "AI"} completed {unit.unitName ?? "Control Tower"} — ready to place Naval Dock.");
    }

    internal bool TryFindBuiltCoastalTowerDockSite(
        object ecoInstance,
        FactionHQ? hq,
        BuildingDefinition? dockDef,
        out GlobalPosition dockSite)
    {
        dockSite = default;
        if (!IsEnabledFor(hq) || hq == null || dockDef == null || hq.factionUnits == null)
        {
            return false;
        }

        object? preview = GroundControlReflection.EcoGetPreview(ecoInstance);
        if (preview == null)
        {
            return false;
        }

        CoastalOutpostState state = GetOrCreateState(hq);
        foreach (PersistentID pid in hq.factionUnits)
        {
            if (!pid.TryGetUnit(out Unit unit)
                || unit == null
                || unit.disabled
                || !(unit is Building)
                || !GroundControlReflection.BuildPreviewIsControlTowerUnit(unit))
            {
                continue;
            }

            GlobalPosition towerPos = unit.GlobalPosition();
            if (state.HasPlan
                && !IsNearBlockedSite(state, state.PlannedDockSite)
                && FastMath.Distance(towerPos, state.PlannedDockSite) <= TowerSearchRadiusMeters
                && GroundControlReflection.BuildPreviewIsSiteAllowed(preview, dockDef, state.PlannedDockSite, hq, out _, navalDock: true))
            {
                dockSite = state.PlannedDockSite;
                return true;
            }

            if (TryFindFineShoreNearTower(preview, state, dockDef, hq, towerPos, out GlobalPosition foundDock))
            {
                state.HasPlan = true;
                state.PlannedTowerSite = towerPos;
                state.PlannedDockSite = foundDock;
                dockSite = foundDock;
                return true;
            }
        }

        return false;
    }

    internal void TryAssignRemoteBuilders(object ecoInstance)
    {
        if (!AddonConfig.ModEnabled.Value
            || !AddonConfig.EnableTacticalQualityAi.Value
            || !AddonConfig.EnableAiCoastalOutpost.Value)
        {
            return;
        }

        if (GroundControlReflection.EcoConstructionSitesField?.GetValue(ecoInstance) is not IList sites || sites.Count == 0)
        {
            return;
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        Dictionary<Unit, float>? resting = GroundControlReflection.EcoGetBuilderResting(ecoInstance);
        float now = Time.time;

        if (failedLiftBuildersUntil.Count > 0)
        {
            staleFailedLiftScratch.Clear();
            foreach (KeyValuePair<Unit, float> kv in failedLiftBuildersUntil)
            {
                if (kv.Key == null || kv.Key.disabled || now >= kv.Value)
                {
                    staleFailedLiftScratch.Add(kv.Key!);
                }
            }

            for (int r = 0; r < staleFailedLiftScratch.Count; r++)
            {
                failedLiftBuildersUntil.Remove(staleFailedLiftScratch[r]);
            }

            staleFailedLiftScratch.Clear();
        }

        try
        {
            for (int i = 0; i < sites.Count; i++)
            {
                object? cs = sites[i];
                if (cs == null)
                {
                    continue;
                }

                FactionHQ? hq = GroundControlReflection.SiteHqProp?.GetValue(cs) as FactionHQ;
                BuildingDefinition? def = GroundControlReflection.SiteDefinitionProp?.GetValue(cs) as BuildingDefinition;
                if (hq == null || def == null || !(hq is NetworkBehaviour nb) || !nb.IsServer || !GroundControlReflection.IsSafeAiFaction(hq, localHq))
                {
                    continue;
                }

                bool isCoastalCandidate = GroundControlReflection.BuildPreviewIsControlTower(def)
                    || GroundControlReflection.IsSiteKindNavalDock(cs);
                if (!isCoastalCandidate)
                {
                    continue;
                }

                List<Unit>? builders = GroundControlReflection.SiteGetBuilders(cs);
                if (builders == null || builders.Count > 0)
                {
                    continue;
                }

                if (GroundControlReflection.SitePositionProp?.GetValue(cs) is not GlobalPosition sitePos)
                {
                    continue;
                }

                GroundControlReflection.EcoCollectIdleBuilders(hq, idleBuilderScratch);
                for (int r = idleBuilderScratch.Count - 1; r >= 0; r--)
                {
                    Unit idle = idleBuilderScratch[r];
                    if ((resting != null && resting.TryGetValue(idle, out float restUntil) && now < restUntil)
                        || (failedLiftBuildersUntil.TryGetValue(idle, out float liftRetryAt) && now < liftRetryAt))
                    {
                        idleBuilderScratch.RemoveAt(r);
                    }
                }

                bool canAirlift = CanFactionAirliftBuilder(hq);
                Unit? bestDriveable = null;
                float bestDriveSq = float.MaxValue;
                Unit? bestAny = null;
                float bestAnySq = float.MaxValue;

                for (int j = 0; j < idleBuilderScratch.Count; j++)
                {
                    Unit candidate = idleBuilderScratch[j];
                    if (candidate == null
                        || candidate.disabled
                        || (failedLiftBuildersUntil.TryGetValue(candidate, out float liftRetryAt) && now < liftRetryAt))
                    {
                        continue;
                    }

                    GlobalPosition cPos = candidate.GlobalPosition();
                    float distSq = FastMath.SquareDistance(cPos, sitePos);
                    if (canAirlift && distSq < bestAnySq)
                    {
                        bestAnySq = distSq;
                        bestAny = candidate;
                    }

                    if (distSq < bestDriveSq && GroundControlReflection.CanDriveBetween(cPos, sitePos))
                    {
                        bestDriveSq = distSq;
                        bestDriveable = candidate;
                    }
                }

                Unit? chosen = bestDriveable ?? bestAny;
                if (chosen != null)
                {
                    GlobalPosition chosenPos = chosen.GlobalPosition();
                    GroundControlReflection.EcoSendBuilder(cs, chosen);
                    if (!GroundControlReflection.CanDriveBetween(chosenPos, sitePos))
                    {
                        GroundControlReflection.MoveClearOrder(chosen);
                        GroundControlReflection.TrySetAiDestination(chosen, chosenPos);
                    }

                    continue;
                }

                if (GroundControlReflection.EcoHasBusyBuilderNear(ecoInstance, hq, sitePos))
                {
                    continue;
                }

                if (now < GroundControlReflection.SiteGetNextBuilderSpawnAt(cs) || GroundControlReflection.UnitCapIsGroundFull(hq))
                {
                    continue;
                }

                VehicleDefinition? repairDef = GroundControlReflection.FindRepairVehicleDefinition(hq);
                if (repairDef == null)
                {
                    continue;
                }

                float builderCost = Mathf.Max(0f, repairDef.value);
                if (hq.factionFunds < builderCost)
                {
                    continue;
                }

                if (!hq.TryGetNearestDepot(sitePos.ToLocalPosition(), float.MaxValue, out VehicleDepot depot) || depot == null)
                {
                    continue;
                }

                if (!GroundControlReflection.CanDriveBetween(((Unit)depot).GlobalPosition(), sitePos) && !canAirlift)
                {
                    continue;
                }

                if (!GroundControlReflection.TrySpend(hq, builderCost))
                {
                    continue;
                }

                if (GroundControlReflection.EcoTryGiveStartingBuilders(ecoInstance, hq, depot, 1))
                {
                    GroundControlReflection.SiteSetNextBuilderSpawnAt(cs, now + 60f);
                    AddonLog.Info(
                        $"[AI Outpost] {hq.faction?.name ?? "AI"} rolled out a remote builder at {depot.name} for {def.unitName} ({builderCost:0} Cr).");
                }
                else
                {
                    hq.AddFunds(builderCost);
                }
            }
        }
        finally
        {
            idleBuilderScratch.Clear();
        }
    }

    internal bool TryFindStrandedRemoteBuilder(
        object ecoInstance,
        FactionHQ? hq,
        out GlobalPosition sitePos,
        out Unit? builder)
    {
        sitePos = default;
        builder = null;
        if (!IsEnabledFor(hq) || hq == null)
        {
            return false;
        }

        if (GroundControlReflection.EcoConstructionSitesField?.GetValue(ecoInstance) is not IList sites)
        {
            return false;
        }

        for (int i = 0; i < sites.Count; i++)
        {
            object? cs = sites[i];
            if (cs == null
                || !ReferenceEquals(GroundControlReflection.SiteHqProp?.GetValue(cs) as FactionHQ, hq))
            {
                continue;
            }

            float progress = GroundControlReflection.SiteProgressProp?.GetValue(cs) is float p ? p : 0f;
            if (progress > 0f)
            {
                continue;
            }

            BuildingDefinition? def = GroundControlReflection.SiteDefinitionProp?.GetValue(cs) as BuildingDefinition;
            if (def == null
                || (!GroundControlReflection.BuildPreviewIsControlTower(def) && !GroundControlReflection.IsSiteKindNavalDock(cs)))
            {
                continue;
            }

            if (GroundControlReflection.SitePositionProp?.GetValue(cs) is not GlobalPosition csPos)
            {
                continue;
            }

            List<Unit>? builders = GroundControlReflection.SiteGetBuilders(cs);
            if (builders == null)
            {
                continue;
            }

            float now = Time.time;
            for (int j = 0; j < builders.Count; j++)
            {
                Unit? assigned = builders[j];
                if (assigned == null || assigned.disabled || assigned.IsSlung())
                {
                    continue;
                }

                if (failedLiftBuildersUntil.TryGetValue(assigned, out float liftRetryAt) && now < liftRetryAt)
                {
                    continue;
                }

                if (IsUnderStaticCover(assigned))
                {
                    continue;
                }

                GlobalPosition bPos = assigned.GlobalPosition();
                if (EvaluateKnownHostileExposure(hq, csPos) >= 450f || EvaluateKnownHostileExposure(hq, bPos) >= 450f)
                {
                    continue;
                }

                float dist = FastMath.Distance(bPos, csPos);
                if (dist > RemoteBuilderAirThresholdMeters
                    || (dist > 250f && !GroundControlReflection.CanDriveBetween(bPos, csPos)))
                {
                    sitePos = csPos;
                    builder = assigned;
                    return true;
                }
            }
        }

        return false;
    }

    internal bool ShouldSuppressStuckRemoteBuilder(object? site, Unit? builder)
    {
        if (site == null || builder == null || builder.disabled || builder.IsSlung())
        {
            return false;
        }

        if (IsUnderStaticCover(builder)
            || (failedLiftBuildersUntil.TryGetValue(builder, out float liftRetryAt) && Time.time < liftRetryAt))
        {
            return false;
        }

        FactionHQ? hq = GroundControlReflection.SiteHqProp?.GetValue(site) as FactionHQ;
        if (!IsEnabledFor(hq) || hq == null)
        {
            return false;
        }

        BuildingDefinition? def = GroundControlReflection.SiteDefinitionProp?.GetValue(site) as BuildingDefinition;
        if (def == null
            || (!GroundControlReflection.BuildPreviewIsControlTower(def) && !GroundControlReflection.IsSiteKindNavalDock(site)))
        {
            return false;
        }

        if (GroundControlReflection.SitePositionProp?.GetValue(site) is not GlobalPosition sitePos)
        {
            return false;
        }

        GlobalPosition builderPos = builder.GlobalPosition();
        return FastMath.Distance(builderPos, sitePos) > 250f
            && !GroundControlReflection.CanDriveBetween(builderPos, sitePos)
            && CanFactionAirliftBuilder(hq);
    }

    private static BuildingDefinition? ResolveControlTowerDefinition()
    {
        List<BuildingDefinition>? buildings = Encyclopedia.i?.buildings;
        if (buildings == null)
        {
            return null;
        }

        for (int i = 0; i < buildings.Count; i++)
        {
            BuildingDefinition? def = buildings[i];
            if (def != null && def.unitPrefab != null && GroundControlReflection.BuildPreviewIsControlTower(def))
            {
                return def;
            }
        }

        return null;
    }

    private bool HasBaseWithWater(object ecoInstance, FactionHQ hq, BuildingDefinition dockDef)
    {
        CoastalOutpostState state = GetOrCreateState(hq);
        int baseCount = GroundControlReflection.CountLandBases(hq);
        float now = Time.time;

        if (state.CheckedBaseCount == baseCount && now < state.NextBaseWaterCheckAt)
        {
            return state.KnownBaseHasWater;
        }

        bool found;
        IsProbingVanillaShore = true;
        try
        {
            found = GroundControlReflection.TryFindShoreSite(ecoInstance, hq, dockDef, out _);
        }
        finally
        {
            IsProbingVanillaShore = false;
        }

        state.CheckedBaseCount = baseCount;
        state.NextBaseWaterCheckAt = now + BaseWaterCheckCooldownSeconds;
        state.KnownBaseHasWater = found;
        return found;
    }

    internal bool IsEconomyReadyForNavalDockLevel(object ecoInstance, FactionHQ hq, int targetLevel)
    {
        if (ecoInstance == null || hq == null)
        {
            return false;
        }

        if (targetLevel >= 3)
        {
            return EnemyTier3NavalDockHelper.IsReadyForTier3NavalDock(ecoInstance, hq, out _);
        }

        AiStrategicPhase phase = AiStrategicDirectorService.Instance.EvaluatePhase(ecoInstance, hq);
        if (phase == AiStrategicPhase.OpeningEconomy || phase == AiStrategicPhase.EmergencyReconstruction)
        {
            return false;
        }

        int bestDepot = GroundControlReflection.GetBestDepotLevel(hq);
        int goldMines = GroundControlReflection.CountGoldMines(ecoInstance, hq);
        float enemyGross = GroundControlReflection.GetHqIncomePerMinute(ecoInstance, hq);

        if (targetLevel <= 1)
        {
            float gasL3Income = GroundControlReflection.EcoGetGasTerminalTier3Income();
            PlayerThreatSnapshot threat = AiStrategicDirectorService.Instance.ReadPlayerThreat(hq);
            float threshold = threat.Ships > 0 ? Mathf.Min(120f, gasL3Income) : Mathf.Max(75f, gasL3Income);
            return enemyGross >= threshold && (goldMines >= 1 || (bestDepot >= 2 && enemyGross >= 225f));
        }

        // Dock Level 2: prevent upgrading L1->L2 on weak Opening/1-mine economies before Depot/TechTier mature.
        if (bestDepot < 2)
        {
            return false;
        }

        if (DynamicCapAndTechService.IsActive && DynamicCapAndTechService.Instance.GetSnapshot(hq).TechTier < 2)
        {
            return false;
        }

        float enemyNet = GroundControlReflection.GetEnemyNetIncomePerMinute(hq);
        return goldMines >= 2 || enemyNet >= 350f;
    }

    internal void NotifyFailedSlingLift(object? ecoInstance, Unit? load, GlobalPosition drop)
    {
        if (load == null)
        {
            return;
        }

        failedLiftBuildersUntil[load] = Time.time + 180f;

        FactionHQ? hq = load.NetworkHQ;
        if (hq == null || !IsEnabledFor(hq))
        {
            return;
        }

        var cellKey = (Mathf.RoundToInt(drop.x / 500f), Mathf.RoundToInt(drop.z / 500f));
        int count = failedLiftSiteCounts.TryGetValue(cellKey, out int prev) ? prev + 1 : 1;
        failedLiftSiteCounts[cellKey] = count;

        if (count >= 2)
        {
            RecordBlockedSite(GetOrCreateState(hq), drop);
        }

        if (ecoInstance != null && GroundControlReflection.EcoConstructionSitesField?.GetValue(ecoInstance) is IList sites)
        {
            for (int i = 0; i < sites.Count; i++)
            {
                object? cs = sites[i];
                if (cs == null || !ReferenceEquals(GroundControlReflection.SiteHqProp?.GetValue(cs) as FactionHQ, hq))
                {
                    continue;
                }

                List<Unit>? builders = GroundControlReflection.SiteGetBuilders(cs);
                if (builders != null && builders.Remove(load))
                {
                    GroundControlReflection.MoveClearOrder(load);
                    break;
                }
            }
        }
    }

    private static bool IsUnderStaticCover(Unit unit)
    {
        if (unit == null)
        {
            return false;
        }

        Vector3 localPos = unit.transform.position;
        RaycastHit[] hits = Physics.SphereCastAll(localPos + Vector3.up * 120f, 3.0f, Vector3.down, 118f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null
                || col.isTrigger
                || col.transform.IsChildOf(unit.transform)
                || string.Equals(col.GetType().Name, "TerrainCollider", StringComparison.Ordinal)
                || col.GetComponentInParent<GroundVehicle>() != null
                || col.GetComponentInParent<Aircraft>() != null)
            {
                continue;
            }

            if (hits[i].point.y > localPos.y + 2.5f)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEconomyReadyForOutpost(object ecoInstance, FactionHQ hq)
        => Instance.IsEconomyReadyForNavalDockLevel(ecoInstance, hq, 1);

    private static int CountControlTowerBlueprints(object ecoInstance, FactionHQ hq)
    {
        if (GroundControlReflection.EcoConstructionSitesField?.GetValue(ecoInstance) is not IList sites)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < sites.Count; i++)
        {
            object? cs = sites[i];
            if (cs == null
                || !ReferenceEquals(GroundControlReflection.SiteHqProp?.GetValue(cs) as FactionHQ, hq))
            {
                continue;
            }

            BuildingDefinition? def = GroundControlReflection.SiteDefinitionProp?.GetValue(cs) as BuildingDefinition;
            if (def != null && GroundControlReflection.BuildPreviewIsControlTower(def))
            {
                count++;
            }
        }

        return count;
    }

    private bool TryEnsureCoastalOutpostPlan(
        object ecoInstance,
        FactionHQ hq,
        BuildingDefinition towerDef,
        BuildingDefinition dockDef,
        out GlobalPosition towerSite,
        out GlobalPosition dockSite)
    {
        CoastalOutpostState state = GetOrCreateState(hq);
        if (state.HasActiveTrackedBlueprint
            && CountControlTowerBlueprints(ecoInstance, hq) == 0
            && !HasBuiltControlTowerNear(hq, state.ActiveBlueprintSite, 250f))
        {
            state.HasActiveTrackedBlueprint = false;
            RecordBlockedSite(state, state.ActiveBlueprintSite);
            state.HasPlan = false;
            if (Time.time - state.ActiveBlueprintQueuedAt <= StaleBlueprintBlockSeconds)
            {
                state.NextPlanScanAt = Mathf.Max(state.NextPlanScanAt, Time.time + 60f);
            }
        }

        object? preview = GroundControlReflection.EcoGetPreview(ecoInstance);
        if (preview == null)
        {
            towerSite = default;
            dockSite = default;
            return false;
        }

        if (state.HasPlan
            && GroundControlReflection.BuildPreviewIsSiteAllowed(preview, towerDef, state.PlannedTowerSite, hq, out _, navalDock: false)
            && GroundControlReflection.BuildPreviewIsShoreline(state.PlannedDockSite)
            && GroundControlReflection.BuildPreviewIsSiteAllowed(preview, dockDef, state.PlannedDockSite, null, out _, navalDock: true)
            && !IsNearBlockedSite(state, state.PlannedTowerSite))
        {
            towerSite = state.PlannedTowerSite;
            dockSite = state.PlannedDockSite;
            return true;
        }

        state.HasPlan = false;
        float now = Time.time;
        if (now < state.NextPlanScanAt)
        {
            towerSite = default;
            dockSite = default;
            return false;
        }

        state.NextPlanScanAt = now + PlanScanCooldownSeconds;
        if (TryPlanBestCoastalOutpost(preview, hq, state, towerDef, dockDef, out GlobalPosition bestTower, out GlobalPosition bestDock, out bool driveable))
        {
            state.HasPlan = true;
            state.PlannedTowerSite = bestTower;
            state.PlannedDockSite = bestDock;
            state.PlanDriveable = driveable;
            towerSite = bestTower;
            dockSite = bestDock;

            AddonLog.Info(
                $"[AI Outpost] {hq.faction?.name ?? "AI"} planned coastal outpost at ({bestTower.x:0}, {bestTower.z:0}) " +
                $"for Naval Dock at ({bestDock.x:0}, {bestDock.z:0}) (driveable={driveable}).");
            return true;
        }

        towerSite = default;
        dockSite = default;
        return false;
    }

    private bool TryPlanBestCoastalOutpost(
        object preview,
        FactionHQ hq,
        CoastalOutpostState state,
        BuildingDefinition towerDef,
        BuildingDefinition dockDef,
        out GlobalPosition bestTower,
        out GlobalPosition bestDock,
        out bool bestDriveable)
    {
        bestTower = default;
        bestDock = default;
        bestDriveable = false;

        LevelInfo? levelInfo = NetworkSceneSingleton<LevelInfo>.i;
        RoadNetwork? seaLanes = levelInfo?.seaLanes;
        if (seaLanes == null || !seaLanes.Exists() || seaLanes.roads == null || seaLanes.roads.Count == 0)
        {
            return false;
        }

        ownAnchorsScratch.Clear();
        FactionHQ? enemyHq = GroundControlReflection.FindPrimaryHostileHq(hq);
        AiBattlefieldIntelService.Instance.GetAirbasesOrderedBySafety(hq, enemyHq, ownBasesScratch);
        for (int i = 0; i < ownBasesScratch.Count; i++)
        {
            Airbase ab = ownBasesScratch[i];
            if (ab != null && !ab.disabled && ab.center != null && GroundControlReflection.IsLandBase(ab))
            {
                ownAnchorsScratch.Add(ab.center.GlobalPosition());
            }
        }

        if (ownAnchorsScratch.Count == 0)
        {
            foreach (Airbase ab in hq.GetAirbases())
            {
                if (ab != null && !ab.disabled && ab.center != null && GroundControlReflection.IsLandBase(ab))
                {
                    ownAnchorsScratch.Add(ab.center.GlobalPosition());
                }
            }
        }

        if (ownAnchorsScratch.Count == 0)
        {
            return false;
        }

        seaCandidateScratch.Clear();
        seaCellScratch.Clear();
        foreach (Road? road in seaLanes.roads)
        {
            if (road?.points == null)
            {
                continue;
            }

            for (int i = 0; i < road.points.Count; i++)
            {
                GlobalPosition pt = road.points[i];
                if (!GroundControlReflection.IsBelowSeaLevel(pt))
                {
                    continue;
                }

                (int, int) cell = (
                    Mathf.RoundToInt(pt.x / SeaLaneDedupCellMeters),
                    Mathf.RoundToInt(pt.z / SeaLaneDedupCellMeters));
                if (!seaCellScratch.Add(cell))
                {
                    continue;
                }

                float nearestSq = float.MaxValue;
                int nearestIdx = 0;
                for (int b = 0; b < ownAnchorsScratch.Count; b++)
                {
                    float dSq = FastMath.SquareDistance(pt, ownAnchorsScratch[b]);
                    if (dSq < nearestSq)
                    {
                        nearestSq = dSq;
                        nearestIdx = b;
                    }
                }

                seaCandidateScratch.Add((pt, nearestSq, nearestIdx));
            }
        }

        if (seaCandidateScratch.Count == 0)
        {
            return false;
        }

        seaCandidateScratch.Sort((a, b) => a.OwnDistSq.CompareTo(b.OwnDistSq));

        bool canAirlift = CanFactionAirliftBuilder(hq);
        float bestScore = float.MinValue;
        bool foundAny = false;
        int sampleLimit = Mathf.Min(MaxSeaSamples, seaCandidateScratch.Count);

        for (int s = 0; s < sampleLimit; s++)
        {
            GlobalPosition seaPt = seaCandidateScratch[s].Pos;
            GlobalPosition baseAnchor = ownAnchorsScratch[seaCandidateScratch[s].BaseIndex];
            Vector3 toLand = baseAnchor.AsVector3() - seaPt.AsVector3();
            toLand.y = 0f;
            if (toLand.sqrMagnitude < 1f)
            {
                continue;
            }

            float distToBase = toLand.magnitude;
            float maxMarch = Mathf.Min(distToBase + 1500f, MaxCoastMarchMeters);
            Vector3 baseDir = toLand / distToBase;

            for (int f = 0; f < CoastFanAnglesDeg.Length; f++)
            {
                Vector3 dir = Quaternion.Euler(0f, CoastFanAnglesDeg[f], 0f) * baseDir;
                if (!TryFindShoreAlongRay(preview, hq, state, towerDef, dockDef, seaPt, dir, maxMarch, out GlobalPosition candTower, out GlobalPosition candDock))
                {
                    continue;
                }

                float nearestOwnDist = float.MaxValue;
                GlobalPosition nearestOwnPos = baseAnchor;
                for (int b = 0; b < ownAnchorsScratch.Count; b++)
                {
                    float d = FastMath.Distance(candTower, ownAnchorsScratch[b]);
                    if (d < nearestOwnDist)
                    {
                        nearestOwnDist = d;
                        nearestOwnPos = ownAnchorsScratch[b];
                    }
                }

                bool driveable = GroundControlReflection.CanDriveBetween(nearestOwnPos, candTower);
                if (!driveable && !canAirlift)
                {
                    continue;
                }

                float seaDist = FastMath.Distance(seaPt, candDock);
                float exposure = EvaluateKnownHostileExposure(hq, candTower);
                if (!driveable && exposure >= 450f)
                {
                    continue;
                }

                float score = (driveable ? 2500f : 0f)
                    - nearestOwnDist * 0.08f
                    - seaDist * 0.04f
                    - exposure;

                if (!foundAny || score > bestScore)
                {
                    foundAny = true;
                    bestScore = score;
                    bestTower = candTower;
                    bestDock = candDock;
                    bestDriveable = driveable;
                }
            }
        }

        return foundAny;
    }

    private static bool TryFindShoreAlongRay(
        object preview,
        FactionHQ hq,
        CoastalOutpostState state,
        BuildingDefinition towerDef,
        BuildingDefinition dockDef,
        GlobalPosition seaOrigin,
        Vector3 inwardDir,
        float maxMarchMeters,
        out GlobalPosition towerSite,
        out GlobalPosition dockSite)
    {
        towerSite = default;
        dockSite = default;

        float lastWaterDist = 0f;
        bool sawWater = GroundControlReflection.EcoIsWater(seaOrigin, Vector3.zero, 0f);
        if (!sawWater)
        {
            return false;
        }

        for (float d = ShoreMarchStepMeters; d <= maxMarchMeters; d += ShoreMarchStepMeters)
        {
            if (GroundControlReflection.EcoIsWater(seaOrigin, inwardDir, d))
            {
                lastWaterDist = d;
                continue;
            }

            GlobalPosition lastWaterPos = OffsetPosition(seaOrigin, inwardDir, lastWaterDist);
            if (!GroundControlReflection.EcoIsOpenWater(lastWaterPos, -inwardDir, 0f)
                || !GroundControlReflection.EcoReachesSea(lastWaterPos))
            {
                // Narrow sandbar or inland pond; keep marching only along this ray if water resumes later
                lastWaterDist = d;
                continue;
            }

            float waterD = lastWaterDist;
            float landD = d;
            for (int iter = 0; iter < 5; iter++)
            {
                float mid = (waterD + landD) * 0.5f;
                if (GroundControlReflection.EcoIsWater(seaOrigin, inwardDir, mid))
                {
                    waterD = mid;
                }
                else
                {
                    landD = mid;
                }
            }

            for (int b = 0; b < ShoreBackoffsMeters.Length; b++)
            {
                GlobalPosition dockProbe = GroundControlReflection.SnapToTerrain(
                    OffsetPosition(seaOrigin, inwardDir, landD + ShoreBackoffsMeters[b]));
                if (IsNearBlockedSite(state, dockProbe)
                    || !GroundControlReflection.BuildPreviewIsShoreline(dockProbe))
                {
                    continue;
                }

                // Passing hq=null tests every terrain, slope, overlap, road, and runway check except base build radius.
                if (!GroundControlReflection.BuildPreviewIsSiteAllowed(preview, dockDef, dockProbe, null, out _, navalDock: true))
                {
                    continue;
                }

                if (TryFindTowerNearShore(preview, hq, state, towerDef, dockProbe, inwardDir, out GlobalPosition towerProbe))
                {
                    dockSite = dockProbe;
                    towerSite = towerProbe;
                    return true;
                }
            }

            return false;
        }

        return false;
    }

    private static bool TryFindTowerNearShore(
        object preview,
        FactionHQ hq,
        CoastalOutpostState state,
        BuildingDefinition towerDef,
        GlobalPosition dockPos,
        Vector3 inwardDir,
        out GlobalPosition towerPos)
    {
        towerPos = default;
        for (int r = 0; r < TowerRadiiMeters.Length; r++)
        {
            float radius = TowerRadiiMeters[r];
            for (int a = 0; a < TowerInwardAnglesDeg.Length; a++)
            {
                Vector3 dir = Quaternion.Euler(0f, TowerInwardAnglesDeg[a], 0f) * inwardDir;
                GlobalPosition cand = GroundControlReflection.SnapToTerrain(OffsetPosition(dockPos, dir, radius));
                if (GroundControlReflection.IsBelowSeaLevel(cand))
                {
                    continue;
                }

                float dockDist = FastMath.Distance(cand, dockPos);
                if (dockDist < MinDockTowerSpacingMeters || dockDist > MaxDockTowerSpacingMeters)
                {
                    continue;
                }

                if (IsNearBlockedSite(state, cand))
                {
                    continue;
                }

                if (GroundControlReflection.BuildPreviewIsSiteAllowed(preview, towerDef, cand, hq, out _, navalDock: false))
                {
                    towerPos = cand;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryFindFineShoreNearTower(
        object preview,
        CoastalOutpostState state,
        BuildingDefinition dockDef,
        FactionHQ hq,
        GlobalPosition towerPos,
        out GlobalPosition dockSite)
    {
        dockSite = default;
        float bestDist = float.MaxValue;

        const int rays = 24;
        for (int i = 0; i < rays; i++)
        {
            float angle = i * (Mathf.PI * 2f / rays);
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            float lastLand = 0f;

            for (float step = 125f; step <= TowerSearchRadiusMeters && step - 125f < bestDist; step += 125f)
            {
                if (!GroundControlReflection.EcoIsWater(towerPos, dir, step))
                {
                    lastLand = step;
                    continue;
                }

                if (!GroundControlReflection.EcoIsOpenWater(towerPos, dir, step)
                    || !GroundControlReflection.EcoReachesSea(OffsetPosition(towerPos, dir, step)))
                {
                    continue;
                }

                float landD = lastLand;
                float waterD = step;
                for (int b = 0; b < 5; b++)
                {
                    float mid = (landD + waterD) * 0.5f;
                    if (GroundControlReflection.EcoIsWater(towerPos, dir, mid))
                    {
                        waterD = mid;
                    }
                    else
                    {
                        landD = mid;
                    }
                }

                for (int k = 0; k < FineDockBackoffsMeters.Length; k++)
                {
                    float dist = Mathf.Max(0f, landD - FineDockBackoffsMeters[k]);
                    if (dist >= bestDist || dist > TowerSearchRadiusMeters)
                    {
                        continue;
                    }

                    GlobalPosition probe = GroundControlReflection.SnapToTerrain(OffsetPosition(towerPos, dir, dist));
                    if (!IsNearBlockedSite(state, probe)
                        && GroundControlReflection.BuildPreviewIsShoreline(probe)
                        && GroundControlReflection.BuildPreviewIsSiteAllowed(preview, dockDef, probe, hq, out _, navalDock: true))
                    {
                        bestDist = dist;
                        dockSite = probe;
                        break;
                    }
                }

                break;
            }
        }

        return bestDist < float.MaxValue;
    }

    private static bool CanFactionAirliftBuilder(FactionHQ hq)
    {
        VehicleDefinition? repairDef = GroundControlReflection.FindRepairVehicleDefinition(hq);
        if (repairDef == null || !repairDef.CanSlingLoad)
        {
            return false;
        }

        List<Aircraft> allAircraft = UnitRegistry.allAircraft;
        for (int i = 0; i < allAircraft.Count; i++)
        {
            Aircraft? ac = allAircraft[i];
            if (ac != null && !ac.disabled && ReferenceEquals(ac.NetworkHQ, hq) && GroundControlReflection.CanSlingLift(ac))
            {
                return true;
            }
        }

        if (hq.AircraftSupply != null)
        {
            foreach (AircraftDefinition def in hq.AircraftSupply.Keys)
            {
                if (def != null
                    && GroundControlReflection.IsRotaryAirframe(def)
                    && GroundControlReflection.AirCanAiFly(def)
                    && GroundControlReflection.AirHasSlingLoadout(def))
                {
                    return true;
                }
            }
        }

        foreach (Airbase ab in hq.GetAirbases())
        {
            if (ab == null || ab.disabled)
            {
                continue;
            }

            foreach (AircraftDefinition def in ab.GetAvailableAircraft())
            {
                if (def != null
                    && GroundControlReflection.IsRotaryAirframe(def)
                    && GroundControlReflection.AirCanAiFly(def)
                    && GroundControlReflection.AirHasSlingLoadout(def))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static float EvaluateKnownHostileExposure(FactionHQ hq, GlobalPosition candTower)
    {
        float penalty = 0f;
        if (hq.trackingDatabase != null)
        {
            float levelTime = Time.timeSinceLevelLoad;
            foreach (KeyValuePair<PersistentID, TrackingInfo> kv in hq.trackingDatabase)
            {
                TrackingInfo? info = kv.Value;
                if (info == null
                    || !info.TryGetUnit(out Unit unit)
                    || unit == null
                    || unit.disabled
                    || ReferenceEquals(unit.NetworkHQ, hq))
                {
                    continue;
                }

                float age = levelTime - info.lastSpottedTime;
                if (age > 180f)
                {
                    continue;
                }

                float dist = FastMath.Distance(candTower, unit.GlobalPosition());
                if (dist < 5000f)
                {
                    float weight = unit is Building || unit is Ship ? 0.25f : 0.10f;
                    penalty += (5000f - dist) * weight;
                }
            }
        }

        if (FactionRegistry.airbaseLookup != null)
        {
            foreach (Airbase? ab in FactionRegistry.airbaseLookup.Values)
            {
                if (ab == null
                    || ab.disabled
                    || ab.center == null
                    || ab.CurrentHQ == null
                    || ReferenceEquals(ab.CurrentHQ, hq))
                {
                    continue;
                }

                string? uniq = ab.SavedAirbase?.UniqueName;
                if (uniq != null && uniq.StartsWith("GCFWD|", StringComparison.Ordinal))
                {
                    continue;
                }

                float dist = FastMath.Distance(candTower, ab.center.GlobalPosition());
                if (dist < 6000f)
                {
                    penalty += (6000f - dist) * 0.35f;
                }
            }
        }

        return penalty;
    }

    private static bool HasBuiltControlTowerNear(FactionHQ hq, GlobalPosition center, float radiusMeters)
    {
        if (hq.factionUnits == null)
        {
            return false;
        }

        foreach (PersistentID pid in hq.factionUnits)
        {
            if (pid.TryGetUnit(out Unit unit)
                && unit != null
                && !unit.disabled
                && unit is Building
                && GroundControlReflection.BuildPreviewIsControlTowerUnit(unit)
                && FastMath.InRange(unit.GlobalPosition(), center, radiusMeters))
            {
                return true;
            }
        }

        return false;
    }

    private static void RecordBlockedSite(CoastalOutpostState state, GlobalPosition pos)
    {
        if (IsNearBlockedSite(state, pos))
        {
            return;
        }

        if (state.BlockedSites.Count >= 8)
        {
            state.BlockedSites.RemoveAt(0);
        }

        state.BlockedSites.Add(pos);
    }

    private static bool IsNearBlockedSite(CoastalOutpostState state, GlobalPosition pos)
    {
        for (int i = 0; i < state.BlockedSites.Count; i++)
        {
            if (FastMath.InRange(state.BlockedSites[i], pos, BlockedRadiusMeters))
            {
                return true;
            }
        }

        return false;
    }

    private static GlobalPosition OffsetPosition(GlobalPosition origin, Vector3 dir, float distance)
        => new(origin.x + dir.x * distance, origin.y, origin.z + dir.z * distance);
}
