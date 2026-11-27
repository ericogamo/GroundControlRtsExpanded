using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal static class AiAirDefenceHelper
{
    internal const int TargetSamsPerStation = 1;
    internal const int TargetHelsPerStation = 1;
    internal const int TargetRadarsPerStation = 1;
    internal const int TargetCramsPerStation = 2;

    internal sealed class StationCounts
    {
        internal int Sites { get; }
        internal int[] Sams { get; }
        internal int[] Stratos { get; }
        internal int[] Hels { get; }
        internal int[] Crams { get; }
        internal int[] Radars { get; }

        internal StationCounts(int sites)
        {
            Sites = Mathf.Max(1, sites);
            Sams = new int[Sites];
            Stratos = new int[Sites];
            Hels = new int[Sites];
            Crams = new int[Sites];
            Radars = new int[Sites];
        }
    }

    private static bool MatchesAnyKeyOrName(VehicleDefinition? def, params string[] candidates)
    {
        if (def == null)
        {
            return false;
        }

        string? unitName = def.unitName;
        string? jsonKey = def.jsonKey;
        for (int i = 0; i < candidates.Length; i++)
        {
            string c = candidates[i];
            if ((!string.IsNullOrEmpty(unitName) && string.Equals(unitName, c, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(jsonKey) && string.Equals(jsonKey, c, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsHelLaserTruck(VehicleDefinition? def)
        => MatchesAnyKeyOrName(def, "MSV LADS", "Truck2-LADS", "HLT-HEL", "LaserTrailer1");

    internal static bool IsCramTruck(VehicleDefinition? def)
        => MatchesAnyKeyOrName(def, "MSV CRAM", "Truck2-CRAM", "HLT-CRAM");

    internal static bool IsHeavySpaag(VehicleDefinition? def)
        => MatchesAnyKeyOrName(def, "AeroSentry SPAAG", "FGA-57 Anvil", "SPAAG1", "SPAAG2");

    internal static bool IsMobileRadarSam(VehicleDefinition? def)
        => MatchesAnyKeyOrName(def, "T9K41 Boltstrike", "Boltstrike RAM45 Launcher", "RadarSAM1");

    internal static bool IsStationSam(VehicleDefinition? def)
        => def != null && (GroundControlReflection.EnemyIsSamSiteLauncher(def) || IsMobileRadarSam(def));

    internal static bool IsStationSam(Unit? unit)
        => unit != null
            && !unit.disabled
            && (GroundControlReflection.EnemyIsSamSiteLauncher(unit)
                || (unit.definition is VehicleDefinition v && IsMobileRadarSam(v)));

    internal static VehicleDefinition? FindStationCramDef(IReadOnlyList<VehicleDefinition>? catalog)
        => FindCatalogMatch(catalog, IsCramTruck) ?? FindCatalogMatch(catalog, IsHeavySpaag);

    internal static bool IsStationCram(VehicleDefinition? def, IReadOnlyList<VehicleDefinition>? catalog)
    {
        if (def == null)
        {
            return false;
        }

        if (IsCramTruck(def))
        {
            return true;
        }

        bool catalogHasCram = catalog != null && FindCatalogMatch(catalog, IsCramTruck) != null;
        return !catalogHasCram && IsHeavySpaag(def);
    }

    internal static bool IsStationCramUnit(Unit? unit, IReadOnlyList<VehicleDefinition>? catalog)
        => unit != null && !unit.disabled && unit.definition is VehicleDefinition v && IsStationCram(v, catalog);

    internal static bool IsPreferredRadarTruck(VehicleDefinition? def, FactionHQ? hq)
    {
        if (def == null || def.vehicleType != VehicleType.RDR)
        {
            return false;
        }

        string tag = $"{hq?.faction?.factionName} {hq?.faction?.factionTag}";
        if (tag.IndexOf("Primeva", StringComparison.OrdinalIgnoreCase) >= 0
            || tag.IndexOf("PALA", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return MatchesAnyKeyOrName(def, "MSV Radar", "Truck2-R");
        }

        if (tag.IndexOf("Boscali", StringComparison.OrdinalIgnoreCase) >= 0
            || tag.IndexOf("BDF", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return MatchesAnyKeyOrName(def, "HLT Radar Truck", "HLT-R");
        }

        return true;
    }

    internal static VehicleDefinition? FindCatalogMatch(
        IReadOnlyList<VehicleDefinition>? catalog,
        Func<VehicleDefinition, bool> predicate)
    {
        if (catalog == null || catalog.Count == 0)
        {
            return null;
        }

        VehicleDefinition? best = null;
        for (int i = 0; i < catalog.Count; i++)
        {
            VehicleDefinition? v = catalog[i];
            if (v != null && predicate(v) && (best == null || v.value > best.value))
            {
                best = v;
            }
        }

        return best;
    }

    internal static bool TryGetStateAndCatalog(
        object? enemyInstance,
        FactionHQ? hq,
        out object? state,
        out List<VehicleDefinition>? catalog)
    {
        state = null;
        catalog = null;
        if (enemyInstance == null || hq == null)
        {
            return false;
        }

        if (GroundControlReflection.EnemyStatesField?.GetValue(enemyInstance) is IDictionary states
            && states.Contains(hq))
        {
            state = states[hq];
        }

        catalog = GroundControlReflection.EnemyCatalogField?.GetValue(enemyInstance) as List<VehicleDefinition>;
        return state != null && catalog != null;
    }

    internal static List<VehicleDefinition>? GetCatalog(object? enemyInstance)
        => enemyInstance != null
            ? GroundControlReflection.EnemyCatalogField?.GetValue(enemyInstance) as List<VehicleDefinition>
            : null;

    internal static int GetEffectiveSiteCount(object state, out List<GlobalPosition>? defencePosts)
    {
        defencePosts = GroundControlReflection.StateDefencePostsProp?.GetValue(state) as List<GlobalPosition>;
        if (defencePosts == null || defencePosts.Count == 0)
        {
            return 0;
        }

        return Mathf.Clamp(GroundControlReflection.EnemySamSiteCount(state), 1, defencePosts.Count);
    }

    internal static StationCounts CountStations(
        object state,
        int sites,
        IReadOnlyList<VehicleDefinition>? catalog)
    {
        var counts = new StationCounts(sites);
        if (sites <= 0)
        {
            return counts;
        }

        if (GroundControlReflection.StateDefendersProp?.GetValue(state) is Dictionary<Unit, int> defenders)
        {
            foreach (KeyValuePair<Unit, int> kvp in defenders)
            {
                Unit u = kvp.Key;
                if (u == null || u.disabled)
                {
                    continue;
                }

                bool isStrato = GroundControlReflection.EnemyIsSamSiteLauncher(u);
                int post = isStrato ? (kvp.Value % counts.Sites) : kvp.Value;
                if (post < 0 || post >= counts.Sites)
                {
                    continue;
                }

                if (isStrato)
                {
                    counts.Sams[post]++;
                    counts.Stratos[post]++;
                }
                else if (u.definition is VehicleDefinition v && IsMobileRadarSam(v))
                {
                    counts.Sams[post]++;
                }
                else if (u.definition is VehicleDefinition hv && IsHelLaserTruck(hv))
                {
                    counts.Hels[post]++;
                }
                else if (IsStationCramUnit(u, catalog))
                {
                    counts.Crams[post]++;
                }
            }
        }

        if (GroundControlReflection.StateSupportProp?.GetValue(state) is Dictionary<Unit, int> support)
        {
            foreach (KeyValuePair<Unit, int> kvp in support)
            {
                Unit u = kvp.Key;
                if (u == null || u.disabled || kvp.Value < 0 || kvp.Value >= counts.Sites)
                {
                    continue;
                }

                if (GroundControlReflection.EnemyIsRadarVehicle(u))
                {
                    counts.Radars[kvp.Value]++;
                }
            }
        }

        return counts;
    }

    internal static int CountActiveSamStations(StationCounts counts)
    {
        int active = 0;
        for (int i = 0; i < counts.Sites; i++)
        {
            if (counts.Sams[i] > 0)
            {
                active++;
            }
        }

        return active;
    }

    internal static bool HasStationLacking(
        FactionHQ hq,
        IReadOnlyList<GlobalPosition>? defencePosts,
        StationCounts counts,
        int[] roleCounts,
        int targetPerStation,
        VehicleDefinition candidate,
        bool requireSamFirst)
    {
        if (defencePosts == null || defencePosts.Count == 0 || candidate == null)
        {
            return false;
        }

        int missing = 0;
        int limit = Mathf.Min(counts.Sites, defencePosts.Count);
        for (int i = 0; i < limit; i++)
        {
            if (requireSamFirst && counts.Sams[i] <= 0)
            {
                continue;
            }

            if (roleCounts[i] >= targetPerStation)
            {
                continue;
            }

            VehicleDepot? depot = GroundControlReflection.EnemyFindSiteDepot(hq, defencePosts[i]);
            if (depot != null && GroundControlReflection.EnemyCanField(depot, candidate))
            {
                missing += targetPerStation - roleCounts[i];
            }
        }

        int reserve = hq.GetUnitSupply(candidate);
        return missing > reserve;
    }

    internal static bool CanAnyOpenSamStationField(
        FactionHQ hq,
        IReadOnlyList<GlobalPosition>? defencePosts,
        StationCounts counts,
        VehicleDefinition? samDef)
    {
        if (defencePosts == null || defencePosts.Count == 0 || samDef == null)
        {
            return false;
        }

        int limit = Mathf.Min(counts.Sites, defencePosts.Count);
        for (int i = 0; i < limit; i++)
        {
            if (counts.Sams[i] >= TargetSamsPerStation)
            {
                continue;
            }

            VehicleDepot? depot = GroundControlReflection.EnemyFindSiteDepot(hq, defencePosts[i]);
            if (depot != null && GroundControlReflection.EnemyCanField(depot, samDef))
            {
                return true;
            }
        }

        return false;
    }

    internal static int CountOpenSamStations(
        FactionHQ hq,
        IReadOnlyList<GlobalPosition>? defencePosts,
        StationCounts counts,
        VehicleDefinition? strato,
        VehicleDefinition? bolt)
    {
        if (defencePosts == null || defencePosts.Count == 0)
        {
            return 0;
        }

        int open = 0;
        int limit = Mathf.Min(counts.Sites, defencePosts.Count);
        for (int i = 0; i < limit; i++)
        {
            if (counts.Sams[i] >= TargetSamsPerStation)
            {
                continue;
            }

            VehicleDepot? depot = GroundControlReflection.EnemyFindSiteDepot(hq, defencePosts[i]);
            if (depot != null
                && ((strato != null && GroundControlReflection.EnemyCanField(depot, strato))
                    || (bolt != null && GroundControlReflection.EnemyCanField(depot, bolt))))
            {
                open++;
            }
        }

        return open;
    }

    internal static int CountReserveSamUnits(FactionHQ hq, VehicleDefinition? strato, VehicleDefinition? bolt)
    {
        int reserve = 0;
        if (strato != null)
        {
            reserve += hq.GetUnitSupply(strato);
        }

        if (bolt != null && !ReferenceEquals(bolt, strato))
        {
            reserve += hq.GetUnitSupply(bolt);
        }

        return reserve;
    }

    internal static VehicleDefinition? ChooseRadarSamCandidate(
        object enemyInstance,
        FactionHQ hq,
        object state,
        IReadOnlyList<VehicleDefinition>? catalog)
    {
        int sites = GetEffectiveSiteCount(state, out List<GlobalPosition>? defencePosts);
        if (sites <= 0 || defencePosts == null)
        {
            return null;
        }

        StationCounts counts = CountStations(state, sites, catalog);
        VehicleDefinition? strato = GroundControlReflection.EnemyChooseSamLauncherUnit(enemyInstance, float.MaxValue);
        VehicleDefinition? bolt = FindCatalogMatch(catalog, IsMobileRadarSam);

        int openSamStations = CountOpenSamStations(hq, defencePosts, counts, strato, bolt);
        int reserveSams = CountReserveSamUnits(hq, strato, bolt);
        if (openSamStations <= reserveSams)
        {
            return null;
        }

        bool wantsSamSite = GroundControlReflection.StateWantsSamSiteProp?.GetValue(state) is bool w && w;
        bool canFieldStrato = wantsSamSite && strato != null && CanAnyOpenSamStationField(hq, defencePosts, counts, strato);
        bool canFieldBolt = bolt != null && CanAnyOpenSamStationField(hq, defencePosts, counts, bolt);

        if (!canFieldStrato)
        {
            return canFieldBolt ? bolt : null;
        }

        if (!canFieldBolt || bolt == null)
        {
            return strato;
        }

        int boltOwned = GroundControlReflection.EnemyCountOwned(hq, bolt);
        int stratoOwned = GroundControlReflection.EnemyCountSamLaunchers(enemyInstance, hq);
        return boltOwned <= stratoOwned ? bolt : strato;
    }

    internal static bool IsRadarSamWanted(
        object enemyInstance,
        FactionHQ hq,
        object state,
        IReadOnlyList<VehicleDefinition>? catalog)
        => ChooseRadarSamCandidate(enemyInstance, hq, state, catalog) != null;

    internal static bool HasUnmetStationPackage(
        object enemyInstance,
        FactionHQ hq,
        object state,
        List<VehicleDefinition> catalog)
    {
        int sites = GetEffectiveSiteCount(state, out List<GlobalPosition>? defencePosts);
        if (sites <= 0 || defencePosts == null)
        {
            return false;
        }

        StationCounts counts = CountStations(state, sites, catalog);
        VehicleDefinition? helDef = FindCatalogMatch(catalog, IsHelLaserTruck);
        VehicleDefinition? cramDef = FindStationCramDef(catalog);

        if (helDef != null && HasStationLacking(hq, defencePosts, counts, counts.Hels, TargetHelsPerStation, helDef, requireSamFirst: false))
        {
            return true;
        }

        if (cramDef != null && HasStationLacking(hq, defencePosts, counts, counts.Crams, TargetCramsPerStation, cramDef, requireSamFirst: false))
        {
            return true;
        }

        return IsRadarSamWanted(enemyInstance, hq, state, catalog);
    }
}

[HarmonyPatch]
internal static class IsExtraLadsPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyIsExtraLadsMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyIsExtraLadsMethod!;

    private static void Postfix(Unit unit, ref bool __result)
    {
        if (!__result
            && AddonConfig.ModEnabled.Value
            && AddonConfig.EnableTacticalQualityAi.Value
            && unit != null
            && unit.definition is VehicleDefinition v
            && AiAirDefenceHelper.IsHelLaserTruck(v))
        {
            __result = true;
        }
    }
}

[HarmonyPatch]
internal static class LadsWantedPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyLadsWantedMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyLadsWantedMethod!;

    private static bool Prefix(object __instance, FactionHQ hq, object state, ref bool __result)
    {
        if (!AddonConfig.ModEnabled.Value || !AddonConfig.EnableTacticalQualityAi.Value)
        {
            return true;
        }

        if (hq == null || state == null || DynamicCapAndTechService.Instance.IsGroundFull(hq))
        {
            __result = false;
            return false;
        }

        List<VehicleDefinition>? catalog = AiAirDefenceHelper.GetCatalog(__instance);
        VehicleDefinition? helDef = AiAirDefenceHelper.FindCatalogMatch(catalog, AiAirDefenceHelper.IsHelLaserTruck);
        if (helDef == null)
        {
            __result = false;
            return false;
        }

        int sites = AiAirDefenceHelper.GetEffectiveSiteCount(state, out List<GlobalPosition>? defencePosts);
        if (sites > 0 && defencePosts != null)
        {
            AiAirDefenceHelper.StationCounts counts = AiAirDefenceHelper.CountStations(state, sites, catalog);
            __result = AiAirDefenceHelper.HasStationLacking(
                hq,
                defencePosts,
                counts,
                counts.Hels,
                AiAirDefenceHelper.TargetHelsPerStation,
                helDef,
                requireSamFirst: false);
        }
        else
        {
            int bases = GroundControlReflection.StateDefenceBaseCountProp?.GetValue(state) is int bc && bc > 0
                ? bc
                : Mathf.Max(1, GroundControlReflection.CountLandBases(hq));
            __result = GroundControlReflection.EnemyCountOwned(hq, helDef) < bases * 3;
        }

        return false;
    }
}

[HarmonyPatch]
internal static class ChooseRingGunPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyChooseRingGunMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyChooseRingGunMethod!;

    private static bool Prefix(object __instance, FactionHQ hq, ref VehicleDefinition? __result)
    {
        if (!AddonConfig.ModEnabled.Value || !AddonConfig.EnableTacticalQualityAi.Value)
        {
            return true;
        }

        if (hq == null || DynamicCapAndTechService.Instance.IsGroundFull(hq))
        {
            __result = null;
            return false;
        }

        if (!AiAirDefenceHelper.TryGetStateAndCatalog(__instance, hq, out object? state, out List<VehicleDefinition>? catalog)
            || state == null
            || catalog == null)
        {
            __result = null;
            return false;
        }

        int sites = AiAirDefenceHelper.GetEffectiveSiteCount(state, out List<GlobalPosition>? defencePosts);
        if (sites <= 0 || defencePosts == null)
        {
            __result = null;
            return false;
        }

        AiAirDefenceHelper.StationCounts counts = AiAirDefenceHelper.CountStations(state, sites, catalog);
        VehicleDefinition? helDef = AiAirDefenceHelper.FindCatalogMatch(catalog, AiAirDefenceHelper.IsHelLaserTruck);
        VehicleDefinition? cramDef = AiAirDefenceHelper.FindStationCramDef(catalog);

        // Priority 1: Finish 1 HEL + 2 C-RAM at stations that already have a SAM deployed.
        if (helDef != null && AiAirDefenceHelper.HasStationLacking(
                hq, defencePosts, counts, counts.Hels, AiAirDefenceHelper.TargetHelsPerStation, helDef, requireSamFirst: true))
        {
            __result = helDef;
            return false;
        }

        if (cramDef != null && AiAirDefenceHelper.HasStationLacking(
                hq, defencePosts, counts, counts.Crams, 1, cramDef, requireSamFirst: true))
        {
            __result = cramDef;
            return false;
        }

        if (cramDef != null && AiAirDefenceHelper.HasStationLacking(
                hq, defencePosts, counts, counts.Crams, AiAirDefenceHelper.TargetCramsPerStation, cramDef, requireSamFirst: true))
        {
            __result = cramDef;
            return false;
        }

        // Priority 2: Alternate cleanly with SAM buys so ring weapons never starve SAM station expansion.
        bool ringTurn = GroundControlReflection.StateSamFundRingTurnProp?.GetValue(state) is bool rt && rt;
        if (!ringTurn && AiAirDefenceHelper.IsRadarSamWanted(__instance, hq, state, catalog))
        {
            __result = null;
            return false;
        }

        // Priority 3: Pre-stage 1 HEL and 2 C-RAM on remaining stations (0..sites-1).
        if (helDef != null && AiAirDefenceHelper.HasStationLacking(
                hq, defencePosts, counts, counts.Hels, AiAirDefenceHelper.TargetHelsPerStation, helDef, requireSamFirst: false))
        {
            __result = helDef;
            return false;
        }

        if (cramDef != null && AiAirDefenceHelper.HasStationLacking(
                hq, defencePosts, counts, counts.Crams, 1, cramDef, requireSamFirst: false))
        {
            __result = cramDef;
            return false;
        }

        if (cramDef != null && AiAirDefenceHelper.HasStationLacking(
                hq, defencePosts, counts, counts.Crams, AiAirDefenceHelper.TargetCramsPerStation, cramDef, requireSamFirst: false))
        {
            __result = cramDef;
            return false;
        }

        __result = null;
        return false;
    }
}

[HarmonyPatch]
internal static class ChooseRadarSamPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyChooseRadarSamMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyChooseRadarSamMethod!;

    private static bool Prefix(object __instance, FactionHQ hq, object state, ref VehicleDefinition? __result)
    {
        if (!AddonConfig.ModEnabled.Value || !AddonConfig.EnableTacticalQualityAi.Value)
        {
            return true;
        }

        if (hq == null || state == null || DynamicCapAndTechService.Instance.IsGroundFull(hq))
        {
            __result = null;
            return false;
        }

        List<VehicleDefinition>? catalog = AiAirDefenceHelper.GetCatalog(__instance);
        __result = AiAirDefenceHelper.ChooseRadarSamCandidate(__instance, hq, state, catalog);
        return false;
    }
}

[HarmonyPatch]
internal static class IsAirDefenceAtMaxPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyIsAirDefenceAtMaxMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyIsAirDefenceAtMaxMethod!;

    private static void Postfix(object __instance, ref bool __result)
    {
        if (!__result || !AddonConfig.ModEnabled.Value || !AddonConfig.EnableTacticalQualityAi.Value)
        {
            return;
        }

        FactionHQ? hq = ReviewContextGuardPatch.ActiveAiHq;
        if (hq == null || DynamicCapAndTechService.Instance.IsGroundFull(hq))
        {
            return;
        }

        if (!AiAirDefenceHelper.TryGetStateAndCatalog(__instance, hq, out object? state, out List<VehicleDefinition>? catalog)
            || state == null
            || catalog == null)
        {
            return;
        }

        if (AiAirDefenceHelper.HasUnmetStationPackage(__instance, hq, state, catalog))
        {
            __result = false;
        }
    }
}

[HarmonyPatch]
internal static class IsAirDefenceFullPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyIsAirDefenceFullMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyIsAirDefenceFullMethod!;

    private static void Postfix(object __instance, VehicleDefinition definition, ref bool __result)
    {
        if (!__result || !AddonConfig.ModEnabled.Value || !AddonConfig.EnableTacticalQualityAi.Value || definition == null)
        {
            return;
        }

        FactionHQ? hq = ReviewContextGuardPatch.ActiveAiHq;
        if (hq == null || DynamicCapAndTechService.Instance.IsGroundFull(hq))
        {
            return;
        }

        if (!AiAirDefenceHelper.TryGetStateAndCatalog(__instance, hq, out object? state, out List<VehicleDefinition>? catalog)
            || state == null
            || catalog == null)
        {
            return;
        }

        int sites = AiAirDefenceHelper.GetEffectiveSiteCount(state, out List<GlobalPosition>? defencePosts);
        if (sites <= 0 || defencePosts == null)
        {
            return;
        }

        AiAirDefenceHelper.StationCounts counts = AiAirDefenceHelper.CountStations(state, sites, catalog);

        if (AiAirDefenceHelper.IsStationSam(definition))
        {
            VehicleDefinition? strato = GroundControlReflection.EnemyChooseSamLauncherUnit(__instance, float.MaxValue);
            VehicleDefinition? bolt = AiAirDefenceHelper.FindCatalogMatch(catalog, AiAirDefenceHelper.IsMobileRadarSam);
            int open = AiAirDefenceHelper.CountOpenSamStations(hq, defencePosts, counts, strato, bolt);
            int reserve = AiAirDefenceHelper.CountReserveSamUnits(hq, strato, bolt);
            if (open > reserve && AiAirDefenceHelper.CanAnyOpenSamStationField(hq, defencePosts, counts, definition))
            {
                __result = false;
            }

            return;
        }

        if (AiAirDefenceHelper.IsHelLaserTruck(definition))
        {
            if (AiAirDefenceHelper.HasStationLacking(
                    hq, defencePosts, counts, counts.Hels, AiAirDefenceHelper.TargetHelsPerStation, definition, requireSamFirst: false))
            {
                __result = false;
            }

            return;
        }

        if (AiAirDefenceHelper.IsStationCram(definition, catalog)
            && AiAirDefenceHelper.HasStationLacking(
                hq, defencePosts, counts, counts.Crams, AiAirDefenceHelper.TargetCramsPerStation, definition, requireSamFirst: false))
        {
            __result = false;
        }
    }
}

[HarmonyPatch]
internal static class SiteFillRankPatch
{
    private static bool Prepare() => GroundControlReflection.EnemySiteFillRankMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemySiteFillRankMethod!;

    private static void Postfix(int launchers, ref int __result)
    {
        if (AddonConfig.ModEnabled.Value && AddonConfig.EnableTacticalQualityAi.Value)
        {
            __result = launchers <= 0 ? 0 : 1;
        }
    }
}

[HarmonyPatch]
internal static class HasSiteWantingLauncherPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyHasSiteWantingLauncherMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyHasSiteWantingLauncherMethod!;

    private static bool Prefix(object __instance, FactionHQ hq, object state, int sites, ref bool __result)
    {
        if (!AddonConfig.ModEnabled.Value || !AddonConfig.EnableTacticalQualityAi.Value)
        {
            return true;
        }

        if (hq == null || state == null || sites <= 0)
        {
            __result = false;
            return false;
        }

        VehicleDefinition? launcher = GroundControlReflection.EnemyChooseSamLauncherUnit(__instance, float.MaxValue);
        List<GlobalPosition>? defencePosts = GroundControlReflection.StateDefencePostsProp?.GetValue(state) as List<GlobalPosition>;
        if (launcher == null || defencePosts == null || defencePosts.Count == 0)
        {
            __result = false;
            return false;
        }

        int limit = Mathf.Min(sites, defencePosts.Count);
        AiAirDefenceHelper.StationCounts counts = AiAirDefenceHelper.CountStations(
            state,
            limit,
            AiAirDefenceHelper.GetCatalog(__instance));

        for (int i = 0; i < limit; i++)
        {
            if (counts.Sams[i] >= AiAirDefenceHelper.TargetSamsPerStation)
            {
                continue;
            }

            VehicleDepot? depot = GroundControlReflection.EnemyFindSiteDepot(hq, defencePosts[i]);
            if (depot != null && GroundControlReflection.EnemyCanField(depot, launcher))
            {
                __result = true;
                return false;
            }
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch]
internal static class ChooseSamSitePatch
{
    private static bool Prepare() => GroundControlReflection.EnemyChooseSamSiteMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyChooseSamSiteMethod!;

    private static bool Prefix(object __instance, object state, int sites, Unit unit, ref int __result)
    {
        if (!AddonConfig.ModEnabled.Value || !AddonConfig.EnableTacticalQualityAi.Value)
        {
            return true;
        }

        List<GlobalPosition>? defencePosts = state != null
            ? GroundControlReflection.StateDefencePostsProp?.GetValue(state) as List<GlobalPosition>
            : null;
        if (state == null || unit == null || defencePosts == null || defencePosts.Count == 0 || sites <= 0)
        {
            __result = -1;
            return false;
        }

        int limit = Mathf.Min(sites, defencePosts.Count);
        AiAirDefenceHelper.StationCounts counts = AiAirDefenceHelper.CountStations(
            state,
            limit,
            AiAirDefenceHelper.GetCatalog(__instance));

        GlobalPosition homeBase = GroundControlReflection.EnemyGetHomeBase(state, unit);
        int bestSite = -1;
        int bestSams = int.MaxValue;

        for (int i = 0; i < limit; i++)
        {
            if (!GroundControlReflection.EnemyIsPostAt(state, i, homeBase))
            {
                continue;
            }

            int samCount = counts.Sams[i];
            if (samCount == 0)
            {
                __result = i;
                return false;
            }

            if (samCount < bestSams)
            {
                bestSams = samCount;
                bestSite = i;
            }
        }

        __result = bestSite;
        return false;
    }
}

[HarmonyPatch]
internal static class ChooseSamDepotPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyChooseSamDepotMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyChooseSamDepotMethod!;

    private static void Postfix(object __instance, FactionHQ hq, object state, int sites)
    {
        if (!AddonConfig.ModEnabled.Value
            || !AddonConfig.EnableTacticalQualityAi.Value
            || __instance == null
            || hq == null
            || state == null
            || sites <= 0)
        {
            return;
        }

        List<GlobalPosition>? defencePosts = GroundControlReflection.StateDefencePostsProp?.GetValue(state) as List<GlobalPosition>;
        if (defencePosts == null || defencePosts.Count == 0)
        {
            GroundControlReflection.StateSamDepotProp?.SetValue(state, null);
            return;
        }

        int limit = Mathf.Min(sites, defencePosts.Count);
        List<VehicleDefinition>? catalog = AiAirDefenceHelper.GetCatalog(__instance);
        AiAirDefenceHelper.StationCounts counts = AiAirDefenceHelper.CountStations(state, limit, catalog);

        VehicleDefinition? strato = GroundControlReflection.EnemyChooseSamLauncherUnit(__instance, float.MaxValue);
        VehicleDefinition? bolt = AiAirDefenceHelper.FindCatalogMatch(catalog, AiAirDefenceHelper.IsMobileRadarSam);

        VehicleDepot? bestDepot = null;
        for (int i = 0; i < limit; i++)
        {
            if (counts.Sams[i] >= AiAirDefenceHelper.TargetSamsPerStation)
            {
                continue;
            }

            VehicleDepot? depot = GroundControlReflection.EnemyFindSiteDepot(hq, defencePosts[i]);
            if (depot == null)
            {
                continue;
            }

            if (strato != null && GroundControlReflection.EnemyCanField(depot, strato))
            {
                bestDepot = depot;
                break;
            }

            if (bestDepot == null && bolt != null && GroundControlReflection.EnemyCanField(depot, bolt))
            {
                bestDepot = depot;
            }
        }

        GroundControlReflection.StateSamDepotProp?.SetValue(state, bestDepot);
    }
}

[HarmonyPatch]
internal static class RecruitAirDefenceTriadPatch
{
    private static readonly List<Unit> UnitScratch = new();
    private static readonly Dictionary<Unit, Unit> ActiveMineGuards = new();

    internal static bool IsActiveMineGuard(Unit? unit)
    {
        if (unit == null)
        {
            return false;
        }

        PruneMineGuards();
        return ActiveMineGuards.ContainsKey(unit);
    }

    private static void PruneMineGuards()
    {
        if (ActiveMineGuards.Count == 0)
        {
            return;
        }

        UnitScratch.Clear();
        foreach (KeyValuePair<Unit, Unit> kv in ActiveMineGuards)
        {
            Unit guard = kv.Key;
            Unit mine = kv.Value;
            if (!ReferenceEquals(guard, null)
                && (guard == null
                    || guard.disabled
                    || mine == null
                    || mine.disabled
                    || guard.NetworkHQ == null
                    || !ReferenceEquals(guard.NetworkHQ, mine.NetworkHQ)))
            {
                UnitScratch.Add(guard!);
            }
        }

        for (int i = 0; i < UnitScratch.Count; i++)
        {
            ActiveMineGuards.Remove(UnitScratch[i]);
        }

        UnitScratch.Clear();
    }

    private static bool Prepare() => GroundControlReflection.EnemyRecruitAirDefenceMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyRecruitAirDefenceMethod!;

    private static void Postfix(object __instance, FactionHQ hq, object state)
    {
        if (!AddonConfig.ModEnabled.Value
            || !AddonConfig.EnableTacticalQualityAi.Value
            || __instance == null
            || hq == null
            || state == null)
        {
            return;
        }

        int sites = AiAirDefenceHelper.GetEffectiveSiteCount(state, out List<GlobalPosition>? defencePosts);
        if (sites <= 0 || defencePosts == null)
        {
            return;
        }

        if (GroundControlReflection.StateDefendersProp?.GetValue(state) is not Dictionary<Unit, int> defenders)
        {
            return;
        }

        IDictionary? slots = GroundControlReflection.StateSlotsProp?.GetValue(state) as IDictionary;
        List<VehicleDefinition>? catalog = AiAirDefenceHelper.GetCatalog(__instance);
        AiAirDefenceHelper.StationCounts counts = AiAirDefenceHelper.CountStations(state, sites, catalog);

        // 1. Place any Boltstrike SAMs onto empty stations (0..sites-1) without stacking multiple SAMs.
        ReconcileStationRole(
            state,
            sites,
            defenders,
            slots,
            counts.Sams,
            AiAirDefenceHelper.TargetSamsPerStation,
            preferSamStation: false,
            counts.Sams,
            u => !GroundControlReflection.EnemyIsSamSiteLauncher(u)
                && u.definition is VehicleDefinition v
                && AiAirDefenceHelper.IsMobileRadarSam(v));

        // 2. Co-locate 1 HEL laser truck onto each AD station, prioritizing stations that already have a SAM.
        ReconcileStationRole(
            state,
            sites,
            defenders,
            slots,
            counts.Hels,
            AiAirDefenceHelper.TargetHelsPerStation,
            preferSamStation: true,
            counts.Sams,
            u => u.definition is VehicleDefinition v && AiAirDefenceHelper.IsHelLaserTruck(v));

        // 3. Co-locate 2 C-RAM point-defence units onto each AD station, prioritizing stations that already have a SAM.
        ReconcileStationRole(
            state,
            sites,
            defenders,
            slots,
            counts.Crams,
            AiAirDefenceHelper.TargetCramsPerStation,
            preferSamStation: true,
            counts.Sams,
            u => AiAirDefenceHelper.IsStationCramUnit(u, catalog));

        // 4. Guard exposed Gold Mines (>1200 m from existing AD stations) with 1 surplus C-RAM/SHORAD if stations are healthy.
        GuardExposedMinesWithSurplusShorad(hq, defencePosts, sites, defenders, slots, counts.Crams, catalog);
    }

    private static void GuardExposedMinesWithSurplusShorad(
        FactionHQ hq,
        List<GlobalPosition> defencePosts,
        int sites,
        Dictionary<Unit, int> defenders,
        IDictionary? slots,
        int[] cramCounts,
        List<VehicleDefinition>? catalog)
    {
        PruneMineGuards();

        if (hq.factionUnits == null)
        {
            return;
        }

        object? eco = GroundControlReflection.GetEcoInstance();
        if (eco == null)
        {
            return;
        }

        Unit? exposedMineUnit = null;
        GlobalPosition? exposedMinePos = null;
        foreach (PersistentID pid in hq.factionUnits)
        {
            if (!pid.TryGetUnit(out Unit unit) || unit == null || unit.disabled || !(unit is Building))
            {
                continue;
            }

            object? kind = GroundControlReflection.GetIncomeKind(eco, unit);
            if (!GroundControlReflection.IsMineBuildKind(kind))
            {
                continue;
            }

            GlobalPosition mPos = unit.GlobalPosition();
            bool covered = false;
            for (int p = 0; p < sites && p < defencePosts.Count; p++)
            {
                if (FastMath.InRange(defencePosts[p], mPos, 1200f))
                {
                    covered = true;
                    break;
                }
            }

            if (!covered)
            {
                bool hasDefenderNearMine = false;
                foreach (KeyValuePair<Unit, Unit> mg in ActiveMineGuards)
                {
                    if (ReferenceEquals(mg.Value, unit)
                        || (mg.Key != null && !mg.Key.disabled && FastMath.InRange(mg.Key.GlobalPosition(), mPos, 900f)))
                    {
                        hasDefenderNearMine = true;
                        break;
                    }
                }

                if (!hasDefenderNearMine)
                {
                    foreach (Unit d in defenders.Keys)
                    {
                        if (d != null && !d.disabled && FastMath.InRange(d.GlobalPosition(), mPos, 900f))
                        {
                            hasDefenderNearMine = true;
                            break;
                        }
                    }
                }

                if (!hasDefenderNearMine)
                {
                    foreach (PersistentID adPid in hq.factionUnits)
                    {
                        if (!adPid.TryGetUnit(out Unit adUnit) || adUnit == null || adUnit.disabled)
                        {
                            continue;
                        }

                        bool isAd = (adUnit is GroundVehicle && adUnit.definition is VehicleDefinition adVeh && GroundControlReflection.IsAirDefence(adVeh))
                            || (adUnit is Building && adUnit.definition is BuildingDefinition adBld && adBld.buildingType == BuildingType.DEF);
                        if (isAd && FastMath.InRange(adUnit.GlobalPosition(), mPos, 950f))
                        {
                            hasDefenderNearMine = true;
                            break;
                        }
                    }
                }

                if (!hasDefenderNearMine)
                {
                    exposedMineUnit = unit;
                    exposedMinePos = mPos;
                    break;
                }
            }
        }

        if (exposedMineUnit == null || !exposedMinePos.HasValue)
        {
            return;
        }

        Unit? surplusCram = null;
        int surplusPost = -1;
        float bestDistSq = float.MaxValue;
        foreach (KeyValuePair<Unit, int> kv in defenders)
        {
            Unit u = kv.Key;
            int post = kv.Value;
            if (u == null || u.disabled || post < 0 || post >= sites)
            {
                continue;
            }

            if (!AiAirDefenceHelper.IsStationCramUnit(u, catalog) || cramCounts[post] <= AiAirDefenceHelper.TargetCramsPerStation)
            {
                continue;
            }

            if (!GroundControlReflection.CanDriveBetween(u.GlobalPosition(), exposedMinePos.Value))
            {
                continue;
            }

            float dSq = FastMath.SquareDistance(u.GlobalPosition(), exposedMinePos.Value);
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                surplusCram = u;
                surplusPost = post;
            }
        }

        if (surplusCram != null)
        {
            if (surplusPost >= 0 && surplusPost < cramCounts.Length && cramCounts[surplusPost] > 0)
            {
                cramCounts[surplusPost]--;
            }

            defenders.Remove(surplusCram);
            slots?.Remove(surplusCram);
            ActiveMineGuards[surplusCram] = exposedMineUnit;
            GroundControlReflection.EnemyHoldAt(surplusCram, exposedMinePos.Value);
        }
    }

    private static void ReconcileStationRole(
        object state,
        int sites,
        Dictionary<Unit, int> defenders,
        IDictionary? slots,
        int[] roleCounts,
        int targetPerStation,
        bool preferSamStation,
        int[] samCounts,
        Func<Unit, bool> predicate)
    {
        UnitScratch.Clear();
        foreach (KeyValuePair<Unit, int> kvp in defenders)
        {
            if (kvp.Key != null && !kvp.Key.disabled && predicate(kvp.Key))
            {
                UnitScratch.Add(kvp.Key);
            }
        }

        for (int i = 0; i < UnitScratch.Count; i++)
        {
            Unit unit = UnitScratch[i];
            int currentPost = defenders[unit];
            bool outOfStationRange = currentPost < 0 || currentPost >= sites;
            bool overStationTarget = !outOfStationRange && roleCounts[currentPost] > targetPerStation;
            bool strandedWithoutSam = preferSamStation && !outOfStationRange && samCounts[currentPost] == 0;

            if (!outOfStationRange && !overStationTarget && !strandedWithoutSam)
            {
                continue;
            }

            GlobalPosition homeBase = GroundControlReflection.EnemyGetHomeBase(state, unit);
            int targetPost = FindBestTargetStation(
                state,
                sites,
                roleCounts,
                targetPerStation,
                preferSamStation,
                samCounts,
                homeBase,
                requireSam: preferSamStation);

            if (targetPost < 0 && !strandedWithoutSam)
            {
                targetPost = FindBestTargetStation(
                    state,
                    sites,
                    roleCounts,
                    targetPerStation,
                    preferSamStation,
                    samCounts,
                    homeBase,
                    requireSam: false);
            }

            if (targetPost < 0 || targetPost == currentPost)
            {
                continue;
            }

            if (!outOfStationRange && roleCounts[currentPost] > 0)
            {
                roleCounts[currentPost]--;
            }

            roleCounts[targetPost]++;
            defenders[unit] = targetPost;
            slots?.Remove(unit);
        }

        UnitScratch.Clear();
    }

    private static int FindBestTargetStation(
        object state,
        int sites,
        int[] roleCounts,
        int targetPerStation,
        bool preferSamStation,
        int[] samCounts,
        GlobalPosition homeBase,
        bool requireSam)
    {
        int bestPost = -1;
        int bestRoleCount = int.MaxValue;

        for (int p = 0; p < sites; p++)
        {
            if (!GroundControlReflection.EnemyIsPostAt(state, p, homeBase))
            {
                continue;
            }

            if (requireSam && samCounts[p] <= 0)
            {
                continue;
            }

            if (preferSamStation && !requireSam && samCounts[p] <= 0)
            {
                // Still allow empty stations only when no SAM station needs this unit.
            }

            int count = roleCounts[p];
            if (count < targetPerStation && count < bestRoleCount)
            {
                bestRoleCount = count;
                bestPost = p;
            }
        }

        return bestPost;
    }
}

[HarmonyPatch]
internal static class SamRadarTargetPatch
{
    private static bool Prepare() => GroundControlReflection.EnemySamRadarTargetMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemySamRadarTargetMethod!;

    private static void Postfix(int bases, int launchers, int sites, ref int __result)
    {
        if (AddonConfig.ModEnabled.Value && AddonConfig.EnableTacticalQualityAi.Value)
        {
            __result = Mathf.Max(bases, Mathf.Min(launchers, sites));
        }
    }
}

[HarmonyPatch]
internal static class ReviewSupportProactiveRadarPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyReviewSupportMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyReviewSupportMethod!;

    private static void Postfix(object __instance, FactionHQ hq, object state)
    {
        if (!AddonConfig.ModEnabled.Value
            || !AddonConfig.EnableTacticalQualityAi.Value
            || __instance == null
            || hq == null
            || state == null)
        {
            return;
        }

        int sites = AiAirDefenceHelper.GetEffectiveSiteCount(state, out _);
        int defenceBases = GroundControlReflection.StateDefenceBaseCountProp?.GetValue(state) is int bc && bc > 0
            ? bc
            : Mathf.Max(1, GroundControlReflection.CountLandBases(hq));

        List<VehicleDefinition>? catalog = AiAirDefenceHelper.GetCatalog(__instance);
        AiAirDefenceHelper.StationCounts? counts = sites > 0
            ? AiAirDefenceHelper.CountStations(state, sites, catalog)
            : null;

        int[]? siteFcus = GroundControlReflection.EnemySiteFireControlsField?.GetValue(__instance) as int[];
        bool needsFcu = false;
        int totalStratoSites = 0;

        if (counts != null)
        {
            for (int i = 0; i < counts.Sites; i++)
            {
                if (counts.Stratos[i] > 0)
                {
                    totalStratoSites++;
                    if (siteFcus == null || i >= siteFcus.Length || siteFcus[i] == 0)
                    {
                        needsFcu = true;
                    }
                }
            }
        }

        // Only StratoLance-equipped stations require Fire Control Units; Boltstrike stations must not buy FCUs.
        GroundControlReflection.StateWantsFireControlProp?.SetValue(state, needsFcu && totalStratoSites > 0);

        if (DynamicCapAndTechService.Instance.IsGroundFull(hq))
        {
            return;
        }

        bool anyStratoLacksRadar = false;
        int activeSamStations = 0;
        List<GlobalPosition>? posts = GroundControlReflection.StateDefencePostsProp?.GetValue(state) as List<GlobalPosition>;
        if (counts != null)
        {
            for (int i = 0; i < counts.Sites; i++)
            {
                if (counts.Sams[i] > 0)
                {
                    activeSamStations++;
                    if (counts.Stratos[i] > 0)
                    {
                        if (counts.Radars[i] == 0)
                        {
                            anyStratoLacksRadar = true;
                        }
                    }
                    else if (posts != null && i < posts.Count)
                    {
                        // Autarkic Boltstrike stations carry integral radar & FC; ensure bunker protection directly
                        // without diverting FCU/radar vehicles from StratoLance batteries.
                        GroundControlReflection.EcoEnsureEnemySiteBunker(hq, posts[i]);
                    }
                }
            }
        }

        int liveRadars = 0;
        int liveTrucks = 0;
        if (hq.factionUnits != null)
        {
            foreach (PersistentID pid in hq.factionUnits)
            {
                if (!pid.TryGetUnit(out Unit unit) || unit == null || unit.disabled || unit is not GroundVehicle)
                {
                    continue;
                }

                if (unit.definition is VehicleDefinition vdef && vdef.vehicleType == VehicleType.RDR)
                {
                    liveRadars++;
                }

                if (GroundControlReflection.EnemyIsRearmVehicle(unit))
                {
                    liveTrucks++;
                }
            }
        }

        if (GroundControlReflection.GetBestDepotLevel(hq) >= 2)
        {
            int targetRadars = Mathf.Clamp(Mathf.Max(totalStratoSites, defenceBases), 1, Mathf.Max(sites, 8));
            int queuedOrOwnedRadars = liveRadars + GroundControlReflection.EnemyCountRadarReserve(__instance, hq);
            if ((anyStratoLacksRadar && GroundControlReflection.EnemyCountRadarReserve(__instance, hq) == 0)
                || queuedOrOwnedRadars < targetRadars)
            {
                GroundControlReflection.StateWantsSiteRadarProp?.SetValue(state, true);
            }
        }

        int targetTrucks = Mathf.Clamp(Mathf.Max(activeSamStations, defenceBases), 1, Mathf.Max(2, defenceBases * 2));
        VehicleDefinition? supplyTruckDef = GroundControlReflection.EnemyChooseSupplyTruck(__instance, float.MaxValue);
        int truckReserve = supplyTruckDef != null ? hq.GetUnitSupply(supplyTruckDef) : 0;
        if (liveTrucks + truckReserve < targetTrucks)
        {
            GroundControlReflection.StateWantsGuardTruckProp?.SetValue(state, true);
        }
    }
}

[HarmonyPatch]
internal static class PreferFactionRadarTruckPatch
{
    private static bool Prepare() => GroundControlReflection.ChooseReconUnitMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.ChooseReconUnitMethod!;

    private static void Postfix(object __instance, float budget, ref VehicleDefinition? __result)
    {
        if (!AddonConfig.ModEnabled.Value || !AddonConfig.EnableTacticalQualityAi.Value)
        {
            return;
        }

        FactionHQ? hq = ReviewContextGuardPatch.ActiveAiHq;
        List<VehicleDefinition>? catalog = AiAirDefenceHelper.GetCatalog(__instance);
        if (hq == null || catalog == null)
        {
            return;
        }

        VehicleDefinition? preferred = AiAirDefenceHelper.FindCatalogMatch(
            catalog,
            v => v.value <= budget && AiAirDefenceHelper.IsPreferredRadarTruck(v, hq));
        if (preferred != null)
        {
            __result = preferred;
        }
    }
}

[HarmonyPatch]
internal static class ReviewDefenceAirShortDepotsPatch
{
    private static readonly List<VehicleDepot> DepotScratch = new();

    private static bool Prepare() => GroundControlReflection.EnemyReviewDefenceMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyReviewDefenceMethod!;

    private static void Postfix(object __instance, FactionHQ hq, object state)
    {
        if (!AddonConfig.ModEnabled.Value
            || !AddonConfig.EnableTacticalQualityAi.Value
            || __instance == null
            || hq == null
            || state == null)
        {
            return;
        }

        List<GlobalPosition>? guardCentres = GroundControlReflection.EnemyGuardCentresField?.GetValue(__instance) as List<GlobalPosition>;
        List<bool>? guardFields = GroundControlReflection.EnemyGuardFieldsField?.GetValue(__instance) as List<bool>;
        List<VehicleDepot>? airShortDepots = GroundControlReflection.StateAirShortDepotsProp?.GetValue(state) as List<VehicleDepot>;
        List<GlobalPosition>? defencePosts = GroundControlReflection.StateDefencePostsProp?.GetValue(state) as List<GlobalPosition>;
        Dictionary<Unit, int>? defenders = GroundControlReflection.StateDefendersProp?.GetValue(state) as Dictionary<Unit, int>;

        if (guardCentres == null || guardFields == null || airShortDepots == null || guardCentres.Count == 0)
        {
            return;
        }

        int baseCount = Mathf.Min(guardCentres.Count, guardFields.Count);
        int[] heavyCounts = new int[baseCount];

        if (defenders != null)
        {
            foreach (KeyValuePair<Unit, int> kvp in defenders)
            {
                Unit u = kvp.Key;
                if (u == null
                    || u.disabled
                    || u.definition is not VehicleDefinition v
                    || v.value < 50f
                    || !GroundControlReflection.IsAirDefence(v))
                {
                    continue;
                }

                GlobalPosition refPos = defencePosts != null && defencePosts.Count > 0
                    ? defencePosts[Math.Abs(kvp.Value) % defencePosts.Count]
                    : u.GlobalPosition();

                int bestIdx = -1;
                float bestDistSq = float.MaxValue;
                for (int b = 0; b < baseCount; b++)
                {
                    float dSq = FastMath.SquareDistance(refPos, guardCentres[b]);
                    if (dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        bestIdx = b;
                    }
                }

                if (bestIdx >= 0)
                {
                    heavyCounts[bestIdx]++;
                }
            }
        }

        for (int b = 0; b < baseCount; b++)
        {
            // Keep Level 2+ depots unlocked until bases have built out 1 SAM + 1 HEL + 2 C-RAM stations.
            if (!guardFields[b] || heavyCounts[b] >= 4)
            {
                continue;
            }

            DepotScratch.Clear();
            GroundControlReflection.EnemyCollectBaseDepots(hq, guardCentres[b], DepotScratch);
            for (int d = 0; d < DepotScratch.Count; d++)
            {
                VehicleDepot depot = DepotScratch[d];
                if (depot == null)
                {
                    continue;
                }

                Unit? owner = depot.GetComponentInParent<Unit>();
                if (owner != null
                    && !owner.disabled
                    && GroundControlReflection.GetDepotLevel(owner) >= 2
                    && !airShortDepots.Contains(depot))
                {
                    airShortDepots.Add(depot);
                }
            }
        }

        DepotScratch.Clear();
    }
}

[HarmonyPatch]
internal static class TryBuySamLauncherPhaseGuardPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyTryBuySamLauncherMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyTryBuySamLauncherMethod!;

    private static bool Prefix(object __instance, FactionHQ? hq, object? state, ref float __result)
    {
        if (!AddonConfig.ModEnabled.Value
            || !AddonConfig.EnableTacticalQualityAi.Value
            || hq == null
            || AiEconomySettings.GetActiveProfile().IsVanilla)
        {
            return true;
        }

        object? eco = GroundControlReflection.GetEcoInstance();
        int goldMines = eco != null ? GroundControlReflection.CountGoldMines(eco, hq) : 0;
        float enemyGross = eco != null ? GroundControlReflection.GetHqIncomePerMinute(eco, hq) : 0f;
        float reserve = GroundControlReflection.GetEnemyBuildReserve(hq, out _, out bool resIncome);
        PlayerThreatSnapshot threat = AiStrategicDirectorService.Instance.ReadPlayerThreat(hq);

        if (goldMines == 0 || enemyGross < 350f)
        {
            GroundControlReflection.SetStateFloat(state, GroundControlReflection.StateSamFundProp, 0f);
            if (!(threat.Aircraft >= 2 && hq.factionFunds >= reserve + 300f))
            {
                __result = 0f;
                return false;
            }
        }

        int activeSamStations = 0;
        int deployedStationUnits = 0;
        if (state != null && AiAirDefenceHelper.TryGetStateAndCatalog(__instance, hq, out _, out List<VehicleDefinition>? catalog))
        {
            int sites = AiAirDefenceHelper.GetEffectiveSiteCount(state, out _);
            if (sites > 0)
            {
                AiAirDefenceHelper.StationCounts counts = AiAirDefenceHelper.CountStations(state, sites, catalog);
                activeSamStations = AiAirDefenceHelper.CountActiveSamStations(counts);
                for (int i = 0; i < counts.Sites; i++)
                {
                    deployedStationUnits += counts.Sams[i] + counts.Hels[i] + counts.Crams[i];
                }
            }
        }

        if (resIncome && reserve > 0f && hq.factionFunds < reserve + 100f)
        {
            if (!(threat.Aircraft >= 2 && activeSamStations == 0))
            {
                __result = 0f;
                return false;
            }
        }

        if (threat.Aircraft == 0 && enemyGross < 550f && deployedStationUnits >= 2)
        {
            GroundControlReflection.SetStateFloat(state, GroundControlReflection.StateSamFundProp, 0f);
            __result = 0f;
            return false;
        }

        if (GroundControlReflection.GetBestDepotLevel(hq) < 2)
        {
            GroundControlReflection.SetStateFloat(state, GroundControlReflection.StateSamFundProp, 0f);
            int bases = Mathf.Max(1, GroundControlReflection.CountLandBases(hq));
            int targetAa = AiStrategicDirectorService.Instance.GetScaledIadsTarget(hq, bases);
            if (GroundControlReflection.GetEnemyOwnedAirDefence(__instance) >= targetAa)
            {
                __result = 0f;
                return false;
            }
        }

        return true;
    }
}

[HarmonyPatch]
internal static class ReviewRadarSamWhileBrokeGuardPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyReviewRadarSamWhileBrokeMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyReviewRadarSamWhileBrokeMethod!;

    private static bool Prefix(FactionHQ? hq, object? state)
    {
        if (!AddonConfig.ModEnabled.Value
            || !AddonConfig.EnableTacticalQualityAi.Value
            || hq == null
            || AiEconomySettings.GetActiveProfile().IsVanilla)
        {
            return true;
        }

        object? eco = GroundControlReflection.GetEcoInstance();
        int goldMines = eco != null ? GroundControlReflection.CountGoldMines(eco, hq) : 0;
        float enemyGross = eco != null ? GroundControlReflection.GetHqIncomePerMinute(eco, hq) : 0f;
        float reserve = GroundControlReflection.GetEnemyBuildReserve(hq, out _, out bool resIncome);
        PlayerThreatSnapshot threat = AiStrategicDirectorService.Instance.ReadPlayerThreat(hq);
        bool defending = GroundControlReflection.GetStateBool(state, GroundControlReflection.StateDefendingProp);

        if (goldMines == 0
            || enemyGross < 400f
            || (resIncome && reserve > 0f && hq.factionFunds < reserve)
            || (threat.Aircraft == 0 && !defending))
        {
            return false;
        }

        return true;
    }
}

[HarmonyPatch]
internal static class TryBuySiteRadarGuardPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyTryBuySiteRadarMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyTryBuySiteRadarMethod!;

    private static bool Prefix(FactionHQ? hq, ref float __result)
    {
        if (!AddonConfig.ModEnabled.Value
            || !AddonConfig.EnableTacticalQualityAi.Value
            || hq == null
            || AiEconomySettings.GetActiveProfile().IsVanilla)
        {
            return true;
        }

        object? eco = GroundControlReflection.GetEcoInstance();
        int goldMines = eco != null ? GroundControlReflection.CountGoldMines(eco, hq) : 0;
        float enemyGross = eco != null ? GroundControlReflection.GetHqIncomePerMinute(eco, hq) : 0f;
        float reserve = GroundControlReflection.GetEnemyBuildReserve(hq, out _, out bool resIncome);
        PlayerThreatSnapshot threat = AiStrategicDirectorService.Instance.ReadPlayerThreat(hq);

        if (threat.Aircraft >= 2 && hq.factionFunds >= reserve + 120f)
        {
            return true;
        }

        if (goldMines == 0 || enemyGross < 350f || (resIncome && reserve > 0f && hq.factionFunds < reserve + 100f))
        {
            __result = 0f;
            return false;
        }

        return true;
    }
}

[HarmonyPatch]
internal static class TryBuyFireControlGuardPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyTryBuyFireControlMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyTryBuyFireControlMethod!;

    private static bool Prefix(FactionHQ? hq, ref float __result)
    {
        if (!AddonConfig.ModEnabled.Value
            || !AddonConfig.EnableTacticalQualityAi.Value
            || hq == null
            || AiEconomySettings.GetActiveProfile().IsVanilla)
        {
            return true;
        }

        object? eco = GroundControlReflection.GetEcoInstance();
        int goldMines = eco != null ? GroundControlReflection.CountGoldMines(eco, hq) : 0;
        float enemyGross = eco != null ? GroundControlReflection.GetHqIncomePerMinute(eco, hq) : 0f;
        float reserve = GroundControlReflection.GetEnemyBuildReserve(hq, out _, out bool resIncome);
        PlayerThreatSnapshot threat = AiStrategicDirectorService.Instance.ReadPlayerThreat(hq);

        if (threat.Aircraft >= 2 && hq.factionFunds >= reserve + 120f)
        {
            return true;
        }

        if (goldMines == 0 || enemyGross < 350f || (resIncome && reserve > 0f && hq.factionFunds < reserve + 100f))
        {
            __result = 0f;
            return false;
        }

        return true;
    }
}

[HarmonyPatch]
internal static class IsRecruitableMineGuardPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyIsRecruitableMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyIsRecruitableMethod!;

    private static void Postfix(Unit? unit, ref bool __result)
    {
        if (__result
            && AddonConfig.ModEnabled.Value
            && AddonConfig.EnableTacticalQualityAi.Value
            && RecruitAirDefenceTriadPatch.IsActiveMineGuard(unit))
        {
            __result = false;
        }
    }
}

[HarmonyPatch]
internal static class EconomyIsBuilderMdsPatch
{
    private static bool Prepare() => GroundControlReflection.EcoIsBuilderMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoIsBuilderMethod!;

    private static void Postfix(Unit? unit, ref bool __result)
    {
        if (!__result
            && unit != null
            && !unit.disabled
            && AddonConfig.ModEnabled.Value
            && MdsBridgeBootstrap.IsUnitBuildingMdsSite?.Invoke(unit) == true)
        {
            __result = true;
        }
    }
}
