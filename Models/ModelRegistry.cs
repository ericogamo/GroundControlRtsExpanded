using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;

namespace GroundControlRtsExpanded;

internal static class ModelRegistry
{
    private sealed class CompiledSubMesh
    {
        internal string Name = string.Empty;
        internal Mesh Mesh = null!;
        internal Material Material = null!;
    }

    private sealed class CompiledModel
    {
        internal List<CompiledSubMesh> Parts = new();
        internal List<Texture2D> Textures = new();
        internal Bounds NormalizedBounds;
    }

    private static readonly Dictionary<string, ParsedModelAsset> ParsedAssets = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ModelSlotEntry> SlotConfigs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, CompiledModel> RuntimeCompiled = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, CompiledModel> ExternalPreviewCompiled = new(StringComparer.OrdinalIgnoreCase);

    private static string modelsDirectory = string.Empty;

    internal static void Initialize(string pluginDirectory)
    {
        modelsDirectory = Path.Combine(pluginDirectory, "Models");
        ReloadAll();
    }

    internal static void ReloadAll()
    {
        ClearRuntimeCache();
        ParsedAssets.Clear();
        SlotConfigs.Clear();

        if (string.IsNullOrEmpty(modelsDirectory))
        {
            return;
        }

        if (!Directory.Exists(modelsDirectory))
        {
            Directory.CreateDirectory(modelsDirectory);
        }

        ModelManifest manifest = LoadOrCreateManifest(Path.Combine(modelsDirectory, "models.json"));

        foreach (string slotId in ModelManifest.DefaultSlotOrder)
        {
            if (!manifest.Slots.ContainsKey(slotId))
            {
                ModelManifest defaults = ModelManifest.CreateDefault();
                if (defaults.Slots.TryGetValue(slotId, out ModelSlotEntry? fallbackCfg))
                {
                    manifest.Slots[slotId] = fallbackCfg;
                }
            }
        }

        var report = new StringBuilder();
        report.AppendLine("ModelRegistry loaded slots:");

        foreach (KeyValuePair<string, ModelSlotEntry> kv in manifest.Slots)
        {
            string slotId = kv.Key;
            ModelSlotEntry cfg = kv.Value;
            SlotConfigs[slotId] = cfg;

            ParsedModelAsset? parsed = null;
            if (AddonConfig.UseCustomModels.Value)
            {
                parsed = TryLoadSlotFromDisk(slotId, cfg);
            }

            parsed ??= ProceduralFallbackModels.Build(slotId);
            ParsedAssets[slotId] = parsed;

            Bounds scaledBounds = ComputeScaledBounds(parsed.RawBounds, cfg);
            report.AppendLine(
                $"  [{slotId}] {parsed.SourceKind.ToString().ToUpperInvariant()} " +
                $"file='{parsed.FileName}' meshes={parsed.SubMeshes.Count} " +
                $"verts={parsed.TotalVertices} tris={parsed.TotalTriangles} " +
                $"size=({scaledBounds.size.x:0.0}m x {scaledBounds.size.y:0.0}m x {scaledBounds.size.z:0.0}m)");
        }

        DiscoverLevelVariantSlots(manifest, report);

        AddonLog.Info(report.ToString().TrimEnd());
    }

    internal static void ClearRuntimeCache()
    {
        DestroyCompiledMap(RuntimeCompiled);
        DestroyCompiledMap(ExternalPreviewCompiled);
    }

    private static void DestroyCompiledMap(Dictionary<string, CompiledModel> map)
    {
        foreach (CompiledModel compiled in map.Values)
        {
            for (int i = 0; i < compiled.Parts.Count; i++)
            {
                if (compiled.Parts[i].Mesh != null)
                {
                    UnityEngine.Object.Destroy(compiled.Parts[i].Mesh);
                }

                if (compiled.Parts[i].Material != null)
                {
                    UnityEngine.Object.Destroy(compiled.Parts[i].Material);
                }
            }

            for (int i = 0; i < compiled.Textures.Count; i++)
            {
                if (compiled.Textures[i] != null)
                {
                    UnityEngine.Object.Destroy(compiled.Textures[i]);
                }
            }
        }

        map.Clear();
    }

    internal static GameObject InstantiateModel(string slotId, Transform? parent, bool addBoxCollider = false)
    {
        CompiledModel compiled = GetOrCompile(slotId);
        var root = new GameObject($"GCE_{slotId}");
        if (parent != null)
        {
            root.transform.SetParent(parent, false);
        }

        for (int i = 0; i < compiled.Parts.Count; i++)
        {
            CompiledSubMesh part = compiled.Parts[i];
            var child = new GameObject(part.Name);
            child.transform.SetParent(root.transform, false);

            MeshFilter mf = child.AddComponent<MeshFilter>();
            mf.sharedMesh = part.Mesh;

            MeshRenderer mr = child.AddComponent<MeshRenderer>();
            mr.sharedMaterial = part.Material;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
        }

        if (addBoxCollider && compiled.NormalizedBounds.size.sqrMagnitude > 0.0001f)
        {
            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.center = compiled.NormalizedBounds.center;
            collider.size = compiled.NormalizedBounds.size;
        }

        return root;
    }

    internal static GameObject InstantiateModelForLevel(
        string baseSlotId,
        int level,
        Transform? parent,
        bool addBoxCollider = false)
    {
        int clampedLevel = Mathf.Clamp(level, 1, 3);
        string resolvedSlot = ResolveSlotForLevel(baseSlotId, clampedLevel, out bool exact);
        GameObject root = InstantiateModel(resolvedSlot, parent, addBoxCollider);
        root.name = $"GCE_{baseSlotId}_L{clampedLevel}";

        if (!exact && clampedLevel > 1)
        {
            float scaleFactor = clampedLevel == 2 ? 1.08f : 1.16f;
            root.transform.localScale = Vector3.one * scaleFactor;
        }

        return root;
    }

    internal static Bounds GetNormalizedBounds(string baseSlotId, int level = 1)
    {
        int clampedLevel = Mathf.Clamp(level, 1, 3);
        string resolvedSlot = ResolveSlotForLevel(baseSlotId, clampedLevel, out bool exact);
        CompiledModel compiled = GetOrCompile(resolvedSlot);
        Bounds b = compiled.NormalizedBounds;

        if (!exact && clampedLevel > 1)
        {
            float scaleFactor = clampedLevel == 2 ? 1.08f : 1.16f;
            b = new Bounds(b.center * scaleFactor, b.size * scaleFactor);
        }

        return b;
    }

    internal static IReadOnlyList<(Mesh Mesh, Material Material)> GetPreviewMeshesForSlot(string slotId, int level = 1)
    {
        int clampedLevel = Mathf.Clamp(level, 1, 3);
        string resolvedSlot = ResolveSlotForLevel(slotId, clampedLevel, out _);
        CompiledModel compiled = GetOrCompile(resolvedSlot);
        return ToPreviewList(compiled);
    }

    internal static IReadOnlyList<(Mesh Mesh, Material Material)> GetPreviewMeshesForExternalGlb(
        string fullPath,
        float targetMaxMeters,
        bool sampleDeployEndFrame = false)
    {
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
        {
            return Array.Empty<(Mesh, Material)>();
        }

        string cacheKey = sampleDeployEndFrame ? (fullPath + "|end") : fullPath;
        if (ExternalPreviewCompiled.TryGetValue(cacheKey, out CompiledModel? existing))
        {
            return ToPreviewList(existing);
        }

        try
        {
            string slotName = Path.GetFileNameWithoutExtension(fullPath);
            ParsedModelAsset? parsed = GlbModelLoader.Load(slotName, fullPath, sampleDeployEndFrame);
            if (parsed == null || parsed.SubMeshes.Count == 0)
            {
                return Array.Empty<(Mesh, Material)>();
            }

            var cfg = new ModelSlotEntry
            {
                TargetMaxDimensionMeters = targetMaxMeters,
                AutoGround = true,
                SmoothnessOverride = null,
            };

            CompiledModel compiled = CompileModel(parsed, cfg);
            ExternalPreviewCompiled[cacheKey] = compiled;
            return ToPreviewList(compiled);
        }
        catch (Exception ex)
        {
            AddonLog.Warn($"[Preview] Failed to compile external GLB '{fullPath}': {ex.Message}");
            return Array.Empty<(Mesh, Material)>();
        }
    }

    private static List<(Mesh Mesh, Material Material)> ToPreviewList(CompiledModel compiled)
    {
        var list = new List<(Mesh, Material)>(compiled.Parts.Count);
        for (int i = 0; i < compiled.Parts.Count; i++)
        {
            CompiledSubMesh part = compiled.Parts[i];
            if (part.Mesh != null && part.Material != null)
            {
                list.Add((part.Mesh, part.Material));
            }
        }

        return list;
    }

    internal static string ResolveSlotForLevel(string baseSlotId, int level, out bool isExactLevelModel)
    {
        int clampedLevel = Mathf.Clamp(level, 1, 3);
        if (clampedLevel == 3)
        {
            if (TryFindDiskSlot(baseSlotId + "_lvl3", baseSlotId + "_l3", out string exactL3))
            {
                isExactLevelModel = true;
                return exactL3;
            }

            isExactLevelModel = false;
            if (TryFindDiskSlot(baseSlotId + "_lvl2", baseSlotId + "_l2", out string fallbackL2))
            {
                return fallbackL2;
            }

            if (TryFindDiskSlot(baseSlotId + "_lvl1", baseSlotId + "_l1", baseSlotId, out string fallbackBase))
            {
                return fallbackBase;
            }

            return baseSlotId;
        }

        if (clampedLevel == 2)
        {
            if (TryFindDiskSlot(baseSlotId + "_lvl2", baseSlotId + "_l2", out string exactL2))
            {
                isExactLevelModel = true;
                return exactL2;
            }

            isExactLevelModel = false;
            if (TryFindDiskSlot(baseSlotId + "_lvl1", baseSlotId + "_l1", baseSlotId, out string fallbackBase))
            {
                return fallbackBase;
            }

            return baseSlotId;
        }

        isExactLevelModel = true;
        return TryFindDiskSlot(baseSlotId + "_lvl1", baseSlotId + "_l1", baseSlotId, out string foundL1)
            ? foundL1
            : baseSlotId;
    }

    private static bool TryFindDiskSlot(string cand1, string cand2, out string foundSlot)
    {
        if (IsDiskLoadedSlot(cand1))
        {
            foundSlot = cand1;
            return true;
        }

        if (IsDiskLoadedSlot(cand2))
        {
            foundSlot = cand2;
            return true;
        }

        foundSlot = string.Empty;
        return false;
    }

    private static bool TryFindDiskSlot(string cand1, string cand2, string cand3, out string foundSlot)
    {
        if (TryFindDiskSlot(cand1, cand2, out foundSlot))
        {
            return true;
        }

        if (IsDiskLoadedSlot(cand3))
        {
            foundSlot = cand3;
            return true;
        }

        foundSlot = string.Empty;
        return false;
    }

    private static bool IsDiskLoadedSlot(string slotId)
        => ParsedAssets.TryGetValue(slotId, out ParsedModelAsset? asset)
           && asset.SourceKind != ModelSourceKind.Fallback;

    private static void DiscoverLevelVariantSlots(ModelManifest manifest, StringBuilder report)
    {
        if (!AddonConfig.UseCustomModels.Value || string.IsNullOrEmpty(modelsDirectory))
        {
            return;
        }

        var baseKeys = new List<string>(manifest.Slots.Keys);
        for (int i = 0; i < ModelManifest.DefaultSlotOrder.Length; i++)
        {
            string s = ModelManifest.DefaultSlotOrder[i];
            if (!baseKeys.Exists(k => string.Equals(k, s, StringComparison.OrdinalIgnoreCase)))
            {
                baseKeys.Add(s);
            }
        }

        string[] suffixes = { "_lvl1", "_lvl2", "_lvl3", "_l1", "_l2", "_l3" };

        for (int i = 0; i < baseKeys.Count; i++)
        {
            string baseKey = baseKeys[i];
            bool isAlreadyLevelVariant = false;
            for (int s = 0; s < suffixes.Length; s++)
            {
                if (baseKey.EndsWith(suffixes[s], StringComparison.OrdinalIgnoreCase))
                {
                    isAlreadyLevelVariant = true;
                    break;
                }
            }

            if (isAlreadyLevelVariant || !SlotConfigs.TryGetValue(baseKey, out ModelSlotEntry? baseCfg))
            {
                continue;
            }

            for (int lvl = 1; lvl <= 3; lvl++)
            {
                string[] lvlCandidates = { $"{baseKey}_lvl{lvl}", $"{baseKey}_l{lvl}" };
                for (int c = 0; c < lvlCandidates.Length; c++)
                {
                    string variantKey = lvlCandidates[c];
                    if (ParsedAssets.ContainsKey(variantKey))
                    {
                        continue;
                    }

                    string glbPath = Path.Combine(modelsDirectory, variantKey + ".glb");
                    string objPath = Path.Combine(modelsDirectory, variantKey + ".obj");
                    if (!File.Exists(glbPath) && !File.Exists(objPath))
                    {
                        continue;
                    }

                    // Keep identical concrete foundation pad across Levels 1-3 (scale 1.0x).
                    var derivedCfg = new ModelSlotEntry
                    {
                        File = null,
                        TargetMaxDimensionMeters = baseCfg.TargetMaxDimensionMeters,
                        ScaleMultiplier = baseCfg.ScaleMultiplier,
                        RotationEuler = (float[])baseCfg.RotationEuler.Clone(),
                        PositionOffset = (float[])baseCfg.PositionOffset.Clone(),
                        AutoGround = baseCfg.AutoGround,
                        TintHex = baseCfg.TintHex,
                        SmoothnessOverride = baseCfg.SmoothnessOverride,
                    };

                    ParsedModelAsset? parsed = TryLoadSlotFromDisk(variantKey, derivedCfg);
                    if (parsed == null)
                    {
                        continue;
                    }

                    SlotConfigs[variantKey] = derivedCfg;
                    ParsedAssets[variantKey] = parsed;

                    Bounds scaledBounds = ComputeScaledBounds(parsed.RawBounds, derivedCfg);
                    report.AppendLine(
                        $"  [{variantKey}] {parsed.SourceKind.ToString().ToUpperInvariant()} " +
                        $"file='{parsed.FileName}' meshes={parsed.SubMeshes.Count} " +
                        $"verts={parsed.TotalVertices} tris={parsed.TotalTriangles} " +
                        $"size=({scaledBounds.size.x:0.0}m x {scaledBounds.size.y:0.0}m x {scaledBounds.size.z:0.0}m)");
                }
            }
        }
    }

    internal static bool TryGetSlotSummary(string slotId, out string summary)
    {
        if (ParsedAssets.TryGetValue(slotId, out ParsedModelAsset? asset))
        {
            summary = $"{asset.SourceKind}:{asset.FileName} ({asset.TotalTriangles} tris)";
            return true;
        }

        summary = string.Empty;
        return false;
    }

    private static ModelManifest LoadOrCreateManifest(string manifestPath)
    {
        if (File.Exists(manifestPath))
        {
            try
            {
                string json = File.ReadAllText(manifestPath);
                ModelManifest? loaded = JsonConvert.DeserializeObject<ModelManifest>(json);
                if (loaded != null && loaded.Slots.Count > 0)
                {
                    return loaded;
                }
            }
            catch (Exception ex)
            {
                AddonLog.Warn($"Failed to parse '{manifestPath}', using defaults: {ex.Message}");
            }
        }

        ModelManifest defaults = ModelManifest.CreateDefault();
        try
        {
            File.WriteAllText(manifestPath, JsonConvert.SerializeObject(defaults, Formatting.Indented));
        }
        catch (Exception ex)
        {
            AddonLog.Warn($"Could not write default models.json: {ex.Message}");
        }

        return defaults;
    }

    private static ParsedModelAsset? TryLoadSlotFromDisk(string slotId, ModelSlotEntry cfg)
    {
        string[] candidates =
        {
            !string.IsNullOrWhiteSpace(cfg.File) ? Path.Combine(modelsDirectory, cfg.File!) : string.Empty,
            Path.Combine(modelsDirectory, slotId + ".glb"),
            Path.Combine(modelsDirectory, slotId + ".obj"),
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            string path = candidates[i];
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                continue;
            }

            try
            {
                string ext = Path.GetExtension(path);
                ParsedModelAsset? loaded = ext.Equals(".glb", StringComparison.OrdinalIgnoreCase)
                    ? GlbModelLoader.Load(slotId, path)
                    : ext.Equals(".obj", StringComparison.OrdinalIgnoreCase)
                        ? ObjModelLoader.Load(slotId, path)
                        : null;
                if (loaded != null)
                {
                    return loaded;
                }
            }
            catch (Exception ex)
            {
                AddonLog.Warn($"Error loading model '{path}' for slot '{slotId}': {ex.Message}");
            }
        }

        return null;
    }

    private static CompiledModel GetOrCompile(string slotId)
    {
        if (RuntimeCompiled.TryGetValue(slotId, out CompiledModel? existing))
        {
            return existing;
        }

        if (!ParsedAssets.TryGetValue(slotId, out ParsedModelAsset? parsed))
        {
            parsed = ProceduralFallbackModels.Build(slotId);
            ParsedAssets[slotId] = parsed;
        }

        if (!SlotConfigs.TryGetValue(slotId, out ModelSlotEntry? cfg))
        {
            cfg = new ModelSlotEntry();
            SlotConfigs[slotId] = cfg;
        }

        CompiledModel compiled = CompileModel(parsed, cfg);
        RuntimeCompiled[slotId] = compiled;
        return compiled;
    }

    private static CompiledModel CompileModel(ParsedModelAsset parsed, ModelSlotEntry cfg)
    {
        Quaternion rot = cfg.RotationEuler != null && cfg.RotationEuler.Length >= 3
            ? Quaternion.Euler(cfg.RotationEuler[0], cfg.RotationEuler[1], cfg.RotationEuler[2])
            : Quaternion.identity;

        Vector3 offset = cfg.PositionOffset != null && cfg.PositionOffset.Length >= 3
            ? new Vector3(cfg.PositionOffset[0], cfg.PositionOffset[1], cfg.PositionOffset[2])
            : Vector3.zero;

        // First compute rotated raw bounds across all submeshes to derive uniform normalization scale.
        var rotatedBounds = new Bounds();
        bool hasRotated = false;

        for (int s = 0; s < parsed.SubMeshes.Count; s++)
        {
            ParsedSubMesh sub = parsed.SubMeshes[s];
            for (int v = 0; v < sub.Vertices.Length; v++)
            {
                Vector3 p = rot * sub.LocalTransform.MultiplyPoint3x4(sub.Vertices[v]);
                if (!hasRotated)
                {
                    rotatedBounds = new Bounds(p, Vector3.zero);
                    hasRotated = true;
                }
                else
                {
                    rotatedBounds.Encapsulate(p);
                }
            }
        }

        float scaleMul = Mathf.Max(0.05f, cfg.ScaleMultiplier);
        float footprintMax = hasRotated ? Mathf.Max(rotatedBounds.size.x, rotatedBounds.size.z) : 1f;
        if (hasRotated && footprintMax <= 0.0001f)
        {
            footprintMax = rotatedBounds.size.y;
        }

        float uniformScale;
        if (cfg.TargetMaxDimensionMeters <= 0f)
        {
            // Native 1:1 Blender meter scaling when targetMaxDimensionMeters <= 0.
            uniformScale = scaleMul;
        }
        else
        {
            float targetDim = Mathf.Max(0.1f, cfg.TargetMaxDimensionMeters) * scaleMul;
            uniformScale = footprintMax > 0.0001f ? targetDim / footprintMax : 1f;
        }

        Vector3 centerXZ = hasRotated ? new Vector3(rotatedBounds.center.x, 0f, rotatedBounds.center.z) : Vector3.zero;
        float minY = hasRotated ? rotatedBounds.min.y : 0f;

        Color? tintOverride = UrpMaterialFactory.TryParseHexColor(cfg.TintHex, out Color parsedTint)
            ? parsedTint
            : null;

        var compiled = new CompiledModel();
        var normBounds = new Bounds();
        bool hasNorm = false;

        for (int s = 0; s < parsed.SubMeshes.Count; s++)
        {
            ParsedSubMesh sub = parsed.SubMeshes[s];
            int vertCount = sub.Vertices.Length;
            var verts = new Vector3[vertCount];

            Matrix4x4 local = sub.LocalTransform;
            float det = Vector3.Dot(
                Vector3.Cross(local.GetColumn(0), local.GetColumn(1)),
                local.GetColumn(2));
            bool flipWinding = det < 0f;

            for (int v = 0; v < vertCount; v++)
            {
                Vector3 p = rot * local.MultiplyPoint3x4(sub.Vertices[v]);
                p -= centerXZ;
                if (cfg.AutoGround)
                {
                    p.y -= minY;
                }

                p = (p * uniformScale) + offset;
                verts[v] = p;

                if (!hasNorm)
                {
                    normBounds = new Bounds(p, Vector3.zero);
                    hasNorm = true;
                }
                else
                {
                    normBounds.Encapsulate(p);
                }
            }

            int[] tris = (int[])sub.Triangles.Clone();
            if (flipWinding)
            {
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    int tmp = tris[i + 1];
                    tris[i + 1] = tris[i + 2];
                    tris[i + 2] = tmp;
                }
            }

            var mesh = new Mesh
            {
                name = sub.Name,
                hideFlags = HideFlags.DontUnloadUnusedAsset,
                indexFormat = vertCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                vertices = verts,
                triangles = tris,
            };

            if (sub.Uvs.Length == vertCount)
            {
                mesh.uv = sub.Uvs;
            }

            if (sub.Colors.Length == vertCount)
            {
                mesh.colors32 = sub.Colors;
            }

            if (sub.Normals.Length == vertCount)
            {
                Matrix4x4 normalMatrix = local.inverse.transpose;
                var norms = new Vector3[vertCount];
                for (int v = 0; v < vertCount; v++)
                {
                    norms[v] = (rot * normalMatrix.MultiplyVector(sub.Normals[v])).normalized;
                }

                mesh.normals = norms;
            }
            else
            {
                mesh.RecalculateNormals();
            }

            mesh.RecalculateBounds();

            Texture2D? tex = null;
            if (sub.Material.BaseColorTextureBytes != null && sub.Material.BaseColorTextureBytes.Length > 0)
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true)
                {
                    name = $"{sub.Name}_tex",
                    wrapMode = TextureWrapMode.Repeat,
                    anisoLevel = 4,
                    hideFlags = HideFlags.DontUnloadUnusedAsset,
                };
                if (!tex.LoadImage(sub.Material.BaseColorTextureBytes, markNonReadable: true))
                {
                    UnityEngine.Object.Destroy(tex);
                    tex = null;
                }
                else
                {
                    compiled.Textures.Add(tex);
                }
            }

            float smoothness = cfg.SmoothnessOverride ?? sub.Material.Smoothness;
            Material mat = UrpMaterialFactory.CreateMaterial(
                $"{parsed.SlotId}_{sub.Material.Name}",
                sub.Material.BaseColor,
                tex,
                sub.Material.Metallic,
                smoothness,
                tintOverride);

            compiled.Parts.Add(new CompiledSubMesh
            {
                Name = sub.Name,
                Mesh = mesh,
                Material = mat,
            });
        }

        compiled.NormalizedBounds = normBounds;
        return compiled;
    }

    private static Bounds ComputeScaledBounds(Bounds rawBounds, ModelSlotEntry cfg)
    {
        float scaleMul = Mathf.Max(0.05f, cfg.ScaleMultiplier);
        if (cfg.TargetMaxDimensionMeters <= 0f)
        {
            return new Bounds(rawBounds.center * scaleMul, rawBounds.size * scaleMul);
        }

        float footprintMax = Mathf.Max(rawBounds.size.x, rawBounds.size.z);
        if (footprintMax <= 0.0001f)
        {
            footprintMax = rawBounds.size.y;
        }

        if (footprintMax <= 0.0001f)
        {
            return new Bounds(Vector3.zero, Vector3.one * cfg.TargetMaxDimensionMeters);
        }

        float scale = Mathf.Max(0.1f, cfg.TargetMaxDimensionMeters) * scaleMul / footprintMax;
        return new Bounds(rawBounds.center * scale, rawBounds.size * scale);
    }
}
