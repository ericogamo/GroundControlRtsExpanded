using System.Reflection;
using HarmonyLib;

namespace GroundControlRtsExpanded;

[HarmonyPatch]
internal static class RepairHoldLeftBehindUnitsAiGuardPatch
{
    private static bool Prepare() => GroundControlReflection.RepairHoldLeftBehindUnitsMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.RepairHoldLeftBehindUnitsMethod!;

    private static bool Prefix(object __instance, FactionHQ? hq)
    {
        if (!AddonConfig.ModEnabled.Value || hq == null)
        {
            return true;
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        if (!GroundControlReflection.HasHumanPlayers(hq) && GroundControlReflection.IsSafeAiFaction(hq, localHq))
        {
            GroundControlReflection.RemoveGuestFaction(__instance, hq);
            return false;
        }

        return true;
    }
}

[HarmonyPatch]
internal static class RepairSweepStandbyAiGuardPatch
{
    private static bool Prepare() => GroundControlReflection.RepairSweepStandbyMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.RepairSweepStandbyMethod!;

    private static void Prefix(FactionHQ? hq, ref bool human)
    {
        if (!AddonConfig.ModEnabled.Value || !human || hq == null)
        {
            return;
        }

        FactionHQ? localHq = GroundControlReflection.GetLocalHq();
        if (!GroundControlReflection.HasHumanPlayers(hq) && GroundControlReflection.IsSafeAiFaction(hq, localHq))
        {
            human = false;
        }
    }
}
