using System;
using System.Collections.Generic;
using System.Reflection;
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
    private static readonly MethodInfo? CommanderIsUnitRadarOfflineMethod =
        AccessTools.Method(GroundControlReflection.CommanderRadarServiceType, "IsUnitRadarOffline", new[] { typeof(Unit) });

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
    private readonly List<float> ewRangeScratch = new(32);
    private readonly List<GlobalPosition> hostileAirScratch = new(32);
    private readonly Dictionary<int, float> nextArmSweepByMissileId = new();
    private readonly List<int> staleMissileIdScratch = new(32);
    private readonly Dictionary<WeaponInfo, bool> aradWeaponCache = new();

    private float nextEmconTickAt;
    private float nextAradScanAt;

    private AiRadarEmconService()
    {
    }

    internal void CollectInactiveEmconUnitIds(List<PersistentID> output)
    {
        if (output == null)
        {
            return;
        }

        output.Clear();
        for (int i = 0; i < entryList.Count; i++)
        {
            RadarUnitEntry entry = entryList[i];
            if (entry.Unit != null
                && !entry.Unit.disabled
                && !entry.CurrentlyActive
                && entry.Unit.persistentID.IsValid)
            {
                output.Add(entry.Unit.persistentID);
            }
        }
    }

    public void ResetSession()
    {
        entries.Clear();
        entryList.Clear();
        pruneScratch.Clear();
        ewCoverageScratch.Clear();
        ewRangeScratch.Clear();
        hostileAirScratch.Clear();
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

        GroundControlReflection.NetworkRunAsHostTeam(() =>
        {
            FactionHQ? localHq = GroundControlReflection.GetLocalHq();
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (IsEmconAiFaction(hq, localHq))
                {
                    RefreshFactionRadarEntries(hq);
                    ProcessFactionEmcon(hq, now);
                }
            }
        });
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
        float levelNow = Time.timeSinceLevelLoad;
        GlobalPosition missilePos = missile.GlobalPosition();

        // Direct lock check on every TrackCurrentTarget call (fast O(1)), gated by sensor fairness
        if (targetedRadar != null && targetedRadar.activated)
        {
            Unit? directOwner = targetedRadar.GetAttachedUnit();
            if (directOwner != null
                && !directOwner.disabled
                && !(directOwner is Aircraft)
                && !(directOwner is Ship)
                && directOwner.NetworkHQ != null
                && !ReferenceEquals(directOwner.NetworkHQ, missile.NetworkHQ)
                && !GroundControlReflection.HasHumanPlayers(directOwner.NetworkHQ)
                && !IsMdsSite(directOwner)
                && !IsCommanderRadarOffline(directOwner)
                && FastMath.InRange(missilePos, directOwner.GlobalPosition(), ArmThreatRadiusMeters)
                && directOwner.NetworkHQ.trackingDatabase != null
                && directOwner.NetworkHQ.trackingDatabase.TryGetValue(missile.persistentID, out TrackingInfo? directTrack)
                && directTrack != null
                && levelNow - directTrack.lastSpottedTime <= 15f)
            {
                bool isSafeAi = false;
                GroundControlReflection.NetworkRunAsHostTeam(() =>
                {
                    FactionHQ? localHq = GroundControlReflection.GetLocalHq();
                    isSafeAi = IsEmconAiFaction(directOwner.NetworkHQ, localHq);
                });

                if (isSafeAi)
                {
                    RadarUnitEntry? directEntry = GetOrRegisterEntry(directOwner);
                    if (directEntry != null)
                    {
                        TriggerBlinkAndHandoff(directEntry, missile, now);
                    }
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

        Vector3 velDir = missile.rb != null && missile.rb.velocity.sqrMagnitude > 1f
            ? missile.rb.velocity.normalized
            : missile.transform.forward;

        for (int i = 0; i < entryList.Count; i++)
        {
            RadarUnitEntry entry = entryList[i];
            if (entry.Unit == null
                || entry.Unit.disabled
                || entry.Hq == null
                || ReferenceEquals(entry.Hq, missile.NetworkHQ)
                || GroundControlReflection.HasHumanPlayers(entry.Hq)
                || entry.Unit is Ship
                || IsCommanderRadarOffline(entry.Unit))
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
        float levelNow = Time.timeSinceLevelLoad;

        GroundControlReflection.NetworkRunAsHostTeam(() =>
        {
            FactionHQ? localHq = GroundControlReflection.GetLocalHq();
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (!IsEmconAiFaction(hq, localHq) || hq.trackingDatabase == null)
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
                            || entry.Unit is Ship
                            || IsCommanderRadarOffline(entry.Unit))
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
        });
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

        float range = entry.MaxWeaponRange > 0f ? entry.MaxWeaponRange : entry.MaxRadarRange;
        if (range > 0f && !HasBackupRadarWithin300m(entry) && HasCloseHostileAirThreat(entry.Hq, entry.Unit.GlobalPosition(), range * 0.55f))
        {
            return;
        }

        float blinkSeconds = Mathf.Clamp(AddonConfig.AiRadarBlinkSeconds.Value, 4f, 18f);
        entry.BlinkOffUntil = now + blinkSeconds;
        entry.BlinkCooldownUntil = Mathf.Max(now + BlinkReblinkCooldownSeconds, entry.BlinkOffUntil + 4f);
        SetUnitRadarsActive(entry, active: false, clearLocks: true);

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
                || candidate.Unit is Ship
                || !ReferenceEquals(candidate.Hq, entry.Hq)
                || now < candidate.BlinkOffUntil
                || IsCommanderRadarOffline(candidate.Unit))
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
            if (!pid.TryGetUnit(out Unit unit) || unit == null || unit.disabled || unit is Aircraft || unit is Ship)
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

        if (unit == null || unit.disabled || unit.NetworkHQ == null || unit is Aircraft || unit is Ship || IsMdsSite(unit))
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
        ewRangeScratch.Clear();

        // Pass 1: Keep non-SAM EW radars online (unless blinking from ARAD or manually turned off) and collect active EW/AWACS coverage
        for (int i = 0; i < entryList.Count; i++)
        {
            RadarUnitEntry entry = entryList[i];
            if (entry.Unit == null || entry.Unit.disabled || entry.Unit is Ship || !ReferenceEquals(entry.Hq, hq))
            {
                continue;
            }

            if (entry.IsSamRadar)
            {
                continue;
            }

            if (!IsCommanderRadarOffline(entry.Unit))
            {
                if (AddonConfig.EnableAiRadarEmcon.Value && now < entry.BlinkOffUntil)
                {
                    SetUnitRadarsActive(entry, active: false, clearLocks: true);
                    continue;
                }

                SetUnitRadarsActive(entry, active: true, clearLocks: false);
            }

            if (HasOperationalNonJammedActiveRadar(entry.Radars))
            {
                ewCoverageScratch.Add(entry.Unit.GlobalPosition());
                float effRange = entry.MaxRadarRange > 0f
                    ? Mathf.Min(EwCoverageRadiusMeters, entry.MaxRadarRange * 0.85f)
                    : EwCoverageRadiusMeters;
                ewRangeScratch.Add(effRange);
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
                    ewRangeScratch.Add(EwCoverageRadiusMeters);
                }
            }
        }

        MdsBridgeBootstrap.CollectFriendlyMdsEwPositions?.Invoke(hq, ewCoverageScratch);
        while (ewRangeScratch.Count < ewCoverageScratch.Count)
        {
            ewRangeScratch.Add(EwCoverageRadiusMeters);
        }

        CollectHostileAirPositionsOnce(hq);

        // Pass 2: Evaluate SAM radars for blink restore, handoff, or Ambush-EMCON
        for (int i = 0; i < entryList.Count; i++)
        {
            RadarUnitEntry entry = entryList[i];
            if (entry.Unit == null
                || entry.Unit.disabled
                || entry.Unit is Ship
                || !ReferenceEquals(entry.Hq, hq)
                || !entry.IsSamRadar
                || IsCommanderRadarOffline(entry.Unit))
            {
                continue;
            }

            if (AddonConfig.EnableAiRadarEmcon.Value && now < entry.BlinkOffUntil)
            {
                SetUnitRadarsActive(entry, active: false, clearLocks: true);
                continue;
            }

            if (now < entry.ForcedOnUntil)
            {
                SetUnitRadarsActive(entry, active: true, clearLocks: false);
                continue;
            }

            if (!AddonConfig.EnableAiAmbushEmcon.Value)
            {
                SetUnitRadarsActive(entry, active: true, clearLocks: false);
                continue;
            }

            GlobalPosition samPos = entry.Unit.GlobalPosition();
            if (!HasEwCoverageNear(samPos))
            {
                // Safety net: never blind SAMs without non-jammed EW/AWACS coverage
                SetUnitRadarsActive(entry, active: true, clearLocks: false);
                continue;
            }

            float maxRange = entry.MaxWeaponRange > 0f ? entry.MaxWeaponRange : entry.MaxRadarRange;
            if (maxRange <= 0f)
            {
                maxRange = 18000f;
            }

            float hotRadius = maxRange * 0.95f;
            float silentKeepRadius = Mathf.Clamp(maxRange * 1.10f, 14000f, 32000f);

            EvaluateAirThreatDistances(samPos, hotRadius, silentKeepRadius, out bool insideHot, out bool insideSilentKeep);

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

            SetUnitRadarsActive(entry, wantHot, clearLocks: false);
        }

        ewCoverageScratch.Clear();
        ewRangeScratch.Clear();
        hostileAirScratch.Clear();
    }

    private bool HasEwCoverageNear(GlobalPosition pos)
    {
        for (int i = 0; i < ewCoverageScratch.Count; i++)
        {
            float range = i < ewRangeScratch.Count ? ewRangeScratch[i] : EwCoverageRadiusMeters;
            if (FastMath.SquareDistance(pos, ewCoverageScratch[i]) <= range * range)
            {
                return true;
            }
        }

        return false;
    }

    private void CollectHostileAirPositionsOnce(FactionHQ hq)
    {
        hostileAirScratch.Clear();
        if (hq.trackingDatabase == null)
        {
            return;
        }

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

            if (levelNow - info.lastSpottedTime > 15f)
            {
                continue;
            }

            hostileAirScratch.Add(info.GetPosition());
        }
    }

    private void EvaluateAirThreatDistances(
        GlobalPosition samPos,
        float hotRadius,
        float silentKeepRadius,
        out bool insideHot,
        out bool insideSilentKeep)
    {
        insideHot = false;
        insideSilentKeep = false;

        float hotSq = hotRadius * hotRadius;
        float keepSq = silentKeepRadius * silentKeepRadius;

        for (int i = 0; i < hostileAirScratch.Count; i++)
        {
            float dSq = FastMath.SquareDistance(samPos, hostileAirScratch[i]);
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

    private bool HasBackupRadarWithin300m(RadarUnitEntry entry)
    {
        if (entry.Unit == null || entry.Hq == null)
        {
            return false;
        }

        GlobalPosition pos = entry.Unit.GlobalPosition();
        for (int i = 0; i < entryList.Count; i++)
        {
            RadarUnitEntry cand = entryList[i];
            if (cand == entry || cand.Unit == null || cand.Unit.disabled || !ReferenceEquals(cand.Hq, entry.Hq))
            {
                continue;
            }

            if (FastMath.InRange(pos, cand.Unit.GlobalPosition(), 300f) && HasOperationalActiveRadar(cand.Radars))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasCloseHostileAirThreat(FactionHQ? hq, GlobalPosition samPos, float radiusMeters)
    {
        if (hq == null || hq.trackingDatabase == null || radiusMeters <= 0f)
        {
            return false;
        }

        float rSq = radiusMeters * radiusMeters;
        float levelNow = Time.timeSinceLevelLoad;
        foreach (KeyValuePair<PersistentID, TrackingInfo> kv in hq.trackingDatabase)
        {
            TrackingInfo info = kv.Value;
            if (info == null || !info.TryGetUnit(out Unit u) || u == null || u.disabled)
            {
                continue;
            }

            if (!(u is Aircraft) && !(u is Missile))
            {
                continue;
            }

            if (u.NetworkHQ == null || ReferenceEquals(u.NetworkHQ, hq) || levelNow - info.lastSpottedTime > 15f)
            {
                continue;
            }

            if (FastMath.SquareDistance(samPos, info.GetPosition()) <= rSq)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasOperationalNonJammedActiveRadar(Radar[] radars)
    {
        for (int i = 0; i < radars.Length; i++)
        {
            Radar r = radars[i];
            if (r != null && r.IsOperational() && r.activated && !r.IsJammed())
            {
                return true;
            }
        }

        return false;
    }

    private static void SetUnitRadarsActive(RadarUnitEntry entry, bool active, bool clearLocks = false)
    {
        if (active && IsCommanderRadarOffline(entry.Unit))
        {
            return;
        }

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
            bool wantEnabled = r.IsJammed() || (active && !GameManager.IsHeadless);
            if (r.activated != active || r.enabled != wantEnabled)
            {
                r.activated = active;
                r.enabled = wantEnabled;
                if (!active)
                {
                    r.detectedTargets.Clear();
                    r.ResetRotators();
                    if (clearLocks)
                    {
                        GroundControlReflection.ClearTargetsUsingRadar(r);
                    }
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

        if (anyChanged && entry.Unit != null && entry.Unit.persistentID.IsValid)
        {
            GcreNetworkSync.BroadcastRadarEmcon(entry.Unit.persistentID.Id, active);
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

    private static bool IsCommanderRadarOffline(Unit? unit)
        => unit != null && CommanderIsUnitRadarOfflineMethod?.Invoke(null, new object?[] { unit }) is bool b && b;

    private static bool IsEmconAiFaction(FactionHQ? hq, FactionHQ? localHq)
        => hq != null
            && ((NetworkBehaviour)hq).IsServer
            && !GroundControlReflection.HasHumanPlayers(hq)
            && GroundControlReflection.IsSafeAiFaction(hq, localHq);

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
            if (e.Unit == null
                || e.Unit.disabled
                || e.Unit is Ship
                || e.Hq == null
                || GroundControlReflection.HasHumanPlayers(e.Hq))
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
