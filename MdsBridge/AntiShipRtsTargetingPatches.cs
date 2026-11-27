using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GroundControlRtsExpanded;

[HarmonyPatch]
internal static class CommanderFireNowCanHurtAntiShipPatch
{
    private static bool Prepare() => GroundControlReflection.FireNowCanHurtMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.FireNowCanHurtMethod!;

    private static void Postfix(WeaponStation? station, Unit? target, ref bool __result)
    {
        if (!__result ||
            station?.WeaponInfo == null ||
            MdsBridgeBootstrap.IsMdsAntiShipWeaponInfo?.Invoke(station.WeaponInfo) != true)
        {
            return;
        }

        if (target == null || target.disabled || target is not Ship)
        {
            __result = false;
        }
    }
}

[HarmonyPatch]
internal static class CommanderAirPickForcedStationAntiShipPatch
{
    private static bool Prepare() => GroundControlReflection.AirPickForcedStationMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.AirPickForcedStationMethod!;

    private static void Postfix(
        Aircraft? aircraft,
        List<WeaponStation>? stations,
        object? mission,
        ref WeaponStation? __result)
    {
        if (__result?.WeaponInfo == null ||
            MdsBridgeBootstrap.IsMdsAntiShipWeaponInfo?.Invoke(__result.WeaponInfo) != true)
        {
            return;
        }

        Unit? forcedTarget = mission != null
            ? GroundControlReflection.AirMissionForcedTargetProp?.GetValue(mission) as Unit
            : null;
        if (forcedTarget is Ship && !forcedTarget.disabled)
        {
            return;
        }

        if (aircraft == null || stations == null || forcedTarget == null)
        {
            __result = null;
            return;
        }

        bool isAirTarget = forcedTarget is Aircraft || forcedTarget is Missile;
        bool isPlayerMission = mission != null && GroundControlReflection.IsPlayerAirMission(mission);

        GlobalPosition targetPos = forcedTarget.GlobalPosition();
        FactionHQ? hq = ((Unit)aircraft).NetworkHQ;
        if (hq != null && hq.TryGetKnownPosition(forcedTarget, out GlobalPosition knownPos))
        {
            targetPos = knownPos;
        }

        float dist = FastMath.Distance(targetPos, ((Unit)aircraft).GlobalPosition());

        WeaponStation? best = null;
        float bestScore = float.MinValue;
        for (int i = 0; i < stations.Count; i++)
        {
            WeaponStation? st = stations[i];
            if (st == null || st.Cargo || st.Ammo <= 0)
            {
                continue;
            }

            WeaponInfo? wi = st.WeaponInfo;
            if (wi == null ||
                MdsBridgeBootstrap.IsMdsAntiShipWeaponInfo?.Invoke(wi) == true ||
                (wi.nuclear && isPlayerMission) ||
                wi.jammer ||
                GroundControlReflection.IsFireNowSensorStore(wi) ||
                !GroundControlReflection.CanAirWeaponDefeatArmor(wi, forcedTarget))
            {
                continue;
            }

            float maxRange = wi.targetRequirements.maxRange;
            float eff = isAirTarget
                ? wi.effectiveness.antiAir
                : Mathf.Max(wi.effectiveness.antiSurface, wi.effectiveness.antiRadar);
            if (!wi.gun && (eff <= 0.05f || (!isAirTarget && wi.effectiveness.antiAir > eff)))
            {
                continue;
            }

            float score = (maxRange >= dist ? 1e9f : 0f) + maxRange + (eff * 100f) + (wi.gun ? -50f : 0f);
            if (score > bestScore)
            {
                bestScore = score;
                best = st;
            }
        }

        __result = best;
    }
}

[HarmonyPatch]
internal static class CommanderAirSwarmReleaseAntiShipPatch
{
    private static readonly MethodInfo? TargetSwarmReleaseMethod = AccessTools.Method(
        GroundControlReflection.AirCommandServiceType,
        "SwarmRelease",
        new[] { typeof(Aircraft), typeof(WeaponStation), typeof(List<Unit>) });

    private static bool Prepare() => TargetSwarmReleaseMethod != null;
    private static MethodBase TargetMethod() => TargetSwarmReleaseMethod!;

    private static bool Prefix(Aircraft? aircraft, WeaponStation? station, List<Unit>? targets)
    {
        if (aircraft == null ||
            station?.WeaponInfo == null ||
            targets == null ||
            MdsBridgeBootstrap.IsMdsAntiShipWeaponInfo?.Invoke(station.WeaponInfo) != true)
        {
            return true;
        }

        for (int i = targets.Count - 1; i >= 0; i--)
        {
            Unit? u = targets[i];
            if (u == null || u.disabled || u is not Ship)
            {
                targets.RemoveAt(i);
            }
        }

        return targets.Count > 0;
    }
}

[HarmonyPatch]
internal static class CommanderAirAddSwarmSpreadAntiShipPatch
{
    private static bool Prepare() => GroundControlReflection.AirAddSwarmSpreadMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.AirAddSwarmSpreadMethod!;

    private static bool Prefix(Aircraft? aircraft, WeaponInfo? info, List<Unit>? targets, int want)
    {
        if (info == null ||
            targets == null ||
            MdsBridgeBootstrap.IsMdsAntiShipWeaponInfo?.Invoke(info) != true)
        {
            return true;
        }

        for (int i = targets.Count - 1; i >= 0; i--)
        {
            Unit? u = targets[i];
            if (u == null || u.disabled || u is not Ship)
            {
                targets.RemoveAt(i);
            }
        }

        if (targets.Count == 0 || aircraft == null || targets.Count >= want)
        {
            return false;
        }

        FactionHQ? hq = ((Unit)aircraft).NetworkHQ;
        if (hq == null || !hq.TryGetKnownPosition(targets[0], out GlobalPosition anchorPos))
        {
            return false;
        }

        var shipCandidates = new List<(Unit Unit, float Distance)>();
        foreach (KeyValuePair<PersistentID, TrackingInfo> item in hq.trackingDatabase)
        {
            if (!item.Value.TryGetUnit(out Unit candidate) ||
                candidate == null ||
                candidate.disabled ||
                candidate is not Ship ||
                candidate.NetworkHQ == null ||
                ReferenceEquals(candidate.NetworkHQ, hq) ||
                targets.Contains(candidate))
            {
                continue;
            }

            GlobalPosition candidatePos = item.Value.lastKnownPosition;
            if (FastMath.InRange(candidatePos, anchorPos, 2500f) &&
                GroundControlReflection.CanAirWeaponDefeatArmor(info, candidate))
            {
                shipCandidates.Add((candidate, FastMath.Distance(candidatePos, anchorPos)));
            }
        }

        shipCandidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        for (int i = 0; i < shipCandidates.Count && targets.Count < want; i++)
        {
            targets.Add(shipCandidates[i].Unit);
        }

        return false;
    }
}

[HarmonyPatch]
internal static class CommanderAirIsWeaponEligibleAntiShipPatch
{
    private static bool Prepare() => GroundControlReflection.AirIsWeaponEligibleMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.AirIsWeaponEligibleMethod!;

    private static void Postfix(object[] __args, ref bool __result)
    {
        if (!__result || __args == null || __args.Length < 2)
        {
            return;
        }

        if (__args[0] is not WeaponInfo info ||
            MdsBridgeBootstrap.IsMdsAntiShipWeaponInfo?.Invoke(info) != true)
        {
            return;
        }

        string modeName = __args[1]?.ToString() ?? string.Empty;
        if (!string.Equals(modeName, "StrategicStrike", StringComparison.Ordinal) &&
            !string.Equals(modeName, "Hybrid", StringComparison.Ordinal))
        {
            __result = false;
        }
    }
}
