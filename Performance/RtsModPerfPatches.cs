using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace GroundControlRtsExpanded;

[HarmonyPatch]
internal static class ClearTargetsUsingRadarCachePatch
{
    private static readonly AccessTools.FieldRef<FireControl, List<Turret>?> FireControlTurretsRef =
        AccessTools.FieldRefAccess<FireControl, List<Turret>?>("turrets");

    private static bool Prepare() => GroundControlReflection.ClearTargetsUsingRadarMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.ClearTargetsUsingRadarMethod!;

    private static bool Prefix(Radar radar)
    {
        if (!AddonConfig.EnableAntiLag.Value || radar == null)
        {
            return true;
        }

        FireControl[] fireControls = SceneComponentCache.GetFireControls();
        for (int i = 0; i < fireControls.Length; i++)
        {
            FireControl fc = fireControls[i];
            if (fc == null || !fc.TryGetRadar(out Radar boundRadar) || boundRadar != radar)
            {
                continue;
            }

            List<Turret>? turrets = FireControlTurretsRef(fc);
            if (turrets == null)
            {
                continue;
            }

            for (int j = 0; j < turrets.Count; j++)
            {
                Turret? t = turrets[j];
                if (t != null)
                {
                    t.SetTargetFromController(null);
                }
            }
        }

        Turret[] allTurrets = SceneComponentCache.GetTurrets();
        for (int i = 0; i < allTurrets.Length; i++)
        {
            Turret? t = allTurrets[i];
            Unit? attached = t != null ? t.GetAttachedUnit() : null;
            if (t != null && attached != null && attached.radar == radar)
            {
                t.SetTargetFromController(null);
            }
        }

        return false;
    }
}

[HarmonyPatch]
internal static class GetFactoriesCachePatch
{
    private static bool Prepare() => GroundControlReflection.GetFactoriesMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.GetFactoriesMethod!;

    private static bool Prefix(ref Factory[] __result)
    {
        if (!AddonConfig.EnableAntiLag.Value)
        {
            return true;
        }

        __result = SceneComponentCache.GetFactories();
        return false;
    }
}

[HarmonyPatch]
internal static class CommanderLogSpamPatch
{
    private const float SuppressWindowSeconds = 30f;
    private const int MaxTrackedKeys = 256;

    private static readonly Dictionary<string, float> LastLoggedAt = new();
    private static readonly StringBuilder KeyBuilder = new(128);

    internal static void ClearCaches()
    {
        LastLoggedAt.Clear();
    }

    private static bool Prepare() => GroundControlReflection.CommanderLogInfoMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.CommanderLogInfoMethod!;

    private static bool Prefix(object? data)
    {
        if (!AddonConfig.EnableAntiLag.Value || !AddonConfig.SuppressLogSpam.Value || data == null)
        {
            return true;
        }

        string msg = data.ToString() ?? string.Empty;
        if (msg.Length == 0)
        {
            return true;
        }

        string key = NormalizeKey(msg);
        float now = Time.unscaledTime;

        if (LastLoggedAt.TryGetValue(key, out float prevAt) && now - prevAt < SuppressWindowSeconds)
        {
            PerfMetricsService.Instance.NoteSuppressedLog();
            return false;
        }

        if (LastLoggedAt.Count >= MaxTrackedKeys)
        {
            LastLoggedAt.Clear();
        }

        LastLoggedAt[key] = now;
        return true;
    }

    private static string NormalizeKey(string msg)
    {
        int limit = Mathf.Min(msg.Length, 120);
        KeyBuilder.Clear();
        bool inDigits = false;

        for (int i = 0; i < limit; i++)
        {
            char c = msg[i];
            if (c >= '0' && c <= '9')
            {
                if (!inDigits)
                {
                    KeyBuilder.Append('#');
                    inDigits = true;
                }
            }
            else
            {
                inDigits = false;
                KeyBuilder.Append(c);
            }
        }

        return KeyBuilder.ToString();
    }
}
