using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal static class NativeBuildMenuState
{
    internal static bool InUiDrawCatalog;
}

[HarmonyPatch]
internal static class EconomyUiDrawCatalogGuardPatch
{
    private static bool Prepare() => GroundControlReflection.EcoUiDrawCatalogMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoUiDrawCatalogMethod!;

    private static void Prefix()
    {
        NativeBuildMenuState.InUiDrawCatalog = true;
    }

    private static void Finalizer()
    {
        NativeBuildMenuState.InUiDrawCatalog = false;
    }
}

[HarmonyPatch]
internal static class EconomyCatalogUiInjectionPatch
{
    private static bool Prepare() => GroundControlReflection.EcoCatalogGetter != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoCatalogGetter!;

    private static void Postfix(ref IReadOnlyList<BuildingDefinition> __result)
    {
        if (!AddonConfig.ModEnabled.Value || __result == null)
        {
            return;
        }

        // Always strip raw [MDS] mds_* encyclopedia clones from Ground Control's catalog so
        // 1) BUILD never shows duplicates, and
        // 2) AI/Champions ResolveCategoryDefinition cannot bypass faction/tech locks via raw encyclopedia entries.
        var merged = new List<BuildingDefinition>(__result.Count + 16);
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < __result.Count; i++)
        {
            BuildingDefinition b = __result[i];
            if (b == null || IsRawMdsEntry(b) || (!NativeBuildMenuState.InUiDrawCatalog && IsCustomCityOrMdsEntry(b)))
            {
                continue;
            }

            merged.Add(b);
            if (!string.IsNullOrEmpty(b.jsonKey))
            {
                seenKeys.Add(b.jsonKey);
            }
        }

        if (!NativeBuildMenuState.InUiDrawCatalog)
        {
            __result = merged;
            return;
        }

        RtsBuildingCatalog.EnsureLookupEntries();
        MdsBridgeBootstrap.EnsureMdsBuildCatalogLookupAction?.Invoke();

        IReadOnlyList<RtsBuildingSpec> rtsSpecs = RtsBuildingCatalog.AllSpecs;
        for (int i = 0; i < rtsSpecs.Count; i++)
        {
            BuildingDefinition? def = RtsBuildingCatalog.GetOrCreateDefinition(rtsSpecs[i].Kind);
            if (def != null && (string.IsNullOrEmpty(def.jsonKey) || seenKeys.Add(def.jsonKey)))
            {
                merged.Add(def);
            }
        }

        if (MdsBridgeBootstrap.GetMdsBuildCatalogDefinitionsForHq != null)
        {
            FactionHQ? localHq = GroundControlReflection.GetLocalHq();
            IReadOnlyList<BuildingDefinition> mdsDefs = MdsBridgeBootstrap.GetMdsBuildCatalogDefinitionsForHq(localHq);
            for (int i = 0; i < mdsDefs.Count; i++)
            {
                BuildingDefinition? def = mdsDefs[i];
                if (def != null && (string.IsNullOrEmpty(def.jsonKey) || seenKeys.Add(def.jsonKey)))
                {
                    merged.Add(def);
                }
            }
        }

        merged.Sort((a, b) =>
        {
            int catCmp = ((int)a.buildingType).CompareTo((int)b.buildingType);
            if (catCmp != 0)
            {
                return catCmp;
            }

            return a.value.CompareTo(b.value);
        });

        __result = merged;
    }

    private static bool IsCustomCityOrMdsEntry(BuildingDefinition def)
    {
        if (RtsBuildingCatalog.TryGetSpec(def, out _))
        {
            return true;
        }

        if (MdsBridgeBootstrap.IsMdsBuildDefinition?.Invoke(def) == true)
        {
            return true;
        }

        return !string.IsNullOrEmpty(def.jsonKey) &&
               (def.jsonKey.StartsWith(RtsBuildingCatalog.JsonKeyPrefix, StringComparison.OrdinalIgnoreCase) ||
                def.jsonKey.StartsWith("mds_build_", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRawMdsEntry(BuildingDefinition def)
    {
        if (MdsBridgeBootstrap.IsRawMdsEncyclopediaDefinition != null)
        {
            return MdsBridgeBootstrap.IsRawMdsEncyclopediaDefinition(def);
        }

        if (MdsBridgeBootstrap.IsMdsBuildDefinition?.Invoke(def) == true)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(def.jsonKey)
            && def.jsonKey.StartsWith("mds_", StringComparison.OrdinalIgnoreCase)
            && !def.jsonKey.StartsWith("mds_build_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrEmpty(def.unitName)
            && def.unitName.StartsWith("[MDS]", StringComparison.OrdinalIgnoreCase);
    }
}

[HarmonyPatch]
internal static class EconomyRefreshCatalogInjectCustomPatch
{
    private static bool Prepare() =>
        GroundControlReflection.EcoRefreshCatalogMethod != null &&
        GroundControlReflection.EcoCatalogListField != null;

    private static MethodBase TargetMethod() => GroundControlReflection.EcoRefreshCatalogMethod!;

    private static void Postfix(object __instance)
    {
        if (!AddonConfig.ModEnabled.Value || __instance == null)
        {
            return;
        }

        if (GroundControlReflection.EcoCatalogListField?.GetValue(__instance) is not List<BuildingDefinition> list)
        {
            return;
        }

        RtsBuildingCatalog.EnsureLookupEntries();
        MdsBridgeBootstrap.EnsureMdsBuildCatalogLookupAction?.Invoke();

        var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < list.Count; i++)
        {
            BuildingDefinition b = list[i];
            if (b != null && !string.IsNullOrEmpty(b.jsonKey))
            {
                existingKeys.Add(b.jsonKey);
            }
        }

        IReadOnlyList<RtsBuildingSpec> specs = RtsBuildingCatalog.AllSpecs;
        for (int i = 0; i < specs.Count; i++)
        {
            BuildingDefinition? def = RtsBuildingCatalog.GetOrCreateDefinition(specs[i].Kind);
            if (def != null && !string.IsNullOrEmpty(def.jsonKey) && existingKeys.Add(def.jsonKey))
            {
                list.Add(def);
            }
        }

        if (MdsBridgeBootstrap.GetAllMdsBuildCatalogDefinitions != null)
        {
            IReadOnlyList<BuildingDefinition> mdsDefs = MdsBridgeBootstrap.GetAllMdsBuildCatalogDefinitions();
            for (int i = 0; i < mdsDefs.Count; i++)
            {
                BuildingDefinition? def = mdsDefs[i];
                if (def != null && !string.IsNullOrEmpty(def.jsonKey) && existingKeys.Add(def.jsonKey))
                {
                    list.Add(def);
                }
            }
        }
    }
}

internal static class StructureBuildGateHelpers
{
    internal static bool TryGetBlockReason(BuildingDefinition definition, FactionHQ hq, out string reason)
    {
        if (RtsBuildingCatalog.TryGetSpec(definition, out RtsBuildingSpec spec))
        {
            if (!DynamicCapAndTechService.Instance.CanStartBuilding(hq, spec.Kind, out string rtsReason))
            {
                reason = rtsReason;
                return true;
            }

            reason = string.Empty;
            return false;
        }

        if (MdsBridgeBootstrap.IsMdsBuildDefinition?.Invoke(definition) == true)
        {
            string? mdsReason = MdsBridgeBootstrap.GetMdsBuildBlockReason?.Invoke(definition, hq);
            if (!string.IsNullOrEmpty(mdsReason))
            {
                reason = mdsReason!;
                return true;
            }
        }

        reason = string.Empty;
        return false;
    }
}

[HarmonyPatch]
internal static class EconomyBeginStructureBuildGatePatch
{
    private static bool Prepare() => GroundControlReflection.BeginStructureBuildMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.BeginStructureBuildMethod!;

    private static bool Prefix(BuildingDefinition? definition)
    {
        if (!AddonConfig.ModEnabled.Value || definition == null)
        {
            return true;
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        if (localHq == null)
        {
            return true;
        }

        if (StructureBuildGateHelpers.TryGetBlockReason(definition, localHq, out string reason))
        {
            GroundControlReflection.SetEconomyStatusText(reason);
            GroundControlReflection.NoticeFail(reason);
            return false;
        }

        return true;
    }
}

[HarmonyPatch]
internal static class EconomyCommitBuildGatePatch
{
    private static readonly PropertyInfo? NetworkIsHandlingGuestProp =
        GroundControlReflection.NetworkType != null
            ? AccessTools.Property(GroundControlReflection.NetworkType, "IsHandlingGuest")
            : null;

    private static readonly MethodInfo? EcoStatusTextSetter =
        GroundControlReflection.EconomyServiceType != null
            ? AccessTools.Property(GroundControlReflection.EconomyServiceType, "StatusText")?.GetSetMethod(true)
            : null;

    private static bool Prepare() => GroundControlReflection.EconomyCommitBuildMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EconomyCommitBuildMethod!;

    private static bool IsHandlingGuest()
        => NetworkIsHandlingGuestProp?.GetValue(null) is bool b && b;

    private static void SetStatusText(object? ecoInstance, string reason)
    {
        if (ecoInstance != null && EcoStatusTextSetter != null)
        {
            EcoStatusTextSetter.Invoke(ecoInstance, new object?[] { reason ?? string.Empty });
        }
        else
        {
            GroundControlReflection.SetEconomyStatusText(reason);
        }
    }

    private static bool Prefix(object __instance, FactionHQ? hq, BuildingDefinition? structure, ref bool __result)
    {
        if (!AddonConfig.ModEnabled.Value || hq == null || structure == null)
        {
            return true;
        }

        if (StructureBuildGateHelpers.TryGetBlockReason(structure, hq, out string reason))
        {
            SetStatusText(__instance, reason);
            if (!IsHandlingGuest() && ReferenceEquals(hq, GroundControlReflection.GetLocalHq()))
            {
                GroundControlReflection.NoticeFail(reason);
                GroundControlReflection.InvokeEconomyCancelBuild(__instance);
            }

            __result = false;
            return false;
        }

        return true;
    }
}

[HarmonyPatch]
internal static class EconomyUiDrawWindowShortcutsPatch
{
    private static bool Prepare() => GroundControlReflection.EcoUiDrawWindowMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoUiDrawWindowMethod!;

    private static void Postfix(object __instance)
    {
        if (!AddonConfig.ModEnabled.Value || __instance == null)
        {
            return;
        }

        Rect wRect = GroundControlReflection.GetEconomyUiWindowRect(__instance);
        if (wRect.width < 260f || wRect.height < 120f)
        {
            return;
        }

        bool showCheats = SingleplayerCheatService.IsAllowed;
        int btnCount = showCheats ? 4 : 3;
        float gap = 5f;
        float leftMargin = 12f;
        float rightMargin = 24f; // Leave clear of Ground Control's bottom-right resize grip.
        float totalW = wRect.width - leftMargin - rightMargin;
        float btnW = (totalW - gap * (btnCount - 1)) / btnCount;
        float y = wRect.height - 28f;

        GUIStyle roadStyle = PioneerRoadService.Instance.IsPlanningMode ? NativeThemeBridge.SelectedButton : NativeThemeBridge.Button;
        if (GUI.Button(new Rect(leftMargin, y, btnW, 22f), PioneerRoadService.Instance.IsPlanningMode ? "ROADS [ACTIVE]" : "ROADS & BRIDGES", roadStyle))
        {
            PioneerRoadService.Instance.TogglePlanningMode();
        }

        GUIStyle cityStyle = RtsCityUiService.Instance.IsWindowVisible ? NativeThemeBridge.SelectedButton : NativeThemeBridge.Button;
        if (GUI.Button(new Rect(leftMargin + (btnW + gap), y, btnW, 22f), RtsCityUiService.Instance.IsWindowVisible ? "CITY [OPEN]" : "CITY & TECH", cityStyle))
        {
            RtsCityUiService.Instance.ToggleWindow();
        }

        bool abmAlert = AbmDefenseService.Instance.HasActiveThreatForLocal();
        GUIStyle abmStyle = abmAlert
            ? NativeThemeBridge.DangerButton
            : (AbmDefenseService.Instance.IsWindowVisible ? NativeThemeBridge.SelectedButton : NativeThemeBridge.Button);
        if (GUI.Button(new Rect(leftMargin + (btnW + gap) * 2f, y, btnW, 22f), AbmDefenseService.Instance.IsWindowVisible ? "ABM [OPEN]" : "ABM STOCK", abmStyle))
        {
            AbmDefenseService.Instance.ToggleWindow();
        }

        if (showCheats)
        {
            GUIStyle cheatStyle = SingleplayerCheatService.Instance.IsWindowVisible ? NativeThemeBridge.SelectedButton : NativeThemeBridge.Button;
            if (GUI.Button(new Rect(leftMargin + (btnW + gap) * 3f, y, btnW, 22f), "CHEATS", cheatStyle))
            {
                SingleplayerCheatService.Instance.ToggleWindow();
            }
        }
    }
}

[HarmonyPatch]
internal static class OverlayContainsScreenPointPatch
{
    private static bool Prepare() => GroundControlReflection.OverlayContainsScreenPointMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.OverlayContainsScreenPointMethod!;

    private static void Postfix(Vector2 screenPoint, ref bool __result)
    {
        if (!__result && AddonConfig.ModEnabled.Value && NativeThemeBridge.IsAnyAddonUiUnderScreenPoint(screenPoint))
        {
            __result = true;
        }
    }
}

[HarmonyPatch]
internal static class SupplyHeliRefreshOptionsAirdropPatch
{
    private static bool Prepare() => GroundControlReflection.SupplyHeliRefreshOptionsMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.SupplyHeliRefreshOptionsMethod!;

    private static void Prefix()
    {
        if (AddonConfig.ModEnabled.Value)
        {
            MdsBridgeBootstrap.EnsureAirdropMountsInjectedAction?.Invoke();
        }
    }
}

internal static class SupplyHeliCargoMountFilterHelper
{
    private static readonly ConditionalWeakTable<HardpointSet, List<WeaponMount>> UnfilteredBySet = new();
    private static readonly FieldInfo? SupplyHeliAircraftOptionsField =
        GroundControlReflection.SupplyHeliServiceType != null
            ? AccessTools.Field(GroundControlReflection.SupplyHeliServiceType, "aircraftOptions")
            : null;
    private static readonly PropertyInfo? CargoSlotMountsProp =
        GroundControlReflection.CargoSlotOptionType != null
            ? AccessTools.Property(GroundControlReflection.CargoSlotOptionType, "Mounts")
            : null;
    private static readonly Type? SupplyHeliUiType =
        AccessTools.TypeByName("GroundControlRts.CommanderSupplyHeliUi");
    private static readonly FieldInfo? SupplyHeliUiServiceField =
        SupplyHeliUiType != null ? AccessTools.Field(SupplyHeliUiType, "service") : null;

    private static FactionHQ? lastSyncedHq;
    private static bool lastIgnoreFactionLock;
    private static int lastFilterPresent;

    internal static void RegisterUnfiltered(HardpointSet? set, List<WeaponMount>? mounts)
    {
        if (set == null || mounts == null)
        {
            return;
        }

        if (!UnfilteredBySet.TryGetValue(set, out List<WeaponMount> copy))
        {
            copy = new List<WeaponMount>(mounts.Count);
            UnfilteredBySet.Add(set, copy);
        }
        else
        {
            copy.Clear();
        }

        for (int i = 0; i < mounts.Count; i++)
        {
            if (mounts[i] != null)
            {
                copy.Add(mounts[i]);
            }
        }
    }

    internal static void SyncForUi(object? supplyUi)
    {
        if (supplyUi == null || SupplyHeliUiServiceField == null)
        {
            return;
        }

        object? service = SupplyHeliUiServiceField.GetValue(supplyUi);
        SyncForService(service, GroundControlReflection.GetLocalHq(), force: false);
    }

    internal static void SyncForService(object? supplyService, FactionHQ? targetHq, bool force)
    {
        if (supplyService == null || SupplyHeliAircraftOptionsField == null)
        {
            return;
        }

        Func<WeaponMount, FactionHQ?, bool>? filter = MdsBridgeBootstrap.IsMdsAirdropMountAllowedForHq;
        bool ignoreLock = SingleplayerCheatService.IsIgnoreFactionLockActive;
        int filterFlag = filter != null ? 1 : 0;

        if (!force
            && ReferenceEquals(targetHq, lastSyncedHq)
            && ignoreLock == lastIgnoreFactionLock
            && filterFlag == lastFilterPresent)
        {
            return;
        }

        if (SupplyHeliAircraftOptionsField.GetValue(supplyService) is not IList options || options.Count == 0)
        {
            return;
        }

        lastSyncedHq = targetHq;
        lastIgnoreFactionLock = ignoreLock;
        lastFilterPresent = filterFlag;

        for (int a = 0; a < options.Count; a++)
        {
            object? option = options[a];
            if (option == null
                || GroundControlReflection.CargoAircraftCargoSlotsProp?.GetValue(option) is not IList slots
                || GroundControlReflection.CargoAircraftHardpointSetsProp?.GetValue(option) is not HardpointSet[] sets)
            {
                continue;
            }

            for (int s = 0; s < slots.Count; s++)
            {
                object? slot = slots[s];
                if (slot == null
                    || GroundControlReflection.CargoSlotHardpointIndexProp?.GetValue(slot) is not int hpIdx
                    || hpIdx < 0
                    || hpIdx >= sets.Length)
                {
                    continue;
                }

                HardpointSet? hpSet = sets[hpIdx];
                if (hpSet == null
                    || !UnfilteredBySet.TryGetValue(hpSet, out List<WeaponMount> master)
                    || CargoSlotMountsProp?.GetValue(slot) is not List<WeaponMount> activeMounts)
                {
                    continue;
                }

                WeaponMount? prevSelected = GroundControlReflection.CargoSlotSelectedMountProp?.GetValue(slot) as WeaponMount;
                activeMounts.Clear();
                int newSelectedIndex = -1;

                for (int m = 0; m < master.Count; m++)
                {
                    WeaponMount? mount = master[m];
                    if (mount == null)
                    {
                        continue;
                    }

                    if (filter != null && targetHq != null && !filter(mount, targetHq))
                    {
                        continue;
                    }

                    if (ReferenceEquals(mount, prevSelected))
                    {
                        newSelectedIndex = activeMounts.Count;
                    }

                    activeMounts.Add(mount);
                }

                GroundControlReflection.CargoSlotSelectedIndexField?.SetValue(slot, newSelectedIndex);
            }
        }
    }

    internal static void InvalidateSyncState()
    {
        lastSyncedHq = null;
        lastFilterPresent = -1;
    }

    internal static Type? UiType => SupplyHeliUiType;
}

[HarmonyPatch]
internal static class SupplyHeliGetCargoMountsFilterPatch
{
    private static bool Prepare() => GroundControlReflection.SupplyHeliGetCargoMountsMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.SupplyHeliGetCargoMountsMethod!;

    private static void Postfix(HardpointSet? set, List<WeaponMount>? __result)
    {
        if (__result == null || __result.Count == 0)
        {
            return;
        }

        SupplyHeliCargoMountFilterHelper.RegisterUnfiltered(set, __result);
        SupplyHeliCargoMountFilterHelper.InvalidateSyncState();
    }
}

[HarmonyPatch]
internal static class SupplyHeliUiDrawWindowFilterPatch
{
    private static readonly MethodInfo? DrawWindowMethod =
        SupplyHeliCargoMountFilterHelper.UiType != null
            ? AccessTools.Method(SupplyHeliCargoMountFilterHelper.UiType, "DrawWindow", new[] { typeof(int) })
            : null;

    private static bool Prepare() => DrawWindowMethod != null;
    private static MethodBase TargetMethod() => DrawWindowMethod!;

    private static void Prefix(object __instance)
    {
        if (AddonConfig.ModEnabled.Value)
        {
            SupplyHeliCargoMountFilterHelper.SyncForUi(__instance);
        }
    }
}

[HarmonyPatch]
internal static class SupplyHeliUiShowForFilterPatch
{
    private static readonly MethodInfo? ShowForMethod =
        SupplyHeliCargoMountFilterHelper.UiType != null
            ? AccessTools.Method(SupplyHeliCargoMountFilterHelper.UiType, "ShowFor", new[] { typeof(AircraftDefinition) })
            : null;

    private static bool Prepare() => ShowForMethod != null;
    private static MethodBase TargetMethod() => ShowForMethod!;

    private static void Postfix(object __instance)
    {
        if (AddonConfig.ModEnabled.Value)
        {
            SupplyHeliCargoMountFilterHelper.InvalidateSyncState();
            SupplyHeliCargoMountFilterHelper.SyncForUi(__instance);
        }
    }
}

[HarmonyPatch]
internal static class SupplyHeliCycleSlotFilterPatch
{
    private static readonly MethodInfo? CycleCargoSlotMethod =
        GroundControlReflection.SupplyHeliServiceType != null
            ? AccessTools.Method(GroundControlReflection.SupplyHeliServiceType, "CycleCargoSlot", new[] { typeof(int) })
            : null;

    private static bool Prepare() => CycleCargoSlotMethod != null;
    private static MethodBase TargetMethod() => CycleCargoSlotMethod!;

    private static void Prefix(object __instance)
    {
        if (AddonConfig.ModEnabled.Value)
        {
            SupplyHeliCargoMountFilterHelper.SyncForService(__instance, GroundControlReflection.GetLocalHq(), force: false);
        }
    }
}

[HarmonyPatch]
internal static class SupplyHeliGetCargoMountLabelPatch
{
    private static bool Prepare() => GroundControlReflection.SupplyHeliGetCargoMountLabelMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.SupplyHeliGetCargoMountLabelMethod!;

    private static void Postfix(WeaponMount mount, ref string __result)
    {
        if (!AddonConfig.ModEnabled.Value || mount == null)
        {
            return;
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        string? labelOverride = MdsBridgeBootstrap.GetMdsAirdropMountLabelOverride?.Invoke(mount, localHq);
        if (!string.IsNullOrEmpty(labelOverride))
        {
            __result = labelOverride!;
            return;
        }

        if (MdsBridgeBootstrap.GetMdsAirdropMountSuffix != null)
        {
            string suffix = MdsBridgeBootstrap.GetMdsAirdropMountSuffix(mount, localHq);
            if (!string.IsNullOrEmpty(suffix))
            {
                __result += suffix;
            }
        }
    }
}

[HarmonyPatch]
internal static class SupplyHeliGetDefaultCargoUnitCostPatch
{
    private static bool Prepare() => GroundControlReflection.SupplyHeliGetDefaultCargoUnitCostMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.SupplyHeliGetDefaultCargoUnitCostMethod!;

    private static bool Prefix(UnitDefinition? cargo, ref float __result)
    {
        if (!AddonConfig.ModEnabled.Value || cargo == null)
        {
            return true;
        }

        float? mdsCost = MdsBridgeBootstrap.GetMdsCargoUnitCost?.Invoke(cargo);
        if (mdsCost.HasValue && mdsCost.Value > 0f)
        {
            __result = mdsCost.Value;
            return false;
        }

        string? key = !string.IsNullOrEmpty(cargo.jsonKey) ? cargo.jsonKey : cargo.name;
        if (!string.IsNullOrEmpty(key) &&
            key!.StartsWith("mds_cargo_", StringComparison.OrdinalIgnoreCase) &&
            cargo.value > 0f)
        {
            __result = cargo.value;
            return false;
        }

        return true;
    }
}

[HarmonyPatch]
internal static class SupplyHeliExcludeMdsFromAmmoCargoPatch
{
    private static bool Prepare() => GroundControlReflection.SupplyHeliTryGetAmmunitionCargoCapacityMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.SupplyHeliTryGetAmmunitionCargoCapacityMethod!;

    private static bool Prefix(WeaponMount? mount, ref float capacity, ref bool __result)
    {
        if (!AddonConfig.ModEnabled.Value || mount == null)
        {
            return true;
        }

        bool isMds = MdsBridgeBootstrap.IsMdsAirdropMount?.Invoke(mount) ?? false;
        if (!isMds)
        {
            string? key = !string.IsNullOrEmpty(mount.jsonKey) ? mount.jsonKey : mount.name;
            isMds = !string.IsNullOrEmpty(key) &&
                    key!.StartsWith("mds_airdrop_", StringComparison.OrdinalIgnoreCase);
        }

        if (isMds)
        {
            capacity = 0f;
            __result = false;
            return false;
        }

        return true;
    }
}

internal static class SupplyHeliCargoGateHelpers
{
    private static readonly List<WeaponMount> MountScratch = new();
    private static readonly PropertyInfo? CargoAircraftDefinitionProp =
        GroundControlReflection.CargoAircraftOptionType != null
            ? AccessTools.Property(GroundControlReflection.CargoAircraftOptionType, "Definition")
            : null;
    private static readonly FieldInfo? QueuedCargoSpawnsField =
        GroundControlReflection.SupplyHeliServiceType != null
            ? AccessTools.Field(GroundControlReflection.SupplyHeliServiceType, "queuedCargoSpawns")
            : null;
    private static readonly PropertyInfo? QueuedSpawnHqProp =
        GroundControlReflection.SupplyHeliQueuedSpawnType != null
            ? AccessTools.Property(GroundControlReflection.SupplyHeliQueuedSpawnType, "Hq")
            : null;

    internal static void ClearScratch()
    {
        MountScratch.Clear();
        SupplyHeliCargoMountFilterHelper.InvalidateSyncState();
    }

    internal static bool TryGetBlockedSelectedReason(object? supplyService, out string reason)
    {
        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        object? selectedOpt = supplyService != null ? GroundControlReflection.SupplyHeliSelectedAircraftProp?.GetValue(supplyService) : null;
        AircraftDefinition? aircraftDef = selectedOpt != null ? CargoAircraftDefinitionProp?.GetValue(selectedOpt) as AircraftDefinition : null;

        if (TryGetAircraftCapOrTechBlockReason(supplyService, localHq, aircraftDef, includeQueuedCount: true, tellPlayer: false, out reason))
        {
            return true;
        }

        return TryGetBlockedLoadoutReason(supplyService, localHq, GroundControlReflection.CollectSupplyHeliSelectedMounts, out reason);
    }

    internal static bool TryGetBlockedPendingReason(object? supplyService, out string reason)
    {
        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        object? pending = supplyService != null ? GroundControlReflection.SupplyHeliPendingTargetSelectionField?.GetValue(supplyService) : null;
        object? pendingOpt = pending != null ? GroundControlReflection.PendingTargetSelectionAircraftProp?.GetValue(pending) : null;
        AircraftDefinition? aircraftDef = pendingOpt != null ? CargoAircraftDefinitionProp?.GetValue(pendingOpt) as AircraftDefinition : null;

        if (TryGetAircraftCapOrTechBlockReason(supplyService, localHq, aircraftDef, includeQueuedCount: true, tellPlayer: false, out reason))
        {
            return true;
        }

        return TryGetBlockedLoadoutReason(supplyService, localHq, GroundControlReflection.CollectSupplyHeliPendingLoadoutMounts, out reason);
    }

    internal static bool TryGetAircraftCapOrTechBlockReason(
        object? supplyService,
        FactionHQ? hq,
        AircraftDefinition? aircraftDef,
        bool includeQueuedCount,
        bool tellPlayer,
        out string reason)
    {
        reason = string.Empty;
        if (!DynamicCapAndTechService.IsActive || hq == null || aircraftDef == null || SingleplayerCheatService.IsIgnoreCapsActiveFor(hq))
        {
            return false;
        }

        if (DynamicCapAndTechService.Instance.TryGetSpawnBlockReason(hq, aircraftDef, tellPlayer, out reason))
        {
            return true;
        }

        if (includeQueuedCount)
        {
            int queued = CountQueuedCargoSpawnsForHq(supplyService, hq);
            if (queued > 0)
            {
                FactionCapSnapshot snap = DynamicCapAndTechService.Instance.GetFreshSnapshot(hq);
                if (snap.AirUsed + queued >= snap.AirCap)
                {
                    reason = $"AIR CAP REACHED: {snap.AirUsed}+{queued}/{snap.AirCap} — Build Pilot Academy (Shift+C)";
                    return true;
                }
            }
        }

        return false;
    }

    internal static AircraftDefinition? FindAircraftDefinitionByJsonKey(string? jsonKey)
    {
        if (string.IsNullOrEmpty(jsonKey))
        {
            return null;
        }

        if (Encyclopedia.Lookup != null
            && Encyclopedia.Lookup.TryGetValue(jsonKey!, out UnitDefinition found)
            && found is AircraftDefinition adef)
        {
            return adef;
        }

        List<AircraftDefinition>? aircraft = Encyclopedia.i?.aircraft;
        if (aircraft != null)
        {
            for (int i = 0; i < aircraft.Count; i++)
            {
                AircraftDefinition? a = aircraft[i];
                if (a != null && string.Equals(a.jsonKey, jsonKey, StringComparison.Ordinal))
                {
                    return a;
                }
            }
        }

        return null;
    }

    internal static AircraftDefinition? GetQueuedSpawnAircraftDefinition(object? queuedSpawn)
    {
        if (queuedSpawn == null)
        {
            return null;
        }

        PropertyInfo? aircraftProp = queuedSpawn.GetType().GetProperty("Aircraft", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object? cargoOpt = aircraftProp?.GetValue(queuedSpawn);
        return cargoOpt != null ? CargoAircraftDefinitionProp?.GetValue(cargoOpt) as AircraftDefinition : null;
    }

    private static int CountQueuedCargoSpawnsForHq(object? supplyService, FactionHQ hq)
    {
        if (supplyService == null || QueuedCargoSpawnsField == null || QueuedSpawnHqProp == null)
        {
            return 0;
        }

        if (QueuedCargoSpawnsField.GetValue(supplyService) is not IEnumerable queued)
        {
            return 0;
        }

        int count = 0;
        foreach (object? item in queued)
        {
            if (item != null && ReferenceEquals(QueuedSpawnHqProp.GetValue(item), hq))
            {
                count++;
            }
        }

        return count;
    }

    private static bool TryGetBlockedLoadoutReason(
        object? supplyService,
        FactionHQ? targetHq,
        Action<object?, List<WeaponMount>> collector,
        out string reason)
    {
        reason = string.Empty;
        if (targetHq == null || MdsBridgeBootstrap.GetMdsAirdropLoadoutBlockReason == null)
        {
            return false;
        }

        collector(supplyService, MountScratch);
        if (MountScratch.Count == 0)
        {
            return false;
        }

        string? block = MdsBridgeBootstrap.GetMdsAirdropLoadoutBlockReason(MountScratch, targetHq);
        if (string.IsNullOrEmpty(block))
        {
            return false;
        }

        reason = block!;
        return true;
    }
}

[HarmonyPatch]
internal static class SupplyHeliHostCargoRunGatePatch
{
    [ThreadStatic]
    internal static FactionHQ? ActiveGuestCargoHq;

    private static bool Prepare() => GroundControlReflection.SupplyHeliHostCargoRunMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.SupplyHeliHostCargoRunMethod!;

    private static bool Prefix(object __instance, FactionHQ? hq, string[]? args, out FactionHQ? __state)
    {
        __state = ActiveGuestCargoHq;
        ActiveGuestCargoHq = hq;

        if (!AddonConfig.ModEnabled.Value || hq == null || args == null)
        {
            return true;
        }

        if (args.Length > 1)
        {
            AircraftDefinition? aircraftDef = SupplyHeliCargoGateHelpers.FindAircraftDefinitionByJsonKey(args[1]);
            if (SupplyHeliCargoGateHelpers.TryGetAircraftCapOrTechBlockReason(
                    __instance,
                    hq,
                    aircraftDef,
                    includeQueuedCount: true,
                    tellPlayer: false,
                    out string capReason))
            {
                GroundControlReflection.NoticeFail(capReason);
                ActiveGuestCargoHq = __state;
                return false;
            }
        }

        if (args.Length <= 3 || MdsBridgeBootstrap.GetMdsAirdropLoadoutBlockReason == null)
        {
            return true;
        }

        string rawMounts = args[3] ?? string.Empty;
        if (rawMounts.Length == 0 || Encyclopedia.i?.weaponMounts == null)
        {
            return true;
        }

        string[] keys = rawMounts.Split(',');
        var mounts = new List<WeaponMount>(keys.Length);
        List<WeaponMount> allMounts = Encyclopedia.i.weaponMounts;
        for (int k = 0; k < keys.Length; k++)
        {
            string key = keys[k];
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            for (int m = 0; m < allMounts.Count; m++)
            {
                WeaponMount wm = allMounts[m];
                if (wm != null && string.Equals(wm.jsonKey, key, StringComparison.Ordinal))
                {
                    mounts.Add(wm);
                    break;
                }
            }
        }

        if (mounts.Count > 0)
        {
            string? reason = MdsBridgeBootstrap.GetMdsAirdropLoadoutBlockReason(mounts, hq);
            if (!string.IsNullOrEmpty(reason))
            {
                GroundControlReflection.NoticeFail(reason!);
                ActiveGuestCargoHq = __state;
                return false;
            }
        }

        return true;
    }

    private static Exception? Finalizer(Exception? __exception, FactionHQ? __state)
    {
        ActiveGuestCargoHq = __state;
        return __exception;
    }
}

[HarmonyPatch]
internal static class SupplyHeliTrySpawnAtAirbaseCapGatePatch
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

    private static bool Prefix(object __instance, object? request, FactionHQ? hq)
    {
        if (!AddonConfig.ModEnabled.Value || hq == null || request == null)
        {
            return true;
        }

        AircraftDefinition? aircraftDef = SupplyHeliCargoGateHelpers.GetQueuedSpawnAircraftDefinition(request);
        if (SupplyHeliCargoGateHelpers.TryGetAircraftCapOrTechBlockReason(
                __instance,
                hq,
                aircraftDef,
                includeQueuedCount: false,
                tellPlayer: false,
                out string reason))
        {
            GroundControlReflection.InvokeSupplyHeliSetStatus(__instance, reason);
            if (ReferenceEquals(hq, GroundControlReflection.GetLocalHq()))
            {
                GroundControlReflection.NoticeFail(reason);
            }

            return false;
        }

        return true;
    }
}

[HarmonyPatch]
internal static class SupplyHeliBeginCargoRunGatePatch
{
    private static bool Prepare() => GroundControlReflection.SupplyHeliBeginSelectedCargoRunMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.SupplyHeliBeginSelectedCargoRunMethod!;

    private static bool Prefix(object __instance)
    {
        if (!AddonConfig.ModEnabled.Value)
        {
            return true;
        }

        if (SupplyHeliCargoGateHelpers.TryGetBlockedSelectedReason(__instance, out string reason))
        {
            GroundControlReflection.InvokeSupplyHeliSetStatus(__instance, reason);
            GroundControlReflection.NoticeFail(reason);
            return false;
        }

        return true;
    }
}

[HarmonyPatch]
internal static class SupplyHeliTrySpawnAtWorldPointGatePatch
{
    private static bool Prepare() => GroundControlReflection.SupplyHeliTrySpawnAtWorldPointMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.SupplyHeliTrySpawnAtWorldPointMethod!;

    private static bool Prefix(object __instance, ref bool __result)
    {
        if (!AddonConfig.ModEnabled.Value)
        {
            return true;
        }

        if (SupplyHeliCargoGateHelpers.TryGetBlockedPendingReason(__instance, out string reason))
        {
            GroundControlReflection.InvokeSupplyHeliCancelTargetSelection(__instance, false);
            GroundControlReflection.InvokeSupplyHeliSetStatus(__instance, reason);
            GroundControlReflection.NoticeFail(reason);
            __result = true;
            return false;
        }

        return true;
    }
}

[HarmonyPatch]
internal static class MoveSetSelectionStanceMdsPatch
{
    private static bool Prepare() => GroundControlReflection.MoveSetSelectionStanceMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.MoveSetSelectionStanceMethod!;

    private static void Postfix(object stance)
    {
        if (!AddonConfig.ModEnabled.Value ||
            MdsBridgeBootstrap.QueryMdsSiteUiStatus == null ||
            MdsBridgeBootstrap.SetMdsSiteHoldFire == null)
        {
            return;
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        IReadOnlyList<Unit>? selected = GroundControlReflection.GetSelectedUnits();
        if (localHq == null || selected == null || selected.Count == 0)
        {
            return;
        }

        bool holdFire = stance != null && string.Equals(stance.ToString(), "HoldFire", StringComparison.OrdinalIgnoreCase);
        for (int i = 0; i < selected.Count; i++)
        {
            Unit? u = selected[i];
            if (u != null && (u is Building || u is Container || u is GroundVehicle) && !u.disabled && ReferenceEquals(u.NetworkHQ, localHq))
            {
                var status = MdsBridgeBootstrap.QueryMdsSiteUiStatus(u);
                if (status.IsMdsSite)
                {
                    MdsBridgeBootstrap.SetMdsSiteHoldFire(u, holdFire);
                }
            }
        }
    }
}

[HarmonyPatch]
internal static class GameAccessIsCommanderMarkerUnitMdsPatch
{
    private static bool Prepare() => GroundControlReflection.GameAccessIsCommanderMarkerUnitMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.GameAccessIsCommanderMarkerUnitMethod!;

    private static void Postfix(Unit unit, ref bool __result)
    {
        if (!__result &&
            AddonConfig.ModEnabled.Value &&
            unit != null &&
            !unit.disabled &&
            MdsBridgeBootstrap.IsMdsSelectableUnit?.Invoke(unit) == true)
        {
            __result = true;
        }
    }
}

[HarmonyPatch]
internal static class OverlayDrawOrderRowStanceEnablePatch
{
    private static readonly MethodInfo? GuiSetEnabledSetter =
        AccessTools.Property(typeof(GUI), nameof(GUI.enabled))?.GetSetMethod(true);

    private static bool Prepare()
        => GroundControlReflection.OverlayDrawOrderRowMethod != null
           && GroundControlReflection.MoveSelectionStanceGetter != null
           && GuiSetEnabledSetter != null;

    private static MethodBase TargetMethod() => GroundControlReflection.OverlayDrawOrderRowMethod!;

    public static bool AdjustStanceButtonEnabled(bool currentEnabled, bool oldEnabled)
        => currentEnabled
           || (oldEnabled
               && AddonConfig.ModEnabled.Value
               && MdsBridgeBootstrap.HasSelectedFriendlyMdsWeaponSite?.Invoke() == true);

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);
        MethodInfo stanceGetter = GroundControlReflection.MoveSelectionStanceGetter!;
        MethodInfo setEnabled = GuiSetEnabledSetter!;
        bool patched = false;

        for (int i = 0; i < list.Count; i++)
        {
            if ((list[i].opcode == OpCodes.Call || list[i].opcode == OpCodes.Callvirt) &&
                list[i].operand is MethodInfo mi &&
                mi == stanceGetter)
            {
                int searchMin = Math.Max(0, i - 10);
                for (int j = i - 1; j >= searchMin; j--)
                {
                    if ((list[j].opcode == OpCodes.Call || list[j].opcode == OpCodes.Callvirt) &&
                        list[j].operand is MethodInfo guiMi &&
                        guiMi == setEnabled)
                    {
                        var ldarg1 = new CodeInstruction(OpCodes.Ldarg_1);
                        ldarg1.MoveLabelsFrom(list[j]);
                        var callAdjust = CodeInstruction.Call(
                            typeof(OverlayDrawOrderRowStanceEnablePatch),
                            nameof(AdjustStanceButtonEnabled));
                        list.Insert(j, callAdjust);
                        list.Insert(j, ldarg1);
                        patched = true;
                        break;
                    }
                }

                break;
            }
        }

        if (!patched)
        {
            AddonLog.Warn("[UI] OverlayDrawOrderRowStanceEnablePatch could not locate GUI.set_enabled before get_SelectionStance.");
        }

        return list;
    }
}

[HarmonyPatch]
internal static class MoveCycleSelectionStanceMdsPatch
{
    private static bool Prepare() => GroundControlReflection.MoveCycleSelectionStanceMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.MoveCycleSelectionStanceMethod!;

    private static bool Prefix(object __instance)
    {
        if (!AddonConfig.ModEnabled.Value ||
            __instance == null ||
            GroundControlReflection.GetMoveHasCommandableSelection(__instance) ||
            GroundControlReflection.GetMoveHasSelectedAircraft(__instance) ||
            MdsBridgeBootstrap.HasSelectedFriendlyMdsWeaponSite?.Invoke() != true)
        {
            return true;
        }

        Unit? focused = GroundControlReflection.GetFocusedSelection();
        bool currentHold = GroundControlReflection.IsUnitHoldingFire(focused);
        if (focused != null && MdsBridgeBootstrap.QueryMdsSiteUiStatus != null)
        {
            var status = MdsBridgeBootstrap.QueryMdsSiteUiStatus(focused);
            if (status.IsMdsSite)
            {
                currentHold = status.IsHoldFire;
            }
        }

        GroundControlReflection.InvokeMoveSetSelectionStance(__instance, !currentHold);
        return false;
    }
}

[HarmonyPatch]
internal static class RadarIsRadarGuidedLauncherMdsPatch
{
    private static bool Prepare() => GroundControlReflection.RadarIsRadarGuidedLauncherMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.RadarIsRadarGuidedLauncherMethod!;

    private static void Postfix(Unit? unit, ref bool __result)
    {
        if (!__result &&
            AddonConfig.ModEnabled.Value &&
            unit != null &&
            MdsBridgeBootstrap.QueryMdsSiteUiStatus != null)
        {
            var status = MdsBridgeBootstrap.QueryMdsSiteUiStatus(unit);
            if (status.IsMdsSite && status.RequiresFcuLink)
            {
                __result = true;
            }
        }
    }
}

[HarmonyPatch]
internal static class RadarGetLauncherLinkShortfallMdsPatch
{
    private static bool Prepare() => GroundControlReflection.RadarGetLauncherLinkShortfallMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.RadarGetLauncherLinkShortfallMethod!;

    private static bool Prefix(Unit launcher, ref string __result)
    {
        if (!AddonConfig.ModEnabled.Value || launcher == null || MdsBridgeBootstrap.QueryMdsSiteUiStatus == null)
        {
            return true;
        }

        var status = MdsBridgeBootstrap.QueryMdsSiteUiStatus(launcher);
        if (!status.IsMdsSite || !status.RequiresFcuLink)
        {
            return true;
        }

        if (status.HasFcuLink)
        {
            __result = string.Empty;
        }
        else
        {
            __result = status.EoIrBackupKm > 0f
                ? $"no Skyshield FCU within {status.FcuRadiusKm:0} km (EO/IR {status.EoIrBackupKm:0.0} km backup only)"
                : $"no Skyshield FCU within {status.FcuRadiusKm:0} km";
        }

        return false;
    }
}

[HarmonyPatch]
internal static class MobileEmplacementIsMoveableTrailerMdsPatch
{
    private static bool Prepare() => GroundControlReflection.MobileEmplacementIsMoveableTrailerMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.MobileEmplacementIsMoveableTrailerMethod!;

    private static void Postfix(Unit? unit, ref bool __result)
    {
        if (!__result && AddonConfig.ModEnabled.Value && MdsFlatbedHaulService.Instance.IsMdsContainerTrailer(unit))
        {
            __result = true;
        }
    }
}

[HarmonyPatch]
internal static class MobileEmplacementIsTowingHaulerMdsPatch
{
    private static bool Prepare() => GroundControlReflection.MobileEmplacementIsTowingHaulerMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.MobileEmplacementIsTowingHaulerMethod!;

    private static void Postfix(Unit? unit, ref bool __result)
    {
        if (!__result && AddonConfig.ModEnabled.Value && MdsFlatbedHaulService.Instance.IsMdsTowingHauler(unit))
        {
            __result = true;
        }
    }
}

[HarmonyPatch]
internal static class MobileEmplacementIsReservedHaulerMdsPatch
{
    private static bool Prepare() => GroundControlReflection.MobileEmplacementIsReservedHaulerMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.MobileEmplacementIsReservedHaulerMethod!;

    private static void Postfix(Unit? unit, ref bool __result)
    {
        if (!__result && AddonConfig.ModEnabled.Value && MdsFlatbedHaulService.Instance.IsMdsReservedHauler(unit))
        {
            __result = true;
        }
    }
}

[HarmonyPatch]
internal static class MobileEmplacementIsTowedByMdsPatch
{
    private static bool Prepare() => GroundControlReflection.MobileEmplacementIsTowedByMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.MobileEmplacementIsTowedByMethod!;

    private static void Postfix(GroundVehicle tractor, Transform? obstacle, ref bool __result)
    {
        if (!__result && AddonConfig.ModEnabled.Value && MdsFlatbedHaulService.Instance.IsMdsTowedBy(tractor, obstacle))
        {
            __result = true;
        }
    }
}

[HarmonyPatch]
internal static class MobileEmplacementIsInTowJobMdsPatch
{
    private static bool Prepare() => GroundControlReflection.MobileEmplacementIsInTowJobMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.MobileEmplacementIsInTowJobMethod!;

    private static void Postfix(Unit? unit, ref bool towing, ref bool __result)
    {
        if (!__result &&
            AddonConfig.ModEnabled.Value &&
            MdsFlatbedHaulService.Instance.TryGetMdsTowJobState(unit, out bool mdsTowing))
        {
            __result = true;
            towing = mdsTowing;
        }
    }
}

[HarmonyPatch]
internal static class MobileEmplacementHasAvailableHaulerMdsPatch
{
    private static bool Prepare() => GroundControlReflection.MobileEmplacementHasAvailableHaulerMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.MobileEmplacementHasAvailableHaulerMethod!;

    private static void Postfix(Unit? unit, ref bool __result)
    {
        if (!__result && AddonConfig.ModEnabled.Value && MdsFlatbedHaulService.Instance.HasAvailableHaulerForMds(unit))
        {
            __result = true;
        }
    }
}

[HarmonyPatch]
internal static class MobileEmplacementBeginHitchMdsPatch
{
    private static bool Prepare() => GroundControlReflection.MobileEmplacementBeginHitchMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.MobileEmplacementBeginHitchMethod!;

    private static bool Prefix()
    {
        if (AddonConfig.ModEnabled.Value && MdsFlatbedHaulService.Instance.TryBeginHitchFocusedFromNativeUi())
        {
            return false;
        }

        return true;
    }
}

[HarmonyPatch]
internal static class MobileEmplacementDropTrailerMdsPatch
{
    private static bool Prepare() => GroundControlReflection.MobileEmplacementDropTrailerMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.MobileEmplacementDropTrailerMethod!;

    private static bool Prefix(Unit? unit)
    {
        if (AddonConfig.ModEnabled.Value && MdsFlatbedHaulService.Instance.TryDropMdsTrailer(unit))
        {
            return false;
        }

        return true;
    }
}

[HarmonyPatch]
internal static class EconomyIsHeldDefenceMdsPatch
{
    private static bool Prepare() => GroundControlReflection.EcoIsHeldDefenceMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoIsHeldDefenceMethod!;

    private static void Postfix(Unit? unit, ref bool __result)
    {
        if (__result || !AddonConfig.ModEnabled.Value || unit == null || MdsBridgeBootstrap.QueryMdsSiteUiStatus == null)
        {
            return;
        }

        var status = MdsBridgeBootstrap.QueryMdsSiteUiStatus(unit);
        if (status.IsMdsSite && (status.IsWeaponSite || status.IsFireControlRadar))
        {
            __result = true;
        }
    }
}

[HarmonyPatch]
internal static class EconomyGetDefencePriceMdsPatch
{
    private static bool Prepare() => GroundControlReflection.EcoGetDefencePriceMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoGetDefencePriceMethod!;

    private static void Postfix(Unit? unit, ref float __result)
    {
        if (!AddonConfig.ModEnabled.Value || unit == null || MdsBridgeBootstrap.GetMdsDefencePrice == null)
        {
            return;
        }

        float? mdsPrice = MdsBridgeBootstrap.GetMdsDefencePrice(unit);
        if (mdsPrice.HasValue && mdsPrice.Value > 0f)
        {
            // Airdropped/slung Container MDS sites cannot be repaired by Engineers (Container is not IRepairable),
            // so charge 50% upkeep compared to permanent repairable Building sites.
            float effectivePrice = unit is Container ? mdsPrice.Value * 0.5f : mdsPrice.Value;
            __result = Mathf.Max(0.05f, effectivePrice);
        }
    }
}

[HarmonyPatch]
internal static class OverlayTryGetUnitSystemsTargetMdsPatch
{
    private static bool Prepare() => GroundControlReflection.OverlayTryGetUnitSystemsTargetMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.OverlayTryGetUnitSystemsTargetMethod!;

    private static void Postfix(ref Unit? unit, ref bool __result)
    {
        if (__result ||
            !AddonConfig.ModEnabled.Value ||
            unit == null ||
            unit.disabled ||
            MdsBridgeBootstrap.QueryMdsSiteUiStatus == null)
        {
            return;
        }

        if (MdsBridgeBootstrap.QueryMdsSiteUiStatus(unit).IsMdsSite)
        {
            __result = true;
        }
    }
}

[HarmonyPatch]
internal static class OverlayDrawRadarWindowMdsPatch
{
    private static bool Prepare()
        => GroundControlReflection.OverlayDrawRadarWindowMethod != null
           && GroundControlReflection.OverlayRadarWindowRectField != null
           && GroundControlReflection.OverlayRadarContentHeightField != null;

    private static MethodBase TargetMethod() => GroundControlReflection.OverlayDrawRadarWindowMethod!;

    private static void Postfix(object? __instance)
    {
        if (!AddonConfig.ModEnabled.Value || __instance == null || MdsBridgeBootstrap.QueryMdsSiteUiStatus == null)
        {
            return;
        }

        Unit? unit = GroundControlReflection.GetFocusedSelection();
        if (unit == null || unit.disabled)
        {
            return;
        }

        var status = MdsBridgeBootstrap.QueryMdsSiteUiStatus(unit);
        if (!status.IsMdsSite)
        {
            return;
        }

        string? lineText = null;
        GUIStyle? lineStyle = null;
        if (status.IsCommandCenter)
        {
            if (status.CommandNetFcuCount > 0)
            {
                lineText = $"SKYSHIELD.NET: ONLINE ({status.CommandNetActiveFcuCount}/{status.CommandNetFcuCount} FCUs \u00b7 {status.CommandNetEffectorCount} SITES \u00b7 COVERAGE-FIRST)";
                lineStyle = NativeThemeBridge.Header;
            }
            else
            {
                lineText = "SKYSHIELD.NET: STANDBY (NO FCU WITHIN 45 KM)";
                lineStyle = NativeThemeBridge.WarningLabel;
            }
        }
        else if (status.IsFireControlRadar)
        {
            if (status.IsInCommandNet)
            {
                lineText = $"SKYSHIELD.NET: LINKED ({status.CommandNetActiveFcuCount}/{status.CommandNetFcuCount} FCUs \u00b7 CEC / BLINKING)";
                lineStyle = NativeThemeBridge.Header;
            }
            else
            {
                lineText = "STANDALONE FCU (NO LEITSTAND IN 45 KM)";
                lineStyle = NativeThemeBridge.MutedLabel;
            }
        }
        else if (status.IsWeaponSite && status.IsInCommandNet)
        {
            lineText = $"SKYSHIELD.NET: CEC COVERAGE-FIRST ({status.CommandNetActiveFcuCount}/{status.CommandNetFcuCount} FCUs)";
            lineStyle = NativeThemeBridge.Header;
        }

        if (string.IsNullOrEmpty(lineText) || lineStyle == null)
        {
            return;
        }

        if (GroundControlReflection.OverlayRadarWindowRectField?.GetValue(__instance) is not Rect wRect ||
            GroundControlReflection.OverlayRadarContentHeightField?.GetValue(__instance) is not float contentH)
        {
            return;
        }

        float y = Mathf.Max(38f, contentH - 4f);
        GUI.Label(new Rect(12f, y, wRect.width - 24f, 24f), lineText!, lineStyle);
        GroundControlReflection.OverlayRadarContentHeightField.SetValue(__instance, y + 32f);
    }
}

internal static class MdsLoadoutIconCache
{
    internal const string IconIndentPrefix = "       ";
    internal static readonly List<Texture2D?> RowIcons = new();

    internal static void Clear()
    {
        RowIcons.Clear();
    }
}

[HarmonyPatch]
internal static class OverlayRefreshLoadoutIconsMdsPatch
{
    private static bool Prepare()
        => GroundControlReflection.OverlayRefreshLoadoutMethod != null
           && GroundControlReflection.OverlayLoadoutUnitField != null
           && GroundControlReflection.OverlayLoadoutRowsField != null
           && GroundControlReflection.OverlayLoadoutNamesField != null;

    private static MethodBase TargetMethod() => GroundControlReflection.OverlayRefreshLoadoutMethod!;

    private static void Postfix(object? __instance)
    {
        if (__instance == null)
        {
            MdsLoadoutIconCache.Clear();
            return;
        }

        Unit? unit = GroundControlReflection.OverlayLoadoutUnitField?.GetValue(__instance) as Unit;
        List<string>? rows = GroundControlReflection.OverlayLoadoutRowsField?.GetValue(__instance) as List<string>;
        List<string>? names = GroundControlReflection.OverlayLoadoutNamesField?.GetValue(__instance) as List<string>;

        MdsLoadoutIconCache.Clear();
        if (!AddonConfig.ModEnabled.Value ||
            unit == null ||
            rows == null ||
            names == null ||
            rows.Count == 0 ||
            unit.weaponStations == null)
        {
            return;
        }

        for (int i = 0; i < names.Count; i++)
        {
            Texture2D? matchedTex = null;
            string rowName = names[i];
            if (!string.IsNullOrEmpty(rowName))
            {
                for (int s = 0; s < unit.weaponStations.Count; s++)
                {
                    WeaponStation? st = unit.weaponStations[s];
                    WeaponInfo? wi = st?.WeaponInfo;
                    if (wi == null || wi.weaponIcon == null || wi.weaponIcon.texture == null)
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(wi.name) ||
                        !wi.name.StartsWith("mds_wi_", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (string.Equals(wi.shortName, rowName, StringComparison.Ordinal) ||
                        string.Equals(wi.weaponName, rowName, StringComparison.Ordinal))
                    {
                        matchedTex = wi.weaponIcon.texture;
                        break;
                    }
                }
            }

            MdsLoadoutIconCache.RowIcons.Add(matchedTex);
            if (matchedTex != null &&
                i < rows.Count &&
                !rows[i].StartsWith(MdsLoadoutIconCache.IconIndentPrefix, StringComparison.Ordinal))
            {
                rows[i] = MdsLoadoutIconCache.IconIndentPrefix + rows[i];
            }
        }
    }
}

[HarmonyPatch]
internal static class OverlayDrawLoadoutIconsMdsPatch
{
    private static bool Prepare()
        => GroundControlReflection.OverlayDrawLoadoutMethod != null
           && GroundControlReflection.OverlaySelectionBarRectField != null
           && GroundControlReflection.OverlayLoadoutRowsField != null;

    private static MethodBase TargetMethod() => GroundControlReflection.OverlayDrawLoadoutMethod!;

    private static void Postfix(object? __instance)
    {
        if (!AddonConfig.ModEnabled.Value ||
            __instance == null ||
            Event.current == null ||
            Event.current.type != EventType.Repaint ||
            MdsLoadoutIconCache.RowIcons.Count == 0)
        {
            return;
        }

        if (GroundControlReflection.OverlaySelectionBarRectField?.GetValue(__instance) is not Rect barRect ||
            GroundControlReflection.OverlayLoadoutRowsField?.GetValue(__instance) is not List<string> rows ||
            rows.Count == 0)
        {
            return;
        }

        float colW = (barRect.width - 34f) * 0.5f;
        float startY = barRect.y + 68f;
        int count = Math.Min(rows.Count, MdsLoadoutIconCache.RowIcons.Count);

        for (int i = 0; i < count; i++)
        {
            Texture2D? tex = MdsLoadoutIconCache.RowIcons[i];
            if (tex == null)
            {
                continue;
            }

            float x = barRect.x + 14f + ((i % 2 == 0) ? 0f : (colW + 6f));
            float y = startY + (i / 2) * 22f;
            GUI.DrawTexture(new Rect(x, y + 2f, 22f, 18f), tex, ScaleMode.ScaleToFit, true);
        }
    }
}

[HarmonyPatch]
internal static class GameAccessTryGetLocalVehicleDefinitionsMdsPatch
{
    private static bool Prepare() => GroundControlReflection.GameAccessTryGetLocalVehicleDefinitionsMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.GameAccessTryGetLocalVehicleDefinitionsMethod!;

    private static void Prefix()
    {
        if (AddonConfig.ModEnabled.Value)
        {
            MdsBridgeBootstrap.EnsureMdsCatalogRegisteredAction?.Invoke();
        }
    }

    private static void Postfix(List<VehicleDefinition>? buffer, ref bool __result)
    {
        if (!AddonConfig.ModEnabled.Value || buffer == null)
        {
            return;
        }

        MdsBridgeBootstrap.AppendMissingMdsVehicleDefinitions?.Invoke(buffer);
        if (buffer.Count > 0)
        {
            __result = true;
        }
    }
}

[HarmonyPatch]
internal static class SpawnServiceRefreshFactionVehiclesMdsPatch
{
    private static bool refreshingVehicles;

    private static bool Prepare()
        => GroundControlReflection.SpawnServiceRefreshFactionVehiclesMethod != null
           && GroundControlReflection.SpawnServiceRefreshVehiclesMethod != null
           && GroundControlReflection.SpawnServiceVehicleDefinitionsField != null;

    private static MethodBase TargetMethod() => GroundControlReflection.SpawnServiceRefreshFactionVehiclesMethod!;

    private static void Prefix(object? __instance)
    {
        if (!AddonConfig.ModEnabled.Value || refreshingVehicles || __instance == null)
        {
            return;
        }

        if (GroundControlReflection.SpawnServiceVehicleDefinitionsField?.GetValue(__instance) is List<VehicleDefinition> list
            && MdsBridgeBootstrap.HasMissingMdsVehicleDefinitions?.Invoke(list) == true)
        {
            refreshingVehicles = true;
            try
            {
                GroundControlReflection.SpawnServiceRefreshVehiclesMethod?.Invoke(__instance, null);
            }
            finally
            {
                refreshingVehicles = false;
            }
        }
    }
}

[HarmonyPatch]
internal static class FactionVehicleCanPurchaseMdsPatch
{
    private static bool Prepare() => GroundControlReflection.FactionVehicleCanPurchaseMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.FactionVehicleCanPurchaseMethod!;

    private static void Postfix(VehicleDefinition? definition, FactionHQ? hq, ref bool __result)
    {
        if (!AddonConfig.ModEnabled.Value || definition == null)
        {
            return;
        }

        if (MdsBridgeBootstrap.TryCanPurchaseMdsVehicle?.Invoke(definition, hq) is bool allowed)
        {
            __result = allowed;
        }
    }
}

[HarmonyPatch]
internal static class EconomyIsSupplyTruckMdsPatch
{
    private static bool Prepare() => GroundControlReflection.EcoIsSupplyTruckMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoIsSupplyTruckMethod!;

    private static bool Prefix(VehicleDefinition? definition, ref bool __result)
    {
        if (AddonConfig.ModEnabled.Value && MdsBridgeBootstrap.IsMdsMobileVehicleDefinition?.Invoke(definition) == true)
        {
            __result = false;
            return false;
        }

        return true;
    }
}

[HarmonyPatch]
internal static class EconomyGetRequiredDepotLevelMdsPatch
{
    private static bool Prepare() => GroundControlReflection.GetRequiredDepotLevelMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.GetRequiredDepotLevelMethod!;

    private static bool Prefix(VehicleDefinition? definition, ref int __result)
    {
        if (AddonConfig.ModEnabled.Value && MdsBridgeBootstrap.GetMdsVehicleRequiredDepotLevel?.Invoke(definition) is int level)
        {
            __result = level;
            return false;
        }

        return true;
    }
}
