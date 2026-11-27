using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Mirage;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal sealed class AbmDefenseService : IAddonTick, IAddonGui, IAddonResetSession
{
    private sealed class FactionAbmState
    {
        internal int Stockpile { get; set; }
        internal int MaxCapacity { get; set; }
        internal int CheatMinCapacity { get; set; }
        internal int PlantCount { get; set; }
        internal int RadarCount { get; set; }
        internal int SiloCount { get; set; }
        internal int BestRadarLevel { get; set; }
        internal float ProductionRate { get; set; }
        internal bool AutoProduce { get; set; } = true;
        internal bool PaidForCurrent { get; set; }
        internal float ProductionElapsed { get; set; }
        internal bool SeededInitialSilo { get; set; }
    }

    private sealed class TrackedBallisticThreat
    {
        internal Missile Missile { get; set; } = null!;
        internal FactionHQ LauncherHq { get; set; } = null!;
        internal float BlastYield { get; set; }
        internal bool HasBallisticGuidance { get; set; }
        internal float DetectedAt { get; set; }
        internal GlobalPosition LaunchPos { get; set; }
        internal bool Intercepted { get; set; }
        internal Dictionary<FactionHQ, float> NextSalvoAt { get; } = new();
        internal HashSet<FactionHQ> NotifiedFactions { get; } = new();
    }

    private sealed class TrackedInterceptor
    {
        internal Missile Interceptor { get; set; } = null!;
        internal Missile TargetThreat { get; set; } = null!;
        internal FactionHQ DefenderHq { get; set; } = null!;
        internal float SpawnedAt { get; set; }
    }

    private const int WindowId = 148121;
    private const float WindowWidth = 436f;
    private const float WindowHeight = 560f;
    private const float BannerWidth = 760f;
    private const float BannerHeight = 40f;

    private const float ProductionTickInterval = 0.25f;
    private const float LaunchScanInterval = 0.35f;
    private const float SiloFireCooldownSeconds = 3.5f;
    private const float ThreatSalvoCooldownSeconds = 4.5f;
    private const int MaxInterceptorsPerThreatPerFaction = 2;
    private const float InterceptRadiusMeters = 95f;
    private const float MinDisarmAglMeters = 250f;
    private const float MaxInterceptorLifetimeSeconds = 120f;

    private static readonly FieldInfo? MissileWarheadField = AccessTools.Field(typeof(Missile), "warhead");
    private static readonly FieldInfo? MissileBlastYieldField = AccessTools.Field(typeof(Missile), "blastYield");

    internal static AbmDefenseService Instance { get; } = new();

    private readonly Dictionary<FactionHQ, FactionAbmState> factionStates = new();
    private readonly Dictionary<Missile, TrackedBallisticThreat> trackedThreats = new();
    private readonly List<Missile> threatPruneScratch = new();
    private readonly List<TrackedInterceptor> activeInterceptors = new();
    private readonly HashSet<Missile> ownInterceptorSet = new();
    private readonly Dictionary<Unit, float> siloCooldownUntil = new();
    private readonly List<RtsBuildingRecord> abmBuildingsScratch = new();
    private readonly List<RtsBuildingRecord> ownedUiBuffer = new();
    private readonly Dictionary<string, float> alertCooldowns = new(StringComparer.Ordinal);

    private sealed class ClientReplicatedThreat
    {
        internal uint ThreatPid;
        internal FactionHQ? LauncherHq;
        internal float BlastYield;
        internal GlobalPosition LastPos;
    }

    private readonly Dictionary<FactionHQ, int> clientInFlightByHq = new();
    private readonly List<ClientReplicatedThreat> clientThreats = new();

    private MissileDefinition? cachedInterceptorDef;
    private Encyclopedia? cachedEncyclopedia;

    private float nextProductionAt;
    private float nextLaunchScanAt;

    private Rect windowRect = new(0f, 80f, WindowWidth, WindowHeight);
    private bool windowPositioned;
    private bool windowVisible;
    private bool helpVisible;
    private Vector2 scrollPos;
    private string uiFeedback = string.Empty;
    private float uiFeedbackUntil;

    private AbmDefenseService()
    {
    }

    internal bool IsWindowVisible => windowVisible;

    internal void BindMdsBridge()
    {
        MdsBridgeBootstrap.ExternalAbmStockpileQuery = GetAvailableInterceptors;
        MdsBridgeBootstrap.ExternalAbmStockpileReserve = TryReserveInterceptor;
    }

    internal void ToggleWindow()
    {
        windowVisible = !windowVisible;
        helpVisible = false;
    }

    internal void OpenWindow()
    {
        windowVisible = true;
        helpVisible = false;
    }

    internal bool ContainsScreenPoint(Vector2 screenPoint)
    {
        if (!AddonConfig.ModEnabled.Value || !GroundControlReflection.IsCommanderModeActive() || !windowVisible)
        {
            return false;
        }

        Vector2 guiPt = NativeThemeBridge.ScreenToGui(screenPoint);
        return NativeThemeBridge.VisualRect(WindowId, windowRect).Contains(guiPt);
    }

    internal bool IsMouseOverUi() => ContainsScreenPoint(Input.mousePosition);

    internal void CheatAddInterceptors(FactionHQ? hq, int amount)
    {
        if (!SingleplayerCheatService.IsAllowed || hq == null || !((NetworkBehaviour)hq).IsServer || amount <= 0)
        {
            return;
        }

        FactionAbmState state = GetOrCreateState(hq);
        state.Stockpile += amount;
        state.CheatMinCapacity = Math.Max(state.CheatMinCapacity, state.Stockpile);
        state.MaxCapacity = Math.Max(state.MaxCapacity, state.Stockpile);
        SetFeedback($"Added +{amount} ABM Interceptors (SP Cheat). Stockpile: {state.Stockpile}/{state.MaxCapacity}.");
    }

    public void ResetSession()
    {
        factionStates.Clear();
        trackedThreats.Clear();
        threatPruneScratch.Clear();
        activeInterceptors.Clear();
        ownInterceptorSet.Clear();
        siloCooldownUntil.Clear();
        abmBuildingsScratch.Clear();
        ownedUiBuffer.Clear();
        alertCooldowns.Clear();
        clientInFlightByHq.Clear();
        clientThreats.Clear();
        cachedInterceptorDef = null;
        cachedEncyclopedia = null;
        nextProductionAt = 0f;
        nextLaunchScanAt = 0f;
        windowVisible = false;
        helpVisible = false;
        windowPositioned = false;
        uiFeedback = string.Empty;
        uiFeedbackUntil = 0f;
        BindMdsBridge();
    }

    public void Tick()
    {
        if (!AddonConfig.ModEnabled.Value)
        {
            return;
        }

        if (GroundControlReflection.IsCommanderModeActive() && AddonConfig.SamSiteMenuKey.Value.IsDown())
        {
            ToggleWindow();
        }

        float now = Time.unscaledTime;
        PruneThreats(now);
        TickActiveInterceptors(now);

        if (now >= nextProductionAt)
        {
            float elapsed = nextProductionAt > 0f ? Mathf.Min(1.0f, now - nextProductionAt + ProductionTickInterval) : ProductionTickInterval;
            nextProductionAt = now + ProductionTickInterval;
            UpdateAllFactionStockpiles(elapsed);
        }

        if (now >= nextLaunchScanAt)
        {
            nextLaunchScanAt = now + LaunchScanInterval;
            TryLaunchAbmSalvos(now);
        }
    }

    internal int GetAvailableInterceptors(FactionHQ? hq)
    {
        if (hq == null)
        {
            return 0;
        }

        return GetOrCreateState(hq).Stockpile;
    }

    internal int GetMaxCapacity(FactionHQ? hq)
    {
        if (hq == null)
        {
            return 0;
        }

        return GetOrCreateState(hq).MaxCapacity;
    }

    internal bool TryReserveInterceptor(FactionHQ? hq, string missileId)
    {
        if (hq == null || !((NetworkBehaviour)hq).IsServer)
        {
            return false;
        }

        FactionAbmState state = GetOrCreateState(hq);
        if (state.Stockpile <= 0)
        {
            return false;
        }

        state.Stockpile--;
        return true;
    }

    internal void SetAutoProduce(FactionHQ? hq, bool autoProduce)
    {
        if (hq == null || !((NetworkBehaviour)hq).IsServer)
        {
            return;
        }

        GetOrCreateState(hq).AutoProduce = autoProduce;
    }

    internal string BuildServerFactionPayload(FactionHQ hq)
    {
        FactionAbmState state = GetOrCreateState(hq);
        int inFlight = CountActiveInterceptorsForFaction(hq);
        return string.Join(",",
            state.Stockpile.ToString(CultureInfo.InvariantCulture),
            state.MaxCapacity.ToString(CultureInfo.InvariantCulture),
            state.PlantCount.ToString(CultureInfo.InvariantCulture),
            state.RadarCount.ToString(CultureInfo.InvariantCulture),
            state.SiloCount.ToString(CultureInfo.InvariantCulture),
            state.BestRadarLevel.ToString(CultureInfo.InvariantCulture),
            state.ProductionRate.ToString("0.###", CultureInfo.InvariantCulture),
            state.AutoProduce ? "1" : "0",
            state.PaidForCurrent ? "1" : "0",
            state.ProductionElapsed.ToString("0.##", CultureInfo.InvariantCulture),
            inFlight.ToString(CultureInfo.InvariantCulture));
    }

    internal string BuildServerThreatsPayload()
    {
        if (trackedThreats.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (TrackedBallisticThreat t in trackedThreats.Values)
        {
            if (t.Missile == null || t.Missile.disabled || t.Intercepted || !t.Missile.persistentID.IsValid)
            {
                continue;
            }

            GlobalPosition pos = t.Missile.GlobalPosition();
            if (sb.Length > 0) sb.Append(';');
            sb.Append(t.Missile.persistentID.Id.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(t.LauncherHq?.faction?.factionName ?? string.Empty).Append(',')
              .Append(t.BlastYield.ToString("0.#", CultureInfo.InvariantCulture)).Append(',')
              .Append(pos.x.ToString("0.#", CultureInfo.InvariantCulture)).Append(',')
              .Append(pos.y.ToString("0.#", CultureInfo.InvariantCulture)).Append(',')
              .Append(pos.z.ToString("0.#", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    internal void ApplyClientSnapshot(FactionHQ? hq, string statePayload, string threatsPayload)
    {
        if (hq != null && !string.IsNullOrEmpty(statePayload))
        {
            string[] f = statePayload.Split(',');
            if (f.Length >= 11)
            {
                FactionAbmState state = GetOrCreateState(hq);
                if (int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sp)) state.Stockpile = sp;
                if (int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mc)) state.MaxCapacity = mc;
                if (int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pc)) state.PlantCount = pc;
                if (int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rc)) state.RadarCount = rc;
                if (int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sc)) state.SiloCount = sc;
                if (int.TryParse(f[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int br)) state.BestRadarLevel = br;
                if (float.TryParse(f[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float pr)) state.ProductionRate = pr;
                state.AutoProduce = f[7] == "1";
                state.PaidForCurrent = f[8] == "1";
                if (float.TryParse(f[9], NumberStyles.Float, CultureInfo.InvariantCulture, out float pe)) state.ProductionElapsed = pe;
                if (int.TryParse(f[10], NumberStyles.Integer, CultureInfo.InvariantCulture, out int inf)) clientInFlightByHq[hq] = inf;
            }
        }

        clientThreats.Clear();
        if (!string.IsNullOrEmpty(threatsPayload))
        {
            string[] items = threatsPayload.Split(';');
            for (int i = 0; i < items.Length; i++)
            {
                string[] tf = items[i].Split(',');
                if (tf.Length >= 6 &&
                    uint.TryParse(tf[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint pid) &&
                    float.TryParse(tf[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float yld) &&
                    float.TryParse(tf[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float tx) &&
                    float.TryParse(tf[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float ty) &&
                    float.TryParse(tf[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float tz))
                {
                    clientThreats.Add(new ClientReplicatedThreat
                    {
                        ThreatPid = pid,
                        LauncherHq = GcreNetworkSync.FindHqByName(tf[1]),
                        BlastYield = yld,
                        LastPos = new GlobalPosition(tx, ty, tz),
                    });
                }
            }
        }
    }

    internal bool HasActiveThreatForLocal()
    {
        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        return localHq != null && (TryGetTopThreatForFaction(localHq, out _) || TryGetTopClientThreat(localHq, out _));
    }

    private bool TryGetTopClientThreat(FactionHQ hq, out ClientReplicatedThreat? top)
    {
        top = null;
        if (!GcreNetworkSync.IsPureClient || !GcreNetworkSync.HostHasGcre)
        {
            return false;
        }

        float bestYield = -1f;
        for (int i = 0; i < clientThreats.Count; i++)
        {
            ClientReplicatedThreat t = clientThreats[i];
            if (ReferenceEquals(t.LauncherHq, hq))
            {
                continue;
            }

            if (t.BlastYield > bestYield)
            {
                bestYield = t.BlastYield;
                top = t;
            }
        }

        return top != null;
    }

    internal void NotifyPotentialBallisticLaunch(Missile? missile, bool hasBallisticGuidance)
    {
        if (!AddonConfig.ModEnabled.Value || missile == null || missile.disabled)
        {
            return;
        }

        if (ownInterceptorSet.Contains(missile))
        {
            return;
        }

        FactionHQ? launcherHq = missile.NetworkHQ;
        if (launcherHq == null)
        {
            return;
        }

        float yield = missile.GetYield();
        if (!hasBallisticGuidance && yield <= 200f)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (!trackedThreats.TryGetValue(missile, out TrackedBallisticThreat? threat))
        {
            threat = new TrackedBallisticThreat
            {
                Missile = missile,
                LauncherHq = launcherHq,
                BlastYield = yield,
                HasBallisticGuidance = hasBallisticGuidance,
                DetectedAt = now,
                LaunchPos = missile.GlobalPosition(),
            };
            trackedThreats[missile] = threat;
        }
        else
        {
            threat.BlastYield = Mathf.Max(threat.BlastYield, yield);
            threat.HasBallisticGuidance = threat.HasBallisticGuidance || hasBallisticGuidance;
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
        {
            if (hq == null || ReferenceEquals(hq, launcherHq) || threat.NotifiedFactions.Contains(hq))
            {
                continue;
            }

            FactionAbmState state = GetOrCreateState(hq);
            bool hasEarlyWarningAsset = state.RadarCount > 0 || state.SiloCount > 0 || yield > 200f;
            if (!hasEarlyWarningAsset)
            {
                continue;
            }

            threat.NotifiedFactions.Add(hq);
            MdsBridgeBootstrap.RaiseBallisticThreatAction?.Invoke(hq, missile.GlobalPosition(), threat.BlastYield);

            string missileLabel = !string.IsNullOrEmpty(missile.definition?.unitName)
                ? missile.definition!.unitName
                : (!string.IsNullOrEmpty(missile.unitName) ? missile.unitName : "Ballistic Missile");
            string yieldTag = threat.BlastYield > 200f ? $" ({threat.BlastYield:0}t NUCLEAR)" : string.Empty;
            string alertMsg = $"EARLY WARNING: {missileLabel}{yieldTag} LAUNCHED!";

            if (localHq != null && ReferenceEquals(hq, localHq))
            {
                EmitLocalAlert(alertMsg);
                AddonLog.Info($"[ABM] Early warning triggered for {missileLabel} (yield={threat.BlastYield:0}t, ballistic={hasBallisticGuidance}).");
            }
            else
            {
                GcreNetworkSync.BroadcastFactionToast(hq, isAlert: true, alertMsg);
            }
        }
    }

    public void DrawGui()
    {
        if (!AddonConfig.ModEnabled.Value || !GroundControlReflection.IsCommanderModeActive())
        {
            return;
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        if (localHq == null)
        {
            return;
        }

        FactionAbmState state = GetOrCreateState(localHq);

        if (TryGetTopThreatForFaction(localHq, out TrackedBallisticThreat? topThreat) && topThreat != null)
        {
            DrawEarlyWarningBanner(localHq, state, topThreat);
        }
        else if (TryGetTopClientThreat(localHq, out ClientReplicatedThreat? clientTop) && clientTop != null)
        {
            DrawClientEarlyWarningBanner(localHq, state, clientTop);
        }

        if (!windowVisible)
        {
            return;
        }

        if (!windowPositioned)
        {
            windowRect = new Rect(
                Mathf.Max(14f, NativeThemeBridge.UiWidth - (WindowWidth * 2f) - 24f),
                80f,
                WindowWidth,
                WindowHeight);
            windowPositioned = true;
        }

        windowRect = NativeThemeBridge.DrawWindow(WindowId, windowRect, id => DrawAbmWindow(localHq, state), "ABM DEFENSE & PLANT");
    }

    private void UpdateAllFactionStockpiles(float dt)
    {
        foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
        {
            if (hq == null)
            {
                continue;
            }

            FactionAbmState state = GetOrCreateState(hq);
            RefreshBuildingStats(hq, state);

            if (!((NetworkBehaviour)hq).IsServer)
            {
                continue;
            }

            if (state.SiloCount > 0 && !state.SeededInitialSilo)
            {
                state.SeededInitialSilo = true;
                if (state.Stockpile < 2 && state.MaxCapacity > state.Stockpile)
                {
                    state.Stockpile = Math.Min(state.MaxCapacity, state.Stockpile + 2);
                }
            }

            if (state.Stockpile > state.MaxCapacity)
            {
                state.Stockpile = state.MaxCapacity;
            }

            if (state.PlantCount <= 0 || state.ProductionRate <= 0f || !state.AutoProduce || state.Stockpile >= state.MaxCapacity)
            {
                continue;
            }

            float cost = GcreNetworkSync.EffectiveAbmMissileCost;
            if (!state.PaidForCurrent)
            {
                if (!GroundControlReflection.TrySpend(hq, cost))
                {
                    continue;
                }

                state.PaidForCurrent = true;
                state.ProductionElapsed = 0f;
            }

            float cycleSeconds = Mathf.Max(10f, GcreNetworkSync.EffectiveAbmProductionSeconds);
            state.ProductionElapsed += dt * state.ProductionRate;
            if (state.ProductionElapsed < cycleSeconds)
            {
                continue;
            }

            state.ProductionElapsed = 0f;
            state.PaidForCurrent = false;
            state.Stockpile = Mathf.Min(state.MaxCapacity, state.Stockpile + 1);
            AddonLog.Info($"[ABM] {hq.faction?.factionName ?? "Faction"} produced 1 ABM Interceptor ({state.Stockpile}/{state.MaxCapacity}).");

            FactionHQ? localHq = GroundControlReflection.GetLocalHq();
            if (localHq != null && ReferenceEquals(hq, localHq))
            {
                SetFeedback($"ABM Interceptor ready ({state.Stockpile}/{state.MaxCapacity}).");
            }
        }
    }

    private void RefreshBuildingStats(FactionHQ hq, FactionAbmState state)
    {
        DynamicCapAndTechService.Instance.GetOwnedBuildingsOfKinds(hq, abmOnly: true, abmBuildingsScratch);

        int plants = 0;
        int radars = 0;
        int silos = 0;
        int bestRadarLvl = 0;
        int maxCap = 0;
        float prodRate = 0f;

        int siloBaseCap = Math.Max(2, GcreNetworkSync.EffectiveAbmMaxStockPerSite);
        int plantBaseCap = Math.Max(4, siloBaseCap / 2);

        for (int i = 0; i < abmBuildingsScratch.Count; i++)
        {
            RtsBuildingRecord rec = abmBuildingsScratch[i];
            int lvl = Mathf.Clamp(rec.Level, 1, 3);
            switch (rec.Spec.Kind)
            {
                case RtsBuildingKind.AbmPlant:
                    plants++;
                    maxCap += plantBaseCap + (lvl - 1) * 2;
                    prodRate += 1.0f + (lvl - 1) * 0.375f;
                    break;
                case RtsBuildingKind.AbmRadar:
                    radars++;
                    if (lvl > bestRadarLvl)
                    {
                        bestRadarLvl = lvl;
                    }
                    break;
                case RtsBuildingKind.AbmSilo:
                    silos++;
                    maxCap += siloBaseCap + (lvl - 1) * 4;
                    break;
            }
        }

        state.PlantCount = plants;
        state.RadarCount = radars;
        state.SiloCount = silos;
        state.BestRadarLevel = bestRadarLvl;
        state.MaxCapacity = Math.Max(maxCap, state.CheatMinCapacity);
        state.ProductionRate = prodRate;
    }

    private void TryLaunchAbmSalvos(float now)
    {
        if (trackedThreats.Count == 0 || Spawner.i == null)
        {
            return;
        }

        MissileDefinition? interceptorDef = ResolveInterceptorDefinition();
        if (interceptorDef == null)
        {
            return;
        }

        foreach (TrackedBallisticThreat threat in trackedThreats.Values)
        {
            Missile targetMissile = threat.Missile;
            if (targetMissile == null || targetMissile.disabled || threat.Intercepted)
            {
                continue;
            }

            GlobalPosition threatPos = targetMissile.GlobalPosition();
            if (threatPos.y <= 120f)
            {
                continue;
            }

            foreach (FactionHQ defenderHq in FactionRegistry.GetAllHQs())
            {
                if (defenderHq == null
                    || !((NetworkBehaviour)defenderHq).IsServer
                    || ReferenceEquals(defenderHq, threat.LauncherHq))
                {
                    continue;
                }

                FactionAbmState state = GetOrCreateState(defenderHq);
                if (state.SiloCount <= 0 || state.Stockpile <= 0)
                {
                    continue;
                }

                if (CountActiveInterceptorsForThreat(defenderHq, targetMissile) >= MaxInterceptorsPerThreatPerFaction)
                {
                    continue;
                }

                if (threat.NextSalvoAt.TryGetValue(defenderHq, out float nextSalvo) && now < nextSalvo)
                {
                    continue;
                }

                RtsBuildingRecord? chosenSilo = FindBestReadySilo(defenderHq, state, threatPos, now);
                if (chosenSilo == null || chosenSilo.Unit == null || chosenSilo.Unit.disabled)
                {
                    continue;
                }

                if (!TryReserveInterceptor(defenderHq, interceptorDef.jsonKey ?? "abm_silo"))
                {
                    continue;
                }

                Vector3 launchPos = chosenSilo.Unit.transform.position + Vector3.up * 12f;
                Missile? launched = Spawner.i.SpawnMissile(
                    interceptorDef,
                    launchPos,
                    Quaternion.LookRotation(Vector3.up),
                    Vector3.up * 85f,
                    targetMissile,
                    chosenSilo.Unit);

                if (launched == null)
                {
                    state.Stockpile++;
                    continue;
                }

                ownInterceptorSet.Add(launched);
                activeInterceptors.Add(new TrackedInterceptor
                {
                    Interceptor = launched,
                    TargetThreat = targetMissile,
                    DefenderHq = defenderHq,
                    SpawnedAt = now,
                });

                siloCooldownUntil[chosenSilo.Unit] = now + SiloFireCooldownSeconds;
                threat.NextSalvoAt[defenderHq] = now + ThreatSalvoCooldownSeconds;

                FactionHQ? localHq = GroundControlReflection.GetLocalHq();
                string launchAlert = $"ABM VLS LAUNCHED ({state.Stockpile} left)";
                if (localHq != null && ReferenceEquals(defenderHq, localHq))
                {
                    GroundControlReflection.NotifyAlert(launchAlert);
                }
                else
                {
                    GcreNetworkSync.BroadcastFactionToast(defenderHq, isAlert: true, launchAlert);
                }

                AddonLog.Info($"[ABM] {defenderHq.faction?.factionName ?? "Faction"} launched ABM interceptor '{interceptorDef.unitName}' at '{targetMissile.unitName}'.");
            }
        }
    }

    private RtsBuildingRecord? FindBestReadySilo(FactionHQ defenderHq, FactionAbmState state, GlobalPosition threatPos, float now)
    {
        DynamicCapAndTechService.Instance.GetOwnedBuildingsOfKinds(defenderHq, abmOnly: true, abmBuildingsScratch);

        float radarRange = state.RadarCount > 0 ? 45000f + (state.BestRadarLevel - 1) * 17500f : 0f;
        RtsBuildingRecord? best = null;
        float bestDistSq = float.MaxValue;

        for (int i = 0; i < abmBuildingsScratch.Count; i++)
        {
            RtsBuildingRecord rec = abmBuildingsScratch[i];
            if (rec.Spec.Kind != RtsBuildingKind.AbmSilo || rec.Unit == null || rec.Unit.disabled)
            {
                continue;
            }

            if (siloCooldownUntil.TryGetValue(rec.Unit, out float readyAt) && now < readyAt)
            {
                continue;
            }

            float siloOrganicRange = 30000f + (Mathf.Clamp(rec.Level, 1, 3) - 1) * 6000f;
            float maxRange = Mathf.Max(siloOrganicRange, radarRange);
            float dSq = FastMath.SquareDistance(rec.Unit.GlobalPosition(), threatPos);
            if (dSq > maxRange * maxRange || dSq >= bestDistSq)
            {
                continue;
            }

            bestDistSq = dSq;
            best = rec;
        }

        return best;
    }

    private void TickActiveInterceptors(float now)
    {
        for (int i = activeInterceptors.Count - 1; i >= 0; i--)
        {
            TrackedInterceptor tr = activeInterceptors[i];
            if (tr.Interceptor == null
                || tr.Interceptor.disabled
                || tr.TargetThreat == null
                || tr.TargetThreat.disabled
                || now - tr.SpawnedAt > MaxInterceptorLifetimeSeconds)
            {
                if (tr.Interceptor != null)
                {
                    ownInterceptorSet.Remove(tr.Interceptor);
                }

                activeInterceptors.RemoveAt(i);
                continue;
            }

            if (!((NetworkBehaviour)tr.DefenderHq).IsServer)
            {
                continue;
            }

            GlobalPosition intPos = tr.Interceptor.GlobalPosition();
            GlobalPosition threatPos = tr.TargetThreat.GlobalPosition();
            float dist = FastMath.Distance(intPos, threatPos);
            if (dist > InterceptRadiusMeters)
            {
                continue;
            }

            GlobalPosition groundAtThreat = GroundControlReflection.SnapToTerrain(threatPos);
            float agl = threatPos.y - Mathf.Max(0f, groundAtThreat.y);

            if (agl >= MinDisarmAglMeters)
            {
                if (MissileWarheadField != null && MissileWarheadField.GetValue(tr.TargetThreat) is Missile.Warhead warhead)
                {
                    warhead.Armed = false;
                }

                float curYield = MissileBlastYieldField?.GetValue(tr.TargetThreat) is float y
                    ? y
                    : tr.TargetThreat.GetYield();
                if (curYield <= 200f && MissileBlastYieldField != null)
                {
                    MissileBlastYieldField.SetValue(tr.TargetThreat, 0f);
                }
            }

            tr.TargetThreat.Detonate(Vector3.up, hitArmor: false, hitTerrain: false);
            if (!tr.Interceptor.disabled)
            {
                tr.Interceptor.Detonate(Vector3.down, hitArmor: false, hitTerrain: false);
            }

            if (trackedThreats.TryGetValue(tr.TargetThreat, out TrackedBallisticThreat? threatRec))
            {
                threatRec.Intercepted = true;
            }

            FactionHQ? localHq = GroundControlReflection.GetLocalHq();
            string interceptAlert = $"ABM INTERCEPT SUCCESSFUL — WARHEAD DISARMED AT {agl:0}m";
            if (localHq != null && ReferenceEquals(tr.DefenderHq, localHq))
            {
                GroundControlReflection.NotifyAlert(interceptAlert);
            }
            else
            {
                GcreNetworkSync.BroadcastFactionToast(tr.DefenderHq, isAlert: true, interceptAlert);
            }

            AddonLog.Info($"[ABM] Intercepted threat at {agl:0}m AGL (warhead disarmed={agl >= MinDisarmAglMeters}).");
            ownInterceptorSet.Remove(tr.Interceptor);
            activeInterceptors.RemoveAt(i);
        }
    }

    private void PruneThreats(float now)
    {
        if (trackedThreats.Count == 0)
        {
            return;
        }

        threatPruneScratch.Clear();
        foreach (KeyValuePair<Missile, TrackedBallisticThreat> kv in trackedThreats)
        {
            if (kv.Key == null || kv.Key.disabled || kv.Value.Intercepted || now - kv.Value.DetectedAt > 240f)
            {
                threatPruneScratch.Add(kv.Key!);
            }
        }

        for (int i = 0; i < threatPruneScratch.Count; i++)
        {
            trackedThreats.Remove(threatPruneScratch[i]);
        }
    }

    private int CountActiveInterceptorsForThreat(FactionHQ defenderHq, Missile threat)
    {
        int count = 0;
        for (int i = 0; i < activeInterceptors.Count; i++)
        {
            TrackedInterceptor tr = activeInterceptors[i];
            if (tr.Interceptor != null
                && !tr.Interceptor.disabled
                && ReferenceEquals(tr.DefenderHq, defenderHq)
                && ReferenceEquals(tr.TargetThreat, threat))
            {
                count++;
            }
        }

        return count;
    }

    private int CountActiveInterceptorsForFaction(FactionHQ defenderHq)
    {
        int count = 0;
        for (int i = 0; i < activeInterceptors.Count; i++)
        {
            TrackedInterceptor tr = activeInterceptors[i];
            if (tr.Interceptor != null
                && !tr.Interceptor.disabled
                && ReferenceEquals(tr.DefenderHq, defenderHq))
            {
                count++;
            }
        }

        return count;
    }

    private bool TryGetTopThreatForFaction(FactionHQ hq, out TrackedBallisticThreat? top)
    {
        top = null;
        float bestYield = -1f;
        foreach (TrackedBallisticThreat threat in trackedThreats.Values)
        {
            if (threat.Missile == null || threat.Missile.disabled || threat.Intercepted || ReferenceEquals(threat.LauncherHq, hq))
            {
                continue;
            }

            if (threat.BlastYield > bestYield)
            {
                bestYield = threat.BlastYield;
                top = threat;
            }
        }

        return top != null;
    }

    private float ComputeNearestThreatDistanceKm(FactionHQ hq, GlobalPosition threatPos)
    {
        float bestSq = float.MaxValue;
        foreach (Airbase ab in hq.GetAirbases())
        {
            if (ab == null || ab.disabled || ab.center == null)
            {
                continue;
            }

            float dSq = FastMath.SquareDistance(ab.center.GlobalPosition(), threatPos);
            if (dSq < bestSq)
            {
                bestSq = dSq;
            }
        }

        DynamicCapAndTechService.Instance.GetOwnedBuildingsOfKinds(hq, abmOnly: true, abmBuildingsScratch);
        for (int i = 0; i < abmBuildingsScratch.Count; i++)
        {
            Unit? u = abmBuildingsScratch[i].Unit;
            if (u == null || u.disabled)
            {
                continue;
            }

            float dSq = FastMath.SquareDistance(u.GlobalPosition(), threatPos);
            if (dSq < bestSq)
            {
                bestSq = dSq;
            }
        }

        return bestSq < float.MaxValue ? Mathf.Sqrt(bestSq) / 1000f : -1f;
    }

    private MissileDefinition? ResolveInterceptorDefinition()
    {
        Encyclopedia? enc = Encyclopedia.i;
        if (enc == null || enc.missiles == null)
        {
            return null;
        }

        if (ReferenceEquals(cachedEncyclopedia, enc) && cachedInterceptorDef != null)
        {
            return cachedInterceptorDef;
        }

        cachedEncyclopedia = enc;
        cachedInterceptorDef = null;

        string[] preferredKeys = { "SAM_Radar2", "SAM_Radar1", "missile_radar1" };
        for (int p = 0; p < preferredKeys.Length; p++)
        {
            for (int i = 0; i < enc.missiles.Count; i++)
            {
                MissileDefinition? m = enc.missiles[i];
                if (m != null && m.unitPrefab != null && string.Equals(m.jsonKey, preferredKeys[p], StringComparison.OrdinalIgnoreCase))
                {
                    cachedInterceptorDef = m;
                    return m;
                }
            }
        }

        for (int i = 0; i < enc.missiles.Count; i++)
        {
            MissileDefinition? m = enc.missiles[i];
            if (m == null || m.unitPrefab == null)
            {
                continue;
            }

            string key = $"{m.jsonKey} {m.unitName}";
            if (key.IndexOf("SAM", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("Radar", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                cachedInterceptorDef = m;
                return m;
            }
        }

        return null;
    }

    private FactionAbmState GetOrCreateState(FactionHQ hq)
    {
        if (!factionStates.TryGetValue(hq, out FactionAbmState? state))
        {
            state = new FactionAbmState();
            factionStates[hq] = state;
        }

        return state;
    }

    private void DrawClientEarlyWarningBanner(FactionHQ localHq, FactionAbmState state, ClientReplicatedThreat threat)
    {
        float bw = Mathf.Min(BannerWidth, NativeThemeBridge.UiWidth - 24f);
        Rect bannerRect = new(Mathf.Round((NativeThemeBridge.UiWidth - bw) * 0.5f), 78f, bw, BannerHeight);
        Unit? liveUnit = null;
        if (threat.ThreatPid != 0)
        {
            new PersistentID { Id = threat.ThreatPid }.TryGetUnit(out liveUnit);
        }

        string mName = liveUnit != null
            ? (!string.IsNullOrEmpty(liveUnit.definition?.unitName) ? liveUnit.definition!.unitName : liveUnit.unitName)
            : "Ballistic Threat";
        GlobalPosition pos = liveUnit != null && !liveUnit.disabled ? liveUnit.GlobalPosition() : threat.LastPos;
        float distKm = ComputeNearestThreatDistanceKm(localHq, pos);
        string distStr = distKm >= 0f ? $"{distKm:0.0} km" : "-- km";
        int inFlight = clientInFlightByHq.TryGetValue(localHq, out int cInf) ? cInf : 0;
        string yieldStr = threat.BlastYield > 200f ? $" | {threat.BlastYield:0}t NUCLEAR" : " | BALLISTIC";

        GUI.Box(
            bannerRect,
            $"BALLISTIC EARLY WARNING: {mName}{yieldStr}  ·  Range {distStr}  ·  ABM {state.Stockpile}/{state.MaxCapacity}  ·  In Flight: {inFlight}",
            NativeThemeBridge.Alert);
    }

    private void DrawEarlyWarningBanner(FactionHQ localHq, FactionAbmState state, TrackedBallisticThreat threat)
    {
        float bw = Mathf.Min(BannerWidth, NativeThemeBridge.UiWidth - 24f);
        Rect bannerRect = new(Mathf.Round((NativeThemeBridge.UiWidth - bw) * 0.5f), 78f, bw, BannerHeight);
        string mName = !string.IsNullOrEmpty(threat.Missile.definition?.unitName)
            ? threat.Missile.definition!.unitName
            : (!string.IsNullOrEmpty(threat.Missile.unitName) ? threat.Missile.unitName : "Ballistic Threat");
        float distKm = ComputeNearestThreatDistanceKm(localHq, threat.Missile.GlobalPosition());
        string distStr = distKm >= 0f ? $"{distKm:0.0} km" : "-- km";
        int inFlight = CountActiveInterceptorsForFaction(localHq);
        string yieldStr = threat.BlastYield > 200f ? $" | {threat.BlastYield:0}t NUCLEAR" : " | BALLISTIC";

        GUI.Box(
            bannerRect,
            $"BALLISTIC EARLY WARNING: {mName}{yieldStr}  ·  Range {distStr}  ·  ABM {state.Stockpile}/{state.MaxCapacity}  ·  In Flight: {inFlight}",
            NativeThemeBridge.Alert);
    }

    private void DrawAbmWindow(FactionHQ localHq, FactionAbmState state)
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
                "Tier-3 ABM network intercepts ballistic and >200t nuclear missiles at high altitude (>=250m AGL) " +
                "and disarms their warhead safely. Stockpile is shared with MDSFramework VLS launchers.") + 10f;
        }

        FactionCapSnapshot capSnap = DynamicCapAndTechService.Instance.GetSnapshot(localHq);
        string bridgeTag = MdsBridgeBootstrap.IsMdsAttached
            ? "Shared Stockpile: Base ABM Silos + MDS VLS"
            : "Standalone Base ABM Network Active";

        int inFlightCount = ((NetworkBehaviour)localHq).IsServer
            ? CountActiveInterceptorsForFaction(localHq)
            : (clientInFlightByHq.TryGetValue(localHq, out int cInfCount) ? cInfCount : 0);

        GUI.Box(new Rect(12f, y, innerW, 64f), string.Empty, NativeThemeBridge.Panel);
        GUI.Label(
            new Rect(20f, y + 6f, innerW - 16f, 20f),
            $"TECH TIER {capSnap.TechTier}   |   ABM STOCK {state.Stockpile}/{state.MaxCapacity}   |   IN FLIGHT {inFlightCount}",
            NativeThemeBridge.Header);
        GUI.Label(
            new Rect(20f, y + 26f, innerW - 16f, 18f),
            $"Sites — Plants: {state.PlantCount}  |  EW Radars: {state.RadarCount}  |  VLS Silos: {state.SiloCount}",
            NativeThemeBridge.Label);
        GUI.Label(new Rect(20f, y + 44f, innerW - 16f, 16f), bridgeTag, NativeThemeBridge.Caption);
        y += 72f;

        // Production box
        float cycle = Mathf.Max(10f, GcreNetworkSync.EffectiveAbmProductionSeconds);
        float missileCost = GcreNetworkSync.EffectiveAbmMissileCost;
        float prodFrac = state.PaidForCurrent ? Mathf.Clamp01(state.ProductionElapsed / cycle) : 0f;
        float stockFrac = state.MaxCapacity > 0 ? Mathf.Clamp01((float)state.Stockpile / state.MaxCapacity) : 0f;
        string prodState = state.PlantCount == 0
            ? "Build a Tier-3 ABM Interceptor Plant to produce interceptors."
            : (state.Stockpile >= state.MaxCapacity
                ? "Stockpile full (build or upgrade ABM Silos / Plants)."
                : (!state.AutoProduce
                    ? "Auto-production paused."
                    : (state.PaidForCurrent
                        ? $"Manufacturing ABM: {prodFrac * 100f:0}% (Rate {state.ProductionRate:0.0}x)"
                        : $"Waiting for ${missileCost:0}m to start next missile...")));

        GUI.Box(new Rect(12f, y, innerW, 82f), string.Empty, NativeThemeBridge.Panel);
        GUI.Label(
            new Rect(20f, y + 6f, innerW - 140f, 18f),
            $"INTERCEPTOR PLANT (${missileCost:0}m / {cycle:0}s)",
            NativeThemeBridge.Caption);
        bool isServer = ((NetworkBehaviour)localHq).IsServer;
        GUI.enabled = isServer || GcreNetworkSync.HostHasGcre;
        if (GUI.Button(
                new Rect(w - 134f, y + 4f, 114f, 22f),
                state.AutoProduce ? "AUTO-PROD: ON" : "AUTO-PROD: OFF",
                state.AutoProduce ? NativeThemeBridge.SelectedButton : NativeThemeBridge.Button))
        {
            bool nextAuto = !state.AutoProduce;
            state.AutoProduce = nextAuto;
            if (isServer)
            {
                SetFeedback($"ABM auto-production set to {(nextAuto ? "ON" : "OFF")}.");
            }
            else
            {
                GcreNetworkSync.RequestAbmAutoProduce(nextAuto);
                SetFeedback($"Requested ABM auto-production {(nextAuto ? "ON" : "OFF")}...");
            }
        }

        GUI.enabled = true;
        GUI.Label(new Rect(20f, y + 28f, innerW - 16f, 18f), prodState, NativeThemeBridge.MutedLabel);
        NativeThemeBridge.DrawBar(new Rect(20f, y + 50f, (innerW - 22f) * 0.5f, 10f), stockFrac, NativeThemeBridge.Accent);
        NativeThemeBridge.DrawBar(new Rect(26f + (innerW - 22f) * 0.5f, y + 50f, (innerW - 22f) * 0.5f, 10f), prodFrac, NativeThemeBridge.StatusColor(1f));
        GUI.Label(new Rect(20f, y + 62f, (innerW - 22f) * 0.5f, 16f), $"MAGAZINE {state.Stockpile}/{state.MaxCapacity}", NativeThemeBridge.Caption);
        GUI.Label(new Rect(26f + (innerW - 22f) * 0.5f, y + 62f, (innerW - 22f) * 0.5f, 16f), $"BUILD {prodFrac * 100f:0}%", NativeThemeBridge.Caption);
        y += 88f;

        if (!string.IsNullOrEmpty(uiFeedback) && Time.unscaledTime < uiFeedbackUntil)
        {
            GUI.Label(new Rect(14f, y, innerW, 18f), uiFeedback, NativeThemeBridge.Caption);
            y += 20f;
        }

        Rect scrollOuter = new(12f, y, innerW, Mathf.Max(120f, h - y - 14f));
        GUI.Box(scrollOuter, string.Empty, NativeThemeBridge.Inset);

        DynamicCapAndTechService.Instance.GetOwnedBuildingsOfKinds(localHq, abmOnly: true, ownedUiBuffer);
        IReadOnlyList<RtsBuildingSpec> specs = RtsBuildingCatalog.AllSpecs;
        float contentH = 360f + ownedUiBuffer.Count * 50f + Mathf.Max(1, trackedThreats.Count) * 22f;
        Rect viewRect = new(0f, 0f, scrollOuter.width - 18f, Mathf.Max(scrollOuter.height - 6f, contentH));
        scrollPos = GUI.BeginScrollView(scrollOuter, scrollPos, viewRect);

        float rowY = 6f;
        rowY = NativeThemeBridge.DrawSection(new Rect(8f, rowY, viewRect.width - 16f, 20f), "TIER-3 ABM INFRASTRUCTURE");
        for (int i = 0; i < specs.Count; i++)
        {
            RtsBuildingSpec spec = specs[i];
            if (!spec.IsAbmInfrastructure)
            {
                continue;
            }

            int ownedCount = capSnap.GetKindCount(spec.Kind);
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
                    SetFeedback("Encyclopedia not ready for ABM building definition.");
                }
            }

            GUI.enabled = true;
            rowY += cardH + 6f;
        }

        rowY = NativeThemeBridge.DrawSection(new Rect(8f, rowY + 4f, viewRect.width - 16f, 20f), $"OWNED ABM SITES ({ownedUiBuffer.Count})");
        if (ownedUiBuffer.Count == 0)
        {
            GUI.Label(new Rect(10f, rowY, viewRect.width - 20f, 22f), "No ABM structures built yet (requires Command HQ Lvl 3).", NativeThemeBridge.MutedLabel);
            rowY += 26f;
        }
        else
        {
            for (int i = 0; i < ownedUiBuffer.Count; i++)
            {
                RtsBuildingRecord rec = ownedUiBuffer[i];
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

        rowY = NativeThemeBridge.DrawSection(new Rect(8f, rowY + 4f, viewRect.width - 16f, 20f), "EARLY-WARNING CONTACTS");
        int threatRows = 0;
        foreach (TrackedBallisticThreat t in trackedThreats.Values)
        {
            if (t.Missile == null || t.Missile.disabled || t.Intercepted || ReferenceEquals(t.LauncherHq, localHq))
            {
                continue;
            }

            threatRows++;
            string name = t.Missile.definition?.unitName ?? t.Missile.unitName ?? "Ballistic Threat";
            float distKm = ComputeNearestThreatDistanceKm(localHq, t.Missile.GlobalPosition());
            int chasing = CountActiveInterceptorsForThreat(localHq, t.Missile);
            GUI.Label(new Rect(10f, rowY, viewRect.width - 20f, 20f), $"! {name} ({t.BlastYield:0}t) — {distKm:0.0} km · {chasing} tracking", NativeThemeBridge.WarningLabel);
            rowY += 22f;
        }

        if (threatRows == 0 && GcreNetworkSync.IsPureClient && GcreNetworkSync.HostHasGcre)
        {
            for (int i = 0; i < clientThreats.Count; i++)
            {
                ClientReplicatedThreat ct = clientThreats[i];
                if (ReferenceEquals(ct.LauncherHq, localHq))
                {
                    continue;
                }

                threatRows++;
                Unit? liveUnit = null;
                if (ct.ThreatPid != 0)
                {
                    new PersistentID { Id = ct.ThreatPid }.TryGetUnit(out liveUnit);
                }

                string name = liveUnit != null
                    ? (liveUnit.definition?.unitName ?? liveUnit.unitName ?? "Ballistic Threat")
                    : "Ballistic Threat";
                GlobalPosition pos = liveUnit != null && !liveUnit.disabled ? liveUnit.GlobalPosition() : ct.LastPos;
                float distKm = ComputeNearestThreatDistanceKm(localHq, pos);
                GUI.Label(new Rect(10f, rowY, viewRect.width - 20f, 20f), $"! {name} ({ct.BlastYield:0}t) — {distKm:0.0} km", NativeThemeBridge.WarningLabel);
                rowY += 22f;
            }
        }

        if (threatRows == 0)
        {
            GUI.Label(new Rect(10f, rowY, viewRect.width - 20f, 22f), "Airspace clear — monitoring for ballistic & >200t warheads.", NativeThemeBridge.MutedLabel);
        }

        GUI.EndScrollView();
        GUI.DragWindow(new Rect(0f, 0f, w - 128f, 28f));
    }

    private void EmitLocalAlert(string text)
    {
        float now = Time.unscaledTime;
        if (alertCooldowns.TryGetValue(text, out float until) && now < until)
        {
            return;
        }

        alertCooldowns[text] = now + 10f;
        GroundControlReflection.NotifyAlert(text);
    }

    private void SetFeedback(string message)
    {
        uiFeedback = message;
        uiFeedbackUntil = Time.unscaledTime + 6f;
    }
}
