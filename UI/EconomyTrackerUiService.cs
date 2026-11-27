using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal sealed class EconomyTrackerUiService : IAddonTick, IAddonGui, IAddonResetSession
{
    private const int WindowId = 148134;
    private const float DetailsWidth = 548f;
    private const float DetailsHeight = 540f;
    private const float CompactWidth = 468f;
    private const float SampleIntervalSeconds = 0.5f;
    private const float CashflowWindowSeconds = 60f;

    private sealed class CashflowTracker
    {
        internal bool Initialized;
        internal float LastFunds;
        internal readonly Queue<(float Time, float Delta)> Events = new();
        internal float ObservedIn60s;
        internal float ObservedOut60s;
        internal float ObservedNet60s;
    }

    private sealed class FactionEconomyRow
    {
        internal FactionHQ Hq = null!;
        internal string FactionName = string.Empty;
        internal bool IsLocal;
        internal bool IsAi;
        internal bool CanInspect;
        internal string RoleBadge = string.Empty;
        internal string StatusTag = string.Empty;
        internal bool StatusWarning;

        internal float Funds;
        internal float SpendableNow;
        internal float MaxTickSpend;
        internal float ReservedTotal;

        internal float BaseIncome;
        internal float CapturedBaseIncome;
        internal float BuildingIncome;
        internal float StipendIncome;
        internal float EffectiveGross;
        internal float Upkeep;
        internal float NetIncome;
        internal float MineMultiplier = 1f;

        internal bool HasIncomeCap;
        internal float AllowedGrossCap;
        internal float NetFloor;
        internal float FairPlayGrossCap;
        internal bool MineBlocked;
        internal bool UpgradeBlocked;

        internal float ObservedIn60s;
        internal float ObservedOut60s;
        internal float ObservedNet60s;

        internal int GoldMines;
        internal int TotalMines;
        internal int FreeMines;
        internal int EffectiveMaxGoldMines;
        internal int HeldCapturableBases;
        internal int CapturedBonusBases;
        internal int Factories;
        internal int EffectiveMaxFactories;
        internal int BestDepot;
        internal int DockLevel;
        internal int TechTier;

        // AI sub-funds
        internal AiStrategicPhase AiPhase;
        internal float SavedBuildReserve;
        internal float MineFund;
        internal float FactoryFund;
        internal float FactoryReserve;
        internal float GodReserve;
        internal float AirFund;
        internal float AirSaveCap;
        internal float SamFund;
        internal float NavalFund;
        internal bool IsBroke;
        internal string SavingForName = string.Empty;

        // Human pilot allocations
        internal string PilotsSummary = string.Empty;
        internal float ExcessThreshold;
        internal float RegularIncome;
    }

    internal static EconomyTrackerUiService Instance { get; } = new();

    private readonly Dictionary<FactionHQ, CashflowTracker> cashflowTrackers = new();
    private readonly List<FactionEconomyRow> rows = new();
    private readonly List<FactionHQ> hqSortBuffer = new();
    private readonly List<FactionHQ> staleHqBuffer = new();
    private readonly List<string> pilotLabelBuffer = new();

    private Rect windowRect = new(0f, 80f, DetailsWidth, DetailsHeight);
    private bool windowPositioned;
    private bool windowVisible;
    private bool compactMode;
    private bool helpVisible;
    private Vector2 scroll;
    private float nextSampleAt;

    private string presetName = "Fair";
    private float presetNetFloor = 500f;
    private float presetCapMult = 1.05f;
    private float presetMineMult = 1.0f;
    private float presetStipend;
    private float incomeTickProgress = -1f;

    private EconomyTrackerUiService()
    {
    }

    internal bool IsWindowVisible => windowVisible;

    internal void ToggleWindow()
    {
        windowVisible = !windowVisible;
        helpVisible = false;
        if (windowVisible)
        {
            nextSampleAt = 0f;
            RefreshSnapshots(forceCashflowUpdate: false);
        }
    }

    internal bool ContainsScreenPoint(Vector2 screenPoint)
    {
        if (!AddonConfig.ModEnabled.Value || !windowVisible || FactionRegistry.GetAllHQs().Count == 0)
        {
            return false;
        }

        Vector2 guiPt = NativeThemeBridge.ScreenToGui(screenPoint);
        return NativeThemeBridge.VisualRect(WindowId, windowRect).Contains(guiPt);
    }

    public void Tick()
    {
        if (!AddonConfig.ModEnabled.Value)
        {
            return;
        }

        if (AddonConfig.EconomyTrackerKey.Value.IsDown())
        {
            ToggleWindow();
        }

        float now = Time.unscaledTime;
        if (now < nextSampleAt)
        {
            return;
        }

        nextSampleAt = now + SampleIntervalSeconds;
        if (FactionRegistry.GetAllHQs().Count == 0)
        {
            return;
        }

        RefreshSnapshots(forceCashflowUpdate: true);
    }

    public void DrawGui()
    {
        if (!AddonConfig.ModEnabled.Value || !windowVisible || FactionRegistry.GetAllHQs().Count == 0)
        {
            return;
        }

        if (!windowPositioned)
        {
            float w = compactMode ? CompactWidth : DetailsWidth;
            float h = compactMode ? ComputeCompactHeight() : DetailsHeight;
            windowRect = new Rect(Mathf.Max(14f, NativeThemeBridge.UiWidth - w - 14f), 80f, w, h);
            windowPositioned = true;
        }

        float targetW = compactMode ? CompactWidth : DetailsWidth;
        float targetH = compactMode ? ComputeCompactHeight() : Mathf.Min(DetailsHeight, NativeThemeBridge.UiHeight - 92f);
        if (!Mathf.Approximately(windowRect.width, targetW) || !Mathf.Approximately(windowRect.height, targetH))
        {
            float right = windowRect.xMax;
            windowRect.width = targetW;
            windowRect.height = targetH;
            windowRect.x = Mathf.Max(12f, right - targetW);
        }

        windowRect = NativeThemeBridge.DrawWindow(WindowId, windowRect, DrawTrackerWindow, "LIVE ECONOMY & BUDGET TRACKER");
    }

    public void ResetSession()
    {
        windowVisible = false;
        windowPositioned = false;
        helpVisible = false;
        scroll = Vector2.zero;
        nextSampleAt = 0f;
        cashflowTrackers.Clear();
        rows.Clear();
        hqSortBuffer.Clear();
        staleHqBuffer.Clear();
        pilotLabelBuffer.Clear();
    }

    private float ComputeCompactHeight()
    {
        int count = Mathf.Max(1, rows.Count);
        float maxH = Mathf.Max(200f, NativeThemeBridge.UiHeight - 96f);
        return Mathf.Clamp(92f + count * 68f, 168f, maxH);
    }

    private void RefreshSnapshots(bool forceCashflowUpdate)
    {
        object? eco = GroundControlReflection.GetEcoInstance();
        object? enemy = GroundControlReflection.GetEnemyInstance();
        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        AiEconomyProfile profile = AiEconomySettings.GetActiveProfile();

        bool hasPlayerRef = AiEconomySettings.TryGetPlayerReferenceEconomy(eco, out float playerRefGross, out _, out _);
        presetName = profile.Name;
        presetNetFloor = profile.NetIncomeTarget;
        presetCapMult = profile.FairPlayIncomeCapMultiplier;
        presetMineMult = profile.IsVanilla
            ? (GroundControlReflection.GetEnemyDifficulty() == 1 ? GroundControlReflection.GetEnemyMineIncomeMultiplier() : 1f)
            : profile.MineIncomeMultiplier;
        presetStipend = profile.ChampionsFlatStipend;
        incomeTickProgress = GroundControlReflection.GetIncomeTickProgress(eco);

        hqSortBuffer.Clear();
        foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
        {
            if (hq != null)
            {
                hqSortBuffer.Add(hq);
            }
        }

        hqSortBuffer.Sort((a, b) =>
        {
            int rankA = GetFactionSortRank(a, localHq);
            int rankB = GetFactionSortRank(b, localHq);
            int cmp = rankA.CompareTo(rankB);
            if (cmp != 0)
            {
                return cmp;
            }

            string nameA = a.faction != null ? a.faction.factionName : a.name;
            string nameB = b.faction != null ? b.faction.factionName : b.name;
            return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
        });

        float now = Time.unscaledTime;
        PruneStaleCashflowTrackers();

        rows.Clear();
        for (int i = 0; i < hqSortBuffer.Count; i++)
        {
            FactionHQ hq = hqSortBuffer[i];
            bool isLocal = localHq != null && ReferenceEquals(hq, localHq);
            bool isAi = IsFactionDisplayAi(hq, localHq);
            bool canInspect = CanInspectFaction(hq, localHq);

            CashflowTracker? cf = null;
            if (canInspect)
            {
                cf = GetOrCreateTracker(hq);
                if (forceCashflowUpdate || !cf.Initialized)
                {
                    UpdateCashflow(cf, hq.factionFunds, now);
                }
            }

            if (!windowVisible)
            {
                continue;
            }

            var row = new FactionEconomyRow
            {
                Hq = hq,
                FactionName = hq.faction != null && !string.IsNullOrEmpty(hq.faction.factionName)
                    ? hq.faction.factionName
                    : hq.name,
                IsLocal = isLocal,
                IsAi = isAi,
                CanInspect = canInspect,
                RoleBadge = isLocal ? "YOU" : (isAi ? "AI" : "PLAYER"),
            };

            if (!canInspect)
            {
                row.StatusTag = "FOGGED";
                rows.Add(row);
                continue;
            }

            float funds = hq.factionFunds;
            row.Funds = funds;
            row.ObservedIn60s = cf != null ? cf.ObservedIn60s : 0f;
            row.ObservedOut60s = cf != null ? cf.ObservedOut60s : 0f;
            row.ObservedNet60s = cf != null ? cf.ObservedNet60s : 0f;

            float baseIncome = GroundControlReflection.GetBaseIncomePerMinute();
            int heldBases = BaseCaptureIncomeService.CountHeldCapturableBases(hq);
            int bonusBases = BaseCaptureIncomeService.CountCapturedBonusBases(hq);
            float capturedBaseIncome = BaseCaptureIncomeService.GetCapturedBaseIncomePerMinute(hq);
            float stipendIncome = GroundControlReflection.GetAiStipendPerMinute(hq, localHq);
            float rawHqIncome = GroundControlReflection.GetHqIncomePerMinute(eco, hq);
            float rawBldgIncome = Mathf.Max(0f, rawHqIncome - baseIncome - stipendIncome - capturedBaseIncome);
            float mineMult = isAi
                ? (profile.IsVanilla
                    ? (GroundControlReflection.GetEnemyDifficulty() == 1 ? GroundControlReflection.GetEnemyMineIncomeMultiplier() : 1f)
                    : profile.MineIncomeMultiplier)
                : 1f;
            float effectiveBldgIncome = rawBldgIncome * mineMult;
            float effectiveGross = baseIncome + capturedBaseIncome + stipendIncome + effectiveBldgIncome;
            float upkeep = GroundControlReflection.GetFactionUpkeepPerMinute(eco, hq, isLocal);

            row.BaseIncome = baseIncome;
            row.CapturedBaseIncome = capturedBaseIncome;
            row.HeldCapturableBases = heldBases;
            row.CapturedBonusBases = bonusBases;
            row.StipendIncome = stipendIncome;
            row.BuildingIncome = effectiveBldgIncome;
            row.EffectiveGross = effectiveGross;
            row.Upkeep = upkeep;
            row.NetIncome = effectiveGross - upkeep;
            row.MineMultiplier = mineMult;

            row.GoldMines = GroundControlReflection.CountGoldMines(eco, hq);
            row.TotalMines = GroundControlReflection.CountMines(eco, hq);
            row.FreeMines = GroundControlReflection.CountFreeMines(eco, hq);
            row.Factories = GroundControlReflection.CountFactories(hq);
            row.BestDepot = GroundControlReflection.GetBestDepotLevel(hq);
            row.DockLevel = GroundControlReflection.GetNavalDockLevel(hq);
            row.TechTier = DynamicCapAndTechService.IsActive ? DynamicCapAndTechService.Instance.GetSnapshot(hq).TechTier : 1;

            if (!isAi)
            {
                row.HasIncomeCap = false;
                row.ReservedTotal = 0f;
                row.SpendableNow = Mathf.Max(0f, funds);
                row.MaxTickSpend = row.SpendableNow;
                row.StatusTag = "UNLIMITED";
                row.StatusWarning = false;
                row.ExcessThreshold = hq.excessFundsThreshold;
                row.RegularIncome = hq.regularIncome;
                row.PilotsSummary = BuildPilotsSummary(hq);
            }
            else
            {
                AiStrategicPhase phase = AiStrategicDirectorService.Instance.EvaluatePhase(eco, hq);
                int humanPilotCount = hq.GetPlayers(false).Count;
                row.AiPhase = phase;
                row.RoleBadge = humanPilotCount > 0
                    ? $"AI · {FormatPhaseShort(phase)} +{humanPilotCount}P"
                    : $"AI · {FormatPhaseShort(phase)}";
                if (humanPilotCount > 0)
                {
                    row.PilotsSummary = BuildPilotsSummary(hq);
                }
                row.HasIncomeCap = true;
                row.EffectiveMaxGoldMines = AiEconomySettings.GetEffectiveMaxGoldMines(eco, hq, profile);
                row.EffectiveMaxFactories = AiEconomySettings.GetEffectiveMaxFactories(eco, hq, profile);
                row.NetFloor = profile.NetIncomeTarget;
                row.FairPlayGrossCap = (profile.FairPlayIncomeCapMultiplier > 0f && hasPlayerRef)
                    ? playerRefGross * profile.FairPlayIncomeCapMultiplier
                    : 0f;
                row.AllowedGrossCap = profile.IsVanilla
                    ? (row.NetFloor + upkeep)
                    : Mathf.Max(row.NetFloor + upkeep, row.FairPlayGrossCap);

                row.MineBlocked = !profile.IsVanilla && eco != null && AiEconomySettings.IsMineBuildBlocked(eco, hq, profile);
                row.UpgradeBlocked = !profile.IsVanilla && eco != null && AiEconomySettings.IsIncomeUpgradeBlocked(eco, hq, profile);

                float buildReserve = GroundControlReflection.GetEnemyBuildReserve(hq, out bool resGoldMine, out _);
                object? cmdState = GroundControlReflection.TryGetCommanderState(enemy, hq);
                float mineFund = GroundControlReflection.GetStateFloat(cmdState, GroundControlReflection.StateMineFundProp);
                row.MineFund = mineFund;
                row.AirFund = GroundControlReflection.GetStateFloat(cmdState, GroundControlReflection.StateAirFundProp);
                row.AirSaveCap = GroundControlReflection.GetStateFloat(cmdState, GroundControlReflection.StateAirSaveCapProp);
                row.SamFund = GroundControlReflection.GetStateFloat(cmdState, GroundControlReflection.StateSamFundProp);
                row.NavalFund = GroundControlReflection.GetStateFloat(cmdState, GroundControlReflection.StateNavalFundProp);
                float warFundAt = GroundControlReflection.GetStateFloat(cmdState, GroundControlReflection.StateWarFundAtProp);
                row.IsBroke = GroundControlReflection.GetStateBool(cmdState, GroundControlReflection.StateBrokeProp);
                row.SavingForName = GroundControlReflection.GetEnemySavingForName(enemy);

                float clampedFunds = Mathf.Max(0f, funds);
                row.SavedBuildReserve = resGoldMine
                    ? Mathf.Min(mineFund, Mathf.Min(buildReserve, clampedFunds))
                    : Mathf.Min(buildReserve, clampedFunds);
                row.FactoryReserve = GroundControlReflection.GetEnemyFactoryReserve(eco, hq);
                row.FactoryFund = GroundControlReflection.GetEnemyFactoryFundReadOnly(eco, hq, rawHqIncome, row.FactoryReserve);
                row.GodReserve = GroundControlReflection.GetGodOpeningReserve(hq);

                row.ReservedTotal = Mathf.Min(clampedFunds, row.SavedBuildReserve + row.FactoryFund + row.GodReserve);
                row.SpendableNow = Mathf.Max(0f, funds - row.ReservedTotal);

                float reviewShare = GroundControlReflection.IsDuelMission() ? 0.45f : 0.25f;
                float warFundEst = warFundAt >= 0f
                    ? Mathf.Min(Mathf.Max(0f, row.NetIncome) * 0.35f * Mathf.Clamp((Time.time - warFundAt) / 60f, 0f, 1f), clampedFunds)
                    : 0f;
                row.MaxTickSpend = Mathf.Max(row.SpendableNow * reviewShare, warFundEst);

                if (row.IsBroke || row.MaxTickSpend <= 0.5f)
                {
                    row.StatusTag = "SAVING / BROKE";
                    row.StatusWarning = true;
                }
                else if (row.MineBlocked && row.UpgradeBlocked)
                {
                    row.StatusTag = "INC CAPPED";
                    row.StatusWarning = true;
                }
                else if (row.NetIncome < row.NetFloor)
                {
                    row.StatusTag = "GROWING";
                    row.StatusWarning = false;
                }
                else
                {
                    row.StatusTag = "SPENDING";
                    row.StatusWarning = false;
                }
            }

            rows.Add(row);
        }
    }

    private void DrawTrackerWindow(int id)
    {
        float w = windowRect.width;
        float h = windowRect.height;

        if (GUI.Button(
                new Rect(w - 140f, 3f, 74f, 22f),
                compactMode ? "DETAILS" : "COMPACT",
                compactMode ? NativeThemeBridge.SelectedButton : NativeThemeBridge.ChromeButton))
        {
            compactMode = !compactMode;
            helpVisible = false;
        }

        NativeThemeBridge.DrawWindowChrome(w, ref helpVisible, out bool closeClicked);
        if (closeClicked)
        {
            windowVisible = false;
            return;
        }

        float innerW = w - 24f;
        float y = 34f;

        if (helpVisible)
        {
            y += NativeThemeBridge.DrawHelpOverlay(
                new Rect(12f, y, innerW, 96f),
                "Live Income & Budget Tracker shows each faction's funds, gross/net income, upkeep, " +
                "AI Fair-Play income caps, reserved construction funds, and how much money may be spent per AI review tick. " +
                $"In Multiplayer, enemy AI details are host-only (Fog of War protected). Toggle hotkey: {AddonConfig.EconomyTrackerKey.Value}.") + 8f;
        }

        // Top Preset & Income Tick Bar
        GUI.Box(new Rect(12f, y, innerW, 42f), string.Empty, NativeThemeBridge.Panel);
        string capLabel = presetCapMult > 0f ? $"{presetCapMult:0.##}x" : "OFF";
        string headerSummary =
            $"PRESET {presetName.ToUpperInvariant()}   |   FLOOR {FormatCredits(presetNetFloor)}/m   |   CAP {capLabel}   |   MINE x{presetMineMult:0.##}   |   STIP {FormatCredits(presetStipend)}/m";
        GUI.Label(new Rect(20f, y + 4f, innerW - 16f, 18f), headerSummary, NativeThemeBridge.Caption);

        float tickPct = incomeTickProgress >= 0f ? Mathf.Clamp01(incomeTickProgress) : 0f;
        string tickText = incomeTickProgress >= 0f ? $"PAY TICK {(tickPct * 100f):0}%" : "PAY TICK --";
        GUI.Label(new Rect(20f, y + 22f, 108f, 16f), tickText, NativeThemeBridge.MutedLabel);
        NativeThemeBridge.DrawBar(new Rect(128f, y + 25f, innerW - 140f, 9f), tickPct, NativeThemeBridge.Accent);
        y += 48f;

        if (rows.Count == 0)
        {
            GUI.Label(new Rect(14f, y, innerW - 4f, 24f), "Waiting for active factions...", NativeThemeBridge.MutedLabel);
            GUI.DragWindow(new Rect(0f, 0f, w - 148f, 28f));
            return;
        }

        if (compactMode)
        {
            DrawCompactRows(y, innerW, h - y - 10f);
        }
        else
        {
            DrawDetailedRows(y, innerW, h - y - 10f);
        }

        GUI.DragWindow(new Rect(0f, 0f, w - 148f, 28f));
    }

    private void DrawCompactRows(float startY, float innerW, float areaH)
    {
        float curY = startY;
        for (int i = 0; i < rows.Count; i++)
        {
            if (curY + 58f > startY + areaH)
            {
                break;
            }

            FactionEconomyRow row = rows[i];
            Rect card = new(12f, curY, innerW, 56f);
            GUI.Box(card, string.Empty, NativeThemeBridge.Panel);

            if (!row.CanInspect)
            {
                GUI.Label(new Rect(card.x + 8f, card.y + 6f, card.width - 16f, 20f), $"{row.FactionName.ToUpperInvariant()}  [{row.RoleBadge}]", NativeThemeBridge.Header);
                GUI.Label(new Rect(card.x + 8f, card.y + 28f, card.width - 16f, 18f), "HIDDEN — Multiplayer Fog of War", NativeThemeBridge.MutedLabel);
                curY += 62f;
                continue;
            }

            GUI.Label(
                new Rect(card.x + 8f, card.y + 4f, card.width - 132f, 18f),
                $"{row.FactionName.ToUpperInvariant()} [{row.RoleBadge}]",
                NativeThemeBridge.Header);
            GUI.Label(
                new Rect(card.xMax - 122f, card.y + 4f, 114f, 18f),
                row.StatusTag,
                row.StatusWarning ? NativeThemeBridge.WarningLabel : NativeThemeBridge.Caption);

            string budgetLine = row.IsAi
                ? $"FUNDS {FormatCredits(row.Funds)}  |  SPEND {FormatCredits(row.SpendableNow)} (Tick {FormatCredits(row.MaxTickSpend)})  |  NET {FormatSignedCredits(row.NetIncome)}/m  (Cap {FormatCredits(row.AllowedGrossCap)}/m)"
                : $"FUNDS {FormatCredits(row.Funds)}  |  SPENDABLE 100%  |  NET {FormatSignedCredits(row.NetIncome)}/m  |  60s {FormatSignedCredits(row.ObservedNet60s)}";
            GUI.Label(new Rect(card.x + 8f, card.y + 23f, card.width - 16f, 18f), budgetLine, NativeThemeBridge.Label);

            float frac = row.HasIncomeCap && row.AllowedGrossCap > 0.1f
                ? Mathf.Clamp01(row.EffectiveGross / row.AllowedGrossCap)
                : (row.Funds > 0.1f ? Mathf.Clamp01(row.SpendableNow / row.Funds) : 0f);
            Color barColor = row.HasIncomeCap
                ? NativeThemeBridge.StatusColor(Mathf.Clamp01(1f - frac * 0.65f))
                : NativeThemeBridge.Accent;
            NativeThemeBridge.DrawBar(new Rect(card.x + 8f, card.y + 43f, card.width - 16f, 7f), frac, barColor);

            curY += 62f;
        }
    }

    private void DrawDetailedRows(float startY, float innerW, float areaH)
    {
        Rect scrollOuter = new(12f, startY, innerW, Mathf.Max(140f, areaH));
        GUI.Box(scrollOuter, string.Empty, NativeThemeBridge.Inset);

        float contentH = 8f;
        for (int i = 0; i < rows.Count; i++)
        {
            FactionEconomyRow r = rows[i];
            float h = r.CanInspect
                ? (r.IsAi ? (string.IsNullOrEmpty(r.PilotsSummary) ? 210f : 228f) : 186f)
                : 64f;
            contentH += h + 8f;
        }

        Rect viewRect = new(0f, 0f, scrollOuter.width - 18f, Mathf.Max(scrollOuter.height - 6f, contentH));
        scroll = GUI.BeginScrollView(scrollOuter, scroll, viewRect);

        float rowY = 6f;
        for (int i = 0; i < rows.Count; i++)
        {
            FactionEconomyRow row = rows[i];
            float cardH = row.CanInspect
                ? (row.IsAi ? (string.IsNullOrEmpty(row.PilotsSummary) ? 210f : 228f) : 186f)
                : 64f;
            Rect card = new(6f, rowY, viewRect.width - 12f, cardH);
            GUI.Box(card, string.Empty, NativeThemeBridge.Panel);

            if (!row.CanInspect)
            {
                GUI.Label(new Rect(card.x + 10f, card.y + 8f, card.width - 20f, 20f), $"{row.FactionName.ToUpperInvariant()}   [{row.RoleBadge}]", NativeThemeBridge.Header);
                GUI.Label(new Rect(card.x + 10f, card.y + 32f, card.width - 20f, 18f), "HIDDEN — Multiplayer Fog of War (Host-only AI inspection)", NativeThemeBridge.MutedLabel);
                rowY += cardH + 8f;
                continue;
            }

            float lx = card.x + 10f;
            float lw = card.width - 20f;
            float ly = card.y + 6f;

            // Header row
            GUI.Label(new Rect(lx, ly, lw - 126f, 20f), $"{row.FactionName.ToUpperInvariant()}   [{row.RoleBadge}]", NativeThemeBridge.Header);
            GUI.Label(
                new Rect(card.xMax - 122f, ly + 2f, 112f, 18f),
                row.StatusTag,
                row.StatusWarning ? NativeThemeBridge.WarningLabel : NativeThemeBridge.Caption);
            ly += 22f;

            NativeThemeBridge.DrawRule(new Rect(lx, ly, lw, 1f), 0.25f);
            ly += 5f;

            // Section A: Money allowed to use right now
            string budgetText = row.IsAi
                ? $"FUNDS: {FormatCredits(row.Funds)}   |   SPENDABLE: {FormatCredits(row.SpendableNow)}   |   MAX/TICK: {FormatCredits(row.MaxTickSpend)}   |   RSV: {FormatCredits(row.ReservedTotal)}"
                : $"FUNDS: {FormatCredits(row.Funds)}   |   SPENDABLE: {FormatCredits(row.SpendableNow)} (100% Free)   |   RSV: $0m";
            GUI.Label(new Rect(lx, ly, lw, 18f), budgetText, NativeThemeBridge.Label);
            ly += 19f;

            float spendFrac = row.Funds > 0.1f ? Mathf.Clamp01(row.SpendableNow / row.Funds) : 0f;
            NativeThemeBridge.DrawBar(new Rect(lx, ly, lw, 8f), spendFrac, NativeThemeBridge.StatusColor(spendFrac));
            ly += 13f;

            // Section B: Income & Upkeep vs Allowed Cap
            string incomeBreakdown =
                $"GROSS: +{FormatCredits(row.EffectiveGross)}/m (HQ {FormatCredits(row.BaseIncome)} + Bases {FormatCredits(row.CapturedBaseIncome)} + Bldgs {FormatCredits(row.BuildingIncome)} + Stip {FormatCredits(row.StipendIncome)})";
            GUI.Label(new Rect(lx, ly, lw, 18f), incomeBreakdown, NativeThemeBridge.Label);
            ly += 18f;

            string netLine =
                $"UPKEEP: -{FormatCredits(row.Upkeep)}/m   |   NET: {FormatSignedCredits(row.NetIncome)}/m   |   MINE MULT: x{row.MineMultiplier:0.##}";
            GUI.Label(new Rect(lx, ly, lw, 18f), netLine, NativeThemeBridge.Caption);
            ly += 19f;

            if (row.HasIncomeCap)
            {
                string capBasis = row.FairPlayGrossCap > 0f
                    ? $"Player×{presetCapMult:0.##}={FormatCredits(row.FairPlayGrossCap)}/m | Floor {FormatCredits(row.NetFloor)}/m"
                    : $"Net Floor {FormatCredits(row.NetFloor)}/m";
                string ecoGate = (row.MineBlocked || row.UpgradeBlocked) ? "BLOCKED" : "OPEN";
                GUI.Label(
                    new Rect(lx, ly, lw, 18f),
                    $"ALLOWED INCOME CAP: {FormatCredits(row.AllowedGrossCap)}/m ({capBasis}) [{ecoGate}]",
                    row.MineBlocked ? NativeThemeBridge.WarningLabel : NativeThemeBridge.Caption);
                ly += 19f;

                float capFrac = row.AllowedGrossCap > 0.1f ? Mathf.Clamp01(row.EffectiveGross / row.AllowedGrossCap) : 0f;
                NativeThemeBridge.DrawBar(new Rect(lx, ly, lw, 8f), capFrac, NativeThemeBridge.StatusColor(Mathf.Clamp01(1f - capFrac * 0.6f)));
                ly += 13f;
            }
            else
            {
                GUI.Label(
                    new Rect(lx, ly, lw, 18f),
                    "ALLOWED INCOME CAP: Unlimited (Human Faction — builds & upgrades freely)",
                    NativeThemeBridge.Caption);
                ly += 20f;
            }

            // Section C: Observed 60s Cashflow
            string flowText =
                $"60s LIVE FLOW: +{FormatCredits(row.ObservedIn60s)} in  /  -{FormatCredits(row.ObservedOut60s)} out  (Net {FormatSignedCredits(row.ObservedNet60s)})";
            GUI.Label(new Rect(lx, ly, lw, 18f), flowText, NativeThemeBridge.Label);
            ly += 19f;

            // Section D: Infrastructure & Sub-Funds / Pilot Allocations
            string infraText = row.IsAi
                ? $"INFRA: Bases {row.HeldCapturableBases} (+{row.CapturedBonusBases})  |  Mines {row.GoldMines}/{row.EffectiveMaxGoldMines} ({row.TotalMines} tot, {row.FreeMines} free)  |  Fac {row.Factories}/{row.EffectiveMaxFactories}  |  Depot L{row.BestDepot}  |  Dock L{row.DockLevel}  |  Tech T{row.TechTier}"
                : $"INFRA: Bases {row.HeldCapturableBases} (+{row.CapturedBonusBases})  |  Mines {row.GoldMines} ({row.TotalMines} tot)  |  Fac {row.Factories}  |  Depot L{row.BestDepot}  |  Dock L{row.DockLevel}  |  Tech T{row.TechTier}";
            GUI.Label(new Rect(lx, ly, lw, 18f), infraText, NativeThemeBridge.MutedLabel);
            ly += 18f;

            if (row.IsAi)
            {
                string saveExtra = string.IsNullOrEmpty(row.SavingForName) ? string.Empty : $" [{row.SavingForName}]";
                string subFundsText =
                    $"FUNDS: Build {FormatCredits(row.SavedBuildReserve)} (Mine {FormatCredits(row.MineFund)}) | Fac {FormatCredits(row.FactoryFund)}/{FormatCredits(row.FactoryReserve)} | Air {FormatCredits(row.AirFund)}/{FormatCredits(row.AirSaveCap)}{saveExtra} | SAM {FormatCredits(row.SamFund)} | Nav {FormatCredits(row.NavalFund)}";
                GUI.Label(new Rect(lx, ly, lw, 20f), subFundsText, NativeThemeBridge.MutedLabel);
                if (!string.IsNullOrEmpty(row.PilotsSummary))
                {
                    GUI.Label(new Rect(lx, ly + 18f, lw, 18f), $"PILOTS: {row.PilotsSummary}", NativeThemeBridge.MutedLabel);
                }
            }
            else
            {
                string pilotLine = $"PILOTS: {row.PilotsSummary}   |   Excess > {FormatCredits(row.ExcessThreshold)} (+{FormatCredits(row.RegularIncome)} reg)";
                GUI.Label(new Rect(lx, ly, lw, 20f), pilotLine, NativeThemeBridge.MutedLabel);
            }

            rowY += cardH + 8f;
        }

        GUI.EndScrollView();
    }

    private static bool IsFactionDisplayAi(FactionHQ hq, FactionHQ? localHq)
    {
        if (!GroundControlReflection.IsNetworkGuest())
        {
            return GroundControlReflection.IsSafeAiFaction(hq, localHq);
        }

        return hq != null && !ReferenceEquals(hq, localHq) && !GroundControlReflection.HasHumanPlayers(hq);
    }

    private static bool CanInspectFaction(FactionHQ hq, FactionHQ? localHq)
    {
        if (ReferenceEquals(hq, localHq) || GameManager.gameState == GameState.SinglePlayer)
        {
            return true;
        }

        return !GroundControlReflection.IsNetworkGuest()
            && GroundControlReflection.IsSafeAiFaction(hq, localHq);
    }

    private static int GetFactionSortRank(FactionHQ hq, FactionHQ? localHq)
    {
        if (localHq != null && ReferenceEquals(hq, localHq))
        {
            return 0;
        }

        return IsFactionDisplayAi(hq, localHq) ? 2 : 1;
    }

    private static string FormatPhaseShort(AiStrategicPhase phase) => phase switch
    {
        AiStrategicPhase.OpeningEconomy => "OPENING",
        AiStrategicPhase.MidExpansion => "EXPAND",
        AiStrategicPhase.FullSpectrum => "FULL-SPEC",
        AiStrategicPhase.EmergencyReconstruction => "REBUILD",
        _ => "AI",
    };

    private static string FormatCredits(float amount)
    {
        float abs = Mathf.Abs(amount);
        string num = abs >= 100f
            ? abs.ToString("0", CultureInfo.InvariantCulture)
            : abs.ToString("0.#", CultureInfo.InvariantCulture);
        return amount < -0.05f ? $"-${num}m" : $"${num}m";
    }

    private static string FormatSignedCredits(float amount)
    {
        if (Mathf.Abs(amount) < 0.05f)
        {
            return "$0m";
        }

        return amount >= 0f ? $"+{FormatCredits(amount)}" : FormatCredits(amount);
    }

    private CashflowTracker GetOrCreateTracker(FactionHQ hq)
    {
        if (!cashflowTrackers.TryGetValue(hq, out CashflowTracker tracker))
        {
            tracker = new CashflowTracker();
            cashflowTrackers[hq] = tracker;
        }

        return tracker;
    }

    private void PruneStaleCashflowTrackers()
    {
        if (cashflowTrackers.Count == 0)
        {
            return;
        }

        staleHqBuffer.Clear();
        foreach (FactionHQ tracked in cashflowTrackers.Keys)
        {
            if (tracked is null)
            {
                continue;
            }

            if (!hqSortBuffer.Contains(tracked))
            {
                staleHqBuffer.Add(tracked);
            }
        }

        for (int i = 0; i < staleHqBuffer.Count; i++)
        {
            cashflowTrackers.Remove(staleHqBuffer[i]);
        }
    }

    private static void UpdateCashflow(CashflowTracker tracker, float currentFunds, float now)
    {
        if (!tracker.Initialized)
        {
            tracker.Initialized = true;
            tracker.LastFunds = currentFunds;
        }
        else
        {
            float delta = currentFunds - tracker.LastFunds;
            tracker.LastFunds = currentFunds;
            if (Mathf.Abs(delta) > 0.01f)
            {
                tracker.Events.Enqueue((now, delta));
            }
        }

        float cutoff = now - CashflowWindowSeconds;
        while (tracker.Events.Count > 0 && tracker.Events.Peek().Time < cutoff)
        {
            tracker.Events.Dequeue();
        }

        float obsIn = 0f;
        float obsOut = 0f;
        foreach ((float Time, float Delta) ev in tracker.Events)
        {
            if (ev.Delta > 0f)
            {
                obsIn += ev.Delta;
            }
            else
            {
                obsOut += -ev.Delta;
            }
        }

        tracker.ObservedIn60s = obsIn;
        tracker.ObservedOut60s = obsOut;
        tracker.ObservedNet60s = obsIn - obsOut;
    }

    private string BuildPilotsSummary(FactionHQ hq)
    {
        var players = hq.GetPlayers(false);
        if (players == null || players.Count == 0)
        {
            return "None";
        }

        pilotLabelBuffer.Clear();
        float totalAlloc = 0f;
        int limit = Mathf.Min(players.Count, 3);
        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p == null)
            {
                continue;
            }

            totalAlloc += p.Allocation;
            if (pilotLabelBuffer.Count < limit)
            {
                string pname = "Pilot";
                try
                {
                    pname = p.GetPlayerName()?.RawSteamName ?? "Pilot";
                }
                catch
                {
                    pname = "Pilot";
                }

                if (string.IsNullOrWhiteSpace(pname))
                {
                    pname = "Pilot";
                }

                pilotLabelBuffer.Add($"{pname} ({FormatCredits(p.Allocation)})");
            }
        }

        if (players.Count > limit)
        {
            pilotLabelBuffer.Add($"+{players.Count - limit} more");
        }

        return $"{players.Count} ({FormatCredits(totalAlloc)} tot): {string.Join(", ", pilotLabelBuffer)}";
    }
}
