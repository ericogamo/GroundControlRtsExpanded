using System;
using System.Collections.Generic;
using Mirage;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal sealed class MdsFlatbedHaulService : IAddonTick, IAddonGui, IAddonResetSession
{
    private enum HaulStage
    {
        Approaching = 0,
        Loading = 1,
        Loaded = 2,
    }

    private sealed class HaulJob
    {
        internal Unit Container { get; set; } = null!;
        internal GroundVehicle Hauler { get; set; } = null!;
        internal FactionHQ Hq { get; set; } = null!;
        internal HaulStage Stage { get; set; }
        internal Vector3 BedOffset { get; set; }
        internal Vector3 LoadStartPos { get; set; }
        internal Quaternion LoadStartRot { get; set; }
        internal float LoadStartedAt { get; set; }
        internal float LoadDurationSeconds { get; set; } = 2.4f;
        internal float GroundClearanceY { get; set; }
        internal float ApproachStartedAt { get; set; }
        internal float NextOrderAt { get; set; }
        internal float StoppedSince { get; set; } = float.MaxValue;
        internal List<(Collider ContainerCol, Collider HaulerCol)> IgnoredCollisionPairs { get; } = new();
        internal bool WasKinematic { get; set; }
    }

    private sealed class ClientMirroredHaulJob
    {
        internal uint ContainerPid { get; set; }
        internal uint HaulerPid { get; set; }
        internal Unit? Container { get; set; }
        internal GroundVehicle? Hauler { get; set; }
        internal HaulStage Stage { get; set; }
        internal Vector3 BedOffset { get; set; }
        internal Vector3 LoadStartPos { get; set; }
        internal Quaternion LoadStartRot { get; set; }
        internal float CreatedAt { get; set; }
        internal float LoadStartedAt { get; set; }
        internal float LoadDurationSeconds { get; set; } = 2.4f;
        internal bool PhysicsLocked { get; set; }
        internal List<(Collider ContainerCol, Collider HaulerCol)> IgnoredCollisionPairs { get; } = new();
        internal bool WasKinematic { get; set; }
    }

    private const float MaxTractorSearchMeters = 300f;
    private const float StagingDistanceMeters = 14f;
    private const float HitchArrivalMeters = 18f;
    private const float StalledHitchRadiusMeters = 22f;
    private const float MaxApproachTimeoutSeconds = 90f;
    private const float ClientUnresolvedTimeoutSeconds = 5f;
    private const float ClientRelocationTimeoutSeconds = 30f;
    private const float BedHeightMeters = 0.82f;

    internal static MdsFlatbedHaulService Instance { get; } = new();

    private readonly List<HaulJob> jobs = new();
    private readonly Dictionary<uint, ClientMirroredHaulJob> clientJobs = new();
    private readonly Dictionary<uint, (GlobalPosition Pos, float Yaw)> relocatedBuildings = new();
    private readonly Dictionary<uint, (GlobalPosition Pos, float Yaw, float AddedAt)> pendingClientRelocations = new();
    private readonly List<uint> relocationScratch = new();
    private readonly List<uint> clientCleanupScratch = new();
    private float nextAirdropRetryAt;
    private Unit? availabilityContainer;
    private bool cachedHaulerAvailable;
    private float nextAvailabilityCheckAt;
    private bool issuingInternalCommand;

    private MdsFlatbedHaulService()
    {
    }

    internal bool ContainsScreenPoint(Vector2 screenPoint) => false;

    internal bool IsMouseOverUi() => false;

    public void ResetSession()
    {
        for (int i = jobs.Count - 1; i >= 0; i--)
        {
            if (jobs[i].Container != null)
            {
                MdsBridgeBootstrap.SetMdsContainerTransitState?.Invoke(jobs[i].Container, false);
            }

            RestoreContainerPhysics(jobs[i], placeOnGround: false);
        }

        foreach (ClientMirroredHaulJob cj in clientJobs.Values)
        {
            RestoreClientContainerPhysics(cj);
        }

        jobs.Clear();
        clientJobs.Clear();
        relocatedBuildings.Clear();
        pendingClientRelocations.Clear();
        relocationScratch.Clear();
        clientCleanupScratch.Clear();
        nextAirdropRetryAt = 0f;
        availabilityContainer = null;
        cachedHaulerAvailable = false;
        nextAvailabilityCheckAt = 0f;
    }

    internal void SendInitialSyncToPlayer(INetworkPlayer guest)
    {
        if (guest == null)
        {
            return;
        }

        for (int i = 0; i < jobs.Count; i++)
        {
            HaulJob j = jobs[i];
            if (j.Container == null || !j.Container.persistentID.IsValid || j.Hauler == null || !j.Hauler.persistentID.IsValid)
            {
                continue;
            }

            float elapsed = j.Stage == HaulStage.Loading ? Mathf.Max(0f, Time.time - j.LoadStartedAt) : 0f;
            GcreNetworkSync.BroadcastHaulSync(
                j.Container.persistentID.Id,
                j.Hauler.persistentID.Id,
                (int)j.Stage,
                j.BedOffset,
                j.LoadDurationSeconds,
                elapsed,
                targetPlayer: guest);
        }

        relocationScratch.Clear();
        foreach (KeyValuePair<uint, (GlobalPosition Pos, float Yaw)> kv in relocatedBuildings)
        {
            if (!new PersistentID { Id = kv.Key }.TryGetUnit(out Unit unit) || unit == null || unit.disabled)
            {
                relocationScratch.Add(kv.Key);
                continue;
            }

            GcreNetworkSync.BroadcastRelocatedBuilding(kv.Key, kv.Value.Pos, kv.Value.Yaw, targetPlayer: guest);
        }

        for (int i = 0; i < relocationScratch.Count; i++)
        {
            relocatedBuildings.Remove(relocationScratch[i]);
        }
    }

    public void Tick()
    {
        if (!AddonConfig.ModEnabled.Value)
        {
            return;
        }

        float now = Time.time;
        float unscaledNow = Time.unscaledTime;
        if (unscaledNow >= nextAirdropRetryAt && MdsBridgeBootstrap.EnsureAirdropMountsInjectedAction != null && Encyclopedia.i != null)
        {
            nextAirdropRetryAt = unscaledNow + 2.0f;
            MdsBridgeBootstrap.EnsureAirdropMountsInjectedAction();
        }

        if (GcreNetworkSync.IsPureClient)
        {
            TickClientMirroredJobs(now);
            return;
        }

        TickJobs(now);
    }

    public void DrawGui()
    {
        // All Skyshield selection, stance, FCU link, and HITCH/DROP controls now live natively
        // in Ground Control's SELECTION bar and UNIT SYSTEMS window.
    }

    internal bool IsMdsContainerTrailer(Unit? unit)
    {
        if (unit == null || unit.disabled || unit is GroundVehicle)
        {
            return false;
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        return localHq != null && ReferenceEquals(unit.NetworkHQ, localHq) && IsHaulableUnit(unit);
    }

    internal bool IsMdsTowingHauler(Unit? unit)
    {
        if (unit == null)
        {
            return false;
        }

        if (GcreNetworkSync.IsPureClient)
        {
            ClientMirroredHaulJob? cj = FindClientJobByUnit(unit);
            return cj != null && cj.Stage == HaulStage.Loaded && ReferenceEquals(cj.Hauler, unit);
        }

        HaulJob? job = FindJobByUnit(unit);
        return job != null && job.Stage == HaulStage.Loaded && ReferenceEquals(job.Hauler, unit);
    }

    internal bool IsMdsReservedHauler(Unit? unit)
    {
        if (unit == null || issuingInternalCommand)
        {
            return false;
        }

        if (GcreNetworkSync.IsPureClient)
        {
            ClientMirroredHaulJob? cj = FindClientJobByUnit(unit);
            return cj != null && cj.Stage != HaulStage.Loaded && ReferenceEquals(cj.Hauler, unit);
        }

        HaulJob? job = FindJobByUnit(unit);
        return job != null && job.Stage != HaulStage.Loaded && ReferenceEquals(job.Hauler, unit);
    }

    internal bool IsMdsTowedBy(GroundVehicle? tractor, Transform? obstacle)
    {
        if (tractor == null || obstacle == null)
        {
            return false;
        }

        if (GcreNetworkSync.IsPureClient)
        {
            ClientMirroredHaulJob? cj = FindClientJobByUnit(tractor);
            if (cj == null || !ReferenceEquals(cj.Hauler, tractor) || cj.Container == null)
            {
                return false;
            }

            return ReferenceEquals(obstacle, cj.Container.transform)
                || obstacle.IsChildOf(cj.Container.transform);
        }

        HaulJob? job = FindJobByUnit(tractor);
        if (job == null || !ReferenceEquals(job.Hauler, tractor) || job.Container == null)
        {
            return false;
        }

        return ReferenceEquals(obstacle, job.Container.transform)
            || obstacle.IsChildOf(job.Container.transform);
    }

    internal bool TryGetMdsTowJobState(Unit? unit, out bool towing)
    {
        towing = false;
        if (unit == null)
        {
            return false;
        }

        if (GcreNetworkSync.IsPureClient)
        {
            ClientMirroredHaulJob? cj = FindClientJobByUnit(unit);
            if (cj == null)
            {
                return false;
            }

            towing = cj.Stage == HaulStage.Loaded;
            return true;
        }

        HaulJob? job = FindJobByUnit(unit);
        if (job == null)
        {
            return false;
        }

        towing = job.Stage == HaulStage.Loaded;
        return true;
    }

    internal bool HasAvailableHaulerForMds(Unit? unit)
    {
        if (!IsMdsContainerTrailer(unit))
        {
            return false;
        }

        float now = Time.unscaledTime;
        if (ReferenceEquals(availabilityContainer, unit) && now < nextAvailabilityCheckAt)
        {
            return cachedHaulerAvailable;
        }

        availabilityContainer = unit;
        nextAvailabilityCheckAt = now + 0.5f;
        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        cachedHaulerAvailable = localHq != null && FindNearestIdleHauler(localHq, unit!.GlobalPosition(), out _) != null;
        return cachedHaulerAvailable;
    }

    internal bool TryBeginHitchFocusedFromNativeUi()
    {
        Unit? focused = GroundControlReflection.GetFocusedSelection();
        if (!IsMdsContainerTrailer(focused))
        {
            return false;
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        if (localHq == null)
        {
            SetFeedback("No local HQ.");
            return true;
        }

        if (GcreNetworkSync.IsPureClient)
        {
            if (!GcreNetworkSync.HostHasGcre)
            {
                SetFeedback("Host requires GroundControlRtsExpanded.");
                return true;
            }

            if (FindClientJobByUnit(focused!) != null)
            {
                SetFeedback("This container is already being hitched.");
                return true;
            }

            if (focused!.persistentID.IsValid)
            {
                GcreNetworkSync.RequestHitch(focused.persistentID.Id);
                SetFeedback("Hitch request sent to host.");
            }

            return true;
        }

        if (!((NetworkBehaviour)localHq).IsServer)
        {
            SetFeedback("Trailer hitching requires host authority.");
            return true;
        }

        if (FindJobByUnit(focused!) != null)
        {
            SetFeedback("This container is already being hitched.");
            return true;
        }

        GroundVehicle? nearestHauler = FindNearestIdleHauler(localHq, focused!.GlobalPosition(), out _);
        if (nearestHauler == null)
        {
            SetFeedback($"Needs idle HLT / MSV Tractor or Flatbed within {MaxTractorSearchMeters:0} m.");
            return true;
        }

        availabilityContainer = null;
        TryBeginLoad(focused!, nearestHauler, localHq);
        return true;
    }

    internal bool TryBeginHitchRemote(FactionHQ hq, uint containerPid, out string reply)
    {
        if (hq == null || !((NetworkBehaviour)hq).IsServer || containerPid == 0)
        {
            reply = "Invalid trailer hitch request.";
            return false;
        }

        var pid = new PersistentID { Id = containerPid };
        if (!pid.TryGetUnit(out Unit container)
            || container == null
            || container.disabled
            || container is GroundVehicle
            || !ReferenceEquals(container.NetworkHQ, hq)
            || !IsHaulableUnit(container))
        {
            reply = "Target container unavailable.";
            return false;
        }

        if (FindJobByUnit(container) != null)
        {
            reply = "This container is already being hitched.";
            return false;
        }

        GroundVehicle? nearestHauler = FindNearestIdleHauler(hq, container.GlobalPosition(), out _);
        if (nearestHauler == null)
        {
            reply = $"Needs idle HLT / MSV Tractor or Flatbed within {MaxTractorSearchMeters:0} m.";
            return false;
        }

        TryBeginLoad(container, nearestHauler, hq);
        reply = $"{GroundControlReflection.GetUnitLabel(nearestHauler)} dispatched to load container.";
        return true;
    }

    internal bool TryDropMdsTrailer(Unit? unit)
    {
        if (unit == null)
        {
            return false;
        }

        if (GcreNetworkSync.IsPureClient)
        {
            if (FindClientJobByUnit(unit) == null)
            {
                return false;
            }

            if (GcreNetworkSync.HostHasGcre && unit.persistentID.IsValid)
            {
                GcreNetworkSync.RequestDrop(unit.persistentID.Id);
                SetFeedback("Drop request sent to host.");
            }

            return true;
        }

        HaulJob? job = FindJobByUnit(unit);
        if (job == null)
        {
            return false;
        }

        bool isLocal = ReferenceEquals(job.Hq, GroundControlReflection.GetLocalHq());
        if (job.Stage == HaulStage.Loaded)
        {
            if (!TryResolveDryDropPosition(job, allowHaulerFallback: false, out _, out _))
            {
                if (isLocal)
                {
                    SetFeedback("Cannot drop container over water.");
                }

                return true;
            }

            UnloadJob(job);
            if (isLocal)
            {
                SetFeedback("Trailer dropped.");
            }
        }
        else
        {
            CancelApproachingOrLoadingJob(job);
            if (isLocal)
            {
                SetFeedback("Trailer pickup cancelled.");
            }
        }

        return true;
    }

    internal bool TryDropRemote(FactionHQ hq, uint unitPid, out string reply)
    {
        if (hq == null || !((NetworkBehaviour)hq).IsServer || unitPid == 0)
        {
            reply = string.Empty;
            return false;
        }

        var pid = new PersistentID { Id = unitPid };
        if (!pid.TryGetUnit(out Unit unit) || unit == null || !ReferenceEquals(unit.NetworkHQ, hq))
        {
            reply = string.Empty;
            return false;
        }

        HaulJob? job = FindJobByUnit(unit);
        if (job == null || !ReferenceEquals(job.Hq, hq))
        {
            reply = string.Empty;
            return false;
        }

        if (job.Stage == HaulStage.Loaded)
        {
            if (!TryResolveDryDropPosition(job, allowHaulerFallback: false, out _, out _))
            {
                reply = "Cannot drop container over water.";
                return true;
            }

            UnloadJob(job);
            reply = "Trailer dropped.";
        }
        else
        {
            CancelApproachingOrLoadingJob(job);
            reply = "Trailer pickup cancelled.";
        }

        return true;
    }

    private void CancelApproachingOrLoadingJob(HaulJob job)
    {
        jobs.Remove(job);
        if (job.Container != null)
        {
            MdsBridgeBootstrap.SetMdsContainerTransitState?.Invoke(job.Container, false);
        }

        if (job.Stage != HaulStage.Approaching)
        {
            RestoreContainerPhysics(job, placeOnGround: false);
        }

        if (job.Hauler != null && !job.Hauler.disabled)
        {
            MdsBridgeBootstrap.SetMdsHaulerOutriggersDeployed?.Invoke(job.Hauler, false);
            GroundControlReflection.SetUnitHoldPosition(job.Hauler, false);
            GroundControlReflection.SendRepairUnitHome(job.Hauler);
        }

        if (job.Container != null && job.Container.persistentID.IsValid)
        {
            uint haulerPid = job.Hauler != null && job.Hauler.persistentID.IsValid ? job.Hauler.persistentID.Id : 0u;
            GcreNetworkSync.BroadcastHaulDrop(
                job.Container.persistentID.Id,
                haulerPid,
                placedOnGround: false,
                job.Container.GlobalPosition(),
                job.Container.transform.eulerAngles.y);
        }
    }

    private void TickJobs(float now)
    {
        for (int i = jobs.Count - 1; i >= 0; i--)
        {
            HaulJob job = jobs[i];
            if (job.Container == null || job.Container.disabled || job.Hq == null || !((NetworkBehaviour)job.Hq).IsServer)
            {
                if (job.Hauler != null && !job.Hauler.disabled)
                {
                    MdsBridgeBootstrap.SetMdsHaulerOutriggersDeployed?.Invoke(job.Hauler, false);
                    GroundControlReflection.SetUnitHoldPosition(job.Hauler, false);
                }

                RestoreContainerPhysics(job, placeOnGround: false);
                if (job.Container != null && job.Container.persistentID.IsValid)
                {
                    uint deadContainerPid = job.Container.persistentID.Id;
                    relocatedBuildings.Remove(deadContainerPid);
                    uint haulerPid = job.Hauler != null && job.Hauler.persistentID.IsValid ? job.Hauler.persistentID.Id : 0u;
                    GcreNetworkSync.BroadcastHaulDrop(
                        deadContainerPid,
                        haulerPid,
                        placedOnGround: false,
                        job.Container.GlobalPosition(),
                        job.Container.transform.eulerAngles.y);
                }

                jobs.RemoveAt(i);
                continue;
            }

            if (job.Hauler == null || job.Hauler.disabled)
            {
                if (job.Stage != HaulStage.Approaching)
                {
                    DropContainerAtCurrentSpot(job);
                }
                else if (job.Container.persistentID.IsValid)
                {
                    uint haulerPid = job.Hauler != null && job.Hauler.persistentID.IsValid ? job.Hauler.persistentID.Id : 0u;
                    GcreNetworkSync.BroadcastHaulDrop(
                        job.Container.persistentID.Id,
                        haulerPid,
                        placedOnGround: false,
                        job.Container.GlobalPosition(),
                        job.Container.transform.eulerAngles.y);
                }

                jobs.RemoveAt(i);
                continue;
            }

            if (job.Stage == HaulStage.Approaching)
            {
                if (now - job.ApproachStartedAt >= MaxApproachTimeoutSeconds)
                {
                    string timeoutMsg = $"{GroundControlReflection.GetUnitLabel(job.Hauler)} could not reach {GroundControlReflection.GetUnitLabel(job.Container)} (approach timed out).";
                    if (ReferenceEquals(job.Hq, GroundControlReflection.GetLocalHq()))
                    {
                        SetFeedback(timeoutMsg);
                    }

                    AddonLog.Warn($"[Trailer] {timeoutMsg}");
                    CancelApproachingOrLoadingJob(job);
                    continue;
                }

                Vector3 delta = job.Hauler.transform.position - job.Container.transform.position;
                delta.y = 0f;
                float dist = delta.magnitude;
                bool stopped = Mathf.Abs(job.Hauler.speed) <= 1.0f;
                job.StoppedSince = stopped ? Mathf.Min(job.StoppedSince, now) : float.MaxValue;

                if (dist <= HitchArrivalMeters || (dist <= StalledHitchRadiusMeters && (now - job.StoppedSince) >= 4.0f))
                {
                    BeginLoadingOntoBed(job, now);
                }
                else if (now >= job.NextOrderAt)
                {
                    job.NextOrderAt = now + 2.5f;
                    GlobalPosition pickupPt = ComputeApproachPoint(job.Hauler, job.Container);
                    IssueHaulerDestination(job.Hauler, pickupPt);
                }

                continue;
            }

            if (job.Stage == HaulStage.Loading)
            {
                float t = Mathf.Clamp01((now - job.LoadStartedAt) / job.LoadDurationSeconds);
                float smooth = Mathf.SmoothStep(0f, 1f, t);
                Vector3 targetWorld = job.Hauler.transform.TransformPoint(job.BedOffset);
                Quaternion targetRot = job.Hauler.transform.rotation;

                Vector3 pos = Vector3.Lerp(job.LoadStartPos, targetWorld, smooth);
                Quaternion rot = Quaternion.Slerp(job.LoadStartRot, targetRot, smooth);
                ApplyContainerTransform(job.Container, pos, rot);

                if (t >= 1f)
                {
                    job.Stage = HaulStage.Loaded;
                    MdsBridgeBootstrap.SetMdsHaulerOutriggersDeployed?.Invoke(job.Hauler, false);
                    GroundControlReflection.SetUnitHoldPosition(job.Hauler, false);
                    BroadcastJobState(job, now);
                    string msg = $"{GroundControlReflection.GetUnitLabel(job.Container)} loaded onto {GroundControlReflection.GetUnitLabel(job.Hauler)}.";
                    if (ReferenceEquals(job.Hq, GroundControlReflection.GetLocalHq()))
                    {
                        SetFeedback(msg);
                    }

                    AddonLog.Info($"[Trailer] {msg}");
                }

                continue;
            }

            // HaulStage.Loaded: lock tightly onto flatbed bed every frame
            Vector3 bedWorld = job.Hauler.transform.TransformPoint(job.BedOffset);
            ApplyContainerTransform(job.Container, bedWorld, job.Hauler.transform.rotation);
        }
    }

    private static void BroadcastJobState(HaulJob job, float now)
    {
        if (job.Container == null || !job.Container.persistentID.IsValid || job.Hauler == null || !job.Hauler.persistentID.IsValid)
        {
            return;
        }

        float elapsed = job.Stage == HaulStage.Loading ? Mathf.Max(0f, now - job.LoadStartedAt) : 0f;
        GcreNetworkSync.BroadcastHaulSync(
            job.Container.persistentID.Id,
            job.Hauler.persistentID.Id,
            (int)job.Stage,
            job.BedOffset,
            job.LoadDurationSeconds,
            elapsed);
    }

    private void TryBeginLoad(Unit container, GroundVehicle hauler, FactionHQ hq)
    {
        if (FindJobByUnit(container) != null || FindJobByUnit(hauler) != null)
        {
            if (ReferenceEquals(hq, GroundControlReflection.GetLocalHq()))
            {
                SetFeedback("Unit is already involved in a trailer task.");
            }

            return;
        }

        float now = Time.time;
        var job = new HaulJob
        {
            Container = container,
            Hauler = hauler,
            Hq = hq,
            Stage = HaulStage.Approaching,
            ApproachStartedAt = now,
            NextOrderAt = 0f,
        };
        jobs.Add(job);

        GlobalPosition pickupPt = ComputeApproachPoint(hauler, container);
        IssueHaulerDestination(hauler, pickupPt);
        BroadcastJobState(job, now);
        if (ReferenceEquals(hq, GroundControlReflection.GetLocalHq()))
        {
            SetFeedback($"{GroundControlReflection.GetUnitLabel(hauler)} dispatched to load container.");
        }

        AddonLog.Info($"[Trailer] Dispatched '{GroundControlReflection.GetUnitLabel(hauler)}' to load '{GroundControlReflection.GetUnitLabel(container)}'.");
    }

    private void BeginLoadingOntoBed(HaulJob job, float now)
    {
        GroundControlReflection.SetUnitHoldPosition(job.Hauler, true);
        IssueHaulerDestination(job.Hauler, job.Hauler.GlobalPosition());

        GlobalPosition groundAtContainer = GroundControlReflection.SnapToTerrain(job.Container.GlobalPosition());
        job.GroundClearanceY = Mathf.Clamp(job.Container.transform.position.y - groundAtContainer.ToLocalPosition().y, 0f, 3f);

        IgnoreContainerHaulerCollisions(job.Container, job.Hauler, job.IgnoredCollisionPairs);
        if (job.Container.rb != null)
        {
            job.WasKinematic = job.Container.rb.isKinematic;
            job.Container.rb.isKinematic = true;
        }

        MdsBridgeBootstrap.SetMdsContainerTransitState?.Invoke(job.Container, true);

        float halfHauler = Mathf.Max(2.5f, (job.Hauler.definition?.length ?? 9f) * 0.5f);
        float halfCargo = Mathf.Max(2.0f, (job.Container.definition?.length ?? 6.06f) * 0.5f);
        BoxCollider? box = job.Container.GetComponent<BoxCollider>();
        float localBottomY = (job.Container is Container && box != null) ? (box.center.y - (box.size.y * 0.5f)) : 0f;
        float standardOffsetZ = -Mathf.Max(0f, halfHauler - halfCargo);
        float maxFrontZ = Mathf.Max(0.5f, halfHauler - 3.65f);
        float bedOffsetZ = Mathf.Min(standardOffsetZ, maxFrontZ - halfCargo);
        Vector3? customSeat = MdsBridgeBootstrap.TryGetMdsHaulerBedOffset?.Invoke(job.Hauler);
        job.BedOffset = customSeat.HasValue
            ? new Vector3(customSeat.Value.x, customSeat.Value.y - localBottomY, customSeat.Value.z)
            : new Vector3(0f, BedHeightMeters - localBottomY, bedOffsetZ);
        MdsBridgeBootstrap.SetMdsHaulerOutriggersDeployed?.Invoke(job.Hauler, true);
        job.LoadStartPos = job.Container.transform.position;
        job.LoadStartRot = job.Container.transform.rotation;
        job.LoadStartedAt = now;
        job.Stage = HaulStage.Loading;
        BroadcastJobState(job, now);
    }

    private void UnloadJob(HaulJob job)
    {
        jobs.Remove(job);
        DropContainerAtCurrentSpot(job);

        string msg = $"{GroundControlReflection.GetUnitLabel(job.Container)} unloaded & deploying.";
        if (ReferenceEquals(job.Hq, GroundControlReflection.GetLocalHq()))
        {
            SetFeedback(msg);
        }

        AddonLog.Info($"[Trailer] {msg}");
    }

    private void DropContainerAtCurrentSpot(HaulJob job)
    {
        if (job.Container == null || job.Container.disabled)
        {
            return;
        }

        uint haulerPid = job.Hauler != null && job.Hauler.persistentID.IsValid ? job.Hauler.persistentID.Id : 0u;
        (GlobalPosition dropPos, float yaw) = RestoreContainerPhysics(job, placeOnGround: true);
        MdsBridgeBootstrap.SetMdsHaulerOutriggersDeployed?.Invoke(job.Hauler, false);
        MdsBridgeBootstrap.SetMdsContainerTransitState?.Invoke(job.Container, false);
        MdsBridgeBootstrap.UpdateMdsSitePosition?.Invoke(job.Container, dropPos);

        if (job.Container.persistentID.IsValid)
        {
            uint pid = job.Container.persistentID.Id;
            GcreNetworkSync.BroadcastHaulDrop(pid, haulerPid, placedOnGround: true, dropPos, yaw);
            if (job.Container is Building)
            {
                relocatedBuildings[pid] = (dropPos, yaw);
                GcreNetworkSync.BroadcastRelocatedBuilding(pid, dropPos, yaw);
            }
        }
    }

    private static bool TryResolveDryDropPosition(
        HaulJob job,
        bool allowHaulerFallback,
        out GlobalPosition snappedDry,
        out float yawDeg)
    {
        snappedDry = default;
        yawDeg = job.Hauler != null ? job.Hauler.transform.eulerAngles.y : job.Container.transform.eulerAngles.y;

        if (job.Hauler != null && !job.Hauler.disabled)
        {
            float halfHaulerDrop = Mathf.Max(3.0f, (job.Hauler.definition?.length ?? 9f) * 0.5f);
            float halfCargoDrop = Mathf.Max(2.0f, (job.Container.definition?.length ?? 6.06f) * 0.5f);
            float dropBackDist = Mathf.Max(7.5f, halfHaulerDrop + halfCargoDrop + 1.0f);
            Vector3 haulerPos = job.Hauler.transform.position;
            Vector3 fwd = job.Hauler.transform.forward;
            Vector3 right = job.Hauler.transform.right;

            Vector3[] candidates =
            {
                haulerPos - fwd * dropBackDist,
                haulerPos + fwd * dropBackDist,
                haulerPos + right * 6.0f,
                haulerPos - right * 6.0f,
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                GlobalPosition candidate = GroundControlReflection.SnapToTerrain(candidates[i].ToGlobalPosition());
                if (!GroundControlReflection.IsBelowSeaLevel(candidate))
                {
                    snappedDry = candidate;
                    return true;
                }
            }

            if (allowHaulerFallback)
            {
                GlobalPosition haulerSnapped = GroundControlReflection.SnapToTerrain(job.Hauler.GlobalPosition());
                snappedDry = !GroundControlReflection.IsBelowSeaLevel(haulerSnapped)
                    ? haulerSnapped
                    : GroundControlReflection.SnapToTerrain(candidates[0].ToGlobalPosition());
                return !GroundControlReflection.IsBelowSeaLevel(snappedDry);
            }

            return false;
        }

        GlobalPosition containerSnapped = GroundControlReflection.SnapToTerrain(job.Container.GlobalPosition());
        snappedDry = containerSnapped;
        return !GroundControlReflection.IsBelowSeaLevel(containerSnapped);
    }

    private static (GlobalPosition Pos, float Yaw) RestoreContainerPhysics(HaulJob job, bool placeOnGround)
    {
        if (job.Container == null)
        {
            RestoreIgnoredCollisions(job.IgnoredCollisionPairs);
            return default;
        }

        GlobalPosition resultPos = job.Container.GlobalPosition();
        float resultYaw = job.Container.transform.eulerAngles.y;

        if (placeOnGround)
        {
            TryResolveDryDropPosition(job, allowHaulerFallback: true, out GlobalPosition snapped, out resultYaw);
            BoxCollider? box = job.Container.GetComponent<BoxCollider>();
            float clearanceY = (job.Container is Container && box != null)
                ? Mathf.Max(0f, -(box.center.y - (box.size.y * 0.5f)))
                : job.GroundClearanceY;
            Vector3 worldDrop = snapped.ToLocalPosition() + Vector3.up * clearanceY;
            resultPos = worldDrop.ToGlobalPosition();
            Quaternion dropRot = Quaternion.Euler(0f, resultYaw, 0f);
            ApplyContainerTransform(job.Container, worldDrop, dropRot);

            if (((NetworkBehaviour)job.Container).IsServer)
            {
                job.Container.NetworkstartPosition = resultPos;
                job.Container.NetworkstartRotation = dropRot;
            }
        }

        Physics.SyncTransforms();
        RestoreIgnoredCollisions(job.IgnoredCollisionPairs);
        if (job.Container.rb != null)
        {
            job.Container.rb.isKinematic = job.WasKinematic;
            job.Container.rb.velocity = Vector3.zero;
            job.Container.rb.angularVelocity = Vector3.zero;
        }

        Physics.SyncTransforms();
        return (resultPos, resultYaw);
    }

    private static void ApplyContainerTransform(Unit unit, Vector3 worldPos, Quaternion worldRot)
    {
        if (unit.rb != null && unit.rb.isKinematic)
        {
            unit.rb.MovePosition(worldPos);
            unit.rb.MoveRotation(worldRot);
        }

        unit.transform.SetPositionAndRotation(worldPos, worldRot);
    }

    internal void ApplyClientHaulSync(
        uint containerPid,
        uint haulerPid,
        int stageInt,
        Vector3 bedOffset,
        float loadDur,
        float loadElapsed)
    {
        if (!GcreNetworkSync.IsPureClient || containerPid == 0 || haulerPid == 0)
        {
            return;
        }

        HaulStage stage = Enum.IsDefined(typeof(HaulStage), stageInt) ? (HaulStage)stageInt : HaulStage.Approaching;
        if (!clientJobs.TryGetValue(containerPid, out ClientMirroredHaulJob? cj))
        {
            cj = new ClientMirroredHaulJob
            {
                ContainerPid = containerPid,
                HaulerPid = haulerPid,
                CreatedAt = Time.time,
            };
            clientJobs[containerPid] = cj;
        }

        cj.HaulerPid = haulerPid;
        cj.Stage = stage;
        cj.BedOffset = bedOffset;
        cj.LoadDurationSeconds = Mathf.Max(0.25f, loadDur);

        ResolveClientJobUnits(cj);
        if (stage == HaulStage.Loading)
        {
            cj.LoadStartedAt = Time.time - Mathf.Max(0f, loadElapsed);
            if (cj.Container != null)
            {
                cj.LoadStartPos = cj.Container.transform.position;
                cj.LoadStartRot = cj.Container.transform.rotation;
            }

            LockClientContainerPhysics(cj);
        }
        else if (stage == HaulStage.Loaded)
        {
            LockClientContainerPhysics(cj);
            if (cj.Hauler != null)
            {
                MdsBridgeBootstrap.SetMdsHaulerOutriggersDeployed?.Invoke(cj.Hauler, false);
            }
        }
    }

    internal void ApplyClientHaulDrop(
        uint containerPid,
        uint haulerPid,
        bool placedOnGround,
        GlobalPosition dropPos,
        float yaw)
    {
        if (!GcreNetworkSync.IsPureClient || containerPid == 0)
        {
            return;
        }

        if (clientJobs.TryGetValue(containerPid, out ClientMirroredHaulJob? cj))
        {
            clientJobs.Remove(containerPid);
            ResolveClientJobUnits(cj);
            if (placedOnGround && cj.Container != null)
            {
                ApplyContainerTransform(cj.Container, dropPos.ToLocalPosition(), Quaternion.Euler(0f, yaw, 0f));
            }

            RestoreClientContainerPhysics(cj);
            if (cj.Hauler != null)
            {
                MdsBridgeBootstrap.SetMdsHaulerOutriggersDeployed?.Invoke(cj.Hauler, false);
            }
        }
        else if (placedOnGround && new PersistentID { Id = containerPid }.TryGetUnit(out Unit container) && container != null)
        {
            ApplyContainerTransform(container, dropPos.ToLocalPosition(), Quaternion.Euler(0f, yaw, 0f));
            MdsBridgeBootstrap.SetMdsContainerTransitState?.Invoke(container, false);
            Physics.SyncTransforms();
        }
    }

    internal void ApplyClientRelocatedBuilding(uint pid, GlobalPosition pos, float yaw)
    {
        if (!GcreNetworkSync.IsPureClient || pid == 0)
        {
            return;
        }

        if (new PersistentID { Id = pid }.TryGetUnit(out Unit unit) && unit != null)
        {
            pendingClientRelocations.Remove(pid);
            if (!unit.disabled)
            {
                ApplyContainerTransform(unit, pos.ToLocalPosition(), Quaternion.Euler(0f, yaw, 0f));
                Physics.SyncTransforms();
            }

            return;
        }

        pendingClientRelocations[pid] = (pos, yaw, Time.time);
    }

    private void TickClientMirroredJobs(float now)
    {
        if (pendingClientRelocations.Count > 0)
        {
            relocationScratch.Clear();
            foreach (KeyValuePair<uint, (GlobalPosition Pos, float Yaw, float AddedAt)> kv in pendingClientRelocations)
            {
                if (new PersistentID { Id = kv.Key }.TryGetUnit(out Unit unit) && unit != null)
                {
                    if (!unit.disabled)
                    {
                        ApplyContainerTransform(unit, kv.Value.Pos.ToLocalPosition(), Quaternion.Euler(0f, kv.Value.Yaw, 0f));
                        Physics.SyncTransforms();
                    }

                    relocationScratch.Add(kv.Key);
                }
                else if (now - kv.Value.AddedAt > ClientRelocationTimeoutSeconds)
                {
                    relocationScratch.Add(kv.Key);
                }
            }

            for (int i = 0; i < relocationScratch.Count; i++)
            {
                pendingClientRelocations.Remove(relocationScratch[i]);
            }
        }

        if (clientJobs.Count == 0)
        {
            return;
        }

        clientCleanupScratch.Clear();
        foreach (KeyValuePair<uint, ClientMirroredHaulJob> kv in clientJobs)
        {
            ClientMirroredHaulJob cj = kv.Value;
            ResolveClientJobUnits(cj);
            if ((cj.Container != null && cj.Container.disabled) || (cj.Hauler != null && cj.Hauler.disabled))
            {
                clientCleanupScratch.Add(kv.Key);
                continue;
            }

            if (cj.Container == null || cj.Hauler == null)
            {
                if (cj.PhysicsLocked || now - cj.CreatedAt > ClientUnresolvedTimeoutSeconds)
                {
                    clientCleanupScratch.Add(kv.Key);
                }

                continue;
            }

            if (cj.Stage == HaulStage.Approaching)
            {
                continue;
            }

            LockClientContainerPhysics(cj);
            if (cj.IgnoredCollisionPairs.Count == 0)
            {
                IgnoreContainerHaulerCollisions(cj.Container, cj.Hauler, cj.IgnoredCollisionPairs);
            }

            if (cj.Stage == HaulStage.Loading)
            {
                float t = Mathf.Clamp01((now - cj.LoadStartedAt) / cj.LoadDurationSeconds);
                float smooth = Mathf.SmoothStep(0f, 1f, t);
                Vector3 targetWorld = cj.Hauler.transform.TransformPoint(cj.BedOffset);
                Quaternion targetRot = cj.Hauler.transform.rotation;
                ApplyContainerTransform(
                    cj.Container,
                    Vector3.Lerp(cj.LoadStartPos, targetWorld, smooth),
                    Quaternion.Slerp(cj.LoadStartRot, targetRot, smooth));

                if (t >= 1f)
                {
                    cj.Stage = HaulStage.Loaded;
                    MdsBridgeBootstrap.SetMdsHaulerOutriggersDeployed?.Invoke(cj.Hauler, false);
                }
            }
            else if (cj.Stage == HaulStage.Loaded)
            {
                Vector3 bedWorld = cj.Hauler.transform.TransformPoint(cj.BedOffset);
                ApplyContainerTransform(cj.Container, bedWorld, cj.Hauler.transform.rotation);
            }
        }

        for (int i = 0; i < clientCleanupScratch.Count; i++)
        {
            uint key = clientCleanupScratch[i];
            if (clientJobs.TryGetValue(key, out ClientMirroredHaulJob? dead))
            {
                if (dead.Hauler != null)
                {
                    MdsBridgeBootstrap.SetMdsHaulerOutriggersDeployed?.Invoke(dead.Hauler, false);
                }

                RestoreClientContainerPhysics(dead);
                clientJobs.Remove(key);
            }
        }
    }

    private static void ResolveClientJobUnits(ClientMirroredHaulJob cj)
    {
        if (cj.Container == null && cj.ContainerPid != 0 && new PersistentID { Id = cj.ContainerPid }.TryGetUnit(out Unit c))
        {
            cj.Container = c;
        }

        if (cj.Hauler == null && cj.HaulerPid != 0 && new PersistentID { Id = cj.HaulerPid }.TryGetUnit(out Unit h) && h is GroundVehicle gv)
        {
            cj.Hauler = gv;
        }
    }

    private static void LockClientContainerPhysics(ClientMirroredHaulJob cj)
    {
        if (cj.PhysicsLocked || cj.Container == null)
        {
            return;
        }

        cj.PhysicsLocked = true;
        if (cj.LoadStartPos == Vector3.zero)
        {
            cj.LoadStartPos = cj.Container.transform.position;
            cj.LoadStartRot = cj.Container.transform.rotation;
        }

        if (cj.Hauler != null)
        {
            IgnoreContainerHaulerCollisions(cj.Container, cj.Hauler, cj.IgnoredCollisionPairs);
        }

        if (cj.Container.rb != null)
        {
            cj.WasKinematic = cj.Container.rb.isKinematic;
            cj.Container.rb.isKinematic = true;
        }

        MdsBridgeBootstrap.SetMdsContainerTransitState?.Invoke(cj.Container, true);
        if (cj.Hauler != null && cj.Stage == HaulStage.Loading)
        {
            MdsBridgeBootstrap.SetMdsHaulerOutriggersDeployed?.Invoke(cj.Hauler, true);
        }
    }

    private static void RestoreClientContainerPhysics(ClientMirroredHaulJob cj)
    {
        RestoreIgnoredCollisions(cj.IgnoredCollisionPairs);
        if (cj.Container == null)
        {
            cj.PhysicsLocked = false;
            return;
        }

        if (cj.Container.rb != null)
        {
            cj.Container.rb.isKinematic = cj.WasKinematic;
            cj.Container.rb.velocity = Vector3.zero;
            cj.Container.rb.angularVelocity = Vector3.zero;
        }

        if (cj.PhysicsLocked)
        {
            MdsBridgeBootstrap.SetMdsContainerTransitState?.Invoke(cj.Container, false);
        }

        cj.PhysicsLocked = false;
        Physics.SyncTransforms();
    }

    private static void IgnoreContainerHaulerCollisions(
        Unit container,
        GroundVehicle hauler,
        List<(Collider ContainerCol, Collider HaulerCol)> ignoredPairs)
    {
        if (container == null || hauler == null)
        {
            return;
        }

        Collider[] containerColliders = container.GetComponentsInChildren<Collider>(true);
        Collider[] haulerColliders = hauler.GetComponentsInChildren<Collider>(true);
        for (int c = 0; c < containerColliders.Length; c++)
        {
            Collider? cCol = containerColliders[c];
            if (cCol == null || !cCol.enabled || !cCol.gameObject.activeInHierarchy)
            {
                continue;
            }

            for (int h = 0; h < haulerColliders.Length; h++)
            {
                Collider? hCol = haulerColliders[h];
                if (hCol == null || !hCol.enabled || !hCol.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Physics.IgnoreCollision(cCol, hCol, true);
                ignoredPairs.Add((cCol, hCol));
            }
        }
    }

    private static void RestoreIgnoredCollisions(List<(Collider ContainerCol, Collider HaulerCol)> ignoredPairs)
    {
        for (int i = 0; i < ignoredPairs.Count; i++)
        {
            (Collider cCol, Collider hCol) = ignoredPairs[i];
            if (cCol != null
                && hCol != null
                && cCol.enabled
                && hCol.enabled
                && cCol.gameObject.activeInHierarchy
                && hCol.gameObject.activeInHierarchy)
            {
                Physics.IgnoreCollision(cCol, hCol, false);
            }
        }

        ignoredPairs.Clear();
    }

    private ClientMirroredHaulJob? FindClientJobByUnit(Unit unit)
    {
        foreach (ClientMirroredHaulJob cj in clientJobs.Values)
        {
            ResolveClientJobUnits(cj);
            if (ReferenceEquals(cj.Container, unit) || ReferenceEquals(cj.Hauler, unit))
            {
                return cj;
            }
        }

        return null;
    }

    private static GlobalPosition ComputeApproachPoint(GroundVehicle hauler, Unit container)
    {
        Vector3 dir = hauler.transform.position - container.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.1f)
        {
            dir = container.transform.forward;
        }

        Vector3 targetWorld = container.transform.position + dir.normalized * StagingDistanceMeters;
        return GroundControlReflection.SnapToTerrain(targetWorld.ToGlobalPosition());
    }

    private HaulJob? FindJobByUnit(Unit unit)
    {
        for (int i = 0; i < jobs.Count; i++)
        {
            if (ReferenceEquals(jobs[i].Container, unit) || ReferenceEquals(jobs[i].Hauler, unit))
            {
                return jobs[i];
            }
        }

        return null;
    }

    private bool IsHaulableUnit(Unit unit)
    {
        if (unit == null || unit.disabled || unit.IsSlung())
        {
            return false;
        }

        if (MdsBridgeBootstrap.IsMdsHaulableContainer?.Invoke(unit) == true)
        {
            return true;
        }

        if (unit is Container)
        {
            return true;
        }

        return false;
    }

    private GroundVehicle? FindNearestIdleHauler(FactionHQ hq, GlobalPosition containerPos, out float bestMeters)
    {
        bestMeters = float.MaxValue;
        GroundVehicle? best = null;
        float maxSq = MaxTractorSearchMeters * MaxTractorSearchMeters;

        for (int i = 0; i < UnitRegistry.allUnits.Count; i++)
        {
            if (UnitRegistry.allUnits[i] is not GroundVehicle gv
                || gv.disabled
                || !ReferenceEquals(gv.NetworkHQ, hq)
                || !IsHaulerVehicle(gv)
                || Mathf.Abs(gv.speed) > 4.0f
                || FindJobByUnit(gv) != null)
            {
                continue;
            }

            GlobalPosition haulerPos = gv.GlobalPosition();
            float dSq = FastMath.SquareDistance(haulerPos, containerPos);
            if (dSq <= maxSq && dSq < bestMeters && GroundControlReflection.CanDriveBetween(haulerPos, containerPos))
            {
                bestMeters = dSq;
                best = gv;
            }
        }

        if (best != null)
        {
            bestMeters = Mathf.Sqrt(bestMeters);
        }

        return best;
    }

    private static bool IsHaulerVehicle(GroundVehicle vehicle)
    {
        if (MdsBridgeBootstrap.IsMdsContainerHauler?.Invoke(vehicle) == true)
        {
            return true;
        }

        string unitName = vehicle.definition?.unitName ?? vehicle.unitName ?? string.Empty;
        string jsonKey = vehicle.definition?.jsonKey ?? string.Empty;

        return unitName.EndsWith("Tractor", StringComparison.OrdinalIgnoreCase)
            || unitName.EndsWith("Flatbed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(jsonKey, "HLT-T", StringComparison.OrdinalIgnoreCase)
            || string.Equals(jsonKey, "HLT-L", StringComparison.OrdinalIgnoreCase)
            || string.Equals(jsonKey, "Truck2-T", StringComparison.OrdinalIgnoreCase)
            || string.Equals(jsonKey, "Truck2-L", StringComparison.OrdinalIgnoreCase);
    }

    private void IssueHaulerDestination(GroundVehicle hauler, GlobalPosition destination)
    {
        issuingInternalCommand = true;
        try
        {
            GroundControlReflection.TrySetDestination(hauler, destination);
        }
        finally
        {
            issuingInternalCommand = false;
        }
    }

    private void SetFeedback(string text)
    {
        GroundControlReflection.SetMobileEmplacementStatus(text);
    }
}
