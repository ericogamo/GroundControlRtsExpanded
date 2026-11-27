using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mirage;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal sealed class SingleplayerCheatService : IAddonTick, IAddonGui, IAddonResetSession
{
    private const int WindowId = 148128;
    private const float WindowWidth = 396f;
    private const float WindowHeight = 528f;

    internal static SingleplayerCheatService Instance { get; } = new();

    private Rect windowRect = new(0f, 84f, WindowWidth, WindowHeight);
    private bool windowPositioned;
    private bool windowVisible;
    private bool helpVisible;
    private string feedback = string.Empty;
    private float feedbackUntil;

    private bool unlockTier3;
    private bool ignoreCaps;
    private bool ignoreFactionLock;
    private bool instantBuild;
    private bool revealMap;
    private bool completeAllRequested;

    private SingleplayerCheatService()
    {
    }

    internal static bool IsAllowed =>
        AddonConfig.ModEnabled.Value
        && AddonConfig.EnableSingleplayerCheats.Value
        && (GameManager.gameState == GameState.SinglePlayer || IsAuthorizedHostTestCheatUser());

    private static bool IsAuthorizedHostTestCheatUser()
    {
        if (GameManager.gameState != GameState.Multiplayer || GroundControlReflection.IsNetworkGuest())
        {
            return false;
        }

        string configured = (AddonConfig.HostTestCheatSteamId.Value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(configured) || !ulong.TryParse(configured, out ulong allowedId) || allowedId == 0UL)
        {
            return false;
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        if (localHq == null || !((NetworkBehaviour)localHq).IsServer)
        {
            return false;
        }

        return GameManager.GetLocalPlayer<NuclearOption.Networking.BasePlayer>(out NuclearOption.Networking.BasePlayer localPlayer)
            && localPlayer != null
            && localPlayer.SteamID == allowedId;
    }

    internal bool IsWindowVisible => IsAllowed && windowVisible;

    internal static bool IsUnlockTier3ActiveFor(FactionHQ? hq)
    {
        if (hq == null)
        {
            return false;
        }

        if (GcreNetworkSync.IsPureClient && GcreNetworkSync.HostHasGcre)
        {
            return !string.IsNullOrEmpty(GcreNetworkSync.RemoteCheatTier3HqName) &&
                   hq.faction != null &&
                   string.Equals(hq.faction.factionName, GcreNetworkSync.RemoteCheatTier3HqName, System.StringComparison.OrdinalIgnoreCase);
        }

        if (!IsAllowed || !Instance.unlockTier3)
        {
            return false;
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        return localHq != null && ReferenceEquals(hq, localHq);
    }

    internal static bool IsIgnoreCapsActiveFor(FactionHQ? hq)
    {
        if (hq == null)
        {
            return false;
        }

        if (GcreNetworkSync.IsPureClient && GcreNetworkSync.HostHasGcre)
        {
            return !string.IsNullOrEmpty(GcreNetworkSync.RemoteCheatIgnoreCapsHqName) &&
                   hq.faction != null &&
                   string.Equals(hq.faction.factionName, GcreNetworkSync.RemoteCheatIgnoreCapsHqName, System.StringComparison.OrdinalIgnoreCase);
        }

        if (!IsAllowed || !Instance.ignoreCaps)
        {
            return false;
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        return localHq != null && ReferenceEquals(hq, localHq);
    }

    internal static bool IsIgnoreFactionLockActive =>
        (GcreNetworkSync.IsPureClient && GcreNetworkSync.HostHasGcre)
            ? GcreNetworkSync.RemoteCheatIgnoreFactionLock
            : (IsAllowed && Instance.ignoreFactionLock);
    internal static bool IsInstantBuildActive => IsAllowed && Instance.instantBuild;
    internal static bool IsRevealMapActive => IsAllowed && Instance.revealMap;

    internal void ToggleWindow()
    {
        if (!IsAllowed)
        {
            return;
        }

        windowVisible = !windowVisible;
        helpVisible = false;
    }

    internal bool ContainsScreenPoint(Vector2 screenPoint)
    {
        if (!IsAllowed || !windowVisible || !GroundControlReflection.IsCommanderModeActive())
        {
            return false;
        }

        Vector2 guiPt = NativeThemeBridge.ScreenToGui(screenPoint);
        return NativeThemeBridge.VisualRect(WindowId, windowRect).Contains(guiPt);
    }

    public void ResetSession()
    {
        windowVisible = false;
        windowPositioned = false;
        helpVisible = false;
        unlockTier3 = false;
        ignoreCaps = false;
        ignoreFactionLock = false;
        instantBuild = false;
        revealMap = false;
        completeAllRequested = false;
        feedback = string.Empty;
        feedbackUntil = 0f;
    }

    public void Tick()
    {
        if (!IsAllowed)
        {
            if (windowVisible || unlockTier3 || ignoreCaps || ignoreFactionLock || instantBuild || revealMap)
            {
                ResetSession();
            }

            return;
        }

        if (GroundControlReflection.IsCommanderModeActive() && AddonConfig.CheatMenuKey.Value.IsDown())
        {
            ToggleWindow();
        }
    }

    public void DrawGui()
    {
        if (!IsAllowed || !windowVisible || !GroundControlReflection.IsCommanderModeActive())
        {
            return;
        }

        if (!windowPositioned)
        {
            windowRect = new Rect(
                Mathf.Max(16f, (NativeThemeBridge.UiWidth - WindowWidth) * 0.5f),
                84f,
                WindowWidth,
                WindowHeight);
            windowPositioned = true;
        }

        string title = GameManager.gameState == GameState.SinglePlayer ? "SINGLEPLAYER CHEATS" : "HOST TEST CHEATS";
        windowRect = NativeThemeBridge.DrawWindow(WindowId, windowRect, DrawCheatWindow, title);
    }

    internal static bool ConsumeCompleteAllOrInstantBuild(object ecoInstance)
    {
        if (!IsAllowed)
        {
            return false;
        }

        bool run = Instance.instantBuild || Instance.completeAllRequested;
        Instance.completeAllRequested = false;
        if (!run || ecoInstance == null || GroundControlReflection.EcoConstructionSitesField == null)
        {
            return false;
        }

        if (GroundControlReflection.EcoConstructionSitesField.GetValue(ecoInstance) is not IList sites || sites.Count == 0)
        {
            return false;
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        PropertyInfo? progressProp = GroundControlReflection.SiteProgressProp;
        if (localHq == null || progressProp == null)
        {
            return false;
        }

        bool anyTouched = false;
        for (int i = 0; i < sites.Count; i++)
        {
            object? site = sites[i];
            if (site == null)
            {
                continue;
            }

            if (GroundControlReflection.TryGetBlueprintInfo(site, out FactionHQ? siteHq, out _)
                && ReferenceEquals(siteHq, localHq))
            {
                progressProp.SetValue(site, 1f);
                anyTouched = true;
            }
        }

        return anyTouched;
    }

    private void DrawCheatWindow(int windowId)
    {
        NativeThemeBridge.DrawWindowChrome(windowRect.width, ref helpVisible, out bool closeClicked);
        if (closeClicked)
        {
            windowVisible = false;
            return;
        }

        float w = windowRect.width;
        float innerW = w - 24f;
        float y = 36f;

        if (helpVisible)
        {
            y += NativeThemeBridge.DrawHelpOverlay(
                new Rect(12f, y, innerW, 84f),
                "Singleplayer-only sandbox & testing cheats. Strictly disabled in Multiplayer. " +
                "Hotkeys: Shift+F10 toggles this window.") + 10f;
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        bool hasServerHq = localHq != null && ((NetworkBehaviour)localHq).IsServer;

        // Status header box
        GUI.Box(new Rect(12f, y, innerW, 38f), string.Empty, NativeThemeBridge.Panel);
        string fundsLabel = localHq != null ? $"${localHq.factionFunds:0.#}m" : "$0m";
        int tier = localHq != null ? DynamicCapAndTechService.Instance.GetSnapshot(localHq).TechTier : 1;
        string modeTag = GameManager.gameState == GameState.SinglePlayer ? "SINGLEPLAYER ONLY" : "HOST TEST MODE";
        GUI.Label(new Rect(22f, y + 8f, innerW - 20f, 22f), $"{modeTag}   |   FUNDS {fundsLabel}   |   TIER {tier}", NativeThemeBridge.Header);
        y += 46f;

        // Section 1: FUNDS
        y = NativeThemeBridge.DrawSection(new Rect(12f, y, innerW, 20f), "FUNDS");
        float colW = (innerW - 12f) / 3f;
        GUI.enabled = hasServerHq;
        if (GUI.Button(new Rect(12f, y, colW, 28f), "+$500m", NativeThemeBridge.PrimaryButton) && localHq != null)
        {
            localHq.AddFunds(500f);
            SetFeedback("Added +$500m credits.");
        }

        if (GUI.Button(new Rect(12f + colW + 6f, y, colW, 28f), "+$5,000m", NativeThemeBridge.PrimaryButton) && localHq != null)
        {
            localHq.AddFunds(5000f);
            SetFeedback("Added +$5,000m credits.");
        }

        if (GUI.Button(new Rect(12f + (colW + 6f) * 2f, y, colW, 28f), "+$50,000m", NativeThemeBridge.PrimaryButton) && localHq != null)
        {
            localHq.AddFunds(50000f);
            SetFeedback("Added +$50,000m credits.");
        }

        GUI.enabled = true;
        y += 38f;

        // Section 2: TECH & CAPS
        y = NativeThemeBridge.DrawSection(new Rect(12f, y, innerW, 20f), "TECH TREE & CAPS");

        bool nextTier3 = NativeThemeBridge.DrawCheck(new Rect(12f, y, innerW, 24f), unlockTier3, "Unlock Tech Tier 3 (All buildings, jets, ships & MDS)");
        if (nextTier3 != unlockTier3)
        {
            unlockTier3 = nextTier3;
            DynamicCapAndTechService.Instance.InvalidateCache(localHq);
            SetFeedback(unlockTier3 ? "Tech Tier 3 unlocked (cheat)." : "Tech Tier returned to normal.");
        }

        y += 28f;
        bool nextCaps = NativeThemeBridge.DrawCheck(new Rect(12f, y, innerW, 24f), ignoreCaps, "Ignore Unit Caps (Ground / Air / Naval / Max limits)");
        if (nextCaps != ignoreCaps)
        {
            ignoreCaps = nextCaps;
            DynamicCapAndTechService.Instance.InvalidateCache(localHq);
            SetFeedback(ignoreCaps ? "Unit & building caps ignored (cheat)." : "Normal caps restored.");
        }

        y += 28f;
        bool nextFaction = NativeThemeBridge.DrawCheck(new Rect(12f, y, innerW, 24f), ignoreFactionLock, "Ignore Contractor Faction Lock (test West & East packs)");
        if (nextFaction != ignoreFactionLock)
        {
            ignoreFactionLock = nextFaction;
            SetFeedback(ignoreFactionLock ? "Faction lock bypassed (SP only)." : "Faction lock restored.");
        }

        y += 34f;

        // Section 3: CONSTRUCTION
        y = NativeThemeBridge.DrawSection(new Rect(12f, y, innerW, 20f), "CONSTRUCTION & ROADS");
        bool nextInstant = NativeThemeBridge.DrawCheck(new Rect(12f, y, innerW, 24f), instantBuild, "Instant Build (Blueprints, Roads & MDS Orders)");
        if (nextInstant != instantBuild)
        {
            instantBuild = nextInstant;
            if (instantBuild)
            {
                TriggerCompleteAll(localHq);
            }

            SetFeedback(instantBuild ? "Instant Build enabled." : "Normal M12 build speed restored.");
        }

        y += 30f;
        GUI.enabled = hasServerHq;
        if (GUI.Button(new Rect(12f, y, innerW, 30f), "COMPLETE ALL BLUEPRINTS & ROADS NOW", NativeThemeBridge.Button))
        {
            TriggerCompleteAll(localHq);
            SetFeedback("Completed all friendly blueprints, roads & MDS orders.");
        }

        GUI.enabled = true;
        y += 40f;

        // Section 4: ABM, MAP & UNITS
        y = NativeThemeBridge.DrawSection(new Rect(12f, y, innerW, 20f), "ABM, MAP & UNITS");
        bool nextReveal = NativeThemeBridge.DrawCheck(new Rect(12f, y, innerW, 24f), revealMap, "Reveal Map (Fog of War OFF)");
        if (nextReveal != revealMap)
        {
            revealMap = nextReveal;
            SetFeedback(revealMap ? "Fog of War disabled (SP cheat)." : "Fog of War restored.");
        }

        y += 30f;
        float halfW = (innerW - 6f) * 0.5f;
        GUI.enabled = hasServerHq;
        if (GUI.Button(new Rect(12f, y, halfW, 30f), "+10 ABM MISSILES", NativeThemeBridge.Button) && localHq != null)
        {
            AbmDefenseService.Instance.CheatAddInterceptors(localHq, 10);
            SetFeedback("Added +10 ABM Interceptors to stockpile.");
        }

        if (GUI.Button(new Rect(12f + halfW + 6f, y, halfW, 30f), "REPAIR ALL UNITS", NativeThemeBridge.Button) && localHq != null)
        {
            int count = RepairAllFriendlyUnits(localHq);
            SetFeedback($"Repaired {count} friendly unit(s) to 100%.");
        }

        GUI.enabled = true;
        y += 38f;

        if (!string.IsNullOrEmpty(feedback) && Time.unscaledTime < feedbackUntil)
        {
            GUI.Label(new Rect(12f, y, innerW, 22f), feedback, NativeThemeBridge.Caption);
        }

        GUI.DragWindow(new Rect(0f, 0f, w - 128f, 28f));
    }

    private void TriggerCompleteAll(FactionHQ? localHq)
    {
        if (!IsAllowed || localHq == null)
        {
            return;
        }

        completeAllRequested = true;
        PioneerRoadService.Instance.CompleteAllActiveProjectsForHq(localHq);
        MdsBridgeBootstrap.CompleteMdsEngineerOrdersForHq?.Invoke(localHq);
    }

    private static int RepairAllFriendlyUnits(FactionHQ hq)
    {
        if (!((NetworkBehaviour)hq).IsServer)
        {
            return 0;
        }

        int repairedCount = 0;
        for (int i = 0; i < UnitRegistry.allUnits.Count; i++)
        {
            Unit? unit = UnitRegistry.allUnits[i];
            if (unit == null || unit.disabled || !ReferenceEquals(unit.NetworkHQ, hq))
            {
                continue;
            }

            bool touched = false;
            List<UnitPart> parts = unit.GetAllParts();
            for (int p = 0; p < parts.Count; p++)
            {
                UnitPart? part = parts[p];
                if (part != null && part.hitPoints > 0f && part.hitPoints < 99.9f)
                {
                    part.Repair();
                    touched = true;
                }
            }

            if (unit is IRepairable repairable && repairable.NeedsRepair())
            {
                repairable.Repair(unit, 1_000_000f);
                touched = true;
            }

            if (touched)
            {
                repairedCount++;
                if (unit.persistentID.IsValid)
                {
                    GcreNetworkSync.BroadcastPartHeal(unit.persistentID.Id);
                }
            }
        }

        return repairedCount;
    }

    private void SetFeedback(string message)
    {
        feedback = message;
        feedbackUntil = Time.unscaledTime + 5f;
    }
}

[HarmonyPatch]
internal static class EconomyInstantBuildPatch
{
    private static bool Prepare() => GroundControlReflection.EcoTickConstructionMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.EcoTickConstructionMethod!;

    private static void Prefix(object __instance)
    {
        SingleplayerCheatService.ConsumeCompleteAllOrInstantBuild(__instance);
    }
}

[HarmonyPatch]
internal static class FogOfWarCheatPatch
{
    private static bool Prepare() => GroundControlReflection.FogHidesFactionMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.FogHidesFactionMethod!;

    private static bool Prefix(ref bool __result)
    {
        if (!SingleplayerCheatService.IsRevealMapActive)
        {
            return true;
        }

        __result = false;
        return false;
    }
}
