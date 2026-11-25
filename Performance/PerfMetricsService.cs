using UnityEngine;

namespace GroundControlRtsExpanded;

internal sealed class PerfMetricsService : IAddonTick, IAddonGui, IAddonResetSession
{
    private const float BattleSampleIntervalSeconds = 2.0f;
    private const float OverlayWidth = 342f;
    private const float OverlayHeight = 166f;

    internal static PerfMetricsService Instance { get; } = new();

    private float nextSampleAt;
    private float nextRateRollAt;

    private int curConeRangeSkips;
    private int curConeLosChecks;
    private int curIdleTurretSkips;

    private bool overlayVisible;
    private Rect overlayRect = new(0f, 48f, OverlayWidth, OverlayHeight);

    internal bool IsOverlayVisible => overlayVisible;

    internal float SmoothedFps { get; private set; } = 60f;
    internal float FrameTimeMs { get; private set; } = 16.6f;

    internal int PlayerUnits { get; private set; }
    internal int AiUnits { get; private set; }
    internal int TotalUnits { get; private set; }
    internal int GroundUnits { get; private set; }
    internal int AirUnits { get; private set; }
    internal int NavalUnits { get; private set; }
    internal int MaxTrackingContacts { get; private set; }

    internal float IdleTurretIntervalSeconds { get; private set; } = 2.0f;
    internal float GroundVisualScanMultiplier { get; private set; } = 1.0f;
    internal float GroundTrackingThrottleSeconds { get; private set; } = 1.0f;

    internal int DetectorLoSLastFrame { get; private set; }
    internal int DetectorLoSDeferred { get; private set; }

    internal int ConeRangeSkipsPerSec { get; private set; }
    internal int ConeLosChecksPerSec { get; private set; }
    internal int IdleTurretSkipsPerSec { get; private set; }
    internal int SuppressedLogsTotal { get; private set; }
    internal int WrecksCleanedTotal { get; private set; }

    private PerfMetricsService()
    {
    }

    public void Tick()
    {
        if (AddonConfig.ShowPerfOverlayKey.Value.IsDown())
        {
            overlayVisible = !overlayVisible;
        }

        float dt = Time.unscaledDeltaTime;
        if (dt > 0.0001f && dt < 1.0f)
        {
            float fps = 1f / dt;
            SmoothedFps = Mathf.Lerp(SmoothedFps, fps, 0.1f);
            FrameTimeMs = Mathf.Lerp(FrameTimeMs, dt * 1000f, 0.1f);
        }

        float now = Time.unscaledTime;
        if (now >= nextRateRollAt)
        {
            ConeRangeSkipsPerSec = curConeRangeSkips;
            ConeLosChecksPerSec = curConeLosChecks;
            IdleTurretSkipsPerSec = curIdleTurretSkips;
            curConeRangeSkips = 0;
            curConeLosChecks = 0;
            curIdleTurretSkips = 0;
            nextRateRollAt = now + 1.0f;
        }

        if (now >= nextSampleAt)
        {
            nextSampleAt = now + BattleSampleIntervalSeconds;
            RefreshBattleScale();
        }
    }

    internal bool ContainsScreenPoint(Vector2 screenPoint)
    {
        if (!overlayVisible)
        {
            return false;
        }

        Vector2 guiPt = NativeThemeBridge.ScreenToGui(screenPoint);
        return overlayRect.Contains(guiPt);
    }

    public void DrawGui()
    {
        if (!overlayVisible)
        {
            return;
        }

        float x = Mathf.Max(14f, NativeThemeBridge.UiWidth - OverlayWidth - 14f);
        overlayRect = new Rect(x, 48f, OverlayWidth, OverlayHeight);

        GUI.Box(overlayRect, string.Empty, NativeThemeBridge.Panel);
        NativeThemeBridge.DrawAccentLine(new Rect(overlayRect.x, overlayRect.yMax - 2f, overlayRect.width, 2f));

        float lx = overlayRect.x + 12f;
        float lw = overlayRect.width - 24f;
        float ly = overlayRect.y + 8f;

        GUI.Label(new Rect(lx, ly, lw, 20f), $"GC EXPANDED PERF ({AddonConfig.ShowPerfOverlayKey.Value})", NativeThemeBridge.Header);
        ly += 22f;
        GUI.Label(new Rect(lx, ly, lw, 18f), $"FPS: {SmoothedFps:0.0} ({FrameTimeMs:0.0} ms)  |  Anti-Lag: {(AddonConfig.EnableAntiLag.Value ? "ON" : "OFF")}", NativeThemeBridge.Label);
        ly += 20f;
        GUI.Label(new Rect(lx, ly, lw, 18f), $"Units: {TotalUnits} (You: {PlayerUnits} / AI: {AiUnits})", NativeThemeBridge.Label);
        ly += 20f;
        GUI.Label(new Rect(lx, ly, lw, 18f), $"Mix: G {GroundUnits} · A {AirUnits} · N {NavalUnits}  |  Contacts: {MaxTrackingContacts}", NativeThemeBridge.Caption);
        ly += 20f;
        GUI.Label(new Rect(lx, ly, lw, 18f), $"Throttle: Turret {IdleTurretIntervalSeconds:0.#}s · TrackRPC {GroundTrackingThrottleSeconds:0.##}s · Det x{GroundVisualScanMultiplier:0.#}", NativeThemeBridge.Caption);
        ly += 20f;
        GUI.Label(new Rect(lx, ly, lw, 18f), $"Detector LoS/f: {DetectorLoSLastFrame} (def {DetectorLoSDeferred})  |  Cone LoS/s: {ConeLosChecksPerSec}", NativeThemeBridge.Caption);
        ly += 20f;
        GUI.Label(new Rect(lx, ly, lw, 18f), $"ConeSkip/s: {ConeRangeSkipsPerSec}  |  IdleSkip/s: {IdleTurretSkipsPerSec}  |  Cleaned: {WrecksCleanedTotal}", NativeThemeBridge.MutedLabel);
    }

    public void ResetSession()
    {
        nextSampleAt = 0f;
        nextRateRollAt = 0f;
        curConeRangeSkips = 0;
        curConeLosChecks = 0;
        curIdleTurretSkips = 0;
        PlayerUnits = 0;
        AiUnits = 0;
        TotalUnits = 0;
        GroundUnits = 0;
        AirUnits = 0;
        NavalUnits = 0;
        MaxTrackingContacts = 0;
        IdleTurretIntervalSeconds = 2.0f;
        GroundVisualScanMultiplier = 1.0f;
        GroundTrackingThrottleSeconds = 1.0f;
        DetectorLoSLastFrame = 0;
        DetectorLoSDeferred = 0;
        ConeRangeSkipsPerSec = 0;
        ConeLosChecksPerSec = 0;
        IdleTurretSkipsPerSec = 0;
        SuppressedLogsTotal = 0;
        WrecksCleanedTotal = 0;

        SceneComponentCache.Clear();
        TurretIdleThrottlePatch.ClearCaches();
        TargetDetectorAdaptiveIntervalPatch.ClearCaches();
        RpcUpdateTrackingInfoScalePatch.ClearCaches();
        DetectorManagerBudgetPatches.ClearCaches();
        CommanderLogSpamPatch.ClearCaches();
    }

    internal void NoteIdleTurretSkip() => curIdleTurretSkips++;
    internal void NoteConeRangeSkip() => curConeRangeSkips++;
    internal void NoteConeLosCheck() => curConeLosChecks++;
    internal void NoteSuppressedLog() => SuppressedLogsTotal++;
    internal void NoteWreckCleaned() => WrecksCleanedTotal++;

    internal void NoteDetectorBatch(int scheduledCount, int deferredCount)
    {
        DetectorLoSLastFrame = scheduledCount;
        DetectorLoSDeferred = deferredCount;
    }

    private void RefreshBattleScale()
    {
        int player = 0;
        int ai = 0;
        int ground = 0;
        int air = 0;
        int naval = 0;
        int contacts = 0;

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
        {
            if (hq == null)
            {
                continue;
            }

            if (hq.trackingDatabase != null && hq.trackingDatabase.Count > contacts)
            {
                contacts = hq.trackingDatabase.Count;
            }

            if (hq.factionUnits == null)
            {
                continue;
            }

            bool isAi = GroundControlReflection.IsAiFaction(hq, localHq);
            foreach (PersistentID pid in hq.factionUnits)
            {
                if (!pid.TryGetUnit(out Unit unit) || unit == null || unit.disabled)
                {
                    continue;
                }

                bool counted = false;
                if (unit is Aircraft)
                {
                    air++;
                    counted = true;
                }
                else if (unit is Ship)
                {
                    naval++;
                    counted = true;
                }
                else if (unit is GroundVehicle)
                {
                    ground++;
                    counted = true;
                }

                if (counted)
                {
                    if (isAi)
                    {
                        ai++;
                    }
                    else
                    {
                        player++;
                    }
                }
            }
        }

        PlayerUnits = player;
        AiUnits = ai;
        GroundUnits = ground;
        AirUnits = air;
        NavalUnits = naval;
        TotalUnits = player + ai;
        MaxTrackingContacts = contacts;

        if (!AddonConfig.EnableAntiLag.Value || TotalUnits < 200)
        {
            IdleTurretIntervalSeconds = 2.0f;
            GroundVisualScanMultiplier = 1.0f;
            GroundTrackingThrottleSeconds = 1.0f;
        }
        else if (TotalUnits < 400)
        {
            IdleTurretIntervalSeconds = 4.0f;
            GroundVisualScanMultiplier = 2.0f;
            GroundTrackingThrottleSeconds = 1.75f;
        }
        else
        {
            IdleTurretIntervalSeconds = 6.0f;
            GroundVisualScanMultiplier = 3.0f;
            GroundTrackingThrottleSeconds = 2.5f;
        }
    }
}
