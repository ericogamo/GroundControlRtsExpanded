using System.Collections.Generic;
using Mirage;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal enum AiStrategicPhase
{
    OpeningEconomy = 0,
    MidExpansion = 1,
    FullSpectrum = 2,
    EmergencyReconstruction = 3,
}

internal readonly struct PlayerThreatSnapshot
{
    internal PlayerThreatSnapshot(int aircraft, int heavyArmor, int ships, int artillery, int samRadars)
    {
        Aircraft = aircraft;
        HeavyArmor = heavyArmor;
        Ships = ships;
        Artillery = artillery;
        SamRadars = samRadars;
    }

    internal int Aircraft { get; }
    internal int HeavyArmor { get; }
    internal int Ships { get; }
    internal int Artillery { get; }
    internal int SamRadars { get; }
}

internal sealed class AiStrategicDirectorService : IAddonResetSession
{
    private const float ThreatCacheSeconds = 2f;
    private const float TelemetryIntervalSeconds = 60f;

    private sealed class FactionDirectorState
    {
        internal float PeakGrossIncome;
        internal AiStrategicPhase? LastLoggedPhase;
        internal float LastTelemetryAt = -120f;
        internal PlayerThreatSnapshot ThreatSnapshot;
        internal float ThreatCachedAt = -10f;
    }

    internal static AiStrategicDirectorService Instance { get; } = new();

    private readonly Dictionary<FactionHQ, FactionDirectorState> factionStates = new();

    private AiStrategicDirectorService()
    {
    }

    public void ResetSession()
    {
        factionStates.Clear();
    }

    private FactionDirectorState GetOrCreateState(FactionHQ hq)
    {
        if (!factionStates.TryGetValue(hq, out FactionDirectorState state))
        {
            state = new FactionDirectorState();
            factionStates[hq] = state;
        }

        return state;
    }

    internal AiStrategicPhase EvaluatePhase(object? ecoInstance, FactionHQ? hq)
    {
        if (hq == null)
        {
            return AiStrategicPhase.OpeningEconomy;
        }

        FactionDirectorState state = GetOrCreateState(hq);
        float enemyGross = ecoInstance != null ? GroundControlReflection.GetHqIncomePerMinute(ecoInstance, hq) : 0f;
        float enemyNet = GroundControlReflection.GetEnemyNetIncomePerMinute(hq);
        int goldMines = ecoInstance != null ? GroundControlReflection.CountGoldMines(ecoInstance, hq) : 0;
        int bestDepot = GroundControlReflection.GetBestDepotLevel(hq);
        int techTier = DynamicCapAndTechService.IsActive ? DynamicCapAndTechService.Instance.GetSnapshot(hq).TechTier : 1;

        if (enemyGross > state.PeakGrossIncome)
        {
            state.PeakGrossIncome = enemyGross;
        }

        int groundCombat = CountLiveGroundCombat(hq);
        if ((state.PeakGrossIncome >= 250f && enemyGross < state.PeakGrossIncome * 0.45f)
            || (bestDepot >= 1 && groundCombat < 4 && hq.factionFunds < 600f && (goldMines >= 1 || bestDepot >= 2)))
        {
            return AiStrategicPhase.EmergencyReconstruction;
        }

        if ((goldMines == 0 && bestDepot < 2) || enemyGross < 150f)
        {
            return AiStrategicPhase.OpeningEconomy;
        }

        if (bestDepot >= 3 && techTier >= 2 && enemyNet >= 400f)
        {
            return AiStrategicPhase.FullSpectrum;
        }

        return AiStrategicPhase.MidExpansion;
    }

    internal PlayerThreatSnapshot ReadPlayerThreat(FactionHQ? aiHq)
    {
        if (aiHq == null)
        {
            return default;
        }

        FactionDirectorState state = GetOrCreateState(aiHq);
        float now = Time.unscaledTime;
        if (now < state.ThreatCachedAt + ThreatCacheSeconds)
        {
            return state.ThreatSnapshot;
        }

        int aircraft = 0;
        int heavyArmor = 0;
        int ships = 0;
        int artillery = 0;
        int samRadars = 0;

        if (aiHq.trackingDatabase != null)
        {
            float levelTime = Time.timeSinceLevelLoad;
            foreach (KeyValuePair<PersistentID, TrackingInfo> kv in aiHq.trackingDatabase)
            {
                TrackingInfo? info = kv.Value;
                if (info == null
                    || !info.TryGetUnit(out Unit unit)
                    || unit == null
                    || unit.disabled
                    || ReferenceEquals(unit.NetworkHQ, aiHq))
                {
                    continue;
                }

                float age = levelTime - info.lastSpottedTime;
                if (unit is Aircraft)
                {
                    if (age <= 45f)
                    {
                        aircraft++;
                    }
                }
                else if (unit is Ship)
                {
                    if (age <= 90f)
                    {
                        ships++;
                    }
                }
                else if (unit is GroundVehicle && unit.definition is VehicleDefinition vdef)
                {
                    if (age > 90f)
                    {
                        continue;
                    }

                    if (vdef.vehicleType == VehicleType.ART)
                    {
                        artillery++;
                    }
                    else if (vdef.value >= 20f
                        && !GroundControlReflection.IsAirDefence(vdef)
                        && !GroundControlReflection.IsBuilderVehicle(vdef)
                        && !GroundControlReflection.IsSupplyVehicle(vdef))
                    {
                        heavyArmor++;
                    }

                    if (vdef.vehicleType == VehicleType.R_SAM
                        || vdef.vehicleType == VehicleType.RDR
                        || unit.HasRadarEmission())
                    {
                        samRadars++;
                    }
                }
                else if (unit is Building && age <= 120f)
                {
                    if (unit.HasRadarEmission()
                        || (unit.definition is BuildingDefinition bdef && bdef.buildingType == BuildingType.RDR))
                    {
                        samRadars++;
                    }
                }
            }
        }

        if (MdsBridgeBootstrap.HasEmittingHostileMdsRadar?.Invoke(aiHq) == true && samRadars == 0)
        {
            samRadars = 1;
        }

        var snapshot = new PlayerThreatSnapshot(aircraft, heavyArmor, ships, artillery, samRadars);
        state.ThreatSnapshot = snapshot;
        state.ThreatCachedAt = now;
        return snapshot;
    }

    internal int GetScaledIadsTarget(FactionHQ? hq, int bases)
    {
        int safeBases = Mathf.Max(1, bases);
        int bestDepot = GroundControlReflection.GetBestDepotLevel(hq);
        if (bestDepot < 2)
        {
            return Mathf.Clamp(2 + safeBases / 2, 2, 5);
        }

        PlayerThreatSnapshot threat = ReadPlayerThreat(hq);
        if (bestDepot == 2)
        {
            return Mathf.Clamp(3 + 2 * safeBases + Mathf.Min(4, threat.Aircraft / 2), 5, 18);
        }

        return Mathf.Clamp(5 + 3 * safeBases + Mathf.Min(8, threat.Aircraft / 2), 8, 30);
    }

    internal bool ShouldSuppressOpeningDepotSpam(object? ecoInstance, FactionHQ? hq)
        => hq != null
            && GroundControlReflection.CountGoldMines(ecoInstance, hq) == 0
            && GroundControlReflection.GetBestDepotLevel(hq) < 2;

    internal bool CanSpendOnCityBuilding(
        object? ecoInstance,
        FactionHQ hq,
        RtsBuildingKind kind,
        float cost,
        bool isUpgrade,
        bool isUrgentCapRelief)
    {
        AiStrategicPhase phase = EvaluatePhase(ecoInstance, hq);
        int goldMines = ecoInstance != null ? GroundControlReflection.CountGoldMines(ecoInstance, hq) : 0;
        int bestDepot = GroundControlReflection.GetBestDepotLevel(hq);
        float enemyGross = ecoInstance != null ? GroundControlReflection.GetHqIncomePerMinute(ecoInstance, hq) : 0f;
        FactionCapSnapshot snap = DynamicCapAndTechService.Instance.GetSnapshot(hq);

        if (phase == AiStrategicPhase.OpeningEconomy || (goldMines == 0 && bestDepot < 2))
        {
            int totalMines = ecoInstance != null ? GroundControlReflection.CountMines(ecoInstance, hq) : 0;
            if (kind == RtsBuildingKind.CommandHq
                && !isUpgrade
                && snap.CommandHqCount == 0
                && ((enemyGross >= 75f && totalMines >= 2) || enemyGross >= 125f))
            {
                return hq.factionFunds >= cost + 120f;
            }

            if (kind == RtsBuildingKind.Barracks && !isUpgrade && snap.BarracksCount == 0 && isUrgentCapRelief)
            {
                return hq.factionFunds >= cost + 60f;
            }

            return false;
        }

        if (phase == AiStrategicPhase.EmergencyReconstruction)
        {
            bool isLuxury = (kind == RtsBuildingKind.CommandHq && isUpgrade)
                || kind == RtsBuildingKind.AbmPlant
                || kind == RtsBuildingKind.AbmRadar
                || kind == RtsBuildingKind.AbmSilo
                || (kind == RtsBuildingKind.PilotAcademy && (isUpgrade || snap.PilotAcademyCount >= 1))
                || (kind == RtsBuildingKind.LogisticsCamp && (isUpgrade || snap.LogisticsCampCount >= 1));

            if (isLuxury && hq.factionFunds < cost + 650f)
            {
                return false;
            }
        }

        float buildReserve = GroundControlReflection.GetEnemyBuildReserve(hq, out bool resGoldMine, out bool resIncome);
        object? enemyInstance = GroundControlReflection.GetEnemyInstance();
        object? cmdState = GroundControlReflection.TryGetCommanderState(enemyInstance, hq);
        float mineFund = GroundControlReflection.GetStateFloat(cmdState, GroundControlReflection.StateMineFundProp);
        float protectedReserve = Mathf.Max(buildReserve, mineFund);

        if (resGoldMine || resIncome || protectedReserve >= 250f)
        {
            float reserveShare = isUrgentCapRelief ? 0.35f : 0.90f;
            if (hq.factionFunds < cost + protectedReserve * reserveShare)
            {
                return false;
            }
        }

        return true;
    }

    internal void LogTelemetryIfDue(object? ecoInstance, FactionHQ hq)
    {
        if (hq == null || !((NetworkBehaviour)hq).IsServer)
        {
            return;
        }

        FactionDirectorState state = GetOrCreateState(hq);
        AiStrategicPhase phase = EvaluatePhase(ecoInstance, hq);
        float now = Time.unscaledTime;
        bool phaseChanged = !state.LastLoggedPhase.HasValue || state.LastLoggedPhase.Value != phase;
        if (!phaseChanged && now < state.LastTelemetryAt + TelemetryIntervalSeconds)
        {
            return;
        }

        state.LastLoggedPhase = phase;
        state.LastTelemetryAt = now;

        float enemyGross = ecoInstance != null ? GroundControlReflection.GetHqIncomePerMinute(ecoInstance, hq) : 0f;
        float enemyNet = GroundControlReflection.GetEnemyNetIncomePerMinute(hq);
        float floor = AiEconomySettings.GetActiveProfile().NetIncomeTarget;
        float playerGross = ecoInstance != null ? AiEconomySettings.GetPlayerReferenceIncomePerMinute(ecoInstance) : 0f;
        int goldMines = ecoInstance != null ? GroundControlReflection.CountGoldMines(ecoInstance, hq) : 0;
        int totalMines = ecoInstance != null ? GroundControlReflection.CountMines(ecoInstance, hq) : 0;
        int bestDepot = GroundControlReflection.GetBestDepotLevel(hq);
        int dockLvl = GroundControlReflection.GetNavalDockLevel(hq);
        int fac = GroundControlReflection.CountFactories(hq);

        object? enemyInstance = GroundControlReflection.GetEnemyInstance();
        object? cmdState = GroundControlReflection.TryGetCommanderState(enemyInstance, hq);
        float airFund = GroundControlReflection.GetStateFloat(cmdState, GroundControlReflection.StateAirFundProp);
        float reserve = GroundControlReflection.GetEnemyBuildReserve(hq, out _, out _);
        PlayerThreatSnapshot threat = ReadPlayerThreat(hq);

        AddonLog.Info(
            $"[AI Director] {hq.faction?.name ?? "AI"} | Phase={phase} | Funds={hq.factionFunds:0} Cr | " +
            $"Income={enemyGross:0} gross / {enemyNet:0} net (Floor={floor:0}, Player={playerGross:0}) | " +
            $"Mines={goldMines}G/{totalMines}T | Depot=L{bestDepot} | Dock=L{dockLvl} | Fac={fac} | " +
            $"AirFund={airFund:0} | Reserve={reserve:0} | " +
            $"Threat(Air={threat.Aircraft},Arm={threat.HeavyArmor},Ship={threat.Ships},Rad={threat.SamRadars})");
    }

    private static int CountLiveGroundCombat(FactionHQ hq)
    {
        if (hq.factionUnits == null)
        {
            return 0;
        }

        int count = 0;
        foreach (PersistentID pid in hq.factionUnits)
        {
            if (!pid.TryGetUnit(out Unit unit)
                || unit == null
                || unit.disabled
                || unit is not GroundVehicle
                || unit.definition is not VehicleDefinition vdef)
            {
                continue;
            }

            if (!GroundControlReflection.IsBuilderVehicle(vdef) && !GroundControlReflection.IsSupplyVehicle(vdef))
            {
                count++;
            }
        }

        return count;
    }
}
