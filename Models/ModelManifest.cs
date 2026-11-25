using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal enum ModelSourceKind
{
    Fallback = 0,
    Obj = 1,
    Glb = 2,
}

internal sealed class ParsedMaterialData
{
    internal string Name { get; set; } = "default";
    internal Color BaseColor { get; set; } = Color.white;
    internal float Metallic { get; set; }
    internal float Smoothness { get; set; } = 0.25f;
    internal byte[]? BaseColorTextureBytes { get; set; }
}

internal sealed class ParsedSubMesh
{
    internal string Name { get; set; } = "part";
    internal Vector3[] Vertices { get; set; } = Array.Empty<Vector3>();
    internal Vector3[] Normals { get; set; } = Array.Empty<Vector3>();
    internal Vector2[] Uvs { get; set; } = Array.Empty<Vector2>();
    internal Color32[] Colors { get; set; } = Array.Empty<Color32>();
    internal int[] Triangles { get; set; } = Array.Empty<int>();
    internal Matrix4x4 LocalTransform { get; set; } = Matrix4x4.identity;
    internal ParsedMaterialData Material { get; set; } = new();
}

internal sealed class ParsedModelAsset
{
    internal string SlotId { get; set; } = string.Empty;
    internal string FileName { get; set; } = string.Empty;
    internal ModelSourceKind SourceKind { get; set; }
    internal List<ParsedSubMesh> SubMeshes { get; } = new();
    internal Bounds RawBounds { get; set; }
    internal int TotalVertices { get; set; }
    internal int TotalTriangles { get; set; }
}

internal sealed class ModelSlotEntry
{
    [JsonProperty("file")]
    public string? File { get; set; }

    [JsonProperty("targetMaxDimensionMeters")]
    public float TargetMaxDimensionMeters { get; set; } = 20f;

    [JsonProperty("scaleMultiplier")]
    public float ScaleMultiplier { get; set; } = 1f;

    [JsonProperty("rotationEuler")]
    public float[] RotationEuler { get; set; } = { 0f, 0f, 0f };

    [JsonProperty("positionOffset")]
    public float[] PositionOffset { get; set; } = { 0f, 0f, 0f };

    [JsonProperty("autoGround")]
    public bool AutoGround { get; set; } = true;

    [JsonProperty("tintHex")]
    public string? TintHex { get; set; }

    [JsonProperty("smoothnessOverride")]
    public float? SmoothnessOverride { get; set; }
}

internal sealed class ModelManifest
{
    [JsonProperty("slots")]
    public Dictionary<string, ModelSlotEntry> Slots { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    internal static readonly string[] DefaultSlotOrder =
    {
        "command_hq",
        "barracks",
        "pilot_academy",
        "logistics_camp",
        "naval_command",
        "abm_radar",
        "abm_silo",
        "abm_plant",
        "sam_kit",
    };

    internal static ModelManifest CreateDefault()
    {
        var manifest = new ModelManifest();
        manifest.Slots["command_hq"] = new ModelSlotEntry
        {
            File = "command_hq.glb",
            TargetMaxDimensionMeters = 28f,
            ScaleMultiplier = 1f,
            AutoGround = true,
            TintHex = "#8C9484",
            SmoothnessOverride = 0.25f,
        };
        manifest.Slots["barracks"] = new ModelSlotEntry
        {
            File = "barracks.glb",
            TargetMaxDimensionMeters = 24f,
            ScaleMultiplier = 1f,
            AutoGround = true,
            TintHex = "#8F937F",
            SmoothnessOverride = 0.22f,
        };
        manifest.Slots["pilot_academy"] = new ModelSlotEntry
        {
            File = "pilot_academy.glb",
            TargetMaxDimensionMeters = 30f,
            ScaleMultiplier = 1f,
            AutoGround = true,
            TintHex = "#8C98A0",
            SmoothnessOverride = 0.30f,
        };
        manifest.Slots["logistics_camp"] = new ModelSlotEntry
        {
            File = "logistics_camp.glb",
            TargetMaxDimensionMeters = 26f,
            ScaleMultiplier = 1f,
            AutoGround = true,
            TintHex = "#95927E",
            SmoothnessOverride = 0.22f,
        };
        manifest.Slots["naval_command"] = new ModelSlotEntry
        {
            File = "naval_command.glb",
            TargetMaxDimensionMeters = 26f,
            ScaleMultiplier = 1f,
            AutoGround = true,
            TintHex = "#849099",
            SmoothnessOverride = 0.28f,
        };
        manifest.Slots["abm_radar"] = new ModelSlotEntry
        {
            File = "abm_radar.glb",
            TargetMaxDimensionMeters = 22f,
            ScaleMultiplier = 1f,
            AutoGround = true,
            TintHex = "#929894",
            SmoothnessOverride = 0.32f,
        };
        manifest.Slots["abm_silo"] = new ModelSlotEntry
        {
            File = "abm_silo.glb",
            TargetMaxDimensionMeters = 20f,
            ScaleMultiplier = 1f,
            AutoGround = true,
            TintHex = "#898E88",
            SmoothnessOverride = 0.25f,
        };
        manifest.Slots["abm_plant"] = new ModelSlotEntry
        {
            File = "abm_plant.glb",
            TargetMaxDimensionMeters = 32f,
            ScaleMultiplier = 1f,
            AutoGround = true,
            TintHex = "#8E9187",
            SmoothnessOverride = 0.24f,
        };
        manifest.Slots["sam_kit"] = new ModelSlotEntry
        {
            File = "sam_kit.glb",
            TargetMaxDimensionMeters = 6.5f,
            ScaleMultiplier = 1f,
            AutoGround = true,
            TintHex = "#838D74",
            SmoothnessOverride = 0.26f,
        };
        return manifest;
    }
}
