using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal sealed class AiTacticalDoctrineService : IAddonResetSession
{
    private const float RadarCountCacheSeconds = 2f;
    private const float MaxSeadHoldSeconds = 65f;
    private const float LossWindowSeconds = 180f;
    private const float StallCooldownSeconds = 180f;
    private const float ForcedTargetDeconflictSeconds = 20f;
    private const float PeriodicPruneIntervalSeconds = 60f;
    private const int StallLossThreshold = 6;
    private static readonly float[] FlankAnglesDegrees = { 35f, -35f, 65f, -65f };

    private sealed class PushTracker
    {
        internal Airbase? Target;
        internal readonly HashSet<Unit> LastCommitted = new();
        internal readonly Queue<float> LossTimestamps = new();
    }

    internal static AiTacticalDoctrineService Instance { get; } = new();

    private readonly Dictionary<FactionHQ, (float CachedAt, int Count)> radarCountCache = new();
    private readonly Dictionary<FactionHQ, float> seadHoldStartedAt = new();
    private readonly Dictionary<FactionHQ, float> seadHoldCooldownUntil = new();
    private readonly Dictionary<FactionHQ, int> savedWaveGoalDuringSead = new();
    private readonly Dictionary<(FactionHQ Hq, Airbase Staging, int Slot), PushTracker> pushTrackers = new();
    private readonly Dictionary<(FactionHQ Hq, Airbase Target), float> targetStalledUntil = new();
    private readonly Dictionary<(FactionHQ Hq, Airbase Target), int> targetFlankIndex = new();
    private readonly Dictionary<FactionHQ, Airbase> lastStalledTargetByHq = new();
    private readonly Dictionary<FactionHQ, int> strikeSortieCounters = new();
    private readonly Dictionary<(FactionHQ Hq, Unit Target), float> recentForcedTargets = new();
    private readonly Dictionary<Unit, float> navalRetreatOrderAt = new();
    private readonly List<FactionHQ> staleRadarKeys = new();
    private readonly List<(FactionHQ Hq, Unit Target)> staleForcedTargetKeys = new();
    private readonly List<Unit> staleNavalKeys = new();
    private readonly List<(FactionHQ Hq, Airbase Target)> staleStallKeys = new();
    private readonly List<(FactionHQ Hq, Airbase Staging, int Slot)> stalePushKeys = new();
    private float nextPruneAt;

    private AiTacticalDoctrineService()
    {
    }

    public void ResetSession()
    {
        radarCountCache.Clear();
        seadHoldStartedAt.Clear();
        seadHoldCooldownUntil.Clear();
        savedWaveGoalDuringSead.Clear();
        pushTrackers.Clear();
        targetStalledUntil.Clear();
        targetFlankIndex.Clear();
        lastStalledTargetByHq.Clear();
        strikeSortieCounters.Clear();
        recentForcedTargets.Clear();
        navalRetreatOrderAt.Clear();
        staleRadarKeys.Clear();
        staleForcedTargetKeys.Clear();
        staleNavalKeys.Clear();
        staleStallKeys.Clear();
        stalePushKeys.Clear();
        nextPruneAt = 0f;
    }

    internal int CountDetectedEnemySamRadars(FactionHQ? aiHq)
    {
        if (aiHq == null)
        {
            return 0;
        }

        float now = Time.unscaledTime;
        if (radarCountCache.TryGetValue(aiHq, out var cached) && now - cached.CachedAt < RadarCountCacheSeconds)
        {
            return cached.Count;
        }

        int count = 0;
        if (aiHq.trackingDatabase != null)
        {
            float levelTime = Time.timeSinceLevelLoad;
            foreach (KeyValuePair<PersistentID, TrackingInfo> kv in aiHq.trackingDatabase)
            {
                TrackingInfo? info = kv.Value;
                if (info == null
                    || !info.TryGetUnit(out Unit unit)
                    || unit == null
                    || unit.disabled
                    || unit is Aircraft
                    || ReferenceEquals(unit.NetworkHQ, aiHq)
                    || levelTime - info.lastSpottedTime > 120f)
                {
                    continue;
                }

                if (unit.HasRadarEmission()
                    || (unit.definition is VehicleDefinition veh
                        && (veh.vehicleType == VehicleType.R_SAM || veh.vehicleType == VehicleType.RDR))
                    || (unit.definition is BuildingDefinition bdef && bdef.buildingType == BuildingType.RDR))
                {
                    count++;
                }
            }
        }

        if (count == 0 && MdsBridgeBootstrap.HasEmittingHostileMdsRadar?.Invoke(aiHq) == true)
        {
            count = 1;
        }

        radarCountCache[aiHq] = (now, count);
        return count;
    }

    internal int CountAirborneRadarHunters(FactionHQ hq)
    {
        if (hq == null || hq.factionUnits == null)
        {
            return 0;
        }

        int count = 0;
        foreach (PersistentID pid in hq.factionUnits)
        {
            if (pid.TryGetUnit(out Unit unit)
                && unit is Aircraft aircraft
                && !unit.disabled
                && GroundControlReflection.IsRadarHunter(aircraft, hq))
            {
                count++;
            }
        }

        return count;
    }

    internal bool HasDetectedEnemyRadar(FactionHQ aiHq)
    {
        if (aiHq == null)
        {
            return false;
        }

        GlobalPosition probe = default;
        if (GroundControlReflection.FindRadarToHunt(aiHq, probe, out _) != null)
        {
            return true;
        }

        return MdsBridgeBootstrap.HasEmittingHostileMdsRadar?.Invoke(aiHq) == true;
    }

    internal bool ShouldHoldBombersForSead(FactionHQ aiHq, FactionHQ? localHq, bool waveCurrentlyOut, out bool releaseSavedGoal)
    {
        releaseSavedGoal = false;

        if (CountDetectedEnemySamRadars(aiHq) <= 0)
        {
            if (seadHoldStartedAt.Remove(aiHq))
            {
                releaseSavedGoal = true;
            }
            return false;
        }

        bool seadInFlight = CountAirborneRadarHunters(aiHq) > 0;
        bool hasAirRoom = !DynamicCapAndTechService.IsActive || !DynamicCapAndTechService.Instance.IsAirFull(aiHq);
        bool hasDetectedRadar = HasDetectedEnemyRadar(aiHq);
        bool seadInFlightOrAvailable = seadInFlight || (hasAirRoom && hasDetectedRadar);

        if (!seadInFlightOrAvailable)
        {
            if (seadHoldStartedAt.Remove(aiHq))
            {
                releaseSavedGoal = true;
            }
            return false;
        }

        float now = Time.time;
        if (!seadHoldStartedAt.TryGetValue(aiHq, out float startedAt))
        {
            if (waveCurrentlyOut || (seadHoldCooldownUntil.TryGetValue(aiHq, out float readyAt) && now < readyAt))
            {
                return false;
            }

            seadHoldStartedAt[aiHq] = now;
            if (AddonConfig.VerboseLogging.Value && aiHq.faction != null)
            {
                AddonLog.Info($"[Tactical AI] {aiHq.faction.name} holding strike wave up to {MaxSeadHoldSeconds:0}s for SEAD suppression.");
            }
            return true;
        }

        if (now - startedAt >= MaxSeadHoldSeconds)
        {
            // Stop waiting after SEAD window expires and enforce a 90s cooldown so holds cannot loop endlessly.
            seadHoldStartedAt.Remove(aiHq);
            seadHoldCooldownUntil[aiHq] = now + 90f;
            releaseSavedGoal = true;
            return false;
        }

        return true;
    }

    internal int NextStrikeSortieIndex(FactionHQ hq)
    {
        int next = (strikeSortieCounters.TryGetValue(hq, out int cur) ? cur : 0) + 1;
        strikeSortieCounters[hq] = next;
        return next;
    }

    internal bool IsForcedTargetRecentlyAssigned(FactionHQ hq, Unit target, float now)
    {
        if (hq == null || target == null)
        {
            return false;
        }

        return recentForcedTargets.TryGetValue((hq, target), out float assignedAt)
            && now - assignedAt < ForcedTargetDeconflictSeconds;
    }

    internal void NoteForcedTargetAssigned(FactionHQ hq, Unit target, float now)
    {
        if (hq == null || target == null)
        {
            return;
        }

        if (recentForcedTargets.Count > 64)
        {
            staleForcedTargetKeys.Clear();
            foreach (KeyValuePair<(FactionHQ Hq, Unit Target), float> kv in recentForcedTargets)
            {
                if (kv.Key.Target == null || kv.Key.Target.disabled || now - kv.Value >= ForcedTargetDeconflictSeconds)
                {
                    staleForcedTargetKeys.Add(kv.Key);
                }
            }

            for (int i = 0; i < staleForcedTargetKeys.Count; i++)
            {
                recentForcedTargets.Remove(staleForcedTargetKeys[i]);
            }

            staleForcedTargetKeys.Clear();
        }

        recentForcedTargets[(hq, target)] = now;
    }

    internal bool ShouldIssueNavalRetreat(Unit ship, float now)
    {
        if (ship == null)
        {
            return false;
        }

        PrunePeriodicIfDue(now);
        if (navalRetreatOrderAt.TryGetValue(ship, out float lastAt) && now < lastAt + 20f)
        {
            return false;
        }

        navalRetreatOrderAt[ship] = now;
        return true;
    }

    internal void NoteSavedWaveGoal(FactionHQ hq, int waveGoal)
    {
        if (!savedWaveGoalDuringSead.ContainsKey(hq))
        {
            savedWaveGoalDuringSead[hq] = waveGoal;
        }
    }

    internal int ConsumeSavedWaveGoal(FactionHQ hq, int fallbackGoal)
    {
        if (savedWaveGoalDuringSead.TryGetValue(hq, out int goal))
        {
            savedWaveGoalDuringSead.Remove(hq);
            return goal > 0 ? goal : fallbackGoal;
        }

        return fallbackGoal;
    }

    internal void ObserveDrives(object captureServiceInstance)
    {
        if (GroundControlReflection.CaptureDrivesField?.GetValue(captureServiceInstance) is not IDictionary drives)
        {
            return;
        }

        float now = Time.time;
        PrunePeriodicIfDue(now);

        foreach (DictionaryEntry entry in drives)
        {
            object? driveObj = entry.Value;
            if (driveObj == null || entry.Key is not ValueTuple<Airbase, int> slotKey)
            {
                continue;
            }

            if (GroundControlReflection.DriveOwnerProp?.GetValue(driveObj) is not FactionHQ hq
                || GroundControlReflection.DriveTargetProp?.GetValue(driveObj) is not Airbase target
                || GroundControlReflection.DriveCommittedProp?.GetValue(driveObj) is not List<Unit> committed)
            {
                continue;
            }

            var key = (hq, slotKey.Item1, slotKey.Item2);
            if (!pushTrackers.TryGetValue(key, out PushTracker tracker))
            {
                tracker = new PushTracker { Target = target };
                pushTrackers[key] = tracker;
            }
            else if (!ReferenceEquals(tracker.Target, target))
            {
                tracker.Target = target;
                tracker.LastCommitted.Clear();
                tracker.LossTimestamps.Clear();
            }

            // Detect units lost from the previously committed assault roster.
            foreach (Unit prevUnit in tracker.LastCommitted)
            {
                if (prevUnit == null || prevUnit.disabled)
                {
                    tracker.LossTimestamps.Enqueue(now);
                }
            }

            tracker.LastCommitted.Clear();
            for (int i = 0; i < committed.Count; i++)
            {
                Unit u = committed[i];
                if (u != null && !u.disabled)
                {
                    tracker.LastCommitted.Add(u);
                }
            }

            while (tracker.LossTimestamps.Count > 0 && now - tracker.LossTimestamps.Peek() > LossWindowSeconds)
            {
                tracker.LossTimestamps.Dequeue();
            }

            float ownerGrip = GroundControlReflection.GetOwnerGrip(target);
            if ((tracker.LossTimestamps.Count >= StallLossThreshold && ownerGrip >= 0.85f)
                || tracker.LossTimestamps.Count >= StallLossThreshold + 2)
            {
                var targetKey = (hq, target);
                if (!targetStalledUntil.TryGetValue(targetKey, out float stalledUntil) || now >= stalledUntil)
                {
                    targetStalledUntil[targetKey] = now + StallCooldownSeconds;
                    lastStalledTargetByHq[hq] = target;

                    int nextIdx = targetFlankIndex.TryGetValue(targetKey, out int cur)
                        ? (cur + 1) % FlankAnglesDegrees.Length
                        : 0;
                    targetFlankIndex[targetKey] = nextIdx;

                    string factionName = hq.faction != null ? hq.faction.name : "AI";
                    string baseLabel = GroundControlReflection.GetAirbaseLabel(target);
                    AddonLog.Info(
                        $"[Tactical AI] {factionName} stalled on slot #{slotKey.Item2} at {baseLabel} ({tracker.LossTimestamps.Count} losses in {LossWindowSeconds:0}s) -> " +
                        $"flank rotation #{nextIdx} ({FlankAnglesDegrees[nextIdx]:+0;-0;0} deg) & shifting target.");
                }
            }
        }
    }

    internal bool IsTargetStalled(FactionHQ hq, Airbase? target)
    {
        if (hq == null || target == null)
        {
            return false;
        }

        return targetStalledUntil.TryGetValue((hq, target), out float stalledUntil)
            && Time.time < stalledUntil;
    }

    internal bool TryGetFlankAngleOffsetDegrees(FactionHQ hq, Airbase? target, out float angleDegrees)
    {
        if (hq != null)
        {
            Airbase? resolvedTarget = target ?? (lastStalledTargetByHq.TryGetValue(hq, out Airbase lastTarget) ? lastTarget : null);
            if (resolvedTarget != null
                && targetStalledUntil.TryGetValue((hq, resolvedTarget), out float stalledUntil)
                && Time.time < stalledUntil
                && targetFlankIndex.TryGetValue((hq, resolvedTarget), out int idx))
            {
                angleDegrees = FlankAnglesDegrees[idx % FlankAnglesDegrees.Length];
                return true;
            }
        }

        angleDegrees = 0f;
        return false;
    }

    private void PrunePeriodicIfDue(float now)
    {
        if (now < nextPruneAt)
        {
            return;
        }

        nextPruneAt = now + PeriodicPruneIntervalSeconds;

        if (navalRetreatOrderAt.Count > 0)
        {
            staleNavalKeys.Clear();
            foreach (KeyValuePair<Unit, float> kv in navalRetreatOrderAt)
            {
                if (kv.Key == null || kv.Key.disabled || now - kv.Value > 120f)
                {
                    staleNavalKeys.Add(kv.Key!);
                }
            }

            for (int i = 0; i < staleNavalKeys.Count; i++)
            {
                navalRetreatOrderAt.Remove(staleNavalKeys[i]);
            }

            staleNavalKeys.Clear();
        }

        if (targetStalledUntil.Count > 0)
        {
            staleStallKeys.Clear();
            foreach (KeyValuePair<(FactionHQ Hq, Airbase Target), float> kv in targetStalledUntil)
            {
                if (kv.Key.Hq == null || kv.Key.Target == null || kv.Key.Target.disabled || now >= kv.Value)
                {
                    staleStallKeys.Add(kv.Key);
                }
            }

            for (int i = 0; i < staleStallKeys.Count; i++)
            {
                targetStalledUntil.Remove(staleStallKeys[i]);
            }

            staleStallKeys.Clear();
        }

        if (pushTrackers.Count > 0)
        {
            stalePushKeys.Clear();
            foreach (KeyValuePair<(FactionHQ Hq, Airbase Staging, int Slot), PushTracker> kv in pushTrackers)
            {
                if (kv.Key.Hq == null || kv.Key.Staging == null || kv.Key.Staging.disabled)
                {
                    stalePushKeys.Add(kv.Key);
                }
            }

            for (int i = 0; i < stalePushKeys.Count; i++)
            {
                pushTrackers.Remove(stalePushKeys[i]);
            }

            stalePushKeys.Clear();
        }

        if (radarCountCache.Count > 0)
        {
            float unscaledNow = Time.unscaledTime;
            staleRadarKeys.Clear();
            foreach (KeyValuePair<FactionHQ, (float CachedAt, int Count)> kv in radarCountCache)
            {
                if (kv.Key == null || unscaledNow - kv.Value.CachedAt > 30f)
                {
                    staleRadarKeys.Add(kv.Key!);
                }
            }

            for (int i = 0; i < staleRadarKeys.Count; i++)
            {
                radarCountCache.Remove(staleRadarKeys[i]);
            }

            staleRadarKeys.Clear();
        }
    }
}
