using System;
using System.Reflection;
using HarmonyLib;

namespace GroundControlRtsExpanded;

[HarmonyPatch]
internal static class UnitCapEnabledPatch
{
    private static bool Prepare() => GroundControlReflection.UnitCapEnabledGetter != null;
    private static MethodBase TargetMethod() => GroundControlReflection.UnitCapEnabledGetter!;

    private static void Postfix(ref bool __result)
    {
        if (DynamicCapAndTechService.IsActive)
        {
            __result = true;
        }
    }
}

[HarmonyPatch]
internal static class UnitCapCapPatch
{
    private static bool Prepare() => GroundControlReflection.UnitCapCapGetter != null;
    private static MethodBase TargetMethod() => GroundControlReflection.UnitCapCapGetter!;

    private static void Postfix(ref int __result)
    {
        if (!DynamicCapAndTechService.IsActive)
        {
            return;
        }

        FactionHQ? targetHq = UnitCapHqScope.ResolveCapTargetHq();
        if (targetHq != null)
        {
            __result = DynamicCapAndTechService.Instance.GetSnapshot(targetHq).TotalCap;
        }
    }
}

[HarmonyPatch]
internal static class UnitCapGroundCapPatch
{
    private static bool Prepare() => GroundControlReflection.UnitCapGroundCapGetter != null;
    private static MethodBase TargetMethod() => GroundControlReflection.UnitCapGroundCapGetter!;

    private static void Postfix(ref int __result)
    {
        if (!DynamicCapAndTechService.IsActive)
        {
            return;
        }

        FactionHQ? targetHq = UnitCapHqScope.ResolveCapTargetHq();
        if (targetHq != null)
        {
            __result = DynamicCapAndTechService.Instance.GetSnapshot(targetHq).GroundCap;
        }
    }
}

[HarmonyPatch]
internal static class UnitCapAirDefenceCapPatch
{
    private static bool Prepare() => GroundControlReflection.UnitCapAirDefenceCapGetter != null;
    private static MethodBase TargetMethod() => GroundControlReflection.UnitCapAirDefenceCapGetter!;

    private static void Postfix(ref int __result)
    {
        if (!DynamicCapAndTechService.IsActive)
        {
            return;
        }

        FactionHQ? targetHq = UnitCapHqScope.ResolveCapTargetHq();
        if (targetHq == null)
        {
            return;
        }

        int groundCap = DynamicCapAndTechService.Instance.GetSnapshot(targetHq).GroundCap;
        __result = UnityEngine.Mathf.Max(24, UnityEngine.Mathf.CeilToInt(groundCap * 0.50f));
    }
}

[HarmonyPatch]
internal static class UnitCapAircraftCapPatch
{
    private static bool Prepare() => GroundControlReflection.UnitCapAircraftCapGetter != null;
    private static MethodBase TargetMethod() => GroundControlReflection.UnitCapAircraftCapGetter!;

    private static void Postfix(ref int __result)
    {
        if (!DynamicCapAndTechService.IsActive)
        {
            return;
        }

        FactionHQ? targetHq = UnitCapHqScope.ResolveCapTargetHq();
        if (targetHq != null)
        {
            __result = DynamicCapAndTechService.Instance.GetSnapshot(targetHq).AirCap;
        }
    }
}

[HarmonyPatch]
internal static class UnitCapShipCapPatch
{
    private static bool Prepare() => GroundControlReflection.UnitCapShipCapGetter != null;
    private static MethodBase TargetMethod() => GroundControlReflection.UnitCapShipCapGetter!;

    private static void Postfix(ref int __result)
    {
        if (!DynamicCapAndTechService.IsActive)
        {
            return;
        }

        FactionHQ? targetHq = UnitCapHqScope.ResolveCapTargetHq();
        if (targetHq != null)
        {
            __result = DynamicCapAndTechService.Instance.GetSnapshot(targetHq).NavalCap;
        }
    }
}

[HarmonyPatch]
internal static class UnitCapTypeCapPatch
{
    private static bool Prepare() => GroundControlReflection.UnitCapTypeCapGetter != null;
    private static MethodBase TargetMethod() => GroundControlReflection.UnitCapTypeCapGetter!;

    private static void Postfix(ref int __result)
    {
        if (!DynamicCapAndTechService.IsActive)
        {
            return;
        }

        FactionHQ? targetHq = UnitCapHqScope.ResolveCapTargetHq();
        if (targetHq != null)
        {
            __result = DynamicCapAndTechService.Instance.GetSnapshot(targetHq).TotalCap * 3 / 5;
        }
    }
}

[HarmonyPatch]
internal static class UnitCapBuilderCapPatch
{
    private static bool Prepare() => GroundControlReflection.UnitCapBuilderCapGetter != null;
    private static MethodBase TargetMethod() => GroundControlReflection.UnitCapBuilderCapGetter!;

    private static void Postfix(ref int __result)
    {
        if (!DynamicCapAndTechService.IsActive)
        {
            return;
        }

        if (EcoTickStartingBuildersContextGuardPatch.InTickStartingBuilders && ReviewContextGuardPatch.ActiveAiHq == null)
        {
            return;
        }

        FactionHQ? targetHq = UnitCapHqScope.ResolveCapTargetHq();
        if (targetHq != null)
        {
            __result = UnityEngine.Mathf.Max(2, DynamicCapAndTechService.Instance.GetSnapshot(targetHq).TotalCap / 10);
        }
    }
}

internal static class UnitCapHqScope
{
    internal static FactionHQ? ResolveCapTargetHq()
        => ReviewContextGuardPatch.ActiveAiHq ?? GroundControlReflection.GetLocalHq();
}

[HarmonyPatch]
internal static class EconomyReviewContextGuardPatch
{
    private static bool Prepare() => GroundControlReflection.ReviewEnemyMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.ReviewEnemyMethod!;

    [HarmonyPriority(Priority.First)]
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
internal static class EcoTickStartingBuildersContextGuardPatch
{
    [ThreadStatic]
    internal static bool InTickStartingBuilders;

    private static bool Prepare() => GroundControlReflection.EcoTickStartingBuildersMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoTickStartingBuildersMethod!;

    private static void Prefix(out bool __state)
    {
        __state = InTickStartingBuilders;
        InTickStartingBuilders = true;
    }

    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        InTickStartingBuilders = __state;
        return __exception;
    }
}

[HarmonyPatch]
internal static class EnemyBuildReserveContextGuardPatch
{
    private static bool Prepare() => GroundControlReflection.GetEnemyBuildReserveMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.GetEnemyBuildReserveMethod!;

    private static void Prefix(FactionHQ hq, out FactionHQ? __state)
    {
        __state = ReviewContextGuardPatch.ActiveAiHq;
        if (__state == null)
        {
            ReviewContextGuardPatch.ActiveAiHq = hq;
        }
    }

    private static Exception? Finalizer(Exception? __exception, FactionHQ? __state)
    {
        ReviewContextGuardPatch.ActiveAiHq = __state;
        return __exception;
    }
}

[HarmonyPatch]
internal static class EnemyBuyAirframeContextGuardPatch
{
    private static bool Prepare() => GroundControlReflection.EnemyBuyAirframeMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EnemyBuyAirframeMethod!;

    private static void Prefix(FactionHQ hq, out FactionHQ? __state)
    {
        __state = ReviewContextGuardPatch.ActiveAiHq;
        if (__state == null && hq != null)
        {
            ReviewContextGuardPatch.ActiveAiHq = hq;
        }
    }

    private static Exception? Finalizer(Exception? __exception, FactionHQ? __state)
    {
        ReviewContextGuardPatch.ActiveAiHq = __state;
        return __exception;
    }
}

[HarmonyPatch]
internal static class AirLaunchAiAircraftContextGuardPatch
{
    private static bool Prepare() => GroundControlReflection.AirLaunchAiAircraftMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.AirLaunchAiAircraftMethod!;

    private static void Prefix(FactionHQ hq, out FactionHQ? __state)
    {
        __state = ReviewContextGuardPatch.ActiveAiHq;
        if (__state == null && hq != null)
        {
            ReviewContextGuardPatch.ActiveAiHq = hq;
        }
    }

    private static Exception? Finalizer(Exception? __exception, FactionHQ? __state, FactionHQ? hq)
    {
        ReviewContextGuardPatch.ActiveAiHq = __state;
        if (hq != null && DynamicCapAndTechService.IsActive)
        {
            DynamicCapAndTechService.Instance.InvalidateCache(hq);
        }

        return __exception;
    }
}

[HarmonyPatch]
internal static class EcoStartingBuildersContextGuardPatch
{
    private static bool Prepare() => GroundControlReflection.EcoTryGiveStartingBuildersMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoTryGiveStartingBuildersMethod!;

    private static void Prefix(FactionHQ hq, out (FactionHQ? PrevHq, bool PrevBuilderScope) __state)
    {
        __state = (ReviewContextGuardPatch.ActiveAiHq, DynamicCapAndTechService.BuilderExemptionScope);
        if (__state.PrevHq == null && hq != null)
        {
            ReviewContextGuardPatch.ActiveAiHq = hq;
        }

        DynamicCapAndTechService.BuilderExemptionScope = true;
    }

    private static Exception? Finalizer(Exception? __exception, (FactionHQ? PrevHq, bool PrevBuilderScope) __state)
    {
        ReviewContextGuardPatch.ActiveAiHq = __state.PrevHq;
        DynamicCapAndTechService.BuilderExemptionScope = __state.PrevBuilderScope;
        return __exception;
    }
}

[HarmonyPatch]
internal static class EcoLocalBuilderContextGuardPatch
{
    private static bool Prepare() => GroundControlReflection.EcoTrySpawnLocalBuilderMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoTrySpawnLocalBuilderMethod!;

    private static void Prefix(object site, out (FactionHQ? PrevHq, bool PrevBuilderScope) __state)
    {
        __state = (ReviewContextGuardPatch.ActiveAiHq, DynamicCapAndTechService.BuilderExemptionScope);
        if (__state.PrevHq == null && site != null && GroundControlReflection.SiteHqProp?.GetValue(site) is FactionHQ hq)
        {
            ReviewContextGuardPatch.ActiveAiHq = hq;
        }

        DynamicCapAndTechService.BuilderExemptionScope = true;
    }

    private static Exception? Finalizer(Exception? __exception, (FactionHQ? PrevHq, bool PrevBuilderScope) __state)
    {
        ReviewContextGuardPatch.ActiveAiHq = __state.PrevHq;
        DynamicCapAndTechService.BuilderExemptionScope = __state.PrevBuilderScope;
        return __exception;
    }
}

[HarmonyPatch]
internal static class UnitCapIsAiTypeFullContextPatch
{
    private static bool Prepare() => GroundControlReflection.UnitCapIsAiTypeFullMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.UnitCapIsAiTypeFullMethod!;

    private static void Prefix(FactionHQ? hq, out FactionHQ? __state)
    {
        __state = ReviewContextGuardPatch.ActiveAiHq;
        if (__state == null && hq != null)
        {
            ReviewContextGuardPatch.ActiveAiHq = hq;
        }
    }

    private static Exception? Finalizer(Exception? __exception, FactionHQ? __state)
    {
        ReviewContextGuardPatch.ActiveAiHq = __state;
        return __exception;
    }
}

[HarmonyPatch]
internal static class UnitCapFactoriesPerBasePatch
{
    private static bool Prepare() => GroundControlReflection.UnitCapFactoriesPerBaseMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.UnitCapFactoriesPerBaseMethod!;

    private static void Postfix(int uncapped, ref int __result)
    {
        if (!DynamicCapAndTechService.IsActive || uncapped <= 1)
        {
            return;
        }

        FactionHQ? targetHq = UnitCapHqScope.ResolveCapTargetHq();
        FactionCapSnapshot snap = DynamicCapAndTechService.Instance.GetSnapshot(targetHq);
        int minPerBase = uncapped <= 2
            ? uncapped
            : UnityEngine.Mathf.Clamp(snap.TechTier + 1, 2, uncapped);
        __result = UnityEngine.Mathf.Clamp(UnityEngine.Mathf.Max(__result, minPerBase), 1, uncapped);
    }
}

[HarmonyPatch]
internal static class UnitCapIsFullPatch
{
    private static bool Prepare() => GroundControlReflection.UnitCapIsFullMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.UnitCapIsFullMethod!;

    private static bool Prefix(FactionHQ? hq, bool tellPlayer, ref bool __result)
    {
        if (!DynamicCapAndTechService.IsActive)
        {
            return true;
        }

        if (hq == null)
        {
            __result = false;
            return false;
        }

        __result = DynamicCapAndTechService.Instance.IsFullTotal(hq, tellPlayer);
        return false;
    }
}

[HarmonyPatch]
internal static class UnitCapIsGroundFullPatch
{
    private static bool Prepare() => GroundControlReflection.UnitCapIsGroundFullMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.UnitCapIsGroundFullMethod!;

    private static bool Prefix(FactionHQ? hq, ref bool __result)
    {
        if (!DynamicCapAndTechService.IsActive)
        {
            return true;
        }

        __result = hq != null && DynamicCapAndTechService.Instance.IsGroundFull(hq, tellPlayer: false);
        return false;
    }
}

[HarmonyPatch]
internal static class UnitCapIsAircraftFullPatch
{
    private static bool Prepare() => GroundControlReflection.UnitCapIsAircraftFullMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.UnitCapIsAircraftFullMethod!;

    private static bool Prefix(FactionHQ? hq, ref bool __result)
    {
        if (!DynamicCapAndTechService.IsActive)
        {
            return true;
        }

        __result = hq != null && DynamicCapAndTechService.Instance.IsAirFull(hq, tellPlayer: false);
        return false;
    }
}

[HarmonyPatch]
internal static class UnitCapIsKindFullPatch
{
    private static bool Prepare() => GroundControlReflection.UnitCapIsKindFullMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.UnitCapIsKindFullMethod!;

    private static bool Prefix(FactionHQ? hq, UnitDefinition? definition, bool tellPlayer, ref bool __result)
    {
        if (!DynamicCapAndTechService.IsActive)
        {
            return true;
        }

        __result = DynamicCapAndTechService.Instance.TryGetSpawnBlockReason(hq, definition, tellPlayer, out _);
        return false;
    }
}

[HarmonyPatch]
internal static class AirCommandLaunchWarningPatch
{
    private static bool Prepare() => GroundControlReflection.BuildLaunchWarningMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.BuildLaunchWarningMethod!;

    private static void Postfix(object __instance, ref string __result)
    {
        if (!string.IsNullOrEmpty(__result) || !DynamicCapAndTechService.IsActive)
        {
            return;
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        AircraftDefinition? selectedDef = GroundControlReflection.GetSelectedAirOptionDefinition(__instance);
        if (localHq == null || selectedDef == null)
        {
            return;
        }

        if (DynamicCapAndTechService.Instance.TryGetSpawnBlockReason(localHq, selectedDef, tellPlayer: false, out string reason))
        {
            __result = reason;
        }
    }
}

[HarmonyPatch]
internal static class EconomyGetStructureCostPatch
{
    private static bool Prepare() => GroundControlReflection.GetStructureCostMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.GetStructureCostMethod!;

    private static bool Prefix(BuildingDefinition? definition, ref float __result)
    {
        if (RtsBuildingCatalog.TryGetSpec(definition, out RtsBuildingSpec spec))
        {
            __result = spec.BuildCost;
            return false;
        }

        if (definition != null
            && MdsBridgeBootstrap.IsMdsBuildDefinition?.Invoke(definition) == true
            && MdsBridgeBootstrap.GetMdsBuildCost != null)
        {
            __result = MdsBridgeBootstrap.GetMdsBuildCost(definition);
            return false;
        }

        return true;
    }
}

internal static class StructureLabelHqScope
{
    [ThreadStatic]
    internal static FactionHQ? ActiveBuildHq;
}

[HarmonyPatch]
internal static class EconomyCommitBuildHqContextPatch
{
    private static bool Prepare() => GroundControlReflection.EconomyCommitBuildMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EconomyCommitBuildMethod!;

    [HarmonyPriority(Priority.First)]
    private static void Prefix(FactionHQ? hq, out FactionHQ? __state)
    {
        __state = StructureLabelHqScope.ActiveBuildHq;
        if (hq != null)
        {
            StructureLabelHqScope.ActiveBuildHq = hq;
        }
    }

    private static Exception? Finalizer(Exception? __exception, FactionHQ? __state)
    {
        StructureLabelHqScope.ActiveBuildHq = __state;
        return __exception;
    }
}

[HarmonyPatch]
internal static class EconomyHostBuildHqContextPatch
{
    private static readonly MethodInfo? HostBuildMethod =
        GroundControlReflection.EconomyServiceType != null
            ? AccessTools.Method(GroundControlReflection.EconomyServiceType, "HostBuild", new[] { typeof(FactionHQ), typeof(string[]) })
            : null;

    private static bool Prepare() => HostBuildMethod != null;
    private static MethodBase TargetMethod() => HostBuildMethod!;

    [HarmonyPriority(Priority.First)]
    private static void Prefix(FactionHQ? hq, out FactionHQ? __state)
    {
        __state = StructureLabelHqScope.ActiveBuildHq;
        if (hq != null)
        {
            StructureLabelHqScope.ActiveBuildHq = hq;
        }
    }

    private static Exception? Finalizer(Exception? __exception, FactionHQ? __state)
    {
        StructureLabelHqScope.ActiveBuildHq = __state;
        return __exception;
    }
}

[HarmonyPatch]
internal static class EconomyUiDrawCatalogHqContextPatch
{
    private static bool Prepare() => GroundControlReflection.EcoUiDrawCatalogMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoUiDrawCatalogMethod!;

    [HarmonyPriority(Priority.First)]
    private static void Prefix(out FactionHQ? __state)
    {
        __state = StructureLabelHqScope.ActiveBuildHq;
        StructureLabelHqScope.ActiveBuildHq = GroundControlReflection.GetLocalHq();
    }

    private static Exception? Finalizer(Exception? __exception, FactionHQ? __state)
    {
        StructureLabelHqScope.ActiveBuildHq = __state;
        return __exception;
    }
}

[HarmonyPatch]
internal static class EconomyGetStructureLabelPatch
{
    private static bool Prepare() => GroundControlReflection.GetStructureLabelMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.GetStructureLabelMethod!;

    private static bool Prefix(BuildingDefinition? definition, ref string __result)
    {
        FactionHQ? targetHq = StructureLabelHqScope.ActiveBuildHq ?? GroundControlReflection.GetLocalHq();
        if (RtsBuildingCatalog.TryGetSpec(definition, out RtsBuildingSpec spec))
        {
            int tier = targetHq != null ? DynamicCapAndTechService.Instance.GetSnapshot(targetHq).TechTier : 1;
            __result = spec.RequiredTechTier > tier
                ? $"{spec.DisplayName} [Need T{spec.RequiredTechTier}]"
                : spec.DisplayName;
            return false;
        }

        if (definition != null
            && MdsBridgeBootstrap.IsMdsBuildDefinition?.Invoke(definition) == true
            && MdsBridgeBootstrap.GetMdsBuildLabel != null)
        {
            __result = MdsBridgeBootstrap.GetMdsBuildLabel(definition, targetHq);
            return false;
        }

        return true;
    }
}

[HarmonyPatch]
internal static class EconomySpawnBuildingPatch
{
    private static bool Prepare() => GroundControlReflection.SpawnBuildingMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.SpawnBuildingMethod!;

    private static void Postfix(FactionHQ hq, BuildingDefinition? definition, Unit? __result)
    {
        if (!AddonConfig.ModEnabled.Value || __result == null || definition == null)
        {
            return;
        }

        if (MdsBridgeBootstrap.IsMdsBuildDefinition?.Invoke(definition) == true
            && MdsBridgeBootstrap.TryReplaceBuiltMdsPlaceholder?.Invoke(definition, hq, __result) == true)
        {
            return;
        }

        DynamicCapAndTechService.Instance.OnBuildingSpawned(hq, definition, __result);
        AiCoastalOutpostService.Instance.OnControlTowerBuilt(GroundControlReflection.GetEcoInstance(), hq, __result);
    }
}

[HarmonyPatch]
internal static class EconomyReviewEnemyCityPatch
{
    private static bool Prepare() => GroundControlReflection.ReviewEnemyMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.ReviewEnemyMethod!;

    private static void Prefix(object __instance, FactionHQ hq)
    {
        DynamicCapAndTechService.Instance.TickAiCityDecision(__instance, hq);
        if (AddonConfig.ModEnabled.Value && hq != null && ((Mirage.NetworkBehaviour)hq).IsServer)
        {
            MdsBridgeBootstrap.TickAiMdsDeploymentAction?.Invoke(hq);
        }
    }
}

[HarmonyPatch(typeof(VehicleDepot), nameof(VehicleDepot.TrySpawnVehicle), new[] { typeof(VehicleDefinition) })]
internal static class VehicleDepotTrySpawnVehicleCapPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(VehicleDefinition? vehicleDefinition, out bool __state)
    {
        __state = DynamicCapAndTechService.BuilderExemptionScope;
        if (vehicleDefinition != null && GroundControlReflection.IsBuilderVehicle(vehicleDefinition))
        {
            DynamicCapAndTechService.BuilderExemptionScope = true;
        }
    }

    private static void Postfix(VehicleDepot __instance, bool __result)
    {
        if (__result && __instance != null && __instance.NetworkHQ != null && DynamicCapAndTechService.IsActive)
        {
            DynamicCapAndTechService.Instance.InvalidateCache(__instance.NetworkHQ);
        }
    }

    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        DynamicCapAndTechService.BuilderExemptionScope = __state;
        return __exception;
    }
}

[HarmonyPatch]
internal static class SpawnServiceQueueVehicleBuilderContextPatch
{
    private static readonly MethodInfo? QueueVehicleMethod =
        GroundControlReflection.SpawnServiceType != null
            ? AccessTools.Method(GroundControlReflection.SpawnServiceType, "QueueVehicle", new[] { typeof(VehicleDefinition) })
            : null;

    private static bool Prepare() => QueueVehicleMethod != null;
    private static MethodBase TargetMethod() => QueueVehicleMethod!;

    [HarmonyPriority(Priority.First)]
    private static void Prefix(VehicleDefinition? definition, out bool __state)
    {
        __state = DynamicCapAndTechService.BuilderExemptionScope;
        if (definition != null && GroundControlReflection.IsBuilderVehicle(definition))
        {
            DynamicCapAndTechService.BuilderExemptionScope = true;
        }
    }

    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        DynamicCapAndTechService.BuilderExemptionScope = __state;
        return __exception;
    }
}

[HarmonyPatch]
internal static class NavalTryPurchaseCapInvalidatePatch
{
    private static readonly MethodInfo? TryPurchaseForHqMethod =
        GroundControlReflection.NavalPurchaseServiceType != null
            ? AccessTools.Method(
                GroundControlReflection.NavalPurchaseServiceType,
                "TryPurchaseForHq",
                new[] { typeof(FactionHQ), typeof(ShipDefinition), typeof(GlobalPosition), typeof(Ship).MakeByRefType(), typeof(GlobalPosition?) })
            : null;

    private static bool Prepare() => TryPurchaseForHqMethod != null;
    private static MethodBase TargetMethod() => TryPurchaseForHqMethod!;

    private static void Postfix(FactionHQ? hq, float __result)
    {
        if (__result >= 0f && hq != null && DynamicCapAndTechService.IsActive)
        {
            DynamicCapAndTechService.Instance.InvalidateCache(hq);
        }
    }
}

[HarmonyPatch]
internal static class NavalCompletePurchaseCapInvalidatePatch
{
    private static readonly MethodInfo? CompletePurchaseMethod =
        GroundControlReflection.NavalPurchaseServiceType != null
            ? AccessTools.Method(
                GroundControlReflection.NavalPurchaseServiceType,
                "CompletePurchase",
                new[] { typeof(ShipDefinition), typeof(FactionHQ), typeof(Unit), typeof(float) })
            : null;

    private static bool Prepare() => CompletePurchaseMethod != null;
    private static MethodBase TargetMethod() => CompletePurchaseMethod!;

    private static void Postfix(FactionHQ? hq, bool __result)
    {
        if (__result && hq != null && DynamicCapAndTechService.IsActive)
        {
            DynamicCapAndTechService.Instance.InvalidateCache(hq);
        }
    }
}

[HarmonyPatch]
internal static class SupplyHeliSpawnCapInvalidatePatch
{
    private static readonly MethodInfo? TrySpawnCargoRunAtAirbaseMethod =
        GroundControlReflection.SupplyHeliServiceType != null && GroundControlReflection.SupplyHeliQueuedSpawnType != null
            ? AccessTools.Method(
                GroundControlReflection.SupplyHeliServiceType,
                "TrySpawnCargoRunAtAirbase",
                new[] { GroundControlReflection.SupplyHeliQueuedSpawnType, typeof(Airbase), typeof(FactionHQ) })
            : null;

    private static bool Prepare() => TrySpawnCargoRunAtAirbaseMethod != null;
    private static MethodBase TargetMethod() => TrySpawnCargoRunAtAirbaseMethod!;

    private static void Postfix(FactionHQ? hq)
    {
        if (hq != null && DynamicCapAndTechService.IsActive)
        {
            DynamicCapAndTechService.Instance.InvalidateCache(hq);
        }
    }
}

[HarmonyPatch(typeof(Unit), nameof(Unit.UnitDisabled), new[] { typeof(bool), typeof(bool) })]
internal static class UnitDisabledCapInvalidatePatch
{
    private static void Postfix(Unit __instance)
    {
        if (__instance != null && __instance.NetworkHQ != null && DynamicCapAndTechService.IsActive)
        {
            DynamicCapAndTechService.Instance.InvalidateCache(__instance.NetworkHQ);
        }
    }
}
