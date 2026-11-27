using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GroundControlRtsExpanded;

internal sealed class AddonRuntimeController : MonoBehaviour
{
    private readonly List<IAddonTick> tickServices = new();
    private readonly List<IAddonResetSession> resetServices = new();
    private readonly List<IAddonGui> guiServices = new();

    internal static AddonRuntimeController? Instance { get; private set; }

    internal T Register<T>(T service) where T : class
    {
        if (service is IAddonTick tick)
        {
            tickServices.Add(tick);
        }

        if (service is IAddonResetSession reset)
        {
            resetServices.Add(reset);
        }

        if (service is IAddonGui gui)
        {
            guiServices.Add(gui);
        }

        return service;
    }

    private void Awake()
    {
        Instance = this;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (AddonConfig.ReloadModelsKey.Value.IsDown())
        {
            ModelRegistry.ReloadAll();
            CustomPreviewService.ResetSession();
            DynamicCapAndTechService.Instance.RefreshAllBuildingVisuals();
        }

        for (int i = 0; i < tickServices.Count; i++)
        {
            tickServices[i].Tick();
        }
    }

    private void OnGUI()
    {
        if (!GroundControlReflection.IsCommanderModeActive()
            && !PerfMetricsService.Instance.IsOverlayVisible
            && !EconomyTrackerUiService.Instance.IsWindowVisible)
        {
            return;
        }

        NativeThemeBridge.BeginScaledUi(out bool hasScale, out Matrix4x4 prevMatrix, out GUISkin? prevSkin);
        try
        {
            for (int i = 0; i < guiServices.Count; i++)
            {
                guiServices[i].DrawGui();
            }
        }
        finally
        {
            NativeThemeBridge.EndScaledUi(hasScale, prevMatrix, prevSkin);
        }
    }

    internal void TriggerNetworkSessionReset()
    {
        ExecuteSessionReset("network disconnect/stop");
    }

    private void OnActiveSceneChanged(Scene previousScene, Scene newScene)
    {
        ExecuteSessionReset($"scene change -> {newScene.name}");
    }

    private void ExecuteSessionReset(string reason)
    {
        CustomPreviewService.ResetSession();
        DuelOppositeAirbaseStartPatch.ResetSession();
        BaseCaptureIncomeService.ResetSession();
        ReviewContextGuardPatch.ResetSession();
        SupplyHeliCargoGateHelpers.ClearScratch();
        MdsLoadoutIconCache.Clear();
        SceneComponentCache.Clear();
        TargetDetectorAdaptiveIntervalPatch.ClearCaches();
        RpcUpdateTrackingInfoScalePatch.ClearCaches();
        DetectorManagerBudgetPatches.ClearCaches();
        TurretIdleThrottlePatch.ClearCaches();
        ModelRegistry.ClearRuntimeCache();
        for (int i = 0; i < resetServices.Count; i++)
        {
            resetServices[i].ResetSession();
        }

        AddonLog.Debug($"Session reset on {reason}");
    }
}
