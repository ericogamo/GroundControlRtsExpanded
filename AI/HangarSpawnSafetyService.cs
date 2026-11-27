using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mirage;
using NuclearOption.Networking;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal sealed class HangarSpawnSafetyService : IAddonResetSession
{
    private sealed class BayReservation
    {
        internal bool IsAiSpawn;
        internal AircraftDefinition? SpawnedDefinition;
        internal GameObject? SpawnedObject;
        internal bool WaitingForDelayedSpawn;
        internal float PendingSpawnTimeoutAt;
        internal float HoldUntilTime;
        internal float PostExitGraceUntil;
        internal bool WasInsideClearanceZone;
    }

    private const float DelayedSpawnTimeoutSeconds = 40f;
    private const float PlayerSpawnShortHoldSeconds = 5f;
    private const float PostExitGraceSeconds = 2.5f;
    private const float NoticeCooldownSeconds = 2.0f;

    private static readonly FieldInfo? HangarSpawnedObjectField = AccessTools.Field(typeof(Hangar), "spawnedObject");
    private static readonly FieldInfo? HangarWaitForOpenField = AccessTools.Field(typeof(Hangar), "waitForOpenBeforeSpawn");
    private static readonly FieldInfo? HangarAvailableAircraftField = AccessTools.Field(typeof(Hangar), "availableAircraft");
    private static readonly FieldInfo? SelectionAirbaseField = AccessTools.Field(typeof(AircraftSelectionMenu), "airbase");
    private static readonly FieldInfo? SelectionListField = AccessTools.Field(typeof(AircraftSelectionMenu), "aircraftSelection");
    private static readonly FieldInfo? SelectionIndexField = AccessTools.Field(typeof(AircraftSelectionMenu), "selectionIndex");
    private static readonly FieldInfo? SelectionNoHangarsPanelField = AccessTools.Field(typeof(AircraftSelectionMenu), "noHangarsPanel");

    private readonly Dictionary<Hangar, BayReservation> reservations = new();
    private readonly Dictionary<INetworkPlayer, float> lastRemoteNoticeByPlayer = new();
    private readonly Collider[] overlapBuffer = new Collider[128];
    private float lastNoticeTime = -100f;

    internal static HangarSpawnSafetyService Instance { get; } = new();

    public void ResetSession()
    {
        reservations.Clear();
        lastRemoteNoticeByPlayer.Clear();
        lastNoticeTime = -100f;
    }

    internal void OnHangarDestroyed(Hangar? hangar)
    {
        if (hangar != null)
        {
            reservations.Remove(hangar);
        }
    }

    internal void OnDelayedSpawnQueued(Hangar? hangar, bool isAi, AircraftDefinition? definition)
    {
        if (hangar == null)
        {
            return;
        }

        float now = Time.unscaledTime;
        BayReservation res = GetOrCreateReservation(hangar);
        res.IsAiSpawn = isAi;
        res.SpawnedDefinition = definition;
        res.SpawnedObject = null;
        res.WaitingForDelayedSpawn = true;
        res.PendingSpawnTimeoutAt = now + DelayedSpawnTimeoutSeconds;
        res.HoldUntilTime = now + (isAi ? AddonConfig.HangarAiPlayerSafetySeconds.Value : PlayerSpawnShortHoldSeconds);
        res.PostExitGraceUntil = 0f;
        res.WasInsideClearanceZone = true;
    }

    internal void OnSpawnStarting(Hangar? hangar, bool isAi, AircraftDefinition? definition)
    {
        if (hangar == null)
        {
            return;
        }

        // Close the vanilla re-entrancy window between SpawnAircraft() and DoorSequenceNormal() -> OpenDoors().
        hangar.Networkavailable = false;

        float now = Time.unscaledTime;
        BayReservation res = GetOrCreateReservation(hangar);
        res.IsAiSpawn = isAi;
        res.SpawnedDefinition = definition;
        res.WaitingForDelayedSpawn = false;
        res.PendingSpawnTimeoutAt = 0f;
        res.HoldUntilTime = now + (isAi ? AddonConfig.HangarAiPlayerSafetySeconds.Value : PlayerSpawnShortHoldSeconds);
        res.PostExitGraceUntil = 0f;
        res.WasInsideClearanceZone = true;
    }

    internal void OnSpawnCompleted(Hangar? hangar, bool isAi, AircraftDefinition? definition)
    {
        if (hangar == null)
        {
            return;
        }

        float now = Time.unscaledTime;
        GameObject? spawnedObj = GetHangarSpawnedObject(hangar);
        BayReservation res = GetOrCreateReservation(hangar);
        res.IsAiSpawn = isAi;
        res.SpawnedDefinition = definition;
        res.SpawnedObject = spawnedObj;
        res.WaitingForDelayedSpawn = false;
        res.PendingSpawnTimeoutAt = 0f;
        res.HoldUntilTime = now + (isAi ? AddonConfig.HangarAiPlayerSafetySeconds.Value : PlayerSpawnShortHoldSeconds);
        res.PostExitGraceUntil = 0f;
        res.WasInsideClearanceZone = spawnedObj != null;
    }

    internal bool IsHangarBlocked(
        Hangar? hangar,
        Player? requestingPlayer,
        AircraftDefinition? incomingDef,
        out string reason)
    {
        reason = string.Empty;
        if (!AddonConfig.ModEnabled.Value || !AddonConfig.EnableHangarSpawnSafety.Value || hangar == null)
        {
            return false;
        }

        if (hangar.Disabled)
        {
            reservations.Remove(hangar);
            return false;
        }

        Transform spawnTf = hangar.GetSpawnTransform();
        if (spawnTf == null)
        {
            spawnTf = hangar.transform;
        }

        Vector3 spawnPos = spawnTf.position;
        float incomingLength = incomingDef != null ? Mathf.Max(4f, incomingDef.length) : 20f;
        float incomingWidth = incomingDef != null ? Mathf.Max(4f, incomingDef.width) : 16f;
        float incomingHeight = incomingDef != null ? Mathf.Max(2f, incomingDef.height) : 6f;
        float incomingSpan = Mathf.Max(incomingLength, incomingWidth);

        float now = Time.unscaledTime;
        bool isDelayedHangar = HangarWaitForOpenField?.GetValue(hangar) is bool wait && wait;
        bool isPlayerRequest = requestingPlayer != null;

        // Check 1: Active bay reservation (AI/player spawn lock + exit-lane tracking).
        if (reservations.TryGetValue(hangar, out BayReservation? res))
        {
            if (res.WaitingForDelayedSpawn)
            {
                if (now < res.PendingSpawnTimeoutAt)
                {
                    reason = res.IsAiSpawn
                        ? "AI aircraft is launching from this bay"
                        : "another aircraft is launching from this bay";
                    return true;
                }

                res.WaitingForDelayedSpawn = false;
            }

            GameObject? tracked = res.SpawnedObject != null ? res.SpawnedObject : GetHangarSpawnedObject(hangar);
            if (tracked != null)
            {
                float spawnedSpan = res.SpawnedDefinition != null
                    ? Mathf.Max(res.SpawnedDefinition.length, res.SpawnedDefinition.width)
                    : 20f;
                float ownClearHoriz = res.IsAiSpawn
                    ? Mathf.Max(AddonConfig.HangarSafetyClearanceMeters.Value, (spawnedSpan + incomingSpan) * 0.5f + 16f)
                    : Mathf.Max(36f, (spawnedSpan + incomingSpan) * 0.5f + 12f);

                if (IsInsideOwnClearanceEnvelope(tracked.transform.position, spawnTf, spawnPos, ownClearHoriz, spawnedSpan, incomingSpan, res.IsAiSpawn))
                {
                    res.WasInsideClearanceZone = true;
                    res.PostExitGraceUntil = 0f;
                    reason = res.IsAiSpawn
                        ? "AI aircraft has not cleared the hangar bay"
                        : "previous aircraft has not cleared the hangar bay";
                    return true;
                }

                if (res.WasInsideClearanceZone)
                {
                    res.WasInsideClearanceZone = false;
                    res.PostExitGraceUntil = now + PostExitGraceSeconds;
                }
            }

            if (now < res.HoldUntilTime)
            {
                if (res.IsAiSpawn || isDelayedHangar || isPlayerRequest)
                {
                    float remaining = Mathf.Ceil(res.HoldUntilTime - now);
                    reason = res.IsAiSpawn
                        ? $"AI aircraft rollout safety lock ({remaining:0}s)"
                        : $"hangar bay cooldown ({remaining:0}s)";
                    return true;
                }
            }

            if (now < res.PostExitGraceUntil)
            {
                reason = "aircraft just exited the hangar bay";
                return true;
            }

            reservations.Remove(hangar);
        }

        // Check 2: Live UnitRegistry.allAircraft (also works on remote clients).
        List<Aircraft> allAircraft = UnitRegistry.allAircraft;
        for (int i = 0; i < allAircraft.Count; i++)
        {
            Aircraft ac = allAircraft[i];
            if (ac == null || !ac.networked || ac.gameObject.layer == PhysicsLayers.IgnoreCollisions)
            {
                continue;
            }

            float otherLen = ac.definition != null ? Mathf.Max(4f, ac.definition.length) : Mathf.Max(4f, ac.maxRadius * 2f);
            float otherWidth = ac.definition != null ? Mathf.Max(4f, ac.definition.width) : Mathf.Max(4f, ac.maxRadius * 2f);
            float otherHeight = ac.definition != null ? Mathf.Max(2f, ac.definition.height) : 6f;
            float otherSpan = Mathf.Max(otherLen, otherWidth);

            if (ReferenceEquals(ac.NetworkspawningHangar, hangar))
            {
                bool isAiAircraft = ac.Player == null;
                float ownClearHoriz = isAiAircraft
                    ? Mathf.Max(AddonConfig.HangarSafetyClearanceMeters.Value, (otherSpan + incomingSpan) * 0.5f + 16f)
                    : Mathf.Max(36f, (otherSpan + incomingSpan) * 0.5f + 12f);

                if (IsInsideOwnClearanceEnvelope(ac.transform.position, spawnTf, spawnPos, ownClearHoriz, otherSpan, incomingSpan, isAiAircraft))
                {
                    reason = isAiAircraft
                        ? "AI aircraft is still clearing this bay"
                        : "an aircraft is still clearing this bay";
                    return true;
                }
            }

            if (IsInsideBayFootprint(
                ac.transform.position - spawnPos,
                spawnTf,
                (incomingWidth + otherWidth) * 0.5f + 4f,
                (incomingLength + otherLen) * 0.5f + 4f,
                (incomingLength + otherLen) * 0.5f + 12f,
                Mathf.Max(18f, (incomingHeight + otherHeight) * 0.5f + 10f)))
            {
                reason = ac.Player == null
                    ? "an AI aircraft is inside this bay"
                    : "an aircraft is inside this bay";
                return true;
            }
        }

        // Check 3: Physical overlap probe (catches disabled aircraft wrecks, ground vehicles, and detached vehicle wreckage in the bay).
        float probeRadius = Mathf.Clamp(incomingSpan * 0.5f + 10f, 16f, 34f);
        int hitCount = Physics.OverlapSphereNonAlloc(
            spawnPos,
            probeRadius,
            overlapBuffer,
            PhysicsLayers.DefaultMask,
            QueryTriggerInteraction.Ignore);

        Collider[] hits = overlapBuffer;
        int count = hitCount;
        if (hitCount >= overlapBuffer.Length)
        {
            hits = Physics.OverlapSphere(
                spawnPos,
                probeRadius,
                PhysicsLayers.DefaultMask,
                QueryTriggerInteraction.Ignore);
            count = hits.Length;
        }

        try
        {
            for (int i = 0; i < count; i++)
            {
                Collider? col = hits[i];
                if (col == null || col.gameObject.layer == PhysicsLayers.IgnoreCollisions)
                {
                    continue;
                }

                Unit? unit = ResolveUnitFromCollider(col);
                if (unit == null)
                {
                    Wreckage? wreck = col.GetComponentInParent<Wreckage>();
                    if (wreck != null)
                    {
                        Vector3 closestWreck = GetSafeClosestPoint(col, spawnPos);
                        if (IsInsideBayFootprint(
                            closestWreck - spawnPos,
                            spawnTf,
                            incomingWidth * 0.5f + 2f,
                            incomingLength * 0.5f + 2f,
                            incomingLength * 0.5f + 4f,
                            10f))
                        {
                            reason = "vehicle wreckage is blocking this bay";
                            return true;
                        }
                    }

                    continue;
                }

                if (ReferenceEquals(unit, hangar.attachedUnit))
                {
                    continue;
                }

                if (unit is Building || unit is Ship || unit is Scenery)
                {
                    continue;
                }

                if (unit is Aircraft previewAc && !previewAc.networked)
                {
                    continue;
                }

                bool isAircraft = unit is Aircraft;
                bool isVehicle = unit is GroundVehicle;
                if (!isAircraft && !isVehicle)
                {
                    continue;
                }

                Vector3 closest = GetSafeClosestPoint(col, spawnPos);
                Vector3 localDelta = closest - spawnPos;

                if (isAircraft)
                {
                    if (IsInsideBayFootprint(
                        localDelta,
                        spawnTf,
                        incomingWidth * 0.5f + 4f,
                        incomingLength * 0.5f + 4f,
                        incomingLength * 0.5f + 10f,
                        14f))
                    {
                        reason = unit.disabled
                            ? "aircraft wreckage is blocking this bay"
                            : "an aircraft is obstructing this bay";
                        return true;
                    }
                }
                else if (IsInsideBayFootprint(
                    localDelta,
                    spawnTf,
                    incomingWidth * 0.5f + 2f,
                    incomingLength * 0.5f + 2f,
                    incomingLength * 0.5f + 4f,
                    10f))
                {
                    reason = "a ground vehicle is inside this bay";
                    return true;
                }
            }
        }
        finally
        {
            if (hitCount > 0)
            {
                Array.Clear(overlapBuffer, 0, Math.Min(hitCount, overlapBuffer.Length));
            }
        }

        return false;
    }

    internal bool HasAnySafetyBlockedCompatibleHangar(
        Airbase? airbase,
        AircraftDefinition? definition,
        out string reason,
        Player? requestingPlayer = null)
    {
        reason = string.Empty;
        if (airbase == null || definition == null || airbase.disabled || airbase.hangars == null)
        {
            return false;
        }

        for (int i = 0; i < airbase.hangars.Count; i++)
        {
            Hangar hangar = airbase.hangars[i];
            if (hangar == null || hangar.Disabled || !SupportsDefinition(hangar, definition))
            {
                continue;
            }

            if (IsHangarBlocked(hangar, requestingPlayer, definition, out reason))
            {
                return true;
            }
        }

        return false;
    }

    internal void HandleSelectionMenuFlyCheck(AircraftSelectionMenu menu)
    {
        if (menu == null || !AddonConfig.ModEnabled.Value || !AddonConfig.EnableHangarSpawnSafety.Value)
        {
            return;
        }

        GameObject? noHangarsPanel = SelectionNoHangarsPanelField?.GetValue(menu) as GameObject;
        if (noHangarsPanel == null || !noHangarsPanel.activeSelf)
        {
            return;
        }

        Airbase? airbase = SelectionAirbaseField?.GetValue(menu) as Airbase;
        if (airbase == null)
        {
            return;
        }

        if (SelectionListField?.GetValue(menu) is not List<AircraftDefinition> selection || selection.Count == 0)
        {
            return;
        }

        int idx = SelectionIndexField?.GetValue(menu) is int i ? i : -1;
        if (idx < 0 || idx >= selection.Count)
        {
            return;
        }

        AircraftDefinition definition = selection[idx];
        if (HasAnySafetyBlockedCompatibleHangar(airbase, definition, out string reason))
        {
            NotifyLocalPlayerBlocked(reason);
        }
    }

    internal void NotifyLocalPlayerBlocked(string reason)
    {
        float now = Time.unscaledTime;
        if (now - lastNoticeTime < NoticeCooldownSeconds)
        {
            return;
        }

        lastNoticeTime = now;
        string details = string.IsNullOrWhiteSpace(reason) ? "bay occupied by AI spawn" : reason;
        GroundControlReflection.NoticeFail($"Hangar bay locked for safety ({details}) — wait a moment or pick another airbase.");
    }

    internal void NotifyRemotePlayerBlocked(Player? player, string reason)
    {
        if (player == null || player.IsLocalPlayer)
        {
            return;
        }

        INetworkPlayer? netPlayer = player.Owner;
        if (netPlayer == null)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (lastRemoteNoticeByPlayer.TryGetValue(netPlayer, out float lastAt) && now - lastAt < NoticeCooldownSeconds)
        {
            return;
        }

        lastRemoteNoticeByPlayer[netPlayer] = now;
        string details = string.IsNullOrWhiteSpace(reason) ? "bay occupied by AI spawn" : reason;
        GcreNetworkSync.SendPlayerNotice(netPlayer, $"Hangar bay locked for safety ({details}) — wait a moment or pick another airbase.");
    }

    private BayReservation GetOrCreateReservation(Hangar hangar)
    {
        if (!reservations.TryGetValue(hangar, out BayReservation? res))
        {
            res = new BayReservation();
            reservations[hangar] = res;
        }

        return res;
    }

    private static GameObject? GetHangarSpawnedObject(Hangar hangar)
        => HangarSpawnedObjectField?.GetValue(hangar) as GameObject;

    private static bool SupportsDefinition(Hangar hangar, AircraftDefinition definition)
    {
        if (HangarAvailableAircraftField?.GetValue(hangar) is AircraftDefinition[] defs)
        {
            for (int i = 0; i < defs.Length; i++)
            {
                if (ReferenceEquals(defs[i], definition))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsInsideOwnClearanceEnvelope(
        Vector3 objectPos,
        Transform spawnTf,
        Vector3 spawnPos,
        float ownClearHoriz,
        float spawnedSpan,
        float incomingSpan,
        bool includeForwardExitLane)
    {
        Vector3 delta = objectPos - spawnPos;
        float horizDist = new Vector2(delta.x, delta.z).magnitude;
        float vertDist = Mathf.Abs(delta.y);

        if (horizDist < ownClearHoriz && vertDist < 30f)
        {
            return true;
        }

        if (!includeForwardExitLane || vertDist >= 30f)
        {
            return false;
        }

        float localForward = Vector3.Dot(delta, spawnTf.forward);
        float localRight = Vector3.Dot(delta, spawnTf.right);
        float halfLaneWidth = Mathf.Max(20f, (spawnedSpan + incomingSpan) * 0.5f + 6f);
        return localForward > -15f
            && localForward < ownClearHoriz + 10f
            && Mathf.Abs(localRight) < halfLaneWidth;
    }

    private static bool IsInsideBayFootprint(
        Vector3 delta,
        Transform spawnTf,
        float halfWidth,
        float backDist,
        float frontDist,
        float heightLimit)
    {
        float localRight = Vector3.Dot(delta, spawnTf.right);
        float localForward = Vector3.Dot(delta, spawnTf.forward);
        float localUp = Vector3.Dot(delta, spawnTf.up);

        return Mathf.Abs(localRight) < halfWidth
            && localForward > -backDist
            && localForward < frontDist
            && Mathf.Abs(localUp) < heightLimit;
    }

    private static Unit? ResolveUnitFromCollider(Collider col)
    {
        Unit? unit = col.GetComponentInParent<Unit>();
        if (unit != null)
        {
            return unit;
        }

        UnitPart? part = col.GetComponentInParent<UnitPart>();
        return part != null ? part.parentUnit : null;
    }

    private static Vector3 GetSafeClosestPoint(Collider col, Vector3 point)
    {
        if (col is BoxCollider || col is SphereCollider || col is CapsuleCollider || (col is MeshCollider mc && mc.convex))
        {
            return col.ClosestPoint(point);
        }

        return col.bounds.ClosestPoint(point);
    }
}
