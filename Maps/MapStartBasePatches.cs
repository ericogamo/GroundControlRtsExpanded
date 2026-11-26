using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mirage;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace GroundControlRtsExpanded;

[HarmonyPatch]
internal static class ChampionsDynamicStartBasePatch
{
    internal static bool IsDealing { get; private set; }

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

            if (IsThirdFactionsAirbase(ab, localHq, enemyHq) ||
                enemyHq.TryGetNearestDepot(ab.center.position, ab.GetRadius(), out _))
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
        IsDealing = true;
        try
        {
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
                if (savedOther != null &&
                    savedOther.Capturable &&
                    !ReferenceEquals(other.CurrentHQ, enemyHq) &&
                    !IsThirdFactionsAirbase(other, localHq, enemyHq))
                {
                    other.capture.ForceCapture(enemyHq);
                }
            }
        }
        finally
        {
            IsDealing = false;
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

    private static bool IsThirdFactionsAirbase(Airbase ab, FactionHQ localHq, FactionHQ enemyHq)
    {
        return ab.CurrentHQ != null &&
               !ReferenceEquals(ab.CurrentHQ, localHq) &&
               !ReferenceEquals(ab.CurrentHQ, enemyHq);
    }
}

[HarmonyPatch]
internal static class DuelOppositeAirbaseStartPatch
{
    private static bool duelStartHandled;

    internal static bool IsRelocating { get; private set; }

    internal static void ResetSession() => duelStartHandled = false;

    private static bool Prepare() => GroundControlReflection.EcoTickStartingBuildersMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoTickStartingBuildersMethod!;

    private static void Prefix(object __instance)
    {
        if (duelStartHandled
            || !AddonConfig.ModEnabled.Value
            || !AddonConfig.DuelOppositeAirbaseStart.Value
            || __instance == null)
        {
            return;
        }

        if (!GroundControlReflection.IsDuelMission()
            || GroundControlReflection.IsChampionsMission()
            || GroundControlReflection.IsLeviathanMission()
            || FactionRegistry.airbaseLookup == null
            || FactionRegistry.airbaseLookup.Count < 2)
        {
            return;
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        if (localHq == null || !((NetworkBehaviour)localHq).IsServer)
        {
            return;
        }

        FactionHQ? enemyHq = null;
        foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
        {
            if (hq != null && !ReferenceEquals(hq, localHq) && hq.faction != null)
            {
                enemyHq = hq;
                break;
            }
        }

        if (enemyHq == null)
        {
            return;
        }

        var oldPlayerBases = CollectOwnedAirbases(localHq);
        var oldEnemyBases = CollectOwnedAirbases(enemyHq);
        if (oldPlayerBases.Count > 1 || oldEnemyBases.Count > 1)
        {
            duelStartHandled = true;
            return;
        }

        string preferred = (AddonConfig.PreferredStartBase.Value ?? string.Empty).Trim();
        Airbase? preferredMatch = null;
        var fullAirbases = new List<Airbase>();
        var mediumAirbases = new List<Airbase>();
        var allLandAirbases = new List<Airbase>();

        foreach (KeyValuePair<string, Airbase> kv in FactionRegistry.airbaseLookup)
        {
            Airbase? ab = kv.Value;
            if (ab == null || ab.disabled || ab.center == null)
            {
                continue;
            }

            SavedAirbase? saved = ab.SavedAirbase;
            if (saved == null
                || !saved.Capturable
                || !GroundControlReflection.IsLandBase(ab)
                || IsThirdFactionsAirbase(ab, localHq, enemyHq))
            {
                continue;
            }

            allLandAirbases.Add(ab);

            string uniqueName = saved.UniqueName ?? string.Empty;
            string label = GroundControlReflection.GetAirbaseLabel(ab);
            if (!string.IsNullOrEmpty(preferred)
                && (string.Equals(uniqueName, preferred, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ab.name, preferred, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(label, preferred, StringComparison.OrdinalIgnoreCase)))
            {
                preferredMatch = ab;
            }

            bool isStrip = IsHighwayOrStrip(ab.name) || IsHighwayOrStrip(uniqueName) || IsHighwayOrStrip(label);
            float maxRunwayLen = GetMaxTakeoffRunwayLength(ab);
            bool hasHangars = ab.hangars != null && ab.hangars.Count > 0;

            if (!isStrip && maxRunwayLen >= 1100f && hasHangars)
            {
                fullAirbases.Add(ab);
            }

            if (!isStrip && maxRunwayLen >= 700f)
            {
                mediumAirbases.Add(ab);
            }
        }

        List<Airbase> pool = fullAirbases.Count >= 2
            ? fullAirbases
            : (mediumAirbases.Count >= 2 ? mediumAirbases : allLandAirbases);

        if (pool.Count < 2)
        {
            duelStartHandled = true;
            return;
        }

        Airbase? playerBase = preferredMatch;
        Airbase? enemyBase = null;

        if (playerBase != null)
        {
            enemyBase = PickFarthestAirbase(playerBase, pool, allLandAirbases);
        }
        else
        {
            PickOppositePair(pool, out Airbase? pairA, out Airbase? pairB);
            playerBase = pairA;
            enemyBase = pairB;

            if (playerBase != null && enemyBase != null && oldPlayerBases.Count > 0 && oldPlayerBases[0].center != null)
            {
                Airbase origLocal = oldPlayerBases[0];
                if (HorizontalDistSq(enemyBase, origLocal) < HorizontalDistSq(playerBase, origLocal))
                {
                    (playerBase, enemyBase) = (enemyBase, playerBase);
                }
            }
        }

        duelStartHandled = true;
        if (playerBase == null || enemyBase == null || ReferenceEquals(playerBase, enemyBase))
        {
            return;
        }

        IsRelocating = true;
        try
        {
            NeutralizeUnusedOldBases(oldPlayerBases, playerBase, enemyBase);
            NeutralizeUnusedOldBases(oldEnemyBases, playerBase, enemyBase);

            if (!ReferenceEquals(playerBase.CurrentHQ, localHq) && playerBase.capture != null)
            {
                playerBase.capture.ForceCapture(localHq);
            }

            if (!ReferenceEquals(enemyBase.CurrentHQ, enemyHq) && enemyBase.capture != null)
            {
                enemyBase.capture.ForceCapture(enemyHq);
            }
        }
        finally
        {
            IsRelocating = false;
        }

        int clearedBldgs = 0;
        int rebuiltBldgs = 0;
        int movedVehs = 0;
        RelocateOldBaseAssets(__instance, localHq, oldPlayerBases, playerBase, enemyBase, ref clearedBldgs, ref rebuiltBldgs, ref movedVehs);
        RelocateOldBaseAssets(__instance, enemyHq, oldEnemyBases, enemyBase, playerBase, ref clearedBldgs, ref rebuiltBldgs, ref movedVehs);

        GroundControlReflection.EnsureBaseDepots(__instance, localHq, playerBase, 2);
        GroundControlReflection.EnsureBaseDepots(__instance, enemyHq, enemyBase, 2);
        GroundControlReflection.SetVictoryHomeBase(localHq, playerBase);
        GroundControlReflection.SetVictoryHomeBase(enemyHq, enemyBase);
        DynamicCapAndTechService.Instance.InvalidateCache(localHq);
        DynamicCapAndTechService.Instance.InvalidateCache(enemyHq);

        string playerLabel = GroundControlReflection.GetAirbaseLabel(playerBase);
        string enemyLabel = GroundControlReflection.GetAirbaseLabel(enemyBase);
        float distKm = Vector3.Distance(playerBase.center.position, enemyBase.center.position) / 1000f;
        string localName = localHq.faction != null ? localHq.faction.factionName : "Player";
        string enemyName = enemyHq.faction != null ? enemyHq.faction.factionName : "AI";

        AddonLog.Info(
            $"[Maps] Duel opposite-side airbase start dealt: {localName}={playerLabel} vs {enemyName}={enemyLabel} ({distKm:0.0} km, cleared={clearedBldgs}, rebuilt={rebuiltBldgs}, movedVeh={movedVehs}).");
        GroundControlReflection.NotifyAlert($"DUEL START: {playerLabel.ToUpperInvariant()} vs {enemyLabel.ToUpperInvariant()} ({distKm:0.0} km)");
    }

    private static void RelocateOldBaseAssets(
        object ecoInstance,
        FactionHQ hq,
        List<Airbase> oldBases,
        Airbase newOwnBase,
        Airbase otherNewBase,
        ref int clearedBldgs,
        ref int rebuiltBldgs,
        ref int movedVehs)
    {
        if (hq == null || newOwnBase == null || newOwnBase.center == null || hq.factionUnits == null)
        {
            return;
        }

        var unusedOldBases = new List<Airbase>();
        for (int i = 0; i < oldBases.Count; i++)
        {
            Airbase oldAb = oldBases[i];
            if (oldAb != null && oldAb.center != null && !ReferenceEquals(oldAb, newOwnBase) && !ReferenceEquals(oldAb, otherNewBase))
            {
                unusedOldBases.Add(oldAb);
            }
        }

        if (unusedOldBases.Count == 0)
        {
            return;
        }

        var unitSnapshot = new List<Unit>();
        foreach (PersistentID pid in hq.factionUnits)
        {
            if (pid.TryGetUnit(out Unit unit) && unit != null && !unit.disabled)
            {
                unitSnapshot.Add(unit);
            }
        }

        GlobalPosition newBasePos = newOwnBase.center.GlobalPosition();
        float keepNewRadius = Mathf.Max(newOwnBase.GetRadius() + 600f, 1800f);
        var defsToRebuild = new List<BuildingDefinition>();

        for (int i = 0; i < unitSnapshot.Count; i++)
        {
            Unit unit = unitSnapshot[i];
            if (unit == null || unit.disabled)
            {
                continue;
            }

            GlobalPosition uPos = unit.GlobalPosition();
            if (FastMath.InRange(uPos, newBasePos, keepNewRadius))
            {
                continue;
            }

            bool nearOldBase = false;
            for (int b = 0; b < unusedOldBases.Count; b++)
            {
                Airbase oldAb = unusedOldBases[b];
                float oldRadius = Mathf.Max(oldAb.GetRadius() + 1500f, 3000f);
                if (FastMath.InRange(uPos, oldAb.center.GlobalPosition(), oldRadius))
                {
                    nearOldBase = true;
                    break;
                }
            }

            if (!nearOldBase)
            {
                continue;
            }

            if (unit is Building bldg)
            {
                bool isSceneOrBuiltIn = bldg.Identity != null && bldg.Identity.IsSceneObject;
                if (!isSceneOrBuiltIn)
                {
                    for (int b = 0; b < unusedOldBases.Count; b++)
                    {
                        if (unusedOldBases[b].buildings != null && unusedOldBases[b].buildings.Contains(bldg))
                        {
                            isSceneOrBuiltIn = true;
                            break;
                        }
                    }
                }

                if (isSceneOrBuiltIn)
                {
                    bldg.NetworkHQ = null;
                    continue;
                }

                if (bldg.definition is BuildingDefinition bdef
                    && bdef.buildingType != BuildingType.DEP
                    && bdef.buildingType != BuildingType.AMMO)
                {
                    defsToRebuild.Add(bdef);
                }

                SilentDespawnUnit(bldg);
                clearedBldgs++;
            }
            else if (unit is GroundVehicle veh)
            {
                float angle = (movedVehs % 8) * (Mathf.PI * 0.25f);
                float radius = 120f + movedVehs * 18f;
                var targetProbe = new GlobalPosition(
                    newBasePos.x + Mathf.Cos(angle) * radius,
                    newBasePos.y,
                    newBasePos.z + Mathf.Sin(angle) * radius);
                GlobalPosition snapped = GroundControlReflection.SnapToTerrain(targetProbe);
                Vector3 localPos = snapped.ToLocalPosition() + Vector3.up * 0.5f;

                veh.transform.position = localPos;
                if (veh.rb != null)
                {
                    veh.rb.position = localPos;
                    veh.rb.velocity = Vector3.zero;
                    veh.rb.angularVelocity = Vector3.zero;
                }

                GroundControlReflection.MoveClearOrder(veh);
                movedVehs++;
            }
        }

        for (int i = 0; i < defsToRebuild.Count; i++)
        {
            if (GroundControlReflection.TryPlaceKitBuilding(ecoInstance, hq, defsToRebuild[i], newBasePos, out _))
            {
                rebuiltBldgs++;
            }
        }
    }

    private static void SilentDespawnUnit(Unit unit)
    {
        if (unit == null)
        {
            return;
        }

        unit.NetworkHQ = null;
        Collider[] cols = unit.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null)
            {
                cols[i].enabled = false;
            }
        }

        unit.gameObject.SetActive(false);
        Physics.SyncTransforms();
        GroundControlReflection.RemoveFromEconomyBuiltUnits(unit);

        NetworkManagerNuclearOption? net = NetworkManagerNuclearOption.i;
        ServerObjectManager? som = net != null ? net.ServerObjectManager : null;
        if (som != null && unit.Identity != null)
        {
            som.Destroy(unit.Identity, !unit.Identity.IsSceneObject);
        }
        else
        {
            UnityEngine.Object.Destroy(unit.gameObject);
        }
    }

    private static List<Airbase> CollectOwnedAirbases(FactionHQ hq)
    {
        var result = new List<Airbase>();
        foreach (Airbase ab in hq.GetAirbases())
        {
            if (ab != null && !ab.disabled)
            {
                result.Add(ab);
            }
        }

        return result;
    }

    private static bool IsThirdFactionsAirbase(Airbase ab, FactionHQ localHq, FactionHQ enemyHq)
        => ab.CurrentHQ != null
            && !ReferenceEquals(ab.CurrentHQ, localHq)
            && !ReferenceEquals(ab.CurrentHQ, enemyHq);

    private static bool IsHighwayOrStrip(string? text)
        => !string.IsNullOrEmpty(text)
            && (text!.IndexOf("highway", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("strip", StringComparison.OrdinalIgnoreCase) >= 0);

    private static float GetMaxTakeoffRunwayLength(Airbase ab)
    {
        if (ab.runways == null)
        {
            return 0f;
        }

        float maxLen = 0f;
        for (int i = 0; i < ab.runways.Length; i++)
        {
            Airbase.Runway? runway = ab.runways[i];
            if (runway != null && runway.Takeoff && runway.Length > maxLen)
            {
                maxLen = runway.Length;
            }
        }

        return maxLen;
    }

    private static float HorizontalDistSq(Airbase a, Airbase b)
    {
        if (a.center == null || b.center == null)
        {
            return 0f;
        }

        Vector3 pa = a.center.position;
        Vector3 pb = b.center.position;
        float dx = pa.x - pb.x;
        float dz = pa.z - pb.z;
        return dx * dx + dz * dz;
    }

    private static Airbase? PickFarthestAirbase(Airbase from, List<Airbase> primaryPool, List<Airbase> fallbackPool)
    {
        Airbase? best = null;
        float bestDistSq = -1f;
        for (int i = 0; i < primaryPool.Count; i++)
        {
            Airbase cand = primaryPool[i];
            if (ReferenceEquals(cand, from))
            {
                continue;
            }

            float distSq = HorizontalDistSq(from, cand);
            if (distSq > bestDistSq)
            {
                bestDistSq = distSq;
                best = cand;
            }
        }

        if (best != null)
        {
            return best;
        }

        for (int i = 0; i < fallbackPool.Count; i++)
        {
            Airbase cand = fallbackPool[i];
            if (ReferenceEquals(cand, from))
            {
                continue;
            }

            float distSq = HorizontalDistSq(from, cand);
            if (distSq > bestDistSq)
            {
                bestDistSq = distSq;
                best = cand;
            }
        }

        return best;
    }

    private static void PickOppositePair(List<Airbase> pool, out Airbase? baseA, out Airbase? baseB)
    {
        baseA = null;
        baseB = null;
        float maxDistSq = -1f;
        for (int i = 0; i < pool.Count; i++)
        {
            for (int j = i + 1; j < pool.Count; j++)
            {
                float dSq = HorizontalDistSq(pool[i], pool[j]);
                if (dSq > maxDistSq)
                {
                    maxDistSq = dSq;
                }
            }
        }

        if (maxDistSq <= 0f)
        {
            return;
        }

        float threshold = maxDistSq * 0.78f;
        var topPairs = new List<(Airbase A, Airbase B)>();
        for (int i = 0; i < pool.Count; i++)
        {
            for (int j = i + 1; j < pool.Count; j++)
            {
                if (HorizontalDistSq(pool[i], pool[j]) >= threshold)
                {
                    topPairs.Add((pool[i], pool[j]));
                }
            }
        }

        if (topPairs.Count > 0)
        {
            (Airbase A, Airbase B) picked = topPairs[UnityEngine.Random.Range(0, topPairs.Count)];
            baseA = picked.A;
            baseB = picked.B;
        }
    }

    private static void NeutralizeUnusedOldBases(List<Airbase> oldBases, Airbase keepPlayer, Airbase keepEnemy)
    {
        for (int i = 0; i < oldBases.Count; i++)
        {
            Airbase oldAb = oldBases[i];
            if (oldAb == null || ReferenceEquals(oldAb, keepPlayer) || ReferenceEquals(oldAb, keepEnemy))
            {
                continue;
            }

            oldAb.capture?.ForceCapture(null);
            if (oldAb.buildings != null)
            {
                for (int b = 0; b < oldAb.buildings.Count; b++)
                {
                    Building? bldg = oldAb.buildings[b];
                    if (bldg != null)
                    {
                        bldg.NetworkHQ = null;
                    }
                }
            }

            if (oldAb.SavedAirbase != null)
            {
                oldAb.capture?.SetCapturable(oldAb.SavedAirbase.Capturable);
            }
        }
    }
}
