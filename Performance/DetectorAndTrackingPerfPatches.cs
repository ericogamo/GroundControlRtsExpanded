using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.Jobs;
using UnityEngine;

namespace GroundControlRtsExpanded;

[HarmonyPatch(typeof(TargetDetector), "TargetSearch")]
internal static class TargetDetectorAdaptiveIntervalPatch
{
    private static readonly AccessTools.FieldRef<TargetDetector, Unit?> AttachedUnitRef =
        AccessTools.FieldRefAccess<TargetDetector, Unit?>("attachedUnit");

    private static readonly AccessTools.FieldRef<TargetDetector, float> CheckIntervalRef =
        AccessTools.FieldRefAccess<TargetDetector, float>("checkInterval");

    private static readonly Dictionary<int, float> BaseCheckIntervals = new();

    internal static void ClearCaches()
    {
        BaseCheckIntervals.Clear();
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

        int id = __instance.GetInstanceID();
        ref float intervalRef = ref CheckIntervalRef(__instance);
        if (!BaseCheckIntervals.TryGetValue(id, out float baseInterval))
        {
            if (BaseCheckIntervals.Count > 2000)
            {
                BaseCheckIntervals.Clear();
            }

            baseInterval = Mathf.Max(0.25f, intervalRef);
            BaseCheckIntervals[id] = baseInterval;
        }

        if (AddonConfig.EnableAntiLag.Value && AddonConfig.AdaptiveDetectorThrottle.Value)
        {
            intervalRef = baseInterval * PerfMetricsService.Instance.GroundVisualScanMultiplier;
        }
        else
        {
            intervalRef = baseInterval;
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

        // Aircraft and missiles stay unthrottled (handled only by GC's default 1s floor).
        if (id.TryGetUnit(out Unit unit) && (unit is Aircraft || unit is Missile))
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
}

internal static class DetectorManagerBudgetPatches
{
    private static readonly AccessTools.FieldRef<DetectorManager, List<DetectionRequest>> LoSRequestsRef =
        AccessTools.FieldRefAccess<DetectorManager, List<DetectionRequest>>("LoSRequests");

    private static readonly List<DetectionRequest> Deferred = new(256);
    private static readonly List<DetectionRequest> ScratchPriority = new(256);
    private static readonly List<DetectionRequest> ScratchVisual = new(256);
    private static readonly HashSet<(int DetectorId, int TargetId)> ActiveBatchKeys = new();

    internal static void ClearCaches()
    {
        Deferred.Clear();
        ScratchPriority.Clear();
        ScratchVisual.Clear();
        ActiveBatchKeys.Clear();
    }

    [HarmonyPatch(typeof(DetectorManager), "Schedule")]
    internal static class SchedulePatch
    {
        [HarmonyPriority(Priority.High)]
        private static void Prefix(DetectorManager __instance)
        {
            List<DetectionRequest> list = LoSRequestsRef(__instance);
            int budget = AddonConfig.MaxDetectorLoSPerFrame.Value;

            if (!AddonConfig.EnableAntiLag.Value || budget <= 0 || list.Count <= budget)
            {
                PerfMetricsService.Instance.NoteDetectorBatch(list.Count, Deferred.Count);
                return;
            }

            ScratchPriority.Clear();
            ScratchVisual.Clear();

            for (int i = 0; i < list.Count; i++)
            {
                DetectionRequest req = list[i];
                if (req.Detector == null || req.target == null || req.target.disabled)
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

            while (priIdx < ScratchPriority.Count && Deferred.Count < maxDeferred)
            {
                Deferred.Add(ScratchPriority[priIdx++]);
            }

            while (visIdx < ScratchVisual.Count && Deferred.Count < maxDeferred)
            {
                Deferred.Add(ScratchVisual[visIdx++]);
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
            for (int i = 0; i < list.Count; i++)
            {
                DetectionRequest existing = list[i];
                if (existing.Detector != null && existing.target != null)
                {
                    ActiveBatchKeys.Add((existing.Detector.GetInstanceID(), existing.target.GetInstanceID()));
                }
            }

            for (int i = 0; i < Deferred.Count; i++)
            {
                DetectionRequest req = Deferred[i];
                if (req.Detector == null || req.target == null || req.target.disabled)
                {
                    continue;
                }

                var key = (req.Detector.GetInstanceID(), req.target.GetInstanceID());
                if (ActiveBatchKeys.Add(key))
                {
                    list.Add(req);
                }
            }

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
