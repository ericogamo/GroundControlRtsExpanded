using System.Reflection;
using HarmonyLib;

namespace GroundControlRtsExpanded;

[HarmonyPatch]
internal static class ArmSeekerTrackCurrentTargetPatch
{
    private static readonly MethodInfo? Target =
        AccessTools.Method(typeof(ARMSeeker), nameof(ARMSeeker.TrackCurrentTarget), System.Type.EmptyTypes);

    private static readonly AccessTools.FieldRef<MissileSeeker, Missile?> SeekerMissileRef =
        AccessTools.FieldRefAccess<MissileSeeker, Missile?>("missile");

    private static readonly AccessTools.FieldRef<ARMSeeker, Radar?> TargetedRadarRef =
        AccessTools.FieldRefAccess<ARMSeeker, Radar?>("targetedRadar");

    private static bool Prepare() => Target != null;
    private static MethodBase TargetMethod() => Target!;

    private static void Postfix(ARMSeeker __instance, Radar? __result)
    {
        if (__instance == null || !AddonConfig.ModEnabled.Value || !AddonConfig.EnableAiRadarEmcon.Value)
        {
            return;
        }

        Missile? missile = SeekerMissileRef(__instance);
        Radar? radar = __result != null ? __result : TargetedRadarRef(__instance);
        AiRadarEmconService.Instance.OnArmSeekerTrack(missile, radar);
    }
}
