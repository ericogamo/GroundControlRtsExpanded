using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Mirage;
using NuclearOption.Networking;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal readonly struct SamThreatBubble
{
    internal SamThreatBubble(
        GlobalPosition pos,
        float wezRadiusMeters,
        bool isLongRange,
        bool isRadarEmitting,
        float lastSeenAt)
    {
        Pos = pos;
        WezRadiusMeters = wezRadiusMeters;
        IsLongRange = isLongRange;
        IsRadarEmitting = isRadarEmitting;
        LastSeenAt = lastSeenAt;
    }

    internal GlobalPosition Pos { get; }
    internal float WezRadiusMeters { get; }
    internal bool IsLongRange { get; }
    internal bool IsRadarEmitting { get; }
    internal float LastSeenAt { get; }
}

internal readonly struct TrackedHostileContact
{
    internal TrackedHostileContact(
        Unit unit,
        GlobalPosition pos,
        bool isBuilding,
        bool isGroundCombat,
        bool isHeavyArmor,
        float cityEconomyWeight)
    {
        Unit = unit;
        Pos = pos;
        IsBuilding = isBuilding;
        IsGroundCombat = isGroundCombat;
        IsHeavyArmor = isHeavyArmor;
        CityEconomyWeight = cityEconomyWeight;
    }

    internal Unit Unit { get; }
    internal GlobalPosition Pos { get; }
    internal bool IsBuilding { get; }
    internal bool IsGroundCombat { get; }
    internal bool IsHeavyArmor { get; }
    internal float CityEconomyWeight { get; }
}

internal sealed class AiBattlefieldIntelService : IAddonTick, IAddonResetSession
{
    private const float RefreshIntervalSeconds = 2.5f;
    private const float VehicleContactExpireSeconds = 90f;
    private const float LocatedArtilleryTtlSeconds = 120f;
    private const float RearQrfAlertTtlSeconds = 90f;
    private const float BaseAnalysisRadiusMeters = 3500f;
    private const float MaxLandAssaultRoadMeters = 70000f;
    private const float LongRangeSamThresholdMeters = 20000f;

    private sealed class FactionIntelState
    {
        internal readonly List<SamThreatBubble> SamBubbles = new(32);
        internal readonly List<TrackedHostileContact> Contacts = new(128);
        internal readonly Dictionary<Unit, float> LocatedArtilleryUntil = new();
        internal readonly List<Unit> PruneArtilleryScratch = new(16);
        internal Airbase? MainObjectiveBase;
        internal float MainObjectiveUpdatedAt;
        internal GlobalPosition QrfAlertPos;
        internal Unit? QrfAttacker;
        internal float QrfAlertUntil;
    }

    internal static AiBattlefieldIntelService Instance { get; } = new();

    private readonly Dictionary<FactionHQ, FactionIntelState> states = new();
    private readonly List<GlobalPosition> enemyBaseAnchorScratch = new(16);
    private readonly List<(Airbase Base, float MinDist)> safetySortScratch = new(16);
    private float nextTickAt;

    private AiBattlefieldIntelService()
    {
    }

    public void ResetSession()
    {
        states.Clear();
        enemyBaseAnchorScratch.Clear();
        safetySortScratch.Clear();
        nextTickAt = 0f;
    }

    public void Tick()
    {
        if (!AddonConfig.ModEnabled.Value || !IsServerAuthorityActive())
        {
            return;
        }

        float now = Time.time;
        if (now < nextTickAt)
        {
            return;
        }

        nextTickAt = now + RefreshIntervalSeconds;

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        GroundControlReflection.NetworkRunAsHostTeam(() =>
        {
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (hq != null && ((NetworkBehaviour)hq).IsServer && GroundControlReflection.IsSafeAiFaction(hq, localHq))
                {
                    RefreshFactionIntel(hq, now);
                }
            }
        });
    }

    private static bool IsServerAuthorityActive()
    {
        NetworkManagerNuclearOption? net = NetworkManagerNuclearOption.i;
        return net != null && net.Server != null && net.Server.Active;
    }

    private FactionIntelState GetOrCreateState(FactionHQ aiHq)
    {
        if (!states.TryGetValue(aiHq, out FactionIntelState? state))
        {
            state = new FactionIntelState();
            states[aiHq] = state;
        }

        return state;
    }

    private void RefreshFactionIntel(FactionHQ aiHq, float now)
    {
        FactionIntelState state = GetOrCreateState(aiHq);
        state.SamBubbles.Clear();
        state.Contacts.Clear();

        float levelNow = Time.timeSinceLevelLoad;

        if (aiHq.trackingDatabase != null)
        {
            foreach (KeyValuePair<PersistentID, TrackingInfo> kv in aiHq.trackingDatabase)
            {
                TrackingInfo info = kv.Value;
                if (info == null || !info.TryGetUnit(out Unit unit) || unit == null || unit.disabled)
                {
                    continue;
                }

                FactionHQ? unitHq = unit.NetworkHQ;
                if (unitHq == null || ReferenceEquals(unitHq, aiHq))
                {
                    continue;
                }

                bool isBuilding = unit is Building;
                if (!isBuilding && levelNow - info.lastSpottedTime > VehicleContactExpireSeconds)
                {
                    continue;
                }

                GlobalPosition pos = info.GetPosition();

                if (TryBuildSamBubble(unit, pos, info.lastSpottedTime, out SamThreatBubble bubble))
                {
                    state.SamBubbles.Add(bubble);
                }

                bool isGroundCombat = false;
                bool isHeavyArmor = false;
                if (unit is GroundVehicle && unit.definition is VehicleDefinition vdef)
                {
                    if (GroundControlReflection.IsCombatVehicle(vdef)
                        && !GroundControlReflection.IsBuilderVehicle(vdef)
                        && !GroundControlReflection.IsSupplyVehicle(vdef))
                    {
                        isGroundCombat = true;
                        isHeavyArmor = vdef.vehicleType == VehicleType.MBT
                            || (vdef.value >= 35f && !GroundControlReflection.IsAirDefence(vdef));
                    }
                }

                float ecoWeight = isBuilding ? EvaluateBuildingStrategicWeight(unit) : 0f;
                if (isBuilding || isGroundCombat)
                {
                    state.Contacts.Add(new TrackedHostileContact(
                        unit,
                        pos,
                        isBuilding,
                        isGroundCombat,
                        isHeavyArmor,
                        ecoWeight));
                }
            }
        }

        // Prune expired artillery fixes
        if (state.LocatedArtilleryUntil.Count > 0)
        {
            state.PruneArtilleryScratch.Clear();
            foreach (KeyValuePair<Unit, float> kv in state.LocatedArtilleryUntil)
            {
                if (kv.Key == null || kv.Key.disabled || now >= kv.Value)
                {
                    state.PruneArtilleryScratch.Add(kv.Key!);
                }
            }

            for (int i = 0; i < state.PruneArtilleryScratch.Count; i++)
            {
                state.LocatedArtilleryUntil.Remove(state.PruneArtilleryScratch[i]);
            }

            state.PruneArtilleryScratch.Clear();
        }

        // Expire stale QRF alerts
        if (state.QrfAlertUntil > 0f && now >= state.QrfAlertUntil)
        {
            state.QrfAlertUntil = 0f;
            state.QrfAttacker = null;
        }

        // Refresh MainObjectiveBase from active MainEffort (slot -1) or primary active ground drive
        RefreshMainObjective(aiHq, state, now);
    }

    private static void RefreshMainObjective(FactionHQ aiHq, FactionIntelState state, float now)
    {
        state.MainObjectiveBase = null;

        object? captureInst = GroundControlReflection.GetCaptureInstance();
        if (captureInst == null || GroundControlReflection.CaptureDrivesField?.GetValue(captureInst) is not IDictionary drives)
        {
            return;
        }

        Airbase? fallbackActive = null;
        foreach (DictionaryEntry entry in drives)
        {
            object? driveObj = entry.Value;
            if (driveObj == null)
            {
                continue;
            }

            if (GroundControlReflection.DriveOwnerProp?.GetValue(driveObj) is not FactionHQ owner
                || !ReferenceEquals(owner, aiHq))
            {
                continue;
            }

            if (GroundControlReflection.DriveTargetProp?.GetValue(driveObj) is not Airbase target
                || target.disabled
                || target.center == null
                || ReferenceEquals(target.CurrentHQ, aiHq))
            {
                continue;
            }

            bool isMainEffort = entry.Key is ValueTuple<Airbase, int> slotKey && slotKey.Item2 == -1;

            bool isFormingOrActive = false;
            if (GroundControlReflection.DriveCommittedProp?.GetValue(driveObj) is List<Unit> committed && committed.Count > 0)
            {
                isFormingOrActive = true;
            }
            else if (GroundControlReflection.DriveMarchingProp?.GetValue(driveObj) is ICollection marching && marching.Count > 0)
            {
                isFormingOrActive = true;
            }
            else if (GroundControlReflection.DriveLaunchedProp?.GetValue(driveObj) is ICollection launched && launched.Count > 0)
            {
                isFormingOrActive = true;
            }
            else if (GroundControlReflection.DriveFormingSinceProp?.GetValue(driveObj) is float formingSince && formingSince > 0f)
            {
                isFormingOrActive = true;
            }

            if (isMainEffort && isFormingOrActive)
            {
                state.MainObjectiveBase = target;
                state.MainObjectiveUpdatedAt = now;
                return;
            }

            if (isFormingOrActive && fallbackActive == null)
            {
                fallbackActive = target;
            }
        }

        if (fallbackActive != null)
        {
            state.MainObjectiveBase = fallbackActive;
            state.MainObjectiveUpdatedAt = now;
        }
    }

    private static bool TryBuildSamBubble(Unit unit, GlobalPosition pos, float lastSpotted, out SamThreatBubble bubble)
    {
        bubble = default;

        float weaponRange = unit.GetMaxRange();
        float radarRange = unit.radar is Radar r ? r.GetRadarRange() : 0f;
        bool emitting = unit.HasRadarEmission();

        if (unit is GroundVehicle && unit.definition is VehicleDefinition vdef)
        {
            bool isAd = GroundControlReflection.IsAirDefence(vdef) || GroundControlReflection.IsRadarGuidedAirDefence(vdef);
            bool isRdr = vdef.vehicleType == VehicleType.RDR;
            if (!isAd && !isRdr && !emitting)
            {
                return false;
            }

            float wez = weaponRange > 0f ? weaponRange : (radarRange > 0f ? Mathf.Min(radarRange, 22000f) : 10000f);
            bool isLong = wez >= LongRangeSamThresholdMeters || (vdef.vehicleType == VehicleType.R_SAM && wez >= 18000f);
            bubble = new SamThreatBubble(pos, wez, isLong, emitting, lastSpotted);
            return true;
        }

        if (unit is Building)
        {
            bool isMds = MdsBridgeBootstrap.IsMdsSelectableUnit?.Invoke(unit) == true;
            bool hasAaWeapon = unit.weaponStations != null
                && unit.weaponStations.Count > 0
                && unit.definition != null
                && unit.definition.roleIdentity.antiAir > 0f;
            bool isRdrBldg = unit.definition is BuildingDefinition bd && bd.buildingType == BuildingType.RDR;

            if (isMds && MdsBridgeBootstrap.QueryMdsSiteUiStatus != null)
            {
                var mdsStatus = MdsBridgeBootstrap.QueryMdsSiteUiStatus(unit);
                if (!mdsStatus.IsWeaponSite && !mdsStatus.IsFireControlRadar && !emitting)
                {
                    return false;
                }

                float mdsRange = weaponRange > 0f
                    ? weaponRange
                    : (radarRange > 0f
                        ? Mathf.Min(radarRange, 35000f)
                        : (mdsStatus.IsFireControlRadar ? Mathf.Max(mdsStatus.FcuRadiusKm * 1000f, 32000f) : 16000f));
                if (mdsRange <= 0f)
                {
                    mdsRange = 16000f;
                }

                bubble = new SamThreatBubble(pos, mdsRange, mdsRange >= LongRangeSamThresholdMeters, emitting, lastSpotted);
                return true;
            }

            if (!hasAaWeapon && !isRdrBldg && !emitting)
            {
                return false;
            }

            float wez = weaponRange > 0f ? weaponRange : (radarRange > 0f ? Mathf.Min(radarRange, 25000f) : 12000f);
            bubble = new SamThreatBubble(pos, wez, wez >= LongRangeSamThresholdMeters, emitting, lastSpotted);
            return true;
        }

        if (unit is Ship && unit.definition != null && (unit.definition.roleIdentity.antiAir > 0f || emitting))
        {
            float wez = weaponRange > 0f ? weaponRange : (radarRange > 0f ? Mathf.Min(radarRange, 28000f) : 14000f);
            bubble = new SamThreatBubble(pos, wez, wez >= LongRangeSamThresholdMeters, emitting, lastSpotted);
            return true;
        }

        return false;
    }

    internal static float EvaluateBuildingStrategicWeight(Unit building)
    {
        if (building == null || building.definition == null)
        {
            return 0f;
        }

        if (building.definition is BuildingDefinition bd && RtsBuildingCatalog.TryGetSpec(bd, out RtsBuildingSpec spec))
        {
            return spec.Kind switch
            {
                RtsBuildingKind.CommandHq => 65f,
                RtsBuildingKind.PilotAcademy => 55f,
                RtsBuildingKind.AbmPlant or RtsBuildingKind.AbmRadar or RtsBuildingKind.AbmSilo => 55f,
                RtsBuildingKind.NavalCommand => 40f,
                RtsBuildingKind.Barracks => 35f,
                RtsBuildingKind.LogisticsCamp => 30f,
                _ => 20f,
            };
        }

        if (GroundControlReflection.EcoIsIncomeBuildingUnit(building))
        {
            return 45f;
        }

        if (building.definition is BuildingDefinition bdef)
        {
            return bdef.buildingType switch
            {
                BuildingType.FAC => 45f,
                BuildingType.CIV => 40f,
                BuildingType.DEP => 30f,
                BuildingType.HGR => 25f,
                BuildingType.RDR => 25f,
                _ => 10f,
            };
        }

        return 10f;
    }

    internal float EvaluateCorridorSamRisk(
        FactionHQ? aiHq,
        GlobalPosition from,
        GlobalPosition waypoint,
        GlobalPosition target)
    {
        if (aiHq == null || !states.TryGetValue(aiHq, out FactionIntelState? state) || state.SamBubbles.Count == 0)
        {
            return 0f;
        }

        float totalRisk = SampleSegmentSamRisk(state.SamBubbles, from, waypoint, target);
        totalRisk += SampleSegmentSamRisk(state.SamBubbles, waypoint, target, target);
        return totalRisk;
    }

    private static float SampleSegmentSamRisk(List<SamThreatBubble> bubbles, GlobalPosition a, GlobalPosition b, GlobalPosition excludeNearTarget)
    {
        float segDist = FastMath.Distance(a, b);
        if (segDist < 100f)
        {
            return 0f;
        }

        int steps = Mathf.Clamp(Mathf.CeilToInt(segDist / 4500f), 3, 10);
        float risk = 0f;
        for (int s = 1; s <= steps; s++)
        {
            float t = (float)s / (steps + 1);
            GlobalPosition sample = FastMath.Lerp(a, b, t);

            for (int i = 0; i < bubbles.Count; i++)
            {
                SamThreatBubble bubble = bubbles[i];
                if (bubble.WezRadiusMeters <= 100f || FastMath.InRange(bubble.Pos, excludeNearTarget, 6000f))
                {
                    continue;
                }

                float d = FastMath.Distance(sample, bubble.Pos);
                if (d < bubble.WezRadiusMeters)
                {
                    float penetration = 1f - (d / bubble.WezRadiusMeters);
                    float weight = 0.5f + penetration;
                    if (bubble.IsLongRange)
                    {
                        weight *= 1.8f;
                    }

                    if (bubble.IsRadarEmitting)
                    {
                        weight *= 1.4f;
                    }

                    risk += weight;
                }
            }
        }

        return risk;
    }

    internal bool HasLongRangeSamThreat(FactionHQ? aiHq, GlobalPosition pos, float searchRadiusMeters)
    {
        if (aiHq == null || !states.TryGetValue(aiHq, out FactionIntelState? state))
        {
            return false;
        }

        for (int i = 0; i < state.SamBubbles.Count; i++)
        {
            SamThreatBubble bubble = state.SamBubbles[i];
            if (bubble.IsLongRange && FastMath.InRange(pos, bubble.Pos, searchRadiusMeters + bubble.WezRadiusMeters))
            {
                return true;
            }
        }

        return false;
    }

    internal Airbase? SelectBestAssaultTarget(FactionHQ? aiHq, IList<Airbase>? stagingBases)
    {
        if (aiHq == null || stagingBases == null || stagingBases.Count == 0)
        {
            return null;
        }

        states.TryGetValue(aiHq, out FactionIntelState? state);

        Airbase? bestBase = null;
        float bestScore = float.MinValue;

        foreach (Airbase candidate in FactionRegistry.airbaseLookup.Values)
        {
            if (candidate == null
                || candidate.disabled
                || candidate.center == null
                || candidate.CurrentHQ == null
                || ReferenceEquals(candidate.CurrentHQ, aiHq)
                || !GroundControlReflection.IsLandBase(candidate)
                || !GroundControlReflection.IsTakeable(candidate, aiHq))
            {
                continue;
            }

            GlobalPosition basePos = candidate.center.GlobalPosition();
            float minRoadMeters = float.MaxValue;
            for (int i = 0; i < stagingBases.Count; i++)
            {
                Airbase staging = stagingBases[i];
                if (staging == null || staging.disabled || staging.center == null)
                {
                    continue;
                }

                GlobalPosition stagingPos = staging.center.GlobalPosition();
                if (!GroundControlReflection.CanDriveBetween(stagingPos, basePos))
                {
                    continue;
                }

                float road = GroundControlReflection.CaptureGetRoadMeters(staging, candidate);
                if (road < minRoadMeters)
                {
                    minRoadMeters = road;
                }
            }

            if (minRoadMeters >= MaxLandAssaultRoadMeters)
            {
                continue;
            }

            float strategicValue = 100f;
            int knownGroundDefenders = 0;
            int knownHeavyArmor = 0;
            int knownSamBubbles = 0;
            float baseRadiusSq = BaseAnalysisRadiusMeters * BaseAnalysisRadiusMeters;

            if (state != null)
            {
                for (int i = 0; i < state.Contacts.Count; i++)
                {
                    TrackedHostileContact c = state.Contacts[i];
                    if (FastMath.SquareDistance(c.Pos, basePos) > baseRadiusSq)
                    {
                        continue;
                    }

                    if (c.IsBuilding)
                    {
                        strategicValue += c.CityEconomyWeight;
                    }

                    if (c.IsGroundCombat)
                    {
                        knownGroundDefenders++;
                        if (c.IsHeavyArmor)
                        {
                            knownHeavyArmor++;
                        }
                    }
                }

                for (int i = 0; i < state.SamBubbles.Count; i++)
                {
                    if (FastMath.SquareDistance(state.SamBubbles[i].Pos, basePos) <= baseRadiusSq)
                    {
                        knownSamBubbles++;
                    }
                }
            }

            float resistance = 1.0f
                + knownGroundDefenders * 0.45f
                + knownHeavyArmor * 0.65f
                + knownSamBubbles * 0.85f;

            if (AiTacticalDoctrineService.Instance.IsTargetStalled(aiHq, candidate))
            {
                resistance *= 4.0f;
            }

            float distanceFactor = 1.0f + minRoadMeters / 25000f;
            float score = strategicValue / Mathf.Max(0.25f, resistance * distanceFactor);

            if (score > bestScore)
            {
                bestScore = score;
                bestBase = candidate;
            }
        }

        if (bestBase == null)
        {
            // Intentionally return null on archipelago / water-separated maps so FindNearestHeldBase
            // falls back to vanilla without sending ground units across impassable water.
        }

        return bestBase;
    }

    internal GlobalPosition GetCoordinatedStrikeTarget(
        FactionHQ? aiHq,
        FactionHQ? localHq,
        GlobalPosition fallback,
        bool includeRearQrf = true)
    {
        if (aiHq == null)
        {
            return fallback;
        }

        if (includeRearQrf && TryGetRearQrfAlert(aiHq, out GlobalPosition alertPos, out Unit? attacker) && !(attacker is Aircraft))
        {
            return alertPos;
        }

        if (states.TryGetValue(aiHq, out FactionIntelState? state))
        {
            if (state.MainObjectiveBase != null
                && !state.MainObjectiveBase.disabled
                && state.MainObjectiveBase.center != null
                && !ReferenceEquals(state.MainObjectiveBase.CurrentHQ, aiHq))
            {
                return state.MainObjectiveBase.center.GlobalPosition();
            }

            // Priority 3: Hostile base with best ratio of known economy/tech value vs. SAM density
            Airbase? bestStrikeBase = null;
            float bestRatio = float.MinValue;
            float baseRadiusSq = BaseAnalysisRadiusMeters * BaseAnalysisRadiusMeters;

            foreach (Airbase ab in FactionRegistry.airbaseLookup.Values)
            {
                if (ab == null
                    || ab.disabled
                    || ab.center == null
                    || ab.CurrentHQ == null
                    || ReferenceEquals(ab.CurrentHQ, aiHq))
                {
                    continue;
                }

                GlobalPosition abPos = ab.center.GlobalPosition();
                float ecoTechVal = 50f;
                int samDensity = 0;

                for (int i = 0; i < state.Contacts.Count; i++)
                {
                    TrackedHostileContact c = state.Contacts[i];
                    if (c.IsBuilding && FastMath.SquareDistance(c.Pos, abPos) <= baseRadiusSq)
                    {
                        ecoTechVal += c.CityEconomyWeight;
                    }
                }

                for (int i = 0; i < state.SamBubbles.Count; i++)
                {
                    if (FastMath.SquareDistance(state.SamBubbles[i].Pos, abPos) <= baseRadiusSq)
                    {
                        samDensity++;
                    }
                }

                float ratio = ecoTechVal / (1.0f + samDensity * 0.85f);
                if (ratio > bestRatio)
                {
                    bestRatio = ratio;
                    bestStrikeBase = ab;
                }
            }

            if (bestStrikeBase != null && bestStrikeBase.center != null)
            {
                return bestStrikeBase.center.GlobalPosition();
            }
        }

        return fallback;
    }

    internal bool TryGetMainObjectiveBase(FactionHQ? aiHq, out Airbase? mainObjective)
    {
        mainObjective = null;
        if (aiHq != null
            && states.TryGetValue(aiHq, out FactionIntelState? state)
            && state.MainObjectiveBase != null
            && !state.MainObjectiveBase.disabled)
        {
            mainObjective = state.MainObjectiveBase;
            return true;
        }

        return false;
    }

    internal GlobalPosition GetPrimaryObjectivePos(FactionHQ? aiHq, FactionHQ? localHq)
    {
        if (TryGetMainObjectiveBase(aiHq, out Airbase? mainObj) && mainObj != null && mainObj.center != null)
        {
            return mainObj.center.GlobalPosition();
        }

        FactionHQ? hostileHq = (localHq != null && !ReferenceEquals(localHq, aiHq))
            ? localHq
            : GroundControlReflection.FindPrimaryHostileHq(aiHq);
        GlobalPosition fallback = hostileHq != null ? GroundControlReflection.CaptureGetTerritoryCenter(hostileHq) : default;
        return GetCoordinatedStrikeTarget(aiHq, hostileHq, fallback, includeRearQrf: false);
    }

    internal void ReportLocatedHostileArtillery(FactionHQ? aiHq, Unit? artilleryUnit)
    {
        if (aiHq == null || artilleryUnit == null || artilleryUnit.disabled || !((NetworkBehaviour)aiHq).IsServer)
        {
            return;
        }

        FactionIntelState state = GetOrCreateState(aiHq);
        state.LocatedArtilleryUntil[artilleryUnit] = Time.time + LocatedArtilleryTtlSeconds;
    }

    internal bool IsLocatedHostileArtillery(FactionHQ? aiHq, Unit? unit)
    {
        if (aiHq == null || unit == null || unit.disabled || !states.TryGetValue(aiHq, out FactionIntelState? state))
        {
            return false;
        }

        return state.LocatedArtilleryUntil.TryGetValue(unit, out float until) && Time.time < until;
    }

    internal void ReportRearAreaAlarm(FactionHQ? aiHq, GlobalPosition pos, Unit? attacker)
    {
        if (aiHq == null || !((NetworkBehaviour)aiHq).IsServer)
        {
            return;
        }

        FactionIntelState state = GetOrCreateState(aiHq);
        state.QrfAlertPos = pos;
        state.QrfAttacker = attacker != null && !attacker.disabled ? attacker : null;
        state.QrfAlertUntil = Time.time + RearQrfAlertTtlSeconds;
    }

    internal bool TryGetRearQrfAlert(FactionHQ? aiHq, out GlobalPosition alertPos, out Unit? attacker)
    {
        alertPos = default;
        attacker = null;
        if (aiHq == null || !states.TryGetValue(aiHq, out FactionIntelState? state))
        {
            return false;
        }

        if (state.QrfAlertUntil <= 0f || Time.time >= state.QrfAlertUntil)
        {
            return false;
        }

        alertPos = state.QrfAlertPos;
        attacker = state.QrfAttacker != null && !state.QrfAttacker.disabled ? state.QrfAttacker : null;
        return true;
    }

    internal void ClearRearQrfAlert(FactionHQ? aiHq)
    {
        if (aiHq != null && states.TryGetValue(aiHq, out FactionIntelState? state))
        {
            state.QrfAlertUntil = 0f;
            state.QrfAttacker = null;
        }
    }

    internal bool TryGetSamBubbles(FactionHQ? aiHq, out IReadOnlyList<SamThreatBubble> bubbles)
    {
        if (aiHq != null && states.TryGetValue(aiHq, out FactionIntelState? state))
        {
            bubbles = state.SamBubbles;
            return true;
        }

        bubbles = Array.Empty<SamThreatBubble>();
        return false;
    }

    internal void GetAirbasesOrderedBySafety(FactionHQ? aiHq, FactionHQ? enemyHq, List<Airbase> outOrdered)
    {
        outOrdered.Clear();
        if (aiHq == null)
        {
            return;
        }

        enemyBaseAnchorScratch.Clear();
        if (enemyHq != null)
        {
            foreach (Airbase eb in enemyHq.GetAirbases())
            {
                if (eb != null && !eb.disabled && eb.center != null)
                {
                    enemyBaseAnchorScratch.Add(eb.center.GlobalPosition());
                }
            }
        }

        if (enemyBaseAnchorScratch.Count == 0)
        {
            foreach (Airbase ab in FactionRegistry.airbaseLookup.Values)
            {
                if (ab != null
                    && !ab.disabled
                    && ab.center != null
                    && ab.CurrentHQ != null
                    && !ReferenceEquals(ab.CurrentHQ, aiHq))
                {
                    enemyBaseAnchorScratch.Add(ab.center.GlobalPosition());
                }
            }
        }

        safetySortScratch.Clear();
        foreach (Airbase ownAb in aiHq.GetAirbases())
        {
            if (ownAb == null || ownAb.disabled || ownAb.center == null || !GroundControlReflection.IsLandBase(ownAb))
            {
                continue;
            }

            GlobalPosition ownPos = ownAb.center.GlobalPosition();
            float minEnemyDist = float.MaxValue;
            for (int i = 0; i < enemyBaseAnchorScratch.Count; i++)
            {
                float d = FastMath.Distance(ownPos, enemyBaseAnchorScratch[i]);
                if (d < minEnemyDist)
                {
                    minEnemyDist = d;
                }
            }

            safetySortScratch.Add((ownAb, minEnemyDist));
        }

        safetySortScratch.Sort((a, b) => b.MinDist.CompareTo(a.MinDist));
        for (int i = 0; i < safetySortScratch.Count; i++)
        {
            outOrdered.Add(safetySortScratch[i].Base);
        }

        safetySortScratch.Clear();
        enemyBaseAnchorScratch.Clear();
    }
}
