using System;
using System.Collections.Generic;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal static class SceneComponentCache
{
    private const float CacheTtlSeconds = 0.5f;

    private static int unitGraphGeneration = 1;

    private static FireControl[] cachedFireControls = Array.Empty<FireControl>();
    private static float fireControlsExpireAt;
    private static int fireControlsGeneration = -1;

    private static Turret[] cachedTurrets = Array.Empty<Turret>();
    private static float turretsExpireAt;
    private static int turretsGeneration = -1;

    private static Factory[] cachedFactories = Array.Empty<Factory>();
    private static float factoriesExpireAt;
    private static int factoriesGeneration = -1;

    private static readonly List<VehicleDepot> CachedVehicleDepots = new(32);
    private static float vehicleDepotsExpireAt;
    private static int vehicleDepotsGeneration = -1;

    internal static void BumpGeneration()
    {
        unchecked
        {
            unitGraphGeneration++;
            if (unitGraphGeneration <= 0)
            {
                unitGraphGeneration = 1;
            }
        }
    }

    internal static FireControl[] GetFireControls()
    {
        float now = Time.unscaledTime;
        int gen = unitGraphGeneration;
        if (now >= fireControlsExpireAt || gen != fireControlsGeneration || ContainsDestroyed(cachedFireControls))
        {
            cachedFireControls = UnityEngine.Object.FindObjectsOfType<FireControl>();
            fireControlsExpireAt = now + CacheTtlSeconds;
            fireControlsGeneration = gen;
        }

        return cachedFireControls;
    }

    internal static Turret[] GetTurrets()
    {
        float now = Time.unscaledTime;
        int gen = unitGraphGeneration;
        if (now >= turretsExpireAt || gen != turretsGeneration || ContainsDestroyed(cachedTurrets))
        {
            cachedTurrets = UnityEngine.Object.FindObjectsOfType<Turret>();
            turretsExpireAt = now + CacheTtlSeconds;
            turretsGeneration = gen;
        }

        return cachedTurrets;
    }

    internal static Factory[] GetFactories()
    {
        float now = Time.unscaledTime;
        int gen = unitGraphGeneration;
        if (now >= factoriesExpireAt || gen != factoriesGeneration || ContainsDestroyed(cachedFactories))
        {
            cachedFactories = UnityEngine.Object.FindObjectsOfType<Factory>();
            factoriesExpireAt = now + CacheTtlSeconds;
            factoriesGeneration = gen;
        }

        return cachedFactories;
    }

    internal static List<VehicleDepot> GetVehicleDepots()
    {
        float now = Time.unscaledTime;
        int gen = unitGraphGeneration;
        if (now >= vehicleDepotsExpireAt || gen != vehicleDepotsGeneration)
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
            vehicleDepotsGeneration = gen;
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
        fireControlsGeneration = -1;
        turretsGeneration = -1;
        factoriesGeneration = -1;
        vehicleDepotsGeneration = -1;
        BumpGeneration();
    }
}
