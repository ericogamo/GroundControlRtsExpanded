using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal sealed class AiSupplyLogisticsService : IAddonTick, IAddonResetSession
{
    private const float TickIntervalSeconds = 5f;
    private const float TruckArriveRangeMeters = 55f;
    private const float CriticalSamAmmoFraction = 0.25f;
    private const float CriticalSamTruckCutoffMeters = 1500f;
    private const float LocalRearmAssistMeters = 90f;
    private const float LocalRearmCoverageMeters = 150f;
    private const float HeliCoverageCheckMeters = 220f;
    private const float InboundHeliDedupMeters = 500f;
    private const float StationCooldownGridMeters = 300f;
    private const float IdleTruckRestockThreshold = 0.50f;
    private const float IdleTruckRestockStopThreshold = 0.85f;
    private const float DryCramHeliAmmoThreshold = 0.35f;
    private const float ArtilleryRearmThreshold = 0.35f;
    private const float CooldownPruneIntervalSeconds = 60f;
    private const float ActiveTruckMinAmmoFraction = 0.15f;
    private const float MinStoreAmmoFraction = 0.10f;
    private const float StoreRefillRangeMeters = 150f;
    private const float LzOffsetMeters = 55f;
    private const float SlopeSampleRadiusMeters = 15f;
    private const float MaxLandingSlopeRadians = 18f * Mathf.Deg2Rad;

    private readonly struct AmmoNeed
    {
        internal Unit Unit { get; }
        internal bool IsSam { get; }
        internal float AmmoFraction { get; }

        internal AmmoNeed(Unit unit, bool isSam, float ammoFraction)
        {
            Unit = unit;
            IsSam = isSam;
            AmmoFraction = ammoFraction;
        }
    }

    private sealed class HeliDispatchCandidate
    {
        internal object AircraftOption { get; } = null!;
        internal AircraftDefinition Definition { get; } = null!;
        internal string AircraftLabel { get; } = string.Empty;
        internal Loadout Loadout { get; } = null!;
        internal WeaponMount CargoMount { get; } = null!;
        internal Airbase Airbase { get; } = null!;
        internal float TotalCost { get; }
        internal float DistanceMeters { get; }
        internal bool InStock { get; }
        internal float AmmoCapacityKg { get; }

        internal HeliDispatchCandidate(
            object aircraftOption,
            AircraftDefinition definition,
            string aircraftLabel,
            Loadout loadout,
            WeaponMount cargoMount,
            Airbase airbase,
            float totalCost,
            float distanceMeters,
            bool inStock,
            float ammoCapacityKg)
        {
            AircraftOption = aircraftOption;
            Definition = definition;
            AircraftLabel = aircraftLabel;
            Loadout = loadout;
            CargoMount = cargoMount;
            Airbase = airbase;
            TotalCost = totalCost;
            DistanceMeters = distanceMeters;
            InStock = inStock;
            AmmoCapacityKg = ammoCapacityKg;
        }
    }

    internal static AiSupplyLogisticsService Instance { get; } = new();
    internal static bool SuppressPlayerNotice { get; private set; }

    private readonly Dictionary<(FactionHQ Hq, int CellX, int CellZ), float> stationHeliCooldownUntil = new();
    private readonly Dictionary<Unit, GlobalPosition> activeTruckDestinations = new();
    private readonly HashSet<Unit> restockingTrucks = new();
    private readonly List<Unit> storeScratch = new();
    private readonly List<Unit> truckScratch = new();
    private readonly List<Unit> staleTrucksScratch = new();
    private readonly List<AmmoNeed> needsScratch = new();
    private readonly HashSet<Unit> seenUnitsScratch = new();
    private readonly HashSet<Unit> reservedTrucksScratch = new();
    private readonly List<(FactionHQ Hq, int CellX, int CellZ)> staleCooldownKeysScratch = new();
    private FactionHQ? lastRefreshedOptionsHq;
    private float nextTickAt;
    private float nextCooldownPruneAt;

    private AiSupplyLogisticsService()
    {
    }

    internal bool IsTruckTasked(Unit? truck) => truck != null && activeTruckDestinations.ContainsKey(truck);

    internal void CopyTaskedTrucksInto(HashSet<Unit> rearmRuns)
    {
        if (rearmRuns == null || activeTruckDestinations.Count == 0)
        {
            return;
        }

        foreach (Unit truck in activeTruckDestinations.Keys)
        {
            if (truck != null && !truck.disabled)
            {
                rearmRuns.Add(truck);
            }
        }
    }

    public void ResetSession()
    {
        stationHeliCooldownUntil.Clear();
        activeTruckDestinations.Clear();
        restockingTrucks.Clear();
        storeScratch.Clear();
        truckScratch.Clear();
        staleTrucksScratch.Clear();
        needsScratch.Clear();
        seenUnitsScratch.Clear();
        reservedTrucksScratch.Clear();
        staleCooldownKeysScratch.Clear();
        lastRefreshedOptionsHq = null;
        SuppressPlayerNotice = false;
        nextTickAt = 0f;
        nextCooldownPruneAt = 0f;
    }

    public void Tick()
    {
        if (!AddonConfig.ModEnabled.Value || !AddonConfig.EnableAiSupplyLogistics.Value)
        {
            return;
        }

        NetworkManagerNuclearOption? net = NetworkManagerNuclearOption.i;
        if (net == null || net.Server == null || !net.Server.Active)
        {
            return;
        }

        float now = Time.time;
        if (now < nextTickAt)
        {
            return;
        }

        nextTickAt = now + TickIntervalSeconds;

        if (now >= nextCooldownPruneAt)
        {
            nextCooldownPruneAt = now + CooldownPruneIntervalSeconds;
            PruneExpiredHeliCooldowns(now);
        }

        object? enemyService = GroundControlReflection.GetEnemyInstance();
        if (enemyService == null)
        {
            return;
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
        {
            if (hq != null && GroundControlReflection.IsAiFaction(hq, localHq))
            {
                ProcessAiFaction(hq, enemyService);
            }
        }
    }

    private void PruneExpiredHeliCooldowns(float now)
    {
        if (stationHeliCooldownUntil.Count == 0)
        {
            return;
        }

        staleCooldownKeysScratch.Clear();
        foreach (KeyValuePair<(FactionHQ Hq, int CellX, int CellZ), float> kv in stationHeliCooldownUntil)
        {
            if (kv.Key.Hq == null || now >= kv.Value)
            {
                staleCooldownKeysScratch.Add(kv.Key);
            }
        }

        for (int i = 0; i < staleCooldownKeysScratch.Count; i++)
        {
            stationHeliCooldownUntil.Remove(staleCooldownKeysScratch[i]);
        }

        staleCooldownKeysScratch.Clear();
    }

    private void ProcessAiFaction(FactionHQ hq, object enemyService)
    {
        object? state = TryGetEnemyState(enemyService, hq);
        Unit? strikeTruck = state != null ? GroundControlReflection.StateStrikeTruckProp?.GetValue(state) as Unit : null;

        PruneTruckStateForHq(hq, strikeTruck);
        CollectRearmTrucksAndStores(hq, strikeTruck);
        CollectAmmoNeeds(hq, enemyService);
        reservedTrucksScratch.Clear();

        if (needsScratch.Count > 0)
        {
            needsScratch.Sort((a, b) =>
            {
                int samCmp = b.IsSam.CompareTo(a.IsSam);
                return samCmp != 0 ? samCmp : a.AmmoFraction.CompareTo(b.AmmoFraction);
            });

            for (int i = 0; i < needsScratch.Count; i++)
            {
                ResolveNeed(hq, state, needsScratch[i]);
            }
        }

        RestockIdleTrucks(hq, enemyService);
    }

    private void PruneTruckStateForHq(FactionHQ hq, Unit? strikeTruck)
    {
        staleTrucksScratch.Clear();
        foreach (Unit truck in activeTruckDestinations.Keys)
        {
            if (truck == null || truck.disabled || truck.NetworkHQ == hq || ReferenceEquals(truck, strikeTruck))
            {
                staleTrucksScratch.Add(truck!);
            }
        }

        for (int i = 0; i < staleTrucksScratch.Count; i++)
        {
            activeTruckDestinations.Remove(staleTrucksScratch[i]);
        }

        staleTrucksScratch.Clear();
        foreach (Unit truck in restockingTrucks)
        {
            if (truck == null
                || truck.disabled
                || ReferenceEquals(truck, strikeTruck)
                || (truck.NetworkHQ == hq && GroundControlReflection.GetSupplyAmmoFraction(truck) >= IdleTruckRestockStopThreshold))
            {
                staleTrucksScratch.Add(truck!);
            }
        }

        for (int i = 0; i < staleTrucksScratch.Count; i++)
        {
            restockingTrucks.Remove(staleTrucksScratch[i]);
        }

        staleTrucksScratch.Clear();
    }

    private void CollectRearmTrucksAndStores(FactionHQ hq, Unit? strikeTruck)
    {
        truckScratch.Clear();
        GroundControlReflection.CollectSupplyStores(hq, storeScratch);

        for (int i = 0; i < storeScratch.Count; i++)
        {
            Unit store = storeScratch[i];
            if (store != null
                && !store.disabled
                && store is GroundVehicle
                && !ReferenceEquals(store, strikeTruck)
                && GroundControlReflection.EnemyIsRearmVehicle(store))
            {
                truckScratch.Add(store);
            }
        }
    }

    private void CollectAmmoNeeds(FactionHQ hq, object enemyService)
    {
        needsScratch.Clear();
        seenUnitsScratch.Clear();

        object? state = TryGetEnemyState(enemyService, hq);
        if (state != null && GroundControlReflection.StateDefendersProp?.GetValue(state) is Dictionary<Unit, int> defenders)
        {
            foreach (Unit unit in defenders.Keys)
            {
                TryAddAmmoNeed(unit, isArtillery: false);
            }
        }

        if (state != null && GroundControlReflection.StateBatteriesProp?.GetValue(state) is IDictionary batteries)
        {
            foreach (DictionaryEntry kv in batteries)
            {
                if (kv.Key is Unit battery)
                {
                    TryAddAmmoNeed(battery, isArtillery: true);
                }
            }
        }

        if (hq.factionUnits == null)
        {
            return;
        }

        foreach (PersistentID pid in hq.factionUnits)
        {
            if (pid.TryGetUnit(out Unit unit) && unit is GroundVehicle && AiAirDefenceHelper.IsStationSam(unit))
            {
                TryAddAmmoNeed(unit, isArtillery: false);
            }
        }
    }

    private void TryAddAmmoNeed(Unit? unit, bool isArtillery)
    {
        if (unit == null
            || unit.disabled
            || AiOperationalManeuverService.Instance.IsRetreatingOrOnQrf(unit)
            || !seenUnitsScratch.Add(unit))
        {
            return;
        }

        float ammo = GroundControlReflection.GetUnitAmmo(unit);
        if (ammo < 0f)
        {
            return;
        }

        bool isSam = AiAirDefenceHelper.IsStationSam(unit);
        float threshold = isSam
            ? AddonConfig.AiSamRearmThreshold.Value
            : (isArtillery ? ArtilleryRearmThreshold : AddonConfig.AiDefenderRearmThreshold.Value);

        if (ammo < threshold)
        {
            needsScratch.Add(new AmmoNeed(unit, isSam, ammo));
        }
    }

    private void ResolveNeed(FactionHQ hq, object? state, AmmoNeed need)
    {
        GlobalPosition targetPos = need.Unit.GlobalPosition();

        if (TryHandleNearbyRearmer(need.Unit, targetPos))
        {
            return;
        }

        Unit? bestTruck = FindBestAvailableTruck(need.IsSam, targetPos, out float bestTruckDist);
        bool criticalSam = need.IsSam && need.AmmoFraction <= CriticalSamAmmoFraction;
        bool truckPreferred = bestTruck != null
            && bestTruckDist <= AddonConfig.AiTruckPreferredRangeMeters.Value
            && (!criticalSam || bestTruckDist <= CriticalSamTruckCutoffMeters);

        if (truckPreferred && bestTruck != null)
        {
            DispatchTruck(bestTruck, targetPos, bestTruckDist);
            return;
        }

        bool allowHeli = need.IsSam
            || (AiAirDefenceHelper.IsStationCramUnit(need.Unit, null) && need.AmmoFraction <= DryCramHeliAmmoThreshold);
        if (allowHeli && AddonConfig.AiHeliSupplyEnabled.Value)
        {
            GlobalPosition stationAnchor = GetStationAnchor(state, need.Unit, targetPos);
            if (TryDispatchSupplyHeli(hq, stationAnchor, need.Unit))
            {
                return;
            }
        }

        if (bestTruck != null)
        {
            DispatchTruck(bestTruck, targetPos, bestTruckDist);
        }
    }

    private bool TryHandleNearbyRearmer(Unit needyUnit, GlobalPosition targetPos)
    {
        bool coveredByLoadedStore = false;

        for (int i = 0; i < storeScratch.Count; i++)
        {
            Unit store = storeScratch[i];
            if (store == null || store.disabled || store == needyUnit)
            {
                continue;
            }

            float ammoFrac = GroundControlReflection.GetSupplyAmmoFraction(store);
            if (ammoFrac <= 0.05f)
            {
                continue;
            }

            float dist = FastMath.Distance(store.GlobalPosition(), targetPos);
            if (dist <= LocalRearmAssistMeters)
            {
                Rearmer? rearmer = GroundControlReflection.GetRearmer(store);
                if (rearmer != null)
                {
                    GroundControlReflection.TopUpWeapons(rearmer, needyUnit);
                }
            }

            if (dist <= LocalRearmCoverageMeters && ammoFrac >= ActiveTruckMinAmmoFraction)
            {
                coveredByLoadedStore = true;
                if (GroundControlReflection.EnemyIsRearmVehicle(store))
                {
                    reservedTrucksScratch.Add(store);
                    restockingTrucks.Remove(store);
                    activeTruckDestinations[store] = targetPos;
                }
            }
        }

        return coveredByLoadedStore;
    }

    private Unit? FindBestAvailableTruck(bool isSam, GlobalPosition targetPos, out float bestDistance)
    {
        Unit? bestTruck = null;
        bestDistance = float.MaxValue;
        float maxRange = AddonConfig.AiTruckMaxRangeMeters.Value;

        for (int i = 0; i < truckScratch.Count; i++)
        {
            Unit truck = truckScratch[i];
            if (truck == null || truck.disabled || reservedTrucksScratch.Contains(truck))
            {
                continue;
            }

            float ammoFrac = GroundControlReflection.GetSupplyAmmoFraction(truck);
            if (ammoFrac < ActiveTruckMinAmmoFraction)
            {
                continue;
            }

            // Let restocking trucks finish filling to 85% unless pulled urgently for a SAM while >= 25% loaded.
            if (restockingTrucks.Contains(truck) && (!isSam || ammoFrac < CriticalSamAmmoFraction))
            {
                continue;
            }

            GlobalPosition truckPos = truck.GlobalPosition();
            float dist = FastMath.Distance(truckPos, targetPos);
            if (dist > maxRange || dist >= bestDistance)
            {
                continue;
            }

            if (!GroundControlReflection.CanDriveBetween(truckPos, targetPos))
            {
                continue;
            }

            bestTruck = truck;
            bestDistance = dist;
        }

        return bestTruck;
    }

    private void DispatchTruck(Unit truck, GlobalPosition targetPos, float currentDistance)
    {
        reservedTrucksScratch.Add(truck);
        restockingTrucks.Remove(truck);
        activeTruckDestinations[truck] = targetPos;

        if (currentDistance > TruckArriveRangeMeters)
        {
            GroundControlReflection.TrySetDestination(truck, targetPos);
        }
    }

    private void RestockIdleTrucks(FactionHQ hq, object enemyService)
    {
        HashSet<Unit>? vanillaRearmRuns = GroundControlReflection.EnemyRearmRunsField?.GetValue(enemyService) as HashSet<Unit>;

        for (int i = 0; i < truckScratch.Count; i++)
        {
            Unit truck = truckScratch[i];
            if (truck == null
                || truck.disabled
                || reservedTrucksScratch.Contains(truck)
                || (vanillaRearmRuns != null && vanillaRearmRuns.Contains(truck)))
            {
                continue;
            }

            float ammoFrac = GroundControlReflection.GetSupplyAmmoFraction(truck);
            bool alreadyRestocking = restockingTrucks.Contains(truck);
            if (ammoFrac >= IdleTruckRestockStopThreshold || (!alreadyRestocking && ammoFrac >= IdleTruckRestockThreshold))
            {
                restockingTrucks.Remove(truck);
                continue;
            }

            Unit? source = FindBestRestockSource(truck);
            if (source == null)
            {
                restockingTrucks.Remove(truck);
                continue;
            }

            GlobalPosition sourcePos = source.GlobalPosition();
            restockingTrucks.Add(truck);
            activeTruckDestinations[truck] = sourcePos;

            float dist = FastMath.Distance(truck.GlobalPosition(), sourcePos);
            if (dist <= StoreRefillRangeMeters)
            {
                GroundControlReflection.RefillTruck(truck, source);
            }
            else
            {
                GroundControlReflection.TrySetDestination(truck, sourcePos);
            }
        }
    }

    private Unit? FindBestRestockSource(Unit truck)
    {
        GlobalPosition truckPos = truck.GlobalPosition();
        Unit? bestSource = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < storeScratch.Count; i++)
        {
            Unit store = storeScratch[i];
            if (store == null
                || store.disabled
                || store == truck
                || GroundControlReflection.EnemyIsRearmVehicle(store)
                || GroundControlReflection.IsSupplyDump(store))
            {
                continue;
            }

            if (GroundControlReflection.GetSupplyAmmoFraction(store) <= MinStoreAmmoFraction)
            {
                continue;
            }

            GlobalPosition storePos = store.GlobalPosition();
            float dist = FastMath.Distance(truckPos, storePos);
            if (dist >= bestDist || !GroundControlReflection.CanDriveBetween(truckPos, storePos))
            {
                continue;
            }

            bestDist = dist;
            bestSource = store;
        }

        return bestSource;
    }

    private bool TryDispatchSupplyHeli(FactionHQ hq, GlobalPosition stationPos, Unit needyUnit)
    {
        object? supplyHeli = GroundControlReflection.SupplyHeliInstanceProp?.GetValue(null);
        if (supplyHeli == null
            || GroundControlReflection.SupplyHeliRefreshOptionsMethod == null
            || GroundControlReflection.SupplyHeliSpawnCargoRunMethod == null
            || GroundControlReflection.NetworkRunAsMethod == null)
        {
            return false;
        }

        if (GroundControlReflection.SupplyHeliAwaitingTargetSelectionProp?.GetValue(supplyHeli) is bool awaiting && awaiting)
        {
            return false;
        }

        if (HasStaticAmmoCoverageNear(stationPos, HeliCoverageCheckMeters))
        {
            return false;
        }

        var cooldownKey = GetCooldownKey(hq, stationPos);
        if (stationHeliCooldownUntil.TryGetValue(cooldownKey, out float readyAt) && Time.time < readyAt)
        {
            return false;
        }

        if (IsHeliCapOrInboundBlocked(supplyHeli, hq, stationPos))
        {
            return false;
        }

        IList? currentOptions = GroundControlReflection.SupplyHeliAircraftOptionsField?.GetValue(supplyHeli) as IList;
        bool needsRefresh = currentOptions == null
            || currentOptions.Count == 0
            || !ReferenceEquals(lastRefreshedOptionsHq, hq);

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        int savedAircraftIndex = 0;
        Airbase? savedAirbase = null;
        string savedStatus = string.Empty;
        bool savedAirdrop = false;
        if (needsRefresh)
        {
            savedAircraftIndex = GroundControlReflection.SupplyHeliSelectedAircraftIndexProp?.GetValue(supplyHeli) is int idx ? idx : 0;
            savedAirbase = GroundControlReflection.SupplyHeliSelectedAirbaseProp?.GetValue(supplyHeli) as Airbase;
            savedStatus = GroundControlReflection.SupplyHeliStatusTextField?.GetValue(supplyHeli) as string ?? string.Empty;
            savedAirdrop = GroundControlReflection.SupplyHeliAirdropDeliveryField?.GetValue(supplyHeli) is bool ad && ad;
        }

        bool dispatched = false;
        SuppressPlayerNotice = true;
        try
        {
            GroundControlReflection.NetworkRunAs(hq, () =>
            {
                if (needsRefresh)
                {
                    GroundControlReflection.SupplyHeliRefreshOptionsMethod.Invoke(supplyHeli, null);
                    lastRefreshedOptionsHq = hq;
                }

                HeliDispatchCandidate? candidate = SelectBestHeliCandidate(supplyHeli, hq, stationPos);
                if (candidate == null)
                {
                    return;
                }

                if (!TryFindSafeSupplyLz(stationPos, out GlobalPosition lz, out bool useAirdrop))
                {
                    return;
                }

                string cargoLabel = GroundControlReflection.SupplyHeliGetCargoLabelMethod?.Invoke(
                    null,
                    new object?[] { candidate.CargoMount, string.Empty }) as string ?? "Munitions Container";

                GroundControlReflection.SupplyHeliSpawnCargoRunMethod.Invoke(
                    supplyHeli,
                    new object?[]
                    {
                        candidate.AircraftOption,
                        candidate.Loadout,
                        cargoLabel,
                        candidate.Airbase,
                        true,
                        100f,
                        useAirdrop,
                        string.Empty,
                        false,
                        lz
                    });

                stationHeliCooldownUntil[cooldownKey] = Time.time + AddonConfig.AiHeliSupplyCooldownSeconds.Value;
                dispatched = true;

                AddonLog.Info(
                    $"[AI Supply] Dispatched {candidate.AircraftLabel} ({(useAirdrop ? "AIRDROP" : "LAND")}) " +
                    $"for {hq.faction?.factionName} -> {needyUnit.unitName} ({cargoLabel}, {candidate.TotalCost:0.#}M).");
            });
        }
        finally
        {
            try
            {
                if (needsRefresh)
                {
                    if (localHq != null && !ReferenceEquals(localHq, hq))
                    {
                        GroundControlReflection.NetworkRunAs(localHq, () =>
                        {
                            GroundControlReflection.SupplyHeliRefreshOptionsMethod.Invoke(supplyHeli, null);
                            lastRefreshedOptionsHq = localHq;
                        });
                    }
                    else
                    {
                        GroundControlReflection.SupplyHeliRefreshOptionsMethod.Invoke(supplyHeli, null);
                    }

                    GroundControlReflection.SupplyHeliSelectAircraftMethod?.Invoke(supplyHeli, new object?[] { savedAircraftIndex });
                    GroundControlReflection.SupplyHeliSelectedAirbaseProp?.SetValue(supplyHeli, savedAirbase);
                    GroundControlReflection.SupplyHeliStatusTextField?.SetValue(supplyHeli, savedStatus);
                    GroundControlReflection.SupplyHeliAirdropDeliveryField?.SetValue(supplyHeli, savedAirdrop);
                }
            }
            catch (Exception ex)
            {
                AddonLog.Debug($"[AI Supply] Failed restoring bootstrapped SupplyHeli UI state: {ex.Message}");
            }
            finally
            {
                SuppressPlayerNotice = false;
            }
        }

        return dispatched;
    }

    private bool HasStaticAmmoCoverageNear(GlobalPosition pos, float radiusMeters)
    {
        for (int i = 0; i < storeScratch.Count; i++)
        {
            Unit store = storeScratch[i];
            if (store == null || store.disabled || GroundControlReflection.EnemyIsRearmVehicle(store))
            {
                continue;
            }

            if (GroundControlReflection.GetSupplyAmmoFraction(store) > ActiveTruckMinAmmoFraction
                && FastMath.Distance(store.GlobalPosition(), pos) <= radiusMeters)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHeliCapOrInboundBlocked(object supplyHeli, FactionHQ hq, GlobalPosition stationPos)
    {
        int activeCount = 0;
        int maxConcurrent = Mathf.Max(1, AddonConfig.AiMaxConcurrentSupplyHelis.Value);

        if (GroundControlReflection.SupplyHeliAssignedMissionsField?.GetValue(supplyHeli) is IDictionary assigned)
        {
            foreach (DictionaryEntry entry in assigned)
            {
                if (entry.Key is not Aircraft ac || ac == null || ac.disabled)
                {
                    continue;
                }

                object? mission = entry.Value;
                if (mission == null)
                {
                    continue;
                }

                FactionHQ? mHq = GroundControlReflection.CargoMissionHqProp?.GetValue(mission) as FactionHQ;
                bool active = GroundControlReflection.CargoMissionTargetOverrideProp?.GetValue(mission) is bool a && a;
                if (mHq != hq || !active)
                {
                    continue;
                }

                activeCount++;
                if (GroundControlReflection.CargoMissionTargetProp?.GetValue(mission) is GlobalPosition target
                    && FastMath.Distance(target, stationPos) <= InboundHeliDedupMeters)
                {
                    return true;
                }
            }
        }

        if (GroundControlReflection.SupplyHeliPendingSpawnsField?.GetValue(supplyHeli) is IList pending)
        {
            for (int i = 0; i < pending.Count; i++)
            {
                object? item = pending[i];
                if (item == null)
                {
                    continue;
                }

                FactionHQ? pHq = GroundControlReflection.PendingSpawnHqProp?.GetValue(item) as FactionHQ;
                if (pHq != hq)
                {
                    continue;
                }

                activeCount++;
                if (GroundControlReflection.PendingSpawnTargetProp?.GetValue(item) is GlobalPosition target
                    && FastMath.Distance(target, stationPos) <= InboundHeliDedupMeters)
                {
                    return true;
                }
            }
        }

        if (GroundControlReflection.SupplyHeliQueuedSpawnsField?.GetValue(supplyHeli) is IEnumerable queued)
        {
            foreach (object? item in queued)
            {
                if (item == null)
                {
                    continue;
                }

                FactionHQ? qHq = GroundControlReflection.QueuedSpawnHqProp?.GetValue(item) as FactionHQ;
                if (qHq != hq)
                {
                    continue;
                }

                activeCount++;
                if (GroundControlReflection.QueuedSpawnTargetProp?.GetValue(item) is GlobalPosition target
                    && FastMath.Distance(target, stationPos) <= InboundHeliDedupMeters)
                {
                    return true;
                }
            }
        }

        return activeCount >= maxConcurrent;
    }

    private static HeliDispatchCandidate? SelectBestHeliCandidate(object supplyHeli, FactionHQ hq, GlobalPosition stationPos)
    {
        if (GroundControlReflection.SupplyHeliAircraftOptionsField?.GetValue(supplyHeli) is not IList options
            || GroundControlReflection.SupplyHeliCreateEmptyLoadoutMethod == null
            || GroundControlReflection.SupplyHeliPlaceCargoMethod == null
            || GroundControlReflection.SupplyHeliIsAvailableAirbaseMethod == null)
        {
            return null;
        }

        float minReserve = Mathf.Max(0f, AddonConfig.AiHeliSupplyMinReserveFunds.Value);
        HeliDispatchCandidate? best = null;

        for (int i = 0; i < options.Count; i++)
        {
            object? option = options[i];
            if (option == null)
            {
                continue;
            }

            bool isRotary = GroundControlReflection.CargoAircraftRotaryProp?.GetValue(option) is bool rot && rot;
            AircraftDefinition? def = GroundControlReflection.CargoAircraftDefProp?.GetValue(option) as AircraftDefinition;
            HardpointSet[]? sets = GroundControlReflection.CargoAircraftHardpointSetsProp?.GetValue(option) as HardpointSet[];
            IList? slots = GroundControlReflection.CargoAircraftCargoSlotsProp?.GetValue(option) as IList;
            if (!isRotary || def == null || sets == null || slots == null)
            {
                continue;
            }

            string acLabel = GroundControlReflection.CargoAircraftLabelProp?.GetValue(option) as string ?? def.unitName ?? "Rotary";
            bool inStock = hq.GetUnitSupply(def) > 0;
            float airframeCost = inStock ? 0f : Mathf.Max(0f, GroundControlReflection.EcoPriceOf(def));

            foreach (Airbase airbase in hq.GetAirbases())
            {
                if (airbase == null
                    || airbase.disabled
                    || airbase.CurrentHQ != hq
                    || !(airbase.GetAvailableAircraft()?.Contains(def) ?? false))
                {
                    continue;
                }

                if (GroundControlReflection.SupplyHeliIsAvailableAirbaseMethod.Invoke(null, new object?[] { airbase, hq, def }) is not bool ready
                    || !ready)
                {
                    continue;
                }

                GlobalPosition abPos = (airbase.center != null ? airbase.center : airbase.transform).GlobalPosition();
                float dist = FastMath.Distance(abPos, stationPos);

                for (int s = 0; s < slots.Count; s++)
                {
                    object? slot = slots[s];
                    if (slot == null
                        || GroundControlReflection.CargoSlotHardpointIndexProp?.GetValue(slot) is not int hpIndex
                        || GroundControlReflection.CargoSlotMountsProp?.GetValue(slot) is not IList mounts)
                    {
                        continue;
                    }

                    for (int m = 0; m < mounts.Count; m++)
                    {
                        if (mounts[m] is not WeaponMount mount
                            || !WeaponChecker.MountAllowedHQ(mount, hq)
                            || !WeaponChecker.MountAllowedAirbase(mount, airbase)
                            || !GroundControlReflection.TryGetSupplyHeliAmmoCapacity(mount, out float capacityKg))
                        {
                            continue;
                        }

                        if (GroundControlReflection.SupplyHeliCreateEmptyLoadoutMethod.Invoke(null, new object?[] { sets.Length }) is not Loadout loadout)
                        {
                            continue;
                        }

                        GroundControlReflection.SupplyHeliPlaceCargoMethod.Invoke(null, new object?[] { loadout, sets, hpIndex, mount });

                        float cargoCost = GroundControlReflection.SupplyHeliGetCargoCostMethod?.Invoke(null, new object?[] { def, loadout }) is float cc
                            ? Mathf.Max(0f, cc)
                            : 0f;
                        float totalCost = airframeCost + cargoCost;
                        if (hq.factionFunds < totalCost + minReserve)
                        {
                            continue;
                        }

                        var candidate = new HeliDispatchCandidate(
                            option,
                            def,
                            acLabel,
                            loadout,
                            mount,
                            airbase,
                            totalCost,
                            dist,
                            inStock,
                            capacityKg);

                        if (IsBetterHeliCandidate(candidate, best))
                        {
                            best = candidate;
                        }
                    }
                }
            }
        }

        return best;
    }

    private static bool IsBetterHeliCandidate(HeliDispatchCandidate candidate, HeliDispatchCandidate? currentBest)
    {
        if (currentBest == null)
        {
            return true;
        }

        // Strongly prefer closer ready airbase; within 2.5 km prefer in-stock airframe and larger ammo load.
        if (candidate.DistanceMeters < currentBest.DistanceMeters - 2500f)
        {
            return true;
        }

        if (currentBest.DistanceMeters < candidate.DistanceMeters - 2500f)
        {
            return false;
        }

        if (candidate.InStock != currentBest.InStock)
        {
            return candidate.InStock;
        }

        if (!Mathf.Approximately(candidate.AmmoCapacityKg, currentBest.AmmoCapacityKg))
        {
            return candidate.AmmoCapacityKg > currentBest.AmmoCapacityKg;
        }

        return candidate.DistanceMeters < currentBest.DistanceMeters;
    }

    private static bool TryFindSafeSupplyLz(GlobalPosition stationPos, out GlobalPosition lz, out bool useAirdrop)
    {
        GlobalPosition bestPos = GroundControlReflection.SnapToTerrain(stationPos);
        float bestSlope = MeasureTerrainSlope(bestPos);
        bool foundDry = !GroundControlReflection.IsBelowSeaLevel(bestPos);

        for (int i = 0; i < 8; i++)
        {
            float angle = i * (Mathf.PI * 2f / 8f);
            var probe = new GlobalPosition(
                stationPos.x + Mathf.Cos(angle) * LzOffsetMeters,
                stationPos.y,
                stationPos.z + Mathf.Sin(angle) * LzOffsetMeters);
            GlobalPosition snapped = GroundControlReflection.SnapToTerrain(probe);
            if (GroundControlReflection.IsBelowSeaLevel(snapped))
            {
                continue;
            }

            float slope = MeasureTerrainSlope(snapped);
            if (!foundDry || slope < bestSlope)
            {
                bestPos = snapped;
                bestSlope = slope;
                foundDry = true;
            }
        }

        if (!foundDry)
        {
            lz = default;
            useAirdrop = false;
            return false;
        }

        lz = bestPos;
        useAirdrop = bestSlope > Mathf.Tan(MaxLandingSlopeRadians);
        return true;
    }

    private static float MeasureTerrainSlope(GlobalPosition center)
    {
        float maxRise = 0f;
        for (int i = 0; i < 4; i++)
        {
            float angle = i * (Mathf.PI * 0.5f);
            var edge = new GlobalPosition(
                center.x + Mathf.Cos(angle) * SlopeSampleRadiusMeters,
                center.y,
                center.z + Mathf.Sin(angle) * SlopeSampleRadiusMeters);
            GlobalPosition snapped = GroundControlReflection.SnapToTerrain(edge);
            maxRise = Mathf.Max(maxRise, Mathf.Abs(snapped.y - center.y));
        }

        return maxRise / SlopeSampleRadiusMeters;
    }

    private static GlobalPosition GetStationAnchor(object? state, Unit unit, GlobalPosition fallback)
    {
        if (state != null
            && GroundControlReflection.StateDefendersProp?.GetValue(state) is Dictionary<Unit, int> defenders
            && defenders.TryGetValue(unit, out int postIdx)
            && GroundControlReflection.StateDefencePostsProp?.GetValue(state) is List<GlobalPosition> posts
            && posts.Count > 0)
        {
            return posts[Math.Abs(postIdx) % posts.Count];
        }

        return fallback;
    }

    private static object? TryGetEnemyState(object enemyService, FactionHQ hq)
    {
        if (GroundControlReflection.EnemyStatesField?.GetValue(enemyService) is IDictionary states
            && states.Contains(hq))
        {
            return states[hq];
        }

        return null;
    }

    private static (FactionHQ Hq, int CellX, int CellZ) GetCooldownKey(FactionHQ hq, GlobalPosition pos)
    {
        int cx = Mathf.RoundToInt(pos.x / StationCooldownGridMeters);
        int cz = Mathf.RoundToInt(pos.z / StationCooldownGridMeters);
        return (hq, cx, cz);
    }
}

[HarmonyPatch]
internal static class AiSupplyNoticeFailGuardPatch
{
    private static bool Prepare() => GroundControlReflection.NoticeFailMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.NoticeFailMethod!;

    private static bool Prefix() => !AiSupplyLogisticsService.SuppressPlayerNotice;
}

[HarmonyPatch]
internal static class AiSupplyHeliStatusGuardPatch
{
    private static bool Prepare() => GroundControlReflection.SupplyHeliSetStatusMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.SupplyHeliSetStatusMethod!;

    private static bool Prefix() => !AiSupplyLogisticsService.SuppressPlayerNotice;
}

[HarmonyPatch]
internal static class AiSupplyPreserveTruckRunsPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyReviewSupportMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyReviewSupportMethod!;

    private static void Prefix(object __instance)
    {
        if (!AddonConfig.ModEnabled.Value || !AddonConfig.EnableAiSupplyLogistics.Value || __instance == null)
        {
            return;
        }

        if (GroundControlReflection.EnemyRearmRunsField?.GetValue(__instance) is HashSet<Unit> rearmRuns)
        {
            AiSupplyLogisticsService.Instance.CopyTaskedTrucksInto(rearmRuns);
        }
    }
}

[HarmonyPatch]
internal static class AiSupplyFindGuardTruckPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyFindGuardTruckMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyFindGuardTruckMethod!;

    private static void Prefix(HashSet<Unit> rearmRuns)
    {
        if (!AddonConfig.ModEnabled.Value || !AddonConfig.EnableAiSupplyLogistics.Value || rearmRuns == null)
        {
            return;
        }

        AiSupplyLogisticsService.Instance.CopyTaskedTrucksInto(rearmRuns);
    }
}

[HarmonyPatch]
internal static class AiSupplyPinToGuardPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyPinToMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyPinToMethod!;

    private static bool Prefix(Unit unit)
    {
        if (!AddonConfig.ModEnabled.Value || !AddonConfig.EnableAiSupplyLogistics.Value)
        {
            return true;
        }

        return !AiSupplyLogisticsService.Instance.IsTruckTasked(unit);
    }
}
