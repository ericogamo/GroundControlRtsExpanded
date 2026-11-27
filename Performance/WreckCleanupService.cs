using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal sealed class WreckCleanupService : IAddonTick, IAddonResetSession
{
    private const float ScanIntervalSeconds = 5.0f;
    private const int MaxDespawnsPerPass = 10;

    internal static WreckCleanupService Instance { get; } = new();

    private readonly Dictionary<Wreckage, float> trackedWrecks = new();
    private readonly List<Wreckage> scratchRemove = new();
    private readonly List<Wreckage> scratchDisintegrated = new();
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
        if (now < nextScanAt)
        {
            return;
        }

        nextScanAt = now + ScanIntervalSeconds;

        MissionManager? missionManager = NetworkSceneSingleton<MissionManager>.i;
        if (missionManager != null && missionManager.listWrecks != null)
        {
            for (int i = 0; i < missionManager.listWrecks.Count; i++)
            {
                Wreckage? w = missionManager.listWrecks[i];
                if (w != null && !trackedWrecks.ContainsKey(w))
                {
                    trackedWrecks[w] = now;
                }
            }
        }

        if (trackedWrecks.Count == 0)
        {
            return;
        }

        float maxAge = AddonConfig.WreckCleanupSeconds.Value;
        int despawned = 0;
        scratchRemove.Clear();
        scratchDisintegrated.Clear();

        foreach (KeyValuePair<Wreckage, float> pair in trackedWrecks)
        {
            Wreckage wreck = pair.Key;
            if (wreck == null)
            {
                scratchRemove.Add(wreck!);
                continue;
            }

            if (float.IsNaN(pair.Value) || now - pair.Value < maxAge)
            {
                continue;
            }

            if (despawned < MaxDespawnsPerPass)
            {
                wreck.Disintegrate();
                despawned++;
                PerfMetricsService.Instance.NoteWreckCleaned();
                scratchDisintegrated.Add(wreck);
            }
        }

        for (int i = 0; i < scratchRemove.Count; i++)
        {
            trackedWrecks.Remove(scratchRemove[i]);
        }

        for (int i = 0; i < scratchDisintegrated.Count; i++)
        {
            Wreckage w = scratchDisintegrated[i];
            if (w != null)
            {
                trackedWrecks[w] = float.NaN;
            }
            else if (!ReferenceEquals(w, null))
            {
                trackedWrecks.Remove(w!);
            }
        }

        scratchRemove.Clear();
        scratchDisintegrated.Clear();
    }

    public void ResetSession()
    {
        trackedWrecks.Clear();
        scratchRemove.Clear();
        scratchDisintegrated.Clear();
        nextScanAt = 0f;
    }

    internal void TrackWreck(Wreckage? wreck)
    {
        if (wreck == null)
        {
            return;
        }

        if (!trackedWrecks.ContainsKey(wreck))
        {
            trackedWrecks[wreck] = Time.time;
        }
    }
}

[HarmonyPatch(typeof(Wreckage), "Start")]
internal static class WreckageStartTrackPatch
{
    private static void Postfix(Wreckage __instance)
    {
        if (!AddonConfig.EnableAntiLag.Value || AddonConfig.WreckCleanupSeconds.Value <= 0f || __instance == null)
        {
            return;
        }

        WreckCleanupService.Instance.TrackWreck(__instance);
    }
}
