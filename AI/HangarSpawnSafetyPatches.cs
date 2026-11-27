using System;
using HarmonyLib;
using NuclearOption.Networking;
using NuclearOption.SavedMission;

namespace GroundControlRtsExpanded;

[HarmonyPatch(typeof(Hangar), nameof(Hangar.CanSpawnAircraft), new[] { typeof(AircraftDefinition) })]
internal static class HangarCanSpawnSafetyPatch
{
    private static void Postfix(Hangar __instance, AircraftDefinition definition, ref bool __result)
    {
        if (!__result || !AddonConfig.ModEnabled.Value || !AddonConfig.EnableHangarSpawnSafety.Value)
        {
            return;
        }

        if (HangarSpawnSafetyService.Instance.IsHangarBlocked(__instance, null, definition, out _))
        {
            __result = false;
        }
    }
}

[HarmonyPatch(
    typeof(Hangar),
    nameof(Hangar.TrySpawnAircraft),
    new[] { typeof(Player), typeof(AircraftDefinition), typeof(LiveryKey), typeof(Loadout), typeof(float) })]
internal static class HangarTrySpawnSafetyPatch
{
    private static bool Prefix(
        Hangar __instance,
        Player? player,
        AircraftDefinition definition,
        ref Airbase.TrySpawnResult __result)
    {
        if (!AddonConfig.ModEnabled.Value || !AddonConfig.EnableHangarSpawnSafety.Value || !__instance.IsServer)
        {
            return true;
        }

        if (HangarSpawnSafetyService.Instance.IsHangarBlocked(__instance, player, definition, out string reason))
        {
            string who = player != null ? $"player '{player.name}'" : "AI";
            AddonLog.Debug($"[HangarSafety] Blocked {who} spawn of {definition?.unitName ?? "?"} at hangar '{__instance.name}': {reason}");
            if (AirbaseTrySpawnSafetyNoticePatch.IsInAirbaseTrySpawn)
            {
                AirbaseTrySpawnSafetyNoticePatch.RecordBlockedReason(reason);
            }
            else if (player != null)
            {
                if (player.IsLocalPlayer)
                {
                    HangarSpawnSafetyService.Instance.NotifyLocalPlayerBlocked(reason);
                }
                else
                {
                    HangarSpawnSafetyService.Instance.NotifyRemotePlayerBlocked(player, reason);
                }
            }

            __result = default;
            return false;
        }

        return true;
    }

    private static void Postfix(
        Hangar __instance,
        Player? player,
        AircraftDefinition definition,
        Airbase.TrySpawnResult __result)
    {
        if (!AddonConfig.ModEnabled.Value || !AddonConfig.EnableHangarSpawnSafety.Value || !__instance.IsServer)
        {
            return;
        }

        if (__result.Allowed && __result.DelayedSpawn)
        {
            HangarSpawnSafetyService.Instance.OnDelayedSpawnQueued(__instance, isAi: player == null, definition);
        }
    }
}

[HarmonyPatch(
    typeof(Hangar),
    "SpawnAircraft",
    new[] { typeof(Player), typeof(AircraftDefinition), typeof(Loadout), typeof(float), typeof(LiveryKey) })]
internal static class HangarSpawnAircraftSafetyPatch
{
    private static void Prefix(Hangar __instance, Player? player, AircraftDefinition definition)
    {
        if (!AddonConfig.ModEnabled.Value || !AddonConfig.EnableHangarSpawnSafety.Value || !__instance.IsServer)
        {
            return;
        }

        HangarSpawnSafetyService.Instance.OnSpawnStarting(__instance, isAi: player == null, definition);
    }

    private static void Postfix(Hangar __instance, Player? player, AircraftDefinition definition)
    {
        if (!AddonConfig.ModEnabled.Value || !AddonConfig.EnableHangarSpawnSafety.Value || !__instance.IsServer)
        {
            return;
        }

        HangarSpawnSafetyService.Instance.OnSpawnCompleted(__instance, isAi: player == null, definition);
    }
}

[HarmonyPatch(typeof(Hangar), "OnDestroy")]
internal static class HangarOnDestroySafetyPatch
{
    private static void Postfix(Hangar __instance)
    {
        HangarSpawnSafetyService.Instance.OnHangarDestroyed(__instance);
    }
}

[HarmonyPatch(
    typeof(Airbase),
    nameof(Airbase.TrySpawnAircraft),
    new[] { typeof(Player), typeof(AircraftDefinition), typeof(LiveryKey), typeof(Loadout), typeof(float) })]
internal static class AirbaseTrySpawnSafetyNoticePatch
{
    [ThreadStatic]
    private static bool inAirbaseTrySpawn;

    [ThreadStatic]
    private static string? lastAirbaseBlockedReason;

    internal static bool IsInAirbaseTrySpawn => inAirbaseTrySpawn;

    internal static void RecordBlockedReason(string reason)
    {
        if (string.IsNullOrEmpty(lastAirbaseBlockedReason) && !string.IsNullOrEmpty(reason))
        {
            lastAirbaseBlockedReason = reason;
        }
    }

    private static void Prefix()
    {
        inAirbaseTrySpawn = true;
        lastAirbaseBlockedReason = null;
    }

    private static void Postfix(
        Airbase __instance,
        Player? player,
        AircraftDefinition definition,
        Airbase.TrySpawnResult __result)
    {
        if (__result.Allowed || player == null)
        {
            return;
        }

        if (!AddonConfig.ModEnabled.Value || !AddonConfig.EnableHangarSpawnSafety.Value)
        {
            return;
        }

        bool hasReason = HangarSpawnSafetyService.Instance.HasAnySafetyBlockedCompatibleHangar(
            __instance,
            definition,
            out string reason,
            player);
        if (!hasReason && !string.IsNullOrEmpty(lastAirbaseBlockedReason))
        {
            reason = lastAirbaseBlockedReason!;
            hasReason = true;
        }

        if (!hasReason)
        {
            return;
        }

        if (player.IsLocalPlayer)
        {
            HangarSpawnSafetyService.Instance.NotifyLocalPlayerBlocked(reason);
        }
        else if (__instance.IsServer)
        {
            HangarSpawnSafetyService.Instance.NotifyRemotePlayerBlocked(player, reason);
        }
    }

    private static Exception? Finalizer(Exception? __exception)
    {
        inAirbaseTrySpawn = false;
        lastAirbaseBlockedReason = null;
        return __exception;
    }
}

[HarmonyPatch(typeof(AircraftSelectionMenu), nameof(AircraftSelectionMenu.FlyAircraft))]
internal static class AircraftSelectionMenuFlySafetyNoticePatch
{
    private static void Postfix(AircraftSelectionMenu __instance)
    {
        HangarSpawnSafetyService.Instance.HandleSelectionMenuFlyCheck(__instance);
    }
}
