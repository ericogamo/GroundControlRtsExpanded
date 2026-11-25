using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mirage;
using NuclearOption.SavedMission;
using UnityEngine;

namespace GroundControlRtsExpanded;

[HarmonyPatch]
internal static class ChampionsDynamicStartBasePatch
{
    private static bool Prepare() => GroundControlReflection.TickChampionsStartMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.TickChampionsStartMethod!;

    private static bool Prefix(object __instance)
    {
        if (!AddonConfig.ModEnabled.Value)
        {
            return true;
        }

        if (GroundControlReflection.GetChampionsDealt(__instance)
            || !GroundControlReflection.IsChampionsMission()
            || GroundControlReflection.IsLeviathanMission()
            || FactionRegistry.airbaseLookup == null
            || Encyclopedia.i?.buildings == null)
        {
            return true;
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        FactionHQ? enemyHq = null;
        foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
        {
            if (hq != null && !ReferenceEquals(hq, localHq) && hq.faction != null)
            {
                enemyHq = hq;
                break;
            }
        }

        if (localHq == null
            || !((NetworkBehaviour)localHq).IsServer
            || enemyHq == null
            || !enemyHq.TryGetNearestDepot(Vector3.zero, float.MaxValue, out _))
        {
            return true;
        }

        string preferred = (AddonConfig.PreferredStartBase.Value ?? string.Empty).Trim();
        string[] vanillaNames = GroundControlReflection.GetChampionsStartBases(GroundControlReflection.IsGodBudget());

        Airbase? preferredMatch = null;
        var vanillaMatches = new List<Airbase>();
        var allLandMatches = new List<Airbase>();
        var allAnyMatches = new List<Airbase>();

        foreach (KeyValuePair<string, Airbase> kv in FactionRegistry.airbaseLookup)
        {
            Airbase? ab = kv.Value;
            if (ab == null || ab.disabled || ab.center == null)
            {
                continue;
            }

            SavedAirbase? saved = ab.SavedAirbase;
            if (saved == null || !saved.Capturable)
            {
                continue;
            }

            if (enemyHq.TryGetNearestDepot(ab.center.position, ab.GetRadius(), out _))
            {
                continue;
            }

            string uniqueName = saved.UniqueName ?? string.Empty;
            string label = GroundControlReflection.GetAirbaseLabel(ab);

            if (!string.IsNullOrEmpty(preferred)
                && (string.Equals(uniqueName, preferred, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(label, preferred, StringComparison.OrdinalIgnoreCase)))
            {
                preferredMatch = ab;
            }

            if (Array.Exists(vanillaNames, name => string.Equals(name, uniqueName, StringComparison.OrdinalIgnoreCase)))
            {
                vanillaMatches.Add(ab);
            }

            if (GroundControlReflection.IsLandBase(ab))
            {
                allLandMatches.Add(ab);
            }

            allAnyMatches.Add(ab);
        }

        // Heartland-safe fast-path: keep GroundControlRts's native Champions start dealing when no override is configured.
        if (string.IsNullOrEmpty(preferred) && vanillaMatches.Count > 0)
        {
            return true;
        }

        Airbase? chosen = preferredMatch;
        if (chosen == null && vanillaMatches.Count > 0)
        {
            chosen = vanillaMatches[UnityEngine.Random.Range(0, vanillaMatches.Count)];
        }
        else if (chosen == null && allLandMatches.Count > 0)
        {
            chosen = allLandMatches[UnityEngine.Random.Range(0, allLandMatches.Count)];
        }
        else if (chosen == null && allAnyMatches.Count > 0)
        {
            chosen = allAnyMatches[UnityEngine.Random.Range(0, allAnyMatches.Count)];
        }

        if (chosen == null)
        {
            return true;
        }

        GroundControlReflection.SetChampionsDealt(__instance, true);
        if (!ReferenceEquals(chosen.CurrentHQ, localHq))
        {
            chosen.capture.ForceCapture(localHq);
        }

        foreach (KeyValuePair<string, Airbase> kv in FactionRegistry.airbaseLookup)
        {
            Airbase? other = kv.Value;
            if (other == null || ReferenceEquals(other, chosen) || other.disabled)
            {
                continue;
            }

            SavedAirbase? savedOther = other.SavedAirbase;
            if (savedOther != null && savedOther.Capturable && !ReferenceEquals(other.CurrentHQ, enemyHq))
            {
                other.capture.ForceCapture(enemyHq);
            }
        }

        GlobalPosition home = chosen.center.GlobalPosition();
        GroundControlReflection.DealRadarStations(home, localHq, enemyHq);

        string[] kitKeys = GroundControlReflection.GetChampionKitKeys();
        int placed = 0;
        int byRoadCount = 0;
        for (int i = 0; i < kitKeys.Length; i++)
        {
            BuildingDefinition? def = GroundControlReflection.FindBuildingByKey(Encyclopedia.i, kitKeys[i]);
            if (def != null && GroundControlReflection.TryPlaceKitBuilding(__instance, localHq, def, home, out bool byRoad))
            {
                placed++;
                if (byRoad)
                {
                    byRoadCount++;
                }
            }
        }

        string chosenLabel = GroundControlReflection.GetAirbaseLabel(chosen);
        AddonLog.Info(
            $"[Maps] Dynamic Champions start base dealt: {chosenLabel} (preferred='{preferred}', landPool={allLandMatches.Count}, opening={placed}/{kitKeys.Length}, byRoad={byRoadCount}).");
        GroundControlReflection.NotifyAlert("YOUR BASE: " + chosenLabel.ToUpperInvariant());
        GroundControlReflection.GiveEnemyBasesDepots(__instance, enemyHq);
        return false;
    }
}
