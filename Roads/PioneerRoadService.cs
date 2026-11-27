using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Mirage;
using RoadPathfinding;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal sealed class RoadConstructionProject
{
    internal int Id { get; set; }
    internal FactionHQ Hq { get; set; } = null!;
    internal List<RoadSample> Samples { get; set; } = new();
    internal float TotalLengthMeters { get; set; }
    internal float BridgeLengthMeters { get; set; }
    internal float CostPaid { get; set; }
    internal float BuiltLengthMeters { get; set; }
    internal int LastRebuiltSamples { get; set; }
    internal float NextDispatchAt { get; set; }
    internal List<Unit> AssignedBuilders { get; } = new();
    internal GameObject? BlueprintRoot { get; set; }
    internal GameObject? BuiltRoot { get; set; }
    internal bool Completed { get; set; }
}

internal sealed class PioneerRoadService : IAddonTick, IAddonGui, IAddonResetSession
{
    private const float WaypointSnapRadiusMeters = 35f;
    private const float SampleSpacingMeters = 16f;
    private const float MaxChunkMeters = 120f;
    private const float MinimumRoadLengthMeters = 24f;
    private const float MinimumCostCredits = 20f;
    private const int MaxRemoteWaypoints = 64;
    private const int MaxDeserializedSamples = 4096;
    private const float MaxRoadRouteLengthMeters = 50000f;
    private const float BridgeMinimumClearanceAboveSea = 4.5f;
    private const float GorgeBridgeThresholdMeters = 4.5f;
    private const float BuilderWorkRadiusMeters = 62f;
    private const float BuilderAutoAssignRangeMeters = 1500f;
    private const float LandBuildMetersPerSecondPerBuilder = 16f;
    private const float BridgeBuildMetersPerSecondPerBuilder = 9f;
    private const float MinimumBuildDurationSeconds = 10f;

    private const int PlannerWindowId = 148125;
    private const int ActiveProgressWindowId = 148126;

    private static readonly AccessTools.FieldRef<GroundVehicle, PathfindingAgent?> VehiclePathfinderRef =
        AccessTools.FieldRefAccess<GroundVehicle, PathfindingAgent?>("pathfinder");

    private static readonly AccessTools.FieldRef<PathfindingAgent, List<Node>> AgentNodesRef =
        AccessTools.FieldRefAccess<PathfindingAgent, List<Node>>("nodes");

    private static readonly AccessTools.FieldRef<PathfindingAgent, List<GlobalPosition>> AgentWaypointsRef =
        AccessTools.FieldRefAccess<PathfindingAgent, List<GlobalPosition>>("waypoints");

    private static readonly AccessTools.FieldRef<PathfindingAgent, Road?> AgentCurrentRoadRef =
        AccessTools.FieldRefAccess<PathfindingAgent, Road?>("currentRoad");

    private static readonly FieldInfo? EnemyIslandBasesField =
        AccessTools.Field(GroundControlReflection.EnemyCommanderType, "IslandBases");

    private static readonly FieldInfo? EnemyIslandDepotsField =
        AccessTools.Field(GroundControlReflection.EnemyCommanderType, "IslandDepots");

    internal static PioneerRoadService Instance { get; } = new();
    internal static bool HasAnyCompletedBridge { get; private set; }

    private readonly List<GlobalPosition> controlWaypoints = new();
    private readonly List<bool> segmentForcedBridge = new();
    private readonly List<RoadSample> plannedSamples = new();
    private readonly List<RoadConstructionProject> activeProjects = new();
    private readonly List<GameObject> completedRoadObjects = new();
    private readonly List<(int Id, List<RoadSample> Samples)> completedRoads = new();
    private readonly HashSet<int> appliedCompletedRoadIds = new();
    private readonly List<RoadSample> remoteSamplesScratch = new();
    private readonly List<Unit> builderCandidateScratch = new();

    private bool forceNextSegmentBridge;
    private float plannedTotalMeters;
    private float plannedBridgeMeters;
    private float plannedMaxLandGradePercent;
    private float plannedCost;
    private bool plannedHasCursorPreview;
    private bool plannedCursorSnapped;
    private GlobalPosition plannedCursorPos;
    private bool plannedCursorOverWater;

    private LineRenderer? previewLine;
    private Material? previewLineMat;
    private int nextProjectId = 1;
    private float nextProjectTickAt;

    private Rect plannerRect = new(14f, 0f, 392f, 248f);
    private Rect progressRect = new(14f, 80f, 356f, 150f);
    private bool plannerPositioned;
    private bool progressPositioned;
    private string plannerFeedback = string.Empty;
    private float plannerFeedbackUntil;

    private PioneerRoadService()
    {
    }

    internal bool IsPlanningMode { get; private set; }

    internal bool ContainsScreenPoint(Vector2 screenPoint)
    {
        if (!AddonConfig.ModEnabled.Value || !GroundControlReflection.IsCommanderModeActive())
        {
            return false;
        }

        Vector2 guiPt = NativeThemeBridge.ScreenToGui(screenPoint);
        if (IsPlanningMode && NativeThemeBridge.VisualRect(PlannerWindowId, plannerRect).Contains(guiPt))
        {
            return true;
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        for (int i = 0; i < activeProjects.Count; i++)
        {
            if (ShouldShowProjectInHud(activeProjects[i], localHq))
            {
                return NativeThemeBridge.VisualRect(ActiveProgressWindowId, progressRect).Contains(guiPt);
            }
        }

        return false;
    }

    internal bool IsMouseOverUi() => ContainsScreenPoint(Input.mousePosition);

    private static bool ShouldShowProjectInHud(RoadConstructionProject project, FactionHQ? localHq)
        => project.Hq != null && localHq != null && ReferenceEquals(project.Hq, localHq);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool IsFinitePosition(GlobalPosition pos)
        => IsFinite(pos.x) && IsFinite(pos.y) && IsFinite(pos.z);

    internal IReadOnlyList<(int Id, List<RoadSample> Samples)> GetCompletedRoads() => completedRoads;

    internal IReadOnlyList<RoadConstructionProject> GetActiveProjects() => activeProjects;

    internal static string SerializeSamples(IReadOnlyList<RoadSample> samples)
    {
        if (samples == null || samples.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(samples.Count * 28);
        for (int i = 0; i < samples.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(';');
            }

            RoadSample s = samples[i];
            sb.Append(s.Pos.x.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
              .Append(s.Pos.y.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
              .Append(s.Pos.z.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
              .Append(s.IsBridge ? '1' : '0').Append(',')
              .Append(s.GroundY.ToString("0.##", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    internal static List<RoadSample> DeserializeSamples(string data)
    {
        var result = new List<RoadSample>();
        if (string.IsNullOrEmpty(data))
        {
            return result;
        }

        string[] parts = data.Split(';');
        int limit = Mathf.Min(parts.Length, MaxDeserializedSamples);
        for (int i = 0; i < limit; i++)
        {
            string[] f = parts[i].Split(',');
            if (f.Length >= 5 &&
                float.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) &&
                float.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float gy) &&
                IsFinite(x) && IsFinite(y) && IsFinite(z) && IsFinite(gy))
            {
                result.Add(new RoadSample
                {
                    Pos = new GlobalPosition(x, y, z),
                    IsBridge = f[3] == "1",
                    GroundY = gy,
                });
            }
        }

        return result;
    }

    internal void CompleteAllActiveProjectsForHq(FactionHQ? hq)
    {
        if (hq == null || GcreNetworkSync.IsPureClient)
        {
            return;
        }

        for (int i = activeProjects.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(activeProjects[i].Hq, hq))
            {
                RoadConstructionProject p = activeProjects[i];
                activeProjects.RemoveAt(i);
                FinalizeProject(p);
            }
        }
    }

    internal void TogglePlanningMode()
    {
        if (IsPlanningMode)
        {
            CancelPlanning();
            return;
        }

        if (GroundControlReflection.IsEconomyAwaitingPlacement())
        {
            GroundControlReflection.CancelEconomyBuild();
        }

        IsPlanningMode = true;
        forceNextSegmentBridge = false;
        controlWaypoints.Clear();
        segmentForcedBridge.Clear();
        plannedSamples.Clear();
        plannedTotalMeters = 0f;
        plannedBridgeMeters = 0f;
        plannedMaxLandGradePercent = 0f;
        plannedCost = 0f;
        SetFeedback("LMB add waypoint (auto-snaps to existing roads) · RMB undo · Enter build.", 8f);
    }

    internal void CancelPlanning()
    {
        IsPlanningMode = false;
        controlWaypoints.Clear();
        segmentForcedBridge.Clear();
        plannedSamples.Clear();
        plannedHasCursorPreview = false;
        plannedCursorSnapped = false;
        DestroyPreviewLine();
    }

    public void Tick()
    {
        if (!AddonConfig.ModEnabled.Value)
        {
            if (IsPlanningMode)
            {
                CancelPlanning();
            }

            return;
        }

        if (GroundControlReflection.IsCommanderModeActive() && AddonConfig.RoadBuildKey.Value.IsDown())
        {
            TogglePlanningMode();
        }

        if (IsPlanningMode)
        {
            if (!GroundControlReflection.IsCommanderModeActive())
            {
                CancelPlanning();
            }
            else
            {
                TickPlannerPreview();
                HandlePlannerKeyboardShortcuts();
            }
        }

        KeepObjectsAttachedToDatum();

        if (GcreNetworkSync.IsPureClient)
        {
            nextProjectTickAt = 0f;
            return;
        }

        float now = Time.unscaledTime;
        if (activeProjects.Count > 0 && now >= nextProjectTickAt)
        {
            float dt = nextProjectTickAt > 0f ? Mathf.Clamp(now - nextProjectTickAt + 0.5f, 0.1f, 2.0f) : 0.5f;
            nextProjectTickAt = now + 0.5f;
            TickProjects(now, dt);
        }
        else if (activeProjects.Count == 0)
        {
            nextProjectTickAt = 0f;
        }
    }

    public void DrawGui()
    {
        if (!AddonConfig.ModEnabled.Value || !GroundControlReflection.IsCommanderModeActive())
        {
            return;
        }

        if (IsPlanningMode)
        {
            if (!plannerPositioned)
            {
                plannerRect = new Rect(14f, Mathf.Max(80f, NativeThemeBridge.UiHeight - 268f), 392f, 248f);
                plannerPositioned = true;
            }

            plannerRect = NativeThemeBridge.DrawWindow(
                PlannerWindowId,
                plannerRect,
                _ => DrawPlannerWindow(),
                "PIONEER ROADS & BRIDGES");
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        int visibleCount = 0;
        for (int i = 0; i < activeProjects.Count; i++)
        {
            if (ShouldShowProjectInHud(activeProjects[i], localHq))
            {
                visibleCount++;
            }
        }

        if (visibleCount > 0)
        {
            float dynamicHeight = Mathf.Clamp(44f + visibleCount * 54f, 96f, 280f);
            if (!progressPositioned)
            {
                progressRect = new Rect(14f, 80f, 356f, dynamicHeight);
                progressPositioned = true;
            }
            else
            {
                progressRect.height = dynamicHeight;
            }

            progressRect = NativeThemeBridge.DrawWindow(
                ActiveProgressWindowId,
                progressRect,
                _ => DrawActiveProjectsWindow(),
                "ROAD CONSTRUCTION");
        }
    }

    public void ResetSession()
    {
        CancelPlanning();
        plannerPositioned = false;
        progressPositioned = false;

        for (int i = 0; i < activeProjects.Count; i++)
        {
            RoadConstructionProject p = activeProjects[i];
            RoadMeshBuilder.DestroyRoadObject(p.BlueprintRoot);
            p.BlueprintRoot = null;
            RoadMeshBuilder.DestroyRoadObject(p.BuiltRoot);
            p.BuiltRoot = null;
        }

        activeProjects.Clear();

        for (int i = 0; i < completedRoadObjects.Count; i++)
        {
            RoadMeshBuilder.DestroyRoadObject(completedRoadObjects[i]);
        }

        completedRoadObjects.Clear();
        completedRoads.Clear();
        appliedCompletedRoadIds.Clear();
        remoteSamplesScratch.Clear();
        HasAnyCompletedBridge = false;
        PioneerBridgeDriveCachePatch.ClearCache();
        RoadMeshBuilder.ClearMaterials();
        nextProjectId = 1;
        nextProjectTickAt = 0f;
    }

    internal void HandleWorldClick(Vector2 screenPosition, bool overMap, bool primaryDown, bool secondaryDown)
    {
        if (!IsPlanningMode || overMap || NativeThemeBridge.IsAnyAddonUiUnderScreenPoint(screenPosition))
        {
            return;
        }

        if (secondaryDown)
        {
            UndoOrExit();
            return;
        }

        if (primaryDown && TrySampleWorldPoint(screenPosition, out GlobalPosition worldPt, out _))
        {
            worldPt = TrySnapEndpointToRoadNetwork(worldPt, out _);
            if (controlWaypoints.Count > 0)
            {
                if (FastMath.Distance(controlWaypoints[controlWaypoints.Count - 1], worldPt) < 6f)
                {
                    return;
                }

                segmentForcedBridge.Add(forceNextSegmentBridge);
            }

            controlWaypoints.Add(worldPt);
            RebuildPlannedRoute();
        }
    }

    private void UndoOrExit()
    {
        if (controlWaypoints.Count == 0)
        {
            CancelPlanning();
            return;
        }

        controlWaypoints.RemoveAt(controlWaypoints.Count - 1);
        if (segmentForcedBridge.Count > 0)
        {
            segmentForcedBridge.RemoveAt(segmentForcedBridge.Count - 1);
        }

        RebuildPlannedRoute();
    }

    private void HandlePlannerKeyboardShortcuts()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CancelPlanning();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            TryCommitPlannedRoad();
        }
    }

    private void TickPlannerPreview()
    {
        plannedHasCursorPreview = false;
        plannedCursorSnapped = false;
        DynamicMap? map = SceneSingleton<DynamicMap>.i;
        bool overMap = map != null && DynamicMap.mapMaximized && map.IsCursorInMapRectangle();
        Vector2 mousePos = Input.mousePosition;
        if (!overMap && !NativeThemeBridge.IsAnyAddonUiUnderScreenPoint(mousePos))
        {
            if (TrySampleWorldPoint(mousePos, out GlobalPosition cursorWorld, out bool overWater))
            {
                plannedCursorPos = TrySnapEndpointToRoadNetwork(cursorWorld, out bool snapped);
                plannedCursorSnapped = snapped;
                plannedCursorOverWater = overWater;
                plannedHasCursorPreview = true;
            }
        }

        UpdatePreviewLineRenderer();
    }

    private void UpdatePreviewLineRenderer()
    {
        if (plannedSamples.Count == 0 && (!plannedHasCursorPreview || controlWaypoints.Count == 0))
        {
            if (previewLine != null)
            {
                previewLine.positionCount = 0;
            }

            return;
        }

        EnsurePreviewLine();
        if (previewLine == null)
        {
            return;
        }

        RoadMeshBuilder.AttachToDatumOrigin(previewLine.gameObject);

        int extraCursor = plannedHasCursorPreview && controlWaypoints.Count >= 1 ? 1 : 0;
        int totalPts = Mathf.Max(plannedSamples.Count, controlWaypoints.Count) + extraCursor;
        previewLine.positionCount = totalPts;

        if (plannedSamples.Count >= 2)
        {
            for (int i = 0; i < plannedSamples.Count; i++)
            {
                Vector3 v = plannedSamples[i].Pos.AsVector3() + Vector3.up * 1.1f;
                previewLine.SetPosition(i, v);
            }

            if (extraCursor == 1)
            {
                float cursorY = plannedCursorOverWater ? Datum.SeaLevel.y + BridgeMinimumClearanceAboveSea : plannedCursorPos.y;
                previewLine.SetPosition(plannedSamples.Count, new Vector3(plannedCursorPos.x, cursorY + 1.1f, plannedCursorPos.z));
            }
        }
        else if (controlWaypoints.Count == 1 && extraCursor == 1)
        {
            previewLine.positionCount = 2;
            previewLine.SetPosition(0, controlWaypoints[0].AsVector3() + Vector3.up * 1.1f);
            float cursorY = plannedCursorOverWater ? Datum.SeaLevel.y + BridgeMinimumClearanceAboveSea : plannedCursorPos.y;
            previewLine.SetPosition(1, new Vector3(plannedCursorPos.x, cursorY + 1.1f, plannedCursorPos.z));
        }

        Color lineColor = plannedMaxLandGradePercent > GcreNetworkSync.EffectiveMaxRoadGradePercent
            ? new Color(1f, 0.30f, 0.25f, 0.95f)
            : plannedBridgeMeters > 1f
                ? new Color(1f, 0.78f, 0.25f, 0.95f)
                : new Color(0.32f, 0.96f, 0.55f, 0.95f);

        previewLine.startColor = lineColor;
        previewLine.endColor = lineColor;
    }

    private void RebuildPlannedRoute()
    {
        ComputeRoadRoute(
            controlWaypoints,
            segmentForcedBridge,
            GcreNetworkSync.EffectiveRoadCostPer100m,
            GcreNetworkSync.EffectiveBridgeCostPer100m,
            plannedSamples,
            out plannedTotalMeters,
            out plannedBridgeMeters,
            out plannedMaxLandGradePercent,
            out plannedCost);
    }

    private static void ComputeRoadRoute(
        IReadOnlyList<GlobalPosition> waypoints,
        IReadOnlyList<bool> forcedBridges,
        float roadCostPer100m,
        float bridgeCostPer100m,
        List<RoadSample> outSamples,
        out float totalMeters,
        out float bridgeMeters,
        out float maxLandGradePercent,
        out float cost)
    {
        outSamples.Clear();
        totalMeters = 0f;
        bridgeMeters = 0f;
        maxLandGradePercent = 0f;
        cost = 0f;

        if (waypoints.Count < 2)
        {
            return;
        }

        // 1. Subdivide waypoint segments at ~16 m spacing
        for (int seg = 0; seg < waypoints.Count - 1; seg++)
        {
            GlobalPosition a = waypoints[seg];
            GlobalPosition b = waypoints[seg + 1];
            bool forcedBridge = seg < forcedBridges.Count && forcedBridges[seg];

            Vector3 flat = new Vector3(b.x - a.x, 0f, b.z - a.z);
            float horiz = flat.magnitude;
            int steps = Mathf.Max(1, Mathf.CeilToInt(horiz / SampleSpacingMeters));

            int startStep = seg == 0 ? 0 : 1;
            for (int s = startStep; s <= steps; s++)
            {
                float t = (float)s / steps;
                float gx = Mathf.Lerp(a.x, b.x, t);
                float gz = Mathf.Lerp(a.z, b.z, t);
                GlobalPosition sampled = SampleGroundHeight(new GlobalPosition(gx, Mathf.Lerp(a.y, b.y, t), gz), out bool underwater);
                bool bridge = forcedBridge || underwater;
                outSamples.Add(new RoadSample
                {
                    Pos = sampled,
                    IsBridge = bridge,
                    GroundY = sampled.y,
                });
            }
        }

        if (outSamples.Count < 2)
        {
            return;
        }

        // 2. Detect deep gullies compared to local endpoint chord & resolve smooth bridge deck heights
        for (int i = 0; i < outSamples.Count; i++)
        {
            if (outSamples[i].IsBridge)
            {
                continue;
            }

            // Look back & ahead 3 samples (~48 m): if local terrain dips > 4.5 m beneath chord, elevate as bridge span
            int prevIdx = Mathf.Max(0, i - 3);
            int nextIdx = Mathf.Min(outSamples.Count - 1, i + 3);
            if (nextIdx > prevIdx)
            {
                float t = (float)(i - prevIdx) / (nextIdx - prevIdx);
                float chordY = Mathf.Lerp(outSamples[prevIdx].GroundY, outSamples[nextIdx].GroundY, t);
                if (chordY - outSamples[i].GroundY > GorgeBridgeThresholdMeters)
                {
                    RoadSample rs = outSamples[i];
                    rs.IsBridge = true;
                    outSamples[i] = rs;
                }
            }
        }

        // Smooth bridge deck elevations across contiguous bridge runs with bank-level approaches
        int idx = 0;
        while (idx < outSamples.Count)
        {
            if (!outSamples[idx].IsBridge)
            {
                idx++;
                continue;
            }

            int runStart = idx;
            while (idx < outSamples.Count && outSamples[idx].IsBridge)
            {
                idx++;
            }

            int runEnd = idx - 1;
            float leftBankY = runStart > 0
                ? outSamples[runStart - 1].GroundY
                : Mathf.Max(outSamples[runStart].GroundY, Datum.SeaLevel.y + BridgeMinimumClearanceAboveSea);
            float rightBankY = runEnd + 1 < outSamples.Count
                ? outSamples[runEnd + 1].GroundY
                : Mathf.Max(outSamples[runEnd].GroundY, Datum.SeaLevel.y + BridgeMinimumClearanceAboveSea);

            float minSeaDeck = Datum.SeaLevel.y + BridgeMinimumClearanceAboveSea;
            for (int k = runStart; k <= runEnd; k++)
            {
                float t = runEnd > runStart ? (float)(k - runStart) / (runEnd - runStart) : 0.5f;
                float rampEdge = Mathf.Min(1f, Mathf.Min(k - runStart + 1, runEnd - k + 1) / 2.5f);
                float spanY = Mathf.Max(Mathf.Lerp(leftBankY, rightBankY, t), minSeaDeck);
                RoadSample rs = outSamples[k];
                rs.Pos = new GlobalPosition(rs.Pos.x, Mathf.Lerp(Mathf.Lerp(leftBankY, rightBankY, t), spanY, rampEdge), rs.Pos.z);
                outSamples[k] = rs;
            }
        }

        // Keep start/end anchors firmly as land/approach if on dry ground so RoadNodes blend smoothly with existing roads
        RoadSample first = outSamples[0];
        if (first.GroundY > Datum.SeaLevel.y + 0.5f)
        {
            first.IsBridge = false;
            first.Pos = new GlobalPosition(first.Pos.x, first.GroundY, first.Pos.z);
            outSamples[0] = first;
        }

        RoadSample last = outSamples[outSamples.Count - 1];
        if (last.GroundY > Datum.SeaLevel.y + 0.5f)
        {
            last.IsBridge = false;
            last.Pos = new GlobalPosition(last.Pos.x, last.GroundY, last.Pos.z);
            outSamples[outSamples.Count - 1] = last;
        }

        // 3. Measure total length, bridge share, max land grade %, and credits cost
        for (int i = 0; i < outSamples.Count - 1; i++)
        {
            RoadSample s0 = outSamples[i];
            RoadSample s1 = outSamples[i + 1];
            float d = FastMath.Distance(s0.Pos, s1.Pos);
            totalMeters += d;

            bool segBridge = s0.IsBridge || s1.IsBridge;
            if (segBridge)
            {
                bridgeMeters += d;
            }
            else
            {
                float horiz = Vector2.Distance(new Vector2(s0.Pos.x, s0.Pos.z), new Vector2(s1.Pos.x, s1.Pos.z));
                float grade = horiz > 1f ? Mathf.Abs(s1.Pos.y - s0.Pos.y) / horiz * 100f : 0f;
                if (grade > maxLandGradePercent)
                {
                    maxLandGradePercent = grade;
                }
            }
        }

        float roadOnlyMeters = Mathf.Max(0f, totalMeters - bridgeMeters);
        float rawCost = roadOnlyMeters * 0.01f * roadCostPer100m
            + bridgeMeters * 0.01f * bridgeCostPer100m;
        cost = Mathf.Max(MinimumCostCredits, Mathf.Ceil(rawCost));
    }

    private bool CanCommitPlannedRoad(out string reason)
    {
        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        if (localHq == null)
        {
            reason = "No local HQ.";
            return false;
        }

        if (GcreNetworkSync.IsPureClient && !GcreNetworkSync.HostHasGcre)
        {
            reason = "Host requires GroundControlRtsExpanded.";
            return false;
        }

        if (controlWaypoints.Count < 2 || plannedSamples.Count < 2)
        {
            reason = "Place at least 2 waypoints (LMB).";
            return false;
        }

        if (plannedTotalMeters < MinimumRoadLengthMeters)
        {
            reason = $"Route too short ({plannedTotalMeters:0}/{MinimumRoadLengthMeters:0} m).";
            return false;
        }

        float maxGrade = GcreNetworkSync.EffectiveMaxRoadGradePercent;
        if (plannedMaxLandGradePercent > maxGrade)
        {
            reason = $"Land grade {plannedMaxLandGradePercent:0}% > {maxGrade:0}% max! Toggle Bridge Span or reroute.";
            return false;
        }

        if (localHq.factionFunds < plannedCost)
        {
            reason = $"Insufficient funds ({plannedCost:0} Cr needed).";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private void TryCommitPlannedRoad()
    {
        if (!CanCommitPlannedRoad(out string blockReason))
        {
            SetFeedback(blockReason, 5f);
            return;
        }

        if (GcreNetworkSync.IsPureClient)
        {
            GcreNetworkSync.RequestBuildRoad(controlWaypoints, segmentForcedBridge);
            CancelPlanning();
            SetFeedback("Road construction order sent to host.", 4f);
            return;
        }

        FactionHQ localHq = GroundControlReflection.GetLocalHq()!;
        if (!GroundControlReflection.TrySpend(localHq, plannedCost))
        {
            SetFeedback("Insufficient faction funds.", 4f);
            return;
        }

        var samplesCopy = new List<RoadSample>(plannedSamples);
        float totalM = plannedTotalMeters;
        float bridgeM = plannedBridgeMeters;
        float paid = plannedCost;
        CancelPlanning();

        string msg = StartServerProject(localHq, samplesCopy, totalM, bridgeM, paid);
        SetFeedback(msg, 6f);
    }

    internal bool TryCommitRemoteRoad(
        FactionHQ hq,
        List<GlobalPosition> waypoints,
        List<bool> forcedBridges,
        out string reply)
    {
        if (hq == null || !((NetworkBehaviour)hq).IsServer)
        {
            reply = "Host authority required for road construction.";
            return false;
        }

        if (waypoints == null || waypoints.Count < 2)
        {
            reply = "Place at least 2 waypoints.";
            return false;
        }

        if (waypoints.Count > MaxRemoteWaypoints)
        {
            reply = $"Too many waypoints (max {MaxRemoteWaypoints}).";
            return false;
        }

        GetMapBounds(out float halfX, out float halfZ);
        float approxLength = 0f;
        for (int i = 0; i < waypoints.Count; i++)
        {
            GlobalPosition wp = waypoints[i];
            if (!IsFinitePosition(wp)
                || Mathf.Abs(wp.x) > halfX
                || Mathf.Abs(wp.z) > halfZ
                || Mathf.Abs(wp.y) > 10000f)
            {
                reply = "Road waypoint coordinates are invalid or out of bounds.";
                return false;
            }

            if (i > 0)
            {
                approxLength += FlatDist(waypoints[i - 1], wp);
                if (!IsFinite(approxLength) || approxLength > MaxRoadRouteLengthMeters)
                {
                    reply = $"Route exceeds maximum allowed length ({MaxRoadRouteLengthMeters:0} m).";
                    return false;
                }
            }
        }

        ComputeRoadRoute(
            waypoints,
            forcedBridges ?? (IReadOnlyList<bool>)Array.Empty<bool>(),
            AddonConfig.RoadCostPer100m.Value,
            AddonConfig.BridgeCostPer100m.Value,
            remoteSamplesScratch,
            out float totalMeters,
            out float bridgeMeters,
            out float maxGrade,
            out float cost);

        if (!IsFinite(totalMeters) || !IsFinite(bridgeMeters) || !IsFinite(maxGrade) || !IsFinite(cost) || cost <= 0f)
        {
            remoteSamplesScratch.Clear();
            reply = "Invalid road route calculation.";
            return false;
        }

        if (remoteSamplesScratch.Count < 2 || totalMeters < MinimumRoadLengthMeters)
        {
            remoteSamplesScratch.Clear();
            reply = $"Route too short ({totalMeters:0}/{MinimumRoadLengthMeters:0} m).";
            return false;
        }

        if (totalMeters > MaxRoadRouteLengthMeters)
        {
            remoteSamplesScratch.Clear();
            reply = $"Route exceeds maximum allowed length ({MaxRoadRouteLengthMeters:0} m).";
            return false;
        }

        if (maxGrade > AddonConfig.MaxRoadGradePercent.Value)
        {
            remoteSamplesScratch.Clear();
            reply = $"Land grade {maxGrade:0}% exceeds {AddonConfig.MaxRoadGradePercent.Value:0}% max.";
            return false;
        }

        if (!IsFinite(hq.factionFunds) || hq.factionFunds < cost || !GroundControlReflection.TrySpend(hq, cost))
        {
            remoteSamplesScratch.Clear();
            reply = $"Insufficient funds ({cost:0} Cr needed).";
            return false;
        }

        var samplesCopy = new List<RoadSample>(remoteSamplesScratch);
        remoteSamplesScratch.Clear();
        reply = StartServerProject(hq, samplesCopy, totalMeters, bridgeMeters, cost);
        return true;
    }

    private static void GetMapBounds(out float halfX, out float halfZ)
    {
        halfX = 60000f;
        halfZ = 60000f;
        LevelInfo? level = NetworkSceneSingleton<LevelInfo>.i;
        if (level == null)
        {
            return;
        }

        if (level.LoadedMapSettings != null && level.LoadedMapSettings.MapSize.x > 0f && level.LoadedMapSettings.MapSize.y > 0f)
        {
            halfX = level.LoadedMapSettings.MapSize.x * 0.5f + 500f;
            halfZ = level.LoadedMapSettings.MapSize.y * 0.5f + 500f;
        }
        else if (level.mapSize > 0f)
        {
            halfX = level.mapSize * 0.5f + 500f;
            halfZ = level.mapSize * 0.5f + 500f;
        }
    }

    internal bool TryCancelRemoteRoad(FactionHQ hq, int projectId, out string reply)
    {
        for (int i = 0; i < activeProjects.Count; i++)
        {
            if (activeProjects[i].Id == projectId && ReferenceEquals(activeProjects[i].Hq, hq))
            {
                CancelProjectAndRefund(i);
                reply = $"Road #{projectId} cancelled.";
                return true;
            }
        }

        reply = string.Empty;
        return false;
    }

    private string StartServerProject(
        FactionHQ hq,
        List<RoadSample> samplesCopy,
        float totalMeters,
        float bridgeMeters,
        float costPaid)
    {
        int id = nextProjectId++;
        var project = new RoadConstructionProject
        {
            Id = id,
            Hq = hq,
            Samples = samplesCopy,
            TotalLengthMeters = totalMeters,
            BridgeLengthMeters = bridgeMeters,
            CostPaid = costPaid,
            BuiltLengthMeters = 0f,
            LastRebuiltSamples = 0,
            NextDispatchAt = 0f,
        };

        if (!AddonConfig.RequirePhysicalConstruction.Value)
        {
            FinalizeProject(project);
            return $"Road #{id} completed immediately ({totalMeters:0} m).";
        }

        project.BlueprintRoot = RoadMeshBuilder.BuildRoadObject(
            $"GCE_RoadBlueprint_{id}",
            project.Samples,
            project.Samples.Count,
            isBlueprint: true,
            addColliders: false);

        activeProjects.Add(project);
        DispatchNearestEngineers(project, GetWorkHeadPosition(project));
        GcreNetworkSync.BroadcastRoadProgress(project.Id, project.Hq, project.TotalLengthMeters, 0f, cancelled: false, project.Samples);

        int dispatched = project.AssignedBuilders.Count;
        string msg = dispatched > 0
            ? $"Road #{id} blueprint placed ({project.TotalLengthMeters:0} m). {dispatched} M12 Jackknife(s) en route."
            : $"Road #{id} blueprint placed ({project.TotalLengthMeters:0} m). Move an M12 Jackknife near the road start!";
        AddonLog.Info($"[Roads] {msg}");
        return msg;
    }

    private void TickProjects(float now, float dt)
    {
        for (int i = activeProjects.Count - 1; i >= 0; i--)
        {
            RoadConstructionProject project = activeProjects[i];
            if (project.Hq == null || project.Samples.Count < 2)
            {
                RoadMeshBuilder.DestroyRoadObject(project.BlueprintRoot);
                project.BlueprintRoot = null;
                RoadMeshBuilder.DestroyRoadObject(project.BuiltRoot);
                project.BuiltRoot = null;
                activeProjects.RemoveAt(i);
                continue;
            }

            if (SingleplayerCheatService.IsInstantBuildActive && ReferenceEquals(project.Hq, GroundControlReflection.GetLocalHq()))
            {
                activeProjects.RemoveAt(i);
                FinalizeProject(project);
                continue;
            }

            RoadMeshBuilder.AttachToDatumOrigin(project.BlueprintRoot!);
            if (project.BuiltRoot != null)
            {
                RoadMeshBuilder.AttachToDatumOrigin(project.BuiltRoot);
            }

            GlobalPosition workHead = GetWorkHeadPosition(project);
            int workingEngineers = CountWorkingEngineersNear(project.Hq, workHead);

            if (now >= project.NextDispatchAt)
            {
                project.NextDispatchAt = now + 4.0f;
                PruneInvalidBuilders(project);
                if (project.AssignedBuilders.Count < 2)
                {
                    DispatchNearestEngineers(project, workHead);
                }
                else
                {
                    for (int b = 0; b < project.AssignedBuilders.Count; b++)
                    {
                        Unit eng = project.AssignedBuilders[b];
                        if (FastMath.Distance(eng.GlobalPosition(), workHead) > BuilderWorkRadiusMeters * 0.65f)
                        {
                            GroundControlReflection.TrySetDestination(eng, workHead);
                        }
                    }
                }
            }

            if (workingEngineers > 0)
            {
                int curSampleIdx = GetWorkHeadSampleIndex(project);
                bool onBridge = project.Samples[curSampleIdx].IsBridge;
                float rawRate = (onBridge ? BridgeBuildMetersPerSecondPerBuilder : LandBuildMetersPerSecondPerBuilder) * Mathf.Min(3, workingEngineers);
                float cappedRate = Mathf.Min(rawRate, project.TotalLengthMeters / MinimumBuildDurationSeconds);
                project.BuiltLengthMeters = Mathf.Min(project.TotalLengthMeters, project.BuiltLengthMeters + cappedRate * dt);

                int builtSampleCount = Mathf.Clamp(curSampleIdx + 1, 2, project.Samples.Count);
                if (builtSampleCount >= project.LastRebuiltSamples + 2)
                {
                    RoadMeshBuilder.DestroyRoadObject(project.BuiltRoot);

                    project.BuiltRoot = RoadMeshBuilder.BuildRoadObject(
                        $"GCE_RoadPartial_{project.Id}",
                        project.Samples,
                        builtSampleCount,
                        isBlueprint: false,
                        addColliders: false);
                    project.LastRebuiltSamples = builtSampleCount;
                    GcreNetworkSync.BroadcastRoadProgress(
                        project.Id,
                        project.Hq,
                        project.TotalLengthMeters,
                        project.BuiltLengthMeters,
                        cancelled: false,
                        samples: null);
                }
            }

            if (project.BuiltLengthMeters >= project.TotalLengthMeters - 0.1f)
            {
                activeProjects.RemoveAt(i);
                FinalizeProject(project);
            }
        }
    }

    private void CancelProjectAndRefund(int index)
    {
        if (index < 0 || index >= activeProjects.Count)
        {
            return;
        }

        RoadConstructionProject project = activeProjects[index];
        activeProjects.RemoveAt(index);

        RoadMeshBuilder.DestroyRoadObject(project.BlueprintRoot);
        project.BlueprintRoot = null;
        RoadMeshBuilder.DestroyRoadObject(project.BuiltRoot);
        project.BuiltRoot = null;

        for (int i = 0; i < project.AssignedBuilders.Count; i++)
        {
            if (project.AssignedBuilders[i] != null && !project.AssignedBuilders[i].disabled)
            {
                GroundControlReflection.SendRepairUnitHome(project.AssignedBuilders[i]);
            }
        }

        float pct = project.TotalLengthMeters > 0.1f ? Mathf.Clamp01(project.BuiltLengthMeters / project.TotalLengthMeters) : 0f;
        float refund = Mathf.Round(project.CostPaid * (1f - pct * 0.5f));
        if (project.Hq != null && ((NetworkBehaviour)project.Hq).IsServer && refund > 0f)
        {
            project.Hq.AddFunds(refund);
        }

        GcreNetworkSync.BroadcastRoadProgress(
            project.Id,
            project.Hq,
            project.TotalLengthMeters,
            project.BuiltLengthMeters,
            cancelled: true,
            samples: null);

        AddonLog.Info($"[Roads] Road #{project.Id} cancelled; refunded {refund:0} Cr.");
    }

    private void FinalizeProject(RoadConstructionProject project)
    {
        RoadMeshBuilder.DestroyRoadObject(project.BlueprintRoot);
        project.BlueprintRoot = null;
        RoadMeshBuilder.DestroyRoadObject(project.BuiltRoot);
        project.BuiltRoot = null;

        if (appliedCompletedRoadIds.Add(project.Id))
        {
            completedRoads.Add((project.Id, new List<RoadSample>(project.Samples)));
        }

        GameObject finishedObj = RoadMeshBuilder.BuildRoadObject(
            $"GCE_RoadComplete_{project.Id}",
            project.Samples,
            project.Samples.Count,
            isBlueprint: false,
            addColliders: true);
        completedRoadObjects.Add(finishedObj);

        for (int i = 0; i < project.AssignedBuilders.Count; i++)
        {
            Unit eng = project.AssignedBuilders[i];
            if (eng != null && !eng.disabled)
            {
                GroundControlReflection.SendRepairUnitHome(eng);
            }
        }

        int injectedSegments = InjectIntoRoadNetwork(project);
        project.Completed = true;

        GcreNetworkSync.BroadcastRoadCompleted(project.Id, project.Samples);

        string summary = $"Road #{project.Id} completed ({project.TotalLengthMeters:0} m, {project.BridgeLengthMeters:0} m bridge) · {injectedSegments} nav segment(s) linked!";
        AddonLog.Info($"[Roads] {summary}");

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        if (localHq != null && ReferenceEquals(localHq, project.Hq))
        {
            GroundControlReflection.NoticeFail(summary);
        }

        GcreNetworkSync.BroadcastFactionToast(project.Hq, isAlert: false, summary);
    }

    internal void ApplyClientCompletedRoad(int id, List<RoadSample> samples)
    {
        if (!GcreNetworkSync.IsPureClient || samples == null || samples.Count < 2)
        {
            return;
        }

        for (int i = activeProjects.Count - 1; i >= 0; i--)
        {
            if (activeProjects[i].Id == id)
            {
                RoadConstructionProject p = activeProjects[i];
                RoadMeshBuilder.DestroyRoadObject(p.BlueprintRoot);
                p.BlueprintRoot = null;
                RoadMeshBuilder.DestroyRoadObject(p.BuiltRoot);
                p.BuiltRoot = null;
                activeProjects.RemoveAt(i);
            }
        }

        if (!appliedCompletedRoadIds.Add(id))
        {
            return;
        }

        completedRoads.Add((id, samples));
        GameObject finishedObj = RoadMeshBuilder.BuildRoadObject(
            $"GCE_RoadComplete_{id}",
            samples,
            samples.Count,
            isBlueprint: false,
            addColliders: true);
        completedRoadObjects.Add(finishedObj);

        var tempProject = new RoadConstructionProject
        {
            Id = id,
            Samples = samples,
        };
        InjectIntoRoadNetwork(tempProject);
    }

    internal void ApplyClientRoadProgress(
        int id,
        string hqName,
        float totalLen,
        float builtLen,
        bool cancelled,
        List<RoadSample>? samples)
    {
        if (!GcreNetworkSync.IsPureClient || !IsFinite(totalLen) || !IsFinite(builtLen) || totalLen < 0f || builtLen < 0f)
        {
            return;
        }

        int existingIdx = -1;
        for (int i = 0; i < activeProjects.Count; i++)
        {
            if (activeProjects[i].Id == id)
            {
                existingIdx = i;
                break;
            }
        }

        if (cancelled || appliedCompletedRoadIds.Contains(id))
        {
            if (existingIdx >= 0)
            {
                RoadConstructionProject p = activeProjects[existingIdx];
                RoadMeshBuilder.DestroyRoadObject(p.BlueprintRoot);
                p.BlueprintRoot = null;
                RoadMeshBuilder.DestroyRoadObject(p.BuiltRoot);
                p.BuiltRoot = null;
                activeProjects.RemoveAt(existingIdx);
            }

            return;
        }

        RoadConstructionProject project;
        if (existingIdx >= 0)
        {
            project = activeProjects[existingIdx];
        }
        else
        {
            if (samples == null || samples.Count < 2)
            {
                return;
            }

            project = new RoadConstructionProject
            {
                Id = id,
                Hq = GcreNetworkSync.FindHqByName(hqName)!,
                Samples = samples,
                TotalLengthMeters = totalLen,
                BuiltLengthMeters = builtLen,
                LastRebuiltSamples = 0,
            };
            project.BlueprintRoot = RoadMeshBuilder.BuildRoadObject(
                $"GCE_RoadBlueprint_{id}",
                project.Samples,
                project.Samples.Count,
                isBlueprint: true,
                addColliders: false);
            activeProjects.Add(project);
        }

        if (project.Hq == null && !string.IsNullOrEmpty(hqName))
        {
            project.Hq = GcreNetworkSync.FindHqByName(hqName)!;
        }

        project.TotalLengthMeters = totalLen;
        project.BuiltLengthMeters = builtLen;

        if (project.Samples.Count >= 2 && project.BuiltLengthMeters > 0.1f)
        {
            int curSampleIdx = GetWorkHeadSampleIndex(project);
            int builtSampleCount = Mathf.Clamp(curSampleIdx + 1, 2, project.Samples.Count);
            if (builtSampleCount >= project.LastRebuiltSamples + 2)
            {
                RoadMeshBuilder.DestroyRoadObject(project.BuiltRoot);

                project.BuiltRoot = RoadMeshBuilder.BuildRoadObject(
                    $"GCE_RoadPartial_{project.Id}",
                    project.Samples,
                    builtSampleCount,
                    isBlueprint: false,
                    addColliders: false);
                project.LastRebuiltSamples = builtSampleCount;
            }
        }
    }

    private static int InjectIntoRoadNetwork(RoadConstructionProject project)
    {
        LevelInfo? level = NetworkSceneSingleton<LevelInfo>.i;
        RoadNetwork? net = level != null ? level.roadNetwork : null;
        if (net == null || project.Samples.Count < 2)
        {
            AddonLog.Warn("[Roads] LevelInfo.roadNetwork unavailable — road mesh built without pathfinding graph update.");
            return 0;
        }

        var working = new List<RoadSample>(project.Samples);
        var forcedJunctions = new HashSet<int>();

        if (TryConnectAndSplitAtSample(net, working, 0, WaypointSnapRadiusMeters))
        {
            forcedJunctions.Add(0);
        }

        int lastIdx = working.Count - 1;
        if (lastIdx > 0 && TryConnectAndSplitAtSample(net, working, lastIdx, WaypointSnapRadiusMeters))
        {
            forcedJunctions.Add(lastIdx);
        }

        GlobalPosition lastCrossingAnchor = working[0].Pos;
        for (int s = 1; s < lastIdx; s++)
        {
            if (FlatDist(working[s].Pos, lastCrossingAnchor) < 30f
                || FlatDist(working[s].Pos, working[lastIdx].Pos) < 30f)
            {
                continue;
            }

            if (TryConnectAndSplitAtSample(net, working, s, 10f))
            {
                forcedJunctions.Add(s);
                lastCrossingAnchor = working[s].Pos;
            }
        }

        int addedRuns = 0;
        int i = 0;
        while (i < working.Count - 1)
        {
            bool runBridge = working[i].IsBridge || working[i + 1].IsBridge;
            var road = new Road();
            road.SetBridge(runBridge);
            road.AddPoint(working[i].Pos);

            float chunkMeters = 0f;
            int j = i + 1;
            while (j < working.Count)
            {
                bool segIsBridge = working[j - 1].IsBridge || working[j].IsBridge;
                if (segIsBridge != runBridge && j > i + 1)
                {
                    break;
                }

                float segDist = FastMath.Distance(working[j - 1].Pos, working[j].Pos);
                if (segDist >= 0.5f || j == working.Count - 1)
                {
                    road.AddPoint(working[j].Pos);
                    chunkMeters += segDist;
                }

                j++;

                int boundaryIdx = j - 1;
                if (boundaryIdx < working.Count - 1)
                {
                    if ((working[boundaryIdx].IsBridge || working[j].IsBridge) != runBridge)
                    {
                        break;
                    }

                    if (chunkMeters >= MaxChunkMeters || forcedJunctions.Contains(boundaryIdx))
                    {
                        break;
                    }
                }
            }

            road.CalcLength();
            road.UpdateBB();
            if (road.points.Count >= 2 && road.length >= 1.0f && FlatDist(road.points[0], road.points[road.points.Count - 1]) >= 1.0f)
            {
                net.roads.Add(road);
                addedRuns++;
            }

            i = Mathf.Max(i + 1, j - 1);
        }

        if (addedRuns > 0)
        {
            net.RegenerateNetwork();
            Physics.SyncTransforms();
            RepathfindActiveGroundVehicles(net);
            GroundControlReflection.InvalidateTerrainRouteCache();

            if (project.BridgeLengthMeters > 0.5f || HasBridgeSample(project.Samples))
            {
                HasAnyCompletedBridge = true;
                InvalidateIslandCaches();
            }

            AddonLog.Info($"[Roads] RoadNetwork regenerated: {net.roads.Count} roads, {net.nodes.Count} nodes.");
        }

        return addedRuns;
    }

    private static bool HasBridgeSample(IReadOnlyList<RoadSample> samples)
    {
        for (int i = 0; i < samples.Count; i++)
        {
            if (samples[i].IsBridge)
            {
                return true;
            }
        }

        return false;
    }

    private static void InvalidateIslandCaches()
    {
        if (EnemyIslandBasesField?.GetValue(null) is IDictionary islandBases)
        {
            islandBases.Clear();
        }

        if (EnemyIslandDepotsField?.GetValue(null) is IDictionary islandDepots)
        {
            islandDepots.Clear();
        }

        PioneerBridgeDriveCachePatch.ClearCache();
    }

    private static void RepathfindActiveGroundVehicles(RoadNetwork net)
    {
        for (int i = 0; i < UnitRegistry.allUnits.Count; i++)
        {
            if (UnitRegistry.allUnits[i] is not GroundVehicle gv || gv == null || gv.disabled || !gv.IsServer)
            {
                continue;
            }

            PathfindingAgent? agent = VehiclePathfinderRef(gv);
            if (agent == null || agent.IsOnBridge())
            {
                continue;
            }

            List<Node> nodes = AgentNodesRef(agent);
            List<GlobalPosition> waypoints = AgentWaypointsRef(agent);
            if (nodes.Count == 0 && waypoints.Count == 0)
            {
                continue;
            }

            GlobalPosition dest = gv.GetDestination();
            agent.ClearDestination();
            AgentCurrentRoadRef(agent) = null;

            if (FastMath.Distance(gv.GlobalPosition(), dest) > 15f)
            {
                agent.Pathfind(net, dest, null);
            }
        }
    }

    private static bool TryConnectAndSplitAtSample(
        RoadNetwork net,
        List<RoadSample> working,
        int sampleIndex,
        float maxDistMeters)
    {
        RoadSample sample = working[sampleIndex];
        GlobalPosition pt = sample.Pos;
        if (!TryConnectAndSplitExistingRoad(net, ref pt, maxDistMeters))
        {
            return false;
        }

        sample.Pos = pt;
        working[sampleIndex] = sample;
        return true;
    }

    private static bool TryConnectAndSplitExistingRoad(RoadNetwork net, ref GlobalPosition point, float maxDistMeters)
    {
        if (!TryFindClosestPointOnRoadNetwork(net, point, maxDistMeters, out GlobalPosition projected, out Road? existing, out int segIdx)
            || existing == null
            || existing.points == null
            || existing.points.Count < 2)
        {
            return false;
        }

        GlobalPosition startPt = existing.points[0];
        GlobalPosition endPt = existing.points[existing.points.Count - 1];
        if (FlatDist(projected, startPt) <= 8f)
        {
            point = startPt;
            return true;
        }

        if (FlatDist(projected, endPt) <= 8f)
        {
            point = endPt;
            return true;
        }

        GlobalPosition splitAnchor = projected;
        int tailStartNextIdx = segIdx + 1;
        int headLastIdx = segIdx;
        bool appendAnchorToHead = true;

        if (segIdx > 0 && FlatDist(projected, existing.points[segIdx]) <= 2f)
        {
            splitAnchor = existing.points[segIdx];
            appendAnchorToHead = false;
        }
        else if (segIdx + 1 < existing.points.Count - 1 && FlatDist(projected, existing.points[segIdx + 1]) <= 2f)
        {
            splitAnchor = existing.points[segIdx + 1];
            headLastIdx = segIdx + 1;
            tailStartNextIdx = segIdx + 2;
            appendAnchorToHead = false;
        }

        var headPoints = new List<GlobalPosition>(headLastIdx + 2);
        for (int p = 0; p <= headLastIdx; p++)
        {
            headPoints.Add(existing.points[p]);
        }

        if (appendAnchorToHead)
        {
            headPoints.Add(splitAnchor);
        }

        var tailPoints = new List<GlobalPosition>(existing.points.Count - tailStartNextIdx + 1)
        {
            splitAnchor,
        };
        for (int p = tailStartNextIdx; p < existing.points.Count; p++)
        {
            tailPoints.Add(existing.points[p]);
        }

        if (headPoints.Count < 2 || tailPoints.Count < 2)
        {
            point = FlatDist(projected, startPt) <= FlatDist(projected, endPt) ? startPt : endPt;
            return true;
        }

        existing.points.Clear();
        existing.points.AddRange(headPoints);
        existing.CalcLength();
        existing.UpdateBB();

        var tail = new Road();
        tail.SetBridge(existing.IsBridge());
        for (int p = 0; p < tailPoints.Count; p++)
        {
            tail.AddPoint(tailPoints[p]);
        }

        tail.CalcLength();
        tail.UpdateBB();
        net.roads.Add(tail);

        point = splitAnchor;
        return true;
    }

    private static bool TryFindClosestPointOnRoadNetwork(
        RoadNetwork? net,
        GlobalPosition queryPos,
        float maxDistanceMeters,
        out GlobalPosition snappedPos,
        out Road? matchedRoad,
        out int segmentIndex)
    {
        snappedPos = queryPos;
        matchedRoad = null;
        segmentIndex = -1;

        if (net == null || net.roads == null || net.roads.Count == 0)
        {
            return false;
        }

        float bestDistSq = maxDistanceMeters * maxDistanceMeters;
        float qx = queryPos.x;
        float qz = queryPos.z;

        for (int r = 0; r < net.roads.Count; r++)
        {
            Road? road = net.roads[r];
            if (road == null || road.points == null || road.points.Count < 2)
            {
                continue;
            }

            for (int k = 0; k < road.points.Count - 1; k++)
            {
                GlobalPosition a = road.points[k];
                GlobalPosition b = road.points[k + 1];

                float dx = b.x - a.x;
                float dz = b.z - a.z;
                float segLenSq = (dx * dx) + (dz * dz);
                float t = segLenSq > 0.0001f
                    ? Mathf.Clamp01((((qx - a.x) * dx) + ((qz - a.z) * dz)) / segLenSq)
                    : 0f;

                float px = a.x + (dx * t);
                float pz = a.z + (dz * t);
                float dSq = ((qx - px) * (qx - px)) + ((qz - pz) * (qz - pz));
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    float py = Mathf.Lerp(a.y, b.y, t);
                    snappedPos = new GlobalPosition(px, py, pz);
                    matchedRoad = road;
                    segmentIndex = k;
                }
            }
        }

        return matchedRoad != null;
    }

    private static float FlatDist(GlobalPosition a, GlobalPosition b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt((dx * dx) + (dz * dz));
    }

    private void DispatchNearestEngineers(RoadConstructionProject project, GlobalPosition target)
    {
        PruneInvalidBuilders(project);
        if (project.AssignedBuilders.Count >= 2)
        {
            return;
        }

        builderCandidateScratch.Clear();
        for (int i = 0; i < UnitRegistry.allUnits.Count; i++)
        {
            Unit? u = UnitRegistry.allUnits[i];
            if (u == null
                || u.disabled
                || !ReferenceEquals(u.NetworkHQ, project.Hq)
                || !(u is GroundVehicle)
                || !GroundControlReflection.IsRepairUnit(u)
                || project.AssignedBuilders.Contains(u))
            {
                continue;
            }

            if (FastMath.Distance(u.GlobalPosition(), target) <= BuilderAutoAssignRangeMeters)
            {
                builderCandidateScratch.Add(u);
            }
        }

        builderCandidateScratch.Sort((left, right) =>
            FastMath.SquareDistance(left.GlobalPosition(), target).CompareTo(
                FastMath.SquareDistance(right.GlobalPosition(), target)));

        for (int i = 0; i < builderCandidateScratch.Count && project.AssignedBuilders.Count < 2; i++)
        {
            Unit chosen = builderCandidateScratch[i];
            if (GroundControlReflection.TrySetDestination(chosen, target))
            {
                project.AssignedBuilders.Add(chosen);
            }
        }
    }

    private static int CountWorkingEngineersNear(FactionHQ hq, GlobalPosition target)
    {
        float radiusSq = BuilderWorkRadiusMeters * BuilderWorkRadiusMeters;
        int count = 0;
        for (int i = 0; i < UnitRegistry.allUnits.Count; i++)
        {
            Unit? u = UnitRegistry.allUnits[i];
            if (u != null
                && !u.disabled
                && ReferenceEquals(u.NetworkHQ, hq)
                && u is GroundVehicle
                && GroundControlReflection.IsRepairUnit(u)
                && FastMath.SquareDistance(u.GlobalPosition(), target) <= radiusSq)
            {
                count++;
            }
        }

        return count;
    }

    private static void PruneInvalidBuilders(RoadConstructionProject project)
    {
        for (int i = project.AssignedBuilders.Count - 1; i >= 0; i--)
        {
            Unit u = project.AssignedBuilders[i];
            if (u == null || u.disabled || !ReferenceEquals(u.NetworkHQ, project.Hq))
            {
                project.AssignedBuilders.RemoveAt(i);
            }
        }
    }

    private static int GetWorkHeadSampleIndex(RoadConstructionProject project)
    {
        if (project.TotalLengthMeters <= 0.1f || project.Samples.Count < 2)
        {
            return 0;
        }

        float pct = Mathf.Clamp01(project.BuiltLengthMeters / project.TotalLengthMeters);
        return Mathf.Clamp(Mathf.FloorToInt(pct * (project.Samples.Count - 1)), 0, project.Samples.Count - 1);
    }

    private static GlobalPosition GetWorkHeadPosition(RoadConstructionProject project)
    {
        return project.Samples[GetWorkHeadSampleIndex(project)].Pos;
    }

    private static GlobalPosition TrySnapEndpointToRoadNetwork(GlobalPosition raw, out bool snapped)
    {
        RoadNetwork? net = NetworkSceneSingleton<LevelInfo>.i?.roadNetwork;
        if (TryFindClosestPointOnRoadNetwork(net, raw, WaypointSnapRadiusMeters, out GlobalPosition nearest, out _, out _))
        {
            snapped = true;
            return nearest;
        }

        snapped = false;
        return raw;
    }

    private static bool TrySampleWorldPoint(Vector2 screenPosition, out GlobalPosition worldPos, out bool overWater)
    {
        overWater = false;
        if (!GroundControlReflection.TryRaycastWorldPosition(screenPosition, out worldPos))
        {
            return false;
        }

        worldPos = SampleGroundHeight(worldPos, out overWater);
        return true;
    }

    private static GlobalPosition SampleGroundHeight(GlobalPosition xzPos, out bool underwater)
    {
        Vector3 localProbe = xzPos.ToLocalPosition();
        Vector3 rayOrigin = new(localProbe.x, Datum.LocalSeaY + 2200f, localProbe.z);
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 3500f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
        {
            GlobalPosition hitGlobal = hit.point.ToGlobalPosition();
            underwater = hitGlobal.y <= Datum.SeaLevel.y + 0.35f;
            return hitGlobal;
        }

        GlobalPosition snapped = GroundControlReflection.SnapToTerrain(xzPos);
        underwater = snapped.y <= Datum.SeaLevel.y + 0.35f;
        return snapped;
    }

    private void KeepObjectsAttachedToDatum()
    {
        if (previewLine != null)
        {
            RoadMeshBuilder.AttachToDatumOrigin(previewLine.gameObject);
        }

        for (int i = 0; i < completedRoadObjects.Count; i++)
        {
            if (completedRoadObjects[i] != null)
            {
                RoadMeshBuilder.AttachToDatumOrigin(completedRoadObjects[i]);
            }
        }
    }

    private void EnsurePreviewLine()
    {
        if (previewLine != null)
        {
            return;
        }

        var go = new GameObject("GCE_RoadPreviewLine");
        RoadMeshBuilder.AttachToDatumOrigin(go);
        previewLine = go.AddComponent<LineRenderer>();
        previewLine.useWorldSpace = false;
        previewLine.startWidth = 3.2f;
        previewLine.endWidth = 3.2f;
        previewLine.numCapVertices = 2;

        Shader? shader = Shader.Find("Sprites/Default") ?? UrpMaterialFactory.ResolveLitShader();
        if (shader != null)
        {
            previewLineMat = new Material(shader)
            {
                hideFlags = HideFlags.DontUnloadUnusedAsset,
            };
            previewLine.sharedMaterial = previewLineMat;
        }
    }

    private void DestroyPreviewLine()
    {
        if (previewLine != null)
        {
            UnityEngine.Object.Destroy(previewLine.gameObject);
            previewLine = null;
        }

        if (previewLineMat != null)
        {
            UnityEngine.Object.Destroy(previewLineMat);
            previewLineMat = null;
        }
    }

    private void SetFeedback(string message, float duration)
    {
        plannerFeedback = message;
        plannerFeedbackUntil = Time.unscaledTime + duration;
    }

    private void DrawPlannerWindow()
    {
        float w = plannerRect.width;
        float innerW = w - 24f;
        if (GUI.Button(new Rect(w - 34f, 3f, 26f, 22f), "X", NativeThemeBridge.ChromeButton))
        {
            CancelPlanning();
            return;
        }

        float y = 36f;
        GUI.Box(new Rect(12f, y, innerW, 56f), string.Empty, NativeThemeBridge.Panel);
        GUI.Label(
            new Rect(20f, y + 6f, innerW - 16f, 20f),
            $"WAYPOINTS {controlWaypoints.Count}   |   LENGTH {plannedTotalMeters:0} m   |   BRIDGE {plannedBridgeMeters:0} m",
            NativeThemeBridge.Header);
        GUI.Label(
            new Rect(20f, y + 28f, innerW - 16f, 20f),
            $"Max Grade: {plannedMaxLandGradePercent:0.#}% (Limit {GcreNetworkSync.EffectiveMaxRoadGradePercent:0}%)   |   Cost: ${plannedCost:0}m",
            NativeThemeBridge.Caption);
        y += 64f;

        forceNextSegmentBridge = NativeThemeBridge.DrawCheck(
            new Rect(12f, y, innerW, 24f),
            forceNextSegmentBridge,
            "Force next segment as Bridge Span (ravine / elevated)");
        y += 30f;

        bool canCommit = CanCommitPlannedRoad(out string blockReason);
        string statusLine;
        GUIStyle statusStyle;
        if (!canCommit && controlWaypoints.Count >= 2)
        {
            statusLine = blockReason;
            statusStyle = NativeThemeBridge.WarningLabel;
        }
        else if (!string.IsNullOrEmpty(plannerFeedback) && Time.unscaledTime < plannerFeedbackUntil)
        {
            statusLine = plannedCursorSnapped ? plannerFeedback + " [SNAPPED TO ROAD]" : plannerFeedback;
            statusStyle = NativeThemeBridge.Caption;
        }
        else
        {
            statusLine = plannedCursorSnapped
                ? "LMB click terrain to place waypoints [SNAPPED TO ROAD]."
                : "LMB click terrain to place waypoints (snaps within 35m to roads).";
            statusStyle = NativeThemeBridge.MutedLabel;
        }

        GUI.Label(new Rect(12f, y, innerW, 38f), statusLine, statusStyle);
        y += 44f;

        float btnW = (innerW - 12f) / 3f;
        GUI.enabled = controlWaypoints.Count > 0;
        if (GUI.Button(new Rect(12f, y, btnW, 30f), "UNDO (RMB)", NativeThemeBridge.Button))
        {
            UndoOrExit();
        }

        GUI.enabled = canCommit;
        if (GUI.Button(new Rect(12f + btnW + 6f, y, btnW, 30f), $"COMMIT (${plannedCost:0}m)", canCommit ? NativeThemeBridge.PrimaryButton : NativeThemeBridge.Button))
        {
            TryCommitPlannedRoad();
        }

        GUI.enabled = true;
        if (GUI.Button(new Rect(12f + (btnW + 6f) * 2f, y, btnW, 30f), "CANCEL (ESC)", NativeThemeBridge.DangerButton))
        {
            CancelPlanning();
        }

        GUI.DragWindow(new Rect(0f, 0f, w - 40f, 28f));
    }

    private void DrawActiveProjectsWindow()
    {
        float w = progressRect.width;
        float innerW = w - 24f;
        float y = 34f;
        FactionHQ? localHq = GroundControlReflection.GetLocalHq();

        for (int i = 0; i < activeProjects.Count; i++)
        {
            RoadConstructionProject p = activeProjects[i];
            if (!ShouldShowProjectInHud(p, localHq))
            {
                continue;
            }

            float frac = p.TotalLengthMeters > 0.1f ? Mathf.Clamp01(p.BuiltLengthMeters / p.TotalLengthMeters) : 0f;
            int working = p.Hq != null ? CountWorkingEngineersNear(p.Hq, GetWorkHeadPosition(p)) : 0;

            GUI.Box(new Rect(12f, y, innerW, 46f), string.Empty, NativeThemeBridge.Inset);
            GUI.Label(
                new Rect(18f, y + 4f, innerW - 92f, 20f),
                $"Road #{p.Id}: {frac * 100f:0}% ({p.TotalLengthMeters:0}m) · {working} M12",
                NativeThemeBridge.Label);
            NativeThemeBridge.DrawBar(
                new Rect(18f, y + 28f, innerW - 92f, 10f),
                frac,
                NativeThemeBridge.Accent);

            if (GUI.Button(new Rect(w - 86f, y + 9f, 68f, 28f), "CANCEL", NativeThemeBridge.DangerButton))
            {
                if (GcreNetworkSync.IsPureClient)
                {
                    GcreNetworkSync.RequestCancelRoad(p.Id);
                }
                else
                {
                    CancelProjectAndRefund(i);
                }

                break;
            }

            y += 52f;
        }

        GUI.DragWindow(new Rect(0f, 0f, w - 28f, 28f));
    }
}

[HarmonyPatch]
internal static class RoadInputIsPlacementArmedPatch
{
    private static bool Prepare() => GroundControlReflection.IsPlacementArmedMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.IsPlacementArmedMethod!;

    private static void Postfix(ref bool __result)
    {
        if (PioneerRoadService.Instance.IsPlanningMode)
        {
            __result = true;
        }
    }
}

[HarmonyPatch]
internal static class RoadInputHandleClickPatch
{
    private static bool Prepare() => GroundControlReflection.TryHandlePlacementClickMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.TryHandlePlacementClickMethod!;

    private static bool Prefix(Vector2 mousePosition, bool overMap, ref bool __result)
    {
        if (!PioneerRoadService.Instance.IsPlanningMode)
        {
            return true;
        }

        bool primaryDown = Input.GetMouseButtonDown(0);
        bool secondaryDown = Input.GetMouseButtonDown(1);
        PioneerRoadService.Instance.HandleWorldClick(mousePosition, overMap, primaryDown, secondaryDown);

        // Consume world interaction so GroundControl doesn't forward clicks to depot rally / selection during road planning.
        __result = !NativeThemeBridge.IsAnyAddonUiUnderScreenPoint(mousePosition);
        return false;
    }
}

[HarmonyPatch(typeof(RoadPathfinder), nameof(RoadPathfinder.TryPathfind))]
internal static class RoadPathfinderNoConnectionPatch
{
    private static void Postfix(
        RoadNetwork? network,
        GlobalPosition startPos,
        GlobalPosition targetPos,
        List<Node>? results,
        ref RoadPathfinder.PathfindResult result)
    {
        if (!AddonConfig.ModEnabled.Value
            || result != RoadPathfinder.PathfindResult.NoConnection
            || network == null
            || ReferenceEquals(network, NetworkSceneSingleton<LevelInfo>.i?.seaLanes)
            || network.nodes == null
            || network.nodes.Count == 0
            || results == null)
        {
            return;
        }

        Node? endNode = null;
        if (network.TryGetNearestRoad(targetPos, out Road? targetRoad) && targetRoad != null)
        {
            Node? sn = targetRoad.startNode;
            Node? en = targetRoad.endNode;
            if (sn != null && en != null)
            {
                endNode = FastMath.SquareDistance(targetPos, sn.position) <= FastMath.SquareDistance(targetPos, en.position)
                    ? sn
                    : en;
            }
            else
            {
                endNode = sn ?? en;
            }
        }

        if (endNode == null)
        {
            RoadPathfinder.TryFindNearestNode(network, targetPos, out endNode);
        }

        if (endNode == null
            || endNode.connectionsLookup == null
            || endNode.connectionsLookup.Count == 0
            || SegmentCrossesWater(endNode.position, targetPos))
        {
            results.Clear();
            return;
        }

        var distToEnd = new Dictionary<Node, float>(64)
        {
            [endNode] = 0f,
        };
        var nextTowardEnd = new Dictionary<Node, Node?>(64);
        var open = new List<Node>(64) { endNode };
        var visited = new HashSet<Node>();

        while (open.Count > 0)
        {
            int bestIdx = 0;
            float bestD = distToEnd[open[0]];
            for (int i = 1; i < open.Count; i++)
            {
                float d = distToEnd[open[i]];
                if (d < bestD)
                {
                    bestD = d;
                    bestIdx = i;
                }
            }

            Node cur = open[bestIdx];
            open.RemoveAt(bestIdx);
            if (!visited.Add(cur) || cur.connectionsLookup == null)
            {
                continue;
            }

            foreach (KeyValuePair<Road, Node> kv in cur.connectionsLookup)
            {
                Road? road = kv.Key;
                Node? neighbor = kv.Value;
                if (road == null || neighbor == null || visited.Contains(neighbor))
                {
                    continue;
                }

                float candDist = bestD + Mathf.Max(1f, road.length);
                if (!distToEnd.TryGetValue(neighbor, out float prevDist) || candDist < prevDist)
                {
                    distToEnd[neighbor] = candDist;
                    nextTowardEnd[neighbor] = cur;
                    open.Add(neighbor);
                }
            }
        }

        float directDist = FastMath.Distance(startPos, targetPos);
        Node? bestEntry = null;
        float bestTotalScore = float.MaxValue;

        foreach (KeyValuePair<Node, float> kv in distToEnd)
        {
            Node cand = kv.Key;
            if (cand == endNode || cand.connectionsLookup == null || cand.connectionsLookup.Count == 0)
            {
                continue;
            }

            float startToCand = FastMath.Distance(startPos, cand.position);
            if (startToCand >= directDist * 0.85f || SegmentCrossesWater(startPos, cand.position))
            {
                continue;
            }

            float endToTarget = FastMath.Distance(endNode.position, targetPos);
            float totalLen = startToCand + kv.Value + endToTarget;
            if (totalLen < directDist * 2.2f && totalLen < bestTotalScore)
            {
                bestTotalScore = totalLen;
                bestEntry = cand;
            }
        }

        if (bestEntry != null)
        {
            results.Clear();
            Node? cursor = bestEntry;
            int guard = 0;
            while (cursor != null && guard++ < network.nodes.Count + 4)
            {
                results.Add(cursor);
                if (cursor == endNode)
                {
                    break;
                }

                nextTowardEnd.TryGetValue(cursor, out cursor);
            }

            if (results.Count >= 2 && results[results.Count - 1] == endNode)
            {
                result = RoadPathfinder.PathfindResult.Success;
                return;
            }
        }

        results.Clear();
    }

    private static bool SegmentCrossesWater(GlobalPosition a, GlobalPosition b)
    {
        const int samples = 10;
        for (int i = 1; i < samples; i++)
        {
            float t = (float)i / samples;
            GlobalPosition probe = FastMath.Lerp(a, b, t);
            GlobalPosition snapped = GroundControlReflection.SnapToTerrain(probe);
            if (GroundControlReflection.IsBelowSeaLevel(snapped))
            {
                return true;
            }
        }

        return false;
    }
}

[HarmonyPatch]
internal static class PioneerBridgeDriveCachePatch
{
    private static readonly Dictionary<((int X, int Z) From, (int X, int Z) To), bool> BridgeConnectedCache = new();
    private static readonly List<Node> ScratchNodes = new(64);

    internal static void ClearCache()
    {
        BridgeConnectedCache.Clear();
        ScratchNodes.Clear();
    }

    private static bool Prepare() => GroundControlReflection.CanDriveBetweenMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.CanDriveBetweenMethod!;

    private static void Postfix(GlobalPosition from, GlobalPosition to, ref bool __result)
    {
        if (__result || !AddonConfig.ModEnabled.Value || !PioneerRoadService.HasAnyCompletedBridge)
        {
            return;
        }

        RoadNetwork? net = NetworkSceneSingleton<LevelInfo>.i?.roadNetwork;
        if (net == null || net.nodes == null || net.nodes.Count == 0)
        {
            return;
        }

        var key = (
            (Mathf.RoundToInt(from.x / 100f), Mathf.RoundToInt(from.z / 100f)),
            (Mathf.RoundToInt(to.x / 100f), Mathf.RoundToInt(to.z / 100f)));

        if (BridgeConnectedCache.TryGetValue(key, out bool cached))
        {
            __result = cached;
            return;
        }

        ScratchNodes.Clear();
        RoadPathfinder.TryPathfind(net, from, to, ScratchNodes, out RoadPathfinder.PathfindResult pathResult);
        ScratchNodes.Clear();

        bool connected = pathResult == RoadPathfinder.PathfindResult.Success;
        if (BridgeConnectedCache.Count > 2048)
        {
            BridgeConnectedCache.Clear();
        }

        BridgeConnectedCache[key] = connected;
        if (connected)
        {
            __result = true;
        }
    }
}
