using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal static class CustomPreviewService
{
    private static readonly Dictionary<string, Texture2D?> PngCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<UnitDefinition> ClearScratch = new();

    internal static void ResetSession()
    {
        ClearCustomDictEntries(GroundControlReflection.UnitPreviewPartsField?.GetValue(null) as IDictionary);

        object? previewObj = GroundControlReflection.EcoGetPreview(GroundControlReflection.GetEcoInstance());
        if (previewObj != null)
        {
            ClearCustomDictEntries(GroundControlReflection.BuildPreviewPartsField?.GetValue(previewObj) as IDictionary);
        }
    }

    private static void ClearCustomDictEntries(IDictionary? partsDict)
    {
        if (partsDict == null || partsDict.Count == 0)
        {
            return;
        }

        ClearScratch.Clear();
        foreach (DictionaryEntry entry in partsDict)
        {
            if (entry.Key is UnitDefinition def && IsCustomDefinition(def))
            {
                ClearScratch.Add(def);
            }
        }

        for (int i = 0; i < ClearScratch.Count; i++)
        {
            partsDict.Remove(ClearScratch[i]);
        }

        ClearScratch.Clear();
    }

    internal static bool TryDrawPngOverride(Rect rect, UnitDefinition? definition)
    {
        if (!AddonConfig.ModEnabled.Value || definition == null)
        {
            return false;
        }

        Texture2D? tex = ResolvePngOverride(definition);
        if (tex == null)
        {
            return false;
        }

        if (Event.current.type == EventType.Repaint)
        {
            GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, true);
        }

        return true;
    }

    internal static void EnsureUnitPreviewParts(UnitDefinition? definition)
    {
        if (!AddonConfig.ModEnabled.Value
            || definition == null
            || GroundControlReflection.UnitPreviewPartsField == null
            || GroundControlReflection.UnitPreviewPartType == null
            || GroundControlReflection.UnitPreviewPartCtor == null)
        {
            return;
        }

        if (GroundControlReflection.UnitPreviewPartsField.GetValue(null) is not IDictionary partsDict
            || partsDict.Contains(definition))
        {
            return;
        }

        IReadOnlyList<(Mesh Mesh, Material Material)>? meshes = ResolveMeshes(definition);
        if (meshes == null || meshes.Count == 0)
        {
            return;
        }

        IList? list = BuildPartList(
            GroundControlReflection.UnitPreviewPartType,
            GroundControlReflection.UnitPreviewPartCtor,
            meshes);
        if (list != null)
        {
            partsDict[definition] = list;
        }
    }

    internal static void EnsureBuildPreviewParts(object? buildPreviewInstance, BuildingDefinition? candidate)
    {
        if (!AddonConfig.ModEnabled.Value
            || buildPreviewInstance == null
            || candidate == null
            || GroundControlReflection.BuildPreviewPartsField == null
            || GroundControlReflection.BuildPreviewPartType == null
            || GroundControlReflection.BuildPreviewPartCtor == null)
        {
            return;
        }

        if (GroundControlReflection.BuildPreviewPartsField.GetValue(buildPreviewInstance) is not IDictionary partsDict
            || partsDict.Contains(candidate))
        {
            return;
        }

        IReadOnlyList<(Mesh Mesh, Material Material)>? meshes = ResolveMeshes(candidate);
        if (meshes == null || meshes.Count == 0)
        {
            return;
        }

        IList? list = BuildPartList(
            GroundControlReflection.BuildPreviewPartType,
            GroundControlReflection.BuildPreviewPartCtor,
            meshes);
        if (list != null)
        {
            partsDict[candidate] = list;
        }
    }

    private static bool IsCustomDefinition(UnitDefinition def)
    {
        if (def is BuildingDefinition bd && RtsBuildingCatalog.TryGetSpec(bd, out _))
        {
            return true;
        }

        if (MdsBridgeBootstrap.TryResolveMdsPreviewGlb?.Invoke(def) != null)
        {
            return true;
        }

        return !string.IsNullOrEmpty(def.jsonKey)
            && (def.jsonKey.StartsWith(RtsBuildingCatalog.JsonKeyPrefix, StringComparison.OrdinalIgnoreCase)
                || def.jsonKey.StartsWith("mds_", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<(Mesh Mesh, Material Material)>? ResolveMeshes(UnitDefinition def)
    {
        if (def is BuildingDefinition bd && RtsBuildingCatalog.TryGetSpec(bd, out RtsBuildingSpec rtsSpec))
        {
            return ModelRegistry.GetPreviewMeshesForSlot(rtsSpec.ModelSlotId, 1);
        }

        (string GlbPath, float TargetMaxMeters, bool SampleEndFrame)? mdsGlb = MdsBridgeBootstrap.TryResolveMdsPreviewGlb?.Invoke(def);
        if (mdsGlb.HasValue && !string.IsNullOrEmpty(mdsGlb.Value.GlbPath))
        {
            return ModelRegistry.GetPreviewMeshesForExternalGlb(
                mdsGlb.Value.GlbPath,
                mdsGlb.Value.TargetMaxMeters,
                mdsGlb.Value.SampleEndFrame);
        }

        return null;
    }

    private static IList? BuildPartList(
        Type partType,
        ConstructorInfo partCtor,
        IReadOnlyList<(Mesh Mesh, Material Material)> meshes)
    {
        Type listType = typeof(List<>).MakeGenericType(partType);
        if (Activator.CreateInstance(listType) is not IList list)
        {
            return null;
        }

        for (int i = 0; i < meshes.Count; i++)
        {
            (Mesh mesh, Material mat) = meshes[i];
            if (mesh == null || mat == null)
            {
                continue;
            }

            object part = partCtor.Invoke(new object[] { mesh, new[] { mat }, Matrix4x4.identity });
            list.Add(part);
        }

        return list.Count > 0 ? list : null;
    }

    private static Texture2D? ResolvePngOverride(UnitDefinition def)
    {
        if (string.IsNullOrEmpty(AddonPlugin.PluginDirectory))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(def.jsonKey))
        {
            Texture2D? byKey = LoadPngCached(def.jsonKey);
            if (byKey != null)
            {
                return byKey;
            }
        }

        if (def is BuildingDefinition bd && RtsBuildingCatalog.TryGetSpec(bd, out RtsBuildingSpec spec))
        {
            Texture2D? bySlot = LoadPngCached(spec.ModelSlotId);
            if (bySlot != null)
            {
                return bySlot;
            }
        }

        return null;
    }

    private static Texture2D? LoadPngCached(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (PngCache.TryGetValue(key, out Texture2D? cached))
        {
            return cached;
        }

        string iconsDir = Path.Combine(AddonPlugin.PluginDirectory, "Icons");
        string fullPath = Path.Combine(iconsDir, key + ".png");
        if (!File.Exists(fullPath))
        {
            PngCache[key] = null;
            return null;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(fullPath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
            {
                name = $"GCE_Icon_{key}",
                hideFlags = HideFlags.DontUnloadUnusedAsset,
            };
            if (!tex.LoadImage(bytes, markNonReadable: true))
            {
                UnityEngine.Object.Destroy(tex);
                PngCache[key] = null;
                return null;
            }

            PngCache[key] = tex;
            return tex;
        }
        catch (Exception ex)
        {
            AddonLog.Warn($"[Icons] Failed to load icon '{fullPath}': {ex.Message}");
            PngCache[key] = null;
            return null;
        }
    }
}

[HarmonyPatch]
internal static class UnitPreviewGetPartsPatch
{
    private static bool Prepare() =>
        GroundControlReflection.UnitPreviewGetPartsMethod != null
        && GroundControlReflection.UnitPreviewPartsField != null
        && GroundControlReflection.UnitPreviewPartType != null
        && GroundControlReflection.UnitPreviewPartCtor != null;

    private static MethodBase TargetMethod() => GroundControlReflection.UnitPreviewGetPartsMethod!;

    private static void Prefix(UnitDefinition? definition)
    {
        CustomPreviewService.EnsureUnitPreviewParts(definition);
    }
}

[HarmonyPatch]
internal static class UnitPreviewDrawStillPatch
{
    private static bool Prepare() => GroundControlReflection.UnitPreviewDrawStillMethod != null;
    private static MethodBase TargetMethod() => GroundControlReflection.UnitPreviewDrawStillMethod!;

    private static bool Prefix(Rect rect, UnitDefinition? definition)
    {
        return !CustomPreviewService.TryDrawPngOverride(rect, definition);
    }
}

[HarmonyPatch]
internal static class BuildPreviewGetPartsPatch
{
    private static bool Prepare() =>
        GroundControlReflection.BuildPreviewGetPartsMethod != null
        && GroundControlReflection.BuildPreviewPartsField != null
        && GroundControlReflection.BuildPreviewPartType != null
        && GroundControlReflection.BuildPreviewPartCtor != null;

    private static MethodBase TargetMethod() => GroundControlReflection.BuildPreviewGetPartsMethod!;

    private static void Prefix(object? __instance, BuildingDefinition? candidate)
    {
        CustomPreviewService.EnsureBuildPreviewParts(__instance, candidate);
    }
}
