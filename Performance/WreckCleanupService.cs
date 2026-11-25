using System.Collections.Generic;
using HarmonyLib;
using Mirage;
using NuclearOption.Networking;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal sealed class WreckCleanupService : IAddonTick, IAddonResetSession
{
    private const float ScanIntervalSeconds = 5.0f;
    private const int MaxDespawnsPerPass = 10;

    internal static WreckCleanupService Instance { get; } = new();

    private readonly Dictionary<Unit, float> trackedWrecks = new();
    private readonly List<Unit> scratchRemove = new();
    private float nextScanAt;

    private WreckCleanupService()
    {
    }

    public void Tick()
    {
        if (!AddonConfig.EnableAntiLag.Value || AddonConfig.WreckCleanupSeconds.Value <= 0f)
        {
            return;
        }

        float now = Time.time;
        if (now < nextScanAt || trackedWrecks.Count == 0)
        {
            return;
        }

        nextScanAt = now + ScanIntervalSeconds;

        NetworkManagerNuclearOption net = NetworkManagerNuclearOption.i;
        ServerObjectManager? som = net != null ? net.ServerObjectManager : null;
        if (som == null)
        {
            return;
        }

        float maxAge = AddonConfig.WreckCleanupSeconds.Value;
        int despawned = 0;
        scratchRemove.Clear();

        foreach (KeyValuePair<Unit, float> pair in trackedWrecks)
        {
            Unit unit = pair.Key;
            if (unit == null || !unit.disabled || unit.Identity == null || unit.Identity.IsSceneObject || !unit.IsServer)
            {
                scratchRemove.Add(unit!);
                continue;
            }

            if (now - pair.Value < maxAge)
            {
                continue;
            }

            if (despawned < MaxDespawnsPerPass)
            {
                som.Destroy(unit.Identity, true);
                despawned++;
                PerfMetricsService.Instance.NoteWreckCleaned();
                scratchRemove.Add(unit);
            }
        }

        for (int i = 0; i < scratchRemove.Count; i++)
        {
            trackedWrecks.Remove(scratchRemove[i]);
        }

        scratchRemove.Clear();
    }

    public void ResetSession()
    {
        trackedWrecks.Clear();
        scratchRemove.Clear();
        nextScanAt = 0f;
    }

    internal void TrackDeadGroundVehicle(Unit unit)
    {
        if (unit is not GroundVehicle || !unit.IsServer || !unit.disabled)
        {
            return;
        }

        if (unit.Identity == null || unit.Identity.IsSceneObject)
        {
            return;
        }

        if (!trackedWrecks.ContainsKey(unit))
        {
            trackedWrecks[unit] = Time.time;
        }
    }
}

[HarmonyPatch(typeof(Unit), "ServerDisableUnit")]
internal static class UnitServerDisableWreckTrackPatch
{
    private static void Postfix(Unit __instance)
    {
        if (!AddonConfig.EnableAntiLag.Value || AddonConfig.WreckCleanupSeconds.Value <= 0f || __instance == null)
        {
            return;
        }

        WreckCleanupService.Instance.TrackDeadGroundVehicle(__instance);
    }
}
