using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mirage;
using NuclearOption.Networking;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal sealed class AiOperationalManeuverService : IAddonTick, IAddonResetSession
{
    private const float TickIntervalSeconds = 3.0f;
    private const float RetreatMaxRatio = 0.35f;
    private const float RetreatHealCompleteCondition = 0.80f;
    private const float RetreatTimeoutSeconds = 90f;
    private const float ArtillerySalvoAmmoDropThreshold = 0.03f;
    private const float ArtilleryScootCooldownSeconds = 45f;
    private const float ArtilleryScootHoldWindowSeconds = 45f;
    private const float ArtilleryScootArriveRadiusMeters = 45f;
    private const float RepairThreatRadiusMeters = 4500f;
    private const float ArtilleryThreatRadiusMeters = 3500f;
    private const float QrfMaxGroundUnits = 5;
    private const float QrfSearchRadiusMeters = 9000f;
    private const float QrfNoContactReleaseSeconds = 25f;
    private const float QrfMaxDurationSeconds = 90f;
    private const float ReconRefreshIntervalSeconds = 60f;
    private const float ReconShiftThresholdMeters = 1500f;

    private sealed class RetreatEntry
    {
        internal FactionHQ Hq = null!;
        internal GlobalPosition Destination;
        internal Unit? TargetRepairUnit;
        internal float TimeoutAt;
        internal float LastOrderAt;
    }

    private sealed class QrfFactionState
    {
        internal GlobalPosition ActivePos;
        internal float DispatchedAt;
        internal float LastHostileSeenAt;
        internal bool Active;
    }

    private sealed class ReconRefreshState
    {
        internal GlobalPosition LastObjectivePos;
        internal float NextRefreshAt;
    }

    internal static AiOperationalManeuverService Instance { get; } = new();
    internal static bool IsIssuingManagedOrder { get; private set; }

    private static readonly MethodInfo? IsBallisticLauncherMethod = GroundControlReflection.EnemyCommanderType != null
        ? AccessTools.Method(
            GroundControlReflection.EnemyCommanderType,
            "IsBallisticLauncherType",
            new[] { typeof(VehicleDefinition) })
        : null;

    private static readonly Type? AreaFireType = AccessTools.TypeByName("GroundControlRts.CommanderAreaFire");
    private static readonly MethodInfo? AreaFireIsAreaFiringMethod = AreaFireType != null
        ? AccessTools.Method(AreaFireType, "IsAreaFiring", new[] { typeof(Unit) })
        : null;

    private static readonly FieldInfo? GroundVehicleCommandedDestField =
        AccessTools.Field(typeof(GroundVehicle), "commandedDestination");
    private static readonly FieldInfo? GroundVehicleHoldAtPlayerDestField =
        AccessTools.Field(typeof(GroundVehicle), "holdAtPlayerCommandedDestination");

    private readonly Dictionary<Unit, RetreatEntry> retreatingUnits = new();
    private readonly Dictionary<Unit, FactionHQ> qrfUnits = new();
    private readonly Dictionary<FactionHQ, QrfFactionState> qrfByFaction = new();
    private readonly Dictionary<Unit, float> lastArtilleryAmmo = new();
    private readonly Dictionary<Unit, float> artilleryCooldownUntil = new();
    private readonly Dictionary<Unit, float> artilleryScootUntil = new();
    private readonly Dictionary<Unit, GlobalPosition> artilleryScootDestinations = new();
    private readonly HashSet<Unit> artilleryNearThreatLatched = new();
    private readonly Dictionary<Unit, GlobalPosition> managedExpectedDestinations = new();
    private readonly Dictionary<Unit, bool> savedHoldAtDestination = new();
    private readonly Dictionary<Aircraft, (GlobalPosition AlertPos, float NextRetargetAt)> qrfAirRetargets = new();
    private readonly Dictionary<FactionHQ, ReconRefreshState> reconStates = new();

    private readonly List<Unit> pruneScratch = new(32);
    private readonly List<FactionHQ> pruneFactionScratch = new(8);
    private readonly List<RtsBuildingRecord> buildingScratch = new(16);
    private readonly List<GlobalPosition> repairDepotPosScratch = new(16);
    private readonly List<float> repairDepotPenaltyScratch = new(16);
    private readonly List<Unit> repairJackknifeScratch = new(8);
    private readonly List<float> repairJackknifePenaltyScratch = new(8);
    private readonly List<Unit> combatUnitScratch = new(64);
    private readonly List<(Unit Unit, float DistSq)> qrfCandidateScratch = new(32);
    private readonly List<Unit> batteryKeyScratch = new(16);
    private readonly List<Aircraft> pruneAirScratch = new(16);
    private readonly Dictionary<VehicleDefinition, (bool IsBuilder, bool IsSupply, bool IsSamLauncher, bool IsStaticEmplacement)> vdefRoleCache = new();

    private float nextTickAt;

    private AiOperationalManeuverService()
    {
    }

    public void ResetSession()
    {
        retreatingUnits.Clear();
        qrfUnits.Clear();
        qrfByFaction.Clear();
        lastArtilleryAmmo.Clear();
        artilleryCooldownUntil.Clear();
        artilleryScootUntil.Clear();
        artilleryScootDestinations.Clear();
        artilleryNearThreatLatched.Clear();
        managedExpectedDestinations.Clear();
        savedHoldAtDestination.Clear();
        qrfAirRetargets.Clear();
        reconStates.Clear();
        pruneScratch.Clear();
        pruneFactionScratch.Clear();
        buildingScratch.Clear();
        repairDepotPosScratch.Clear();
        repairDepotPenaltyScratch.Clear();
        repairJackknifeScratch.Clear();
        repairJackknifePenaltyScratch.Clear();
        combatUnitScratch.Clear();
        qrfCandidateScratch.Clear();
        batteryKeyScratch.Clear();
        pruneAirScratch.Clear();
        vdefRoleCache.Clear();
        IsIssuingManagedOrder = false;
        nextTickAt = 0f;
    }

    internal bool IsManagedExpectedDestination(Unit? unit, GlobalPosition spot)
    {
        if (unit == null || !managedExpectedDestinations.TryGetValue(unit, out GlobalPosition expected))
        {
            return false;
        }

        return FastMath.InRange(expected, spot, 8f);
    }

    internal bool ShouldRetargetAirForQrf(Aircraft ac, GlobalPosition alertPos)
    {
        if (ac == null)
        {
            return false;
        }

        float now = Time.time;
        if (qrfAirRetargets.TryGetValue(ac, out var prev)
            && now < prev.NextRetargetAt
            && FastMath.InRange(prev.AlertPos, alertPos, 2000f))
        {
            return false;
        }

        qrfAirRetargets[ac] = (alertPos, now + 15f);
        return true;
    }

    internal bool IsUnitManaged(Unit? unit)
    {
        if (unit == null || unit.disabled)
        {
            return false;
        }

        if (retreatingUnits.ContainsKey(unit) || qrfUnits.ContainsKey(unit))
        {
            return true;
        }

        return artilleryScootUntil.TryGetValue(unit, out float until) && Time.time < until;
    }

    internal bool IsRetreatingOrOnQrf(Unit? unit)
        => unit != null && !unit.disabled && (retreatingUnits.ContainsKey(unit) || qrfUnits.ContainsKey(unit));

    public void Tick()
    {
        if (!AddonConfig.ModEnabled.Value || !IsServerAuthorityActive())
        {
            return;
        }

        float now = Time.time;
        if (now < nextTickAt)
        {
            return;
        }

        nextTickAt = now + TickIntervalSeconds;

        PruneDeadEntries(now);

        object? enemyService = GroundControlReflection.GetEnemyInstance();
        object? captureService = GroundControlReflection.GetCaptureInstance();
        GroundControlReflection.NetworkRunAsHostTeam(() =>
        {
            FactionHQ? localHq = GroundControlReflection.GetLocalHq();
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (hq == null || !((NetworkBehaviour)hq).IsServer || !GroundControlReflection.IsSafeAiFaction(hq, localHq))
                {
                    continue;
                }

                FactionHQ? hostileHq = GroundControlReflection.FindPrimaryHostileHq(hq);
                object? cmdState = GroundControlReflection.TryGetCommanderState(enemyService, hq);

                if (AddonConfig.EnableAiUnitPreservation.Value)
                {
                    TickUnitPreservation(hq, cmdState, captureService, now);
                }

                if (AddonConfig.EnableAiArtilleryShootAndScoot.Value && cmdState != null)
                {
                    TickArtilleryShootAndScoot(hq, hostileHq, cmdState, now);
                }

                if (AddonConfig.EnableAiRearQrf.Value)
                {
                    TickRearQrf(hq, cmdState, captureService, now);
                }

                if (AddonConfig.EnableTacticalQualityAi.Value && cmdState != null)
                {
                    TickDynamicReconPosts(hq, hostileHq, cmdState, now);
                }
            }
        });
    }

    private static bool IsServerAuthorityActive()
    {
        NetworkManagerNuclearOption? net = NetworkManagerNuclearOption.i;
        return net != null && net.Server != null && net.Server.Active;
    }

    private void PruneDeadEntries(float now)
    {
        pruneScratch.Clear();
        foreach (KeyValuePair<Unit, RetreatEntry> kv in retreatingUnits)
        {
            if (kv.Key == null || kv.Key.disabled)
            {
                pruneScratch.Add(kv.Key!);
            }
        }

        for (int i = 0; i < pruneScratch.Count; i++)
        {
            retreatingUnits.Remove(pruneScratch[i]);
        }

        pruneScratch.Clear();
        foreach (KeyValuePair<Unit, FactionHQ> kv in qrfUnits)
        {
            if (kv.Key == null || kv.Key.disabled)
            {
                pruneScratch.Add(kv.Key!);
            }
        }

        for (int i = 0; i < pruneScratch.Count; i++)
        {
            qrfUnits.Remove(pruneScratch[i]);
        }

        pruneScratch.Clear();
        foreach (KeyValuePair<Unit, float> kv in artilleryScootUntil)
        {
            if (kv.Key == null || kv.Key.disabled || now >= kv.Value)
            {
                pruneScratch.Add(kv.Key!);
            }
        }

        for (int i = 0; i < pruneScratch.Count; i++)
        {
            artilleryScootUntil.Remove(pruneScratch[i]);
            artilleryScootDestinations.Remove(pruneScratch[i]);
        }

        pruneScratch.Clear();
        foreach (Unit u in lastArtilleryAmmo.Keys)
        {
            if (u == null || u.disabled)
            {
                pruneScratch.Add(u!);
            }
        }

        for (int i = 0; i < pruneScratch.Count; i++)
        {
            lastArtilleryAmmo.Remove(pruneScratch[i]);
            artilleryCooldownUntil.Remove(pruneScratch[i]);
            artilleryScootDestinations.Remove(pruneScratch[i]);
            artilleryNearThreatLatched.Remove(pruneScratch[i]);
        }

        pruneScratch.Clear();
        foreach (Unit u in artilleryNearThreatLatched)
        {
            if (u == null || u.disabled)
            {
                pruneScratch.Add(u!);
            }
        }

        for (int i = 0; i < pruneScratch.Count; i++)
        {
            artilleryNearThreatLatched.Remove(pruneScratch[i]);
        }

        pruneScratch.Clear();
        foreach (Unit u in managedExpectedDestinations.Keys)
        {
            if (u == null || u.disabled)
            {
                pruneScratch.Add(u!);
            }
        }

        for (int i = 0; i < pruneScratch.Count; i++)
        {
            managedExpectedDestinations.Remove(pruneScratch[i]);
        }

        pruneScratch.Clear();
        foreach (Unit u in savedHoldAtDestination.Keys)
        {
            if (u == null || u.disabled)
            {
                pruneScratch.Add(u!);
            }
        }

        for (int i = 0; i < pruneScratch.Count; i++)
        {
            savedHoldAtDestination.Remove(pruneScratch[i]);
        }

        pruneScratch.Clear();

        if (qrfAirRetargets.Count > 0)
        {
            pruneAirScratch.Clear();
            foreach (KeyValuePair<Aircraft, (GlobalPosition AlertPos, float NextRetargetAt)> kv in qrfAirRetargets)
            {
                if (kv.Key == null || kv.Key.disabled || now >= kv.Value.NextRetargetAt)
                {
                    pruneAirScratch.Add(kv.Key!);
                }
            }

            for (int i = 0; i < pruneAirScratch.Count; i++)
            {
                qrfAirRetargets.Remove(pruneAirScratch[i]);
            }

            pruneAirScratch.Clear();
        }

        if (qrfByFaction.Count > 0)
        {
            pruneFactionScratch.Clear();
            foreach (KeyValuePair<FactionHQ, QrfFactionState> kv in qrfByFaction)
            {
                if (kv.Key == null || (!kv.Value.Active && now - kv.Value.LastHostileSeenAt > 300f))
                {
                    pruneFactionScratch.Add(kv.Key!);
                }
            }

            for (int i = 0; i < pruneFactionScratch.Count; i++)
            {
                qrfByFaction.Remove(pruneFactionScratch[i]);
            }

            pruneFactionScratch.Clear();
        }

        if (reconStates.Count > 0)
        {
            pruneFactionScratch.Clear();
            foreach (KeyValuePair<FactionHQ, ReconRefreshState> kv in reconStates)
            {
                if (kv.Key == null)
                {
                    pruneFactionScratch.Add(kv.Key!);
                }
            }

            for (int i = 0; i < pruneFactionScratch.Count; i++)
            {
                reconStates.Remove(pruneFactionScratch[i]);
            }

            pruneFactionScratch.Clear();
        }
    }

    private void TickUnitPreservation(FactionHQ hq, object? cmdState, object? captureService, float now)
    {
        // First evaluate releasing existing retreating units for this HQ
        pruneScratch.Clear();
        foreach (KeyValuePair<Unit, RetreatEntry> kv in retreatingUnits)
        {
            if (!ReferenceEquals(kv.Value.Hq, hq))
            {
                continue;
            }

            Unit unit = kv.Key;
            RetreatEntry entry = kv.Value;
            float cond = GroundControlReflection.GetUnitCondition(unit);
            if (cond >= RetreatHealCompleteCondition || now >= entry.TimeoutAt)
            {
                pruneScratch.Add(unit);
                continue;
            }

            bool destinationShifted = false;
            if (entry.TargetRepairUnit != null)
            {
                GlobalPosition unitPos = unit.GlobalPosition();
                if (entry.TargetRepairUnit.disabled
                    || !GroundControlReflection.CanDriveBetween(unitPos, entry.TargetRepairUnit.GlobalPosition()))
                {
                    // Jackknife died or became unreachable; release now so this tick can re-route if another healer exists.
                    pruneScratch.Add(unit);
                    continue;
                }

                GlobalPosition liveSpot = OffsetAndValidateRepairSpot(unitPos, entry.TargetRepairUnit.GlobalPosition());
                if (!FastMath.InRange(entry.Destination, liveSpot, 35f))
                {
                    entry.Destination = liveSpot;
                    destinationShifted = true;
                }
            }

            if (destinationShifted || now >= entry.LastOrderAt + 12f)
            {
                IssueManagedPin(unit, entry.Destination);
                entry.LastOrderAt = now;
            }
        }

        for (int i = 0; i < pruneScratch.Count; i++)
        {
            Unit released = pruneScratch[i];
            retreatingUnits.Remove(released);
            IssueManagedRelease(released);
        }

        pruneScratch.Clear();

        if (hq.factionUnits == null)
        {
            return;
        }

        // Gather eligible combat vehicles and repair destinations
        combatUnitScratch.Clear();
        repairDepotPosScratch.Clear();
        repairJackknifeScratch.Clear();

        DynamicCapAndTechService.Instance.GetOwnedBuildings(hq, buildingScratch);
        for (int i = 0; i < buildingScratch.Count; i++)
        {
            RtsBuildingRecord rec = buildingScratch[i];
            if (rec.Spec.Kind == RtsBuildingKind.LogisticsCamp && rec.Unit != null && !rec.Unit.disabled)
            {
                repairDepotPosScratch.Add(rec.Unit.GlobalPosition());
            }
        }

        IDictionary? batteries = cmdState != null ? GroundControlReflection.StateBatteriesProp?.GetValue(cmdState) as IDictionary : null;

        foreach (PersistentID pid in hq.factionUnits)
        {
            if (!pid.TryGetUnit(out Unit unit) || unit == null || unit.disabled)
            {
                continue;
            }

            if (unit is not GroundVehicle || unit.definition is not VehicleDefinition vdef)
            {
                continue;
            }

            if (GroundControlReflection.IsRepairUnit(unit))
            {
                repairJackknifeScratch.Add(unit);
                continue;
            }

            if (vdef.vehicleType == VehicleType.ART || (batteries != null && batteries.Contains(unit)))
            {
                continue;
            }

            if (!vdefRoleCache.TryGetValue(vdef, out var roles))
            {
                roles = (
                    GroundControlReflection.IsBuilderVehicle(vdef),
                    GroundControlReflection.IsSupplyVehicle(vdef),
                    GroundControlReflection.EnemyIsSamSiteLauncher(vdef),
                    IsStaticEmplacementVehicle(vdef));
                vdefRoleCache[vdef] = roles;
            }

            if (vdef.value < 25f
                || roles.IsBuilder
                || roles.IsSupply
                || roles.IsSamLauncher
                || roles.IsStaticEmplacement)
            {
                continue;
            }

            combatUnitScratch.Add(unit);
        }

        if (combatUnitScratch.Count == 0 || (repairDepotPosScratch.Count == 0 && repairJackknifeScratch.Count == 0))
        {
            return;
        }

        // Precompute threat penalty once per repair spot per tick (avoids O(U * S * C) trackingDatabase scans).
        repairDepotPenaltyScratch.Clear();
        for (int i = 0; i < repairDepotPosScratch.Count; i++)
        {
            repairDepotPenaltyScratch.Add(ComputeRepairSpotThreatPenalty(hq, repairDepotPosScratch[i]));
        }

        repairJackknifePenaltyScratch.Clear();
        for (int i = 0; i < repairJackknifeScratch.Count; i++)
        {
            Unit jk = repairJackknifeScratch[i];
            repairJackknifePenaltyScratch.Add(jk != null && !jk.disabled ? ComputeRepairSpotThreatPenalty(hq, jk.GlobalPosition()) : float.MaxValue);
        }

        int currentRetreating = 0;
        foreach (RetreatEntry entry in retreatingUnits.Values)
        {
            if (ReferenceEquals(entry.Hq, hq))
            {
                currentRetreating++;
            }
        }

        int maxRetreating = Mathf.Max(1, Mathf.FloorToInt(combatUnitScratch.Count * RetreatMaxRatio));
        if (currentRetreating >= maxRetreating)
        {
            return;
        }

        float threshold = Mathf.Clamp(AddonConfig.AiRetreatHealthFraction.Value, 0.20f, 0.65f);
        IDictionary? defenders = cmdState != null ? GroundControlReflection.StateDefendersProp?.GetValue(cmdState) as IDictionary : null;
        IDictionary? drives = captureService != null ? GroundControlReflection.CaptureDrivesField?.GetValue(captureService) as IDictionary : null;

        for (int i = 0; i < combatUnitScratch.Count && currentRetreating < maxRetreating; i++)
        {
            Unit unit = combatUnitScratch[i];
            if (retreatingUnits.ContainsKey(unit))
            {
                continue;
            }

            float cond = GroundControlReflection.GetUnitCondition(unit);
            if (cond >= threshold || cond <= 0f)
            {
                continue;
            }

            GlobalPosition unitPos = unit.GlobalPosition();
            if (IsFinishingBaseCapture(hq, unitPos) || IsWithin90mOfAnyRepairSpot(unitPos))
            {
                continue;
            }

            if (!TryFindNearestRepairSpot(unitPos, out GlobalPosition repairSpot, out Unit? repairUnit))
            {
                continue;
            }

            // Detach from ground drives & guard posts so vanilla doesn't yank it back into combat
            RemoveUnitFromDrives(drives, unit, keepIfLaunched: false);
            defenders?.Remove(unit);
            qrfUnits.Remove(unit);

            retreatingUnits[unit] = new RetreatEntry
            {
                Hq = hq,
                Destination = repairSpot,
                TargetRepairUnit = repairUnit,
                TimeoutAt = now + RetreatTimeoutSeconds,
                LastOrderAt = now,
            };

            IssueManagedPin(unit, repairSpot);
            currentRetreating++;
        }
    }

    private bool IsWithin90mOfAnyRepairSpot(GlobalPosition fromPos)
    {
        for (int i = 0; i < repairDepotPosScratch.Count; i++)
        {
            if (FastMath.InRange(fromPos, repairDepotPosScratch[i], 90f))
            {
                return true;
            }
        }

        for (int i = 0; i < repairJackknifeScratch.Count; i++)
        {
            Unit jk = repairJackknifeScratch[i];
            if (jk != null && !jk.disabled && FastMath.InRange(fromPos, jk.GlobalPosition(), 90f))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryFindNearestRepairSpot(GlobalPosition fromPos, out GlobalPosition spot, out Unit? targetUnit)
    {
        spot = default;
        targetUnit = null;
        float bestEffectiveDist = float.MaxValue;
        bool found = false;

        for (int i = 0; i < repairDepotPosScratch.Count; i++)
        {
            GlobalPosition candidate = repairDepotPosScratch[i];
            float penalty = i < repairDepotPenaltyScratch.Count ? repairDepotPenaltyScratch[i] : 0f;
            float lowerBound = FastMath.Distance(fromPos, candidate) + penalty;
            if (lowerBound >= bestEffectiveDist || !GroundControlReflection.CanDriveBetween(fromPos, candidate))
            {
                continue;
            }

            bestEffectiveDist = lowerBound;
            spot = candidate;
            targetUnit = null;
            found = true;
        }

        for (int i = 0; i < repairJackknifeScratch.Count; i++)
        {
            Unit jackknife = repairJackknifeScratch[i];
            if (jackknife == null || jackknife.disabled)
            {
                continue;
            }

            GlobalPosition candidate = jackknife.GlobalPosition();
            float penalty = i < repairJackknifePenaltyScratch.Count ? repairJackknifePenaltyScratch[i] : 0f;
            float lowerBound = FastMath.Distance(fromPos, candidate) + penalty;
            if (lowerBound >= bestEffectiveDist || !GroundControlReflection.CanDriveBetween(fromPos, candidate))
            {
                continue;
            }

            bestEffectiveDist = lowerBound;
            spot = candidate;
            targetUnit = jackknife;
            found = true;
        }

        if (found)
        {
            spot = OffsetAndValidateRepairSpot(fromPos, spot);
        }

        return found;
    }

    private static GlobalPosition OffsetAndValidateRepairSpot(GlobalPosition fromPos, GlobalPosition candidate)
    {
        Vector3 backDir = fromPos.AsVector3() - candidate.AsVector3();
        backDir.y = 0f;
        if (backDir.sqrMagnitude > 1f)
        {
            backDir.Normalize();
            GlobalPosition offset = new GlobalPosition(
                candidate.x + backDir.x * 45f,
                candidate.y,
                candidate.z + backDir.z * 45f);
            GlobalPosition snapped = GroundControlReflection.SnapToTerrain(offset);
            if (!GroundControlReflection.IsBelowSeaLevel(snapped)
                && GroundControlReflection.CanDriveBetween(fromPos, snapped)
                && GroundControlReflection.TerrainRouteIsDrivable(fromPos, snapped))
            {
                return snapped;
            }
        }

        return candidate;
    }

    private static float ComputeRepairSpotThreatPenalty(FactionHQ hq, GlobalPosition candidate)
    {
        if (hq.trackingDatabase == null)
        {
            return 0f;
        }

        float penalty = 0f;
        float rSq = RepairThreatRadiusMeters * RepairThreatRadiusMeters;
        float levelNow = Time.timeSinceLevelLoad;

        foreach (KeyValuePair<PersistentID, TrackingInfo> kv in hq.trackingDatabase)
        {
            TrackingInfo info = kv.Value;
            if (info == null || !info.TryGetUnit(out Unit u) || u == null || u.disabled)
            {
                continue;
            }

            if (u.NetworkHQ == null || ReferenceEquals(u.NetworkHQ, hq) || u is Aircraft)
            {
                continue;
            }

            if (!(u is Building) && levelNow - info.lastSpottedTime > 90f)
            {
                continue;
            }

            GlobalPosition pos = info.GetPosition();
            float dSq = FastMath.SquareDistance(candidate, pos);
            if (dSq <= rSq)
            {
                float d = Mathf.Sqrt(dSq);
                penalty += (RepairThreatRadiusMeters - d) * 2.0f;
            }
        }

        return penalty;
    }

    private static bool IsFinishingBaseCapture(FactionHQ hq, GlobalPosition unitPos)
    {
        foreach (Airbase ab in FactionRegistry.airbaseLookup.Values)
        {
            if (ab == null || ab.disabled || ab.center == null || ReferenceEquals(ab.CurrentHQ, hq))
            {
                continue;
            }

            float radius = ab.SavedAirbase != null ? Mathf.Max(450f, ab.SavedAirbase.CaptureRange) : 450f;
            if (FastMath.InRange(unitPos, ab.center.GlobalPosition(), radius)
                && GroundControlReflection.GetOwnerGrip(ab) < 0.35f)
            {
                return true;
            }
        }

        return false;
    }

    private void TickArtilleryShootAndScoot(FactionHQ hq, FactionHQ? localHq, object cmdState, float now)
    {
        if (GroundControlReflection.StateBatteriesProp?.GetValue(cmdState) is not IDictionary batteries
            || batteries.Count == 0)
        {
            return;
        }

        IDictionary? batteryTargets = GroundControlReflection.StateBatteryTargetsProp?.GetValue(cmdState) as IDictionary;

        batteryKeyScratch.Clear();
        foreach (object? key in batteries.Keys)
        {
            if (key is Unit u && u != null && !u.disabled)
            {
                batteryKeyScratch.Add(u);
            }
        }

        for (int i = 0; i < batteryKeyScratch.Count; i++)
        {
            Unit gun = batteryKeyScratch[i];
            if (retreatingUnits.ContainsKey(gun))
            {
                continue;
            }

            if (IsBallisticOrAreaFiring(gun))
            {
                artilleryScootUntil.Remove(gun);
                artilleryScootDestinations.Remove(gun);
                artilleryNearThreatLatched.Remove(gun);
                continue;
            }

            GlobalPosition gunPos = gun.GlobalPosition();
            if (artilleryScootUntil.TryGetValue(gun, out float scootUntil)
                && now < scootUntil
                && artilleryScootDestinations.TryGetValue(gun, out GlobalPosition scootDest)
                && FastMath.InRange(gunPos, scootDest, ArtilleryScootArriveRadiusMeters))
            {
                artilleryScootUntil.Remove(gun);
                artilleryScootDestinations.Remove(gun);
            }

            float curAmmo = GroundControlReflection.GetUnitAmmo(gun);
            bool ammoDropped = false;
            if (curAmmo >= 0f)
            {
                if (lastArtilleryAmmo.TryGetValue(gun, out float prevAmmo))
                {
                    if (prevAmmo - curAmmo >= ArtillerySalvoAmmoDropThreshold)
                    {
                        ammoDropped = true;
                        lastArtilleryAmmo[gun] = curAmmo;
                    }
                    else if (curAmmo > prevAmmo + 0.05f)
                    {
                        lastArtilleryAmmo[gun] = curAmmo;
                    }
                }
                else
                {
                    lastArtilleryAmmo[gun] = curAmmo;
                }
            }

            bool nearThreat = HasTrackedCounterBatteryOrSurfaceThreat(hq, gunPos, ArtilleryThreatRadiusMeters);
            if (!nearThreat)
            {
                artilleryNearThreatLatched.Remove(gun);
            }

            bool shouldProximityScoot = nearThreat && !artilleryNearThreatLatched.Contains(gun);

            if (artilleryCooldownUntil.TryGetValue(gun, out float cdUntil) && now < cdUntil)
            {
                continue;
            }

            if (!ammoDropped && !shouldProximityScoot)
            {
                continue;
            }

            GlobalPosition targetPos = default;
            if (batteryTargets != null && batteryTargets.Contains(gun) && batteryTargets[gun] is Airbase abTarget && abTarget != null && abTarget.center != null)
            {
                targetPos = abTarget.center.GlobalPosition();
            }
            else
            {
                targetPos = AiBattlefieldIntelService.Instance.GetPrimaryObjectivePos(hq, localHq);
            }

            float maxRange = gun.GetMaxRange();
            if (TryFindArtilleryScootSpot(gunPos, targetPos, maxRange, out GlobalPosition scootSpot))
            {
                batteries[gun] = scootSpot;
                artilleryCooldownUntil[gun] = now + ArtilleryScootCooldownSeconds;
                artilleryScootUntil[gun] = now + ArtilleryScootHoldWindowSeconds;
                artilleryScootDestinations[gun] = scootSpot;
                if (nearThreat)
                {
                    artilleryNearThreatLatched.Add(gun);
                }

                IssueManagedHold(gun, scootSpot);
            }
        }

        batteryKeyScratch.Clear();
    }

    private static bool IsBallisticOrAreaFiring(Unit gun)
    {
        if (gun == null)
        {
            return true;
        }

        if (AreaFireIsAreaFiringMethod?.Invoke(null, new object?[] { gun }) is bool isAreaFiring && isAreaFiring)
        {
            return true;
        }

        if (gun.definition is VehicleDefinition vdef)
        {
            if (IsBallisticLauncherMethod != null
                && IsBallisticLauncherMethod.Invoke(null, new object?[] { vdef }) is bool isBallistic
                && isBallistic)
            {
                return true;
            }

            if (vdef.vehicleType == VehicleType.ART
                && !string.IsNullOrEmpty(vdef.unitName)
                && vdef.unitName.EndsWith("Ballistic Missile Launcher", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasTrackedCounterBatteryOrSurfaceThreat(FactionHQ aiHq, GlobalPosition gunPos, float radiusMeters)
    {
        if (aiHq.trackingDatabase == null)
        {
            return false;
        }

        float rSq = radiusMeters * radiusMeters;
        float levelNow = Time.timeSinceLevelLoad;

        foreach (KeyValuePair<PersistentID, TrackingInfo> kv in aiHq.trackingDatabase)
        {
            TrackingInfo info = kv.Value;
            if (info == null || !info.TryGetUnit(out Unit unit) || unit == null || unit.disabled)
            {
                continue;
            }

            if (unit.NetworkHQ == null || ReferenceEquals(unit.NetworkHQ, aiHq))
            {
                continue;
            }

            if (unit is Building)
            {
                if (FastMath.SquareDistance(gunPos, info.GetPosition()) <= rSq)
                {
                    return true;
                }
            }
            else if (unit is GroundVehicle || unit is Ship)
            {
                if (levelNow - info.lastSpottedTime <= 90f && FastMath.SquareDistance(gunPos, info.GetPosition()) <= rSq)
                {
                    return true;
                }

                if (AiBattlefieldIntelService.Instance.IsLocatedHostileArtillery(aiHq, unit)
                    && FastMath.SquareDistance(gunPos, info.GetPosition()) <= rSq)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryFindArtilleryScootSpot(
        GlobalPosition gunPos,
        GlobalPosition targetPos,
        float maxRange,
        out GlobalPosition scootSpot)
    {
        scootSpot = default;
        bool hasTarget = !targetPos.Equals(default);
        Vector3 awayDir = hasTarget ? (gunPos.AsVector3() - targetPos.AsVector3()) : Vector3.back;
        awayDir.y = 0f;
        if (awayDir.sqrMagnitude < 1f)
        {
            awayDir = Vector3.back;
        }
        else
        {
            awayDir.Normalize();
        }

        float baseAngle = Mathf.Atan2(awayDir.z, awayDir.x);
        float maxAllowedTargetDist = (hasTarget && maxRange > 500f) ? maxRange * 0.82f : float.MaxValue;

        // Lateral-rear offsets between 350m and 650m
        for (int attempt = 0; attempt < 12; attempt++)
        {
            float sign = (attempt % 2 == 0) ? 1f : -1f;
            float angleDeg = 45f + (attempt / 2) * 20f;
            float rad = baseAngle + sign * angleDeg * Mathf.Deg2Rad;
            float dist = 350f + ((attempt * 97) % 300);

            var raw = new GlobalPosition(
                gunPos.x + Mathf.Cos(rad) * dist,
                gunPos.y,
                gunPos.z + Mathf.Sin(rad) * dist);
            GlobalPosition snapped = GroundControlReflection.SnapToTerrain(raw);

            if (GroundControlReflection.IsBelowSeaLevel(snapped))
            {
                continue;
            }

            if (GroundControlReflection.BuildPreviewIsOnLevelRoad(snapped, 25f))
            {
                continue;
            }

            if (!GroundControlReflection.CanDriveBetween(gunPos, snapped)
                || !GroundControlReflection.TerrainRouteIsDrivable(gunPos, snapped))
            {
                continue;
            }

            if (hasTarget && maxRange > 500f && FastMath.Distance(snapped, targetPos) > maxAllowedTargetDist)
            {
                continue;
            }

            scootSpot = snapped;
            return true;
        }

        return false;
    }

    private void TickRearQrf(FactionHQ hq, object? cmdState, object? captureService, float now)
    {
        if (!qrfByFaction.TryGetValue(hq, out QrfFactionState? qrfState))
        {
            qrfState = new QrfFactionState();
            qrfByFaction[hq] = qrfState;
        }

        bool alertExists = AiBattlefieldIntelService.Instance.TryGetRearQrfAlert(hq, out GlobalPosition alertPos, out Unit? attacker);
        if (!alertExists)
        {
            if (qrfState.Active)
            {
                ReleaseQrfForFaction(hq, qrfState);
            }

            return;
        }

        bool isAirRaid = attacker is Aircraft;
        bool shoradOnly = false;
        if (isAirRaid)
        {
            // Only mobilize 1 nearby mobile SHORAD/SPAAG if an air raid hits an unprotected rear site (no friendly AD within 2.5 km).
            if (HasFriendlyAirDefenceNear(hq, alertPos, 2500f))
            {
                if (qrfState.Active)
                {
                    ReleaseQrfForFaction(hq, qrfState);
                }

                AiBattlefieldIntelService.Instance.ClearRearQrfAlert(hq);
                return;
            }

            shoradOnly = true;
        }

        bool hostileNearAlert = HasTrackedHostileNear(hq, alertPos, 3000f);
        if (!qrfState.Active && attacker == null && !hostileNearAlert)
        {
            return;
        }

        bool alertShifted = false;
        if (!qrfState.Active)
        {
            qrfState.Active = true;
            qrfState.ActivePos = alertPos;
            qrfState.DispatchedAt = now;
            qrfState.LastHostileSeenAt = now;
        }
        else
        {
            alertShifted = FastMath.Distance(qrfState.ActivePos, alertPos) > 400f;
            qrfState.ActivePos = alertPos;
            if (hostileNearAlert)
            {
                qrfState.LastHostileSeenAt = now;
            }
        }

        if (now - qrfState.LastHostileSeenAt >= QrfNoContactReleaseSeconds
            || now - qrfState.DispatchedAt >= QrfMaxDurationSeconds)
        {
            ReleaseQrfForFaction(hq, qrfState);
            AiBattlefieldIntelService.Instance.ClearRearQrfAlert(hq);
            return;
        }

        int currentQrfCount = 0;
        foreach (KeyValuePair<Unit, FactionHQ> kv in qrfUnits)
        {
            if (ReferenceEquals(kv.Value, hq) && kv.Key != null && !kv.Key.disabled)
            {
                currentQrfCount++;
                if (alertShifted)
                {
                    IssueManagedPin(kv.Key, alertPos);
                }
            }
        }

        int maxQrfCount = shoradOnly ? 1 : (int)QrfMaxGroundUnits;
        float maxSearchRadius = shoradOnly ? 5000f : QrfSearchRadiusMeters;
        if (currentQrfCount < maxQrfCount && hq.factionUnits != null)
        {
            qrfCandidateScratch.Clear();
            IDictionary? drives = captureService != null ? GroundControlReflection.CaptureDrivesField?.GetValue(captureService) as IDictionary : null;

            foreach (PersistentID pid in hq.factionUnits)
            {
                if (!pid.TryGetUnit(out Unit unit) || unit == null || unit.disabled || unit is not GroundVehicle)
                {
                    continue;
                }

                if (retreatingUnits.ContainsKey(unit) || qrfUnits.ContainsKey(unit))
                {
                    continue;
                }

                if (unit.definition is not VehicleDefinition vdef)
                {
                    continue;
                }

                if (shoradOnly)
                {
                    bool isMobileShorad = (vdef.vehicleType == VehicleType.AAA || vdef.vehicleType == VehicleType.IR_SAM || vdef.vehicleType == VehicleType.R_SAM)
                        && !GroundControlReflection.EnemyIsSamSiteLauncher(vdef)
                        && !IsStaticEmplacementVehicle(vdef);
                    if (!isMobileShorad)
                    {
                        continue;
                    }
                }
                else if (!GroundControlReflection.IsCombatVehicle(vdef)
                    || GroundControlReflection.IsAirDefence(vdef)
                    || vdef.vehicleType == VehicleType.ART
                    || GroundControlReflection.IsBuilderVehicle(vdef)
                    || GroundControlReflection.IsSupplyVehicle(vdef)
                    || IsStaticEmplacementVehicle(vdef))
                {
                    continue;
                }

                if (IsUnitInLaunchedDrive(drives, unit))
                {
                    continue;
                }

                GlobalPosition uPos = unit.GlobalPosition();
                if (!FastMath.InRange(uPos, alertPos, maxSearchRadius))
                {
                    continue;
                }

                if (!GroundControlReflection.CanDriveBetween(uPos, alertPos))
                {
                    continue;
                }

                qrfCandidateScratch.Add((unit, FastMath.SquareDistance(uPos, alertPos)));
            }

            qrfCandidateScratch.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));
            IDictionary? defenders = cmdState != null ? GroundControlReflection.StateDefendersProp?.GetValue(cmdState) as IDictionary : null;

            for (int i = 0; i < qrfCandidateScratch.Count && currentQrfCount < maxQrfCount; i++)
            {
                Unit responder = qrfCandidateScratch[i].Unit;
                RemoveUnitFromDrives(drives, responder, keepIfLaunched: true);
                defenders?.Remove(responder);
                qrfUnits[responder] = hq;
                IssueManagedPin(responder, alertPos);
                currentQrfCount++;
            }

            qrfCandidateScratch.Clear();
        }

        // Also divert free AI gunship/escort helicopters onto rear surface threat
        if (!shoradOnly && hq.factionUnits != null)
        {
            foreach (PersistentID pid in hq.factionUnits)
            {
                if (!pid.TryGetUnit(out Unit unit) || unit is not Aircraft ac || ac.disabled || ac.Player != null)
                {
                    continue;
                }

                if (!GroundControlReflection.AirHasRotaryPilot(ac)
                    || IsCargoConfigured(ac)
                    || !CanSafelyDivertToRearQrf(ac)
                    || !ShouldRetargetAirForQrf(ac, alertPos))
                {
                    continue;
                }

                GroundControlReflection.AirClearAiRoute(ac);
                GroundControlReflection.AirClearAiForcedTarget(ac);
                GroundControlReflection.AirSetAiCommitted(ac, false);
                // AirCommandMode.Cas = 1
                GroundControlReflection.AirRetargetAiMission(ac, 1, alertPos, 4500f);
            }
        }
    }

    internal static bool HasFriendlyAirDefenceNear(FactionHQ hq, GlobalPosition center, float radiusMeters)
    {
        if (hq.factionUnits == null)
        {
            return false;
        }

        foreach (PersistentID pid in hq.factionUnits)
        {
            if (!pid.TryGetUnit(out Unit unit) || unit == null || unit.disabled)
            {
                continue;
            }

            bool isAd = (unit is GroundVehicle && unit.definition is VehicleDefinition vd && GroundControlReflection.IsAirDefence(vd))
                || (unit is Building && unit.definition != null && unit.definition.roleIdentity.antiAir > 0f);
            if (isAd && FastMath.InRange(unit.GlobalPosition(), center, radiusMeters))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool CanSafelyDivertToRearQrf(Aircraft aircraft)
    {
        if (aircraft == null
            || aircraft.disabled
            || GroundControlReflection.AirIsMissionReturning(aircraft)
            || GroundControlReflection.AirGetMissionLandingBase(aircraft) != null
            || GroundControlReflection.AirGetMissionModeInt(aircraft) == GroundControlReflection.AirCommandModeAradValue
            || !GroundControlReflection.AirHasCombatAmmo(aircraft))
        {
            return false;
        }

        Unit? forcedTarget = GroundControlReflection.AirGetForcedTarget(aircraft);
        return forcedTarget == null || forcedTarget.disabled;
    }

    private void ReleaseQrfForFaction(FactionHQ hq, QrfFactionState qrfState)
    {
        qrfState.Active = false;
        pruneScratch.Clear();
        foreach (KeyValuePair<Unit, FactionHQ> kv in qrfUnits)
        {
            if (ReferenceEquals(kv.Value, hq))
            {
                pruneScratch.Add(kv.Key);
            }
        }

        for (int i = 0; i < pruneScratch.Count; i++)
        {
            Unit u = pruneScratch[i];
            qrfUnits.Remove(u);
            IssueManagedRelease(u);
        }

        pruneScratch.Clear();
    }

    private void TickDynamicReconPosts(FactionHQ hq, FactionHQ? localHq, object cmdState, float now)
    {
        if (GroundControlReflection.StateReconPostsProp?.GetValue(cmdState) is not List<GlobalPosition> reconPosts)
        {
            return;
        }

        if (!reconStates.TryGetValue(hq, out ReconRefreshState? state))
        {
            state = new ReconRefreshState();
            reconStates[hq] = state;
        }

        GlobalPosition objectivePos = AiBattlefieldIntelService.Instance.GetPrimaryObjectivePos(hq, localHq);
        if (objectivePos.Equals(default))
        {
            return;
        }

        bool objectiveShifted = state.LastObjectivePos.Equals(default)
            || FastMath.Distance(state.LastObjectivePos, objectivePos) > ReconShiftThresholdMeters;
        if (!objectiveShifted && now < state.NextRefreshAt)
        {
            return;
        }

        state.LastObjectivePos = objectivePos;
        state.NextRefreshAt = now + ReconRefreshIntervalSeconds;

        GlobalPosition ownCenter = GroundControlReflection.CaptureGetTerritoryCenter(hq);
        Vector3 toOwn = ownCenter.AsVector3() - objectivePos.AsVector3();
        toOwn.y = 0f;
        if (toOwn.sqrMagnitude < 1f)
        {
            return;
        }

        toOwn.Normalize();
        reconPosts.Clear();

        float[] fanAngles = { -42f, 0f, 42f };
        for (int i = 0; i < fanAngles.Length; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, fanAngles[i], 0f) * toOwn;
            float standoff = i == 1 ? 8500f : 11000f;
            var raw = new GlobalPosition(
                objectivePos.x + dir.x * standoff,
                objectivePos.y,
                objectivePos.z + dir.z * standoff);
            GlobalPosition high = GroundControlReflection.EnemyFindHighGround(raw);
            if (!GroundControlReflection.IsBelowSeaLevel(high)
                && GroundControlReflection.CanDriveBetween(ownCenter, high)
                && GroundControlReflection.TerrainRouteIsDrivable(ownCenter, high))
            {
                reconPosts.Add(high);
            }
        }
    }

    internal static bool HasTrackedHostileNear(FactionHQ aiHq, GlobalPosition center, float radiusMeters)
    {
        if (aiHq.trackingDatabase == null)
        {
            return false;
        }

        float rSq = radiusMeters * radiusMeters;
        float levelNow = Time.timeSinceLevelLoad;

        foreach (KeyValuePair<PersistentID, TrackingInfo> kv in aiHq.trackingDatabase)
        {
            TrackingInfo info = kv.Value;
            if (info == null || !info.TryGetUnit(out Unit u) || u == null || u.disabled)
            {
                continue;
            }

            if (u.NetworkHQ == null || ReferenceEquals(u.NetworkHQ, aiHq))
            {
                continue;
            }

            if (!(u is Building) && levelNow - info.lastSpottedTime > 60f)
            {
                continue;
            }

            if (FastMath.SquareDistance(center, info.GetPosition()) <= rSq)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnitInLaunchedDrive(IDictionary? drives, Unit unit)
    {
        if (drives == null)
        {
            return false;
        }

        foreach (DictionaryEntry entry in drives)
        {
            object? drive = entry.Value;
            if (drive != null
                && GroundControlReflection.DriveLaunchedProp?.GetValue(drive) is HashSet<Unit> launched
                && launched.Contains(unit))
            {
                return true;
            }
        }

        return false;
    }

    private static void RemoveUnitFromDrives(IDictionary? drives, Unit unit, bool keepIfLaunched)
    {
        if (drives == null)
        {
            return;
        }

        foreach (DictionaryEntry entry in drives)
        {
            object? drive = entry.Value;
            if (drive == null)
            {
                continue;
            }

            if (keepIfLaunched
                && GroundControlReflection.DriveLaunchedProp?.GetValue(drive) is HashSet<Unit> launchedCheck
                && launchedCheck.Contains(unit))
            {
                continue;
            }

            if (GroundControlReflection.DriveCommittedProp?.GetValue(drive) is List<Unit> committed)
            {
                committed.Remove(unit);
            }

            if (GroundControlReflection.DriveLaunchedProp?.GetValue(drive) is HashSet<Unit> launched)
            {
                launched.Remove(unit);
            }

            if (GroundControlReflection.DriveMarchingProp?.GetValue(drive) is HashSet<Unit> marching)
            {
                marching.Remove(unit);
            }
        }
    }

    private static bool IsStaticEmplacementVehicle(VehicleDefinition vdef)
    {
        if (vdef == null)
        {
            return true;
        }

        string? name = vdef.unitName;
        if (!string.IsNullOrEmpty(name)
            && string.Equals(name, "MSV CRAM", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsCargoConfigured(Aircraft aircraft)
    {
        List<WeaponStation>? stations = ((Unit)aircraft).weaponStations;
        if (stations == null)
        {
            return false;
        }

        for (int i = 0; i < stations.Count; i++)
        {
            WeaponStation? st = stations[i];
            if (st != null && st.Cargo)
            {
                return true;
            }
        }

        return false;
    }

    private void SaveOriginalHoldFlagIfNeeded(Unit? unit)
    {
        if (unit is GroundVehicle gv
            && GroundVehicleHoldAtPlayerDestField != null
            && !savedHoldAtDestination.ContainsKey(unit)
            && GroundVehicleHoldAtPlayerDestField.GetValue(gv) is bool origHold)
        {
            savedHoldAtDestination[unit] = origHold;
        }
    }

    private void IssueManagedPin(Unit unit, GlobalPosition spot)
    {
        if (unit != null)
        {
            SaveOriginalHoldFlagIfNeeded(unit);
            managedExpectedDestinations[unit] = spot;
        }

        bool prev = IsIssuingManagedOrder;
        IsIssuingManagedOrder = true;
        try
        {
            GroundControlReflection.EnemyPinTo(unit, spot);
        }
        finally
        {
            IsIssuingManagedOrder = prev;
        }
    }

    private void IssueManagedHold(Unit unit, GlobalPosition spot)
    {
        if (unit != null)
        {
            SaveOriginalHoldFlagIfNeeded(unit);
            managedExpectedDestinations[unit] = spot;
        }

        bool prev = IsIssuingManagedOrder;
        IsIssuingManagedOrder = true;
        try
        {
            GroundControlReflection.EnemyHoldAt(unit, spot);
        }
        finally
        {
            IsIssuingManagedOrder = prev;
        }
    }

    private void IssueManagedRelease(Unit unit)
    {
        if (unit != null)
        {
            managedExpectedDestinations.Remove(unit);
        }

        bool prev = IsIssuingManagedOrder;
        IsIssuingManagedOrder = true;
        try
        {
            GroundControlReflection.EnemyReleaseCommandedDestination(unit);
            if (unit is GroundVehicle gv)
            {
                GroundVehicleCommandedDestField?.SetValue(gv, false);
                if (savedHoldAtDestination.TryGetValue(unit, out bool origHold))
                {
                    savedHoldAtDestination.Remove(unit);
                    GroundVehicleHoldAtPlayerDestField?.SetValue(gv, origHold);
                }
            }
        }
        finally
        {
            IsIssuingManagedOrder = prev;
        }
    }
}
