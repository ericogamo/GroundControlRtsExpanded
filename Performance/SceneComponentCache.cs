using System;
using System.Collections.Generic;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal static class SceneComponentCache
{
    private const float CacheTtlSeconds = 1.5f;

    private static FireControl[] cachedFireControls = Array.Empty<FireControl>();
    private static float fireControlsExpireAt;

    private static Turret[] cachedTurrets = Array.Empty<Turret>();
    private static float turretsExpireAt;

    private static Factory[] cachedFactories = Array.Empty<Factory>();
    private static float factoriesExpireAt;

    private static readonly List<VehicleDepot> CachedVehicleDepots = new(32);
    private static float vehicleDepotsExpireAt;

    internal static FireControl[] GetFireControls()
    {
        float now = Time.unscaledTime;
        if (now >= fireControlsExpireAt)
        {
            cachedFireControls = UnityEngine.Object.FindObjectsOfType<FireControl>();
            fireControlsExpireAt = now + CacheTtlSeconds;
        }

        return cachedFireControls;
    }

    internal static Turret[] GetTurrets()
    {
        float now = Time.unscaledTime;
        if (now >= turretsExpireAt)
        {
            cachedTurrets = UnityEngine.Object.FindObjectsOfType<Turret>();
            turretsExpireAt = now + CacheTtlSeconds;
        }

        return cachedTurrets;
    }

    internal static Factory[] GetFactories()
    {
        float now = Time.unscaledTime;
        if (now >= factoriesExpireAt)
        {
            cachedFactories = UnityEngine.Object.FindObjectsOfType<Factory>();
            factoriesExpireAt = now + CacheTtlSeconds;
        }

        return cachedFactories;
    }

    internal static List<VehicleDepot> GetVehicleDepots()
    {
        float now = Time.unscaledTime;
        if (now >= vehicleDepotsExpireAt)
        {
            CachedVehicleDepots.Clear();
            for (int i = 0; i < UnitRegistry.allUnits.Count; i++)
            {
                if (UnitRegistry.allUnits[i] is VehicleDepot depot && depot != null && !depot.disabled)
                {
                    CachedVehicleDepots.Add(depot);
                }
            }

            vehicleDepotsExpireAt = now + CacheTtlSeconds;
        }

        return CachedVehicleDepots;
    }

    internal static void Clear()
    {
        cachedFireControls = Array.Empty<FireControl>();
        cachedTurrets = Array.Empty<Turret>();
        cachedFactories = Array.Empty<Factory>();
        CachedVehicleDepots.Clear();
        fireControlsExpireAt = 0f;
        turretsExpireAt = 0f;
        factoriesExpireAt = 0f;
        vehicleDepotsExpireAt = 0f;
    }
}
