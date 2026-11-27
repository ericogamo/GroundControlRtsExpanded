using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using NuclearOption.Jobs;
using UnityEngine;

namespace GroundControlRtsExpanded;

[HarmonyPatch(typeof(TargetDetector), "TargetSearch")]
internal static class TargetDetectorAdaptiveIntervalPatch
{
    private sealed class IntervalBaseline
    {
        internal float BaseInterval;
    }

    private static readonly AccessTools.FieldRef<TargetDetector, Unit?> AttachedUnitRef =
        AccessTools.FieldRefAccess<TargetDetector, Unit?>("attachedUnit");

    private static readonly AccessTools.FieldRef<TargetDetector, float> CheckIntervalRef =
        AccessTools.FieldRefAccess<TargetDetector, float>("checkInterval");

    private static readonly ConditionalWeakTable<TargetDetector, IntervalBaseline> BaseCheckIntervals = new();

    internal static void ClearCaches()
    {
        // ConditionalWeakTable automatically reclaims destroyed TargetDetectors without clearing live baselines.
    }

    private static void Postfix(TargetDetector __instance)
    {
        if (__instance == null || __instance is Radar)
        {
            return;
        }

        Unit? unit = AttachedUnitRef(__instance);
        if (unit is not GroundVehicle || unit.definition is not VehicleDefinition vd)
        {
            return;
        }

        // Keep SAM, AAA, and Radar units scanning at native cadence.
        if (vd.vehicleType == VehicleType.R_SAM
            || vd.vehicleType == VehicleType.IR_SAM
            || vd.vehicleType == VehicleType.AAA
            || vd.vehicleType == VehicleType.RDR)
        {
            return;
        }

        ref float intervalRef = ref CheckIntervalRef(__instance);
        if (!BaseCheckIntervals.TryGetValue(__instance, out IntervalBaseline? baseline))
        {
            baseline = new IntervalBaseline
            {
                BaseInterval = Mathf.Max(0.25f, intervalRef),
            };
            BaseCheckIntervals.Add(__instance, baseline);
        }

        if (AddonConfig.EnableAntiLag.Value && AddonConfig.AdaptiveDetectorThrottle.Value)
        {
            intervalRef = baseline.BaseInterval * PerfMetricsService.Instance.GroundVisualScanMultiplier;
        }
        else
        {
            intervalRef = baseline.BaseInterval;
        }
    }
}

[HarmonyPatch(typeof(FactionHQ), "RpcUpdateTrackingInfo", new[] { typeof(PersistentID) })]
internal static class RpcUpdateTrackingInfoScalePatch
{
    private static readonly Dictionary<(FactionHQ Hq, uint UnitId), float> LastGroundSent = new();
    private static float lastObservedTime;

    internal static void ClearCaches()
    {
        LastGroundSent.Clear();
        lastObservedTime = 0f;
    }

    [HarmonyPriority(Priority.High)]
    private static bool Prefix(FactionHQ __instance, PersistentID id)
    {
        if (__instance == null || !AddonConfig.EnableAntiLag.Value || !AddonConfig.AdaptiveDetectorThrottle.Value)
        {
            return true;
        }

        float minInterval = PerfMetricsService.Instance.GroundTrackingThrottleSeconds;
        if (minInterval <= 1.05f)
        {
            return true;
        }

        if (!id.TryGetUnit(out Unit unit) || unit == null)
        {
            return true;
        }

        // Allow immediate first-time discovery whenever trackingDatabase does not yet have this unit.
        if (__instance.trackingDatabase == null || !__instance.trackingDatabase.ContainsKey(id))
        {
            return true;
        }

        // Exempt aircraft, missiles, ships, radars, SAMs, artillery, defensive buildings, and MDS sites.
        if (IsHighPriorityTrackingUnit(unit))
        {
            return true;
        }

        float now = Time.timeSinceLevelLoad;
        if (now < lastObservedTime || LastGroundSent.Count > 8192)
        {
            LastGroundSent.Clear();
        }
        lastObservedTime = now;

        var key = (__instance, id.Id);
        if (LastGroundSent.TryGetValue(key, out float prev) && now - prev < minInterval)
        {
            return false;
        }

        LastGroundSent[key] = now;
        return true;
    }

    private static bool IsHighPriorityTrackingUnit(Unit unit)
    {
        if (unit is Aircraft || unit is Missile || unit is Ship)
        {
            return true;
        }

        if (unit.radar != null)
        {
            return true;
        }

        if (unit.definition is VehicleDefinition vd)
        {
            if (vd.vehicleType == VehicleType.R_SAM
                || vd.vehicleType == VehicleType.IR_SAM
                || vd.vehicleType == VehicleType.AAA
                || vd.vehicleType == VehicleType.ART
                || vd.vehicleType == VehicleType.RDR)
            {
                return true;
            }

            if (vd.roleIdentity.antiAir > 0f
                || vd.roleIdentity.antiMissile > 0f
                || vd.roleIdentity.antiRadar > 0f)
            {
                return true;
            }
        }
        else if (unit.definition is BuildingDefinition bd)
        {
            if (bd.buildingType == BuildingType.DEF
                || bd.buildingType == BuildingType.RDR)
            {
                return true;
            }

            if (bd.roleIdentity.antiAir > 0f
                || bd.roleIdentity.antiMissile > 0f
                || bd.roleIdentity.antiRadar > 0f)
            {
                return true;
            }

            if ((MdsBridgeBootstrap.IsMdsBuildDefinition != null && MdsBridgeBootstrap.IsMdsBuildDefinition(bd))
                || (MdsBridgeBootstrap.IsRawMdsEncyclopediaDefinition != null && MdsBridgeBootstrap.IsRawMdsEncyclopediaDefinition(bd)))
            {
                return true;
            }
        }

        return MdsBridgeBootstrap.IsMdsSelectableUnit != null && MdsBridgeBootstrap.IsMdsSelectableUnit(unit);
    }
}

internal static class DetectorManagerBudgetPatches
{
    private static readonly AccessTools.FieldRef<DetectorManager, List<DetectionRequest>> LoSRequestsRef =
        AccessTools.FieldRefAccess<DetectorManager, List<DetectionRequest>>("LoSRequests");

    private static readonly List<DetectionRequest> Deferred = new(256);
    private static readonly List<DetectionRequest> ScratchPriority = new(256);
    private static readonly List<DetectionRequest> ScratchVisual = new(256);
    private static readonly List<DetectionRequest> ScratchMerged = new(256);
    private static readonly HashSet<(int DetectorId, int TargetId)> ActiveBatchKeys = new();

    internal static void ClearCaches()
    {
        Deferred.Clear();
        ScratchPriority.Clear();
        ScratchVisual.Clear();
        ScratchMerged.Clear();
        ActiveBatchKeys.Clear();
    }

    private static bool IsValidRequest(in DetectionRequest req)
    {
        return req.Detector != null
            && req.Detector.GetScanPoint() != null
            && req.target != null
            && !req.target.disabled;
    }

    private static void EnqueueDeferredRotating(DetectionRequest req, int maxDeferred)
    {
        if (Deferred.Count >= maxDeferred && Deferred.Count > 0)
        {
            Deferred.RemoveAt(0);
        }

        if (Deferred.Count < maxDeferred)
        {
            Deferred.Add(req);
        }
    }

    [HarmonyPatch(typeof(DetectorManager), "Schedule")]
    internal static class SchedulePatch
    {
        [HarmonyPriority(Priority.High)]
        private static void Prefix(DetectorManager __instance)
        {
            if (GroundControlReflection.IsNetworkGuest())
            {
                return;
            }

            List<DetectionRequest> list = LoSRequestsRef(__instance);
            int budget = AddonConfig.MaxDetectorLoSPerFrame.Value;

            if (!AddonConfig.EnableAntiLag.Value || budget <= 0 || list.Count <= budget)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    DetectionRequest req = list[i];
                    if (!IsValidRequest(in req))
                    {
                        list.RemoveAt(i);
                    }
                }

                PerfMetricsService.Instance.NoteDetectorBatch(list.Count, Deferred.Count);
                return;
            }

            ScratchPriority.Clear();
            ScratchVisual.Clear();
            ActiveBatchKeys.Clear();

            for (int i = 0; i < list.Count; i++)
            {
                DetectionRequest req = list[i];
                if (!IsValidRequest(in req))
                {
                    continue;
                }

                var key = (req.Detector.GetInstanceID(), req.target.GetInstanceID());
                if (!ActiveBatchKeys.Add(key))
                {
                    continue;
                }

                if (req.Detector is Radar || req.target is Aircraft || req.target is Missile)
                {
                    ScratchPriority.Add(req);
                }
                else
                {
                    ScratchVisual.Add(req);
                }
            }

            ActiveBatchKeys.Clear();
            list.Clear();

            int maxDeferred = budget * 4;
            int visualReserve = Mathf.Min(ScratchVisual.Count, Mathf.Max(32, budget / 4));
            int priorityCap = Mathf.Max(0, budget - visualReserve);

            int priIdx = 0;
            while (priIdx < ScratchPriority.Count && list.Count < priorityCap)
            {
                list.Add(ScratchPriority[priIdx++]);
            }

            int visIdx = 0;
            while (visIdx < ScratchVisual.Count && list.Count < budget)
            {
                list.Add(ScratchVisual[visIdx++]);
            }

            while (priIdx < ScratchPriority.Count && list.Count < budget)
            {
                list.Add(ScratchPriority[priIdx++]);
            }

            // Prune any stale leftover Deferred items before folding in new overflow.
            for (int i = Deferred.Count - 1; i >= 0; i--)
            {
                DetectionRequest existingDef = Deferred[i];
                if (!IsValidRequest(in existingDef))
                {
                    Deferred.RemoveAt(i);
                }
            }

            int remVis = ScratchVisual.Count - visIdx;
            int visualDeferredCap = Mathf.Min(remVis, Mathf.Max(64, maxDeferred / 4));
            int priDeferredAdded = 0;
            int priDeferredLimit = Mathf.Max(0, maxDeferred - visualDeferredCap);

            while (priIdx < ScratchPriority.Count && priDeferredAdded < priDeferredLimit)
            {
                EnqueueDeferredRotating(ScratchPriority[priIdx++], maxDeferred);
                priDeferredAdded++;
            }

            while (visIdx < ScratchVisual.Count)
            {
                EnqueueDeferredRotating(ScratchVisual[visIdx++], maxDeferred);
            }

            while (priIdx < ScratchPriority.Count)
            {
                EnqueueDeferredRotating(ScratchPriority[priIdx++], maxDeferred);
            }

            ScratchPriority.Clear();
            ScratchVisual.Clear();
            PerfMetricsService.Instance.NoteDetectorBatch(list.Count, Deferred.Count);
        }
    }

    [HarmonyPatch(typeof(DetectorManager), "FinishJob")]
    internal static class FinishJobPatch
    {
        private static void Postfix(DetectorManager __instance)
        {
            if (Deferred.Count == 0)
            {
                return;
            }

            List<DetectionRequest> list = LoSRequestsRef(__instance);
            ActiveBatchKeys.Clear();
            ScratchMerged.Clear();

            // Place aged deferred requests ahead of fresh arrivals so deferred entries rotate FIFO without starvation.
            for (int i = 0; i < Deferred.Count; i++)
            {
                DetectionRequest req = Deferred[i];
                if (!IsValidRequest(in req))
                {
                    continue;
                }

                var key = (req.Detector.GetInstanceID(), req.target.GetInstanceID());
                if (ActiveBatchKeys.Add(key))
                {
                    ScratchMerged.Add(req);
                }
            }

            for (int i = 0; i < list.Count; i++)
            {
                DetectionRequest existing = list[i];
                if (!IsValidRequest(in existing))
                {
                    continue;
                }

                var key = (existing.Detector.GetInstanceID(), existing.target.GetInstanceID());
                if (ActiveBatchKeys.Add(key))
                {
                    ScratchMerged.Add(existing);
                }
            }

            list.Clear();
            list.AddRange(ScratchMerged);

            ScratchMerged.Clear();
            ActiveBatchKeys.Clear();
            Deferred.Clear();
        }
    }

    [HarmonyPatch(typeof(DetectorManager), "DisposeAll")]
    internal static class DisposeAllPatch
    {
        private static void Postfix()
        {
            ClearCaches();
        }
    }
}
