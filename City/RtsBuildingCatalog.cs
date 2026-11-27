using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal enum RtsBuildingKind
{
    CommandHq = 0,
    Barracks = 1,
    PilotAcademy = 2,
    LogisticsCamp = 3,
    NavalCommand = 4,
    AbmPlant = 5,
    AbmRadar = 6,
    AbmSilo = 7,
}

internal sealed class RtsBuildingSpec
{
    internal RtsBuildingKind Kind { get; set; }
    internal string JsonKey { get; set; } = string.Empty;
    internal string ModelSlotId { get; set; } = string.Empty;
    internal string DisplayName { get; set; } = string.Empty;
    internal string ShortSummary { get; set; } = string.Empty;
    internal int RequiredTechTier { get; set; } = 1;
    internal bool IsAbmInfrastructure { get; set; }
    internal BuildingType NativeBuildCategory { get; set; } = BuildingType.FAC;
    internal int MaxPerFaction { get; set; }
    internal float BuildCost { get; set; }
    internal float[] UpgradeCosts { get; set; } = Array.Empty<float>();
    internal int[] GroundCapBonus { get; set; } = Array.Empty<int>();
    internal int[] AirCapBonus { get; set; } = Array.Empty<int>();
    internal int[] NavalCapBonus { get; set; } = Array.Empty<int>();

    internal float GetUpgradeCost(int currentLevel)
    {
        if (currentLevel < 1 || currentLevel >= UpgradeCosts.Length)
        {
            return 0f;
        }

        return UpgradeCosts[currentLevel];
    }

    internal int GetGroundBonus(int level) => GetLevelValue(GroundCapBonus, level);
    internal int GetAirBonus(int level) => GetLevelValue(AirCapBonus, level);
    internal int GetNavalBonus(int level) => GetLevelValue(NavalCapBonus, level);

    private static int GetLevelValue(int[] table, int level)
    {
        int idx = Mathf.Clamp(level - 1, 0, Math.Max(0, table.Length - 1));
        return table.Length > 0 ? table[idx] : 0;
    }
}

internal static class RtsBuildingCatalog
{
    internal const string JsonKeyPrefix = "gce_";
    private const string CustomVisualPrefix = "GCE_";
    private static Material? foundationPlinthMaterial;

    private static readonly RtsBuildingSpec[] Specs =
    {
        new RtsBuildingSpec
        {
            Kind = RtsBuildingKind.CommandHq,
            JsonKey = "gce_command_hq",
            ModelSlotId = "command_hq",
            DisplayName = "Command HQ",
            ShortSummary = "Tech Tier 1->3 & +3/+6/+10 Ground Cap",
            NativeBuildCategory = BuildingType.FAC,
            MaxPerFaction = 1,
            BuildCost = 180f,
            UpgradeCosts = new[] { 0f, 320f, 650f },
            GroundCapBonus = new[] { 3, 6, 10 },
            AirCapBonus = new[] { 0, 0, 0 },
            NavalCapBonus = new[] { 0, 0, 0 },
        },
        new RtsBuildingSpec
        {
            Kind = RtsBuildingKind.Barracks,
            JsonKey = "gce_barracks",
            ModelSlotId = "barracks",
            DisplayName = "Barracks",
            ShortSummary = "+10 / +22 / +38 Ground Cap",
            NativeBuildCategory = BuildingType.FAC,
            MaxPerFaction = 6,
            BuildCost = 120f,
            UpgradeCosts = new[] { 0f, 180f, 320f },
            GroundCapBonus = new[] { 10, 22, 38 },
            AirCapBonus = new[] { 0, 0, 0 },
            NavalCapBonus = new[] { 0, 0, 0 },
        },
        new RtsBuildingSpec
        {
            Kind = RtsBuildingKind.PilotAcademy,
            JsonKey = "gce_pilot_academy",
            ModelSlotId = "pilot_academy",
            DisplayName = "Pilot Academy",
            ShortSummary = "+3/+6/+10 Air Cap · Unlocks Tier 2/3 Jets",
            NativeBuildCategory = BuildingType.HGR,
            MaxPerFaction = 4,
            BuildCost = 220f,
            UpgradeCosts = new[] { 0f, 340f, 560f },
            GroundCapBonus = new[] { 0, 0, 0 },
            AirCapBonus = new[] { 3, 6, 10 },
            NavalCapBonus = new[] { 0, 0, 0 },
        },
        new RtsBuildingSpec
        {
            Kind = RtsBuildingKind.LogisticsCamp,
            JsonKey = "gce_logistics_camp",
            ModelSlotId = "logistics_camp",
            DisplayName = "Logistics Camp",
            ShortSummary = "+6/+12/+20 Gnd, +1..3 Air, +1..2 Nav & Repair Aura",
            NativeBuildCategory = BuildingType.FAC,
            MaxPerFaction = 4,
            BuildCost = 160f,
            UpgradeCosts = new[] { 0f, 240f, 400f },
            GroundCapBonus = new[] { 6, 12, 20 },
            AirCapBonus = new[] { 1, 2, 3 },
            NavalCapBonus = new[] { 1, 1, 2 },
        },
        new RtsBuildingSpec
        {
            Kind = RtsBuildingKind.NavalCommand,
            JsonKey = "gce_naval_command",
            ModelSlotId = "naval_command",
            DisplayName = "Naval Command",
            ShortSummary = "+2/+5/+9 Naval Cap · Unlocks Tier 2/3 Ships",
            NativeBuildCategory = BuildingType.FAC,
            MaxPerFaction = 3,
            BuildCost = 200f,
            UpgradeCosts = new[] { 0f, 320f, 520f },
            GroundCapBonus = new[] { 0, 0, 0 },
            AirCapBonus = new[] { 0, 0, 0 },
            NavalCapBonus = new[] { 2, 5, 9 },
        },
        new RtsBuildingSpec
        {
            Kind = RtsBuildingKind.AbmPlant,
            JsonKey = "gce_abm_plant",
            ModelSlotId = "abm_plant",
            DisplayName = "ABM Interceptor Plant",
            ShortSummary = "T3 · Produces ABM Interceptors (Lvl 1-3 speed) & +4/+6/+8 Mag",
            RequiredTechTier = 3,
            IsAbmInfrastructure = true,
            NativeBuildCategory = BuildingType.DEF,
            MaxPerFaction = 2,
            BuildCost = 450f,
            UpgradeCosts = new[] { 0f, 550f, 850f },
            GroundCapBonus = new[] { 0, 0, 0 },
            AirCapBonus = new[] { 0, 0, 0 },
            NavalCapBonus = new[] { 0, 0, 0 },
        },
        new RtsBuildingSpec
        {
            Kind = RtsBuildingKind.AbmRadar,
            JsonKey = "gce_abm_radar",
            ModelSlotId = "abm_radar",
            DisplayName = "ABM Early-Warning Radar",
            ShortSummary = "T3 · Ballistic Early Warning & Guidance (45 / 60 / 80 km)",
            RequiredTechTier = 3,
            IsAbmInfrastructure = true,
            NativeBuildCategory = BuildingType.RDR,
            MaxPerFaction = 2,
            BuildCost = 320f,
            UpgradeCosts = new[] { 0f, 420f, 680f },
            GroundCapBonus = new[] { 0, 0, 0 },
            AirCapBonus = new[] { 0, 0, 0 },
            NavalCapBonus = new[] { 0, 0, 0 },
        },
        new RtsBuildingSpec
        {
            Kind = RtsBuildingKind.AbmSilo,
            JsonKey = "gce_abm_silo",
            ModelSlotId = "abm_silo",
            DisplayName = "ABM VLS Silo",
            ShortSummary = "T3 · Vertical ABM Launcher & +8/+12/+16 Stockpile Cap",
            RequiredTechTier = 3,
            IsAbmInfrastructure = true,
            NativeBuildCategory = BuildingType.DEF,
            MaxPerFaction = 3,
            BuildCost = 380f,
            UpgradeCosts = new[] { 0f, 480f, 720f },
            GroundCapBonus = new[] { 0, 0, 0 },
            AirCapBonus = new[] { 0, 0, 0 },
            NavalCapBonus = new[] { 0, 0, 0 },
        },
    };

    private static readonly Dictionary<RtsBuildingKind, RtsBuildingSpec> SpecByKind = new();
    private static readonly Dictionary<string, RtsBuildingSpec> SpecByKey = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<RtsBuildingKind, BuildingDefinition> ClonedDefs = new();
    private static Encyclopedia? cachedEncyclopedia;

    static RtsBuildingCatalog()
    {
        for (int i = 0; i < Specs.Length; i++)
        {
            SpecByKind[Specs[i].Kind] = Specs[i];
            SpecByKey[Specs[i].JsonKey] = Specs[i];
        }
    }

    internal static IReadOnlyList<RtsBuildingSpec> AllSpecs => Specs;

    internal static RtsBuildingSpec GetSpec(RtsBuildingKind kind) => SpecByKind[kind];

    internal static bool TryGetSpec(BuildingDefinition? def, out RtsBuildingSpec spec)
    {
        if (def != null && !string.IsNullOrEmpty(def.jsonKey) && SpecByKey.TryGetValue(def.jsonKey, out RtsBuildingSpec? found))
        {
            spec = found;
            return true;
        }

        spec = null!;
        return false;
    }

    internal static bool TryMatchByUniqueName(string? uniqueName, out RtsBuildingSpec spec)
    {
        if (!string.IsNullOrEmpty(uniqueName))
        {
            string name = uniqueName!;
            for (int i = 0; i < Specs.Length; i++)
            {
                if (name.StartsWith(Specs[i].DisplayName + " ", StringComparison.Ordinal)
                    || string.Equals(name, Specs[i].DisplayName, StringComparison.Ordinal))
                {
                    spec = Specs[i];
                    return true;
                }
            }
        }

        spec = null!;
        return false;
    }

    internal static BuildingDefinition? GetOrCreateDefinition(RtsBuildingKind kind)
    {
        Encyclopedia? enc = Encyclopedia.i;
        if (enc == null || enc.buildings == null)
        {
            return null;
        }

        if (!ReferenceEquals(cachedEncyclopedia, enc))
        {
            ClonedDefs.Clear();
            cachedEncyclopedia = enc;
        }

        if (ClonedDefs.TryGetValue(kind, out BuildingDefinition? existing) && existing != null)
        {
            return existing;
        }

        BuildingDefinition? plain = PickPlainBuilding(enc);
        if (plain == null)
        {
            AddonLog.Warn($"[City] Could not find a plain vanilla BuildingDefinition to clone '{kind}'.");
            return null;
        }

        RtsBuildingSpec spec = SpecByKind[kind];
        BuildingDefinition clone = UnityEngine.Object.Instantiate(plain);
        clone.name = spec.JsonKey;
        clone.jsonKey = spec.JsonKey;
        clone.unitName = spec.DisplayName;
        clone.code = spec.ModelSlotId.ToUpperInvariant();
        clone.buildingType = spec.NativeBuildCategory;
        clone.value = spec.BuildCost;
        clone.spawnOffset = Vector3.zero;
        clone.hideFlags = HideFlags.DontUnloadUnusedAsset;

        if (plain is INetworkDefinition srcNet && clone is INetworkDefinition dstNet)
        {
            dstNet.LookupIndex = srcNet.LookupIndex;
        }

        if (Encyclopedia.Lookup != null)
        {
            Encyclopedia.Lookup[clone.jsonKey] = clone;
        }

        ClonedDefs[kind] = clone;
        AddonLog.Debug($"[City] Registered cloned BuildingDefinition '{spec.JsonKey}' from '{plain.jsonKey}'.");
        return clone;
    }

    internal static void EnsureLookupEntries()
    {
        for (int i = 0; i < Specs.Length; i++)
        {
            GetOrCreateDefinition(Specs[i].Kind);
        }
    }

    internal static void SetCustomVisualVisible(Unit unit, RtsBuildingSpec spec, int level, bool visible)
    {
        if (unit == null)
        {
            return;
        }

        bool foundCustom = false;
        for (int i = unit.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = unit.transform.GetChild(i);
            if (child != null && child.name.StartsWith(CustomVisualPrefix, StringComparison.Ordinal))
            {
                foundCustom = true;
                if (child.gameObject.activeSelf != visible)
                {
                    child.gameObject.SetActive(visible);
                }
            }
        }

        if (visible)
        {
            if (!foundCustom)
            {
                ApplyCustomVisual(unit, spec, level, forceReplace: true);
                return;
            }

            MeshRenderer[] renderers = unit.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && !IsUnderCustomVisual(renderers[i].transform))
                {
                    renderers[i].enabled = false;
                }
            }

            SkinnedMeshRenderer[] skinned = unit.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinned.Length; i++)
            {
                if (skinned[i] != null && !IsUnderCustomVisual(skinned[i].transform))
                {
                    skinned[i].enabled = false;
                }
            }
        }
    }

    internal static void ApplyCustomVisual(Unit unit, RtsBuildingSpec spec, int level = 1, bool forceReplace = false)
    {
        if (unit == null)
        {
            return;
        }

        bool hasExisting = false;
        for (int i = unit.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = unit.transform.GetChild(i);
            if (child != null && child.name.StartsWith(CustomVisualPrefix, StringComparison.Ordinal))
            {
                if (!forceReplace)
                {
                    hasExisting = true;
                    break;
                }

                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        if (hasExisting)
        {
            return;
        }

        MeshRenderer[] renderers = unit.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && !IsUnderCustomVisual(renderers[i].transform))
            {
                renderers[i].enabled = false;
            }
        }

        SkinnedMeshRenderer[] skinned = unit.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinned.Length; i++)
        {
            if (skinned[i] != null && !IsUnderCustomVisual(skinned[i].transform))
            {
                skinned[i].enabled = false;
            }
        }

        GameObject visual = ModelRegistry.InstantiateModelForLevel(spec.ModelSlotId, level, unit.transform, addBoxCollider: false);
        GroundVisualToTerrain(unit, visual, spec, level);
    }

    private static void GroundVisualToTerrain(Unit unit, GameObject visual, RtsBuildingSpec spec, int level)
    {
        if (unit == null || visual == null)
        {
            return;
        }

        Vector3 centerGroundLocal = GroundControlReflection.SnapToTerrain(unit.GlobalPosition()).ToLocalPosition();
        float centerDeltaY = Mathf.Clamp(centerGroundLocal.y - unit.transform.position.y, -18f, 4f) + 0.02f;
        visual.transform.localPosition = new Vector3(0f, centerDeltaY, 0f);

        Bounds normBounds = ModelRegistry.GetNormalizedBounds(spec.ModelSlotId, level);
        float halfX = Mathf.Max(2f, normBounds.extents.x * 0.85f);
        float halfZ = Mathf.Max(2f, normBounds.extents.z * 0.85f);
        Quaternion rot = unit.transform.rotation;

        float minCornerGround = centerGroundLocal.y;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 cornerLocalOffset = rot * new Vector3(
                    normBounds.center.x + sx * halfX,
                    0f,
                    normBounds.center.z + sz * halfZ);
                GlobalPosition cornerGlobal = (unit.transform.position + cornerLocalOffset).ToGlobalPosition();
                float cornerGroundY = GroundControlReflection.SnapToTerrain(cornerGlobal).ToLocalPosition().y;
                if (cornerGroundY < minCornerGround)
                {
                    minCornerGround = cornerGroundY;
                }
            }
        }

        float slopeDrop = Mathf.Clamp(centerGroundLocal.y - minCornerGround, 0f, 8f);
        if (slopeDrop <= 0.15f)
        {
            return;
        }

        float scaleX = Mathf.Max(0.1f, visual.transform.localScale.x);
        float scaleY = Mathf.Max(0.1f, visual.transform.localScale.y);
        float scaleZ = Mathf.Max(0.1f, visual.transform.localScale.z);
        float plinthHeightWorld = slopeDrop + 0.6f;

        GameObject plinth = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plinth.name = "GCE_FoundationPlinth";
        Collider? plinthCol = plinth.GetComponent<Collider>();
        if (plinthCol != null)
        {
            plinthCol.enabled = false;
            UnityEngine.Object.Destroy(plinthCol);
        }

        plinth.transform.SetParent(visual.transform, false);
        plinth.transform.localScale = new Vector3(
            Mathf.Max(2f, normBounds.size.x * 0.88f) / scaleX,
            plinthHeightWorld / scaleY,
            Mathf.Max(2f, normBounds.size.z * 0.88f) / scaleZ);
        plinth.transform.localPosition = new Vector3(
            normBounds.center.x / scaleX,
            -(plinthHeightWorld * 0.5f + 0.02f) / scaleY,
            normBounds.center.z / scaleZ);

        if (plinth.TryGetComponent<MeshRenderer>(out MeshRenderer mr))
        {
            foundationPlinthMaterial ??= UrpMaterialFactory.CreateMaterial(
                "GCE_FoundationPlinthMat",
                new Color(0.25f, 0.26f, 0.27f, 1f),
                null,
                metallic: 0.05f,
                smoothness: 0.25f,
                tintOverride: null);
            mr.sharedMaterial = foundationPlinthMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
        }
    }

    private static bool IsUnderCustomVisual(Transform? tf)
    {
        while (tf != null)
        {
            if (tf.name.StartsWith(CustomVisualPrefix, StringComparison.Ordinal))
            {
                return true;
            }

            tf = tf.parent;
        }

        return false;
    }

    internal static void ResetSession()
    {
        ClonedDefs.Clear();
        cachedEncyclopedia = null;
    }

    internal static BuildingDefinition? PickPlainBuildingDefinition()
        => Encyclopedia.i != null ? PickPlainBuilding(Encyclopedia.i) : null;

    private static readonly FieldInfo? UnitPartDamageEffectsField = AccessTools.Field(typeof(UnitPart), "damageEffects");

    private static readonly string[] HazardousBuildingKeywords =
    {
        "fuel",
        "tank",
        "ammo",
        "munition",
        "oil",
        "gas",
        "refinery",
        "reactor",
        "power",
        "storage",
        "silo",
        "explosive",
        "bomb",
        "radar",
        "control tower",
    };

    private static BuildingDefinition? PickPlainBuilding(Encyclopedia enc)
    {
        BuildingDefinition? best = null;
        BuildingDefinition? fallback = null;
        BuildingDefinition? lastResortFallback = null;

        for (int i = 0; i < enc.buildings.Count; i++)
        {
            BuildingDefinition? b = enc.buildings[i];
            if (b == null || b.unitPrefab == null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(b.jsonKey)
                && (b.jsonKey.StartsWith("mds_", StringComparison.OrdinalIgnoreCase)
                    || b.jsonKey.StartsWith(JsonKeyPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (string.Equals(b.jsonKey, "controlTower1", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(b.jsonKey) && b.jsonKey.IndexOf("tower", StringComparison.OrdinalIgnoreCase) >= 0)
                || (!string.IsNullOrEmpty(b.unitName) && b.unitName.IndexOf("Control Tower", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                continue;
            }

            GameObject prefab = b.unitPrefab;
            if (prefab.GetComponent<Building>() == null)
            {
                continue;
            }

            if (prefab.GetComponentInChildren<Radar>(true) != null
                || prefab.GetComponentInChildren<Airbase>(true) != null)
            {
                continue;
            }

            lastResortFallback ??= b;

            if (IsHazardousOrExplosiveBuilding(b, prefab))
            {
                continue;
            }

            fallback ??= b;

            if (prefab.GetComponentInChildren<TargetDetector>(true) != null
                || prefab.GetComponentInChildren<Turret>(true) != null
                || prefab.GetComponentInChildren<Factory>(true) != null
                || prefab.GetComponentInChildren<Hangar>(true) != null
                || prefab.GetComponentInChildren<VehicleDepot>(true) != null
                || prefab.GetComponentInChildren<Rearmer>(true) != null
                || prefab.GetComponentInChildren<UnitStorage>(true) != null)
            {
                continue;
            }

            if (best == null || b.armorTier < best.armorTier || (Mathf.Approximately(b.armorTier, best.armorTier) && b.height < best.height))
            {
                best = b;
            }
        }

        return best ?? fallback ?? lastResortFallback;
    }

    private static bool IsHazardousOrExplosiveBuilding(BuildingDefinition b, GameObject prefab)
    {
        if (b.buildingType == BuildingType.AMMO)
        {
            return true;
        }

        string combined = $"{b.jsonKey} {b.unitName}";
        for (int i = 0; i < HazardousBuildingKeywords.Length; i++)
        {
            if (combined.IndexOf(HazardousBuildingKeywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        if (UnitPartDamageEffectsField == null)
        {
            return false;
        }

        UnitPart[] parts = prefab.GetComponentsInChildren<UnitPart>(true);
        for (int p = 0; p < parts.Length; p++)
        {
            UnitPart? part = parts[p];
            if (part == null || UnitPartDamageEffectsField.GetValue(part) is not List<DamageEffect> effects)
            {
                continue;
            }

            for (int e = 0; e < effects.Count; e++)
            {
                string fxName = effects[e]?.prefab != null ? effects[e].prefab.name : string.Empty;
                if (fxName.IndexOf("fire", StringComparison.OrdinalIgnoreCase) >= 0
                    || fxName.IndexOf("flame", StringComparison.OrdinalIgnoreCase) >= 0
                    || fxName.IndexOf("explos", StringComparison.OrdinalIgnoreCase) >= 0
                    || fxName.IndexOf("blast", StringComparison.OrdinalIgnoreCase) >= 0
                    || fxName.IndexOf("burn", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
        }

        return false;
    }
}

[HarmonyPatch(typeof(Encyclopedia), "AfterLoad")]
internal static class RtsBuildingCatalogAfterLoadPatch
{
    private static void Postfix()
    {
        if (!AddonConfig.ModEnabled.Value)
        {
            return;
        }

        RtsBuildingCatalog.EnsureLookupEntries();
        MdsBridgeBootstrap.EnsureMdsBuildCatalogLookupAction?.Invoke();
    }
}

[HarmonyPatch(typeof(Building), nameof(Building.UnitDisabled), new[] { typeof(bool), typeof(bool) })]
internal static class RtsBuildingUnitDisabledVisualPatch
{
    private static void Postfix(Building __instance, bool oldState, bool newState)
    {
        if (!AddonConfig.ModEnabled.Value || __instance == null)
        {
            return;
        }

        RtsBuildingRecord? rec = __instance.persistentID.IsValid
            ? DynamicCapAndTechService.Instance.FindRecordByPersistentId(__instance.persistentID.Id)
            : null;
        if (rec == null && __instance.definition is BuildingDefinition bd && RtsBuildingCatalog.TryGetSpec(bd, out RtsBuildingSpec specByDef))
        {
            RtsBuildingCatalog.SetCustomVisualVisible(__instance, specByDef, level: 1, visible: !newState);
            return;
        }

        if (rec != null)
        {
            RtsBuildingCatalog.SetCustomVisualVisible(__instance, rec.Spec, rec.Level, visible: !newState);
        }
    }
}
