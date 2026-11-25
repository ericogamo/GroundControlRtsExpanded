using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirage;
using NuclearOption.Networking;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal sealed class AiRadarEmconService : IAddonTick, IAddonResetSession
{
    private const float EmconTickSeconds = 2.0f;
    private const float AradScanIntervalSeconds = 0.5f;
    private const float ArmSweepIntervalSeconds = 0.25f;
    private const float ArmThreatConeDotThreshold = 0.72f;
    private const float ArmThreatRadiusMeters = 10000f;
    private const float HandoffSearchRadiusMeters = 25000f;
    private const float BlinkReblinkCooldownSeconds = 16.0f;
    private const float EwCoverageRadiusMeters = 35000f;
    private const float HotHoldSeconds = 25.0f;
    private const string ModularSiteBehaviorTypeName = "MDSFramework.Sensors.ModularSiteBehavior";

    private static readonly Type? ModularSiteBehaviorType = AccessTools.TypeByName(ModularSiteBehaviorTypeName);

    private sealed class RadarUnitEntry
    {
        internal Unit Unit = null!;
        internal FactionHQ Hq = null!;
        internal Radar[] Radars = Array.Empty<Radar>();
        internal bool IsSamRadar;
        internal float MaxWeaponRange;
        internal float MaxRadarRange;
        internal float BlinkOffUntil;
        internal float BlinkCooldownUntil;
        internal float ForcedOnUntil;
        internal float ForcedHotUntil;
        internal bool CurrentlyActive;
    }

    internal static AiRadarEmconService Instance { get; } = new();

    private readonly Dictionary<Unit, RadarUnitEntry> entries = new();
    private readonly List<RadarUnitEntry> entryList = new(64);
    private readonly List<Unit> pruneScratch = new(32);
    private readonly List<GlobalPosition> ewCoverageScratch = new(32);
    private readonly Dictionary<int, float> nextArmSweepByMissileId = new();
    private readonly List<int> staleMissileIdScratch = new(32);
    private readonly Dictionary<WeaponInfo, bool> aradWeaponCache = new();

    private float nextEmconTickAt;
    private float nextAradScanAt;

    private AiRadarEmconService()
    {
    }

    public void ResetSession()
    {
        entries.Clear();
        entryList.Clear();
        pruneScratch.Clear();
        ewCoverageScratch.Clear();
        nextArmSweepByMissileId.Clear();
        staleMissileIdScratch.Clear();
        aradWeaponCache.Clear();
        nextEmconTickAt = 0f;
        nextAradScanAt = 0f;
    }

    public void Tick()
    {
        if (!AddonConfig.ModEnabled.Value || !IsServerAuthorityActive())
        {
            return;
        }

        if (!AddonConfig.EnableAiRadarEmcon.Value && !AddonConfig.EnableAiAmbushEmcon.Value)
        {
            return;
        }

        float now = Time.time;
        if (AddonConfig.EnableAiRadarEmcon.Value && now >= nextAradScanAt)
        {
            nextAradScanAt = now + AradScanIntervalSeconds;
            PruneDeadEntries();
            PruneStaleArmSweeps(now);
            TickServerAradDetection(now);
        }

        if (now < nextEmconTickAt)
        {
            return;
        }

        nextEmconTickAt = now + EmconTickSeconds;

        PruneDeadEntries();
        PruneStaleArmSweeps(now);

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
        {
            if (hq != null && ((NetworkBehaviour)hq).IsServer && GroundControlReflection.IsAiFaction(hq, localHq))
            {
                RefreshFactionRadarEntries(hq);
                ProcessFactionEmcon(hq, now);
            }
        }
    }

    internal void OnArmSeekerTrack(Missile? missile, Radar? targetedRadar)
    {
        if (!AddonConfig.ModEnabled.Value
            || !AddonConfig.EnableAiRadarEmcon.Value
            || !IsServerAuthorityActive()
            || missile == null
            || missile.disabled)
        {
            return;
        }

        float now = Time.time;
        FactionHQ? localHq = GroundControlReflection.GetLocalHq();

        // Direct lock check on every TrackCurrentTarget call (fast O(1))
        if (targetedRadar != null && targetedRadar.activated)
        {
            Unit? directOwner = targetedRadar.GetAttachedUnit();
            if (directOwner != null
                && !directOwner.disabled
                && !(directOwner is Aircraft)
                && directOwner.NetworkHQ != null
                && !ReferenceEquals(directOwner.NetworkHQ, missile.NetworkHQ)
                && GroundControlReflection.IsAiFaction(directOwner.NetworkHQ, localHq)
                && !IsMdsSite(directOwner))
            {
                RadarUnitEntry? directEntry = GetOrRegisterEntry(directOwner);
                if (directEntry != null)
                {
                    TriggerBlinkAndHandoff(directEntry, missile, now);
                }
            }
        }

        // Throttled 10 km approach-cone sweep across cached AI vanilla radar units
        int missileId = missile.GetInstanceID();
        if (nextArmSweepByMissileId.TryGetValue(missileId, out float nextSweepAt) && now < nextSweepAt)
        {
            return;
        }

        nextArmSweepByMissileId[missileId] = now + ArmSweepIntervalSeconds;

        GlobalPosition missilePos = missile.GlobalPosition();
        Vector3 velDir = missile.rb != null && missile.rb.velocity.sqrMagnitude > 1f
            ? missile.rb.velocity.normalized
            : missile.transform.forward;
        float levelNow = Time.timeSinceLevelLoad;

        for (int i = 0; i < entryList.Count; i++)
        {
            RadarUnitEntry entry = entryList[i];
            if (entry.Unit == null
                || entry.Unit.disabled
                || entry.Hq == null
                || ReferenceEquals(entry.Hq, missile.NetworkHQ)
                || entry.Unit is Ship)
            {
                continue;
            }

            if (!entry.CurrentlyActive || now < entry.BlinkCooldownUntil || now < entry.BlinkOffUntil)
            {
                continue;
            }

            if (entry.Hq.trackingDatabase == null
                || !entry.Hq.trackingDatabase.TryGetValue(missile.persistentID, out TrackingInfo? info)
                || info == null
                || levelNow - info.lastSpottedTime > 15f)
            {
                continue;
            }

            GlobalPosition radarPos = entry.Unit.GlobalPosition();
            if (!FastMath.InRange(missilePos, radarPos, ArmThreatRadiusMeters))
            {
                continue;
            }

            Vector3 toRadar = radarPos - missilePos;
            if (toRadar.sqrMagnitude < 1f)
            {
                continue;
            }

            if (Vector3.Dot(velDir, toRadar.normalized) > ArmThreatConeDotThreshold)
            {
                TriggerBlinkAndHandoff(entry, missile, now);
            }
        }
    }

    private void TickServerAradDetection(float now)
    {
        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        float levelNow = Time.timeSinceLevelLoad;

        foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
        {
            if (hq == null
                || !((NetworkBehaviour)hq).IsServer
                || !GroundControlReflection.IsAiFaction(hq, localHq)
                || hq.trackingDatabase == null)
            {
                continue;
            }

            foreach (KeyValuePair<PersistentID, TrackingInfo> kv in hq.trackingDatabase)
            {
                TrackingInfo? info = kv.Value;
                if (info == null
                    || levelNow - info.lastSpottedTime > 8f
                    || !info.TryGetUnit(out Unit unit)
                    || unit is not Missile missile
                    || missile.disabled
                    || ReferenceEquals(missile.NetworkHQ, hq))
                {
                    continue;
                }

                if (!IsAradMissile(missile))
                {
                    continue;
                }

                int missileId = missile.GetInstanceID();
                if (nextArmSweepByMissileId.TryGetValue(missileId, out float nextSweepAt) && now < nextSweepAt)
                {
                    continue;
                }

                nextArmSweepByMissileId[missileId] = now + ArmSweepIntervalSeconds;

                GlobalPosition missilePos = info.GetPosition();
                Vector3 velDir = missile.rb != null && missile.rb.velocity.sqrMagnitude > 1f
                    ? missile.rb.velocity.normalized
                    : missile.transform.forward;

                for (int i = 0; i < entryList.Count; i++)
                {
                    RadarUnitEntry entry = entryList[i];
                    if (entry.Unit == null
                        || entry.Unit.disabled
                        || !ReferenceEquals(entry.Hq, hq)
                        || entry.Unit is Ship)
                    {
                        continue;
                    }

                    if (!entry.CurrentlyActive || now < entry.BlinkCooldownUntil || now < entry.BlinkOffUntil)
                    {
                        continue;
                    }

                    GlobalPosition radarPos = entry.Unit.GlobalPosition();
                    if (!FastMath.InRange(missilePos, radarPos, ArmThreatRadiusMeters))
                    {
                        continue;
                    }

                    Vector3 toRadar = radarPos - missilePos;
                    if (toRadar.sqrMagnitude < 1f)
                    {
                        continue;
                    }

                    if (Vector3.Dot(velDir, toRadar.normalized) > ArmThreatConeDotThreshold)
                    {
                        TriggerBlinkAndHandoff(entry, missile, now);
                    }
                }
            }
        }
    }

    private bool IsAradMissile(Missile missile)
    {
        WeaponInfo? info = missile.GetWeaponInfo();
        if (info != null)
        {
            if (aradWeaponCache.TryGetValue(info, out bool cached))
            {
                return cached;
            }

            bool isArad = GroundControlReflection.AirIsAradWeapon(info)
                || (info.weaponPrefab != null && info.weaponPrefab.GetComponentInChildren<ARMSeeker>(true) != null);
            aradWeaponCache[info] = isArad;
            return isArad;
        }

        return missile.GetComponentInChildren<ARMSeeker>(true) != null;
    }

    private void TriggerBlinkAndHandoff(RadarUnitEntry entry, Missile threateningMissile, float now)
    {
        if (now < entry.BlinkCooldownUntil || now < entry.BlinkOffUntil)
        {
            return;
        }

        if (!HasOperationalActiveRadar(entry.Radars))
        {
            return;
        }

        float blinkSeconds = Mathf.Clamp(AddonConfig.AiRadarBlinkSeconds.Value, 4f, 18f);
        entry.BlinkOffUntil = now + blinkSeconds;
        entry.BlinkCooldownUntil = Mathf.Max(now + BlinkReblinkCooldownSeconds, entry.BlinkOffUntil + 4f);
        SetUnitRadarsActive(entry, active: false);

        // Handoff track continuity to nearest neighbor AI radar within 25 km not under ARAD approach
        GlobalPosition origin = entry.Unit.GlobalPosition();
        GlobalPosition missilePos = threateningMissile.GlobalPosition();
        Vector3 missileVelDir = threateningMissile.rb != null && threateningMissile.rb.velocity.sqrMagnitude > 1f
            ? threateningMissile.rb.velocity.normalized
            : threateningMissile.transform.forward;

        RadarUnitEntry? bestNeighbor = null;
        float bestDistSq = HandoffSearchRadiusMeters * HandoffSearchRadiusMeters;

        for (int i = 0; i < entryList.Count; i++)
        {
            RadarUnitEntry candidate = entryList[i];
            if (candidate == entry
                || candidate.Unit == null
                || candidate.Unit.disabled
                || !ReferenceEquals(candidate.Hq, entry.Hq)
                || now < candidate.BlinkOffUntil)
            {
                continue;
            }

            GlobalPosition candPos = candidate.Unit.GlobalPosition();
            float dSq = FastMath.SquareDistance(origin, candPos);
            if (dSq > bestDistSq)
            {
                continue;
            }

            if (FastMath.InRange(missilePos, candPos, ArmThreatRadiusMeters))
            {
                Vector3 toCand = candPos - missilePos;
                if (toCand.sqrMagnitude > 1f && Vector3.Dot(missileVelDir, toCand.normalized) > ArmThreatConeDotThreshold)
                {
                    continue;
                }
            }

            if (HasAnyOperationalRadar(candidate.Radars))
            {
                bestDistSq = dSq;
                bestNeighbor = candidate;
            }
        }

        if (bestNeighbor != null)
        {
            bestNeighbor.ForcedOnUntil = Mathf.Max(bestNeighbor.ForcedOnUntil, now + blinkSeconds + 4f);
            SetUnitRadarsActive(bestNeighbor, active: true);
        }
    }

    private void RefreshFactionRadarEntries(FactionHQ hq)
    {
        if (hq.factionUnits == null)
        {
            return;
        }

        foreach (PersistentID pid in hq.factionUnits)
        {
            if (!pid.TryGetUnit(out Unit unit) || unit == null || unit.disabled || unit is Aircraft)
            {
                continue;
            }

            if (entries.ContainsKey(unit) || IsMdsSite(unit))
            {
                continue;
            }

            GetOrRegisterEntry(unit);
        }
    }

    private RadarUnitEntry? GetOrRegisterEntry(Unit unit)
    {
        if (entries.TryGetValue(unit, out RadarUnitEntry? existing))
        {
            return existing;
        }

        if (unit == null || unit.disabled || unit.NetworkHQ == null || unit is Aircraft || IsMdsSite(unit))
        {
            return null;
        }

        Radar[] radars = unit.GetComponentsInChildren<Radar>(true);
        if (radars == null || radars.Length == 0)
        {
            return null;
        }

        float maxRadarRange = 0f;
        bool anyActive = false;
        for (int i = 0; i < radars.Length; i++)
        {
            Radar r = radars[i];
            if (r == null)
            {
                continue;
            }

            maxRadarRange = Mathf.Max(maxRadarRange, r.GetRadarRange());
            if (r.IsOperational() && r.activated)
            {
                anyActive = true;
            }
        }

        bool isSamRadar = ClassifySamRadar(unit);
        var entry = new RadarUnitEntry
        {
            Unit = unit,
            Hq = unit.NetworkHQ,
            Radars = radars,
            IsSamRadar = isSamRadar,
            MaxWeaponRange = unit.GetMaxRange(),
            MaxRadarRange = maxRadarRange,
            CurrentlyActive = anyActive,
        };

        entries[unit] = entry;
        entryList.Add(entry);
        return entry;
    }

    private void ProcessFactionEmcon(FactionHQ hq, float now)
    {
        ewCoverageScratch.Clear();

        // Pass 1: Keep non-SAM EW radars online (unless blinking from ARAD) and collect active EW/AWACS coverage
        for (int i = 0; i < entryList.Count; i++)
        {
            RadarUnitEntry entry = entryList[i];
            if (entry.Unit == null || entry.Unit.disabled || !ReferenceEquals(entry.Hq, hq))
            {
                continue;
            }

            if (entry.IsSamRadar)
            {
                continue;
            }

            if (AddonConfig.EnableAiRadarEmcon.Value && now < entry.BlinkOffUntil)
            {
                SetUnitRadarsActive(entry, active: false);
                continue;
            }

            SetUnitRadarsActive(entry, active: true);
            if (HasOperationalActiveRadar(entry.Radars))
            {
                ewCoverageScratch.Add(entry.Unit.GlobalPosition());
            }
        }

        if (hq.factionUnits != null)
        {
            foreach (PersistentID pid in hq.factionUnits)
            {
                if (pid.TryGetUnit(out Unit unit)
                    && unit is Aircraft ac
                    && !ac.disabled
                    && GroundControlReflection.EnemyIsRadarPicket(ac.definition, hq)
                    && ac.HasRadarEmission())
                {
                    ewCoverageScratch.Add(ac.GlobalPosition());
                }
            }
        }

        MdsBridgeBootstrap.CollectFriendlyMdsEwPositions?.Invoke(hq, ewCoverageScratch);

        // Pass 2: Evaluate SAM radars for blink restore, handoff, or Ambush-EMCON
        for (int i = 0; i < entryList.Count; i++)
        {
            RadarUnitEntry entry = entryList[i];
            if (entry.Unit == null || entry.Unit.disabled || !ReferenceEquals(entry.Hq, hq) || !entry.IsSamRadar)
            {
                continue;
            }

            if (AddonConfig.EnableAiRadarEmcon.Value && now < entry.BlinkOffUntil)
            {
                SetUnitRadarsActive(entry, active: false);
                continue;
            }

            if (now < entry.ForcedOnUntil)
            {
                SetUnitRadarsActive(entry, active: true);
                continue;
            }

            if (!AddonConfig.EnableAiAmbushEmcon.Value)
            {
                SetUnitRadarsActive(entry, active: true);
                continue;
            }

            GlobalPosition samPos = entry.Unit.GlobalPosition();
            if (!HasEwCoverageNear(samPos, EwCoverageRadiusMeters))
            {
                // Safety net: never blind SAMs without EW/AWACS coverage within 35 km
                SetUnitRadarsActive(entry, active: true);
                continue;
            }

            float maxRange = entry.MaxWeaponRange > 0f ? entry.MaxWeaponRange : entry.MaxRadarRange;
            if (maxRange <= 0f)
            {
                maxRange = 18000f;
            }

            float hotRadius = maxRange * 0.70f;
            float silentKeepRadius = Mathf.Clamp(maxRange * 0.75f, 11000f, 26000f);

            EvaluateAirThreatDistances(hq, samPos, hotRadius, silentKeepRadius, out bool insideHot, out bool insideSilentKeep);

            bool wantHot;
            if (insideHot)
            {
                entry.ForcedHotUntil = now + HotHoldSeconds;
                wantHot = true;
            }
            else if (now < entry.ForcedHotUntil || insideSilentKeep)
            {
                wantHot = true;
            }
            else
            {
                wantHot = false;
            }

            SetUnitRadarsActive(entry, wantHot);
        }

        ewCoverageScratch.Clear();
    }

    private bool HasEwCoverageNear(GlobalPosition pos, float radiusMeters)
    {
        float rSq = radiusMeters * radiusMeters;
        for (int i = 0; i < ewCoverageScratch.Count; i++)
        {
            if (FastMath.SquareDistance(pos, ewCoverageScratch[i]) <= rSq)
            {
                return true;
            }
        }

        return false;
    }

    private static void EvaluateAirThreatDistances(
        FactionHQ hq,
        GlobalPosition samPos,
        float hotRadius,
        float silentKeepRadius,
        out bool insideHot,
        out bool insideSilentKeep)
    {
        insideHot = false;
        insideSilentKeep = false;

        if (hq.trackingDatabase == null)
        {
            return;
        }

        float hotSq = hotRadius * hotRadius;
        float keepSq = silentKeepRadius * silentKeepRadius;
        float levelNow = Time.timeSinceLevelLoad;

        foreach (KeyValuePair<PersistentID, TrackingInfo> kv in hq.trackingDatabase)
        {
            TrackingInfo info = kv.Value;
            if (info == null || !info.TryGetUnit(out Unit unit) || unit == null || unit.disabled)
            {
                continue;
            }

            if (!(unit is Aircraft) && !(unit is Missile))
            {
                continue;
            }

            if (unit.NetworkHQ == null || ReferenceEquals(unit.NetworkHQ, hq))
            {
                continue;
            }

            if (levelNow - info.lastSpottedTime > 45f)
            {
                continue;
            }

            float dSq = FastMath.SquareDistance(samPos, info.GetPosition());
            if (dSq <= hotSq)
            {
                insideHot = true;
                insideSilentKeep = true;
                return;
            }

            if (dSq <= keepSq)
            {
                insideSilentKeep = true;
            }
        }
    }

    private static void SetUnitRadarsActive(RadarUnitEntry entry, bool active)
    {
        bool anyChanged = false;
        bool anyOperational = false;

        for (int i = 0; i < entry.Radars.Length; i++)
        {
            Radar r = entry.Radars[i];
            if (r == null || !r.IsOperational())
            {
                continue;
            }

            anyOperational = true;
            if (r.activated != active || r.enabled != active)
            {
                r.activated = active;
                r.enabled = active;
                if (!active)
                {
                    r.detectedTargets.Clear();
                    r.ResetRotators();
                    GroundControlReflection.ClearTargetsUsingRadar(r);
                }

                anyChanged = true;
            }
        }

        if (anyOperational)
        {
            entry.CurrentlyActive = active;
        }
        else if (anyChanged)
        {
            entry.CurrentlyActive = false;
        }
    }

    private static bool ClassifySamRadar(Unit unit)
    {
        if (unit is Ship)
        {
            return false;
        }

        if (unit.definition is VehicleDefinition vdef)
        {
            if (vdef.vehicleType == VehicleType.RDR)
            {
                return false;
            }

            if (vdef.vehicleType == VehicleType.R_SAM
                || GroundControlReflection.IsRadarGuidedAirDefence(vdef)
                || GroundControlReflection.IsAirDefence(vdef))
            {
                return true;
            }
        }

        if (unit.definition is BuildingDefinition bdef && bdef.buildingType == BuildingType.RDR)
        {
            return false;
        }

        return unit.weaponStations != null
            && unit.weaponStations.Count > 0
            && unit.definition != null
            && unit.definition.roleIdentity.antiAir > 0f;
    }

    private static bool IsMdsSite(Unit unit)
    {
        if (MdsBridgeBootstrap.IsMdsSelectableUnit?.Invoke(unit) == true)
        {
            return true;
        }

        return ModularSiteBehaviorType != null && unit.GetComponent(ModularSiteBehaviorType) != null;
    }

    private static bool HasOperationalActiveRadar(Radar[] radars)
    {
        for (int i = 0; i < radars.Length; i++)
        {
            Radar r = radars[i];
            if (r != null && r.IsOperational() && r.activated)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAnyOperationalRadar(Radar[] radars)
    {
        for (int i = 0; i < radars.Length; i++)
        {
            Radar r = radars[i];
            if (r != null && r.IsOperational())
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsServerAuthorityActive()
    {
        NetworkManagerNuclearOption? net = NetworkManagerNuclearOption.i;
        return net != null && net.Server != null && net.Server.Active;
    }

    private void PruneDeadEntries()
    {
        pruneScratch.Clear();
        for (int i = entryList.Count - 1; i >= 0; i--)
        {
            RadarUnitEntry e = entryList[i];
            if (e.Unit == null || e.Unit.disabled || e.Hq == null)
            {
                if (e.Unit != null)
                {
                    pruneScratch.Add(e.Unit);
                }

                entryList.RemoveAt(i);
            }
        }

        for (int i = 0; i < pruneScratch.Count; i++)
        {
            entries.Remove(pruneScratch[i]);
        }

        pruneScratch.Clear();
    }

    private void PruneStaleArmSweeps(float now)
    {
        if (nextArmSweepByMissileId.Count <= 32)
        {
            return;
        }

        staleMissileIdScratch.Clear();
        foreach (KeyValuePair<int, float> kv in nextArmSweepByMissileId)
        {
            if (now - kv.Value > 30f)
            {
                staleMissileIdScratch.Add(kv.Key);
            }
        }

        for (int i = 0; i < staleMissileIdScratch.Count; i++)
        {
            nextArmSweepByMissileId.Remove(staleMissileIdScratch[i]);
        }

        staleMissileIdScratch.Clear();
    }
}
