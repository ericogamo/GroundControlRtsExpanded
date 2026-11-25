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
            DynamicCapAndTechService.Instance.RefreshAllBuildingVisuals();
        }

        for (int i = 0; i < tickServices.Count; i++)
        {
            tickServices[i].Tick();
        }
    }

    private void OnGUI()
    {
        if (!GroundControlReflection.IsCommanderModeActive() && !PerfMetricsService.Instance.IsOverlayVisible)
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

    private void OnActiveSceneChanged(Scene previousScene, Scene newScene)
    {
        CustomPreviewService.ResetSession();
        ModelRegistry.ClearRuntimeCache();
        for (int i = 0; i < resetServices.Count; i++)
        {
            resetServices[i].ResetSession();
        }

        AddonLog.Debug($"Session reset on scene change -> {newScene.name}");
    }
}
