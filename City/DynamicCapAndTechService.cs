using System;
using System.Collections;
using System.Collections.Generic;
using Mirage;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal readonly struct FactionCapSnapshot
{
    internal FactionCapSnapshot(
        int groundUsed,
        int groundCap,
        int airUsed,
        int airCap,
        int navalUsed,
        int navalCap,
        int techTier,
        int commandHqLevel,
        int bestPilotAcademyLevel,
        int bestNavalCommandLevel,
        int commandHqCount,
        int barracksCount,
        int pilotAcademyCount,
        int logisticsCampCount,
        int navalCommandCount,
        int abmPlantCount,
        int abmRadarCount,
        int abmSiloCount)
    {
        GroundUsed = groundUsed;
        GroundCap = groundCap;
        AirUsed = airUsed;
        AirCap = airCap;
        NavalUsed = navalUsed;
        NavalCap = navalCap;
        TechTier = techTier;
        CommandHqLevel = commandHqLevel;
        BestPilotAcademyLevel = bestPilotAcademyLevel;
        BestNavalCommandLevel = bestNavalCommandLevel;
        CommandHqCount = commandHqCount;
        BarracksCount = barracksCount;
        PilotAcademyCount = pilotAcademyCount;
        LogisticsCampCount = logisticsCampCount;
        NavalCommandCount = navalCommandCount;
        AbmPlantCount = abmPlantCount;
        AbmRadarCount = abmRadarCount;
        AbmSiloCount = abmSiloCount;
    }

    internal int GroundUsed { get; }
    internal int GroundCap { get; }
    internal int AirUsed { get; }
    internal int AirCap { get; }
    internal int NavalUsed { get; }
    internal int NavalCap { get; }
    internal int TotalUsed => GroundUsed + AirUsed + NavalUsed;
    internal int TotalCap => GroundCap + AirCap + NavalCap;
    internal int TechTier { get; }
    internal int CommandHqLevel { get; }
    internal int BestPilotAcademyLevel { get; }
    internal int BestNavalCommandLevel { get; }
    internal int CommandHqCount { get; }
    internal int BarracksCount { get; }
    internal int PilotAcademyCount { get; }
    internal int LogisticsCampCount { get; }
    internal int NavalCommandCount { get; }
    internal int AbmPlantCount { get; }
    internal int AbmRadarCount { get; }
    internal int AbmSiloCount { get; }

    internal int GetKindCount(RtsBuildingKind kind) => kind switch
    {
        RtsBuildingKind.CommandHq => CommandHqCount,
        RtsBuildingKind.Barracks => BarracksCount,
        RtsBuildingKind.PilotAcademy => PilotAcademyCount,
        RtsBuildingKind.LogisticsCamp => LogisticsCampCount,
        RtsBuildingKind.NavalCommand => NavalCommandCount,
        RtsBuildingKind.AbmPlant => AbmPlantCount,
        RtsBuildingKind.AbmRadar => AbmRadarCount,
        RtsBuildingKind.AbmSilo => AbmSiloCount,
        _ => 0,
    };
}

internal sealed class RtsBuildingRecord
{
    internal Unit Unit { get; set; } = null!;
    internal FactionHQ Hq { get; set; } = null!;
    internal RtsBuildingSpec Spec { get; set; } = null!;
    internal int Level { get; set; } = 1;
}

internal sealed class DynamicCapAndTechService : IAddonTick, IAddonResetSession
{
    private const float SnapshotTtlSeconds = 0.5f;
    private const float AdoptScanIntervalSeconds = 2.0f;
    private const float LogisticsAuraIntervalSeconds = 3.0f;
    private const float AiCityReviewCooldownSeconds = 8.0f;
    private const float NoticeCooldownSeconds = 15.0f;
    private const int ArtilleryVehicleTypeInt = 5;

    internal static DynamicCapAndTechService Instance { get; } = new();

    private readonly Dictionary<Unit, RtsBuildingRecord> records = new();
    private readonly List<Unit> pruneScratch = new();
    private readonly List<RtsBuildingRecord> ownedScratch = new();
    private readonly List<Airbase> safeBaseScratch = new();
    private readonly Dictionary<FactionHQ, (FactionCapSnapshot Snap, float ExpiresAt)> snapshotCache = new();
    private readonly Dictionary<FactionHQ, float> aiNextReviewAt = new();
    private readonly Dictionary<string, float> noticeCooldowns = new(StringComparer.Ordinal);
    private readonly Dictionary<Unit, float> logisticsHealedAt = new();
    private readonly List<Unit> logisticsCleanScratch = new();

    private float nextAdoptScanAt;
    private float nextLogisticsAuraAt;

    internal static bool IsActive => AddonConfig.ModEnabled.Value && AddonConfig.EnableDynamicCaps.Value;

    private DynamicCapAndTechService()
    {
    }

    public void Tick()
    {
        if (!AddonConfig.ModEnabled.Value)
        {
            return;
        }

        float now = Time.unscaledTime;
        PruneDeadRecords();

        if (now >= nextAdoptScanAt)
        {
            nextAdoptScanAt = now + AdoptScanIntervalSeconds;
            AdoptExistingCustomBuildings();
        }

        if (IsActive && now >= nextLogisticsAuraAt)
        {
            nextLogisticsAuraAt = now + LogisticsAuraIntervalSeconds;
            TickLogisticsAura(now);
        }
    }

    public void ResetSession()
    {
        records.Clear();
        pruneScratch.Clear();
        ownedScratch.Clear();
        safeBaseScratch.Clear();
        snapshotCache.Clear();
        aiNextReviewAt.Clear();
        noticeCooldowns.Clear();
        logisticsHealedAt.Clear();
        logisticsCleanScratch.Clear();
        nextAdoptScanAt = 0f;
        nextLogisticsAuraAt = 0f;
        RtsBuildingCatalog.ResetSession();
    }

    internal void OnBuildingSpawned(FactionHQ hq, BuildingDefinition? def, Unit? spawned)
    {
        if (spawned == null || spawned.disabled || hq == null)
        {
            return;
        }

        if (!RtsBuildingCatalog.TryGetSpec(def, out RtsBuildingSpec spec))
        {
            return;
        }

        RegisterBuilding(spawned, hq, spec);
        AddonLog.Info($"[City] {hq.faction?.factionName ?? "Faction"} completed {spec.DisplayName} (Lvl 1).");
    }

    internal FactionCapSnapshot GetSnapshot(FactionHQ? hq)
    {
        if (hq == null)
        {
            return new FactionCapSnapshot(
                0,
                AddonConfig.StartGroundCap.Value,
                0,
                AddonConfig.StartAirCap.Value,
                0,
                AddonConfig.StartNavalCap.Value,
                1,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0);
        }

        float now = Time.unscaledTime;
        if (snapshotCache.TryGetValue(hq, out (FactionCapSnapshot Snap, float ExpiresAt) cached) && now < cached.ExpiresAt)
        {
            return cached.Snap;
        }

        FactionCapSnapshot computed = ComputeSnapshot(hq);
        snapshotCache[hq] = (computed, now + SnapshotTtlSeconds);
        return computed;
    }

    internal void InvalidateCache(FactionHQ? hq = null)
    {
        if (hq != null)
        {
            snapshotCache.Remove(hq);
        }
        else
        {
            snapshotCache.Clear();
        }
    }

    internal void GetOwnedBuildings(FactionHQ hq, List<RtsBuildingRecord> results)
    {
        results.Clear();
        foreach (RtsBuildingRecord rec in records.Values)
        {
            if (rec.Unit != null && !rec.Unit.disabled && ReferenceEquals(rec.Hq, hq))
            {
                results.Add(rec);
            }
        }

        results.Sort((left, right) =>
        {
            int kindCmp = left.Spec.Kind.CompareTo(right.Spec.Kind);
            if (kindCmp != 0)
            {
                return kindCmp;
            }

            return right.Level.CompareTo(left.Level);
        });
    }

    internal void GetOwnedBuildingsOfKinds(FactionHQ hq, bool abmOnly, List<RtsBuildingRecord> results)
    {
        results.Clear();
        foreach (RtsBuildingRecord rec in records.Values)
        {
            if (rec.Unit != null
                && !rec.Unit.disabled
                && ReferenceEquals(rec.Hq, hq)
                && rec.Spec.IsAbmInfrastructure == abmOnly)
            {
                results.Add(rec);
            }
        }

        results.Sort((left, right) =>
        {
            int kindCmp = left.Spec.Kind.CompareTo(right.Spec.Kind);
            if (kindCmp != 0)
            {
                return kindCmp;
            }

            return right.Level.CompareTo(left.Level);
        });
    }

    internal int CountInProgressBlueprints(FactionHQ hq, RtsBuildingKind kind)
    {
        IReadOnlyList<object>? bps = GroundControlReflection.GetActiveBlueprints();
        if (bps == null || bps.Count == 0)
        {
            return 0;
        }

        RtsBuildingSpec targetSpec = RtsBuildingCatalog.GetSpec(kind);
        int count = 0;
        for (int i = 0; i < bps.Count; i++)
        {
            if (GroundControlReflection.TryGetBlueprintInfo(bps[i], out FactionHQ? bpHq, out BuildingDefinition? bpDef)
                && ReferenceEquals(bpHq, hq)
                && bpDef != null
                && string.Equals(bpDef.jsonKey, targetSpec.JsonKey, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    internal bool CanStartBuilding(FactionHQ hq, RtsBuildingKind kind, out string reason)
    {
        if (!((NetworkBehaviour)hq).IsServer)
        {
            reason = "Host-only building placement.";
            return false;
        }

        RtsBuildingSpec spec = RtsBuildingCatalog.GetSpec(kind);
        bool ignoreCaps = SingleplayerCheatService.IsIgnoreCapsActiveFor(hq);
        FactionCapSnapshot snap = GetSnapshot(hq);
        if (IsActive && spec.RequiredTechTier > 1 && snap.TechTier < spec.RequiredTechTier)
        {
            reason = $"Requires Command HQ Lvl {spec.RequiredTechTier} (Tech Tier {spec.RequiredTechTier}).";
            return false;
        }

        if (!ignoreCaps)
        {
            int totalOwnedAndQueued = snap.GetKindCount(kind) + CountInProgressBlueprints(hq, kind);
            if (totalOwnedAndQueued >= spec.MaxPerFaction)
            {
                reason = $"Max {spec.MaxPerFaction} {spec.DisplayName} reached.";
                return false;
            }
        }

        if (hq.factionFunds < spec.BuildCost)
        {
            reason = $"Need {spec.BuildCost:0} Cr ({hq.factionFunds:0} Cr available).";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    internal bool CanUpgradeBuilding(RtsBuildingRecord rec, out string reason)
    {
        if (rec.Unit == null || rec.Unit.disabled)
        {
            reason = "Building destroyed.";
            return false;
        }

        if (!((NetworkBehaviour)rec.Hq).IsServer)
        {
            reason = "Host-only upgrade.";
            return false;
        }

        if (rec.Level >= 3)
        {
            reason = "MAX LEVEL";
            return false;
        }

        int nextLevel = rec.Level + 1;
        FactionCapSnapshot snap = GetSnapshot(rec.Hq);
        if (rec.Spec.Kind != RtsBuildingKind.CommandHq && snap.TechTier < nextLevel)
        {
            reason = $"Requires Command HQ Lvl {nextLevel}";
            return false;
        }

        float cost = rec.Spec.GetUpgradeCost(rec.Level);
        if (rec.Hq.factionFunds < cost)
        {
            reason = $"Need {cost:0} Cr";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    internal bool TryUpgradeBuilding(RtsBuildingRecord rec, out string message)
    {
        if (!CanUpgradeBuilding(rec, out string block))
        {
            message = block;
            return false;
        }

        float cost = rec.Spec.GetUpgradeCost(rec.Level);
        if (!GroundControlReflection.TrySpend(rec.Hq, cost))
        {
            message = $"Insufficient funds ({cost:0} Cr needed).";
            return false;
        }

        rec.Level++;
        RtsBuildingCatalog.ApplyCustomVisual(rec.Unit, rec.Spec, rec.Level, forceReplace: true);
        InvalidateCache(rec.Hq);
        message = $"{rec.Spec.DisplayName} upgraded to Level {rec.Level}.";
        AddonLog.Info($"[City] {rec.Hq.faction?.factionName ?? "Faction"}: {message}");
        return true;
    }

    internal void RefreshAllBuildingVisuals()
    {
        foreach (RtsBuildingRecord rec in records.Values)
        {
            if (rec.Unit != null && !rec.Unit.disabled)
            {
                RtsBuildingCatalog.ApplyCustomVisual(rec.Unit, rec.Spec, rec.Level, forceReplace: true);
            }
        }
    }

    internal bool IsFullTotal(FactionHQ hq, bool tellPlayer)
    {
        if (SingleplayerCheatService.IsIgnoreCapsActiveFor(hq))
        {
            return false;
        }

        FactionCapSnapshot snap = GetSnapshot(hq);
        if (snap.TotalUsed < snap.TotalCap)
        {
            return false;
        }

        if (tellPlayer)
        {
            EmitFactionNotice(
                hq,
                "total_cap",
                $"TOTAL CAP REACHED: {snap.TotalUsed}/{snap.TotalCap} (Build City Infrastructure · Shift+C)",
                $"{hq.faction?.factionName} reached total cap ({snap.TotalUsed}/{snap.TotalCap}).");
        }

        return true;
    }

    internal bool IsGroundFull(FactionHQ hq, bool tellPlayer = false)
    {
        if (SingleplayerCheatService.IsIgnoreCapsActiveFor(hq))
        {
            return false;
        }

        FactionCapSnapshot snap = GetSnapshot(hq);
        if (snap.GroundUsed < snap.GroundCap)
        {
            return false;
        }

        if (tellPlayer)
        {
            EmitFactionNotice(
                hq,
                "ground_cap",
                $"GROUND CAP REACHED: {snap.GroundUsed}/{snap.GroundCap} (Build Barracks/Logistics · Shift+C)",
                $"{hq.faction?.factionName} reached ground cap ({snap.GroundUsed}/{snap.GroundCap}).");
        }

        return true;
    }

    internal bool IsAirFull(FactionHQ hq, bool tellPlayer = false)
    {
        if (SingleplayerCheatService.IsIgnoreCapsActiveFor(hq))
        {
            return false;
        }

        FactionCapSnapshot snap = GetSnapshot(hq);
        if (snap.AirUsed < snap.AirCap)
        {
            return false;
        }

        if (tellPlayer)
        {
            EmitFactionNotice(
                hq,
                "air_cap",
                $"AIR CAP REACHED: {snap.AirUsed}/{snap.AirCap} (Build Pilot Academy · Shift+C)",
                $"{hq.faction?.factionName} reached air cap ({snap.AirUsed}/{snap.AirCap}).");
        }

        return true;
    }

    internal bool TryGetSpawnBlockReason(
        FactionHQ? hq,
        UnitDefinition? def,
        bool tellPlayer,
        out string reason)
    {
        reason = string.Empty;
        if (!IsActive || hq == null || def == null || SingleplayerCheatService.IsIgnoreCapsActiveFor(hq))
        {
            return false;
        }

        FactionCapSnapshot snap = GetSnapshot(hq);

        if (def is VehicleDefinition vdef)
        {
            // Never soft-lock a faction that lost its construction engineers.
            if (GroundControlReflection.IsBuilderVehicle(vdef) && CountLiveBuilders(hq) < 2)
            {
                return false;
            }

            int reqDepot = GroundControlReflection.GetRequiredDepotLevel(vdef);
            if (reqDepot >= 3 && snap.TechTier < 2)
            {
                reason = $"TECH TIER 2 REQUIRED (Upgrade Command HQ Lvl 2) for {def.unitName}";
                NotifyIfWanted(hq, "tier_veh", reason, tellPlayer);
                return true;
            }

            if ((int)vdef.vehicleType == ArtilleryVehicleTypeInt)
            {
                int artCap = GroundControlReflection.GetConfiguredArtilleryCap();
                if (artCap > 0 && CountArtillery(hq) >= artCap)
                {
                    reason = $"ARTILLERY CAP REACHED: {artCap}";
                    NotifyIfWanted(hq, "arty_cap", reason, tellPlayer);
                    return true;
                }
            }

            if (snap.GroundUsed >= snap.GroundCap)
            {
                reason = $"GROUND CAP REACHED: {snap.GroundUsed}/{snap.GroundCap} — Build Barracks (Shift+C)";
                NotifyIfWanted(hq, "ground_cap", reason, tellPlayer);
                return true;
            }

            return false;
        }

        if (def is AircraftDefinition adef)
        {
            int reqTier = GetRequiredAircraftTechTier(adef);
            if (reqTier == 3 && (snap.TechTier < 3 || snap.BestPilotAcademyLevel < 2))
            {
                reason = $"LOCKED: {def.unitName} needs Command HQ Lvl 3 + Pilot Academy Lvl 2";
                NotifyIfWanted(hq, "tier_air3", reason, tellPlayer);
                return true;
            }

            if (reqTier == 2 && (snap.TechTier < 2 || snap.BestPilotAcademyLevel < 1))
            {
                reason = $"LOCKED: {def.unitName} needs Command HQ Lvl 2 + Pilot Academy Lvl 1";
                NotifyIfWanted(hq, "tier_air2", reason, tellPlayer);
                return true;
            }

            if (snap.AirUsed >= snap.AirCap)
            {
                reason = $"AIR CAP REACHED: {snap.AirUsed}/{snap.AirCap} — Build Pilot Academy (Shift+C)";
                NotifyIfWanted(hq, "air_cap", reason, tellPlayer);
                return true;
            }

            return false;
        }

        if (def is ShipDefinition sdef)
        {
            int reqTier = GetRequiredShipTechTier(sdef);
            if (reqTier == 3 && (snap.TechTier < 3 || snap.BestNavalCommandLevel < 2))
            {
                reason = $"LOCKED: {def.unitName} needs Command HQ Lvl 3 + Naval Command Lvl 2";
                NotifyIfWanted(hq, "tier_nav3", reason, tellPlayer);
                return true;
            }

            if (reqTier == 2 && (snap.TechTier < 2 || snap.BestNavalCommandLevel < 1))
            {
                reason = $"LOCKED: {def.unitName} needs Command HQ Lvl 2 + Naval Command Lvl 1";
                NotifyIfWanted(hq, "tier_nav2", reason, tellPlayer);
                return true;
            }

            if (snap.NavalUsed >= snap.NavalCap)
            {
                reason = $"NAVAL CAP REACHED: {snap.NavalUsed}/{snap.NavalCap} — Build Naval Command (Shift+C)";
                NotifyIfWanted(hq, "naval_cap", reason, tellPlayer);
                return true;
            }
        }

        return false;
    }

    internal static int GetRequiredShipTechTier(ShipDefinition def)
    {
        int dockLvl = GroundControlReflection.GetRequiredDockLevel(def);
        if (dockLvl > 0)
        {
            return Mathf.Clamp(dockLvl, 1, 3);
        }

        if (def.value > 1400f)
        {
            return 3;
        }

        return def.value > 200f ? 2 : 1;
    }

    internal static int GetRequiredAircraftTechTier(AircraftDefinition def)
    {
        if (GroundControlReflection.IsRotaryAirframe(def))
        {
            return 1;
        }

        if (def.value > 95f)
        {
            return 3;
        }

        return def.value > 45f ? 2 : 1;
    }

    internal void TickAiCityDecision(object ecoInstance, FactionHQ hq)
    {
        if (!IsActive || !AddonConfig.EnableAiCityBuilding.Value || hq == null || !((NetworkBehaviour)hq).IsServer)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (aiNextReviewAt.TryGetValue(hq, out float nextAt) && now < nextAt)
        {
            return;
        }

        aiNextReviewAt[hq] = now + AiCityReviewCooldownSeconds;

        FactionCapSnapshot snap = GetSnapshot(hq);
        GetOwnedBuildings(hq, ownedScratch);

        // 1. Ensure a Command HQ exists first (before MDS/City expansion so TechTier never stays locked at 1)
        if (snap.CommandHqCount == 0 && CountInProgressBlueprints(hq, RtsBuildingKind.CommandHq) == 0)
        {
            if (TryQueueAiCityBuilding(ecoInstance, hq, RtsBuildingKind.CommandHq, minReserve: 180f, isUrgentCapRelief: false))
            {
                return;
            }
        }

        // 1b. Faction-appropriate modular air defence / contractor batteries via MDSFramework
        MdsBridgeBootstrap.TickAiMdsDeploymentAction?.Invoke(hq);

        // 2. Expand ground capacity when close to ground cap or if no Barracks yet
        bool urgentGround = snap.GroundUsed >= snap.GroundCap - 2;
        bool needGroundRoom = snap.GroundUsed >= Mathf.Max(8, snap.GroundCap - 3)
            || (snap.BarracksCount == 0 && snap.GroundUsed >= 8);
        if (needGroundRoom)
        {
            if (TryUpgradeAiBuilding(ecoInstance, hq, RtsBuildingKind.Barracks, snap.TechTier, reserveFactor: 1.5f, isUrgentCapRelief: urgentGround))
            {
                return;
            }

            if (CountInProgressBlueprints(hq, RtsBuildingKind.Barracks) == 0
                && TryQueueAiCityBuilding(ecoInstance, hq, RtsBuildingKind.Barracks, minReserve: 160f, isUrgentCapRelief: urgentGround))
            {
                return;
            }
        }

        // 3. Push Command HQ -> Lvl 2 cleanly once Barracks exists (or funds are healthy)
        if (snap.CommandHqLevel == 1
            && ((snap.BarracksCount >= 1 && hq.factionFunds >= 350f) || hq.factionFunds >= 480f))
        {
            if (TryUpgradeAiBuilding(ecoInstance, hq, RtsBuildingKind.CommandHq, snap.TechTier, reserveFactor: 1.15f, isUrgentCapRelief: false))
            {
                return;
            }
        }

        // 4. Pilot Academy (Tier 1 unlocked — build early when air cap tightens or HQ exists, upgrade at Tier 2+)
        bool urgentAir = snap.AirUsed >= snap.AirCap;
        bool needFirstAcademy = snap.PilotAcademyCount == 0
            && (snap.AirUsed >= Mathf.Max(1, snap.AirCap - 1) || snap.CommandHqCount >= 1 || snap.TechTier >= 2);
        bool needMoreAir = snap.TechTier >= 2
            && (snap.AirUsed >= Mathf.Max(1, snap.AirCap - 1) || (snap.TechTier >= 3 && snap.BestPilotAcademyLevel < 2));
        if (needFirstAcademy || needMoreAir)
        {
            if (snap.TechTier >= 2
                && TryUpgradeAiBuilding(ecoInstance, hq, RtsBuildingKind.PilotAcademy, snap.TechTier, reserveFactor: 1.2f, isUrgentCapRelief: urgentAir))
            {
                return;
            }

            bool canBuildAnotherAcademy = snap.PilotAcademyCount == 0
                || (snap.AirUsed >= snap.AirCap - 1 && snap.BestPilotAcademyLevel >= snap.TechTier);
            if (canBuildAnotherAcademy
                && CountInProgressBlueprints(hq, RtsBuildingKind.PilotAcademy) == 0
                && TryQueueAiCityBuilding(ecoInstance, hq, RtsBuildingKind.PilotAcademy, minReserve: urgentAir ? 180f : 220f, isUrgentCapRelief: urgentAir))
            {
                return;
            }
        }

        // 5. Logistics Camp to boost all caps and support forward forces
        if (snap.GroundUsed >= 16 && snap.LogisticsCampCount == 0
            && CountInProgressBlueprints(hq, RtsBuildingKind.LogisticsCamp) == 0)
        {
            if (TryQueueAiCityBuilding(ecoInstance, hq, RtsBuildingKind.LogisticsCamp, minReserve: 240f, isUrgentCapRelief: false))
            {
                return;
            }
        }

        // 6. Naval Command when AI operates naval infrastructure/vessels
        if (snap.TechTier >= 2 && HasNavalPresence(hq, snap))
        {
            bool urgentNaval = snap.NavalUsed >= snap.NavalCap;
            if ((snap.NavalUsed >= Mathf.Max(1, snap.NavalCap - 1)
                    || (snap.TechTier >= 3 && snap.BestNavalCommandLevel < 2))
                && TryUpgradeAiBuilding(ecoInstance, hq, RtsBuildingKind.NavalCommand, snap.TechTier, reserveFactor: 1.35f, isUrgentCapRelief: urgentNaval))
            {
                return;
            }

            if (snap.NavalCommandCount == 0
                && CountInProgressBlueprints(hq, RtsBuildingKind.NavalCommand) == 0
                && TryQueueAiCityBuilding(ecoInstance, hq, RtsBuildingKind.NavalCommand, minReserve: 260f, isUrgentCapRelief: urgentNaval))
            {
                return;
            }
        }

        // 7. Late-game Command HQ -> Lvl 3 when established
        if (snap.CommandHqLevel == 2
            && snap.BarracksCount >= 1
            && snap.PilotAcademyCount >= 1
            && hq.factionFunds >= 700f)
        {
            TryUpgradeAiBuilding(ecoInstance, hq, RtsBuildingKind.CommandHq, snap.TechTier, reserveFactor: 1.15f, isUrgentCapRelief: false);
        }

        // 8. Late-game AI Tier-3 ABM site (max 1 Plant, 1 Radar, 1 Silo built by M12 Jackknife)
        if (AddonConfig.AllowAiAbmSite.Value && snap.TechTier >= 3 && hq.factionFunds >= 550f)
        {
            if (snap.AbmPlantCount == 0
                && CountInProgressBlueprints(hq, RtsBuildingKind.AbmPlant) == 0
                && TryQueueAiCityBuilding(ecoInstance, hq, RtsBuildingKind.AbmPlant, minReserve: 380f, isUrgentCapRelief: false))
            {
                return;
            }

            if (snap.AbmRadarCount == 0
                && CountInProgressBlueprints(hq, RtsBuildingKind.AbmRadar) == 0
                && TryQueueAiCityBuilding(ecoInstance, hq, RtsBuildingKind.AbmRadar, minReserve: 280f, isUrgentCapRelief: false))
            {
                return;
            }

            if (snap.AbmSiloCount == 0
                && CountInProgressBlueprints(hq, RtsBuildingKind.AbmSilo) == 0
                && TryQueueAiCityBuilding(ecoInstance, hq, RtsBuildingKind.AbmSilo, minReserve: 350f, isUrgentCapRelief: false))
            {
                return;
            }
        }
    }

    private bool TryUpgradeAiBuilding(
        object ecoInstance,
        FactionHQ hq,
        RtsBuildingKind kind,
        int techTier,
        float reserveFactor,
        bool isUrgentCapRelief = false)
    {
        for (int i = 0; i < ownedScratch.Count; i++)
        {
            RtsBuildingRecord rec = ownedScratch[i];
            if (rec.Spec.Kind != kind || rec.Level >= 3)
            {
                continue;
            }

            int targetLevel = rec.Level + 1;
            if (kind != RtsBuildingKind.CommandHq && techTier < targetLevel)
            {
                continue;
            }

            float cost = rec.Spec.GetUpgradeCost(rec.Level);
            if (hq.factionFunds < cost * reserveFactor)
            {
                continue;
            }

            if (!AiStrategicDirectorService.Instance.CanSpendOnCityBuilding(
                    ecoInstance,
                    hq,
                    kind,
                    cost,
                    isUpgrade: true,
                    isUrgentCapRelief: isUrgentCapRelief))
            {
                continue;
            }

            if (TryUpgradeBuilding(rec, out _))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryQueueAiCityBuilding(
        object ecoInstance,
        FactionHQ hq,
        RtsBuildingKind kind,
        float minReserve,
        bool isUrgentCapRelief = false)
    {
        RtsBuildingSpec spec = RtsBuildingCatalog.GetSpec(kind);
        if (hq.factionFunds < Mathf.Max(minReserve, spec.BuildCost))
        {
            return false;
        }

        if (!AiStrategicDirectorService.Instance.CanSpendOnCityBuilding(
                ecoInstance,
                hq,
                kind,
                spec.BuildCost,
                isUpgrade: false,
                isUrgentCapRelief: isUrgentCapRelief))
        {
            return false;
        }

        FactionCapSnapshot snap = GetSnapshot(hq);
        if (snap.GetKindCount(kind) >= spec.MaxPerFaction || !GroundControlReflection.HasAnyBlueprintRoom(hq))
        {
            return false;
        }

        BuildingDefinition? def = RtsBuildingCatalog.GetOrCreateDefinition(kind);
        if (def == null)
        {
            return false;
        }

        GlobalPosition site = default;
        bool found = false;

        if (AddonConfig.EnableAiSmartBaseLayout.Value)
        {
            FactionHQ? localHq = GroundControlReflection.GetLocalHq();
            FactionHQ? enemyHq = ReferenceEquals(localHq, hq) ? null : localHq;
            AiBattlefieldIntelService.Instance.GetAirbasesOrderedBySafety(hq, enemyHq, safeBaseScratch);
        }
        else
        {
            safeBaseScratch.Clear();
        }

        if (AddonConfig.EnableAiSmartBaseLayout.Value && safeBaseScratch.Count > 0)
        {
            bool isCoreTechOrAbm = kind == RtsBuildingKind.CommandHq
                || kind == RtsBuildingKind.PilotAcademy
                || kind == RtsBuildingKind.NavalCommand
                || kind == RtsBuildingKind.AbmPlant
                || kind == RtsBuildingKind.AbmRadar
                || kind == RtsBuildingKind.AbmSilo;

            // Core tech and ABM infrastructure belong at the safest rear base with >= 180 m spacing;
            // Barracks and Logistics Camps prefer mid/front bases as fallback forward staging.
            if (isCoreTechOrAbm)
            {
                for (int i = 0; i < safeBaseScratch.Count && !found; i++)
                {
                    if (TryFindDispersedBaseSite(ecoInstance, hq, def, safeBaseScratch[i], 180f, out site))
                    {
                        found = true;
                    }
                }

                for (int i = 0; i < safeBaseScratch.Count && !found; i++)
                {
                    Airbase ab = safeBaseScratch[i];
                    Transform anchorTf = ab.center != null ? ab.center : ab.transform;
                    if (GroundControlReflection.TryFindSiteNear(ecoInstance, hq, def, anchorTf.GlobalPosition(), out site, 16))
                    {
                        found = true;
                    }
                }
            }
            else
            {
                for (int i = safeBaseScratch.Count - 1; i >= 0 && !found; i--)
                {
                    if (TryFindDispersedBaseSite(ecoInstance, hq, def, safeBaseScratch[i], 180f, out site))
                    {
                        found = true;
                    }
                }

                for (int i = safeBaseScratch.Count - 1; i >= 0 && !found; i--)
                {
                    Airbase ab = safeBaseScratch[i];
                    Transform anchorTf = ab.center != null ? ab.center : ab.transform;
                    if (GroundControlReflection.TryFindSiteNear(ecoInstance, hq, def, anchorTf.GlobalPosition(), out site, 16))
                    {
                        found = true;
                    }
                }
            }
        }
        else
        {
            foreach (Airbase ab in hq.GetAirbases())
            {
                if (ab == null || !GroundControlReflection.IsLandBase(ab))
                {
                    continue;
                }

                Transform anchorTf = ab.center != null ? ab.center : ab.transform;
                if (GroundControlReflection.TryFindSiteNear(ecoInstance, hq, def, anchorTf.GlobalPosition(), out site, 16))
                {
                    found = true;
                    break;
                }
            }
        }

        if (!found && !GroundControlReflection.TryFindEnemyBuildSite(ecoInstance, hq, def, ref site))
        {
            return false;
        }

        return GroundControlReflection.QueueEnemyStructureBlueprint(
            ecoInstance,
            hq,
            def,
            site,
            spec.BuildCost,
            spec.DisplayName);
    }

    private bool HasMinBuildingSpacing(FactionHQ hq, GlobalPosition candidate, float minSpacingMeters)
    {
        float minSq = minSpacingMeters * minSpacingMeters;
        foreach (RtsBuildingRecord rec in records.Values)
        {
            if (rec.Unit == null || rec.Unit.disabled || !ReferenceEquals(rec.Hq, hq))
            {
                continue;
            }

            if (FastMath.SquareDistance(rec.Unit.GlobalPosition(), candidate) < minSq)
            {
                return false;
            }
        }

        if (hq.factionUnits != null)
        {
            foreach (PersistentID pid in hq.factionUnits)
            {
                if (!pid.TryGetUnit(out Unit unit) || unit == null || unit.disabled || !(unit is Building))
                {
                    continue;
                }

                if (FastMath.SquareDistance(unit.GlobalPosition(), candidate) < minSq)
                {
                    return false;
                }
            }
        }

        if (GroundControlReflection.GetEcoInstance() is object eco
            && GroundControlReflection.EcoConstructionSitesField?.GetValue(eco) is IList sites)
        {
            for (int i = 0; i < sites.Count; i++)
            {
                object? site = sites[i];
                if (site == null
                    || GroundControlReflection.SiteHqProp?.GetValue(site) is not FactionHQ siteHq
                    || !ReferenceEquals(siteHq, hq)
                    || GroundControlReflection.SitePositionProp?.GetValue(site) is not GlobalPosition sitePos)
                {
                    continue;
                }

                if (FastMath.SquareDistance(sitePos, candidate) < minSq)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool TryFindDispersedBaseSite(
        object ecoInstance,
        FactionHQ hq,
        BuildingDefinition def,
        Airbase baseObj,
        float minSpacingMeters,
        out GlobalPosition site)
    {
        Transform anchorTf = baseObj.center != null ? baseObj.center : baseObj.transform;
        GlobalPosition baseAnchor = anchorTf.GlobalPosition();
        for (int attempt = 0; attempt < 6; attempt++)
        {
            float angle = (attempt * (Mathf.PI * 2f / 6f)) + UnityEngine.Random.Range(-0.25f, 0.25f);
            float offset = UnityEngine.Random.Range(minSpacingMeters * 1.15f, minSpacingMeters * 2.1f);
            var probeAnchor = new GlobalPosition(
                baseAnchor.x + Mathf.Cos(angle) * offset,
                baseAnchor.y,
                baseAnchor.z + Mathf.Sin(angle) * offset);

            if (GroundControlReflection.TryFindSiteNear(ecoInstance, hq, def, probeAnchor, out GlobalPosition candidate, 8)
                && HasMinBuildingSpacing(hq, candidate, minSpacingMeters)
                && !GroundControlReflection.BuildPreviewIsOnLevelRoad(candidate, 24f)
                && GroundControlReflection.CanDriveBetween(baseAnchor, candidate))
            {
                site = candidate;
                return true;
            }
        }

        site = default;
        return false;
    }

    private static bool HasNavalPresence(FactionHQ hq, FactionCapSnapshot snap)
    {
        if (snap.NavalUsed > 0 || GroundControlReflection.GetNavalDockLevel(hq) > 0)
        {
            return true;
        }

        for (int i = 0; i < UnitRegistry.allUnits.Count; i++)
        {
            Unit? u = UnitRegistry.allUnits[i];
            if (u != null
                && !u.disabled
                && ReferenceEquals(u.NetworkHQ, hq)
                && u is Building
                && u.UniqueName != null
                && u.UniqueName.StartsWith("Naval Dock", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void RegisterBuilding(Unit unit, FactionHQ hq, RtsBuildingSpec spec)
    {
        unit.unitName = spec.DisplayName;
        int level = records.TryGetValue(unit, out RtsBuildingRecord? existing) ? existing.Level : 1;
        RtsBuildingCatalog.ApplyCustomVisual(unit, spec, level, forceReplace: false);

        if (existing == null)
        {
            records[unit] = new RtsBuildingRecord
            {
                Unit = unit,
                Hq = hq,
                Spec = spec,
                Level = 1,
            };
        }

        InvalidateCache(hq);
    }

    private void AdoptExistingCustomBuildings()
    {
        for (int i = 0; i < UnitRegistry.allUnits.Count; i++)
        {
            Unit? u = UnitRegistry.allUnits[i];
            if (u == null || u.disabled || u.NetworkHQ == null || !(u is Building) || records.ContainsKey(u))
            {
                continue;
            }

            if ((u.definition is BuildingDefinition bd && RtsBuildingCatalog.TryGetSpec(bd, out RtsBuildingSpec spec))
                || RtsBuildingCatalog.TryMatchByUniqueName(u.UniqueName, out spec))
            {
                RegisterBuilding(u, u.NetworkHQ, spec);
            }
        }
    }

    private void PruneDeadRecords()
    {
        pruneScratch.Clear();
        foreach (KeyValuePair<Unit, RtsBuildingRecord> kv in records)
        {
            if (kv.Key == null || kv.Key.disabled || kv.Value.Hq == null)
            {
                pruneScratch.Add(kv.Key!);
            }
        }

        if (pruneScratch.Count == 0)
        {
            return;
        }

        for (int i = 0; i < pruneScratch.Count; i++)
        {
            Unit dead = pruneScratch[i];
            if (records.TryGetValue(dead, out RtsBuildingRecord? rec) && rec != null)
            {
                InvalidateCache(rec.Hq);
            }

            records.Remove(dead);
        }
    }

    private FactionCapSnapshot ComputeSnapshot(FactionHQ hq)
    {
        int groundUsed = 0;
        int airUsed = 0;
        int navalUsed = 0;

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
                    groundUsed++;
                }
                else if (unit is Aircraft aircraft && aircraft.Player == null)
                {
                    airUsed++;
                }
                else if (unit is Ship)
                {
                    navalUsed++;
                }
            }
        }

        if (MdsBridgeBootstrap.GetReservedCapOverride != null)
        {
            (int mdsGnd, int mdsAir, int mdsNav) = MdsBridgeBootstrap.GetReservedCapOverride(hq);
            groundUsed += mdsGnd;
            airUsed += mdsAir;
            navalUsed += mdsNav;
        }

        int groundBonus = 0;
        int airBonus = 0;
        int navalBonus = 0;
        int commandHqLevel = 0;
        int bestPilotAcademyLevel = 0;
        int bestNavalCommandLevel = 0;
        int commandHqCount = 0;
        int barracksCount = 0;
        int pilotAcademyCount = 0;
        int logisticsCampCount = 0;
        int navalCommandCount = 0;
        int abmPlantCount = 0;
        int abmRadarCount = 0;
        int abmSiloCount = 0;

        foreach (RtsBuildingRecord rec in records.Values)
        {
            if (rec.Unit == null || rec.Unit.disabled || !ReferenceEquals(rec.Hq, hq))
            {
                continue;
            }

            groundBonus += rec.Spec.GetGroundBonus(rec.Level);
            airBonus += rec.Spec.GetAirBonus(rec.Level);
            navalBonus += rec.Spec.GetNavalBonus(rec.Level);

            switch (rec.Spec.Kind)
            {
                case RtsBuildingKind.CommandHq:
                    commandHqCount++;
                    commandHqLevel = Mathf.Max(commandHqLevel, rec.Level);
                    break;
                case RtsBuildingKind.Barracks:
                    barracksCount++;
                    break;
                case RtsBuildingKind.PilotAcademy:
                    pilotAcademyCount++;
                    bestPilotAcademyLevel = Mathf.Max(bestPilotAcademyLevel, rec.Level);
                    break;
                case RtsBuildingKind.LogisticsCamp:
                    logisticsCampCount++;
                    break;
                case RtsBuildingKind.NavalCommand:
                    navalCommandCount++;
                    bestNavalCommandLevel = Mathf.Max(bestNavalCommandLevel, rec.Level);
                    break;
                case RtsBuildingKind.AbmPlant:
                    abmPlantCount++;
                    break;
                case RtsBuildingKind.AbmRadar:
                    abmRadarCount++;
                    break;
                case RtsBuildingKind.AbmSilo:
                    abmSiloCount++;
                    break;
            }
        }

        int techTier = Mathf.Clamp(commandHqLevel > 0 ? commandHqLevel : 1, 1, 3);
        if (SingleplayerCheatService.IsUnlockTier3ActiveFor(hq))
        {
            techTier = 3;
            commandHqLevel = Mathf.Max(commandHqLevel, 3);
            bestPilotAcademyLevel = Mathf.Max(bestPilotAcademyLevel, 2);
            bestNavalCommandLevel = Mathf.Max(bestNavalCommandLevel, 2);
        }

        int groundCap = AddonConfig.StartGroundCap.Value + groundBonus;
        int airCap = AddonConfig.StartAirCap.Value + airBonus;
        int navalCap = AddonConfig.StartNavalCap.Value + navalBonus;
        if (SingleplayerCheatService.IsIgnoreCapsActiveFor(hq))
        {
            groundCap = Mathf.Max(groundCap, 999);
            airCap = Mathf.Max(airCap, 999);
            navalCap = Mathf.Max(navalCap, 999);
        }

        return new FactionCapSnapshot(
            groundUsed,
            groundCap,
            airUsed,
            airCap,
            navalUsed,
            navalCap,
            techTier,
            commandHqLevel,
            bestPilotAcademyLevel,
            bestNavalCommandLevel,
            commandHqCount,
            barracksCount,
            pilotAcademyCount,
            logisticsCampCount,
            navalCommandCount,
            abmPlantCount,
            abmRadarCount,
            abmSiloCount);
    }

    private void TickLogisticsAura(float now)
    {
        logisticsCleanScratch.Clear();
        foreach (KeyValuePair<Unit, float> kv in logisticsHealedAt)
        {
            if (kv.Key == null || kv.Key.disabled || now - kv.Value > 60f)
            {
                logisticsCleanScratch.Add(kv.Key!);
            }
        }

        for (int i = 0; i < logisticsCleanScratch.Count; i++)
        {
            logisticsHealedAt.Remove(logisticsCleanScratch[i]);
        }

        foreach (RtsBuildingRecord camp in records.Values)
        {
            if (camp.Spec.Kind != RtsBuildingKind.LogisticsCamp
                || camp.Unit == null
                || camp.Unit.disabled
                || camp.Hq == null
                || !((NetworkBehaviour)camp.Hq).IsServer)
            {
                continue;
            }

            float radius = 180f + (camp.Level - 1) * 45f;
            float radiusSq = radius * radius;
            float unitCooldown = 12f - (camp.Level - 1) * 3f;
            GlobalPosition campPos = camp.Unit.GlobalPosition();

            for (int i = 0; i < UnitRegistry.allUnits.Count; i++)
            {
                Unit? candidate = UnitRegistry.allUnits[i];
                if (candidate == null
                    || candidate.disabled
                    || !ReferenceEquals(candidate.NetworkHQ, camp.Hq)
                    || (!(candidate is GroundVehicle) && !(candidate is Building)))
                {
                    continue;
                }

                if (FastMath.SquareDistance(campPos, candidate.GlobalPosition()) > radiusSq)
                {
                    continue;
                }

                if (logisticsHealedAt.TryGetValue(candidate, out float healedAt) && now < healedAt + unitCooldown)
                {
                    continue;
                }

                if (GroundControlReflection.GetUnitCondition(candidate) >= 0.99f)
                {
                    continue;
                }

                List<UnitPart> parts = candidate.GetAllParts();
                for (int p = 0; p < parts.Count; p++)
                {
                    UnitPart? part = parts[p];
                    if (part != null && part.hitPoints > 0f && part.hitPoints < 99.5f)
                    {
                        part.Repair();
                        logisticsHealedAt[candidate] = now;
                        break;
                    }
                }
            }
        }
    }

    private static int CountLiveBuilders(FactionHQ hq)
    {
        if (hq.factionUnits == null)
        {
            return 0;
        }

        int count = 0;
        foreach (PersistentID pid in hq.factionUnits)
        {
            if (pid.TryGetUnit(out Unit unit) && unit != null && !unit.disabled && GroundControlReflection.IsRepairUnit(unit))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountArtillery(FactionHQ hq)
    {
        if (hq.factionUnits == null)
        {
            return 0;
        }

        int count = 0;
        foreach (PersistentID pid in hq.factionUnits)
        {
            if (pid.TryGetUnit(out Unit unit)
                && unit != null
                && !unit.disabled
                && unit.definition is VehicleDefinition vdef
                && (int)vdef.vehicleType == ArtilleryVehicleTypeInt)
            {
                count++;
            }
        }

        return count;
    }

    private void NotifyIfWanted(FactionHQ hq, string key, string message, bool tellPlayer)
    {
        if (!tellPlayer)
        {
            return;
        }

        EmitFactionNotice(
            hq,
            key,
            message,
            $"{hq.faction?.factionName ?? "Faction"} blocked: {message}");
    }

    private void EmitFactionNotice(FactionHQ hq, string key, string playerNotice, string aiLogLine)
    {
        float now = Time.unscaledTime;
        string cooldownKey = $"{hq.GetInstanceID()}_{key}";
        if (noticeCooldowns.TryGetValue(cooldownKey, out float until) && now < until)
        {
            return;
        }

        noticeCooldowns[cooldownKey] = now + NoticeCooldownSeconds;
        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        if (localHq != null && ReferenceEquals(hq, localHq))
        {
            GroundControlReflection.NoticeFail(playerNotice);
        }
        else
        {
            AddonLog.Debug($"[City] {aiLogLine}");
        }
    }
}
