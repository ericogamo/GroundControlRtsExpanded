using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mirage;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal static class BaseCaptureIncomeService
{
    internal const float IncomeIntervalShareOfMinute = 0.25f;
    private const float OpeningGraceSeconds = 15f;
    private const float CaptureBountyCooldownSeconds = 300f;

    private static readonly Dictionary<(Airbase Base, FactionHQ Hq), float> BountyCooldownUntil = new();
    private static readonly Dictionary<Airbase, FactionHQ> InitialChampionsBaseOwners = new();
    private static readonly HashSet<Airbase> SeenBasesScratch = new();
    private static readonly List<(Airbase Base, FactionHQ Hq)> StaleCooldownKeysScratch = new();

    internal static void ResetSession()
    {
        BountyCooldownUntil.Clear();
        InitialChampionsBaseOwners.Clear();
    }

    internal static void RecordChampionsInitialBases()
    {
        if (!GroundControlReflection.IsChampionsMission() || FactionRegistry.airbaseLookup == null)
        {
            return;
        }

        InitialChampionsBaseOwners.Clear();
        foreach (Airbase? ab in FactionRegistry.airbaseLookup.Values)
        {
            if (IsEligibleCapturableBase(ab) && ab!.CurrentHQ != null)
            {
                InitialChampionsBaseOwners[ab] = ab.CurrentHQ;
            }
        }
    }

    internal static bool IsEligibleCapturableBase(Airbase? ab)
    {
        return ab != null
            && !ab.disabled
            && !ab.AttachedAirbase
            && ab.SavedAirbase != null
            && ab.SavedAirbase.Capturable;
    }

    internal static int CountHeldCapturableBases(FactionHQ? hq)
    {
        if (hq == null)
        {
            return 0;
        }

        SeenBasesScratch.Clear();
        int count = 0;
        foreach (Airbase ab in hq.GetAirbases())
        {
            if (IsEligibleCapturableBase(ab) && SeenBasesScratch.Add(ab))
            {
                count++;
            }
        }

        SeenBasesScratch.Clear();
        return count;
    }

    internal static int CountCapturedBonusBases(FactionHQ? hq)
    {
        if (!AddonConfig.ModEnabled.Value || !GcreNetworkSync.EffectiveEnableCapturedBaseIncome || hq == null)
        {
            return 0;
        }

        SeenBasesScratch.Clear();
        int totalHeld = 0;
        int nonInitialHeld = 0;
        foreach (Airbase ab in hq.GetAirbases())
        {
            if (!IsEligibleCapturableBase(ab) || !SeenBasesScratch.Add(ab))
            {
                continue;
            }

            totalHeld++;
            if (!InitialChampionsBaseOwners.TryGetValue(ab, out FactionHQ? initialOwner)
                || !ReferenceEquals(initialOwner, hq))
            {
                nonInitialHeld++;
            }
        }

        SeenBasesScratch.Clear();
        return Mathf.Min(Mathf.Max(0, totalHeld - 1), nonInitialHeld);
    }

    internal static float GetCapturedBaseIncomePerMinute(FactionHQ? hq)
    {
        int bonusBases = CountCapturedBonusBases(hq);
        if (bonusBases <= 0)
        {
            return 0f;
        }

        return bonusBases * Mathf.Max(0f, GcreNetworkSync.EffectiveCapturedBaseIncomePerMinute);
    }

    internal static void OnAirbaseCaptured(Airbase airbase, FactionHQ? previousHq, FactionHQ? newHq)
    {
        if (!AddonConfig.ModEnabled.Value
            || !GcreNetworkSync.EffectiveEnableCapturedBaseIncome
            || !IsEligibleCapturableBase(airbase)
            || newHq == null
            || ReferenceEquals(previousHq, newHq)
            || !((NetworkBehaviour)newHq).IsServer)
        {
            return;
        }

        if (Time.timeSinceLevelLoad < OpeningGraceSeconds
            || DuelOppositeAirbaseStartPatch.IsRelocating
            || ChampionsDynamicStartBasePatch.IsDealing)
        {
            return;
        }

        PruneStaleCooldowns();

        float now = Time.time;
        float until = 0f;
        BountyCooldownUntil.TryGetValue((airbase, newHq), out until);

        float bounty = Mathf.Max(0f, AddonConfig.BaseCaptureBounty.Value);
        bool paidBounty = false;
        if (bounty > 0f && now >= until)
        {
            newHq.AddFunds(bounty);
            BountyCooldownUntil[(airbase, newHq)] = now + CaptureBountyCooldownSeconds;
            paidBounty = true;
        }

        float rate = Mathf.Max(0f, GcreNetworkSync.EffectiveCapturedBaseIncomePerMinute);
        int bonusCount = CountCapturedBonusBases(newHq);
        float effectiveAddedRate = bonusCount > 0 ? rate : 0f;
        if (!paidBounty && effectiveAddedRate <= 0f)
        {
            return;
        }

        string label = GroundControlReflection.GetAirbaseLabel(airbase);
        string factionName = newHq.faction != null && !string.IsNullOrEmpty(newHq.faction.factionName)
            ? newHq.faction.factionName
            : newHq.name;

        AddonLog.Info(
            $"[Economy] {factionName} captured {label}: bounty={(paidBounty ? $"+${bounty:0}m" : "$0m (cd)")}, ongoing=+${effectiveAddedRate:0}m/min ({bonusCount} extra bases).");

        string upperLabel = label.ToUpperInvariant();
        string toastText;
        if (paidBounty && effectiveAddedRate > 0f)
        {
            toastText = $"BASE CAPTURED: {upperLabel} (+${bounty:0}m | +${effectiveAddedRate:0}m/min)";
        }
        else if (paidBounty)
        {
            toastText = $"BASE CAPTURED: {upperLabel} (+${bounty:0}m)";
        }
        else
        {
            toastText = $"BASE CAPTURED: {upperLabel} (+${effectiveAddedRate:0}m/min)";
        }

        if (ReferenceEquals(newHq, GroundControlReflection.GetLocalHq()))
        {
            GroundControlReflection.NotifyAlert(toastText);
        }

        GcreNetworkSync.BroadcastFactionToast(newHq, isAlert: true, toastText);
    }

    private static void PruneStaleCooldowns()
    {
        if (BountyCooldownUntil.Count == 0)
        {
            return;
        }

        StaleCooldownKeysScratch.Clear();
        foreach ((Airbase Base, FactionHQ Hq) key in BountyCooldownUntil.Keys)
        {
            if (key.Base == null || key.Hq == null)
            {
                StaleCooldownKeysScratch.Add(key);
            }
        }

        for (int i = 0; i < StaleCooldownKeysScratch.Count; i++)
        {
            BountyCooldownUntil.Remove(StaleCooldownKeysScratch[i]);
        }

        StaleCooldownKeysScratch.Clear();
    }
}

[HarmonyPatch]
internal static class PayCapturedBaseIncomePatch
{
    private static bool Prepare() => GroundControlReflection.PayIncomeMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.PayIncomeMethod!;

    private static void Postfix()
    {
        if (!AddonConfig.ModEnabled.Value
            || !GcreNetworkSync.EffectiveEnableCapturedBaseIncome
            || GcreNetworkSync.EffectiveCapturedBaseIncomePerMinute <= 0f)
        {
            return;
        }

        foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
        {
            if (hq == null || !((NetworkBehaviour)hq).IsServer)
            {
                continue;
            }

            float perMinute = BaseCaptureIncomeService.GetCapturedBaseIncomePerMinute(hq);
            if (perMinute > 0f)
            {
                hq.AddFunds(perMinute * BaseCaptureIncomeService.IncomeIntervalShareOfMinute);
            }
        }
    }
}

[HarmonyPatch]
internal static class GetHqIncomePerMinuteBaseBonusPatch
{
    private static bool Prepare() => GroundControlReflection.GetHqIncomePerMinuteMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.GetHqIncomePerMinuteMethod!;

    private static void Postfix(FactionHQ hq, ref float __result)
    {
        __result += BaseCaptureIncomeService.GetCapturedBaseIncomePerMinute(hq);
    }
}

[HarmonyPatch]
internal static class RefreshLedgerBaseBonusPatch
{
    private static bool Prepare()
        => GroundControlReflection.EcoRefreshLedgerMethod != null
        && GroundControlReflection.EcoLedgerGrossField != null
        && GroundControlReflection.EcoNextLedgerAtField != null;

    private static MethodBase TargetMethod() => GroundControlReflection.EcoRefreshLedgerMethod!;

    private static void Prefix(object __instance, out float __state)
    {
        __state = __instance != null && GroundControlReflection.EcoNextLedgerAtField?.GetValue(__instance) is float t
            ? t
            : -1f;
    }

    private static void Postfix(object __instance, float __state)
    {
        if (__instance == null
            || !AddonConfig.ModEnabled.Value
            || !GcreNetworkSync.EffectiveEnableCapturedBaseIncome
            || GroundControlReflection.EcoNextLedgerAtField?.GetValue(__instance) is not float after
            || Mathf.Approximately(after, __state))
        {
            return;
        }

        float bonus = BaseCaptureIncomeService.GetCapturedBaseIncomePerMinute(GroundControlReflection.GetLocalHq());
        if (bonus <= 0f)
        {
            return;
        }

        if (GroundControlReflection.EcoLedgerGrossField?.GetValue(__instance) is float currentGross)
        {
            GroundControlReflection.EcoLedgerGrossField.SetValue(__instance, currentGross + bonus);
        }
    }
}

[HarmonyPatch]
internal static class AirbaseCaptureBountyPatch
{
    private static bool Prepare() => GroundControlReflection.AirbaseCaptureFactionMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.AirbaseCaptureFactionMethod!;

    private static void Prefix(Airbase __instance, out FactionHQ? __state)
    {
        __state = __instance != null ? __instance.CurrentHQ : null;
    }

    private static void Postfix(Airbase __instance, FactionHQ? newHQ, FactionHQ? __state)
    {
        if (__instance != null)
        {
            BaseCaptureIncomeService.OnAirbaseCaptured(__instance, __state, newHQ);
        }
    }
}

[HarmonyPatch]
internal static class ChampionsBaselineCapturePatch
{
    private static bool Prepare() => GroundControlReflection.TickChampionsStartMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.TickChampionsStartMethod!;

    private static void Prefix(object __instance, out bool __state)
    {
        __state = GroundControlReflection.GetChampionsDealt(__instance);
    }

    private static void Postfix(object __instance, bool __state)
    {
        if (!__state && GroundControlReflection.GetChampionsDealt(__instance))
        {
            BaseCaptureIncomeService.RecordChampionsInitialBases();
        }
    }
}
