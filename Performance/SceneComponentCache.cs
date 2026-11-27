using System;
using System.Collections.Generic;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal static class SceneComponentCache
{
    private const float CacheTtlSeconds = 0.5f;

    private static FireControl[] cachedFireControls = Array.Empty<FireControl>();
    private static float fireControlsExpireAt;
    private static int fireControlsUnitCount = -1;

    private static Turret[] cachedTurrets = Array.Empty<Turret>();
    private static float turretsExpireAt;
    private static int turretsUnitCount = -1;

    private static Factory[] cachedFactories = Array.Empty<Factory>();
    private static float factoriesExpireAt;
    private static int factoriesUnitCount = -1;

    private static readonly List<VehicleDepot> CachedVehicleDepots = new(32);
    private static float vehicleDepotsExpireAt;
    private static int vehicleDepotsUnitCount = -1;

    internal static FireControl[] GetFireControls()
    {
        float now = Time.unscaledTime;
        int unitCount = UnitRegistry.allUnits.Count;
        if (now >= fireControlsExpireAt || unitCount != fireControlsUnitCount || ContainsDestroyed(cachedFireControls))
        {
            cachedFireControls = UnityEngine.Object.FindObjectsOfType<FireControl>();
            fireControlsExpireAt = now + CacheTtlSeconds;
            fireControlsUnitCount = unitCount;
        }

        return cachedFireControls;
    }

    internal static Turret[] GetTurrets()
    {
        float now = Time.unscaledTime;
        int unitCount = UnitRegistry.allUnits.Count;
        if (now >= turretsExpireAt || unitCount != turretsUnitCount || ContainsDestroyed(cachedTurrets))
        {
            cachedTurrets = UnityEngine.Object.FindObjectsOfType<Turret>();
            turretsExpireAt = now + CacheTtlSeconds;
            turretsUnitCount = unitCount;
        }

        return cachedTurrets;
    }

    internal static Factory[] GetFactories()
    {
        float now = Time.unscaledTime;
        int unitCount = UnitRegistry.allUnits.Count;
        if (now >= factoriesExpireAt || unitCount != factoriesUnitCount || ContainsDestroyed(cachedFactories))
        {
            cachedFactories = UnityEngine.Object.FindObjectsOfType<Factory>();
            factoriesExpireAt = now + CacheTtlSeconds;
            factoriesUnitCount = unitCount;
        }

        return cachedFactories;
    }

    internal static List<VehicleDepot> GetVehicleDepots()
    {
        float now = Time.unscaledTime;
        int unitCount = UnitRegistry.allUnits.Count;
        if (now >= vehicleDepotsExpireAt || unitCount != vehicleDepotsUnitCount)
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
            vehicleDepotsUnitCount = unitCount;
        }
        else
        {
            for (int i = CachedVehicleDepots.Count - 1; i >= 0; i--)
            {
                VehicleDepot d = CachedVehicleDepots[i];
                if (d == null || d.disabled)
                {
                    CachedVehicleDepots.RemoveAt(i);
                }
            }
        }

        return CachedVehicleDepots;
    }

    private static bool ContainsDestroyed<T>(T[] items) where T : UnityEngine.Object
    {
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] == null)
            {
                return true;
            }
        }

        return false;
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
        fireControlsUnitCount = -1;
        turretsUnitCount = -1;
        factoriesUnitCount = -1;
        vehicleDepotsUnitCount = -1;
    }
}
