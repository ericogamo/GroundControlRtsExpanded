using System.Collections.Generic;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal sealed class RtsCityUiService : IAddonTick, IAddonGui, IAddonResetSession
{
    private const int WindowId = 148120;
    private const float WindowWidth = 448f;
    private const float WindowHeight = 590f;
    private const float SubBarHeight = 30f;

    internal static RtsCityUiService Instance { get; } = new();

    private readonly List<RtsBuildingRecord> ownedBuffer = new();
    private Rect windowRect = new(0f, 80f, WindowWidth, WindowHeight);
    private Rect subBarRect = new(0f, 42f, 700f, SubBarHeight);
    private bool subBarVisible;
    private bool windowPositioned;
    private bool windowVisible;
    private bool helpVisible;
    private Vector2 catalogScroll;
    private string uiFeedback = string.Empty;
    private float uiFeedbackUntil;

    private RtsCityUiService()
    {
    }

    internal bool IsWindowVisible => windowVisible;

    internal void ToggleWindow()
    {
        windowVisible = !windowVisible;
        helpVisible = false;
    }

    public void Tick()
    {
        if (!AddonConfig.ModEnabled.Value || !GroundControlReflection.IsCommanderModeActive())
        {
            return;
        }

        if (AddonConfig.CityMenuKey.Value.IsDown())
        {
            ToggleWindow();
        }
    }

    public void DrawGui()
    {
        subBarVisible = false;
        if (!AddonConfig.ModEnabled.Value || !GroundControlReflection.IsCommanderModeActive())
        {
            return;
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        if (localHq == null)
        {
            return;
        }

        if (!windowPositioned)
        {
            windowRect = new Rect(Mathf.Max(14f, NativeThemeBridge.UiWidth - WindowWidth - 14f), 80f, WindowWidth, WindowHeight);
            windowPositioned = true;
        }

        FactionCapSnapshot snap = DynamicCapAndTechService.Instance.GetSnapshot(localHq);
        DrawTopSubBar(localHq, snap);

        if (!windowVisible)
        {
            return;
        }

        windowRect = NativeThemeBridge.DrawWindow(WindowId, windowRect, id => DrawCityWindow(localHq, snap), "CITY & TECH TREE");
    }

    public void ResetSession()
    {
        windowVisible = false;
        subBarVisible = false;
        helpVisible = false;
        windowPositioned = false;
        uiFeedback = string.Empty;
        uiFeedbackUntil = 0f;
        ownedBuffer.Clear();
    }

    internal bool ContainsScreenPoint(Vector2 screenPoint)
    {
        if (!AddonConfig.ModEnabled.Value || !GroundControlReflection.IsCommanderModeActive())
        {
            return false;
        }

        Vector2 guiPt = NativeThemeBridge.ScreenToGui(screenPoint);
        if (subBarVisible && subBarRect.Contains(guiPt))
        {
            return true;
        }

        return windowVisible && NativeThemeBridge.VisualRect(WindowId, windowRect).Contains(guiPt);
    }

    internal bool IsMouseOverUi() => ContainsScreenPoint(Input.mousePosition);

    private void DrawTopSubBar(FactionHQ localHq, FactionCapSnapshot snap)
    {
        bool showCheats = SingleplayerCheatService.IsAllowed;
        float desiredWidth = showCheats ? 854f : 768f;
        float barW = Mathf.Min(desiredWidth, NativeThemeBridge.UiWidth - 24f);
        subBarRect = new Rect(Mathf.Round((NativeThemeBridge.UiWidth - barW) * 0.5f), 42f, barW, SubBarHeight);
        subBarVisible = true;

        GUI.Box(subBarRect, string.Empty, NativeThemeBridge.Panel);
        NativeThemeBridge.DrawAccentLine(new Rect(subBarRect.x, subBarRect.yMax - 2f, subBarRect.width, 2f));

        float x = subBarRect.x + 10f;
        float y = subBarRect.y + 3f;
        float btnH = 22f;

        float btnAreaW = showCheats ? 378f : 298f;
        float textAreaW = Mathf.Max(140f, barW - btnAreaW - 24f);
        string statusText = GcreNetworkSync.EffectiveEnableDynamicCaps
            ? $"TECH T{snap.TechTier}   |   GND {snap.GroundUsed}/{snap.GroundCap}   |   AIR {snap.AirUsed}/{snap.AirCap}   |   NAV {snap.NavalUsed}/{snap.NavalCap}"
            : $"TECH T{snap.TechTier}   |   DYNAMIC CAPS OFF";
        GUI.Label(new Rect(x, subBarRect.y + 4f, textAreaW, 20f), statusText, NativeThemeBridge.Caption);

        float bx = subBarRect.xMax - btnAreaW - 8f;
        if (GUI.Button(new Rect(bx, y, 74f, btnH), "CITY", windowVisible ? NativeThemeBridge.SelectedButton : NativeThemeBridge.Button))
        {
            ToggleWindow();
        }

        bx += 78f;
        bool roadActive = PioneerRoadService.Instance.IsPlanningMode;
        if (GUI.Button(new Rect(bx, y, 74f, btnH), roadActive ? "ROADS *" : "ROADS", roadActive ? NativeThemeBridge.SelectedButton : NativeThemeBridge.Button))
        {
            PioneerRoadService.Instance.TogglePlanningMode();
        }

        bx += 78f;
        int abmStock = AbmDefenseService.Instance.GetAvailableInterceptors(localHq);
        int abmCap = AbmDefenseService.Instance.GetMaxCapacity(localHq);
        bool abmAlert = AbmDefenseService.Instance.HasActiveThreatForLocal();
        GUIStyle abmStyle = abmAlert
            ? NativeThemeBridge.DangerButton
            : (AbmDefenseService.Instance.IsWindowVisible ? NativeThemeBridge.SelectedButton : NativeThemeBridge.Button);
        string abmLabel = abmAlert ? $"ABM! {abmStock}" : $"ABM {abmStock}/{abmCap}";
        if (GUI.Button(new Rect(bx, y, 78f, btnH), abmLabel, abmStyle))
        {
            AbmDefenseService.Instance.ToggleWindow();
        }

        bx += 82f;
        if (GUI.Button(new Rect(bx, y, 60f, btnH), "ECO", EconomyTrackerUiService.Instance.IsWindowVisible ? NativeThemeBridge.SelectedButton : NativeThemeBridge.Button))
        {
            EconomyTrackerUiService.Instance.ToggleWindow();
        }

        if (showCheats)
        {
            bx += 64f;
            if (GUI.Button(new Rect(bx, y, 76f, btnH), "CHEATS", SingleplayerCheatService.Instance.IsWindowVisible ? NativeThemeBridge.SelectedButton : NativeThemeBridge.Button))
            {
                SingleplayerCheatService.Instance.ToggleWindow();
            }
        }
    }

    private void DrawCityWindow(FactionHQ localHq, FactionCapSnapshot snap)
    {
        NativeThemeBridge.DrawWindowChrome(windowRect.width, ref helpVisible, out bool closeClicked);
        if (closeClicked)
        {
            windowVisible = false;
            return;
        }

        float w = windowRect.width;
        float h = windowRect.height;
        float innerW = w - 24f;
        float y = 36f;

        if (helpVisible)
        {
            y += NativeThemeBridge.DrawHelpOverlay(
                new Rect(12f, y, innerW, 92f),
                "RTS City buildings unlock higher Tech Tiers and expand unit caps. " +
                "All buildings and pioneer roads are constructed physically by M12 Jackknifes. " +
                "Hotkeys: Shift+C (City), Shift+R (Roads), Shift+B (ABM), Shift+E (Eco Tracker).") + 10f;
        }

        // Top stats + cap bars
        GUI.Box(new Rect(12f, y, innerW, 74f), string.Empty, NativeThemeBridge.Panel);
        string factionName = (localHq.faction != null ? localHq.faction.factionName : "Faction").ToUpperInvariant();
        GUI.Label(
            new Rect(20f, y + 6f, innerW - 16f, 20f),
            $"{factionName}   |   FUNDS ${localHq.factionFunds:0.#}m   |   TECH TIER {snap.TechTier}",
            NativeThemeBridge.Header);

        float barW = (innerW - 28f) / 3f;
        DrawCapGauge(new Rect(20f, y + 32f, barW, 34f), "GROUND", snap.GroundUsed, snap.GroundCap);
        DrawCapGauge(new Rect(26f + barW, y + 32f, barW, 34f), "AIR", snap.AirUsed, snap.AirCap);
        DrawCapGauge(new Rect(32f + barW * 2f, y + 32f, barW, 34f), "NAVAL", snap.NavalUsed, snap.NavalCap);
        y += 80f;

        if (GroundControlReflection.IsEconomyAwaitingPlacement())
        {
            string ecoStatus = GroundControlReflection.GetEconomyStatusText();
            GUI.Box(new Rect(12f, y, innerW, 30f), string.Empty, NativeThemeBridge.Inset);
            GUI.Label(
                new Rect(18f, y + 5f, innerW - 88f, 20f),
                string.IsNullOrEmpty(ecoStatus) ? "Placing building: LMB place, R rotate, RMB cancel." : ecoStatus,
                NativeThemeBridge.WarningLabel);
            if (GUI.Button(new Rect(w - 86f, y + 3f, 68f, 24f), "CANCEL", NativeThemeBridge.DangerButton))
            {
                GroundControlReflection.CancelEconomyBuild();
            }

            y += 34f;
        }

        if (!string.IsNullOrEmpty(uiFeedback) && Time.unscaledTime < uiFeedbackUntil)
        {
            GUI.Label(new Rect(14f, y, innerW - 4f, 18f), uiFeedback, NativeThemeBridge.Caption);
            y += 20f;
        }

        float footerH = 38f;
        Rect scrollOuter = new(12f, y, innerW, Mathf.Max(140f, h - y - footerH - 8f));
        GUI.Box(scrollOuter, string.Empty, NativeThemeBridge.Inset);

        DynamicCapAndTechService.Instance.GetOwnedBuildingsOfKinds(localHq, abmOnly: false, ownedBuffer);
        IReadOnlyList<RtsBuildingSpec> specs = RtsBuildingCatalog.AllSpecs;

        float contentH = ComputeCatalogContentHeight(specs, ownedBuffer.Count);
        Rect viewRect = new(0f, 0f, scrollOuter.width - 18f, Mathf.Max(scrollOuter.height - 6f, contentH));
        catalogScroll = GUI.BeginScrollView(scrollOuter, catalogScroll, viewRect);

        float rowY = 6f;
        rowY = NativeThemeBridge.DrawSection(new Rect(8f, rowY, viewRect.width - 16f, 20f), "CONSTRUCT CITY INFRASTRUCTURE");

        for (int i = 0; i < specs.Count; i++)
        {
            RtsBuildingSpec spec = specs[i];
            if (spec.IsAbmInfrastructure)
            {
                continue;
            }

            int ownedCount = snap.GetKindCount(spec.Kind);
            int bpCount = DynamicCapAndTechService.Instance.CountInProgressBlueprints(localHq, spec.Kind);
            bool canBuild = DynamicCapAndTechService.Instance.CanStartBuilding(localHq, spec.Kind, out string blockReason);
            float cardH = canBuild ? 52f : 68f;
            Rect rowRect = new(6f, rowY, viewRect.width - 12f, cardH);

            GUI.Box(rowRect, string.Empty, NativeThemeBridge.Panel);
            string countSuffix = bpCount > 0 ? $"({ownedCount}+{bpCount}/{spec.MaxPerFaction})" : $"({ownedCount}/{spec.MaxPerFaction})";
            GUI.Label(new Rect(rowRect.x + 8f, rowRect.y + 4f, rowRect.width - 96f, 20f), $"{spec.DisplayName} {countSuffix}  —  ${spec.BuildCost:0}m", NativeThemeBridge.Header);
            GUI.Label(new Rect(rowRect.x + 8f, rowRect.y + 24f, rowRect.width - 96f, 20f), spec.ShortSummary, NativeThemeBridge.MutedLabel);
            if (!canBuild)
            {
                GUI.Label(new Rect(rowRect.x + 8f, rowRect.y + 44f, rowRect.width - 96f, 18f), blockReason, NativeThemeBridge.WarningLabel);
            }

            GUI.enabled = canBuild;
            if (GUI.Button(new Rect(rowRect.xMax - 82f, rowRect.y + 9f, 74f, 34f), "BUILD", canBuild ? NativeThemeBridge.PrimaryButton : NativeThemeBridge.Button))
            {
                GUI.enabled = true;
                PioneerRoadService.Instance.CancelPlanning();
                BuildingDefinition? def = RtsBuildingCatalog.GetOrCreateDefinition(spec.Kind);
                if (def != null)
                {
                    GroundControlReflection.BeginStructureBuild(def);
                    SetFeedback($"Placement armed: {spec.DisplayName} (LMB place, R rotate).");
                }
                else
                {
                    SetFeedback("Encyclopedia not ready for building definition.");
                }
            }

            GUI.enabled = true;
            rowY += cardH + 6f;
        }

        rowY = NativeThemeBridge.DrawSection(new Rect(8f, rowY + 4f, viewRect.width - 16f, 20f), $"OWNED CITY BUILDINGS ({ownedBuffer.Count})");
        if (ownedBuffer.Count == 0)
        {
            GUI.Label(new Rect(10f, rowY, viewRect.width - 20f, 22f), "No RTS city buildings completed yet. Build a Command HQ first.", NativeThemeBridge.MutedLabel);
            rowY += 28f;
        }
        else
        {
            for (int i = 0; i < ownedBuffer.Count; i++)
            {
                RtsBuildingRecord rec = ownedBuffer[i];
                bool canUpgrade = DynamicCapAndTechService.Instance.CanUpgradeBuilding(rec, out string upgBlock);
                float nextCost = rec.Spec.GetUpgradeCost(rec.Level);
                Rect rowRect = new(6f, rowY, viewRect.width - 12f, 44f);
                GUI.Box(rowRect, string.Empty, NativeThemeBridge.Panel);

                string label = !string.IsNullOrEmpty(rec.Unit.UniqueName) ? rec.Unit.UniqueName : rec.Spec.DisplayName;
                GUI.Label(new Rect(rowRect.x + 8f, rowRect.y + 4f, rowRect.width - 126f, 20f), $"{label}  —  LVL {rec.Level}", NativeThemeBridge.Label);
                if (!canUpgrade)
                {
                    GUI.Label(new Rect(rowRect.x + 8f, rowRect.y + 22f, rowRect.width - 126f, 18f), upgBlock, NativeThemeBridge.MutedLabel);
                }

                if (rec.Level < 3)
                {
                    GUI.enabled = canUpgrade;
                    if (GUI.Button(new Rect(rowRect.xMax - 114f, rowRect.y + 6f, 106f, 32f), $"UPGRADE ${nextCost:0}m", NativeThemeBridge.Button))
                    {
                        GUI.enabled = true;
                        DynamicCapAndTechService.Instance.TryUpgradeBuilding(rec, out string msg);
                        SetFeedback(msg);
                    }

                    GUI.enabled = true;
                }

                rowY += 50f;
            }
        }

        rowY = NativeThemeBridge.DrawSection(new Rect(8f, rowY + 4f, viewRect.width - 16f, 20f), "TECH UNLOCKS & MDS BRIDGE");
        GUI.Label(new Rect(10f, rowY, viewRect.width - 20f, 34f), "T1: Helis/Light  |  T2 (HQ L2+Acad/Nav L1): Armor/Jets/Corvettes  |  T3 (HQ L3+L2): Stealth/Heavy/Cruisers", NativeThemeBridge.MutedLabel);
        rowY += 36f;
        GUI.Label(new Rect(10f, rowY, viewRect.width - 20f, 20f), MdsBridgeBootstrap.BridgeStatusSummary, NativeThemeBridge.Caption);

        GUI.EndScrollView();

        // Bottom quick buttons
        float footY = h - 32f;
        float footW = w - 34f;
        bool hasCheatBtn = SingleplayerCheatService.IsAllowed;
        int fCount = hasCheatBtn ? 3 : 2;
        float fBtnW = (footW - 6f * (fCount - 1)) / fCount;

        if (GUI.Button(new Rect(12f, footY, fBtnW, 24f), PioneerRoadService.Instance.IsPlanningMode ? "CLOSE ROADS" : "ROADS & BRIDGES", PioneerRoadService.Instance.IsPlanningMode ? NativeThemeBridge.SelectedButton : NativeThemeBridge.Button))
        {
            PioneerRoadService.Instance.TogglePlanningMode();
        }

        if (GUI.Button(new Rect(12f + fBtnW + 6f, footY, fBtnW, 24f), AbmDefenseService.Instance.IsWindowVisible ? "CLOSE ABM" : "OPEN ABM DEFENSE", AbmDefenseService.Instance.IsWindowVisible ? NativeThemeBridge.SelectedButton : NativeThemeBridge.Button))
        {
            AbmDefenseService.Instance.ToggleWindow();
        }

        if (hasCheatBtn && GUI.Button(new Rect(12f + (fBtnW + 6f) * 2f, footY, fBtnW, 24f), "CHEATS", SingleplayerCheatService.Instance.IsWindowVisible ? NativeThemeBridge.SelectedButton : NativeThemeBridge.Button))
        {
            SingleplayerCheatService.Instance.ToggleWindow();
        }

        GUI.DragWindow(new Rect(0f, 0f, w - 128f, 28f));
    }

    private static float ComputeCatalogContentHeight(IReadOnlyList<RtsBuildingSpec> specs, int ownedCount)
    {
        float h = 30f;
        for (int i = 0; i < specs.Count; i++)
        {
            if (!specs[i].IsAbmInfrastructure)
            {
                h += 74f;
            }
        }

        h += 30f + Mathf.Max(1, ownedCount) * 50f + 92f;
        return h;
    }

    private static void DrawCapGauge(Rect rect, string label, int used, int cap)
    {
        float frac = cap > 0 ? Mathf.Clamp01((float)used / cap) : 1f;
        float healthFrac = 1f - frac;
        GUI.Label(new Rect(rect.x, rect.y, rect.width, 16f), $"{label}  {used}/{cap}", NativeThemeBridge.Caption);
        NativeThemeBridge.DrawBar(new Rect(rect.x, rect.y + 18f, rect.width, 10f), frac, NativeThemeBridge.StatusColor(healthFrac));
    }

    private void SetFeedback(string message)
    {
        uiFeedback = message;
        uiFeedbackUntil = Time.unscaledTime + 6f;
    }
}
