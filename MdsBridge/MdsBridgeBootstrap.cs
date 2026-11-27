using System;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;

namespace GroundControlRtsExpanded;

internal static class MdsBridgeBootstrap
{
    internal const string MdsFrameworkPluginGuid = "com.mds.framework";

    internal static bool IsMdsAttached { get; set; }
    internal static string BridgeStatusSummary { get; set; } = "MDS Bridge: Standalone Mode (no MDSFramework)";

    internal static Func<FactionHQ, (int Ground, int Air, int Naval)>? GetReservedCapOverride { get; set; }
    internal static Func<FactionHQ, int>? CountHostileOrOwnedMdsRadarsForHq { get; set; }
    internal static Func<FactionHQ, GlobalPosition, (Unit? Target, GlobalPosition Pos)>? TryFindPriorityMdsSeadTarget { get; set; }
    internal static Func<FactionHQ, bool>? HasEmittingHostileMdsRadar { get; set; }
    internal static Action<FactionHQ>? TickAiMdsDeploymentAction { get; set; }
    internal static Func<Unit?, bool>? IsUnitBuildingMdsSite { get; set; }
    internal static Action<FactionHQ, System.Collections.Generic.List<GlobalPosition>>? CollectFriendlyMdsEwPositions { get; set; }
    internal static Func<FactionHQ, int>? GetHostileMdsCompensation { get; set; }
    internal static Func<FactionHQ, int>? CountKnownHostileMdsSites { get; set; }
    internal static Action<Unit, GlobalPosition>? UpdateMdsSitePosition { get; set; }

    // Future Milestone 6 (ABM Interceptor Plant) hook points
    internal static Func<FactionHQ, int>? ExternalAbmStockpileQuery { get; set; }
    internal static Func<FactionHQ, string, bool>? ExternalAbmStockpileReserve { get; set; }
    internal static Action<FactionHQ, GlobalPosition, float>? RaiseBallisticThreatAction { get; set; }

    // Native BUILD menu, AIR TRANSPORT airdrop injection, & Trailer/Flatbed loading hook points
    internal static Action? EnsureAirdropMountsInjectedAction { get; set; }
    internal static Func<WeaponMount, FactionHQ?, bool>? IsMdsAirdropMountAllowedForHq { get; set; }
    internal static Func<WeaponMount, FactionHQ?, string>? GetMdsAirdropMountSuffix { get; set; }
    internal static Func<WeaponMount, FactionHQ?, string?>? GetMdsAirdropMountLabelOverride { get; set; }
    internal static Func<System.Collections.Generic.IReadOnlyList<WeaponMount>, FactionHQ?, string?>? GetMdsAirdropLoadoutBlockReason { get; set; }
    internal static Func<UnitDefinition?, float?>? GetMdsCargoUnitCost { get; set; }
    internal static Func<WeaponMount?, bool>? IsMdsAirdropMount { get; set; }
    internal static Func<FactionHQ?, System.Collections.Generic.IReadOnlyList<BuildingDefinition>>? GetMdsBuildCatalogDefinitionsForHq { get; set; }
    internal static Func<BuildingDefinition?, bool>? IsMdsBuildDefinition { get; set; }
    internal static Func<BuildingDefinition?, bool>? IsRawMdsEncyclopediaDefinition { get; set; }
    internal static Func<BuildingDefinition?, float>? GetMdsBuildCost { get; set; }
    internal static Func<BuildingDefinition?, FactionHQ?, string>? GetMdsBuildLabel { get; set; }
    internal static Func<BuildingDefinition?, FactionHQ?, string?>? GetMdsBuildBlockReason { get; set; }
    internal static Func<BuildingDefinition, FactionHQ, Unit, bool>? TryReplaceBuiltMdsPlaceholder { get; set; }
    internal static Func<Unit?, bool>? IsMdsHaulableContainer { get; set; }
    internal static Action<Unit, bool>? SetMdsContainerTransitState { get; set; }
    internal static Func<UnitDefinition?, (string GlbPath, float TargetMaxMeters, bool SampleEndFrame)?>? TryResolveMdsPreviewGlb { get; set; }
    internal static Func<UnityEngine.Vector2, bool>? IsLogisticsWindowUnderScreenPoint { get; set; }
    internal static Action<FactionHQ>? CompleteMdsEngineerOrdersForHq { get; set; }
    internal static Func<Unit?, (bool IsMdsSite, bool IsWeaponSite, bool IsFireControlRadar, bool IsCommandCenter, bool RequiresFcuLink, bool HasFcuLink, float FcuRadiusKm, float EoIrBackupKm, bool IsHoldFire, bool IsInCommandNet, int CommandNetFcuCount, int CommandNetActiveFcuCount, int CommandNetEffectorCount)>? QueryMdsSiteUiStatus { get; set; }
    internal static Action<Unit, bool>? SetMdsSiteHoldFire { get; set; }
    internal static Func<Unit?, bool>? IsMdsSelectableUnit { get; set; }
    internal static Func<bool>? HasSelectedFriendlyMdsWeaponSite { get; set; }
    internal static Func<Unit?, float?>? GetMdsDefencePrice { get; set; }
    internal static Func<WeaponInfo?, bool>? IsMdsAntiShipWeaponInfo { get; set; }
    internal static Func<GroundVehicle?, bool>? IsMdsContainerHauler { get; set; }
    internal static Func<GroundVehicle?, UnityEngine.Vector3?>? TryGetMdsHaulerBedOffset { get; set; }
    internal static Action<GroundVehicle?, bool>? SetMdsHaulerOutriggersDeployed { get; set; }
    internal static Action? EnsureMdsCatalogRegisteredAction { get; set; }
    internal static Action<System.Collections.Generic.List<VehicleDefinition>>? AppendMissingMdsVehicleDefinitions { get; set; }
    internal static Func<System.Collections.Generic.List<VehicleDefinition>, bool>? HasMissingMdsVehicleDefinitions { get; set; }
    internal static Func<VehicleDefinition?, FactionHQ?, bool?>? TryCanPurchaseMdsVehicle { get; set; }
    internal static Func<VehicleDefinition?, bool>? IsMdsMobileVehicleDefinition { get; set; }
    internal static Func<VehicleDefinition?, int?>? GetMdsVehicleRequiredDepotLevel { get; set; }
    internal static Action? EnsureMdsBuildCatalogLookupAction { get; set; }
    internal static Func<System.Collections.Generic.IReadOnlyList<BuildingDefinition>>? GetAllMdsBuildCatalogDefinitions { get; set; }
    internal static Func<string>? BuildReservedCapsSyncString { get; set; }
    internal static Action<string>? ApplyClientReservedCapsAction { get; set; }

    internal static bool IsMdsPluginPresent()
    {
        try
        {
            return Chainloader.PluginInfos != null && Chainloader.PluginInfos.ContainsKey(MdsFrameworkPluginGuid);
        }
        catch
        {
            return false;
        }
    }

    internal static void TryAttachIfPresent(AddonRuntimeController? controller)
    {
        if (!AddonConfig.EnableMdsBridge.Value)
        {
            BridgeStatusSummary = "MDS Bridge: Disabled in config";
            return;
        }

        if (!IsMdsPluginPresent())
        {
            BridgeStatusSummary = "MDS Bridge: Standalone Mode (MDSFramework not installed)";
            AddonLog.Info("[MDS Bridge] MDSFramework not detected; running safely in standalone RTS mode.");
            return;
        }

#if HAS_MDS_FRAMEWORK
        try
        {
            AttachInternal(controller);
        }
        catch (Exception ex)
        {
            AddonLog.Warn($"[MDS Bridge] Failed to attach MDSFramework bridge cleanly: {ex}");
            BridgeStatusSummary = "MDS Bridge: Attach failed (see log)";
            ClearCallbacks();
        }
#else
        BridgeStatusSummary = "MDS Bridge: Present at runtime (build without MDS reference)";
        AddonLog.Warn("[MDS Bridge] MDSFramework plugin is loaded at runtime, but this DLL was compiled without HAS_MDS_FRAMEWORK.");
#endif
    }

    internal static void DetachOnDestroy()
    {
#if HAS_MDS_FRAMEWORK
        if (IsMdsAttached)
        {
            try
            {
                DetachInternal();
            }
            catch (Exception ex)
            {
                AddonLog.Debug($"[MDS Bridge] Exception while detaching on destroy: {ex.Message}");
            }
        }
#endif
        ClearCallbacks();
    }

    private static void ClearCallbacks()
    {
        IsMdsAttached = false;
        GetReservedCapOverride = null;
        CountHostileOrOwnedMdsRadarsForHq = null;
        TryFindPriorityMdsSeadTarget = null;
        HasEmittingHostileMdsRadar = null;
        TickAiMdsDeploymentAction = null;
        IsUnitBuildingMdsSite = null;
        CollectFriendlyMdsEwPositions = null;
        GetHostileMdsCompensation = null;
        CountKnownHostileMdsSites = null;
        UpdateMdsSitePosition = null;
        RaiseBallisticThreatAction = null;
        EnsureAirdropMountsInjectedAction = null;
        IsMdsAirdropMountAllowedForHq = null;
        GetMdsAirdropMountSuffix = null;
        GetMdsAirdropMountLabelOverride = null;
        GetMdsAirdropLoadoutBlockReason = null;
        GetMdsCargoUnitCost = null;
        IsMdsAirdropMount = null;
        GetMdsBuildCatalogDefinitionsForHq = null;
        IsMdsBuildDefinition = null;
        IsRawMdsEncyclopediaDefinition = null;
        GetMdsBuildCost = null;
        GetMdsBuildLabel = null;
        GetMdsBuildBlockReason = null;
        TryReplaceBuiltMdsPlaceholder = null;
        IsMdsHaulableContainer = null;
        SetMdsContainerTransitState = null;
        TryResolveMdsPreviewGlb = null;
        IsLogisticsWindowUnderScreenPoint = null;
        CompleteMdsEngineerOrdersForHq = null;
        QueryMdsSiteUiStatus = null;
        SetMdsSiteHoldFire = null;
        IsMdsSelectableUnit = null;
        HasSelectedFriendlyMdsWeaponSite = null;
        GetMdsDefencePrice = null;
        IsMdsAntiShipWeaponInfo = null;
        IsMdsContainerHauler = null;
        TryGetMdsHaulerBedOffset = null;
        SetMdsHaulerOutriggersDeployed = null;
        EnsureMdsCatalogRegisteredAction = null;
        AppendMissingMdsVehicleDefinitions = null;
        HasMissingMdsVehicleDefinitions = null;
        TryCanPurchaseMdsVehicle = null;
        IsMdsMobileVehicleDefinition = null;
        GetMdsVehicleRequiredDepotLevel = null;
        EnsureMdsBuildCatalogLookupAction = null;
        GetAllMdsBuildCatalogDefinitions = null;
        BuildReservedCapsSyncString = null;
        ApplyClientReservedCapsAction = null;
    }

#if HAS_MDS_FRAMEWORK
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AttachInternal(AddonRuntimeController? controller)
    {
        MdsBridgeAdapter.AttachAndRegister(controller);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DetachInternal()
    {
        MdsBridgeAdapter.Detach();
    }
#endif
}
