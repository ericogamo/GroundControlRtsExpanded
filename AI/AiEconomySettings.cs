using System;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal readonly struct AiEconomyProfile
{
    internal AiEconomyProfile(
        string name,
        bool isVanilla,
        float netIncomeTarget,
        int maxGoldMines,
        float headStartMultiplier,
        float championsFlatStipend,
        float championsPerPlayerStipend,
        int freeGoldMines,
        float mineIncomeMultiplier,
        int maxFactories,
        float fairPlayIncomeCapMultiplier)
    {
        Name = name;
        IsVanilla = isVanilla;
        NetIncomeTarget = netIncomeTarget;
        MaxGoldMines = maxGoldMines;
        HeadStartMultiplier = headStartMultiplier;
        ChampionsFlatStipend = championsFlatStipend;
        ChampionsPerPlayerStipend = championsPerPlayerStipend;
        FreeGoldMines = freeGoldMines;
        MineIncomeMultiplier = mineIncomeMultiplier;
        MaxFactories = maxFactories;
        FairPlayIncomeCapMultiplier = fairPlayIncomeCapMultiplier;
    }

    internal string Name { get; }
    internal bool IsVanilla { get; }
    internal float NetIncomeTarget { get; }
    internal int MaxGoldMines { get; }
    internal float HeadStartMultiplier { get; }
    internal float ChampionsFlatStipend { get; }
    internal float ChampionsPerPlayerStipend { get; }
    internal int FreeGoldMines { get; }
    internal float MineIncomeMultiplier { get; }
    internal int MaxFactories { get; }
    internal float FairPlayIncomeCapMultiplier { get; }
}

internal static class AiEconomySettings
{
    internal static AiEconomyProfile GetActiveProfile()
    {
        string preset = GcreNetworkSync.EffectiveEconomyPreset;
        if (string.Equals(preset, "Vanilla", StringComparison.OrdinalIgnoreCase))
        {
            return new AiEconomyProfile(
                name: "Vanilla",
                isVanilla: true,
                netIncomeTarget: 2000f,
                maxGoldMines: 20,
                headStartMultiplier: 3.0f,
                championsFlatStipend: 100f,
                championsPerPlayerStipend: 75f,
                freeGoldMines: 2,
                mineIncomeMultiplier: 1.5f,
                maxFactories: 32,
                fairPlayIncomeCapMultiplier: 0f);
        }

        if (string.Equals(preset, "BalancedPlus", StringComparison.OrdinalIgnoreCase))
        {
            return new AiEconomyProfile(
                name: "BalancedPlus",
                isVanilla: false,
                netIncomeTarget: 750f,
                maxGoldMines: 12,
                headStartMultiplier: 1.2f,
                championsFlatStipend: 35f,
                championsPerPlayerStipend: 15f,
                freeGoldMines: 1,
                mineIncomeMultiplier: 1.15f,
                maxFactories: 20,
                fairPlayIncomeCapMultiplier: 1.25f);
        }

        if (string.Equals(preset, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            return new AiEconomyProfile(
                name: "Custom",
                isVanilla: false,
                netIncomeTarget: AddonConfig.AiNetIncomeTarget.Value,
                maxGoldMines: AddonConfig.AiMaxGoldMines.Value,
                headStartMultiplier: AddonConfig.AiHeadStartMultiplier.Value,
                championsFlatStipend: AddonConfig.AiChampionsFlatStipend.Value,
                championsPerPlayerStipend: 0f,
                freeGoldMines: AddonConfig.AiFreeGoldMines.Value,
                mineIncomeMultiplier: AddonConfig.AiMineIncomeMultiplier.Value,
                maxFactories: AddonConfig.AiMaxFactories.Value,
                fairPlayIncomeCapMultiplier: AddonConfig.AiIncomeCapMultiplier.Value);
        }

        // Default: "Fair" (guaranteed 500/min net floor, dynamically tracks Player Gross + 5%, grows with player)
        return new AiEconomyProfile(
            name: "Fair",
            isVanilla: false,
            netIncomeTarget: 500f,
            maxGoldMines: 8,
            headStartMultiplier: 1.0f,
            championsFlatStipend: 0f,
            championsPerPlayerStipend: 0f,
            freeGoldMines: 0,
            mineIncomeMultiplier: 1.0f,
            maxFactories: 16,
            fairPlayIncomeCapMultiplier: 1.05f);
    }

    internal static bool TryGetPlayerReferenceEconomy(
        object? ecoInstance,
        out float playerGross,
        out int playerGoldMines,
        out int playerFactories)
    {
        playerGross = 0f;
        playerGoldMines = 0;
        playerFactories = 0;
        if (ecoInstance == null)
        {
            return false;
        }

        bool foundAny = false;
        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        if (localHq != null && !GroundControlReflection.IsSafeAiFaction(localHq, localHq))
        {
            foundAny = true;
            playerGross = GroundControlReflection.GetHqIncomePerMinute(ecoInstance, localHq);
            playerGoldMines = GroundControlReflection.CountGoldMines(ecoInstance, localHq);
            playerFactories = GroundControlReflection.CountFactories(localHq);
        }

        foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
        {
            if (hq == null
                || ReferenceEquals(hq, localHq)
                || (GroundControlReflection.IsSafeAiFaction(hq, localHq) && !GroundControlReflection.HasHumanPlayers(hq)))
            {
                continue;
            }

            foundAny = true;
            float income = GroundControlReflection.GetHqIncomePerMinute(ecoInstance, hq);
            if (income > playerGross)
            {
                playerGross = income;
            }

            int mines = GroundControlReflection.CountGoldMines(ecoInstance, hq);
            if (mines > playerGoldMines)
            {
                playerGoldMines = mines;
            }

            int factories = GroundControlReflection.CountFactories(hq);
            if (factories > playerFactories)
            {
                playerFactories = factories;
            }
        }

        return foundAny;
    }

    internal static float GetPlayerReferenceIncomePerMinute(object ecoInstance)
        => TryGetPlayerReferenceEconomy(ecoInstance, out float playerGross, out _, out _) ? playerGross : 0f;

    internal static int GetEffectiveMaxGoldMines(object? ecoInstance, FactionHQ hq, in AiEconomyProfile profile)
    {
        if (ecoInstance == null
            || profile.FairPlayIncomeCapMultiplier <= 0f
            || string.Equals(profile.Name, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            return profile.MaxGoldMines;
        }

        float enemyGross = GroundControlReflection.GetHqIncomePerMinute(ecoInstance, hq);
        float enemyNet = GroundControlReflection.GetEnemyNetIncomePerMinute(hq);
        float enemyUpkeep = Mathf.Max(0f, enemyGross - enemyNet);
        float targetGross = TryGetPlayerReferenceEconomy(ecoInstance, out float playerGross, out int playerGoldMines, out _)
            ? playerGross * profile.FairPlayIncomeCapMultiplier
            : 0f;

        float neededGross = Mathf.Max(profile.NetIncomeTarget + enemyUpkeep, targetGross);
        int minesForTarget = Mathf.CeilToInt(neededGross / 300f) + 1;
        int effectiveMax = Mathf.Max(profile.MaxGoldMines, Mathf.Max(playerGoldMines + 1, minesForTarget));
        return Mathf.Clamp(effectiveMax, profile.MaxGoldMines, 24);
    }

    internal static int GetEffectiveMaxFactories(object? ecoInstance, FactionHQ hq, in AiEconomyProfile profile)
    {
        _ = hq;
        if (ecoInstance == null
            || profile.FairPlayIncomeCapMultiplier <= 0f
            || string.Equals(profile.Name, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            return profile.MaxFactories;
        }

        if (TryGetPlayerReferenceEconomy(ecoInstance, out _, out _, out int playerFactories))
        {
            int effectiveMax = Mathf.Max(profile.MaxFactories, playerFactories + 4);
            return Mathf.Clamp(effectiveMax, profile.MaxFactories, 24);
        }

        return profile.MaxFactories;
    }

    internal static float GetNextMineBuildGain(object ecoInstance, FactionHQ hq)
    {
        object? kind = GroundControlReflection.ChooseEnemyIncomeKind(ecoInstance, hq);
        if (kind != null)
        {
            float gain = GroundControlReflection.GetStageIncomePerMinute(kind, 1);
            if (gain > 0f)
            {
                return gain;
            }
        }

        return GroundControlReflection.CountGoldMines(ecoInstance, hq) >= 1 ? 100f : 25f;
    }

    internal static float GetNextIncomeUpgradeGain(object ecoInstance, FactionHQ hq)
    {
        Unit? unit = GroundControlReflection.FindEnemyIncomeUpgrade(ecoInstance, hq, out _);
        if (unit == null)
        {
            return 0f;
        }

        object? kind = GroundControlReflection.GetIncomeKind(ecoInstance, unit);
        return kind != null ? GroundControlReflection.GetStageIncomePerMinute(kind, 1) : 0f;
    }

    internal static bool IsExceedingFairPlayIncomeCap(
        object ecoInstance,
        FactionHQ hq,
        in AiEconomyProfile profile,
        float pendingGain = 0f)
    {
        if (profile.FairPlayIncomeCapMultiplier <= 0f)
        {
            return false;
        }

        // Guaranteed net floor after upkeep so defense upkeep never stalls AI economy below NetIncomeTarget.
        if (GroundControlReflection.GetEnemyNetIncomePerMinute(hq) < profile.NetIncomeTarget)
        {
            return false;
        }

        if (!TryGetPlayerReferenceEconomy(ecoInstance, out float playerGross, out _, out _))
        {
            // AI-vs-AI match with no human reference: net target is already met.
            return true;
        }

        float enemyGross = GroundControlReflection.GetHqIncomePerMinute(ecoInstance, hq);
        float targetGross = playerGross * profile.FairPlayIncomeCapMultiplier;
        float remainingGap = targetGross - enemyGross;
        float minGapRequired = pendingGain > 0f ? Mathf.Max(5f, pendingGain * 0.5f) : 5f;
        return remainingGap < minGapRequired;
    }

    internal static bool IsIncomeUpgradeBlocked(object ecoInstance, FactionHQ hq, in AiEconomyProfile profile)
    {
        float gain = GetNextIncomeUpgradeGain(ecoInstance, hq);
        return IsExceedingFairPlayIncomeCap(ecoInstance, hq, profile, gain);
    }

    internal static bool IsMineBuildBlocked(object ecoInstance, FactionHQ hq, in AiEconomyProfile profile)
    {
        int effectiveMaxMines = GetEffectiveMaxGoldMines(ecoInstance, hq, profile);
        int goldMines = GroundControlReflection.CountGoldMines(ecoInstance, hq);
        int totalIncome = GroundControlReflection.CountMines(ecoInstance, hq);
        if (goldMines >= effectiveMaxMines || totalIncome >= effectiveMaxMines + 6)
        {
            return true;
        }

        float gain = GetNextMineBuildGain(ecoInstance, hq);
        return IsExceedingFairPlayIncomeCap(ecoInstance, hq, profile, gain);
    }

    internal static bool IsBelowEffectiveIncomeTarget(object? ecoInstance, FactionHQ hq, in AiEconomyProfile profile)
    {
        if (ecoInstance != null && IsMineBuildBlocked(ecoInstance, hq, profile))
        {
            return false;
        }

        if (GroundControlReflection.GetEnemyNetIncomePerMinute(hq) < profile.NetIncomeTarget)
        {
            return true;
        }

        if (ecoInstance != null
            && profile.FairPlayIncomeCapMultiplier > 0f
            && TryGetPlayerReferenceEconomy(ecoInstance, out float playerGross, out _, out _)
            && playerGross > 0f)
        {
            float enemyGross = GroundControlReflection.GetHqIncomePerMinute(ecoInstance, hq);
            float targetGross = playerGross * profile.FairPlayIncomeCapMultiplier;
            float nextGain = GetNextMineBuildGain(ecoInstance, hq);
            return (targetGross - enemyGross) >= Mathf.Max(5f, nextGain * 0.5f);
        }

        return false;
    }
}
