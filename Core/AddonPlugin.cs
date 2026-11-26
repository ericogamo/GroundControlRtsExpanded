using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using BepInEx;
using HarmonyLib;

namespace GroundControlRtsExpanded;

[BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
[BepInDependency("com.groundcontrol.rts", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency(MdsBridgeBootstrap.MdsFrameworkPluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
[SuppressMessage("Usage", "CA2243:Attribute string literals should parse correctly", Justification = "BepInEx plugin GUID is reverse-DNS.")]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Unity owns plugin lifetime.")]
public sealed class AddonPlugin : BaseUnityPlugin
{
    private Harmony? harmony;

    internal static AddonPlugin? Instance { get; private set; }
    internal static string PluginDirectory { get; private set; } = string.Empty;
    internal AddonRuntimeController? RuntimeController { get; private set; }

    private void Awake()
    {
        Instance = this;
        AddonLog.Initialize(Logger);
        AddonConfig.Initialize(Config);

        if (!AddonConfig.ModEnabled.Value)
        {
            AddonLog.Info($"{PluginInfo.Name} {PluginInfo.Version} is disabled in config.");
            return;
        }

        PluginDirectory = Path.GetDirectoryName(Info.Location) ?? AppContext.BaseDirectory;

        GroundControlReflection.ValidateAllBindings();

        harmony = new Harmony(PluginInfo.Guid);
        harmony.PatchAll();

        ModelRegistry.Initialize(PluginDirectory);
        RuntimeController = gameObject.AddComponent<AddonRuntimeController>();
        RuntimeController.Register(AiStrategicDirectorService.Instance);
        RuntimeController.Register(AiCoastalOutpostService.Instance);
        RuntimeController.Register(AiBattlefieldIntelService.Instance);
        RuntimeController.Register(AiTacticalDoctrineService.Instance);
        RuntimeController.Register(AiOperationalManeuverService.Instance);
        RuntimeController.Register(AiRadarEmconService.Instance);
        RuntimeController.Register(AiSupplyLogisticsService.Instance);
        RuntimeController.Register(PerfMetricsService.Instance);
        RuntimeController.Register(WreckCleanupService.Instance);
        RuntimeController.Register(DynamicCapAndTechService.Instance);
        RuntimeController.Register(RtsCityUiService.Instance);
        RuntimeController.Register(EconomyTrackerUiService.Instance);
        RuntimeController.Register(PioneerRoadService.Instance);
        AbmDefenseService.Instance.BindMdsBridge();
        RuntimeController.Register(AbmDefenseService.Instance);
        MdsBridgeBootstrap.TryAttachIfPresent(RuntimeController);
        RuntimeController.Register(MdsFlatbedHaulService.Instance);
        RuntimeController.Register(SingleplayerCheatService.Instance);

        AddonLog.Info($"{PluginInfo.Name} {PluginInfo.Version} loaded from '{PluginDirectory}'.");
    }

    private void OnDestroy()
    {
        MdsBridgeBootstrap.DetachOnDestroy();
        harmony?.UnpatchSelf();
        harmony = null;
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }
    }
}
