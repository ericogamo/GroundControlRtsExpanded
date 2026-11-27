using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GroundControlRtsExpanded;

[HarmonyPatch]
internal static class ReviewContextGuardPatch
{
    internal static bool InEnemyReview { get; private set; }
    internal static FactionHQ? CurrentReviewHq { get; private set; }
    internal static FactionHQ? ActiveAiHq { get; set; }
    internal static int GroundPicksThisReview { get; set; }

    internal static void ResetSession()
    {
        InEnemyReview = false;
        CurrentReviewHq = null;
        ActiveAiHq = null;
        GroundPicksThisReview = 0;
    }

    private static bool Prepare() => GroundControlReflection.EnemyReviewMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyReviewMethod!;

    private static void Prefix(FactionHQ hq, out FactionHQ? __state)
    {
        __state = ActiveAiHq;
        ActiveAiHq = hq;
        InEnemyReview = true;
        CurrentReviewHq = hq;
        GroundPicksThisReview = 0;
    }

    private static Exception? Finalizer(Exception? __exception, FactionHQ? __state)
    {
        ActiveAiHq = __state;
        InEnemyReview = false;
        CurrentReviewHq = null;
        GroundPicksThisReview = 0;
        return __exception;
    }
}

[HarmonyPatch]
internal static class ReviewDefenceContextGuardPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyReviewDefenceMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyReviewDefenceMethod!;

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
internal static class IsChallengingDecouplePatch
{
    private static bool Prepare() => GroundControlReflection.IsChallengingMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.IsChallengingMethod!;

    private static void Postfix(FactionHQ? hq, ref bool __result)
    {
        if (AddonConfig.EnableTacticalQualityAi.Value
            && !AiEconomySettings.GetActiveProfile().IsVanilla
            && hq != null
            && hq.faction != null
            && ((Mirage.NetworkBehaviour)hq).IsServer)
        {
            // Unlock counter-buying, main-effort pushes, anti-ship sorties, and smart base expansion independent of cheat funding.
            __result = true;
        }
    }
}

[HarmonyPatch]
internal static class ScalePurchasesCapPatch
{
    private static bool Prepare() => GroundControlReflection.ScalePurchasesMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.ScalePurchasesMethod!;

    private static void Postfix(int baseline, int perDecade, ref int __result)
    {
        // Guard against firing during GC startup self-checks (which run outside Review).
        if (!ReviewContextGuardPatch.InEnemyReview || !AddonConfig.EnableTacticalQualityAi.Value)
        {
            return;
        }

        int cap = Mathf.Max(1, AddonConfig.MaxGroundBuysPerReview.Value);
        if (perDecade == 1)
        {
            __result = Mathf.Min(__result, Mathf.Max(2, cap));
        }
        else if (baseline == 3 && perDecade == 3)
        {
            __result = Mathf.Min(__result, cap);
        }
    }
}

internal static class GroundBuyCapLogic
{
    internal static bool ShouldBlockPick()
    {
        return ReviewContextGuardPatch.InEnemyReview
            && AddonConfig.EnableTacticalQualityAi.Value
            && ReviewContextGuardPatch.GroundPicksThisReview >= Mathf.Max(1, AddonConfig.MaxGroundBuysPerReview.Value);
    }

    internal static void NotePicked(VehicleDefinition? result)
    {
        if (result != null && ReviewContextGuardPatch.InEnemyReview && AddonConfig.EnableTacticalQualityAi.Value)
        {
            ReviewContextGuardPatch.GroundPicksThisReview++;
        }
    }
}

[HarmonyPatch]
internal static class PickBuyCapPatch
{
    private static bool Prepare() => GroundControlReflection.PickMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.PickMethod!;

    private static bool Prefix(object __instance, float budget, bool airDefenceOnly, ref VehicleDefinition? __result)
    {
        if (airDefenceOnly)
        {
            return true;
        }

        if (GroundBuyCapLogic.ShouldBlockPick())
        {
            __result = null;
            return false;
        }

        if (ReviewContextGuardPatch.InEnemyReview && AddonConfig.EnableTacticalQualityAi.Value)
        {
            FactionHQ? hq = ReviewContextGuardPatch.CurrentReviewHq;
            if (hq != null
                && GroundControlReflection.GetBestDepotLevel(hq) >= 2
                && GroundControlReflection.GetEnemyOwnedCombat(__instance) >= 6
                && GroundControlReflection.EnemyCatalogField?.GetValue(__instance) is List<VehicleDefinition> catalog
                && catalog.Count > 0)
            {
                float minQualityCost = float.MaxValue;
                for (int i = 0; i < catalog.Count; i++)
                {
                    VehicleDefinition v = catalog[i];
                    if (v == null
                        || GroundControlReflection.IsAirDefence(v)
                        || GroundControlReflection.IsBuilderVehicle(v)
                        || GroundControlReflection.IsSupplyVehicle(v)
                        || !GroundControlReflection.IsCombatVehicle(v))
                    {
                        continue;
                    }

                    if (GroundControlReflection.GetRequiredDepotLevel(v) >= 2
                        && v.value > 0f
                        && v.value < minQualityCost)
                    {
                        minQualityCost = v.value;
                    }
                }

                if (minQualityCost < float.MaxValue)
                {
                    minQualityCost = Mathf.Min(minQualityCost, 35f);
                    if (budget < minQualityCost)
                    {
                        __result = null;
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static void Postfix(bool airDefenceOnly, VehicleDefinition? __result)
    {
        if (!airDefenceOnly)
        {
            GroundBuyCapLogic.NotePicked(__result);
        }
    }
}

[HarmonyPatch]
internal static class ChooseCaptureUnitCapPatch
{
    private static bool Prepare() => GroundControlReflection.ChooseCaptureUnitMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.ChooseCaptureUnitMethod!;

    private static bool Prefix(ref VehicleDefinition? __result)
    {
        if (!GroundBuyCapLogic.ShouldBlockPick())
        {
            return true;
        }

        __result = null;
        return false;
    }

    private static void Postfix(VehicleDefinition? __result)
        => GroundBuyCapLogic.NotePicked(__result);
}

[HarmonyPatch]
internal static class CollectCatalogFilterPatch
{
    private static bool Prepare() => GroundControlReflection.CollectCatalogMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.CollectCatalogMethod!;

    private static void Postfix(object __instance, FactionHQ hq)
    {
        int thresholdDepot = AddonConfig.BlockCheapSpamAtDepotLevel.Value;
        if (!AddonConfig.EnableTacticalQualityAi.Value || thresholdDepot <= 0 || hq == null)
        {
            return;
        }

        int bestDepot = GroundControlReflection.GetBestDepotLevel(hq);
        if (bestDepot < thresholdDepot)
        {
            return;
        }

        if (GroundControlReflection.EnemyCatalogField?.GetValue(__instance) is not List<VehicleDefinition> catalog || catalog.Count < 3)
        {
            return;
        }

        float maxCombatValue = 0f;
        int captureCount = 0;
        int aaCount = 0;

        for (int i = 0; i < catalog.Count; i++)
        {
            VehicleDefinition v = catalog[i];
            if (v == null)
            {
                continue;
            }

            if (v.captureStrength > 0f && GroundControlReflection.IsCombatVehicle(v) && !GroundControlReflection.IsAirDefence(v))
            {
                captureCount++;
            }

            if (GroundControlReflection.IsAirDefence(v))
            {
                aaCount++;
            }

            if (IsRegularFrontlineCombatVehicle(v) && v.value > maxCombatValue)
            {
                maxCombatValue = v.value;
            }
        }

        if (maxCombatValue > 0f)
        {
            float minKeepValue = maxCombatValue * 0.35f;
            int higherTierCombatCount = 0;
            for (int i = 0; i < catalog.Count; i++)
            {
                VehicleDefinition v = catalog[i];
                if (v != null && IsRegularFrontlineCombatVehicle(v) && !IsCheapThrowaway(v, bestDepot, minKeepValue))
                {
                    higherTierCombatCount++;
                }
            }

            if (higherTierCombatCount >= 1)
            {
                for (int i = catalog.Count - 1; i >= 0; i--)
                {
                    VehicleDefinition v = catalog[i];
                    if (v == null || IsProtectedRole(v) || !IsRegularFrontlineCombatVehicle(v))
                    {
                        continue;
                    }

                    if (!IsCheapThrowaway(v, bestDepot, minKeepValue))
                    {
                        continue;
                    }

                    if (v.captureStrength > 0f && captureCount <= 1)
                    {
                        continue;
                    }

                    if (GroundControlReflection.IsAirDefence(v) && aaCount <= 1)
                    {
                        continue;
                    }

                    if (v.captureStrength > 0f)
                    {
                        captureCount--;
                    }

                    catalog.RemoveAt(i);
                }
            }
        }

        // At Depot Level 2+, filter out ultra-cheap non-convoy light AA so heavy AD trucks fill base slots.
        if (bestDepot >= 2)
        {
            int heavyAdCount = 0;
            for (int i = 0; i < catalog.Count; i++)
            {
                VehicleDefinition v = catalog[i];
                if (v != null
                    && GroundControlReflection.IsAirDefence(v)
                    && !GroundControlReflection.IsRadarGuidedAirDefence(v)
                    && v.value >= 50f)
                {
                    heavyAdCount++;
                }
            }

            if (heavyAdCount >= 2)
            {
                for (int i = catalog.Count - 1; i >= 0; i--)
                {
                    VehicleDefinition v = catalog[i];
                    if (v != null
                        && GroundControlReflection.IsAirDefence(v)
                        && !GroundControlReflection.IsRadarGuidedAirDefence(v)
                        && !GroundControlReflection.IsConvoyAirDefence(v)
                        && v.value < 20f)
                    {
                        catalog.RemoveAt(i);
                    }
                }
            }
        }
    }

    private static bool IsProtectedRole(VehicleDefinition v)
    {
        return GroundControlReflection.IsBuilderVehicle(v)
            || GroundControlReflection.IsSupplyVehicle(v)
            || GroundControlReflection.IsAirDefence(v)
            || GroundControlReflection.IsRadarGuidedAirDefence(v)
            || v.vehicleType == VehicleType.ART
            || v.vehicleType == VehicleType.RDR;
    }

    private static bool IsRegularFrontlineCombatVehicle(VehicleDefinition v)
    {
        return GroundControlReflection.IsCombatVehicle(v)
            && !GroundControlReflection.IsAirDefence(v)
            && v.vehicleType != VehicleType.ART
            && v.vehicleType != VehicleType.RDR;
    }

    private static bool IsCheapThrowaway(VehicleDefinition v, int bestDepot, float minKeepValue)
    {
        return GroundControlReflection.GetRequiredDepotLevel(v) < bestDepot && v.value < minKeepValue;
    }
}

[HarmonyPatch]
internal static class ChooseDearLeanPatch
{
    private static bool Prepare() => GroundControlReflection.ChooseMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.ChooseMethod!;

    private static void Prefix(object __instance)
    {
        if (!AddonConfig.EnableTacticalQualityAi.Value || GroundControlReflection.EnemyDearLeanField == null)
        {
            return;
        }

        float desired = Mathf.Clamp01(AddonConfig.QualityDearLean.Value);
        GroundControlReflection.EnemyDearLeanField.SetValue(__instance, desired);
    }
}

[HarmonyPatch]
internal static class CounterWeightQualityPatch
{
    private static bool Prepare() => GroundControlReflection.CounterWeightMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.CounterWeightMethod!;

    private static void Postfix(float offence, float exposure, float value, ref float __result)
    {
        // Keep GC startup self-checks untouched outside Review.
        if (!ReviewContextGuardPatch.InEnemyReview || !AddonConfig.EnableTacticalQualityAi.Value || __result <= 0f)
        {
            return;
        }

        float lean = Mathf.Clamp01(AddonConfig.QualityDearLean.Value);
        if (lean > 0.6f)
        {
            __result *= Mathf.Pow(Mathf.Max(1f, value), lean - 0.6f);
        }

        FactionHQ? hq = ReviewContextGuardPatch.ActiveAiHq;
        if (hq == null)
        {
            return;
        }

        PlayerThreatSnapshot threat = AiStrategicDirectorService.Instance.ReadPlayerThreat(hq);
        if (threat.HeavyArmor >= 3 && offence >= 0.35f)
        {
            float armorBias = 1.0f + Mathf.Clamp01((threat.HeavyArmor - 2f) / 6f) * 0.45f;
            __result *= armorBias;
        }

        AiStrategicPhase phase = AiStrategicDirectorService.Instance.EvaluatePhase(GroundControlReflection.GetEcoInstance(), hq);
        if (phase == AiStrategicPhase.EmergencyReconstruction)
        {
            if (value >= 15f && value <= 95f && exposure < 0.65f)
            {
                __result *= 1.35f;
            }
            else if (value > 140f)
            {
                __result *= 0.80f;
            }
        }
    }
}

[HarmonyPatch]
internal static class CompactIadsTargetPatch
{
    private static bool Prepare() => GroundControlReflection.ChallengingAirDefenceTargetMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.ChallengingAirDefenceTargetMethod!;

    private static void Postfix(int bases, ref int __result)
    {
        if (AddonConfig.EnableTacticalQualityAi.Value && bases > 0)
        {
            // Scale IADS target by depot tier, bases, and player air threat so Lvl-1 AA never blocks upgrades.
            __result = AiStrategicDirectorService.Instance.GetScaledIadsTarget(
                ReviewContextGuardPatch.ActiveAiHq,
                bases);
        }
    }
}

[HarmonyPatch]
internal static class ResolveDefenceQualityPatch
{
    private static bool Prepare() => GroundControlReflection.ResolveCategoryDefinitionMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.ResolveCategoryDefinitionMethod!;

    private static void Prefix(BuildingType type, ref bool preferDearest)
    {
        if (AddonConfig.EnableTacticalQualityAi.Value && type == BuildingType.DEF)
        {
            preferDearest = true;
        }
    }
}
