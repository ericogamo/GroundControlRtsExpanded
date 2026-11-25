#if HAS_MDS_FRAMEWORK
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using MDSFramework.Api;
using Mirage;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal static class MdsBridgeAdapter
{
    private static RtsEconomyBridgeImpl? economy;
    private static RtsCapBridgeImpl? caps;
    private static RtsLogisticsBridgeImpl? logistics;
    private static RtsAbmStockpileBridgeImpl? abmStockpile;
    private static RtsAiThreatSinkImpl? aiThreats;
    private static MdsUiAndTransportHelper? uiTransport;

    private static readonly MethodInfo? GameInteropResolveHqMethod =
        AccessTools.Method("MDSFramework.GameInteropHelpers:TryResolveFactionHQ", new[] { typeof(FactionAffinity) });
    private static Harmony? interopHarmony;

    [ThreadStatic]
    private static FactionHQ? activeDeliveryHq;

    internal static RtsAiThreatSinkImpl? CurrentAiThreats => aiThreats;

    internal readonly struct DeliveryHqScope : IDisposable
    {
        private readonly FactionHQ? previous;

        internal DeliveryHqScope(FactionHQ? hq)
        {
            previous = activeDeliveryHq;
            if (hq != null)
            {
                activeDeliveryHq = hq;
            }
        }

        public void Dispose()
        {
            activeDeliveryHq = previous;
        }
    }

    private static bool TryResolveFactionHqPrefix(ref FactionHQ? __result)
    {
        if (activeDeliveryHq != null)
        {
            __result = activeDeliveryHq;
            return false;
        }

        return true;
    }

    internal static void AttachAndRegister(AddonRuntimeController? controller)
    {
        AttachBridgeOnly();
        if (controller != null && logistics != null)
        {
            controller.Register(logistics);
        }

        AddonLog.Info($"[MDS Bridge] Attached to MDSFramework ({ContractorRegistry.GetPackages().Count} contractor pack(s) currently registered).");
    }

    internal static void AttachBridgeOnly()
    {
        economy ??= new RtsEconomyBridgeImpl();
        caps ??= new RtsCapBridgeImpl();
        logistics ??= new RtsLogisticsBridgeImpl(economy, caps);
        abmStockpile ??= new RtsAbmStockpileBridgeImpl();
        aiThreats ??= new RtsAiThreatSinkImpl(economy, caps, logistics);
        uiTransport ??= new MdsUiAndTransportHelper(economy, caps, logistics);
        caps.SetPendingBlueprintCapProvider(uiTransport.CountPendingBlueprintCapForHq);
        caps.SetDestroyedSiteHqConsumer(aiThreats.ConsumeDestroyedSiteHq);

        if (interopHarmony == null && GameInteropResolveHqMethod != null)
        {
            interopHarmony = new Harmony("com.groundcontrolrtsexpanded.mdsinterop");
            MethodInfo? prefix = AccessTools.Method(typeof(MdsBridgeAdapter), nameof(TryResolveFactionHqPrefix));
            if (prefix != null)
            {
                interopHarmony.Patch(GameInteropResolveHqMethod, prefix: new HarmonyMethod(prefix));
            }
        }

        RtsBridgeHost.Attach(economy, caps, logistics, abmStockpile, aiThreats);

        MdsBridgeBootstrap.IsMdsAttached = true;
        MdsBridgeBootstrap.GetReservedCapOverride = caps.GetReservedForHq;
        MdsBridgeBootstrap.CountHostileOrOwnedMdsRadarsForHq = aiThreats.CountOwnedSitesForHq;
        MdsBridgeBootstrap.HasEmittingHostileMdsRadar = aiThreats.HasEmittingHostileSite;
        MdsBridgeBootstrap.TryFindPriorityMdsSeadTarget = aiThreats.FindPrioritySeadTarget;
        MdsBridgeBootstrap.TickAiMdsDeploymentAction = aiThreats.TickAiDeployment;
        MdsBridgeBootstrap.IsUnitBuildingMdsSite = logistics.IsBuilderAssignedToAnyOrder;
        MdsBridgeBootstrap.CollectFriendlyMdsEwPositions = aiThreats.CollectActiveFriendlyMdsEwPositions;
        MdsBridgeBootstrap.RaiseBallisticThreatAction = abmStockpile.RaiseBallisticThreat;

        MdsBridgeBootstrap.EnsureAirdropMountsInjectedAction = uiTransport.EnsureAirdropMountsInjected;
        MdsBridgeBootstrap.IsMdsAirdropMountAllowedForHq = uiTransport.IsAirdropMountAllowedForHq;
        MdsBridgeBootstrap.GetMdsAirdropMountSuffix = uiTransport.GetAirdropMountSuffix;
        MdsBridgeBootstrap.GetMdsAirdropMountLabelOverride = uiTransport.GetAirdropMountLabelOverride;
        MdsBridgeBootstrap.GetMdsAirdropLoadoutBlockReason = uiTransport.GetAirdropLoadoutBlockReason;
        MdsBridgeBootstrap.GetMdsCargoUnitCost = uiTransport.GetCargoUnitCost;
        MdsBridgeBootstrap.IsMdsAirdropMount = uiTransport.IsAirdropMount;
        MdsBridgeBootstrap.GetMdsBuildCatalogDefinitionsForHq = uiTransport.GetBuildCatalogDefinitionsForHq;
        MdsBridgeBootstrap.IsMdsBuildDefinition = uiTransport.IsBuildDefinition;
        MdsBridgeBootstrap.IsRawMdsEncyclopediaDefinition = uiTransport.IsRawEncyclopediaDefinition;
        MdsBridgeBootstrap.GetMdsBuildCost = uiTransport.GetBuildCost;
        MdsBridgeBootstrap.GetMdsBuildLabel = uiTransport.GetBuildLabel;
        MdsBridgeBootstrap.GetMdsBuildBlockReason = uiTransport.GetBuildBlockReason;
        MdsBridgeBootstrap.TryReplaceBuiltMdsPlaceholder = uiTransport.TryReplaceBuiltPlaceholder;
        MdsBridgeBootstrap.IsMdsHaulableContainer = uiTransport.IsHaulableContainer;
        MdsBridgeBootstrap.SetMdsContainerTransitState = uiTransport.SetContainerTransitState;
        MdsBridgeBootstrap.TryResolveMdsPreviewGlb = uiTransport.TryResolvePreviewGlb;
        MdsBridgeBootstrap.IsLogisticsWindowUnderScreenPoint = logistics.ContainsScreenPoint;
        MdsBridgeBootstrap.CompleteMdsEngineerOrdersForHq = logistics.CompleteOrdersForHq;
        MdsBridgeBootstrap.QueryMdsSiteUiStatus = uiTransport.QueryMdsSiteUiStatus;
        MdsBridgeBootstrap.SetMdsSiteHoldFire = uiTransport.SetMdsSiteHoldFire;
        MdsBridgeBootstrap.IsMdsSelectableUnit = uiTransport.IsMdsSelectableUnit;
        MdsBridgeBootstrap.HasSelectedFriendlyMdsWeaponSite = uiTransport.HasSelectedFriendlyMdsWeaponSite;
        MdsBridgeBootstrap.GetMdsDefencePrice = uiTransport.GetMdsDefencePrice;
        MdsBridgeBootstrap.IsMdsAntiShipWeaponInfo = ContractorRegistry.IsAntiShipWeaponInfo;
        RtsBridgeHost.QueryUnitHoldFire = GroundControlReflection.IsUnitHoldingFire;

        UpdateStatusSummary();
        ContractorRegistry.OnPackageRegistered -= OnPackageRegistered;
        ContractorRegistry.OnPackageRegistered += OnPackageRegistered;
    }

    internal static void Detach()
    {
        ContractorRegistry.OnPackageRegistered -= OnPackageRegistered;
        RtsBridgeHost.QueryUnitHoldFire = null;
        if (interopHarmony != null)
        {
            interopHarmony.UnpatchSelf();
            interopHarmony = null;
        }

        activeDeliveryHq = null;
        if (MdsBridgeBootstrap.IsMdsAttached)
        {
            RtsBridgeHost.ResetToVanillaDefaults();
        }
    }

    private static void OnPackageRegistered(ContractorPackage package)
    {
        UpdateStatusSummary();
        AddonLog.Info($"[MDS Bridge] Contractor available: '{package.DisplayName}' (DefaultFaction={package.DefaultFaction}).");
    }

    private static void UpdateStatusSummary()
    {
        int count = ContractorRegistry.GetPackages().Count;
        MdsBridgeBootstrap.BridgeStatusSummary = $"MDS Bridge: Connected ({count} contractor pack{(count == 1 ? string.Empty : "s")})";
    }

    internal static FactionAffinity ToAffinity(FactionHQ? hq)
    {
        if (hq == null || hq.faction == null)
        {
            return FactionAffinity.Any;
        }

        string tag = $"{hq.faction.factionName} {hq.faction.factionTag}";
        if (tag.IndexOf("Boscali", StringComparison.OrdinalIgnoreCase) >= 0
            || tag.IndexOf("BDF", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return FactionAffinity.Boscali;
        }

        if (tag.IndexOf("Primeva", StringComparison.OrdinalIgnoreCase) >= 0
            || tag.IndexOf("PALA", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return FactionAffinity.Primeva;
        }

        return FactionAffinity.Any;
    }

    internal static FactionHQ? ResolveHq(FactionAffinity affinity)
    {
        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        if (affinity == FactionAffinity.Any)
        {
            return localHq;
        }

        if (localHq != null && ToAffinity(localHq) == affinity)
        {
            return localHq;
        }

        foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
        {
            if (hq != null && ToAffinity(hq) == affinity)
            {
                return hq;
            }
        }

        return null;
    }

    internal sealed class RtsEconomyBridgeImpl : IRtsEconomyBridge
    {
        private const string VanguardPackageId = "vanguard.skyshield";

        public int QueryFactionTechTier(FactionAffinity faction)
            => QueryFactionTechTier(activeDeliveryHq ?? ResolveHq(faction));

        internal int QueryFactionTechTier(FactionHQ? hq)
        {
            if (!DynamicCapAndTechService.IsActive)
            {
                return 3;
            }

            return hq != null ? DynamicCapAndTechService.Instance.GetSnapshot(hq).TechTier : 1;
        }

        public bool TrySpendFactionCredits(FactionAffinity faction, float amount, string reason)
            => TrySpendFactionCredits(activeDeliveryHq ?? ResolveHq(faction), amount, reason);

        internal bool TrySpendFactionCredits(FactionHQ? hq, float amount, string reason)
        {
            if (hq == null || !((NetworkBehaviour)hq).IsServer)
            {
                return false;
            }

            if (amount < 0f)
            {
                hq.AddFunds(-amount);
                return true;
            }

            if (amount == 0f)
            {
                return true;
            }

            return GroundControlReflection.TrySpend(hq, amount);
        }

        public bool IsPackageAllowedForFaction(ContractorPackage package, FactionAffinity faction)
        {
            if (package == null)
            {
                return false;
            }

            if (SingleplayerCheatService.IsIgnoreFactionLockActive)
            {
                return true;
            }

            if (!AddonConfig.EnforceContractorFactionLock.Value)
            {
                return true;
            }

            FactionAffinity effective = faction != FactionAffinity.Any
                ? faction
                : ToAffinity(GroundControlReflection.GetLocalHq());

            // Strict Westmacht (Boscali) lock for Vanguard Defence regardless of package config.
            if (string.Equals(package.PackageId, VanguardPackageId, StringComparison.OrdinalIgnoreCase)
                || package.DefaultFaction == FactionAffinity.Boscali)
            {
                return effective == FactionAffinity.Boscali;
            }

            if (package.DefaultFaction == FactionAffinity.Primeva)
            {
                return effective == FactionAffinity.Primeva;
            }

            return true;
        }
    }

    internal sealed class RtsCapBridgeImpl : IRtsCapBridge
    {
        private sealed class ReservedCaps
        {
            internal int Ground;
            internal int Air;
            internal int Naval;
        }

        private readonly Dictionary<FactionHQ, ReservedCaps> reservedByHq = new();
        private Func<FactionHQ, (int Ground, int Air, int Naval)>? pendingBlueprintCapProvider;
        private Func<FactionAffinity, FactionHQ?>? destroyedSiteHqConsumer;
        private float lastCapBlockNoticeAt = -10f;

        internal void SetPendingBlueprintCapProvider(Func<FactionHQ, (int Ground, int Air, int Naval)> provider)
        {
            pendingBlueprintCapProvider = provider;
        }

        internal void SetDestroyedSiteHqConsumer(Func<FactionAffinity, FactionHQ?> consumer)
        {
            destroyedSiteHqConsumer = consumer;
        }

        internal void ResetSession()
        {
            reservedByHq.Clear();
            lastCapBlockNoticeAt = -10f;
        }

        internal (int Ground, int Air, int Naval) GetReservedForHq(FactionHQ hq)
        {
            return reservedByHq.TryGetValue(hq, out ReservedCaps? r)
                ? (r.Ground, r.Air, r.Naval)
                : (0, 0, 0);
        }

        public bool CanConsumeCap(FactionAffinity faction, string capCategory, int weight, out string blockReason)
            => CanConsumeCap(activeDeliveryHq ?? ResolveHq(faction), capCategory, weight, out blockReason);

        internal bool CanConsumeCap(FactionHQ? hq, string capCategory, int weight, out string blockReason)
        {
            if (weight <= 0)
            {
                blockReason = string.Empty;
                return true;
            }

            if (hq == null)
            {
                blockReason = "No matching Faction HQ.";
                return false;
            }

            if (SingleplayerCheatService.IsIgnoreCapsActiveFor(hq))
            {
                blockReason = string.Empty;
                return true;
            }

            (int bpG, int bpA, int bpN) = pendingBlueprintCapProvider != null
                ? pendingBlueprintCapProvider(hq)
                : (0, 0, 0);
            int bpTotal = bpG + bpA + bpN;

            if (DynamicCapAndTechService.IsActive)
            {
                FactionCapSnapshot snap = DynamicCapAndTechService.Instance.GetSnapshot(hq);
                int totalUsed = snap.TotalUsed + bpTotal;
                if (totalUsed + weight > snap.TotalCap)
                {
                    blockReason = $"UNIT CAP FULL: {totalUsed}/{snap.TotalCap} (City · Shift+C)";
                    return false;
                }

                if (string.Equals(capCategory, "Air", StringComparison.OrdinalIgnoreCase))
                {
                    int airUsed = snap.AirUsed + bpA;
                    if (airUsed + weight > snap.AirCap)
                    {
                        blockReason = $"AIR CAP FULL: {airUsed}/{snap.AirCap} (Build Pilot Academy · Shift+C)";
                        return false;
                    }
                }
                else if (string.Equals(capCategory, "Naval", StringComparison.OrdinalIgnoreCase))
                {
                    int navalUsed = snap.NavalUsed + bpN;
                    if (navalUsed + weight > snap.NavalCap)
                    {
                        blockReason = $"NAVAL CAP FULL: {navalUsed}/{snap.NavalCap} (Build Naval Command · Shift+C)";
                        return false;
                    }
                }
                else
                {
                    int groundUsed = snap.GroundUsed + bpG;
                    if (groundUsed + weight > snap.GroundCap)
                    {
                        blockReason = $"GROUND CAP FULL: {groundUsed}/{snap.GroundCap} (Build Barracks / Logistics · Shift+C)";
                        return false;
                    }
                }

                blockReason = string.Empty;
                return true;
            }

            if (GroundControlReflection.IsVanillaUnitCapEnabled())
            {
                (int totalCap, int groundCap, int airCap, int shipCap) = GroundControlReflection.GetVanillaCaps();
                (int resG, int resA, int resN) = GetReservedForHq(hq);
                int groundCount = resG + bpG;
                int airCount = resA + bpA;
                int navalCount = resN + bpN;

                if (hq.factionUnits != null)
                {
                    foreach (PersistentID pid in hq.factionUnits)
                    {
                        if (!pid.TryGetUnit(out Unit unit) || unit == null || unit.disabled)
                        {
                            continue;
                        }

                        if (unit is GroundVehicle)
                        {
                            groundCount++;
                        }
                        else if (unit is Aircraft)
                        {
                            airCount++;
                        }
                        else if (unit is Ship)
                        {
                            navalCount++;
                        }
                    }
                }

                int totalCount = groundCount + airCount + navalCount;
                if (totalCap > 0 && totalCount + weight > totalCap)
                {
                    blockReason = $"UNIT CAP FULL: {totalCount}/{totalCap}";
                    return false;
                }

                if (string.Equals(capCategory, "Air", StringComparison.OrdinalIgnoreCase))
                {
                    if (airCap > 0 && airCount + weight > airCap)
                    {
                        blockReason = $"AIR CAP FULL: {airCount}/{airCap}";
                        return false;
                    }
                }
                else if (string.Equals(capCategory, "Naval", StringComparison.OrdinalIgnoreCase))
                {
                    if (shipCap > 0 && navalCount + weight > shipCap)
                    {
                        blockReason = $"NAVAL CAP FULL: {navalCount}/{shipCap}";
                        return false;
                    }
                }
                else if (groundCap > 0 && groundCount + weight > groundCap)
                {
                    blockReason = $"GROUND CAP FULL: {groundCount}/{groundCap}";
                    return false;
                }
            }

            blockReason = string.Empty;
            return true;
        }

        public bool TryConsumeCap(FactionAffinity faction, string capCategory, int weight)
            => TryConsumeCap(activeDeliveryHq ?? ResolveHq(faction), capCategory, weight);

        internal bool TryConsumeCap(FactionHQ? hq, string capCategory, int weight)
        {
            if (!CanConsumeCap(hq, capCategory, weight, out string blockReason))
            {
                if (hq != null && ReferenceEquals(hq, GroundControlReflection.GetLocalHq()))
                {
                    float now = Time.unscaledTime;
                    if (now - lastCapBlockNoticeAt >= 1.25f)
                    {
                        lastCapBlockNoticeAt = now;
                        GroundControlReflection.NoticeFail($"BLOCKED: {blockReason}");
                    }
                }

                return false;
            }

            if (weight <= 0)
            {
                return true;
            }

            if (hq == null)
            {
                return false;
            }

            if (!reservedByHq.TryGetValue(hq, out ReservedCaps? r))
            {
                r = new ReservedCaps();
                reservedByHq[hq] = r;
            }

            if (string.Equals(capCategory, "Air", StringComparison.OrdinalIgnoreCase))
            {
                r.Air += weight;
            }
            else if (string.Equals(capCategory, "Naval", StringComparison.OrdinalIgnoreCase))
            {
                r.Naval += weight;
            }
            else
            {
                r.Ground += weight;
            }

            DynamicCapAndTechService.Instance.InvalidateCache(hq);
            return true;
        }

        public void ReleaseCap(FactionAffinity faction, string capCategory, int weight)
        {
            if (weight <= 0)
            {
                return;
            }

            FactionHQ? hq = destroyedSiteHqConsumer?.Invoke(faction) ?? activeDeliveryHq ?? ResolveHq(faction);
            ReleaseCap(hq, capCategory, weight);
        }

        internal void ReleaseCap(FactionHQ? hq, string capCategory, int weight)
        {
            if (weight <= 0 || hq == null || !reservedByHq.TryGetValue(hq, out ReservedCaps? r))
            {
                return;
            }

            if (string.Equals(capCategory, "Air", StringComparison.OrdinalIgnoreCase))
            {
                r.Air = Math.Max(0, r.Air - weight);
            }
            else if (string.Equals(capCategory, "Naval", StringComparison.OrdinalIgnoreCase))
            {
                r.Naval = Math.Max(0, r.Naval - weight);
            }
            else
            {
                r.Ground = Math.Max(0, r.Ground - weight);
            }

            DynamicCapAndTechService.Instance.InvalidateCache(hq);
        }
    }

    internal sealed class RtsLogisticsBridgeImpl : IRtsLogisticsBridge, IAddonTick, IAddonGui, IAddonResetSession
    {
        private sealed class EngineerBuildOrder
        {
            internal int Id;
            internal FactionHQ Hq = null!;
            internal FactionAffinity Affinity;
            internal ModularUnitSpec Spec = null!;
            internal GlobalPosition SitePos;
            internal float YawDeg;
            internal float Progress;
            internal float CreatedAt;
            internal float NextDispatchAt;
            internal readonly List<Unit> AssignedBuilders = new();
            internal GameObject? MarkerRoot;
        }

        private sealed class TarantulaSlingOrder
        {
            internal int Id;
            internal FactionHQ Hq = null!;
            internal FactionAffinity Affinity;
            internal ModularUnitSpec Spec = null!;
            internal Aircraft? Helo;
            internal GlobalPosition DropPos;
            internal float YawDeg;
            internal float CreatedAt;
            internal float LoweringProgress;
            internal bool Lowering;
            internal float NextOrderAt;
            internal GameObject? KitVisual;
            internal LineRenderer? CableLine;
            internal Material? CableMat;
        }

        private const int LogisticsWindowId = 48135;
        private const float EngineerWorkRadiusMeters = 62f;
        private const float EngineerSearchRadiusMeters = 1600f;
        private const float AiBuildTimeoutSeconds = 420f;
        private const float TarantulaArrivalRadiusMeters = 165f;
        private const float TarantulaLoweringSeconds = 3.0f;

        private readonly RtsEconomyBridgeImpl ecoBridge;
        private readonly RtsCapBridgeImpl capBridge;
        private readonly List<EngineerBuildOrder> engineerOrders = new();
        private readonly List<TarantulaSlingOrder> slingOrders = new();
        private readonly List<Unit> builderScratch = new();

        private int nextOrderId = 1;
        private float nextTickAt;
        private Rect statusWindowRect = new(0f, 0f, 360f, 150f);
        private bool statusWindowVisible;
        private bool statusPositioned;

        public event Action<string, FactionAffinity, Vector3, float>? OnPhysicalKitDelivered;

        internal RtsLogisticsBridgeImpl(RtsEconomyBridgeImpl ecoBridge, RtsCapBridgeImpl capBridge)
        {
            this.ecoBridge = ecoBridge;
            this.capBridge = capBridge;
        }

        internal bool ContainsScreenPoint(Vector2 screenPoint)
        {
            if (!statusWindowVisible || !GroundControlReflection.IsCommanderModeActive())
            {
                return false;
            }

            Vector2 guiPt = NativeThemeBridge.ScreenToGui(screenPoint);
            return NativeThemeBridge.VisualRect(LogisticsWindowId, statusWindowRect).Contains(guiPt);
        }

        internal void CompleteOrdersForHq(FactionHQ? hq)
        {
            if (hq == null || !((NetworkBehaviour)hq).IsServer)
            {
                return;
            }

            for (int i = engineerOrders.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(engineerOrders[i].Hq, hq))
                {
                    EngineerBuildOrder order = engineerOrders[i];
                    engineerOrders.RemoveAt(i);
                    CompleteEngineerOrder(order);
                }
            }
        }

        internal int CountPendingForHq(FactionHQ hq)
        {
            int count = 0;
            for (int i = 0; i < engineerOrders.Count; i++)
            {
                if (ReferenceEquals(engineerOrders[i].Hq, hq))
                {
                    count++;
                }
            }

            for (int i = 0; i < slingOrders.Count; i++)
            {
                if (ReferenceEquals(slingOrders[i].Hq, hq))
                {
                    count++;
                }
            }

            return count;
        }

        internal int CountPendingForHqRole(FactionHQ hq, WeaponCascadeRole role)
        {
            int count = 0;
            for (int i = 0; i < engineerOrders.Count; i++)
            {
                if (ReferenceEquals(engineerOrders[i].Hq, hq) && engineerOrders[i].Spec.Role == role)
                {
                    count++;
                }
            }

            for (int i = 0; i < slingOrders.Count; i++)
            {
                if (ReferenceEquals(slingOrders[i].Hq, hq) && slingOrders[i].Spec.Role == role)
                {
                    count++;
                }
            }

            return count;
        }

        internal bool TryGetPendingRolePosition(FactionHQ hq, WeaponCascadeRole role, out GlobalPosition pos)
        {
            for (int i = 0; i < engineerOrders.Count; i++)
            {
                if (ReferenceEquals(engineerOrders[i].Hq, hq) && engineerOrders[i].Spec.Role == role)
                {
                    pos = engineerOrders[i].SitePos;
                    return true;
                }
            }

            for (int i = 0; i < slingOrders.Count; i++)
            {
                if (ReferenceEquals(slingOrders[i].Hq, hq) && slingOrders[i].Spec.Role == role)
                {
                    pos = slingOrders[i].DropPos;
                    return true;
                }
            }

            pos = default;
            return false;
        }

        internal bool IsPointNearPendingSite(FactionHQ hq, GlobalPosition pos, float minSpacingMeters)
        {
            float minSq = minSpacingMeters * minSpacingMeters;
            for (int i = 0; i < engineerOrders.Count; i++)
            {
                if (ReferenceEquals(engineerOrders[i].Hq, hq)
                    && FastMath.SquareDistance(engineerOrders[i].SitePos, pos) < minSq)
                {
                    return true;
                }
            }

            for (int i = 0; i < slingOrders.Count; i++)
            {
                if (ReferenceEquals(slingOrders[i].Hq, hq)
                    && FastMath.SquareDistance(slingOrders[i].DropPos, pos) < minSq)
                {
                    return true;
                }
            }

            return false;
        }

        public bool RequestEngineerBuildSite(FactionAffinity faction, ModularUnitSpec spec, Vector3 worldPos, float yawDeg)
            => RequestEngineerBuildSite(ResolveHq(faction), faction, spec, worldPos, yawDeg);

        internal bool RequestEngineerBuildSite(FactionHQ? hq, FactionAffinity faction, ModularUnitSpec spec, Vector3 worldPos, float yawDeg)
        {
            if (hq == null || !((NetworkBehaviour)hq).IsServer || spec == null)
            {
                return false;
            }

            FactionAffinity resolvedAffinity = faction != FactionAffinity.Any ? faction : ToAffinity(hq);
            GlobalPosition siteGlobal = GroundControlReflection.SnapToTerrain(worldPos.ToGlobalPosition());
            if (!AddonConfig.RequirePhysicalConstruction.Value)
            {
                using (new DeliveryHqScope(hq))
                {
                    OnPhysicalKitDelivered?.Invoke(spec.UnitId, resolvedAffinity, siteGlobal.ToLocalPosition(), yawDeg);
                }

                return true;
            }

            GameObject marker = ModelRegistry.InstantiateModel("sam_kit", Datum.origin, addBoxCollider: false);
            marker.name = $"GCE_MdsSiteBlueprint_{nextOrderId}";
            marker.transform.localPosition = siteGlobal.AsVector3();
            marker.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);

            var order = new EngineerBuildOrder
            {
                Id = nextOrderId++,
                Hq = hq,
                Affinity = resolvedAffinity,
                Spec = spec,
                SitePos = siteGlobal,
                YawDeg = yawDeg,
                Progress = 0f,
                CreatedAt = Time.time,
                NextDispatchAt = 0f,
                MarkerRoot = marker,
            };

            engineerOrders.Add(order);
            DispatchNearestEngineers(order);

            if (ReferenceEquals(hq, GroundControlReflection.GetLocalHq()) && order.AssignedBuilders.Count == 0)
            {
                GroundControlReflection.NoticeFail($"{spec.DisplayName} blueprint queued — move an M12 Jackknife near the marker!");
            }

            AddonLog.Info($"[MDS Bridge] M12 build site #{order.Id} placed for '{spec.DisplayName}' (builders assigned={order.AssignedBuilders.Count}).");
            return true;
        }

        public bool RequestTarantulaSlingKit(FactionAffinity faction, ModularUnitSpec spec, Vector3 dropPos, float yawDeg)
        {
            FactionHQ? hq = ResolveHq(faction);
            if (hq == null || !((NetworkBehaviour)hq).IsServer || spec == null)
            {
                return false;
            }

            if (!spec.Logistics.SupportsTarantulaSlingKit)
            {
                GroundControlReflection.NoticeFail($"{spec.DisplayName} is not sling-load rated.");
                return false;
            }

            if (spec.Logistics.SlingKitMassKg > AddonConfig.TarantulaMaxSlingMassKg.Value)
            {
                GroundControlReflection.NoticeFail(
                    $"{spec.DisplayName} ({spec.Logistics.SlingKitMassKg:0} kg) exceeds Tarantula limit ({AddonConfig.TarantulaMaxSlingMassKg.Value:0} kg).");
                return false;
            }

            GlobalPosition dropGlobal = GroundControlReflection.SnapToTerrain(dropPos.ToGlobalPosition());
            if (!AddonConfig.RequirePhysicalConstruction.Value)
            {
                using (new DeliveryHqScope(hq))
                {
                    OnPhysicalKitDelivered?.Invoke(spec.UnitId, faction, dropGlobal.ToLocalPosition(), yawDeg);
                }

                return true;
            }

            Aircraft? helo = FindOrLaunchAvailableTarantula(hq, dropGlobal);
            if (helo == null)
            {
                if (ReferenceEquals(hq, GroundControlReflection.GetLocalHq()))
                {
                    GroundControlReflection.NoticeFail("No VL-49 Tarantula available (check Hangar / Air Cap) — use M12 Engineer Build.");
                }

                return false;
            }

            GameObject kitVisual = ModelRegistry.InstantiateModel("sam_kit", parent: null, addBoxCollider: false);
            kitVisual.name = $"GCE_TarantulaKit_{nextOrderId}";

            var lineGo = new GameObject($"GCE_TarantulaCable_{nextOrderId}");
            LineRenderer cable = lineGo.AddComponent<LineRenderer>();
            cable.useWorldSpace = true;
            cable.startWidth = 0.08f;
            cable.endWidth = 0.08f;
            cable.positionCount = 2;
            Shader? cableShader = Shader.Find("Sprites/Default") ?? UrpMaterialFactory.ResolveLitShader();
            Material? cableMat = cableShader != null ? new Material(cableShader) { color = new Color(0.85f, 0.85f, 0.85f, 1f) } : null;
            if (cableMat != null)
            {
                cable.sharedMaterial = cableMat;
            }

            var sling = new TarantulaSlingOrder
            {
                Id = nextOrderId++,
                Hq = hq,
                Affinity = faction != FactionAffinity.Any ? faction : ToAffinity(hq),
                Spec = spec,
                Helo = helo,
                DropPos = dropGlobal,
                YawDeg = yawDeg,
                CreatedAt = Time.time,
                NextOrderAt = Time.unscaledTime + 2.0f,
                KitVisual = kitVisual,
                CableLine = cable,
                CableMat = cableMat,
            };

            GroundControlReflection.SetAircraftMoveOrder(helo, dropGlobal);
            UpdateSlingVisualTransform(sling, lowerRatio: 0f);
            slingOrders.Add(sling);

            AddonLog.Info($"[MDS Bridge] Dispatched VL-49 Tarantula sling order #{sling.Id} for '{spec.DisplayName}' ({spec.Logistics.SlingKitMassKg:0} kg).");
            return true;
        }

        public void Tick()
        {
            if (engineerOrders.Count == 0 && slingOrders.Count == 0)
            {
                return;
            }

            UpdateAllSlingVisualsFrame();

            float now = Time.unscaledTime;
            if (now < nextTickAt)
            {
                return;
            }

            float dt = nextTickAt > 0f ? Mathf.Clamp(now - nextTickAt + 0.35f, 0.1f, 1.5f) : 0.35f;
            nextTickAt = now + 0.35f;

            TickEngineerOrders(now, dt);
            TickSlingOrders(now, dt);
        }

        public void DrawGui()
        {
            statusWindowVisible = false;
            if (!AddonConfig.ModEnabled.Value || !GroundControlReflection.IsCommanderModeActive())
            {
                return;
            }

            FactionHQ? localHq = GroundControlReflection.GetLocalHq();
            if (localHq == null)
            {
                return;
            }

            int playerOrders = CountPendingForHq(localHq);
            if (playerOrders <= 0)
            {
                return;
            }

            statusWindowVisible = true;
            float h = Mathf.Clamp(44f + playerOrders * 52f, 96f, 260f);
            if (!statusPositioned)
            {
                statusWindowRect = new Rect(14f, Mathf.Max(82f, NativeThemeBridge.UiHeight - h - 252f), 360f, h);
                statusPositioned = true;
            }
            else
            {
                statusWindowRect.height = h;
            }

            statusWindowRect = NativeThemeBridge.DrawWindow(
                LogisticsWindowId,
                statusWindowRect,
                _ => DrawLogisticsWindow(localHq),
                "MDS KIT LOGISTICS");
        }

        public void ResetSession()
        {
            statusWindowVisible = false;
            statusPositioned = false;
            for (int i = 0; i < engineerOrders.Count; i++)
            {
                DestroyObj(engineerOrders[i].MarkerRoot);
            }

            for (int i = 0; i < slingOrders.Count; i++)
            {
                DestroySlingVisuals(slingOrders[i]);
            }

            engineerOrders.Clear();
            slingOrders.Clear();
            builderScratch.Clear();
            capBridge.ResetSession();
            abmStockpile?.ResetSession();
            aiThreats?.ResetSession();
            uiTransport?.ResetSession();
            nextOrderId = 1;
            nextTickAt = 0f;
        }

        internal bool IsBuilderAssignedToAnyOrder(Unit? unit)
        {
            if (unit == null)
            {
                return false;
            }

            for (int i = 0; i < engineerOrders.Count; i++)
            {
                List<Unit> assigned = engineerOrders[i].AssignedBuilders;
                for (int j = 0; j < assigned.Count; j++)
                {
                    if (ReferenceEquals(assigned[j], unit))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal FactionHQ? FindPendingOrderHqNear(Vector3 worldPos, FactionAffinity faction)
        {
            GlobalPosition pos = worldPos.ToGlobalPosition();
            float bestSq = 60f * 60f;
            FactionHQ? bestHq = null;

            for (int i = 0; i < engineerOrders.Count; i++)
            {
                EngineerBuildOrder order = engineerOrders[i];
                if (order.Hq == null)
                {
                    continue;
                }

                if (faction != FactionAffinity.Any && order.Affinity != faction && ToAffinity(order.Hq) != faction)
                {
                    continue;
                }

                float dSq = FastMath.SquareDistance(order.SitePos, pos);
                if (dSq < bestSq)
                {
                    bestSq = dSq;
                    bestHq = order.Hq;
                }
            }

            for (int i = 0; i < slingOrders.Count; i++)
            {
                TarantulaSlingOrder sling = slingOrders[i];
                if (sling.Hq == null)
                {
                    continue;
                }

                if (faction != FactionAffinity.Any && sling.Affinity != faction && ToAffinity(sling.Hq) != faction)
                {
                    continue;
                }

                float dSq = FastMath.SquareDistance(sling.DropPos, pos);
                if (dSq < bestSq)
                {
                    bestSq = dSq;
                    bestHq = sling.Hq;
                }
            }

            return bestHq;
        }

        internal void DeliverBuiltSiteImmediately(FactionHQ hq, string unitId, FactionAffinity affinity, Vector3 worldPos, float yawDeg)
        {
            using (new DeliveryHqScope(hq))
            {
                OnPhysicalKitDelivered?.Invoke(unitId, affinity, worldPos, yawDeg);
            }
        }

        private void TickEngineerOrders(float now, float dt)
        {
            FactionHQ? localHq = GroundControlReflection.GetLocalHq();

            for (int i = engineerOrders.Count - 1; i >= 0; i--)
            {
                EngineerBuildOrder order = engineerOrders[i];
                if (order.Hq == null)
                {
                    DestroyObj(order.MarkerRoot);
                    engineerOrders.RemoveAt(i);
                    continue;
                }

                if (order.MarkerRoot != null)
                {
                    RoadMeshBuilder.AttachToDatumOrigin(order.MarkerRoot);
                }

                if (now >= order.NextDispatchAt)
                {
                    order.NextDispatchAt = now + 4.0f;
                    DispatchNearestEngineers(order);
                }

                int working = CountWorkingEngineersNear(order.Hq, order.SitePos);
                if (working > 0)
                {
                    float buildSeconds = Mathf.Max(8f, order.Spec.Logistics.EngineerBuildSeconds);
                    order.Progress = Mathf.Min(1f, order.Progress + (working * dt / buildSeconds));
                }

                if (SingleplayerCheatService.IsInstantBuildActive && localHq != null && ReferenceEquals(order.Hq, localHq))
                {
                    order.Progress = 1f;
                }

                if (order.Progress >= 1f)
                {
                    engineerOrders.RemoveAt(i);
                    CompleteEngineerOrder(order);
                    continue;
                }

                bool isAi = localHq == null || !ReferenceEquals(order.Hq, localHq);
                if (isAi && Time.time - order.CreatedAt > AiBuildTimeoutSeconds)
                {
                    engineerOrders.RemoveAt(i);
                    CancelAndRefundEngineerOrder(order);
                }
            }
        }

        private void CompleteEngineerOrder(EngineerBuildOrder order)
        {
            DestroyObj(order.MarkerRoot);
            for (int i = 0; i < order.AssignedBuilders.Count; i++)
            {
                Unit b = order.AssignedBuilders[i];
                if (b != null && !b.disabled)
                {
                    GroundControlReflection.SendRepairUnitHome(b);
                }
            }

            Vector3 worldPos = order.SitePos.ToLocalPosition();
            using (new DeliveryHqScope(order.Hq))
            {
                OnPhysicalKitDelivered?.Invoke(order.Spec.UnitId, order.Affinity, worldPos, order.YawDeg);
            }

            AddonLog.Info($"[MDS Bridge] M12 Jackknife finished building '{order.Spec.DisplayName}' at {order.SitePos}.");
        }

        private void CancelAndRefundEngineerOrder(EngineerBuildOrder order)
        {
            DestroyObj(order.MarkerRoot);
            for (int i = 0; i < order.AssignedBuilders.Count; i++)
            {
                Unit b = order.AssignedBuilders[i];
                if (b != null && !b.disabled)
                {
                    GroundControlReflection.SendRepairUnitHome(b);
                }
            }

            capBridge.ReleaseCap(order.Hq, order.Spec.Logistics.CapCategory, order.Spec.Logistics.CapWeight);
            ecoBridge.TrySpendFactionCredits(order.Hq, -order.Spec.Logistics.CreditCost, $"Refund {order.Spec.DisplayName}");
        }

        private void TickSlingOrders(float now, float dt)
        {
            for (int i = slingOrders.Count - 1; i >= 0; i--)
            {
                TarantulaSlingOrder sling = slingOrders[i];
                if (sling.Hq == null || sling.Helo == null || sling.Helo.disabled)
                {
                    // Shot down / lost in transit -> kit is destroyed (no credit refund), cap released.
                    slingOrders.RemoveAt(i);
                    DestroySlingVisuals(sling);
                    capBridge.ReleaseCap(sling.Hq, sling.Spec.Logistics.CapCategory, sling.Spec.Logistics.CapWeight);

                    string lostMsg = $"VL-49 TARANTULA LOST — {sling.Spec.DisplayName} sling kit destroyed!";
                    AddonLog.Warn($"[MDS Bridge] {lostMsg}");
                    if (ReferenceEquals(sling.Hq, GroundControlReflection.GetLocalHq()))
                    {
                        GroundControlReflection.NoticeFail(lostMsg);
                    }

                    continue;
                }

                GlobalPosition heloPos = sling.Helo.GlobalPosition();
                float horizDist = Vector2.Distance(
                    new Vector2(heloPos.x, heloPos.z),
                    new Vector2(sling.DropPos.x, sling.DropPos.z));

                if (!sling.Lowering)
                {
                    if (horizDist <= TarantulaArrivalRadiusMeters)
                    {
                        sling.Lowering = true;
                        sling.LoweringProgress = 0f;
                    }
                    else if (now >= sling.NextOrderAt)
                    {
                        sling.NextOrderAt = now + 4.0f;
                        GroundControlReflection.SetAircraftMoveOrder(sling.Helo, sling.DropPos);
                    }
                }
                else
                {
                    sling.LoweringProgress += dt / TarantulaLoweringSeconds;
                    if (sling.LoweringProgress >= 1f)
                    {
                        slingOrders.RemoveAt(i);
                        DestroySlingVisuals(sling);

                        Vector3 worldPos = sling.DropPos.ToLocalPosition();
                        using (new DeliveryHqScope(sling.Hq))
                        {
                            OnPhysicalKitDelivered?.Invoke(sling.Spec.UnitId, sling.Affinity, worldPos, sling.YawDeg);
                        }

                        AddonLog.Info($"[MDS Bridge] Tarantula delivered '{sling.Spec.DisplayName}' at {sling.DropPos}.");

                        if (ReferenceEquals(sling.Hq, GroundControlReflection.GetLocalHq()))
                        {
                            GroundControlReflection.NoticeFail($"{sling.Spec.DisplayName} delivered & deploying!");
                        }
                    }
                }
            }
        }

        private void UpdateAllSlingVisualsFrame()
        {
            for (int i = 0; i < slingOrders.Count; i++)
            {
                TarantulaSlingOrder sling = slingOrders[i];
                if (sling.Helo != null && !sling.Helo.disabled)
                {
                    UpdateSlingVisualTransform(sling, sling.Lowering ? Mathf.Clamp01(sling.LoweringProgress) : 0f);
                }
            }
        }

        private static void UpdateSlingVisualTransform(TarantulaSlingOrder sling, float lowerRatio)
        {
            if (sling.Helo == null || sling.KitVisual == null)
            {
                return;
            }

            Vector3 heloAttachWorld = sling.Helo.transform.position + Vector3.down * 1.4f;
            Vector3 suspendedWorld = heloAttachWorld + Vector3.down * 8.5f;
            Vector3 targetWorld = sling.DropPos.ToLocalPosition();
            Vector3 kitPos = lowerRatio > 0f ? Vector3.Lerp(suspendedWorld, targetWorld, lowerRatio) : suspendedWorld;

            sling.KitVisual.transform.position = kitPos;
            sling.KitVisual.transform.rotation = lowerRatio > 0f
                ? Quaternion.Euler(0f, sling.YawDeg, 0f)
                : Quaternion.Euler(0f, sling.Helo.transform.eulerAngles.y, 0f);

            if (sling.CableLine != null)
            {
                sling.CableLine.SetPosition(0, heloAttachWorld);
                sling.CableLine.SetPosition(1, kitPos + Vector3.up * 2.2f);
            }
        }

        private bool IsHeloInActiveSlingOrder(Aircraft helo)
        {
            for (int i = 0; i < slingOrders.Count; i++)
            {
                if (ReferenceEquals(slingOrders[i].Helo, helo))
                {
                    return true;
                }
            }

            return false;
        }

        private Aircraft? FindOrLaunchAvailableTarantula(FactionHQ hq, GlobalPosition dropGlobal)
        {
            Aircraft? bestExisting = null;
            float bestDistSq = float.MaxValue;

            for (int i = 0; i < UnitRegistry.allUnits.Count; i++)
            {
                Unit? u = UnitRegistry.allUnits[i];
                if (u is not Aircraft ac
                    || ac.disabled
                    || !ReferenceEquals(ac.NetworkHQ, hq)
                    || ac.Player != null
                    || ac.pilots == null
                    || ac.pilots.Length == 0
                    || IsHeloInActiveSlingOrder(ac)
                    || GroundControlReflection.IsSlingLifting(ac))
                {
                    continue;
                }

                bool hasSling = GroundControlReflection.CanSlingLift(ac) || HasSlingWeaponStation(ac);
                if (!hasSling)
                {
                    continue;
                }

                float dSq = FastMath.SquareDistance(ac.GlobalPosition(), dropGlobal);
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    bestExisting = ac;
                }
            }

            if (bestExisting != null)
            {
                return bestExisting;
            }

            // Try spawning an available Tarantula / sling helicopter from the nearest compatible airbase.
            AircraftDefinition? heloDef = FindTarantulaDefinition();
            if (heloDef == null)
            {
                return null;
            }

            Loadout? slingLoadout = BuildSlingLoadout(heloDef);
            if (slingLoadout == null)
            {
                slingLoadout = new Loadout();
            }

            Airbase? bestBase = null;
            float bestBaseDistSq = float.MaxValue;
            foreach (Airbase ab in hq.GetAirbases())
            {
                if (ab == null || ab.disabled || !ab.CanSpawnAircraft(heloDef))
                {
                    continue;
                }

                Transform tf = ab.center != null ? ab.center : ab.transform;
                float dSq = FastMath.SquareDistance(tf.GlobalPosition(), dropGlobal);
                if (dSq < bestBaseDistSq)
                {
                    bestBaseDistSq = dSq;
                    bestBase = ab;
                }
            }

            if (bestBase == null)
            {
                return null;
            }

            if (!GroundControlReflection.TryLaunchAiAircraft(hq, bestBase, heloDef, new LiveryKey(0), slingLoadout, 1f, hangar: null))
            {
                return null;
            }

            for (int i = UnitRegistry.allUnits.Count - 1; i >= 0; i--)
            {
                if (UnitRegistry.allUnits[i] is Aircraft launched
                    && !launched.disabled
                    && ReferenceEquals(launched.NetworkHQ, hq)
                    && ReferenceEquals(launched.definition, heloDef)
                    && !IsHeloInActiveSlingOrder(launched))
                {
                    return launched;
                }
            }

            return null;
        }

        private static bool HasSlingWeaponStation(Aircraft ac)
        {
            List<WeaponStation>? stations = ac.weaponStations;
            if (stations == null)
            {
                return false;
            }

            for (int i = 0; i < stations.Count; i++)
            {
                WeaponStation? ws = stations[i];
                if (ws != null && ws.WeaponInfo != null && ws.WeaponInfo.sling && ws.Weapons.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static AircraftDefinition? FindTarantulaDefinition()
        {
            List<AircraftDefinition>? defs = Encyclopedia.i?.aircraft;
            if (defs == null)
            {
                return null;
            }

            AircraftDefinition? fallbackSling = null;
            for (int i = 0; i < defs.Count; i++)
            {
                AircraftDefinition? def = defs[i];
                if (def == null || def.unitPrefab == null)
                {
                    continue;
                }

                string label = $"{def.jsonKey} {def.unitName}";
                bool isTarantula = label.IndexOf("Tarantula", StringComparison.OrdinalIgnoreCase) >= 0
                    || label.IndexOf("VL-49", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isTarantula)
                {
                    return def;
                }

                if (fallbackSling == null && BuildSlingLoadout(def) != null)
                {
                    fallbackSling = def;
                }
            }

            return fallbackSling;
        }

        private static Loadout? BuildSlingLoadout(AircraftDefinition def)
        {
            HardpointSet[]? sets = def.unitPrefab?.GetComponent<Aircraft>()?.weaponManager?.hardpointSets;
            if (sets == null || sets.Length == 0)
            {
                return null;
            }

            var loadout = new Loadout();
            bool foundSling = false;
            for (int i = 0; i < sets.Length; i++)
            {
                WeaponMount? mount = null;
                if (!foundSling && sets[i].weaponOptions != null)
                {
                    for (int o = 0; o < sets[i].weaponOptions.Count; o++)
                    {
                        WeaponMount? cand = sets[i].weaponOptions[o];
                        if (cand != null && cand.info != null && cand.info.sling)
                        {
                            mount = cand;
                            foundSling = true;
                            break;
                        }
                    }
                }

                loadout.weapons.Add(mount);
            }

            return foundSling ? loadout : null;
        }

        private void DispatchNearestEngineers(EngineerBuildOrder order)
        {
            for (int i = order.AssignedBuilders.Count - 1; i >= 0; i--)
            {
                Unit b = order.AssignedBuilders[i];
                if (b == null
                    || b.disabled
                    || !ReferenceEquals(b.NetworkHQ, order.Hq)
                    || AiOperationalManeuverService.Instance.IsRetreatingOrOnQrf(b))
                {
                    order.AssignedBuilders.RemoveAt(i);
                }
            }

            if (order.AssignedBuilders.Count >= 2)
            {
                return;
            }

            builderScratch.Clear();
            for (int i = 0; i < UnitRegistry.allUnits.Count; i++)
            {
                Unit? u = UnitRegistry.allUnits[i];
                if (u == null
                    || u.disabled
                    || !ReferenceEquals(u.NetworkHQ, order.Hq)
                    || !(u is GroundVehicle)
                    || !GroundControlReflection.IsRepairUnit(u)
                    || order.AssignedBuilders.Contains(u)
                    || GroundControlReflection.EcoIsBuilder(u)
                    || GroundControlReflection.RepairHasAssignedTarget(u)
                    || AiOperationalManeuverService.Instance.IsRetreatingOrOnQrf(u)
                    || IsBuilderAssignedToAnyOrder(u))
                {
                    continue;
                }

                GlobalPosition uPos = u.GlobalPosition();
                if (FastMath.Distance(uPos, order.SitePos) <= EngineerSearchRadiusMeters
                    && GroundControlReflection.CanDriveBetween(uPos, order.SitePos))
                {
                    builderScratch.Add(u);
                }
            }

            builderScratch.Sort((a, b) =>
                FastMath.SquareDistance(a.GlobalPosition(), order.SitePos).CompareTo(
                    FastMath.SquareDistance(b.GlobalPosition(), order.SitePos)));

            for (int i = 0; i < builderScratch.Count && order.AssignedBuilders.Count < 2; i++)
            {
                Unit chosen = builderScratch[i];
                if (GroundControlReflection.TrySetDestination(chosen, order.SitePos))
                {
                    order.AssignedBuilders.Add(chosen);
                }
            }
        }

        private static int CountWorkingEngineersNear(FactionHQ hq, GlobalPosition site)
        {
            float rSq = EngineerWorkRadiusMeters * EngineerWorkRadiusMeters;
            int count = 0;
            for (int i = 0; i < UnitRegistry.allUnits.Count; i++)
            {
                Unit? u = UnitRegistry.allUnits[i];
                if (u != null
                    && !u.disabled
                    && ReferenceEquals(u.NetworkHQ, hq)
                    && u is GroundVehicle
                    && GroundControlReflection.IsRepairUnit(u)
                    && FastMath.SquareDistance(u.GlobalPosition(), site) <= rSq)
                {
                    count++;
                }
            }

            return count;
        }

        private void DrawLogisticsWindow(FactionHQ localHq)
        {
            float innerW = statusWindowRect.width - 24f;
            float y = 34f;

            for (int i = 0; i < slingOrders.Count; i++)
            {
                TarantulaSlingOrder s = slingOrders[i];
                if (!ReferenceEquals(s.Hq, localHq) || s.Helo == null)
                {
                    continue;
                }

                float dist = FastMath.Distance(s.Helo.GlobalPosition(), s.DropPos);
                string state = s.Lowering ? $"Lowering {s.LoweringProgress * 100f:0}%" : $"Tarantula en route ({dist:0} m)";
                GUI.Box(new Rect(12f, y, innerW, 44f), string.Empty, NativeThemeBridge.Inset);
                GUI.Label(new Rect(18f, y + 4f, innerW - 12f, 20f), $"{s.Spec.DisplayName}: {state}", NativeThemeBridge.Label);
                NativeThemeBridge.DrawBar(
                    new Rect(18f, y + 28f, innerW - 12f, 8f),
                    s.Lowering ? Mathf.Clamp01(s.LoweringProgress) : 0.2f,
                    NativeThemeBridge.Accent);
                y += 50f;
            }

            for (int i = 0; i < engineerOrders.Count; i++)
            {
                EngineerBuildOrder e = engineerOrders[i];
                if (!ReferenceEquals(e.Hq, localHq))
                {
                    continue;
                }

                int working = CountWorkingEngineersNear(e.Hq, e.SitePos);
                GUI.Box(new Rect(12f, y, innerW, 44f), string.Empty, NativeThemeBridge.Inset);
                GUI.Label(
                    new Rect(18f, y + 4f, innerW - 92f, 20f),
                    $"{e.Spec.DisplayName} · {working} M12",
                    NativeThemeBridge.Label);
                NativeThemeBridge.DrawBar(
                    new Rect(18f, y + 28f, innerW - 92f, 8f),
                    Mathf.Clamp01(e.Progress),
                    NativeThemeBridge.Accent);
                if (GUI.Button(new Rect(statusWindowRect.width - 86f, y + 8f, 68f, 28f), "CANCEL", NativeThemeBridge.DangerButton))
                {
                    engineerOrders.RemoveAt(i);
                    CancelAndRefundEngineerOrder(e);
                    break;
                }

                y += 50f;
            }

            GUI.DragWindow(new Rect(0f, 0f, statusWindowRect.width - 28f, 28f));
        }

        private static void DestroySlingVisuals(TarantulaSlingOrder sling)
        {
            DestroyObj(sling.KitVisual);
            sling.KitVisual = null;
            if (sling.CableLine != null)
            {
                DestroyObj(sling.CableLine.gameObject);
                sling.CableLine = null;
            }

            if (sling.CableMat != null)
            {
                UnityEngine.Object.Destroy(sling.CableMat);
                sling.CableMat = null;
            }
        }

        private static void DestroyObj(GameObject? go)
        {
            if (go != null)
            {
                UnityEngine.Object.Destroy(go);
            }
        }
    }

    internal sealed class RtsAbmStockpileBridgeImpl : IRtsAbmStockpileBridge
    {
        private readonly Dictionary<FactionAffinity, int> fallbackStock = new();

        public event Action<FactionAffinity, Vector3, float>? OnBallisticThreatDetected;

        public int GetAvailableAbmInterceptors(FactionAffinity faction)
        {
            FactionHQ? hq = ResolveHq(faction);
            if (hq != null && MdsBridgeBootstrap.ExternalAbmStockpileQuery != null)
            {
                return MdsBridgeBootstrap.ExternalAbmStockpileQuery(hq);
            }

            if (!fallbackStock.TryGetValue(faction, out int count))
            {
                count = Math.Max(4, AddonConfig.AbmMaxStockPerSite.Value);
                fallbackStock[faction] = count;
            }

            return count;
        }

        public bool TryReserveAbmInterceptor(FactionAffinity faction, string missileId)
        {
            FactionHQ? hq = ResolveHq(faction);
            if (hq != null && MdsBridgeBootstrap.ExternalAbmStockpileReserve != null)
            {
                return MdsBridgeBootstrap.ExternalAbmStockpileReserve(hq, missileId);
            }

            int avail = GetAvailableAbmInterceptors(faction);
            if (avail <= 0)
            {
                return false;
            }

            fallbackStock[faction] = avail - 1;
            return true;
        }

        internal void ResetSession()
        {
            fallbackStock.Clear();
        }

        internal void RaiseBallisticThreat(FactionHQ targetHq, GlobalPosition originPos, float blastYield)
        {
            FactionAffinity targetAffinity = ToAffinity(targetHq);
            OnBallisticThreatDetected?.Invoke(targetAffinity, originPos.ToLocalPosition(), blastYield);
        }
    }

    internal sealed class RtsAiThreatSinkImpl : IRtsAiThreatSink
    {
        private sealed class TrackedSite
        {
            internal string SiteId = string.Empty;
            internal FactionAffinity Faction;
            internal FactionHQ? Hq;
            internal WeaponCascadeRole Role;
            internal GlobalPosition Pos;
            internal bool RadarActive;
        }

        private const float AiMdsCooldownSeconds = 45f;

        private readonly RtsEconomyBridgeImpl ecoBridge;
        private readonly RtsCapBridgeImpl capBridge;
        private readonly RtsLogisticsBridgeImpl logisticsBridge;
        private readonly Dictionary<string, TrackedSite> trackedSites = new(StringComparer.Ordinal);
        private readonly Dictionary<FactionHQ, float> nextAiDeployAt = new();
        private readonly List<Airbase> safeBaseScratch = new();
        private readonly List<GlobalPosition> friendlyFcuScratch = new();
        private readonly List<WeaponCascadeRole> desiredRoleOrderScratch = new();
        private FactionHQ? lastDestroyedSiteHq;
        private FactionAffinity lastDestroyedSiteAffinity;
        private int lastDestroyedSiteFrame = -1;

        internal RtsAiThreatSinkImpl(RtsEconomyBridgeImpl ecoBridge, RtsCapBridgeImpl capBridge, RtsLogisticsBridgeImpl logisticsBridge)
        {
            this.ecoBridge = ecoBridge;
            this.capBridge = capBridge;
            this.logisticsBridge = logisticsBridge;
        }

        internal FactionHQ? ConsumeDestroyedSiteHq(FactionAffinity faction)
        {
            if (lastDestroyedSiteHq != null
                && Time.frameCount - lastDestroyedSiteFrame <= 1
                && (lastDestroyedSiteAffinity == faction || faction == FactionAffinity.Any || ToAffinity(lastDestroyedSiteHq) == faction))
            {
                FactionHQ result = lastDestroyedSiteHq;
                lastDestroyedSiteHq = null;
                lastDestroyedSiteFrame = -1;
                return result;
            }

            return null;
        }

        public void ReportDeployedModularSite(string siteId, FactionAffinity faction, WeaponCascadeRole role, Vector3 worldPos, bool radarActive)
        {
            if (string.IsNullOrEmpty(siteId))
            {
                return;
            }

            FactionHQ? resolvedHq = activeDeliveryHq
                ?? TryResolveSiteOwnerHqFromWorld(worldPos, faction)
                ?? logisticsBridge.FindPendingOrderHqNear(worldPos, faction)
                ?? ResolveHq(faction);
            if (trackedSites.TryGetValue(siteId, out TrackedSite? existing))
            {
                existing.Faction = faction;
                if (resolvedHq != null)
                {
                    existing.Hq = resolvedHq;
                }

                existing.Role = role;
                existing.Pos = worldPos.ToGlobalPosition();
                existing.RadarActive = radarActive;
                return;
            }

            trackedSites[siteId] = new TrackedSite
            {
                SiteId = siteId,
                Faction = faction,
                Hq = resolvedHq,
                Role = role,
                Pos = worldPos.ToGlobalPosition(),
                RadarActive = radarActive,
            };
        }

        private static FactionHQ? TryResolveSiteOwnerHqFromWorld(Vector3 worldPos, FactionAffinity faction)
        {
            GlobalPosition target = worldPos.ToGlobalPosition();
            float bestSq = 55f * 55f;
            FactionHQ? bestHq = null;
            for (int i = 0; i < UnitRegistry.allUnits.Count; i++)
            {
                Unit? u = UnitRegistry.allUnits[i];
                if (u == null || u.disabled || u.NetworkHQ == null)
                {
                    continue;
                }

                if (faction != FactionAffinity.Any && ToAffinity(u.NetworkHQ) != faction)
                {
                    continue;
                }

                float dSq = FastMath.SquareDistance(u.GlobalPosition(), target);
                if (dSq < bestSq)
                {
                    bestSq = dSq;
                    bestHq = u.NetworkHQ;
                }
            }

            return bestHq;
        }

        public void ReportSiteDestroyed(string siteId)
        {
            if (!string.IsNullOrEmpty(siteId))
            {
                if (trackedSites.TryGetValue(siteId, out TrackedSite? dying) && dying.Hq != null)
                {
                    lastDestroyedSiteHq = dying.Hq;
                    lastDestroyedSiteAffinity = dying.Faction;
                    lastDestroyedSiteFrame = Time.frameCount;
                }

                // Cap release is handled cleanly by MDSFramework SensorNetworkManager.UnregisterSite -> Caps.ReleaseCap.
                trackedSites.Remove(siteId);
            }
        }

        internal void ResetSession()
        {
            trackedSites.Clear();
            nextAiDeployAt.Clear();
            safeBaseScratch.Clear();
            friendlyFcuScratch.Clear();
            desiredRoleOrderScratch.Clear();
            lastDestroyedSiteHq = null;
            lastDestroyedSiteFrame = -1;
        }

        internal void CollectActiveFriendlyMdsEwPositions(FactionHQ hq, List<GlobalPosition> results)
        {
            if (hq == null)
            {
                return;
            }

            FactionAffinity affinity = ToAffinity(hq);
            foreach (TrackedSite site in trackedSites.Values)
            {
                bool owned = site.Hq != null ? ReferenceEquals(site.Hq, hq) : (site.Faction == affinity && affinity != FactionAffinity.Any);
                if (owned
                    && site.RadarActive
                    && (site.Role == WeaponCascadeRole.EarlyWarningRadar || site.Role == WeaponCascadeRole.FireControl))
                {
                    results.Add(site.Pos);
                }
            }
        }

        internal int CountOwnedSitesForHq(FactionHQ hq)
        {
            if (hq == null)
            {
                return 0;
            }

            FactionAffinity targetAffinity = ToAffinity(hq);
            int count = 0;
            foreach (TrackedSite site in trackedSites.Values)
            {
                if (ReferenceEquals(site.Hq, hq) || (site.Hq == null && site.Faction == targetAffinity))
                {
                    count++;
                }
            }

            return count;
        }

        internal bool HasFriendlyFcuWithinRange(FactionHQ? hq, FactionAffinity affinity, GlobalPosition pos, float radiusMeters)
        {
            float rSq = radiusMeters * radiusMeters;
            foreach (TrackedSite site in trackedSites.Values)
            {
                bool owned = hq != null
                    ? (ReferenceEquals(site.Hq, hq) || (site.Hq == null && site.Faction == affinity))
                    : (site.Faction == affinity);
                if (!owned || site.Role != WeaponCascadeRole.FireControl)
                {
                    continue;
                }

                if (FastMath.SquareDistance(site.Pos, pos) <= rSq)
                {
                    return true;
                }
            }

            return false;
        }

        internal bool HasEmittingHostileSite(FactionHQ aiHq)
        {
            if (aiHq == null || aiHq.trackingDatabase == null)
            {
                return false;
            }

            FactionAffinity aiAffinity = ToAffinity(aiHq);
            float levelTime = Time.timeSinceLevelLoad;
            foreach (KeyValuePair<PersistentID, TrackingInfo> kv in aiHq.trackingDatabase)
            {
                TrackingInfo? info = kv.Value;
                if (info == null
                    || !info.TryGetUnit(out Unit unit)
                    || unit == null
                    || unit.disabled
                    || ReferenceEquals(unit.NetworkHQ, aiHq)
                    || levelTime - info.lastSpottedTime > 90f)
                {
                    continue;
                }

                if (TryMatchEmittingHostileSite(aiHq, aiAffinity, info.GetPosition(), out _))
                {
                    return true;
                }
            }

            return false;
        }

        internal (Unit? Target, GlobalPosition Pos) FindPrioritySeadTarget(FactionHQ aiHq, GlobalPosition from)
        {
            if (aiHq == null || aiHq.trackingDatabase == null)
            {
                return (null, default);
            }

            FactionAffinity aiAffinity = ToAffinity(aiHq);
            float levelTime = Time.timeSinceLevelLoad;
            Unit? bestUnit = null;
            GlobalPosition bestPos = default;
            float bestScore = float.MaxValue;

            foreach (KeyValuePair<PersistentID, TrackingInfo> kv in aiHq.trackingDatabase)
            {
                TrackingInfo? info = kv.Value;
                if (info == null
                    || !info.TryGetUnit(out Unit unit)
                    || unit == null
                    || unit.disabled
                    || ReferenceEquals(unit.NetworkHQ, aiHq)
                    || levelTime - info.lastSpottedTime > 90f)
                {
                    continue;
                }

                GlobalPosition detectedPos = info.GetPosition();
                if (!TryMatchEmittingHostileSite(aiHq, aiAffinity, detectedPos, out TrackedSite? matchedSite)
                    || matchedSite == null)
                {
                    continue;
                }

                float d = FastMath.Distance(from, detectedPos);
                // Prioritize active FCU / radar hubs slightly ahead of raw distance.
                float priorityBias = matchedSite.Role == WeaponCascadeRole.FireControl ? 0.65f : 1.0f;
                float score = d * priorityBias;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestUnit = unit;
                    bestPos = detectedPos;
                }
            }

            return bestUnit != null ? (bestUnit, bestPos) : (null, default);
        }

        private bool TryMatchEmittingHostileSite(
            FactionHQ aiHq,
            FactionAffinity aiAffinity,
            GlobalPosition detectedPos,
            out TrackedSite? matched)
        {
            matched = null;
            float bestSq = 55f * 55f;
            foreach (TrackedSite site in trackedSites.Values)
            {
                bool hostile = site.Hq != null
                    ? !ReferenceEquals(site.Hq, aiHq)
                    : (site.Faction != FactionAffinity.Any && site.Faction != aiAffinity);
                if (!hostile || !site.RadarActive)
                {
                    continue;
                }

                float dSq = FastMath.SquareDistance(detectedPos, site.Pos);
                if (dSq < bestSq)
                {
                    bestSq = dSq;
                    matched = site;
                }
            }

            return matched != null;
        }

        internal void TickAiDeployment(FactionHQ aiHq)
        {
            if (!AddonConfig.EnableAiMdsDeployment.Value
                || AddonConfig.AiMaxMdsSites.Value <= 0
                || aiHq == null
                || !((NetworkBehaviour)aiHq).IsServer)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (nextAiDeployAt.TryGetValue(aiHq, out float nextAt) && now < nextAt)
            {
                return;
            }

            nextAiDeployAt[aiHq] = now + AiMdsCooldownSeconds;

            object? eco = GroundControlReflection.GetEcoInstance();
            if (AiStrategicDirectorService.Instance.EvaluatePhase(eco, aiHq) == AiStrategicPhase.EmergencyReconstruction)
            {
                return;
            }

            int liveAndQueued = CountOwnedSitesForHq(aiHq) + logisticsBridge.CountPendingForHq(aiHq);
            if (liveAndQueued >= AddonConfig.AiMaxMdsSites.Value)
            {
                return;
            }

            FactionAffinity aiAffinity = ToAffinity(aiHq);
            int techTier = ecoBridge.QueryFactionTechTier(aiHq);

            ModularUnitSpec? chosen = PickAiModularSpec(aiHq, aiAffinity, techTier, liveAndQueued);
            if (chosen == null)
            {
                return;
            }

            if (!capBridge.CanConsumeCap(aiHq, chosen.Logistics.CapCategory, chosen.Logistics.CapWeight, out _))
            {
                return;
            }

            if (!TryPickAiSitePosition(aiHq, aiAffinity, chosen, out Vector3 worldPos))
            {
                return;
            }

            if (!ecoBridge.TrySpendFactionCredits(aiHq, chosen.Logistics.CreditCost, $"AI MDS {chosen.DisplayName}"))
            {
                return;
            }

            if (!capBridge.TryConsumeCap(aiHq, chosen.Logistics.CapCategory, chosen.Logistics.CapWeight))
            {
                ecoBridge.TrySpendFactionCredits(aiHq, -chosen.Logistics.CreditCost, $"Refund AI {chosen.DisplayName}");
                return;
            }

            float yaw = UnityEngine.Random.Range(0f, 360f);
            if (!logisticsBridge.RequestEngineerBuildSite(aiHq, aiAffinity, chosen, worldPos, yaw))
            {
                capBridge.ReleaseCap(aiHq, chosen.Logistics.CapCategory, chosen.Logistics.CapWeight);
                ecoBridge.TrySpendFactionCredits(aiHq, -chosen.Logistics.CreditCost, $"Refund AI {chosen.DisplayName}");
                return;
            }

            AddonLog.Info($"[MDS Bridge] AI commander ({aiHq.faction?.factionName}) ordered '{chosen.DisplayName}' via M12 Jackknife.");
        }

        private int CountRoleOwnedAndPending(FactionHQ hq, FactionAffinity affinity, WeaponCascadeRole role)
        {
            int count = logisticsBridge.CountPendingForHqRole(hq, role);
            foreach (TrackedSite site in trackedSites.Values)
            {
                bool owned = ReferenceEquals(site.Hq, hq) || (site.Hq == null && site.Faction == affinity);
                if (owned && site.Role == role)
                {
                    count++;
                }
            }

            return count;
        }

        private ModularUnitSpec? PickAiModularSpec(FactionHQ aiHq, FactionAffinity aiAffinity, int techTier, int totalSites)
        {
            int fcuCount = CountRoleOwnedAndPending(aiHq, aiAffinity, WeaponCascadeRole.FireControl);
            int ewCount = CountRoleOwnedAndPending(aiHq, aiAffinity, WeaponCascadeRole.EarlyWarningRadar);
            int shortMedCount = CountRoleOwnedAndPending(aiHq, aiAffinity, WeaponCascadeRole.ShortMedSam);
            int helCount = CountRoleOwnedAndPending(aiHq, aiAffinity, WeaponCascadeRole.LaserHel);
            int closeInCount = CountRoleOwnedAndPending(aiHq, aiAffinity, WeaponCascadeRole.CloseInGun);
            int longSamCount = CountRoleOwnedAndPending(aiHq, aiAffinity, WeaponCascadeRole.LongRangeSam);
            int abmVlsCount = CountRoleOwnedAndPending(aiHq, aiAffinity, WeaponCascadeRole.AbmVls);

            desiredRoleOrderScratch.Clear();

            // Always ensure first Fire Control hub before FCU-dependent launchers.
            if (fcuCount == 0)
            {
                desiredRoleOrderScratch.Add(WeaponCascadeRole.FireControl);
            }

            // Build balanced core point/area layers (ShortMed -> LaserHel -> CloseInGun -> LongSam -> Tier3 ABM).
            if (shortMedCount == 0)
            {
                desiredRoleOrderScratch.Add(WeaponCascadeRole.ShortMedSam);
            }

            if (helCount == 0)
            {
                desiredRoleOrderScratch.Add(WeaponCascadeRole.LaserHel);
            }

            if (closeInCount == 0)
            {
                desiredRoleOrderScratch.Add(WeaponCascadeRole.CloseInGun);
            }

            // Build redundant 2nd networked FCU once Tech Tier 2+ and initial firing unit(s) exist.
            if (techTier >= 2 && fcuCount == 1 && totalSites >= 2)
            {
                desiredRoleOrderScratch.Add(WeaponCascadeRole.FireControl);
            }

            if (longSamCount == 0 && techTier >= 2)
            {
                desiredRoleOrderScratch.Add(WeaponCascadeRole.LongRangeSam);
            }

            if (abmVlsCount == 0 && techTier >= 3)
            {
                desiredRoleOrderScratch.Add(WeaponCascadeRole.AbmVls);
            }

            if (ewCount == 0)
            {
                desiredRoleOrderScratch.Add(WeaponCascadeRole.EarlyWarningRadar);
            }

            // Expansion duplicates once initial balanced network exists.
            desiredRoleOrderScratch.Add(WeaponCascadeRole.ShortMedSam);
            desiredRoleOrderScratch.Add(WeaponCascadeRole.LaserHel);
            desiredRoleOrderScratch.Add(WeaponCascadeRole.LongRangeSam);
            desiredRoleOrderScratch.Add(WeaponCascadeRole.AbmVls);
            desiredRoleOrderScratch.Add(WeaponCascadeRole.CloseInGun);
            if (techTier >= 2 && fcuCount < 2)
            {
                desiredRoleOrderScratch.Add(WeaponCascadeRole.FireControl);
            }

            IReadOnlyCollection<ContractorPackage> packs = ContractorRegistry.GetPackages();
            for (int r = 0; r < desiredRoleOrderScratch.Count; r++)
            {
                WeaponCascadeRole role = desiredRoleOrderScratch[r];
                foreach (ContractorPackage pkg in packs)
                {
                    if (pkg == null || !ecoBridge.IsPackageAllowedForFaction(pkg, aiAffinity) || pkg.Units == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < pkg.Units.Count; i++)
                    {
                        ModularUnitSpec spec = pkg.Units[i];
                        if (spec == null || spec.Role != role)
                        {
                            continue;
                        }

                        if (spec.Logistics.RequiredTechTier > techTier)
                        {
                            continue;
                        }

                        if (spec.RequiresFcuLink && fcuCount == 0)
                        {
                            continue;
                        }

                        if (aiHq.factionFunds < spec.Logistics.CreditCost * 1.4f)
                        {
                            continue;
                        }

                        return spec;
                    }
                }
            }

            return null;
        }

        private void CollectFriendlyFcuPositions(FactionHQ aiHq, FactionAffinity affinity)
        {
            friendlyFcuScratch.Clear();
            foreach (TrackedSite site in trackedSites.Values)
            {
                bool owned = ReferenceEquals(site.Hq, aiHq) || (site.Hq == null && site.Faction == affinity);
                if (owned && site.Role == WeaponCascadeRole.FireControl)
                {
                    friendlyFcuScratch.Add(site.Pos);
                }
            }

            if (logisticsBridge.TryGetPendingRolePosition(aiHq, WeaponCascadeRole.FireControl, out GlobalPosition pendingFcu))
            {
                friendlyFcuScratch.Add(pendingFcu);
            }
        }

        private bool IsSiteSpacingOk(FactionHQ aiHq, FactionAffinity affinity, GlobalPosition candidate, float minSpacingMeters)
        {
            float minSq = minSpacingMeters * minSpacingMeters;
            foreach (TrackedSite site in trackedSites.Values)
            {
                bool owned = ReferenceEquals(site.Hq, aiHq) || (site.Hq == null && site.Faction == affinity);
                if (owned && FastMath.SquareDistance(site.Pos, candidate) < minSq)
                {
                    return false;
                }
            }

            return !logisticsBridge.IsPointNearPendingSite(aiHq, candidate, minSpacingMeters);
        }

        private bool IsWithinFcuNetwork(GlobalPosition candidate, float maxRangeMeters)
        {
            if (friendlyFcuScratch.Count == 0)
            {
                return true;
            }

            float maxSq = maxRangeMeters * maxRangeMeters;
            for (int i = 0; i < friendlyFcuScratch.Count; i++)
            {
                if (FastMath.SquareDistance(friendlyFcuScratch[i], candidate) <= maxSq)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetThreatAngleFromBase(FactionHQ aiHq, FactionHQ? enemyHq, GlobalPosition baseAnchor, out float yawRad)
        {
            yawRad = 0f;
            float bestDistSq = float.MaxValue;
            GlobalPosition bestThreatPos = default;
            bool found = false;

            if (enemyHq != null)
            {
                foreach (Airbase eb in enemyHq.GetAirbases())
                {
                    if (eb == null || eb.disabled || eb.center == null)
                    {
                        continue;
                    }

                    GlobalPosition pos = eb.center.GlobalPosition();
                    float dSq = FastMath.SquareDistance(baseAnchor, pos);
                    if (dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        bestThreatPos = pos;
                        found = true;
                    }
                }
            }

            if (!found)
            {
                foreach (Airbase ab in FactionRegistry.airbaseLookup.Values)
                {
                    if (ab == null || ab.disabled || ab.center == null || ab.CurrentHQ == null || ReferenceEquals(ab.CurrentHQ, aiHq))
                    {
                        continue;
                    }

                    GlobalPosition pos = ab.center.GlobalPosition();
                    float dSq = FastMath.SquareDistance(baseAnchor, pos);
                    if (dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        bestThreatPos = pos;
                        found = true;
                    }
                }
            }

            if (!found)
            {
                return false;
            }

            yawRad = Mathf.Atan2(bestThreatPos.z - baseAnchor.z, bestThreatPos.x - baseAnchor.x);
            return true;
        }

        private bool TryPickAiSitePosition(FactionHQ aiHq, FactionAffinity affinity, ModularUnitSpec chosen, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            CollectFriendlyFcuPositions(aiHq, affinity);

            FactionHQ? localHq = GroundControlReflection.GetLocalHq();
            FactionHQ? enemyHq = ReferenceEquals(localHq, aiHq) ? null : localHq;
            AiBattlefieldIntelService.Instance.GetAirbasesOrderedBySafety(aiHq, enemyHq, safeBaseScratch);
            if (safeBaseScratch.Count == 0)
            {
                foreach (Airbase ab in aiHq.GetAirbases())
                {
                    if (ab != null && !ab.disabled && GroundControlReflection.IsLandBase(ab))
                    {
                        safeBaseScratch.Add(ab);
                    }
                }
            }

            float fcuRangeMeters = chosen.RequiresFcuLink ? Mathf.Max(800f, chosen.FcuLinkRadiusKm * 1000f * 0.9f) : float.MaxValue;

            // Second FCU: prefer another safe base anchor within mutual link range, else 350..maxLinkMeters around first FCU.
            if (chosen.Role == WeaponCascadeRole.FireControl && friendlyFcuScratch.Count > 0)
            {
                GlobalPosition fcuAnchor = friendlyFcuScratch[0];
                float maxLinkMeters = Mathf.Clamp(chosen.FcuLinkRadiusKm * 850f, 450f, 700f);
                for (int b = 0; b < safeBaseScratch.Count; b++)
                {
                    Airbase ab = safeBaseScratch[b];
                    Transform anchor = ab.center != null ? ab.center : ab.transform;
                    GlobalPosition basePos = anchor.GlobalPosition();
                    float distToFirst = FastMath.Distance(basePos, fcuAnchor);
                    if (distToFirst > maxLinkMeters + 380f)
                    {
                        continue;
                    }

                    bool hasThreatDir = TryGetThreatAngleFromBase(aiHq, enemyHq, basePos, out float threatYaw);
                    for (int attempt = 0; attempt < 12; attempt++)
                    {
                        float angle = (hasThreatDir && attempt < 8)
                            ? threatYaw + UnityEngine.Random.Range(-55f, 55f) * Mathf.Deg2Rad
                            : UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                        float radius = UnityEngine.Random.Range(160f, 380f);
                        var candidate = new GlobalPosition(
                            basePos.x + Mathf.Cos(angle) * radius,
                            basePos.y,
                            basePos.z + Mathf.Sin(angle) * radius);
                        GlobalPosition snapped = GroundControlReflection.SnapToTerrain(candidate);
                        float sep = FastMath.Distance(snapped, fcuAnchor);
                        if (sep < 350f || sep > maxLinkMeters)
                        {
                            continue;
                        }

                        if (!IsValidAiSiteCandidate(aiHq, affinity, chosen, snapped, fcuRangeMeters))
                        {
                            continue;
                        }

                        worldPos = snapped.ToLocalPosition();
                        return true;
                    }
                }

                for (int attempt = 0; attempt < 16; attempt++)
                {
                    float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                    float radius = UnityEngine.Random.Range(350f, maxLinkMeters);
                    var candidate = new GlobalPosition(
                        fcuAnchor.x + Mathf.Cos(angle) * radius,
                        fcuAnchor.y,
                        fcuAnchor.z + Mathf.Sin(angle) * radius);
                    GlobalPosition snapped = GroundControlReflection.SnapToTerrain(candidate);
                    if (!IsValidAiSiteCandidate(aiHq, affinity, chosen, snapped, fcuRangeMeters))
                    {
                        continue;
                    }

                    worldPos = snapped.ToLocalPosition();
                    return true;
                }
            }

            // Short-range point-defense / SHORAD covers forward/mid threatened bases first;
            // strategic FCU / EW / long-range / ABM hubs stay in safest rear bases first.
            bool preferForwardCoverage = chosen.Role == WeaponCascadeRole.ShortMedSam
                || chosen.Role == WeaponCascadeRole.LaserHel
                || chosen.Role == WeaponCascadeRole.CloseInGun;

            int count = safeBaseScratch.Count;
            for (int step = 0; step < count; step++)
            {
                int b = preferForwardCoverage ? (count - 1 - step) : step;
                Airbase ab = safeBaseScratch[b];
                Transform anchor = ab.center != null ? ab.center : ab.transform;
                GlobalPosition anchorGlobal = anchor.GlobalPosition();
                bool hasThreatDir = TryGetThreatAngleFromBase(aiHq, enemyHq, anchorGlobal, out float threatYaw);

                for (int attempt = 0; attempt < 16; attempt++)
                {
                    float angle = (hasThreatDir && attempt < 10)
                        ? threatYaw + UnityEngine.Random.Range(-50f, 50f) * Mathf.Deg2Rad
                        : UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                    float radius = UnityEngine.Random.Range(160f, 380f);
                    var candidate = new GlobalPosition(
                        anchorGlobal.x + Mathf.Cos(angle) * radius,
                        anchorGlobal.y,
                        anchorGlobal.z + Mathf.Sin(angle) * radius);
                    GlobalPosition snapped = GroundControlReflection.SnapToTerrain(candidate);
                    if (!IsValidAiSiteCandidate(aiHq, affinity, chosen, snapped, fcuRangeMeters))
                    {
                        continue;
                    }

                    worldPos = snapped.ToLocalPosition();
                    return true;
                }
            }

            return false;
        }

        private bool IsValidAiSiteCandidate(
            FactionHQ aiHq,
            FactionAffinity affinity,
            ModularUnitSpec chosen,
            GlobalPosition snapped,
            float fcuRangeMeters)
        {
            if (GroundControlReflection.IsBelowSeaLevel(snapped)
                || GroundControlReflection.BuildPreviewIsOnLevelRoad(snapped, 24f))
            {
                return false;
            }

            if (!IsSiteSpacingOk(aiHq, affinity, snapped, 120f))
            {
                return false;
            }

            if (chosen.RequiresFcuLink && friendlyFcuScratch.Count > 0 && !IsWithinFcuNetwork(snapped, fcuRangeMeters))
            {
                return false;
            }

            return HasReachableDepotOrBuilder(aiHq, snapped, 1600f);
        }

        private static bool HasReachableDepotOrBuilder(FactionHQ aiHq, GlobalPosition candidate, float maxRangeMeters)
        {
            float maxSq = maxRangeMeters * maxRangeMeters;
            List<VehicleDepot> depots = SceneComponentCache.GetVehicleDepots();
            for (int i = 0; i < depots.Count; i++)
            {
                VehicleDepot depot = depots[i];
                if (depot == null || depot.disabled || !ReferenceEquals(depot.NetworkHQ, aiHq))
                {
                    continue;
                }

                GlobalPosition depotPos = depot.GlobalPosition();
                if (FastMath.SquareDistance(depotPos, candidate) <= maxSq
                    && GroundControlReflection.CanDriveBetween(depotPos, candidate))
                {
                    return true;
                }
            }

            for (int i = 0; i < UnitRegistry.allUnits.Count; i++)
            {
                Unit? u = UnitRegistry.allUnits[i];
                if (u == null
                    || u.disabled
                    || !ReferenceEquals(u.NetworkHQ, aiHq)
                    || !(u is GroundVehicle)
                    || !GroundControlReflection.IsRepairUnit(u))
                {
                    continue;
                }

                GlobalPosition uPos = u.GlobalPosition();
                if (FastMath.SquareDistance(uPos, candidate) <= maxSq
                    && GroundControlReflection.CanDriveBetween(uPos, candidate))
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal sealed class MdsUiAndTransportHelper
    {
        private const string AirdropMountPrefix = "mds_airdrop_";
        private const string BuildDefKeyPrefix = "mds_build_";
        private const string ModularSiteBehaviorTypeName = "MDSFramework.Sensors.ModularSiteBehavior";
        private const string AirdropBehaviorTypeName = "MDSFramework.Deployment.AirdropContainerBehavior";

        private static readonly Type? ModularSiteBehaviorType = AccessTools.TypeByName(ModularSiteBehaviorTypeName);
        private static readonly Type? AirdropBehaviorType = AccessTools.TypeByName(AirdropBehaviorTypeName);
        private static readonly FieldInfo? SiteSpecField =
            AccessTools.Field(ModularSiteBehaviorType, "Spec");
        private static readonly PropertyInfo? AirdropSpecProp =
            AccessTools.Property(AirdropBehaviorType, "Spec");
        private static readonly FieldInfo? SiteSuspendedInTransitField =
            AccessTools.Field(ModularSiteBehaviorType, "SuspendedInTransit");
        private static readonly MethodInfo? SiteRequestDeployMethod =
            AccessTools.Method(ModularSiteBehaviorType, "RequestDeployState", new[] { typeof(bool) });
        private static readonly PropertyInfo? SiteIsWeaponSiteProp =
            AccessTools.Property(ModularSiteBehaviorType, "IsWeaponSite");
        private static readonly PropertyInfo? SiteIsFireControlRadarProp =
            AccessTools.Property(ModularSiteBehaviorType, "IsFireControlRadar");
        private static readonly PropertyInfo? SiteIsCommandCenterProp =
            AccessTools.Property(ModularSiteBehaviorType, "IsCommandCenter");
        private static readonly PropertyInfo? SiteRequiresFcuLinkProp =
            AccessTools.Property(ModularSiteBehaviorType, "RequiresFcuLink");
        private static readonly PropertyInfo? SiteHasActiveFcuLinkProp =
            AccessTools.Property(ModularSiteBehaviorType, "HasActiveFcuLink");
        private static readonly PropertyInfo? SiteFcuLinkRadiusKmProp =
            AccessTools.Property(ModularSiteBehaviorType, "FcuLinkRadiusKm");
        private static readonly PropertyInfo? SiteEoIrBackupRangeKmProp =
            AccessTools.Property(ModularSiteBehaviorType, "EoIrBackupRangeKm");
        private static readonly PropertyInfo? SiteIsHoldFireActiveProp =
            AccessTools.Property(ModularSiteBehaviorType, "IsHoldFireActive");
        private static readonly PropertyInfo? SiteIsInCommandNetProp =
            AccessTools.Property(ModularSiteBehaviorType, "IsInCommandNet");
        private static readonly PropertyInfo? SiteCommandNetFcuCountProp =
            AccessTools.Property(ModularSiteBehaviorType, "CommandNetFcuCount");
        private static readonly PropertyInfo? SiteCommandNetActiveFcuCountProp =
            AccessTools.Property(ModularSiteBehaviorType, "CommandNetActiveFcuCount");
        private static readonly PropertyInfo? SiteCommandNetEffectorCountProp =
            AccessTools.Property(ModularSiteBehaviorType, "CommandNetEffectorCount");
        private static readonly MethodInfo? SiteSetHoldFireMethod =
            AccessTools.Method(ModularSiteBehaviorType, "SetHoldFire", new[] { typeof(bool) });

        private readonly RtsEconomyBridgeImpl ecoBridge;
        private readonly RtsCapBridgeImpl capBridge;
        private readonly RtsLogisticsBridgeImpl logisticsBridge;
        private readonly Dictionary<string, BuildingDefinition> buildDefsByUnitId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<BuildingDefinition, (ContractorPackage Pkg, ModularUnitSpec Spec)> metaByBuildDef = new();
        private Encyclopedia? cachedEnc;
        private int lastInjectedMountCount = -1;

        internal MdsUiAndTransportHelper(
            RtsEconomyBridgeImpl ecoBridge,
            RtsCapBridgeImpl capBridge,
            RtsLogisticsBridgeImpl logisticsBridge)
        {
            this.ecoBridge = ecoBridge;
            this.capBridge = capBridge;
            this.logisticsBridge = logisticsBridge;
        }

        internal void ResetSession()
        {
            buildDefsByUnitId.Clear();
            metaByBuildDef.Clear();
            cachedEnc = null;
            lastInjectedMountCount = -1;
        }

        internal void EnsureAirdropMountsInjected()
        {
            Encyclopedia? enc = Encyclopedia.i;
            if (enc == null || enc.weaponMounts == null)
            {
                return;
            }

            var mdsMounts = new List<WeaponMount>();
            for (int i = 0; i < enc.weaponMounts.Count; i++)
            {
                WeaponMount? wm = enc.weaponMounts[i];
                if (wm != null
                    && !string.IsNullOrEmpty(wm.jsonKey)
                    && wm.jsonKey.StartsWith(AirdropMountPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    mdsMounts.Add(wm);
                }
            }

            if (mdsMounts.Count == 0 || (ReferenceEquals(cachedEnc, enc) && lastInjectedMountCount == mdsMounts.Count))
            {
                return;
            }

            var candidateAircraft = new List<AircraftDefinition>();
            var seenAircraft = new HashSet<AircraftDefinition>();
            if (enc.aircraft != null)
            {
                for (int i = 0; i < enc.aircraft.Count; i++)
                {
                    AircraftDefinition? ac = enc.aircraft[i];
                    if (ac != null && seenAircraft.Add(ac))
                    {
                        candidateAircraft.Add(ac);
                    }
                }
            }

            AircraftDefinition[] allResourceAircraft = Resources.FindObjectsOfTypeAll<AircraftDefinition>();
            for (int i = 0; i < allResourceAircraft.Length; i++)
            {
                AircraftDefinition? ac = allResourceAircraft[i];
                if (ac != null && seenAircraft.Add(ac))
                {
                    candidateAircraft.Add(ac);
                }
            }

            int injectedAircraft = 0;
            int injectedBays = 0;

            for (int a = 0; a < candidateAircraft.Count; a++)
            {
                AircraftDefinition acDef = candidateAircraft[a];
                Aircraft? ac = acDef.unitPrefab != null ? acDef.unitPrefab.GetComponent<Aircraft>() : null;
                HardpointSet[]? sets = ac?.weaponManager?.hardpointSets;
                if (sets == null || sets.Length == 0)
                {
                    continue;
                }

                bool acUpdated = false;
                for (int h = 0; h < sets.Length; h++)
                {
                    HardpointSet? hs = sets[h];
                    if (hs == null || hs.weaponOptions == null || hs.weaponOptions.Count == 0)
                    {
                        continue;
                    }

                    if (!IsContainerCargoBay(hs, out bool fullContainerBay))
                    {
                        continue;
                    }

                    bool bayUpdated = false;
                    for (int m = 0; m < mdsMounts.Count; m++)
                    {
                        WeaponMount mount = mdsMounts[m];
                        bool allowBay = fullContainerBay;
                        if (!allowBay && TryResolveSpecFromAirdropMount(mount, out _, out ModularUnitSpec spec))
                        {
                            allowBay = spec.Logistics.AllowLightCargoBays;
                        }

                        if (!allowBay)
                        {
                            continue;
                        }

                        if (!hs.weaponOptions.Contains(mount))
                        {
                            hs.weaponOptions.Add(mount);
                            bayUpdated = true;
                        }
                    }

                    if (bayUpdated)
                    {
                        injectedBays++;
                        acUpdated = true;
                    }
                }

                if (acUpdated)
                {
                    injectedAircraft++;
                }
            }

            if (injectedBays > 0)
            {
                lastInjectedMountCount = mdsMounts.Count;
                cachedEnc = enc;
                AddonLog.Info($"[MDS Bridge] Injected {mdsMounts.Count} MDS airdrop container mount(s) into {injectedAircraft} aircraft ({injectedBays} cargo bay(s)).");
            }
            else if (lastInjectedMountCount == mdsMounts.Count)
            {
                // Already injected earlier; avoid redundant re-scan unless new mounts arrive.
                cachedEnc = enc;
            }
        }

        internal bool IsAirdropMount(WeaponMount? mount)
        {
            return mount != null && TryResolveSpecFromAirdropMount(mount, out _, out _);
        }

        internal float? GetCargoUnitCost(UnitDefinition? cargo)
        {
            if (cargo == null)
            {
                return null;
            }

            string? key = !string.IsNullOrEmpty(cargo.jsonKey) ? cargo.jsonKey : cargo.name;
            if (string.IsNullOrEmpty(key) || !key!.StartsWith("mds_cargo_", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string unitId = key.Substring("mds_cargo_".Length);
            if (!string.IsNullOrEmpty(unitId) &&
                ContractorRegistry.TryGetUnitSpec(unitId, out _, out ModularUnitSpec spec) &&
                spec != null)
            {
                return Mathf.Max(1f, spec.Logistics.CreditCost);
            }

            return cargo.value > 0f ? cargo.value : null;
        }

        internal bool IsAirdropMountAllowedForHq(WeaponMount? mount, FactionHQ? hq)
        {
            if (mount == null || !TryResolveSpecFromAirdropMount(mount, out ContractorPackage pkg, out _))
            {
                return true;
            }

            return ecoBridge.IsPackageAllowedForFaction(pkg, ToAffinity(hq));
        }

        internal string GetAirdropMountSuffix(WeaponMount? mount, FactionHQ? hq)
        {
            if (mount == null || hq == null || !TryResolveSpecFromAirdropMount(mount, out _, out ModularUnitSpec spec))
            {
                return string.Empty;
            }

            int tier = ecoBridge.QueryFactionTechTier(hq);
            if (tier < spec.Logistics.RequiredTechTier)
            {
                return $" [Need T{spec.Logistics.RequiredTechTier}]";
            }

            return string.Empty;
        }

        internal string? GetAirdropMountLabelOverride(WeaponMount? mount, FactionHQ? hq)
        {
            if (mount == null || !TryResolveSpecFromAirdropMount(mount, out _, out ModularUnitSpec spec))
            {
                return null;
            }

            int tier = hq != null ? ecoBridge.QueryFactionTechTier(hq) : 1;
            string tierTag = tier < spec.Logistics.RequiredTechTier
                ? $" [Need T{spec.Logistics.RequiredTechTier}]"
                : string.Empty;
            string capTag = hq != null &&
                            spec.Logistics.CapWeight > 0 &&
                            !capBridge.CanConsumeCap(hq, spec.Logistics.CapCategory, spec.Logistics.CapWeight, out _)
                ? " [CAP FULL]"
                : string.Empty;
            return $"{spec.DisplayName} [Airdrop]{tierTag}{capTag}";
        }

        internal string? GetAirdropLoadoutBlockReason(IReadOnlyList<WeaponMount>? mounts, FactionHQ? hq)
        {
            if (mounts == null || mounts.Count == 0 || hq == null)
            {
                return null;
            }

            FactionAffinity affinity = ToAffinity(hq);
            int tier = ecoBridge.QueryFactionTechTier(hq);
            int groundWeight = 0;
            int airWeight = 0;
            int navalWeight = 0;
            ModularUnitSpec? firstGroundSpec = null;
            ModularUnitSpec? firstAirSpec = null;
            ModularUnitSpec? firstNavalSpec = null;

            for (int i = 0; i < mounts.Count; i++)
            {
                WeaponMount? mount = mounts[i];
                if (mount == null || !TryResolveSpecFromAirdropMount(mount, out ContractorPackage pkg, out ModularUnitSpec spec))
                {
                    continue;
                }

                if (!ecoBridge.IsPackageAllowedForFaction(pkg, affinity))
                {
                    return $"BLOCKED ({spec.DisplayName}): restricted to {pkg.DefaultFaction} faction.";
                }

                if (tier < spec.Logistics.RequiredTechTier)
                {
                    return $"BLOCKED ({spec.DisplayName}): Requires Command HQ Lvl {spec.Logistics.RequiredTechTier}.";
                }

                int w = Mathf.Max(0, spec.Logistics.CapWeight);
                if (w <= 0)
                {
                    continue;
                }

                if (string.Equals(spec.Logistics.CapCategory, "Air", StringComparison.OrdinalIgnoreCase))
                {
                    airWeight += w;
                    firstAirSpec ??= spec;
                }
                else if (string.Equals(spec.Logistics.CapCategory, "Naval", StringComparison.OrdinalIgnoreCase))
                {
                    navalWeight += w;
                    firstNavalSpec ??= spec;
                }
                else
                {
                    groundWeight += w;
                    firstGroundSpec ??= spec;
                }
            }

            if (groundWeight > 0 && !capBridge.CanConsumeCap(hq, "Ground", groundWeight, out string gReason))
            {
                return $"BLOCKED ({firstGroundSpec!.DisplayName}): {gReason}";
            }

            if (airWeight > 0 && !capBridge.CanConsumeCap(hq, "Air", airWeight, out string aReason))
            {
                return $"BLOCKED ({firstAirSpec!.DisplayName}): {aReason}";
            }

            if (navalWeight > 0 && !capBridge.CanConsumeCap(hq, "Naval", navalWeight, out string nReason))
            {
                return $"BLOCKED ({firstNavalSpec!.DisplayName}): {nReason}";
            }

            return null;
        }

        internal (int Ground, int Air, int Naval) CountPendingBlueprintCapForHq(FactionHQ hq)
        {
            if (hq == null)
            {
                return (0, 0, 0);
            }

            IReadOnlyList<object>? bps = GroundControlReflection.GetActiveBlueprints();
            if (bps == null || bps.Count == 0)
            {
                return (0, 0, 0);
            }

            int ground = 0;
            int air = 0;
            int naval = 0;
            for (int i = 0; i < bps.Count; i++)
            {
                if (!GroundControlReflection.TryGetBlueprintInfo(bps[i], out FactionHQ? bpHq, out BuildingDefinition? bpDef)
                    || !ReferenceEquals(bpHq, hq)
                    || bpDef == null
                    || !TryResolveBuildMeta(bpDef, out _, out ModularUnitSpec spec))
                {
                    continue;
                }

                int w = Mathf.Max(0, spec.Logistics.CapWeight);
                if (w <= 0)
                {
                    continue;
                }

                if (string.Equals(spec.Logistics.CapCategory, "Air", StringComparison.OrdinalIgnoreCase))
                {
                    air += w;
                }
                else if (string.Equals(spec.Logistics.CapCategory, "Naval", StringComparison.OrdinalIgnoreCase))
                {
                    naval += w;
                }
                else
                {
                    ground += w;
                }
            }

            return (ground, air, naval);
        }

        internal bool IsRawEncyclopediaDefinition(BuildingDefinition? def)
        {
            if (def == null || IsBuildDefinition(def))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(def.jsonKey)
                && def.jsonKey.StartsWith("mds_", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrEmpty(def.unitName)
                && def.unitName.StartsWith("[MDS]", StringComparison.OrdinalIgnoreCase);
        }

        internal (string GlbPath, float TargetMaxMeters, bool SampleEndFrame)? TryResolvePreviewGlb(UnitDefinition? def)
        {
            if (def == null || string.IsNullOrEmpty(def.jsonKey))
            {
                return null;
            }

            string key = def.jsonKey;
            if (key.StartsWith("mds_missile_", StringComparison.OrdinalIgnoreCase))
            {
                string missileId = key.Substring("mds_missile_".Length);
                if (ContractorRegistry.TryGetMissileSpec(missileId, out ContractorPackage mPkg, out CustomMissileSpec mSpec) &&
                    mPkg != null &&
                    mSpec != null &&
                    !string.IsNullOrEmpty(mPkg.AssetRootDirectory))
                {
                    string? missileGlb = TryFindGlbInPackageRoot(mPkg.AssetRootDirectory, mSpec.GlbRelativePath);
                    if (string.IsNullOrEmpty(missileGlb) && mSpec.FallbackGlbRelativePaths != null)
                    {
                        for (int i = 0; i < mSpec.FallbackGlbRelativePaths.Length; i++)
                        {
                            missileGlb = TryFindGlbInPackageRoot(mPkg.AssetRootDirectory, mSpec.FallbackGlbRelativePaths[i]);
                            if (!string.IsNullOrEmpty(missileGlb))
                            {
                                break;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(missileGlb))
                    {
                        return (missileGlb!, 4.2f, true);
                    }
                }

                return null;
            }

            string unitId;
            if (key.StartsWith(BuildDefKeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                unitId = key.Substring(BuildDefKeyPrefix.Length);
            }
            else if (key.StartsWith("mds_cargo_", StringComparison.OrdinalIgnoreCase))
            {
                unitId = key.Substring("mds_cargo_".Length);
            }
            else if (key.StartsWith("mds_", StringComparison.OrdinalIgnoreCase))
            {
                unitId = key.Substring("mds_".Length);
            }
            else
            {
                return null;
            }

            if (!ContractorRegistry.TryGetUnitSpec(unitId, out ContractorPackage pkg, out ModularUnitSpec spec)
                || pkg == null
                || spec == null
                || string.IsNullOrEmpty(pkg.AssetRootDirectory))
            {
                return null;
            }

            string? resolvedPath = ResolveDistinctPreviewGlbPath(pkg.AssetRootDirectory, spec);
            return string.IsNullOrEmpty(resolvedPath) ? null : (resolvedPath!, 6.2f, false);
        }

        private static string? ResolveDistinctPreviewGlbPath(string assetRoot, ModularUnitSpec spec)
        {
            // Prefer open weapon/turret models for icon clarity whenever a *_container.glb also has an open sibling.
            if (!string.IsNullOrEmpty(spec.GlbRelativePath)
                && spec.GlbRelativePath.EndsWith("_container.glb", StringComparison.OrdinalIgnoreCase))
            {
                string openRel = spec.GlbRelativePath.Substring(0, spec.GlbRelativePath.Length - "_container.glb".Length) + ".glb";
                string? openFull = TryFindGlbInPackageRoot(assetRoot, openRel);
                if (!string.IsNullOrEmpty(openFull))
                {
                    return openFull;
                }
            }

            if (spec.FallbackGlbRelativePaths != null)
            {
                for (int i = 0; i < spec.FallbackGlbRelativePaths.Length; i++)
                {
                    string rel = spec.FallbackGlbRelativePaths[i];
                    if (!string.IsNullOrEmpty(rel)
                        && rel.IndexOf("_container", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        string? found = TryFindGlbInPackageRoot(assetRoot, rel);
                        if (!string.IsNullOrEmpty(found))
                        {
                            return found;
                        }
                    }
                }
            }

            string? primary = TryFindGlbInPackageRoot(assetRoot, spec.GlbRelativePath);
            if (!string.IsNullOrEmpty(primary))
            {
                return primary;
            }

            if (spec.FallbackGlbRelativePaths != null)
            {
                for (int i = 0; i < spec.FallbackGlbRelativePaths.Length; i++)
                {
                    string? fb = TryFindGlbInPackageRoot(assetRoot, spec.FallbackGlbRelativePaths[i]);
                    if (!string.IsNullOrEmpty(fb))
                    {
                        return fb;
                    }
                }
            }

            return null;
        }

        private static string? TryFindGlbInPackageRoot(string root, string? relPath)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(relPath))
            {
                return null;
            }

            string direct = Path.Combine(root, relPath);
            if (File.Exists(direct))
            {
                return direct;
            }

            string assetsSub = Path.Combine(root, "Assets", relPath);
            if (File.Exists(assetsSub))
            {
                return assetsSub;
            }

            string fileOnly = Path.GetFileName(relPath!);
            if (!string.IsNullOrEmpty(fileOnly))
            {
                string flat = Path.Combine(root, fileOnly);
                if (File.Exists(flat))
                {
                    return flat;
                }

                string flatAssets = Path.Combine(root, "Assets", fileOnly);
                if (File.Exists(flatAssets))
                {
                    return flatAssets;
                }
            }

            return null;
        }

        internal IReadOnlyList<BuildingDefinition> GetBuildCatalogDefinitionsForHq(FactionHQ? hq)
        {
            EnsureBuildCatalogSynced();
            var list = new List<BuildingDefinition>();
            if (buildDefsByUnitId.Count == 0)
            {
                return list;
            }

            FactionAffinity affinity = ToAffinity(hq);
            foreach (KeyValuePair<BuildingDefinition, (ContractorPackage Pkg, ModularUnitSpec Spec)> kv in metaByBuildDef)
            {
                if (kv.Key != null && ecoBridge.IsPackageAllowedForFaction(kv.Value.Pkg, affinity))
                {
                    list.Add(kv.Key);
                }
            }

            return list;
        }

        internal bool IsBuildDefinition(BuildingDefinition? def)
        {
            if (def == null)
            {
                return false;
            }

            if (metaByBuildDef.ContainsKey(def))
            {
                return true;
            }

            return !string.IsNullOrEmpty(def.jsonKey)
                && def.jsonKey.StartsWith(BuildDefKeyPrefix, StringComparison.OrdinalIgnoreCase);
        }

        internal float GetBuildCost(BuildingDefinition? def)
        {
            if (def != null && TryResolveBuildMeta(def, out _, out ModularUnitSpec spec))
            {
                return Mathf.Max(1f, spec.Logistics.CreditCost);
            }

            return def != null ? def.value : 0f;
        }

        internal string GetBuildLabel(BuildingDefinition? def, FactionHQ? hq)
        {
            if (def == null)
            {
                return string.Empty;
            }

            if (!TryResolveBuildMeta(def, out _, out ModularUnitSpec spec))
            {
                return !string.IsNullOrEmpty(def.unitName) ? def.unitName : def.jsonKey;
            }

            int tier = ecoBridge.QueryFactionTechTier(hq);
            if (tier < spec.Logistics.RequiredTechTier)
            {
                return $"{spec.DisplayName} [T{spec.Logistics.RequiredTechTier}]";
            }

            if (hq != null &&
                spec.Logistics.CapWeight > 0 &&
                !capBridge.CanConsumeCap(hq, spec.Logistics.CapCategory, spec.Logistics.CapWeight, out _))
            {
                return $"{spec.DisplayName} [CAP FULL]";
            }

            return spec.DisplayName;
        }

        internal string? GetBuildBlockReason(BuildingDefinition? def, FactionHQ? hq)
        {
            if (def == null || !TryResolveBuildMeta(def, out ContractorPackage pkg, out ModularUnitSpec spec))
            {
                return null;
            }

            FactionAffinity affinity = ToAffinity(hq);
            if (!ecoBridge.IsPackageAllowedForFaction(pkg, affinity))
            {
                return $"{pkg.DisplayName} restricted to {pkg.DefaultFaction} faction.";
            }

            int tier = ecoBridge.QueryFactionTechTier(hq);
            if (tier < spec.Logistics.RequiredTechTier)
            {
                return $"Requires Command HQ Lvl {spec.Logistics.RequiredTechTier} (Tech Tier {spec.Logistics.RequiredTechTier}).";
            }

            if (!capBridge.CanConsumeCap(hq, spec.Logistics.CapCategory, spec.Logistics.CapWeight, out string capReason))
            {
                return capReason;
            }

            return null;
        }

        internal bool TryReplaceBuiltPlaceholder(BuildingDefinition def, FactionHQ hq, Unit spawnedPlaceholder)
        {
            if (!TryResolveBuildMeta(def, out _, out ModularUnitSpec spec))
            {
                return false;
            }

            if (hq == null || !((NetworkBehaviour)hq).IsServer || spawnedPlaceholder == null)
            {
                return true;
            }

            FactionAffinity affinity = ToAffinity(hq);
            Vector3 worldPos = GroundControlReflection.SnapToTerrain(spawnedPlaceholder.GlobalPosition()).ToLocalPosition();
            float yaw = spawnedPlaceholder.transform.eulerAngles.y;

            bool capOk = capBridge.TryConsumeCap(hq, spec.Logistics.CapCategory, spec.Logistics.CapWeight);

            Collider[] cols = spawnedPlaceholder.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null)
                {
                    cols[i].enabled = false;
                }
            }

            spawnedPlaceholder.gameObject.SetActive(false);
            Physics.SyncTransforms();

            GroundControlReflection.RemoveFromEconomyBuiltUnits(spawnedPlaceholder);
            NetworkManagerNuclearOption? net = NetworkManagerNuclearOption.i;
            ServerObjectManager? som = net != null ? net.ServerObjectManager : null;
            if (som != null && spawnedPlaceholder.Identity != null && spawnedPlaceholder.Identity.IsSpawned)
            {
                som.Destroy(spawnedPlaceholder.Identity, !spawnedPlaceholder.Identity.IsSceneObject);
            }
            else
            {
                UnityEngine.Object.Destroy(spawnedPlaceholder.gameObject);
            }

            if (!capOk)
            {
                ecoBridge.TrySpendFactionCredits(hq, -spec.Logistics.CreditCost, $"Refund {spec.DisplayName}");
                if (ReferenceEquals(hq, GroundControlReflection.GetLocalHq()))
                {
                    GroundControlReflection.NoticeFail($"{spec.DisplayName} cancelled: Unit cap full (refunded {spec.Logistics.CreditCost:0}).");
                }

                return true;
            }

            logisticsBridge.DeliverBuiltSiteImmediately(hq, spec.UnitId, affinity, worldPos, yaw);
            AddonLog.Info($"[MDS Bridge] Built '{spec.DisplayName}' via BUILD menu at {worldPos} for {hq.faction?.factionName}.");

            Unit? spawnedSiteUnit = FindNearestSpawnedMdsUnit(hq, worldPos, 12f);
            if (spawnedSiteUnit != null)
            {
                GroundControlReflection.AddToEconomyBuiltUnits(spawnedSiteUnit);
            }

            if (spec.RequiresFcuLink && ReferenceEquals(hq, GroundControlReflection.GetLocalHq()))
            {
                RtsAiThreatSinkImpl? threats = MdsBridgeAdapter.CurrentAiThreats;
                bool hasFcu = threats != null && threats.HasFriendlyFcuWithinRange(hq, affinity, worldPos.ToGlobalPosition(), spec.FcuLinkRadiusKm * 1000f);
                if (!hasFcu)
                {
                    GroundControlReflection.NoticeFail(
                        $"WARNING: {spec.DisplayName} has NO FCU link within {spec.FcuLinkRadiusKm:0} km — build a Skyshield AESA FCU for full radar guidance.");
                }
            }

            return true;
        }

        internal bool IsHaulableContainer(Unit? unit)
        {
            if (unit == null || unit.disabled || unit is GroundVehicle)
            {
                return false;
            }

            if (HasMdsComponent(unit))
            {
                if (unit.definition != null)
                {
                    unit.definition.CanSlingLoad = true;
                }

                return true;
            }

            return false;
        }

        internal bool IsMdsSelectableUnit(Unit? unit)
            => unit != null && !unit.disabled && HasMdsComponent(unit);

        internal bool HasSelectedFriendlyMdsWeaponSite()
        {
            FactionHQ? localHq = GroundControlReflection.GetLocalHq();
            IReadOnlyList<Unit>? selected = GroundControlReflection.GetSelectedUnits();
            if (localHq == null || selected == null || selected.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < selected.Count; i++)
            {
                Unit? u = selected[i];
                if (u == null || u.disabled || !ReferenceEquals(u.NetworkHQ, localHq))
                {
                    continue;
                }

                var status = QueryMdsSiteUiStatus(u);
                if (status.IsMdsSite && status.IsWeaponSite)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasMdsComponent(Unit? unit)
        {
            if (unit == null)
            {
                return false;
            }

            MonoBehaviour[] behaviours = unit.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour? mb = behaviours[i];
                if (mb == null)
                {
                    continue;
                }

                string? typeName = mb.GetType().FullName;
                if (string.Equals(typeName, ModularSiteBehaviorTypeName, StringComparison.Ordinal)
                    || string.Equals(typeName, AirdropBehaviorTypeName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static Unit? FindNearestSpawnedMdsUnit(FactionHQ hq, Vector3 worldPos, float maxRadiusMeters)
        {
            Unit? best = null;
            float bestDistSq = maxRadiusMeters * maxRadiusMeters;
            GlobalPosition targetGlobal = worldPos.ToGlobalPosition();
            for (int i = UnitRegistry.allUnits.Count - 1; i >= 0; i--)
            {
                Unit? u = UnitRegistry.allUnits[i];
                if (u == null || u.disabled || !ReferenceEquals(u.NetworkHQ, hq) || !HasMdsComponent(u))
                {
                    continue;
                }

                float dSq = FastMath.SquareDistance(u.GlobalPosition(), targetGlobal);
                if (dSq <= bestDistSq)
                {
                    bestDistSq = dSq;
                    best = u;
                }
            }

            return best;
        }

        internal (bool IsMdsSite, bool IsWeaponSite, bool IsFireControlRadar, bool IsCommandCenter, bool RequiresFcuLink, bool HasFcuLink, float FcuRadiusKm, float EoIrBackupKm, bool IsHoldFire, bool IsInCommandNet, int CommandNetFcuCount, int CommandNetActiveFcuCount, int CommandNetEffectorCount) QueryMdsSiteUiStatus(Unit? unit)
        {
            Component? siteComp = FindModularSiteComponent(unit);
            if (siteComp == null)
            {
                return default;
            }

            bool isWeaponSite = SiteIsWeaponSiteProp?.GetValue(siteComp) is bool ws && ws;
            bool isFcr = SiteIsFireControlRadarProp?.GetValue(siteComp) is bool fcr && fcr;
            bool isCmd = SiteIsCommandCenterProp?.GetValue(siteComp) is bool cmd && cmd;
            bool requiresFcu = SiteRequiresFcuLinkProp?.GetValue(siteComp) is bool rf && rf;
            bool hasFcu = SiteHasActiveFcuLinkProp?.GetValue(siteComp) is bool hf && hf;
            float fcuRadius = SiteFcuLinkRadiusKmProp?.GetValue(siteComp) is float r ? r : 18f;
            float eoirKm = SiteEoIrBackupRangeKmProp?.GetValue(siteComp) is float eo ? eo : 0f;
            bool isHold = SiteIsHoldFireActiveProp?.GetValue(siteComp) is bool ih && ih;
            bool isInNet = SiteIsInCommandNetProp?.GetValue(siteComp) is bool inet && inet;
            int netFcus = SiteCommandNetFcuCountProp?.GetValue(siteComp) is int nfc ? nfc : 0;
            int netActiveFcus = SiteCommandNetActiveFcuCountProp?.GetValue(siteComp) is int nafc ? nafc : 0;
            int netEffectors = SiteCommandNetEffectorCountProp?.GetValue(siteComp) is int nef ? nef : 0;

            return (true, isWeaponSite, isFcr, isCmd, requiresFcu, hasFcu, fcuRadius, eoirKm, isHold, isInNet, netFcus, netActiveFcus, netEffectors);
        }

        internal float? GetMdsDefencePrice(Unit? unit)
        {
            if (unit == null)
            {
                return null;
            }

            Component? siteComp = FindModularSiteComponent(unit);
            if (siteComp != null && SiteSpecField?.GetValue(siteComp) is ModularUnitSpec siteSpec)
            {
                return Mathf.Max(1f, siteSpec.Logistics.CreditCost);
            }

            Component? airdropComp = FindAirdropComponent(unit);
            if (airdropComp != null && AirdropSpecProp?.GetValue(airdropComp) is ModularUnitSpec dropSpec)
            {
                return Mathf.Max(1f, dropSpec.Logistics.CreditCost);
            }

            string? key = unit.definition != null ? unit.definition.jsonKey : null;
            if (!string.IsNullOrEmpty(key))
            {
                string? unitId = null;
                if (key!.StartsWith(BuildDefKeyPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    unitId = key.Substring(BuildDefKeyPrefix.Length);
                }
                else if (key.StartsWith("mds_cargo_", StringComparison.OrdinalIgnoreCase))
                {
                    unitId = key.Substring("mds_cargo_".Length);
                }
                else if (key.StartsWith("mds_", StringComparison.OrdinalIgnoreCase) &&
                         !key.StartsWith("mds_missile_", StringComparison.OrdinalIgnoreCase))
                {
                    unitId = key.Substring("mds_".Length);
                }

                if (!string.IsNullOrEmpty(unitId) &&
                    ContractorRegistry.TryGetUnitSpec(unitId!, out _, out ModularUnitSpec regSpec) &&
                    regSpec != null)
                {
                    return Mathf.Max(1f, regSpec.Logistics.CreditCost);
                }
            }

            return null;
        }

        private static Component? FindAirdropComponent(Unit? unit)
        {
            if (unit == null)
            {
                return null;
            }

            if (AirdropBehaviorType != null)
            {
                return unit.GetComponent(AirdropBehaviorType);
            }

            MonoBehaviour[] behaviours = unit.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != null && string.Equals(behaviours[i].GetType().FullName, AirdropBehaviorTypeName, StringComparison.Ordinal))
                {
                    return behaviours[i];
                }
            }

            return null;
        }

        internal void SetMdsSiteHoldFire(Unit unit, bool holdFire)
        {
            if (unit == null)
            {
                return;
            }

            Component? siteComp = FindModularSiteComponent(unit);
            if (siteComp != null)
            {
                SiteSetHoldFireMethod?.Invoke(siteComp, new object[] { holdFire });
            }

            GroundControlReflection.SetUnitHoldFireStance(unit, holdFire);
        }

        private static Component? FindModularSiteComponent(Unit? unit)
        {
            if (unit == null)
            {
                return null;
            }

            if (ModularSiteBehaviorType != null)
            {
                return unit.GetComponent(ModularSiteBehaviorType);
            }

            MonoBehaviour[] behaviours = unit.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != null && string.Equals(behaviours[i].GetType().FullName, ModularSiteBehaviorTypeName, StringComparison.Ordinal))
                {
                    return behaviours[i];
                }
            }

            return null;
        }

        internal void SetContainerTransitState(Unit unit, bool inTransit)
        {
            if (unit == null)
            {
                return;
            }

            if (unit is Container container && ((NetworkBehaviour)container).IsServer)
            {
                container.Networkkinematic = inTransit;
            }

            MonoBehaviour[] behaviours = unit.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour? mb = behaviours[i];
                if (mb == null)
                {
                    continue;
                }

                string? typeName = mb.GetType().FullName;
                if (string.Equals(typeName, AirdropBehaviorTypeName, StringComparison.Ordinal))
                {
                    mb.enabled = !inTransit;
                }
                else if (string.Equals(typeName, ModularSiteBehaviorTypeName, StringComparison.Ordinal))
                {
                    SiteSuspendedInTransitField?.SetValue(mb, inTransit);
                    SiteRequestDeployMethod?.Invoke(mb, new object[] { !inTransit });
                }
            }
        }

        private void EnsureBuildCatalogSynced()
        {
            Encyclopedia? enc = Encyclopedia.i;
            if (enc == null)
            {
                return;
            }

            if (!ReferenceEquals(cachedEnc, enc))
            {
                buildDefsByUnitId.Clear();
                metaByBuildDef.Clear();
                cachedEnc = enc;
            }

            BuildingDefinition? plain = null;
            IReadOnlyCollection<ContractorPackage> packages = ContractorRegistry.GetPackages();
            foreach (ContractorPackage pkg in packages)
            {
                if (pkg == null || pkg.Units == null)
                {
                    continue;
                }

                for (int i = 0; i < pkg.Units.Count; i++)
                {
                    ModularUnitSpec spec = pkg.Units[i];
                    if (spec == null || string.IsNullOrEmpty(spec.UnitId))
                    {
                        continue;
                    }

                    if (buildDefsByUnitId.TryGetValue(spec.UnitId, out BuildingDefinition? existing) && existing != null)
                    {
                        existing.value = Mathf.Max(1f, spec.Logistics.CreditCost);
                        metaByBuildDef[existing] = (pkg, spec);
                        continue;
                    }

                    plain ??= RtsBuildingCatalog.PickPlainBuildingDefinition();
                    if (plain == null)
                    {
                        return;
                    }

                    BuildingDefinition clone = UnityEngine.Object.Instantiate(plain);
                    string key = BuildDefKeyPrefix + spec.UnitId;
                    clone.name = key;
                    clone.jsonKey = key;
                    clone.unitName = spec.DisplayName;
                    clone.code = "MDS";
                    clone.value = Mathf.Max(1f, spec.Logistics.CreditCost);
                    clone.buildingType = (spec.Role == WeaponCascadeRole.FireControl ||
                                          spec.Role == WeaponCascadeRole.EarlyWarningRadar ||
                                          spec.Role == WeaponCascadeRole.CommandControl)
                        ? BuildingType.RDR
                        : BuildingType.DEF;
                    clone.spawnOffset = Vector3.zero;
                    clone.hideFlags = HideFlags.DontUnloadUnusedAsset;

                    if (plain is INetworkDefinition srcNet && clone is INetworkDefinition dstNet)
                    {
                        dstNet.LookupIndex = srcNet.LookupIndex;
                    }

                    buildDefsByUnitId[spec.UnitId] = clone;
                    metaByBuildDef[clone] = (pkg, spec);
                }
            }
        }

        private bool TryResolveBuildMeta(
            BuildingDefinition def,
            out ContractorPackage package,
            out ModularUnitSpec spec)
        {
            if (metaByBuildDef.TryGetValue(def, out (ContractorPackage Pkg, ModularUnitSpec Spec) found))
            {
                package = found.Pkg;
                spec = found.Spec;
                return true;
            }

            if (!string.IsNullOrEmpty(def.jsonKey)
                && def.jsonKey.StartsWith(BuildDefKeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string unitId = def.jsonKey.Substring(BuildDefKeyPrefix.Length);
                if (ContractorRegistry.TryGetUnitSpec(unitId, out ContractorPackage pkg, out ModularUnitSpec unitSpec))
                {
                    metaByBuildDef[def] = (pkg, unitSpec);
                    package = pkg;
                    spec = unitSpec;
                    return true;
                }
            }

            package = null!;
            spec = null!;
            return false;
        }

        private static bool TryResolveSpecFromAirdropMount(
            WeaponMount mount,
            out ContractorPackage package,
            out ModularUnitSpec spec)
        {
            if (!string.IsNullOrEmpty(mount.jsonKey)
                && mount.jsonKey.StartsWith(AirdropMountPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string unitId = mount.jsonKey.Substring(AirdropMountPrefix.Length);
                if (ContractorRegistry.TryGetUnitSpec(unitId, out package, out spec))
                {
                    return true;
                }
            }

            package = null!;
            spec = null!;
            return false;
        }

        private static bool IsContainerCargoBay(HardpointSet hs, out bool hasFullContainerOption)
        {
            hasFullContainerOption = false;
            bool hasAnyContainer = false;
            bool isCombinedName = !string.IsNullOrEmpty(hs.name)
                && hs.name.IndexOf("Combined", StringComparison.OrdinalIgnoreCase) >= 0;

            for (int i = 0; i < hs.weaponOptions.Count; i++)
            {
                WeaponMount? opt = hs.weaponOptions[i];
                if (opt == null || !opt.Cargo || opt.prefab == null)
                {
                    continue;
                }

                MountedCargo[] cargos = opt.prefab.GetComponentsInChildren<MountedCargo>(true);
                for (int c = 0; c < cargos.Length; c++)
                {
                    MountedCargo? mc = cargos[c];
                    if (mc == null || mc.cargo == null || mc.cargo.unitPrefab == null)
                    {
                        continue;
                    }

                    if (mc.cargo.unitPrefab.GetComponent<Container>() == null)
                    {
                        continue;
                    }

                    hasAnyContainer = true;
                    string combined = $"{opt.jsonKey} {opt.mountName} {mc.cargo.jsonKey} {mc.cargo.unitName}";
                    bool isSmallFuelDrum =
                        combined.IndexOf("Fuel", StringComparison.OrdinalIgnoreCase) >= 0
                        || combined.IndexOf("1500L", StringComparison.OrdinalIgnoreCase) >= 0
                        || combined.IndexOf("6000L", StringComparison.OrdinalIgnoreCase) >= 0
                        || combined.IndexOf("6,000L", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isSmallFuelDrum)
                    {
                        continue;
                    }

                    if (isCombinedName
                        || combined.IndexOf("Container", StringComparison.OrdinalIgnoreCase) >= 0
                        || combined.IndexOf("RAM45", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hasFullContainerOption = true;
                    }
                }
            }

            return hasAnyContainer && hasFullContainerOption;
        }
    }
}
#endif
