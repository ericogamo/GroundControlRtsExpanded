using System.Reflection;
using HarmonyLib;

namespace GroundControlRtsExpanded;

[HarmonyPatch(typeof(BallisticMissileGuidance), nameof(BallisticMissileGuidance.Initialize))]
internal static class BallisticGuidanceInitializePatch
{
    private static readonly FieldInfo? SeekerMissileField = AccessTools.Field(typeof(MissileSeeker), "missile");

    private static void Postfix(BallisticMissileGuidance __instance)
    {
        if (!AddonConfig.ModEnabled.Value || __instance == null || SeekerMissileField == null)
        {
            return;
        }

        if (SeekerMissileField.GetValue(__instance) is Missile missile)
        {
            AbmDefenseService.Instance.NotifyPotentialBallisticLaunch(missile, hasBallisticGuidance: true);
        }
    }
}

[HarmonyPatch(typeof(Missile), "StartMissile")]
internal static class MissileStartAbmPatch
{
    private static void Postfix(Missile __instance)
    {
        if (!AddonConfig.ModEnabled.Value || __instance == null)
        {
            return;
        }

        float yield = __instance.GetYield();
        if (yield > 200f)
        {
            AbmDefenseService.Instance.NotifyPotentialBallisticLaunch(__instance, hasBallisticGuidance: false);
        }
    }
}
