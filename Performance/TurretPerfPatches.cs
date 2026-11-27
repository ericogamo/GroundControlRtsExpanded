using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GroundControlRtsExpanded;

[HarmonyPatch]
internal static class TurretIdleThrottlePatch
{
    private const float ActivePaddingMultiplier = 1.15f;
    private const float ActivePaddingMeters = 500f;

    private static readonly AccessTools.FieldRef<Turret, Unit?> TargetRef =
        AccessTools.FieldRefAccess<Turret, Unit?>("target");

    private static readonly AccessTools.FieldRef<Turret, Unit?> AttachedUnitRef =
        AccessTools.FieldRefAccess<Turret, Unit?>("attachedUnit");

    private static readonly AccessTools.FieldRef<Turret, WeaponStation?> CurrentWeaponStationRef =
        AccessTools.FieldRefAccess<Turret, WeaponStation?>("currentWeaponStation");

    private static readonly Dictionary<int, float> NextIdleScanAt = new();
    private static readonly List<int> PruneKeys = new();

    internal static float ActiveConeMaxRangeSqr { get; private set; }
    internal static FactionHQ? ActiveConeHq { get; private set; }

    internal static void ClearCaches()
    {
        ActiveConeMaxRangeSqr = 0f;
        ActiveConeHq = null;
        NextIdleScanAt.Clear();
        PruneKeys.Clear();
    }

    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Turret), "DatalinkTargetSearch");
        yield return AccessTools.Method(typeof(Turret), "FireSupportTargetSearch");
    }

    [HarmonyPriority(Priority.High)]
    private static bool Prefix(Turret __instance)
    {
        ActiveConeMaxRangeSqr = 0f;
        ActiveConeHq = null;

        if (!AddonConfig.EnableAntiLag.Value || !AddonConfig.IdleTurretThrottle.Value || __instance == null || GroundControlReflection.IsNetworkGuest())
        {
            return true;
        }

        Unit? attachedUnit = AttachedUnitRef(__instance);
        Unit? target = TargetRef(__instance);
        float now = Time.timeSinceLevelLoad;

        if (target == null || target.disabled)
        {
            float desiredInterval = PerfMetricsService.Instance.IdleTurretIntervalSeconds;
            if (desiredInterval > 2.05f)
            {
                // Keep anti-air interceptors (aircraft & radar/IR SAMs) responsive; throttle non-SAM ground/naval/building turrets.
                float effectiveInterval = IsHighPriorityAirDefence(attachedUnit)
                    ? Mathf.Min(desiredInterval, 2.5f)
                    : desiredInterval;

                int id = __instance.GetInstanceID();
                if (NextIdleScanAt.TryGetValue(id, out float nextAllowed) && now < nextAllowed)
                {
                    PerfMetricsService.Instance.NoteIdleTurretSkip();
                    return false;
                }

                if (NextIdleScanAt.Count > 1500)
                {
                    PruneExpired(now);
                }

                float stagger = (id & 15) * 0.03f;
                NextIdleScanAt[id] = now + effectiveInterval + stagger;
            }
        }

        float maxRange = 0f;
        WeaponStation[]? stations = __instance.GetWeaponStations();
        if (stations != null)
        {
            for (int i = 0; i < stations.Length; i++)
            {
                WeaponStation? st = stations[i];
                if (st != null && !st.Cargo && st.Ammo > 0 && st.WeaponInfo != null)
                {
                    float r = st.WeaponInfo.targetRequirements.maxRange;
                    if (r > maxRange)
                    {
                        maxRange = r;
                    }
                }
            }
        }

        if (maxRange <= 0f)
        {
            WeaponStation? ws = CurrentWeaponStationRef(__instance);
            maxRange = ws?.WeaponInfo != null ? ws.WeaponInfo.targetRequirements.maxRange : 0f;
        }

        if (maxRange > 0f)
        {
            float paddedRange = maxRange * ActivePaddingMultiplier + ActivePaddingMeters;
            ActiveConeMaxRangeSqr = paddedRange * paddedRange;
            ActiveConeHq = attachedUnit != null ? attachedUnit.NetworkHQ : null;
        }

        return true;
    }

    private static Exception? Finalizer(Exception? __exception)
    {
        ActiveConeMaxRangeSqr = 0f;
        ActiveConeHq = null;
        return __exception;
    }

    private static bool IsHighPriorityAirDefence(Unit? unit)
    {
        if (unit == null)
        {
            return false;
        }

        if (unit is Aircraft)
        {
            return true;
        }

        if (unit.definition is VehicleDefinition vd)
        {
            return vd.vehicleType == VehicleType.R_SAM
                || vd.vehicleType == VehicleType.IR_SAM
                || vd.vehicleType == VehicleType.AAA;
        }

        if (unit is Ship || unit is Building)
        {
            return unit.definition != null && unit.definition.roleIdentity.antiAir > 0f;
        }

        return false;
    }

    private static void PruneExpired(float now)
    {
        PruneKeys.Clear();
        foreach (KeyValuePair<int, float> pair in NextIdleScanAt)
        {
            if (pair.Value <= now - 30f)
            {
                PruneKeys.Add(pair.Key);
            }
        }

        if (PruneKeys.Count > 0)
        {
            for (int i = 0; i < PruneKeys.Count; i++)
            {
                NextIdleScanAt.Remove(PruneKeys[i]);
            }
        }
        else if (NextIdleScanAt.Count > 2500)
        {
            NextIdleScanAt.Clear();
        }

        PruneKeys.Clear();
    }
}

[HarmonyPatch(typeof(FactionHQ), "GetTargetsWithinCone", new[] { typeof(List<Unit>), typeof(Transform), typeof(FiringCone[]), typeof(bool) })]
internal static class FactionHqGetTargetsWithinConePatch
{
    private static bool Prefix(
        FactionHQ __instance,
        List<Unit> targetList,
        Transform fromTransform,
        FiringCone[] firingCones,
        bool requireLineOfSight,
        ref List<Unit> __result)
    {
        float maxRangeSqr = TurretIdleThrottlePatch.ActiveConeMaxRangeSqr;
        if (!AddonConfig.EnableAntiLag.Value
            || !AddonConfig.IdleTurretThrottle.Value
            || maxRangeSqr <= 0f
            || __instance == null
            || !ReferenceEquals(__instance, TurretIdleThrottlePatch.ActiveConeHq)
            || fromTransform == null
            || targetList == null
            || __instance.trackingDatabase == null)
        {
            return true;
        }

        targetList.Clear();
        GlobalPosition fromGlobalPos = fromTransform.GlobalPosition();
        Vector3 fromWorldPos = fromTransform.position;

        foreach (KeyValuePair<PersistentID, TrackingInfo> item in __instance.trackingDatabase)
        {
            TrackingInfo info = item.Value;
            GlobalPosition pos = info.GetPosition();

            // Pre-filter range before cone trig checks, accuracy lookups, and Physics.Linecast.
            if (FastMath.SquareDistance(pos, fromGlobalPos) > maxRangeSqr)
            {
                PerfMetricsService.Instance.NoteConeRangeSkip();
                continue;
            }

            if (FiringConeChecker.VectorWithinFiringCones(firingCones, pos - fromGlobalPos, out _)
                && info.TryGetUnit(out Unit unit)
                && unit != null
                && !unit.disabled
                && __instance.IsTargetPositionAccurate(unit, 10f))
            {
                if (requireLineOfSight)
                {
                    PerfMetricsService.Instance.NoteConeLosCheck();
                    if (!unit.LineOfSight(fromWorldPos, 1000f))
                    {
                        continue;
                    }
                }

                targetList.Add(unit);
            }
        }

        __result = targetList;
        return false;
    }
}
