using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mirage;
using UnityEngine;

namespace GroundControlRtsExpanded;

[HarmonyPatch]
internal static class WantsRadarHunterPatch
{
    private static bool Prepare() => GroundControlReflection.WantsRadarHunterMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.WantsRadarHunterMethod!;

    private static void Postfix(FactionHQ hq, ref bool __result)
    {
        if (__result || !AddonConfig.EnableTacticalQualityAi.Value || !AddonConfig.SeadBeforeStrike.Value || hq == null)
        {
            return;
        }

        int detectedRadars = AiTacticalDoctrineService.Instance.CountDetectedEnemySamRadars(hq);
        if (detectedRadars >= 2 && AiTacticalDoctrineService.Instance.CountAirborneRadarHunters(hq) < 3)
        {
            if (AiTacticalDoctrineService.Instance.HasDetectedEnemyRadar(hq))
            {
                __result = true;
            }
        }
    }
}

[HarmonyPatch]
internal static class TryBuyRoleSeadPriorityPatch
{
    internal static bool SuppressAntiShipForCurrentBuy { get; private set; }

    private static bool Prepare() => GroundControlReflection.TryBuyRoleMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.TryBuyRoleMethod!;

    private static void Prefix(FactionHQ? hq, object? state, ref bool bombing, ref bool escalate, out bool __state)
    {
        __state = SuppressAntiShipForCurrentBuy;
        SuppressAntiShipForCurrentBuy = false;

        if (!AddonConfig.EnableTacticalQualityAi.Value || hq == null)
        {
            return;
        }

        PlayerThreatSnapshot threat = AiStrategicDirectorService.Instance.ReadPlayerThreat(hq);
        bool urgentSead = AddonConfig.SeadBeforeStrike.Value
            && threat.SamRadars >= 1
            && AiTacticalDoctrineService.Instance.HasDetectedEnemyRadar(hq)
            && AiTacticalDoctrineService.Instance.CountAirborneRadarHunters(hq) < Mathf.Clamp(threat.SamRadars, 1, 3);

        if (bombing && urgentSead)
        {
            // Force non-bombing SEAD / ARAD hunter sortie selection first when player SAM coverage is active.
            bombing = false;
        }

        bool throttleAntiShip = false;
        if (threat.Ships > 0 && threat.Ships < 3)
        {
            int sortie = AiTacticalDoctrineService.Instance.NextStrikeSortieIndex(hq);
            throttleAntiShip = (sortie % 3) != 0;
        }

        SuppressAntiShipForCurrentBuy = urgentSead || throttleAntiShip;

        int bestDepot = GroundControlReflection.GetBestDepotLevel(hq);
        int techTier = DynamicCapAndTechService.IsActive ? DynamicCapAndTechService.Instance.GetSnapshot(hq).TechTier : 2;
        object? eco = GroundControlReflection.GetEcoInstance();
        float enemyGross = GroundControlReflection.GetHqIncomePerMinute(eco, hq);

        if (bestDepot >= 2 || techTier >= 2 || enemyGross >= 250f)
        {
            escalate = true;
            if (state != null)
            {
                float currentCap = GroundControlReflection.GetStateFloat(state, GroundControlReflection.StateAirSaveCapProp);
                float desiredCap = Mathf.Max(currentCap, Mathf.Max(260f, enemyGross * 0.75f));
                if (desiredCap > currentCap)
                {
                    GroundControlReflection.SetStateFloat(state, GroundControlReflection.StateAirSaveCapProp, desiredCap);
                }
            }
        }
    }

    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        SuppressAntiShipForCurrentBuy = __state;
        return __exception;
    }
}

[HarmonyPatch]
internal static class KeepAntiShipCandidatesSeadGuardPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyKeepAntiShipCandidatesMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyKeepAntiShipCandidatesMethod!;

    private static bool Prefix(float reference, ref float __result)
    {
        if (!TryBuyRoleSeadPriorityPatch.SuppressAntiShipForCurrentBuy)
        {
            return true;
        }

        __result = reference;
        return false;
    }
}

[HarmonyPatch]
internal static class ScoreAntiShipStoreSeadGuardPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyScoreAntiShipStoreMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyScoreAntiShipStoreMethod!;

    private static bool Prefix(ref float __result)
    {
        if (!TryBuyRoleSeadPriorityPatch.SuppressAntiShipForCurrentBuy)
        {
            return true;
        }

        __result = 0f;
        return false;
    }
}

[HarmonyPatch]
internal static class BuyAirframePicketPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyBuyAirframeMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyBuyAirframeMethod!;

    private static bool Prefix(object __instance, FactionHQ? hq, object? state, float budget, ref float __result)
    {
        if (!AddonConfig.EnableTacticalQualityAi.Value || hq == null || state == null)
        {
            return true;
        }

        int airborne = GroundControlReflection.EnemyCountAirborne(hq);
        if (airborne >= 1
            && budget > 0f
            && budget < float.MaxValue * 0.5f
            && !GroundControlReflection.EnemyHasAirStock(hq))
        {
            object? eco = GroundControlReflection.GetEcoInstance();
            if (eco != null)
            {
                float enemyGross = GroundControlReflection.GetHqIncomePerMinute(eco, hq);
                int goldMines = GroundControlReflection.CountGoldMines(eco, hq);
                if (enemyGross < 100f && goldMines == 0)
                {
                    float reserve = GroundControlReflection.GetEnemyBuildReserve(hq, out _, out _);
                    if (reserve > 0f && hq.factionFunds < reserve + 60f)
                    {
                        __result = 0f;
                        return false;
                    }
                }
            }
        }

        if (!AddonConfig.EnableAiAwacsPickets.Value)
        {
            return true;
        }

        int airborneLimit = GroundControlReflection.EnemyGetAirborneLimit();
        if (airborne >= airborneLimit)
        {
            return true;
        }

        int techTier = DynamicCapAndTechService.IsActive
            ? DynamicCapAndTechService.Instance.GetSnapshot(hq).TechTier
            : Mathf.Clamp(GroundControlReflection.GetBestDepotLevel(hq), 1, 3);
        int airCap = DynamicCapAndTechService.IsActive
            ? DynamicCapAndTechService.Instance.GetSnapshot(hq).AirCap
            : (GroundControlReflection.IsVanillaUnitCapEnabled() ? GroundControlReflection.GetVanillaCaps().AirCap : 10);

        int targetPickets = 0;
        if (techTier >= 3 && airCap >= 7)
        {
            targetPickets = 2;
        }
        else if (techTier >= 2 && airCap >= 4)
        {
            targetPickets = 1;
        }

        if (targetPickets <= 0)
        {
            return true;
        }

        int pickets = GroundControlReflection.EnemyCountPickets(hq);
        if (pickets < targetPickets && (airborne - pickets) >= 2 * (pickets + 1))
        {
            float spent = GroundControlReflection.EnemyTryBuyPicket(__instance, hq, state, budget, out bool available);
            if (spent > 0f)
            {
                __result = spent;
                return false;
            }

            if (available && pickets == 0 && !GroundControlReflection.EnemyHasAirStock(hq))
            {
                __result = 0f;
                return false;
            }
        }

        return true;
    }
}

[HarmonyPatch]
internal static class DropCricketsQualityPatch
{
    private static FieldInfo? cachedItem1Field;

    private static bool Prepare() => GroundControlReflection.EnemyDropCricketsMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyDropCricketsMethod!;

    private static void Postfix(object __instance, FactionHQ? hq, bool escalate, ref float __result)
    {
        if (!AddonConfig.EnableTacticalQualityAi.Value || !escalate || hq == null)
        {
            return;
        }

        AircraftDefinition? savingFor = GroundControlReflection.GetEnemySavingFor(__instance);
        if (savingFor == null)
        {
            return;
        }

        IList? candidates = GroundControlReflection.GetEnemyAirCandidates(__instance);
        if (candidates == null || candidates.Count == 0)
        {
            return;
        }

        if (cachedItem1Field == null && candidates[0] != null)
        {
            cachedItem1Field = candidates[0]!.GetType().GetField("Item1");
        }

        if (cachedItem1Field == null)
        {
            return;
        }

        bool removedAny = false;
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            object? entry = candidates[i];
            if (entry != null
                && cachedItem1Field.GetValue(entry) is AircraftDefinition def
                && GroundControlReflection.IsCricket(def)
                && hq.GetUnitSupply(def) <= 0)
            {
                candidates.RemoveAt(i);
                removedAny = true;
            }
        }

        if (!removedAny)
        {
            return;
        }

        if (candidates.Count == 0)
        {
            __result = 0f;
            return;
        }

        float maxPrice = 0f;
        for (int i = 0; i < candidates.Count; i++)
        {
            object? entry = candidates[i];
            if (entry != null && cachedItem1Field.GetValue(entry) is AircraftDefinition def)
            {
                float price = GroundControlReflection.EcoPriceOf(def);
                if (price > maxPrice)
                {
                    maxPrice = price;
                }
            }
        }

        __result = maxPrice;
    }
}

[HarmonyPatch]
internal static class TaskAirWingSeadHoldPatch
{
    private struct TaskAirWingState
    {
        internal FactionHQ? PrevAiHq;
        internal bool HoldActive;
    }

    private static bool Prepare() => GroundControlReflection.TaskAirWingMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.TaskAirWingMethod!;

    private static void Prefix(object __instance, FactionHQ hq, FactionHQ localHq, out TaskAirWingState __state)
    {
        __state = new TaskAirWingState
        {
            PrevAiHq = ReviewContextGuardPatch.ActiveAiHq,
            HoldActive = false,
        };
        ReviewContextGuardPatch.ActiveAiHq = hq;

        if (!AddonConfig.EnableTacticalQualityAi.Value || !AddonConfig.SeadBeforeStrike.Value || hq == null)
        {
            return;
        }

        if (GroundControlReflection.EnemyStatesField?.GetValue(__instance) is not IDictionary states || !states.Contains(hq))
        {
            return;
        }

        object? state = states[hq];
        if (state == null
            || GroundControlReflection.StateWaveOutProp == null
            || GroundControlReflection.StateWaveWaitSinceProp == null
            || GroundControlReflection.StateWaveGoalProp == null)
        {
            return;
        }

        bool waveOut = GroundControlReflection.StateWaveOutProp.GetValue(state) is bool w && w;
        if (AiTacticalDoctrineService.Instance.ShouldHoldBombersForSead(hq, localHq, waveOut, out bool releaseSavedGoal))
        {
            __state.HoldActive = true;
            int curGoal = GroundControlReflection.StateWaveGoalProp.GetValue(state) is int g ? g : 0;
            AiTacticalDoctrineService.Instance.NoteSavedWaveGoal(hq, curGoal);
            GroundControlReflection.StateWaveWaitSinceProp.SetValue(state, Time.time);
            // Prevent early launch via wave count threshold while holding for SEAD.
            if (curGoal < 99)
            {
                GroundControlReflection.StateWaveGoalProp.SetValue(state, 99);
            }
        }
        else if (releaseSavedGoal)
        {
            int restored = AiTacticalDoctrineService.Instance.ConsumeSavedWaveGoal(hq, 2);
            int curGoal = GroundControlReflection.StateWaveGoalProp.GetValue(state) is int g ? g : 0;
            if (curGoal >= 99)
            {
                GroundControlReflection.StateWaveGoalProp.SetValue(state, restored);
            }
        }
    }

    private static void Postfix(object __instance, FactionHQ hq, FactionHQ localHq, TaskAirWingState __state)
    {
        if (hq == null || hq.factionUnits == null || !AddonConfig.EnableTacticalQualityAi.Value)
        {
            return;
        }

        if (__state.HoldActive)
        {
            if (GroundControlReflection.EnemyStatesField?.GetValue(__instance) is IDictionary states
                && states.Contains(hq)
                && states[hq] is object stateObj)
            {
                GroundControlReflection.StateWaveOutProp?.SetValue(stateObj, false);
            }

            GlobalPosition stagingPos = FindNearestOwnAirbasePos(hq, AiBattlefieldIntelService.Instance.GetPrimaryObjectivePos(hq, localHq));

            foreach (PersistentID pid in hq.factionUnits)
            {
                if (!pid.TryGetUnit(out Unit u) || u is not Aircraft ac || ac.disabled || ac.Player != null)
                {
                    continue;
                }

                if (!GroundControlReflection.AirHasPlanePilot(ac)
                    || IsCargoConfigured(ac)
                    || GroundControlReflection.EnemyIsRadarPicket(ac.definition, hq)
                    || GroundControlReflection.IsRadarHunter(ac, hq)
                    || GroundControlReflection.AirIsMissionReturning(ac)
                    || GroundControlReflection.AirGetMissionLandingBase(ac) != null
                    || GroundControlReflection.AirGetMissionModeInt(ac) == GroundControlReflection.AirCommandModeAradValue
                    || !GroundControlReflection.AirHasCombatAmmo(ac))
                {
                    continue;
                }

                // AirRole: 0=Strike, 1=Fighter, 2=Transport
                if (GroundControlReflection.EnemyGetAirRoleInt(ac) == 1)
                {
                    continue;
                }

                int mode = GroundControlReflection.AirCarriesStrategicStore(ac) ? 4 : 1;
                GroundControlReflection.AirClearAiRoute(ac);
                GroundControlReflection.AirClearAiForcedTarget(ac);
                GroundControlReflection.AirSetAiCommitted(ac, false);
                GroundControlReflection.AirRetargetAiMission(ac, mode, stagingPos, 15000f);
            }

            return;
        }

        // Priority 1 rear-area QRF response for free CAS / gunships / fighters without diverting strategic bombers
        if (AddonConfig.EnableAiRearQrf.Value
            && AddonConfig.EnableAiCombinedArmsSync.Value
            && AiBattlefieldIntelService.Instance.TryGetRearQrfAlert(hq, out GlobalPosition alertPos, out Unit? attacker))
        {
            bool isAirAttack = attacker is Aircraft;
            int fighterIndex = 0;
            foreach (PersistentID pid in hq.factionUnits)
            {
                if (!pid.TryGetUnit(out Unit u) || u is not Aircraft ac || ac.disabled || ac.Player != null)
                {
                    continue;
                }

                if (IsCargoConfigured(ac)
                    || GroundControlReflection.EnemyIsRadarPicket(ac.definition, hq)
                    || GroundControlReflection.IsRadarHunter(ac, hq)
                    || !AiOperationalManeuverService.CanSafelyDivertToRearQrf(ac))
                {
                    continue;
                }

                if (!GroundControlReflection.AirHasPlanePilot(ac))
                {
                    if (isAirAttack)
                    {
                        continue;
                    }

                    // Rotary CAS gunships respond directly to rear surface QRF alert
                    GroundControlReflection.AirClearAiRoute(ac);
                    GroundControlReflection.AirClearAiForcedTarget(ac);
                    GroundControlReflection.AirSetAiCommitted(ac, false);
                    GroundControlReflection.AirRetargetAiMission(ac, 1 /* Cas */, alertPos, 4500f);
                    continue;
                }

                if (GroundControlReflection.AirHasBombAmmo(ac, true) || GroundControlReflection.AirCarriesStrategicStore(ac))
                {
                    // Keep strategic bombers focused on hostile bases / high-value targets
                    continue;
                }

                int role = GroundControlReflection.EnemyGetAirRoleInt(ac);
                if (role == 1)
                {
                    // Divert every 2nd fighter on patrol to cover rear QRF airspace
                    if ((fighterIndex++ % 2) == 0)
                    {
                        GroundControlReflection.AirClearAiRoute(ac);
                        GroundControlReflection.AirClearAiForcedTarget(ac);
                        GroundControlReflection.AirSetAiCommitted(ac, false);
                        GroundControlReflection.AirRetargetAiMission(ac, 2 /* AirGuard */, alertPos, 12000f);
                    }
                }
                else if (role == 0 && !isAirAttack)
                {
                    // Non-strategic CAS fixed-wing responds to rear surface threats
                    GroundControlReflection.AirClearAiRoute(ac);
                    GroundControlReflection.AirClearAiForcedTarget(ac);
                    GroundControlReflection.AirSetAiCommitted(ac, false);
                    GroundControlReflection.AirRetargetAiMission(ac, 1 /* Cas */, alertPos, 6000f);
                }
            }
        }
    }

    private static Exception? Finalizer(Exception? __exception, TaskAirWingState __state)
    {
        ReviewContextGuardPatch.ActiveAiHq = __state.PrevAiHq;
        return __exception;
    }

    private static GlobalPosition FindNearestOwnAirbasePos(FactionHQ hq, GlobalPosition targetPos)
    {
        GlobalPosition best = GroundControlReflection.CaptureGetTerritoryCenter(hq);
        float bestDistSq = float.MaxValue;
        foreach (Airbase ab in hq.GetAirbases())
        {
            if (ab == null || ab.disabled || ab.center == null)
            {
                continue;
            }

            GlobalPosition p = ab.center.GlobalPosition();
            float dSq = FastMath.SquareDistance(p, targetPos);
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                best = p;
            }
        }

        return best;
    }

    private static bool IsCargoConfigured(Aircraft aircraft)
    {
        List<WeaponStation>? stations = ((Unit)aircraft).weaponStations;
        if (stations == null)
        {
            return false;
        }

        for (int i = 0; i < stations.Count; i++)
        {
            WeaponStation? st = stations[i];
            if (st != null && st.Cargo)
            {
                return true;
            }
        }

        return false;
    }
}

[HarmonyPatch]
internal static class SteerPicketsContextPatch
{
    private static bool Prepare() => GroundControlReflection.EnemySteerPicketsMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemySteerPicketsMethod!;

    private static void Prefix(FactionHQ hq, out FactionHQ? __state)
    {
        __state = ReviewContextGuardPatch.ActiveAiHq;
        ReviewContextGuardPatch.ActiveAiHq = hq;
    }

    private static Exception? Finalizer(Exception? __exception, FactionHQ? __state)
    {
        ReviewContextGuardPatch.ActiveAiHq = __state;
        return __exception;
    }
}

[HarmonyPatch]
internal static class ReviewArtilleryContextPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyReviewArtilleryMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyReviewArtilleryMethod!;

    private static void Prefix(FactionHQ hq, out FactionHQ? __state)
    {
        __state = ReviewContextGuardPatch.ActiveAiHq;
        ReviewContextGuardPatch.ActiveAiHq = hq;
    }

    private static Exception? Finalizer(Exception? __exception, FactionHQ? __state)
    {
        ReviewContextGuardPatch.ActiveAiHq = __state;
        return __exception;
    }
}

[HarmonyPatch]
internal static class ReviewAaScoutsSuppressPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyReviewAaScoutsMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyReviewAaScoutsMethod!;

    private static bool Prefix()
        => !AddonConfig.EnableTacticalQualityAi.Value;
}

[HarmonyPatch]
internal static class GetStrikeTargetCoordinatedPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyGetStrikeTargetMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyGetStrikeTargetMethod!;

    private static void Postfix(FactionHQ localHq, ref GlobalPosition __result)
    {
        if (!AddonConfig.EnableTacticalQualityAi.Value || !AddonConfig.EnableAiCombinedArmsSync.Value)
        {
            return;
        }

        FactionHQ? aiHq = ReviewContextGuardPatch.ActiveAiHq;
        if (aiHq == null)
        {
            return;
        }

        __result = AiBattlefieldIntelService.Instance.GetCoordinatedStrikeTarget(
            aiHq,
            localHq,
            __result,
            includeRearQrf: false);
    }
}

[HarmonyPatch]
internal static class PickWaveFlankSamAvoidPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyPickWaveFlankMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyPickWaveFlankMethod!;

    private static void Postfix(GlobalPosition staging, GlobalPosition target, ref string approach, ref GlobalPosition? __result)
    {
        if (!AddonConfig.EnableTacticalQualityAi.Value)
        {
            return;
        }

        FactionHQ? aiHq = ReviewContextGuardPatch.ActiveAiHq;
        if (aiHq == null)
        {
            return;
        }

        Vector3 diff = target.AsVector3() - staging.AsVector3();
        diff.y = 0f;
        float dist = diff.magnitude;
        if (dist < 4000f)
        {
            return;
        }

        Vector3 dir = diff / dist;
        Vector3 right = Vector3.Cross(Vector3.up, dir);
        float flankOffset = Mathf.Clamp(dist * 0.42f, 6000f, 18000f);

        GlobalPosition midStraight = FastMath.Lerp(staging, target, 0.50f);
        GlobalPosition wpLeft = staging + dir * (dist * 0.55f) - right * flankOffset;
        wpLeft.y = target.y;
        GlobalPosition wpRight = staging + dir * (dist * 0.55f) + right * flankOffset;
        wpRight.y = target.y;

        float riskStraight = AiBattlefieldIntelService.Instance.EvaluateCorridorSamRisk(aiHq, staging, midStraight, target);
        float riskLeft = AiBattlefieldIntelService.Instance.EvaluateCorridorSamRisk(aiHq, staging, wpLeft, target);
        float riskRight = AiBattlefieldIntelService.Instance.EvaluateCorridorSamRisk(aiHq, staging, wpRight, target);

        if (riskStraight <= riskLeft + 0.05f && riskStraight <= riskRight + 0.05f)
        {
            __result = null;
            approach = "straight in (lowest SAM risk)";
            return;
        }

        if (riskLeft <= riskRight)
        {
            __result = wpLeft;
            approach = "round the left flank (SAM avoidance)";
        }
        else
        {
            __result = wpRight;
            approach = "round the right flank (SAM avoidance)";
        }
    }
}

internal static class CogTargetSelectorHelper
{
    internal static float ScoreTargetCog(FactionHQ aiHq, Unit unit, bool includeClusterContext)
    {
        if (unit == null || unit.disabled || unit.definition == null)
        {
            return 0f;
        }

        float score = unit.definition.value;

        bool isMdsRadarOrFcu = false;
        if (MdsBridgeBootstrap.IsMdsSelectableUnit?.Invoke(unit) == true && MdsBridgeBootstrap.QueryMdsSiteUiStatus != null)
        {
            var mds = MdsBridgeBootstrap.QueryMdsSiteUiStatus(unit);
            isMdsRadarOrFcu = mds.IsFireControlRadar || (mds.IsWeaponSite && unit.HasRadarEmission());
        }

        bool isRadarBuilding = unit is Building && unit.definition is BuildingDefinition bdr && bdr.buildingType == BuildingType.RDR;
        bool isSamRadarVeh = unit is GroundVehicle && unit.definition is VehicleDefinition vdr
            && (vdr.vehicleType == VehicleType.R_SAM || vdr.vehicleType == VehicleType.RDR || GroundControlReflection.IsRadarGuidedAirDefence(vdr));

        if (isMdsRadarOrFcu || (unit.HasRadarEmission() && (isRadarBuilding || isSamRadarVeh)))
        {
            score += 160000f;
        }
        else if (TryGetCitySpec(unit, out RtsBuildingSpec spec))
        {
            switch (spec.Kind)
            {
                case RtsBuildingKind.CommandHq:
                case RtsBuildingKind.PilotAcademy:
                case RtsBuildingKind.AbmPlant:
                case RtsBuildingKind.AbmSilo:
                case RtsBuildingKind.AbmRadar:
                    score += 130000f;
                    break;
                case RtsBuildingKind.Barracks:
                case RtsBuildingKind.NavalCommand:
                case RtsBuildingKind.LogisticsCamp:
                    score += 80000f;
                    break;
            }
        }
        else if (GroundControlReflection.EcoIsIncomeBuildingUnit(unit)
            || (unit is Building && unit.definition is BuildingDefinition bdf && bdf.buildingType == BuildingType.FAC))
        {
            score += 110000f;
        }
        else if (AiBattlefieldIntelService.Instance.IsLocatedHostileArtillery(aiHq, unit))
        {
            score += 95000f;
        }
        else if (unit is Building && unit.definition is BuildingDefinition bdd
            && (bdd.buildingType == BuildingType.DEP || bdd.buildingType == BuildingType.HGR))
        {
            score += 80000f;
        }

        if (includeClusterContext && unit is Building)
        {
            score += 2000f;
        }

        if (IsNearMainOrStalledObjective(aiHq, unit.GlobalPosition()))
        {
            score += 65000f;
        }

        return score;
    }

    private static bool IsNearMainOrStalledObjective(FactionHQ aiHq, GlobalPosition targetPos)
    {
        if (AiBattlefieldIntelService.Instance.TryGetMainObjectiveBase(aiHq, out Airbase? mainObj)
            && mainObj != null
            && !mainObj.disabled
            && mainObj.center != null
            && FastMath.InRange(targetPos, mainObj.center.GlobalPosition(), 3000f))
        {
            return true;
        }

        foreach (Airbase ab in FactionRegistry.airbaseLookup.Values)
        {
            if (ab != null
                && !ab.disabled
                && ab.center != null
                && !ReferenceEquals(ab.CurrentHQ, aiHq)
                && AiTacticalDoctrineService.Instance.IsTargetStalled(aiHq, ab)
                && FastMath.InRange(targetPos, ab.center.GlobalPosition(), 3000f))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetCitySpec(Unit unit, out RtsBuildingSpec spec)
    {
        if (unit.definition is BuildingDefinition bd && RtsBuildingCatalog.TryGetSpec(bd, out spec))
        {
            return true;
        }

        return RtsBuildingCatalog.TryMatchByUniqueName(unit.UniqueName, out spec);
    }
}

[HarmonyPatch]
internal static class PickPriorityTargetCogPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyPickPriorityTargetMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyPickPriorityTargetMethod!;

    private static void Postfix(FactionHQ hq, FactionHQ localHq, GlobalPosition playerBase, Aircraft aircraft, ref Unit? __result)
    {
        if (!AddonConfig.EnableTacticalQualityAi.Value || hq == null || aircraft == null || hq.trackingDatabase == null)
        {
            return;
        }

        float now = Time.time;
        float levelNow = Time.timeSinceLevelLoad;
        GlobalPosition acPos = aircraft.GlobalPosition();
        Unit? bestUnpenalized = null;
        float bestUnpenalizedScore = 0f;
        Unit? bestFallback = null;
        float bestFallbackScore = 0f;

        foreach (KeyValuePair<PersistentID, TrackingInfo> kv in hq.trackingDatabase)
        {
            TrackingInfo info = kv.Value;
            if (info == null || !info.TryGetUnit(out Unit unit) || unit == null || unit.disabled || unit.definition == null)
            {
                continue;
            }

            if (unit.NetworkHQ == null || ReferenceEquals(unit.NetworkHQ, hq))
            {
                continue;
            }

            bool isLocatedArty = AiBattlefieldIntelService.Instance.IsLocatedHostileArtillery(hq, unit);
            bool isRadarUnit = unit.HasRadarEmission();
            if (!(unit is Building) && !isLocatedArty && !isRadarUnit)
            {
                continue;
            }

            if (!(unit is Building) && !isLocatedArty && levelNow - info.lastSpottedTime > 90f)
            {
                continue;
            }

            GlobalPosition pos = info.GetPosition();
            if (!FastMath.InRange(pos, playerBase, 25000f) && !FastMath.InRange(pos, acPos, 35000f))
            {
                continue;
            }

            if (!GroundControlReflection.EnemyCanAnyStoreDefeat(aircraft, unit))
            {
                continue;
            }

            float score = CogTargetSelectorHelper.ScoreTargetCog(hq, unit, includeClusterContext: false);
            if (score <= 0f)
            {
                continue;
            }

            if (score > bestFallbackScore)
            {
                bestFallbackScore = score;
                bestFallback = unit;
            }

            if (!AiTacticalDoctrineService.Instance.IsForcedTargetRecentlyAssigned(hq, unit, now) && score > bestUnpenalizedScore)
            {
                bestUnpenalizedScore = score;
                bestUnpenalized = unit;
            }
        }

        Unit? chosen = bestUnpenalized ?? bestFallback ?? __result;
        if (chosen != null)
        {
            __result = chosen;
            AiTacticalDoctrineService.Instance.NoteForcedTargetAssigned(hq, chosen, now);
        }
    }
}

[HarmonyPatch]
internal static class PickSalvoTargetCogPatch
{
    private static readonly List<(Unit Unit, GlobalPosition Pos, float BaseScore)> SalvoScratch = new(200);

    private static bool Prepare() => GroundControlReflection.EnemyPickSalvoTargetMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyPickSalvoTargetMethod!;

    private static bool Prefix(FactionHQ hq, GlobalPosition playerBase, Aircraft aircraft, ref Unit? __result)
    {
        if (!AddonConfig.EnableTacticalQualityAi.Value || hq == null || aircraft == null || hq.trackingDatabase == null)
        {
            return true;
        }

        float now = Time.time;
        float levelNow = Time.timeSinceLevelLoad;
        GlobalPosition acPos = aircraft.GlobalPosition();
        SalvoScratch.Clear();

        foreach (KeyValuePair<PersistentID, TrackingInfo> kv in hq.trackingDatabase)
        {
            if (SalvoScratch.Count >= 200)
            {
                break;
            }

            TrackingInfo info = kv.Value;
            if (info == null || !info.TryGetUnit(out Unit unit) || unit == null || unit.disabled || unit.definition == null)
            {
                continue;
            }

            if (!(unit is Building) && !(unit is GroundVehicle))
            {
                continue;
            }

            if (unit.NetworkHQ == null || ReferenceEquals(unit.NetworkHQ, hq))
            {
                continue;
            }

            if (!(unit is Building)
                && levelNow - info.lastSpottedTime > 90f
                && !AiBattlefieldIntelService.Instance.IsLocatedHostileArtillery(hq, unit))
            {
                continue;
            }

            GlobalPosition pos = info.GetPosition();
            if (!FastMath.InRange(pos, acPos, 40000f) && !FastMath.InRange(pos, playerBase, 25000f))
            {
                continue;
            }

            if (!GroundControlReflection.EnemyCanAnyStoreDefeat(aircraft, unit))
            {
                continue;
            }

            float score = CogTargetSelectorHelper.ScoreTargetCog(hq, unit, includeClusterContext: true);
            SalvoScratch.Add((unit, pos, score));
        }

        if (SalvoScratch.Count == 0)
        {
            __result = null;
            return false;
        }

        Unit? bestUnpenalized = null;
        float bestUnpenalizedScore = float.MinValue;
        Unit? bestFallback = null;
        float bestFallbackScore = float.MinValue;

        for (int i = 0; i < SalvoScratch.Count; i++)
        {
            (Unit candidate, GlobalPosition cPos, float cBase) = SalvoScratch[i];
            float clusterScore = cBase;
            for (int j = 0; j < SalvoScratch.Count; j++)
            {
                if (i != j && FastMath.InRange(cPos, SalvoScratch[j].Pos, 1500f))
                {
                    clusterScore += Mathf.Min(SalvoScratch[j].BaseScore * 0.25f, 35000f);
                }
            }

            if (clusterScore > bestFallbackScore)
            {
                bestFallbackScore = clusterScore;
                bestFallback = candidate;
            }

            if (!AiTacticalDoctrineService.Instance.IsForcedTargetRecentlyAssigned(hq, candidate, now)
                && clusterScore > bestUnpenalizedScore)
            {
                bestUnpenalizedScore = clusterScore;
                bestUnpenalized = candidate;
            }
        }

        SalvoScratch.Clear();

        Unit? chosen = bestUnpenalized ?? bestFallback;
        __result = chosen;
        if (chosen != null)
        {
            AiTacticalDoctrineService.Instance.NoteForcedTargetAssigned(hq, chosen, now);
        }

        return false;
    }
}

[HarmonyPatch]
internal static class GetCruiseHeightTacticalPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyGetCruiseHeightMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyGetCruiseHeightMethod!;

    private static void Postfix(FactionHQ hq, Aircraft aircraft, ref float __result)
    {
        if (!AddonConfig.EnableTacticalQualityAi.Value || hq == null || aircraft == null)
        {
            return;
        }

        if (GroundControlReflection.EnemyIsRadarPicket(aircraft.definition, hq))
        {
            __result = Mathf.Clamp(__result, 10500f, 12000f);
            return;
        }

        if (GroundControlReflection.AirHasBombAmmo(aircraft, true))
        {
            __result = Mathf.Clamp(__result, 10500f, 12000f);
            return;
        }

        if (HasJammerStore(aircraft))
        {
            __result = Mathf.Clamp(__result, 8500f, 10500f);
            return;
        }

        if (GroundControlReflection.IsRadarHunter(aircraft, hq))
        {
            // SEAD needs high radar horizon, ARAD seeker loft range, and terrain clearance
            __result = Mathf.Clamp(__result, 6500f, 9500f);
            return;
        }

        bool isCasNonGlide = GroundControlReflection.EnemyGetAirRoleInt(aircraft) == 0 && !GroundControlReflection.AirHasBombAmmo(aircraft, true);
        if (!isCasNonGlide)
        {
            return;
        }

        if (AiBattlefieldIntelService.Instance.HasLongRangeSamThreat(hq, aircraft.GlobalPosition(), 24000f))
        {
            __result = Mathf.Clamp(__result, 750f, 1100f);
        }
    }

    private static bool HasJammerStore(Aircraft aircraft)
    {
        List<WeaponStation>? stations = ((Unit)aircraft).weaponStations;
        if (stations == null)
        {
            return false;
        }

        for (int i = 0; i < stations.Count; i++)
        {
            WeaponInfo? wi = stations[i]?.WeaponInfo;
            if (wi != null && wi.jammer)
            {
                return true;
            }
        }

        return false;
    }
}

[HarmonyPatch]
internal static class PickJammerTargetsDeconflictPatch
{
    private static readonly List<(Unit Radar, GlobalPosition Pos, float DistToCorr)> RadarScratch = new(32);
    private static readonly HashSet<Unit> AssignedRadarScratch = new();

    private static bool Prepare()
        => GroundControlReflection.EnemyPickJammerTargetsMethod != null
            && GroundControlReflection.EnemyJammersField != null
            && GroundControlReflection.ActiveJammerCtor != null;

    private static MethodBase TargetMethod() => GroundControlReflection.EnemyPickJammerTargetsMethod!;

    private static bool Prefix(object __instance, FactionHQ localHq)
    {
        if (!AddonConfig.EnableTacticalQualityAi.Value)
        {
            return true;
        }

        if (GroundControlReflection.EnemyJammersField?.GetValue(__instance) is not IList jammers
            || GroundControlReflection.ActiveJammerCtor == null)
        {
            return true;
        }

        jammers.Clear();

        float levelNow = Time.timeSinceLevelLoad;
        foreach (FactionHQ aiHq in FactionRegistry.GetAllHQs())
        {
            if (aiHq == null
                || ReferenceEquals(aiHq, localHq)
                || !((NetworkBehaviour)aiHq).IsServer
                || !GroundControlReflection.IsAiFaction(aiHq, localHq)
                || aiHq.faction == null
                || aiHq.factionUnits == null)
            {
                continue;
            }

            GlobalPosition primaryObj = AiBattlefieldIntelService.Instance.GetPrimaryObjectivePos(aiHq, localHq);
            RadarScratch.Clear();
            AssignedRadarScratch.Clear();

            if (aiHq.trackingDatabase != null)
            {
                foreach (KeyValuePair<PersistentID, TrackingInfo> kv in aiHq.trackingDatabase)
                {
                    TrackingInfo info = kv.Value;
                    if (info == null || !info.TryGetUnit(out Unit r) || r == null || r.disabled)
                    {
                        continue;
                    }

                    if (r.NetworkHQ == null
                        || ReferenceEquals(r.NetworkHQ, aiHq)
                        || !r.HasRadarEmission()
                        || (!(r is Building) && levelNow - info.lastSpottedTime > 90f))
                    {
                        continue;
                    }

                    GlobalPosition pos = info.GetPosition();
                    float distCorr = !primaryObj.Equals(default) ? FastMath.Distance(pos, primaryObj) : 0f;
                    RadarScratch.Add((r, pos, distCorr));
                }
            }

            if (RadarScratch.Count == 0)
            {
                continue;
            }

            RadarScratch.Sort((a, b) => a.DistToCorr.CompareTo(b.DistToCorr));

            foreach (PersistentID pid in aiHq.factionUnits)
            {
                if (!pid.TryGetUnit(out Unit u) || u is not Aircraft ac || ac.disabled || ac.Player != null)
                {
                    continue;
                }

                if (GroundControlReflection.EnemyIsRadarPicket(ac.definition, aiHq) || ((Unit)ac).weaponStations == null)
                {
                    continue;
                }

                List<WeaponStation> stations = ((Unit)ac).weaponStations;
                for (int s = 0; s < stations.Count; s++)
                {
                    WeaponStation? station = stations[s];
                    WeaponInfo? wi = station?.WeaponInfo;
                    if (station == null || wi == null || !wi.jammer)
                    {
                        continue;
                    }

                    float maxRange = wi.targetRequirements.maxRange;
                    Unit? chosen = PickRadarForJammerPod(ac, maxRange, primaryObj);
                    if (chosen == null)
                    {
                        continue;
                    }

                    AssignedRadarScratch.Add(chosen);
                    for (int w = 0; w < station.Weapons.Count; w++)
                    {
                        if (station.Weapons[w] is JammingPod pod)
                        {
                            pod.SetTarget(chosen);
                            object activeJammer = GroundControlReflection.ActiveJammerCtor.Invoke(new object[] { ac, station, pod, chosen });
                            jammers.Add(activeJammer);
                        }
                    }
                }
            }
        }

        RadarScratch.Clear();
        AssignedRadarScratch.Clear();
        return false;
    }

    private static Unit? PickRadarForJammerPod(Aircraft ac, float maxRange, GlobalPosition primaryObj)
    {
        Vector3 acWorld = ac.transform.position;
        GlobalPosition acPos = ac.GlobalPosition();

        Unit? bestFree = null;
        float bestFreeScore = float.MaxValue;
        Unit? bestFallback = null;
        float bestFallbackScore = float.MaxValue;

        for (int i = 0; i < RadarScratch.Count; i++)
        {
            (Unit r, GlobalPosition rPos, float distToObj) = RadarScratch[i];
            float distToAc = FastMath.Distance(acPos, rPos);
            if (distToAc >= maxRange || !r.LineOfSight(acWorld, 1000f))
            {
                continue;
            }

            float score = distToObj * 0.7f + distToAc * 0.3f;
            if (score < bestFallbackScore)
            {
                bestFallbackScore = score;
                bestFallback = r;
            }

            if (!AssignedRadarScratch.Contains(r) && score < bestFreeScore)
            {
                bestFreeScore = score;
                bestFree = r;
            }
        }

        return bestFree ?? bestFallback;
    }
}

[HarmonyPatch]
internal static class ChooseAirRoleThreatPatch
{
    private const int AirRoleStrike = 0;
    private const int AirRoleFighter = 1;

    private static bool Prepare()
        => GroundControlReflection.EnemyChooseAirRoleMethod != null
            && GroundControlReflection.AirRoleType != null;

    private static MethodBase TargetMethod() => GroundControlReflection.EnemyChooseAirRoleMethod!;

    private static void Postfix(FactionHQ? hq, ref object __result)
    {
        if (!AddonConfig.EnableTacticalQualityAi.Value || hq == null || GroundControlReflection.AirRoleType == null)
        {
            return;
        }

        PlayerThreatSnapshot threat = AiStrategicDirectorService.Instance.ReadPlayerThreat(hq);

        // Ensure SEAD/Strike is chosen first when hostile SAM radars are active and no SEAD hunter is airborne
        if (AddonConfig.SeadBeforeStrike.Value
            && threat.SamRadars >= 1
            && AiTacticalDoctrineService.Instance.CountAirborneRadarHunters(hq) == 0
            && AiTacticalDoctrineService.Instance.HasDetectedEnemyRadar(hq))
        {
            __result = Enum.ToObject(GroundControlReflection.AirRoleType, AirRoleStrike);
            return;
        }

        if (threat.Aircraft >= 4 && hq.factionUnits != null)
        {
            int fighters = 0;
            int strikers = 0;
            foreach (PersistentID pid in hq.factionUnits)
            {
                if (!pid.TryGetUnit(out Unit u) || u is not Aircraft ac || ac.disabled || ac.Player != null)
                {
                    continue;
                }

                if (!GroundControlReflection.AirHasPlanePilot(ac) || GroundControlReflection.EnemyIsRadarPicket(ac.definition, hq))
                {
                    continue;
                }

                int role = GroundControlReflection.EnemyGetAirRoleInt(ac);
                if (role == AirRoleFighter)
                {
                    fighters++;
                }
                else if (role == AirRoleStrike)
                {
                    strikers++;
                }
            }

            int desiredFighters = Mathf.Clamp(Mathf.Max(2, threat.Aircraft / 2), 2, Mathf.Max(3, strikers + 2));
            if (fighters < desiredFighters && strikers >= 1)
            {
                __result = Enum.ToObject(GroundControlReflection.AirRoleType, AirRoleFighter);
            }
        }
    }
}

[HarmonyPatch]
internal static class LocateBatteryIntelPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyLocateBatteryMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyLocateBatteryMethod!;

    private static void Postfix(FactionHQ? hq, PersistentID by)
    {
        if (!AddonConfig.EnableTacticalQualityAi.Value || hq == null || !((NetworkBehaviour)hq).IsServer || !by.IsValid)
        {
            return;
        }

        if (by.TryGetUnit(out Unit attacker)
            && attacker != null
            && !attacker.disabled
            && !ReferenceEquals(attacker.NetworkHQ, hq)
            && attacker.definition is VehicleDefinition vdef
            && vdef.vehicleType == VehicleType.ART)
        {
            AiBattlefieldIntelService.Instance.ReportLocatedHostileArtillery(hq, attacker);
        }
    }
}

[HarmonyPatch]
internal static class RaiseEcoAlarmQrfPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyRaiseEcoAlarmMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyRaiseEcoAlarmMethod!;

    private static void Postfix(FactionHQ? hq, Unit? building, PersistentID by)
    {
        if (!AddonConfig.EnableTacticalQualityAi.Value || hq == null || !((NetworkBehaviour)hq).IsServer || building == null)
        {
            return;
        }

        Unit? attacker = null;
        if (by.IsValid)
        {
            by.TryGetUnit(out attacker);
        }

        AiBattlefieldIntelService.Instance.ReportRearAreaAlarm(hq, building.GlobalPosition(), attacker);
    }
}

[HarmonyPatch]
internal static class ReleaseNavalWaveTacticalPatch
{
    private static readonly List<Ship> StaleShipsScratch = new(16);

    private static bool Prepare() => GroundControlReflection.EnemyReleaseNavalWaveMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyReleaseNavalWaveMethod!;

    private static bool Prefix(FactionHQ? hq, FactionHQ? localHq, object? state)
    {
        if (!AddonConfig.EnableTacticalQualityAi.Value || hq == null || state == null)
        {
            return true;
        }

        GlobalPosition rallyPos = GroundControlReflection.EcoTryGetNavalDockPosition(hq, out GlobalPosition dockPos)
            ? dockPos
            : GroundControlReflection.CaptureGetTerritoryCenter(hq);

        if (!GroundControlReflection.NavalTryGetSeaLanePoint(rallyPos, out GlobalPosition rallyLanePoint))
        {
            return true;
        }

        // Pull back heavily damaged capital ships (< 35% condition)
        if (hq.factionUnits != null)
        {
            float now = Time.time;
            foreach (PersistentID pid in hq.factionUnits)
            {
                if (!pid.TryGetUnit(out Unit u) || u is not Ship s || s.disabled || s.definition is not ShipDefinition sdef)
                {
                    continue;
                }

                bool isCapital = sdef.shipType <= ShipType.DDG || sdef.value >= 300f;
                if (isCapital
                    && GroundControlReflection.GetUnitCondition(u) < 0.35f
                    && !FastMath.InRange(u.GlobalPosition(), rallyLanePoint, 2500f)
                    && AiTacticalDoctrineService.Instance.ShouldIssueNavalRetreat(u, now))
                {
                    GroundControlReflection.TrySetDestination(u, rallyLanePoint);
                }
            }
        }

        if (GroundControlReflection.StateNavalRallyingProp?.GetValue(state) is not HashSet<Ship> rallying)
        {
            return true;
        }

        rallying.RemoveWhere(static ship => ship == null || ((Unit)ship).disabled || GroundControlReflection.GetUnitCondition((Unit)ship) < 0.35f);
        if (rallying.Count == 0)
        {
            GroundControlReflection.SetStateFloat(state, GroundControlReflection.StateNavalWaitingSinceProp, 0f);
            return false;
        }

        float waitingSince = GroundControlReflection.GetStateFloat(state, GroundControlReflection.StateNavalWaitingSinceProp);
        if (waitingSince <= 0f)
        {
            waitingSince = Time.time;
            GroundControlReflection.SetStateFloat(state, GroundControlReflection.StateNavalWaitingSinceProp, waitingSince);
        }

        if (!TryPickNavalTargetSeaLane(hq, localHq, out GlobalPosition targetLanePoint))
        {
            return true;
        }

        int readyCount = 0;
        foreach (Ship ship in rallying)
        {
            if (FastMath.InRange(((Unit)ship).GlobalPosition(), rallyLanePoint, 2500f))
            {
                readyCount++;
            }
        }

        bool timedOut = Time.time - waitingSince > 300f;
        if (readyCount < 3 && (!timedOut || readyCount < 2))
        {
            return false;
        }

        StaleShipsScratch.Clear();
        foreach (Ship ship in rallying)
        {
            if (FastMath.InRange(((Unit)ship).GlobalPosition(), rallyLanePoint, 2500f))
            {
                GroundControlReflection.TrySetDestination((Unit)ship, targetLanePoint);
                StaleShipsScratch.Add(ship);
            }
        }

        for (int i = 0; i < StaleShipsScratch.Count; i++)
        {
            rallying.Remove(StaleShipsScratch[i]);
        }

        StaleShipsScratch.Clear();
        GroundControlReflection.SetStateFloat(
            state,
            GroundControlReflection.StateNavalWaitingSinceProp,
            rallying.Count > 0 ? Time.time : 0f);
        return false;
    }

    private static bool TryPickNavalTargetSeaLane(FactionHQ aiHq, FactionHQ? localHq, out GlobalPosition targetLane)
    {
        targetLane = default;
        float bestShipScore = float.MinValue;
        GlobalPosition bestShipPos = default;
        bool foundShip = false;
        float levelNow = Time.timeSinceLevelLoad;

        if (aiHq.trackingDatabase != null)
        {
            foreach (KeyValuePair<PersistentID, TrackingInfo> kv in aiHq.trackingDatabase)
            {
                TrackingInfo info = kv.Value;
                if (info == null || !info.TryGetUnit(out Unit u) || u is not Ship || u.disabled || u.definition == null)
                {
                    continue;
                }

                if (u.NetworkHQ == null || ReferenceEquals(u.NetworkHQ, aiHq) || levelNow - info.lastSpottedTime > 120f)
                {
                    continue;
                }

                float s = u.definition.value;
                if (s > bestShipScore)
                {
                    bestShipScore = s;
                    bestShipPos = info.GetPosition();
                    foundShip = true;
                }
            }
        }

        if (foundShip && GroundControlReflection.NavalTryGetSeaLanePoint(bestShipPos, out targetLane))
        {
            return true;
        }

        // Fallback: enemy coastal airbase near sea lanes
        float bestBaseDist = float.MaxValue;
        GlobalPosition ownCenter = GroundControlReflection.CaptureGetTerritoryCenter(aiHq);
        foreach (Airbase ab in FactionRegistry.airbaseLookup.Values)
        {
            if (ab == null || ab.disabled || ab.center == null || ab.CurrentHQ == null || ReferenceEquals(ab.CurrentHQ, aiHq))
            {
                continue;
            }

            GlobalPosition abPos = ab.center.GlobalPosition();
            if (GroundControlReflection.NavalTryGetSeaLanePoint(abPos, out GlobalPosition seaPt)
                && FastMath.InRange(abPos, seaPt, 12000f))
            {
                float d = FastMath.Distance(ownCenter, abPos);
                if (d < bestBaseDist)
                {
                    bestBaseDist = d;
                    targetLane = seaPt;
                }
            }
        }

        if (bestBaseDist < float.MaxValue)
        {
            return true;
        }

        if (localHq != null)
        {
            return GroundControlReflection.NavalTryGetSeaLanePoint(
                GroundControlReflection.CaptureGetTerritoryCenter(localHq),
                out targetLane);
        }

        return false;
    }
}

[HarmonyPatch]
internal static class LaunchWaveCombinedArmsPatch
{
    private static bool Prepare() => GroundControlReflection.LaunchWaveIfFormedMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.LaunchWaveIfFormedMethod!;

    private static void Prefix(object __instance, ref int required)
    {
        if (!AddonConfig.EnableTacticalQualityAi.Value || required > 0)
        {
            return;
        }

        if (GroundControlReflection.CaptureSquadField?.GetValue(__instance) is List<Unit> squad && squad.Count >= 4)
        {
            // Keep combined-arms groups cohesive at the form-up point (respects GC's 240s form-up timeout).
            required = Mathf.Min(squad.Count, Mathf.Max(6, Mathf.CeilToInt(squad.Count * 0.7f)));
        }
    }
}

[HarmonyPatch]
internal static class ReviewEnemiesAdaptationPatch
{
    private static bool Prepare() => GroundControlReflection.ReviewEnemiesCaptureMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.ReviewEnemiesCaptureMethod!;

    private static void Postfix(object __instance)
    {
        if (AddonConfig.EnableTacticalQualityAi.Value && AddonConfig.EnableLossAdaptation.Value)
        {
            AiTacticalDoctrineService.Instance.ObserveDrives(__instance);
        }
    }
}

[HarmonyPatch]
internal static class GetLaneFormUpFlankPatch
{
    private static bool Prepare() => GroundControlReflection.GetLaneFormUpMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.GetLaneFormUpMethod!;

    private static void Postfix(GlobalPosition hold, Unit unit, ref GlobalPosition __result)
    {
        if (!AddonConfig.EnableTacticalQualityAi.Value || !AddonConfig.EnableLossAdaptation.Value || unit == null || unit.NetworkHQ == null)
        {
            return;
        }

        Airbase? targetBase = FindNearestAirbaseNear(hold, 1200f);
        if (!AiTacticalDoctrineService.Instance.TryGetFlankAngleOffsetDegrees(unit.NetworkHQ, targetBase, out float angleDeg))
        {
            return;
        }

        float dx = __result.x - hold.x;
        float dz = __result.z - hold.z;
        if (dx * dx + dz * dz < 1f)
        {
            return;
        }

        float rad = angleDeg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        var candidate = new GlobalPosition(
            hold.x + dx * cos - dz * sin,
            __result.y,
            hold.z + dx * sin + dz * cos);

        GlobalPosition snapped = GroundControlReflection.SnapToTerrain(candidate);
        if (!GroundControlReflection.IsBelowSeaLevel(snapped)
            && GroundControlReflection.CanDriveBetween(hold, snapped)
            && GroundControlReflection.TerrainRouteIsDrivable(hold, snapped))
        {
            __result = snapped;
        }
    }

    private static Airbase? FindNearestAirbaseNear(GlobalPosition pos, float maxRadiusMeters)
    {
        Airbase? best = null;
        float bestSq = maxRadiusMeters * maxRadiusMeters;
        foreach (Airbase ab in FactionRegistry.airbaseLookup.Values)
        {
            if (ab == null || ab.disabled || ab.center == null)
            {
                continue;
            }

            float dSq = FastMath.SquareDistance(pos, ab.center.GlobalPosition());
            if (dSq < bestSq)
            {
                bestSq = dSq;
                best = ab;
            }
        }

        return best;
    }
}

[HarmonyPatch]
internal static class FindNearestHeldBaseTacticalPatch
{
    private static bool Prepare() => GroundControlReflection.CaptureFindNearestHeldBaseMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.CaptureFindNearestHeldBaseMethod!;

    private static bool Prefix(object __instance, FactionHQ? hq, ref Airbase? __result)
    {
        if (!AddonConfig.EnableTacticalQualityAi.Value
            || !AddonConfig.EnableAiCombinedArmsSync.Value
            || hq == null)
        {
            return true;
        }

        IList<Airbase>? staging = GroundControlReflection.CaptureStagingBasesField?.GetValue(__instance) as IList<Airbase>;
        Airbase? best = AiBattlefieldIntelService.Instance.SelectBestAssaultTarget(hq, staging);
        if (best == null)
        {
            return true;
        }

        __result = best;
        return false;
    }
}

[HarmonyPatch]
internal static class ReviewMainEffortAdaptationPatch
{
    private static readonly List<(object Drive, int Slot, Airbase Staging, Airbase Target)> StalledDrivesScratch = new(8);

    private static bool Prepare() => GroundControlReflection.CaptureReviewMainEffortMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.CaptureReviewMainEffortMethod!;

    private static void Prefix(object __instance, FactionHQ? hq)
    {
        if (!AddonConfig.EnableTacticalQualityAi.Value
            || !AddonConfig.EnableLossAdaptation.Value
            || hq == null)
        {
            return;
        }

        if (GroundControlReflection.CaptureDrivesField?.GetValue(__instance) is not IDictionary drives)
        {
            return;
        }

        StalledDrivesScratch.Clear();
        foreach (DictionaryEntry entry in drives)
        {
            if (entry.Value == null || entry.Key is not ValueTuple<Airbase, int> slotKey)
            {
                continue;
            }

            if (GroundControlReflection.DriveOwnerProp?.GetValue(entry.Value) is FactionHQ owner
                && ReferenceEquals(owner, hq)
                && GroundControlReflection.DriveTargetProp?.GetValue(entry.Value) is Airbase target
                && AiTacticalDoctrineService.Instance.IsTargetStalled(hq, target))
            {
                StalledDrivesScratch.Add((entry.Value, slotKey.Item2, slotKey.Item1, target));
            }
        }

        if (StalledDrivesScratch.Count == 0)
        {
            return;
        }

        IList<Airbase>? stagingBases = GroundControlReflection.CaptureStagingBasesField?.GetValue(__instance) as IList<Airbase>;
        Airbase? betterMain = AiBattlefieldIntelService.Instance.SelectBestAssaultTarget(hq, stagingBases);

        for (int i = 0; i < StalledDrivesScratch.Count; i++)
        {
            (object drive, int slot, Airbase staging, Airbase stalledTarget) = StalledDrivesScratch[i];
            if (slot == -1)
            {
                if (betterMain != null && betterMain != stalledTarget && !AiTacticalDoctrineService.Instance.IsTargetStalled(hq, betterMain))
                {
                    GroundControlReflection.CaptureReleaseDrive(drive);
                    GroundControlReflection.CaptureClearDriveTarget(drive);
                }
            }
            else if (HasAlternativeDriveTarget(hq, staging, stalledTarget))
            {
                GroundControlReflection.CaptureReleaseDrive(drive);
                GroundControlReflection.CaptureClearDriveTarget(drive);
            }
        }

        StalledDrivesScratch.Clear();
    }

    private static bool HasAlternativeDriveTarget(FactionHQ hq, Airbase? staging, Airbase stalledTarget)
    {
        if (staging == null || staging.center == null)
        {
            return false;
        }

        GlobalPosition stagingPos = staging.center.GlobalPosition();
        foreach (Airbase candidate in FactionRegistry.airbaseLookup.Values)
        {
            if (candidate == null
                || candidate == stalledTarget
                || candidate.disabled
                || candidate.center == null
                || ReferenceEquals(candidate.CurrentHQ, hq)
                || !GroundControlReflection.IsLandBase(candidate)
                || !GroundControlReflection.IsTakeable(candidate, hq)
                || AiTacticalDoctrineService.Instance.IsTargetStalled(hq, candidate))
            {
                continue;
            }

            GlobalPosition candPos = candidate.center.GlobalPosition();
            if (FastMath.Distance(stagingPos, candPos) <= 70000f
                && GroundControlReflection.CanDriveBetween(stagingPos, candPos))
            {
                return true;
            }
        }

        return false;
    }
}

[HarmonyPatch]
internal static class ChooseEnemyTargetAdaptationPatch
{
    private static bool Prepare() => GroundControlReflection.ChooseEnemyTarget4Method != null;
    private static MethodBase TargetMethod() => GroundControlReflection.ChooseEnemyTarget4Method!;

    private static void Postfix(FactionHQ hq, Airbase staging, bool neutralOnly, ref Airbase? __result)
    {
        if (neutralOnly || !AddonConfig.EnableTacticalQualityAi.Value || !AddonConfig.EnableLossAdaptation.Value)
        {
            return;
        }

        if (hq == null || staging == null || __result == null || __result.CurrentHQ == null)
        {
            return;
        }

        if (!AiTacticalDoctrineService.Instance.IsTargetStalled(hq, __result))
        {
            return;
        }

        GlobalPosition stagingPos = staging.center.GlobalPosition();
        Airbase? alternative = null;
        float bestDist = float.MaxValue;

        foreach (Airbase candidate in FactionRegistry.airbaseLookup.Values)
        {
            if (candidate == null
                || candidate == __result
                || candidate.CurrentHQ == null
                || !GroundControlReflection.IsLandBase(candidate)
                || !GroundControlReflection.IsTakeable(candidate, hq)
                || AiTacticalDoctrineService.Instance.IsTargetStalled(hq, candidate))
            {
                continue;
            }

            GlobalPosition candidatePos = candidate.center.GlobalPosition();
            float dist = FastMath.Distance(stagingPos, candidatePos);
            if (dist > 70000f || !GroundControlReflection.CanDriveBetween(stagingPos, candidatePos))
            {
                continue;
            }

            if (dist < bestDist)
            {
                bestDist = dist;
                alternative = candidate;
            }
        }

        if (alternative != null)
        {
            __result = alternative;
        }
    }
}

[HarmonyPatch]
internal static class MdsSeadFindRadarToHuntPatch
{
    private static bool Prepare() => GroundControlReflection.FindRadarToHuntMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.FindRadarToHuntMethod!;

    private static void Postfix(FactionHQ hq, GlobalPosition from, ref GlobalPosition at, ref Unit? __result)
    {
        if (!AddonConfig.EnableTacticalQualityAi.Value
            || !AddonConfig.SeadBeforeStrike.Value
            || MdsBridgeBootstrap.TryFindPriorityMdsSeadTarget == null
            || hq == null)
        {
            return;
        }

        (Unit? mdsTarget, GlobalPosition mdsPos) = MdsBridgeBootstrap.TryFindPriorityMdsSeadTarget(hq, from);
        if (mdsTarget == null || mdsTarget.disabled)
        {
            return;
        }

        if (__result == null
            || __result.disabled
            || FastMath.Distance(from, mdsPos) < FastMath.Distance(from, at) * 1.35f)
        {
            __result = mdsTarget;
            at = mdsPos;
        }
    }
}

[HarmonyPatch]
internal static class IsSquadCandidateManeuverGuardPatch
{
    private static bool Prepare() => GroundControlReflection.CaptureIsSquadCandidateMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.CaptureIsSquadCandidateMethod!;

    private static void Postfix(Unit? unit, ref bool __result)
    {
        if (__result && AiOperationalManeuverService.Instance.IsUnitManaged(unit))
        {
            __result = false;
        }
    }
}

[HarmonyPatch]
internal static class IsRecruitableManeuverGuardPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyIsRecruitableMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyIsRecruitableMethod!;

    private static void Postfix(Unit? unit, ref bool __result)
    {
        if (__result && AiOperationalManeuverService.Instance.IsUnitManaged(unit))
        {
            __result = false;
        }
    }
}

[HarmonyPatch]
internal static class PinToManeuverGuardPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyPinToMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyPinToMethod!;

    private static bool Prefix(Unit? unit)
        => AiOperationalManeuverService.IsIssuingManagedOrder || !AiOperationalManeuverService.Instance.IsUnitManaged(unit);
}

[HarmonyPatch]
internal static class HoldAtManeuverGuardPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyHoldAtMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyHoldAtMethod!;

    private static bool Prefix(Unit? unit)
        => AiOperationalManeuverService.IsIssuingManagedOrder || !AiOperationalManeuverService.Instance.IsUnitManaged(unit);
}

[HarmonyPatch]
internal static class ReleaseCommandedDestinationManeuverGuardPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyReleaseCommandedDestinationMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyReleaseCommandedDestinationMethod!;

    private static bool Prefix(Unit? unit)
        => AiOperationalManeuverService.IsIssuingManagedOrder || !AiOperationalManeuverService.Instance.IsUnitManaged(unit);
}
