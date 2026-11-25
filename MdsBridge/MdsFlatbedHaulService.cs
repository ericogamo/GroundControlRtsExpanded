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
        internal float NextOrderAt { get; set; }
        internal Collider[] SavedColliders { get; set; } = Array.Empty<Collider>();
        internal bool WasKinematic { get; set; }
    }

    private const float MaxTractorSearchMeters = 300f;
    private const float StagingDistanceMeters = 14f;
    private const float HitchArrivalMeters = 18f;
    private const float BedHeightMeters = 0.82f;

    internal static MdsFlatbedHaulService Instance { get; } = new();

    private readonly List<HaulJob> jobs = new();
    private float nextAirdropRetryAt;
    private Unit? availabilityContainer;
    private bool cachedHaulerAvailable;
    private float nextAvailabilityCheckAt;

    private MdsFlatbedHaulService()
    {
    }

    internal bool ContainsScreenPoint(Vector2 screenPoint) => false;

    internal bool IsMouseOverUi() => false;

    public void ResetSession()
    {
        for (int i = jobs.Count - 1; i >= 0; i--)
        {
            RestoreContainerPhysics(jobs[i], placeOnGround: false);
        }

        jobs.Clear();
        nextAirdropRetryAt = 0f;
        availabilityContainer = null;
        cachedHaulerAvailable = false;
        nextAvailabilityCheckAt = 0f;
    }

    public void Tick()
    {
        if (!AddonConfig.ModEnabled.Value)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (now >= nextAirdropRetryAt && MdsBridgeBootstrap.EnsureAirdropMountsInjectedAction != null && Encyclopedia.i != null)
        {
            nextAirdropRetryAt = now + 2.0f;
            MdsBridgeBootstrap.EnsureAirdropMountsInjectedAction();
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

        HaulJob? job = FindJobByUnit(unit);
        return job != null && job.Stage == HaulStage.Loaded && ReferenceEquals(job.Hauler, unit);
    }

    internal bool IsMdsReservedHauler(Unit? unit)
    {
        if (unit == null)
        {
            return false;
        }

        HaulJob? job = FindJobByUnit(unit);
        return job != null && job.Stage != HaulStage.Loaded && ReferenceEquals(job.Hauler, unit);
    }

    internal bool TryGetMdsTowJobState(Unit? unit, out bool towing)
    {
        towing = false;
        if (unit == null)
        {
            return false;
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
        if (localHq == null || !((NetworkBehaviour)localHq).IsServer)
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

    internal bool TryDropMdsTrailer(Unit? unit)
    {
        if (unit == null)
        {
            return false;
        }

        HaulJob? job = FindJobByUnit(unit);
        if (job == null)
        {
            return false;
        }

        if (job.Stage == HaulStage.Loaded)
        {
            UnloadJob(job);
            SetFeedback("Trailer dropped.");
        }
        else
        {
            jobs.Remove(job);
            GroundControlReflection.SendRepairUnitHome(job.Hauler);
            SetFeedback("Trailer pickup cancelled.");
        }

        return true;
    }

    private void TickJobs(float now)
    {
        for (int i = jobs.Count - 1; i >= 0; i--)
        {
            HaulJob job = jobs[i];
            if (job.Container == null || job.Container.disabled || job.Hq == null || !((NetworkBehaviour)job.Hq).IsServer)
            {
                RestoreContainerPhysics(job, placeOnGround: false);
                jobs.RemoveAt(i);
                continue;
            }

            if (job.Hauler == null || job.Hauler.disabled)
            {
                if (job.Stage != HaulStage.Approaching)
                {
                    DropContainerAtCurrentSpot(job);
                }

                jobs.RemoveAt(i);
                continue;
            }

            if (job.Stage == HaulStage.Approaching)
            {
                float dist = FastMath.Distance(job.Hauler.GlobalPosition(), job.Container.GlobalPosition());
                if (dist <= HitchArrivalMeters)
                {
                    BeginLoadingOntoBed(job, now);
                }
                else if (now >= job.NextOrderAt)
                {
                    job.NextOrderAt = now + 2.5f;
                    GlobalPosition pickupPt = ComputeApproachPoint(job.Hauler, job.Container);
                    GroundControlReflection.TrySetDestination(job.Hauler, pickupPt);
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
                    string msg = $"{GroundControlReflection.GetUnitLabel(job.Container)} loaded onto {GroundControlReflection.GetUnitLabel(job.Hauler)}.";
                    SetFeedback(msg);
                    AddonLog.Info($"[Trailer] {msg}");
                }

                continue;
            }

            // HaulStage.Loaded: lock tightly onto flatbed bed every frame
            Vector3 bedWorld = job.Hauler.transform.TransformPoint(job.BedOffset);
            ApplyContainerTransform(job.Container, bedWorld, job.Hauler.transform.rotation);
        }
    }

    private void TryBeginLoad(Unit container, GroundVehicle hauler, FactionHQ hq)
    {
        if (FindJobByUnit(container) != null || FindJobByUnit(hauler) != null)
        {
            SetFeedback("Unit is already involved in a trailer task.");
            return;
        }

        var job = new HaulJob
        {
            Container = container,
            Hauler = hauler,
            Hq = hq,
            Stage = HaulStage.Approaching,
            NextOrderAt = 0f,
        };
        jobs.Add(job);

        GlobalPosition pickupPt = ComputeApproachPoint(hauler, container);
        GroundControlReflection.TrySetDestination(hauler, pickupPt);
        SetFeedback($"{GroundControlReflection.GetUnitLabel(hauler)} dispatched to load container.");
        AddonLog.Info($"[Trailer] Dispatched '{GroundControlReflection.GetUnitLabel(hauler)}' to load '{GroundControlReflection.GetUnitLabel(container)}'.");
    }

    private void BeginLoadingOntoBed(HaulJob job, float now)
    {
        GroundControlReflection.TrySetDestination(job.Hauler, job.Hauler.GlobalPosition());

        GlobalPosition groundAtContainer = GroundControlReflection.SnapToTerrain(job.Container.GlobalPosition());
        job.GroundClearanceY = Mathf.Clamp(job.Container.transform.position.y - groundAtContainer.ToLocalPosition().y, 0f, 3f);

        Collider[] allColliders = job.Container.GetComponentsInChildren<Collider>(true);
        var enabledList = new List<Collider>(allColliders.Length);
        for (int i = 0; i < allColliders.Length; i++)
        {
            if (allColliders[i] != null && allColliders[i].enabled)
            {
                allColliders[i].enabled = false;
                enabledList.Add(allColliders[i]);
            }
        }

        job.SavedColliders = enabledList.ToArray();
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
        job.BedOffset = new Vector3(0f, BedHeightMeters - localBottomY, bedOffsetZ);
        job.LoadStartPos = job.Container.transform.position;
        job.LoadStartRot = job.Container.transform.rotation;
        job.LoadStartedAt = now;
        job.Stage = HaulStage.Loading;
    }

    private void UnloadJob(HaulJob job)
    {
        jobs.Remove(job);
        DropContainerAtCurrentSpot(job);

        string msg = $"{GroundControlReflection.GetUnitLabel(job.Container)} unloaded & deploying.";
        SetFeedback(msg);
        AddonLog.Info($"[Trailer] {msg}");
    }

    private void DropContainerAtCurrentSpot(HaulJob job)
    {
        if (job.Container == null || job.Container.disabled)
        {
            return;
        }

        RestoreContainerPhysics(job, placeOnGround: true);
        MdsBridgeBootstrap.SetMdsContainerTransitState?.Invoke(job.Container, false);
    }

    private static void RestoreContainerPhysics(HaulJob job, bool placeOnGround)
    {
        if (job.Container == null)
        {
            return;
        }

        if (placeOnGround)
        {
            float halfHaulerDrop = Mathf.Max(3.0f, (job.Hauler?.definition?.length ?? 9f) * 0.5f);
            float halfCargoDrop = Mathf.Max(2.0f, (job.Container.definition?.length ?? 6.06f) * 0.5f);
            float dropBackDist = Mathf.Max(7.5f, halfHaulerDrop + halfCargoDrop + 1.0f);
            Vector3 dropBase = job.Hauler != null && !job.Hauler.disabled
                ? job.Hauler.transform.position - job.Hauler.transform.forward * dropBackDist
                : job.Container.transform.position;

            GlobalPosition snapped = GroundControlReflection.SnapToTerrain(dropBase.ToGlobalPosition());
            BoxCollider? box = job.Container.GetComponent<BoxCollider>();
            float clearanceY = (job.Container is Container && box != null)
                ? Mathf.Max(0f, -(box.center.y - (box.size.y * 0.5f)))
                : job.GroundClearanceY;
            Vector3 worldDrop = snapped.ToLocalPosition() + Vector3.up * clearanceY;
            float yaw = job.Hauler != null ? job.Hauler.transform.eulerAngles.y : job.Container.transform.eulerAngles.y;
            ApplyContainerTransform(job.Container, worldDrop, Quaternion.Euler(0f, yaw, 0f));
        }

        for (int i = 0; i < job.SavedColliders.Length; i++)
        {
            Collider? col = job.SavedColliders[i];
            if (col != null)
            {
                col.enabled = true;
            }
        }

        job.SavedColliders = Array.Empty<Collider>();
        if (job.Container.rb != null)
        {
            job.Container.rb.isKinematic = job.WasKinematic;
            job.Container.rb.velocity = Vector3.zero;
            job.Container.rb.angularVelocity = Vector3.zero;
        }
    }

    private static void ApplyContainerTransform(Unit unit, Vector3 worldPos, Quaternion worldRot)
    {
        if (unit.rb != null && unit.rb.isKinematic)
        {
            unit.rb.MovePosition(worldPos);
            unit.rb.MoveRotation(worldRot);
        }

        unit.transform.SetPositionAndRotation(worldPos, worldRot);
        unit.NetworkstartPosition = worldPos.ToGlobalPosition();
        unit.NetworkstartRotation = worldRot;
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
            if (unit.definition != null)
            {
                unit.definition.CanSlingLoad = true;
            }

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
                || Mathf.Abs(gv.speed) > 2.0f
                || FindJobByUnit(gv) != null)
            {
                continue;
            }

            float dSq = FastMath.SquareDistance(gv.GlobalPosition(), containerPos);
            if (dSq <= maxSq && dSq < bestMeters)
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
        string unitName = vehicle.definition?.unitName ?? vehicle.unitName ?? string.Empty;
        string jsonKey = vehicle.definition?.jsonKey ?? string.Empty;

        return unitName.EndsWith("Tractor", StringComparison.OrdinalIgnoreCase)
            || unitName.EndsWith("Flatbed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(jsonKey, "HLT-T", StringComparison.OrdinalIgnoreCase)
            || string.Equals(jsonKey, "HLT-L", StringComparison.OrdinalIgnoreCase)
            || string.Equals(jsonKey, "Truck2-T", StringComparison.OrdinalIgnoreCase)
            || string.Equals(jsonKey, "Truck2-L", StringComparison.OrdinalIgnoreCase);
    }

    private void SetFeedback(string text)
    {
        GroundControlReflection.SetMobileEmplacementStatus(text);
    }
}
