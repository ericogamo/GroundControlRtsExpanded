using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal static class ObjModelLoader
{
    private sealed class GroupBucket
    {
        internal readonly string MaterialName;
        internal readonly List<Vector3> Vertices = new();
        internal readonly List<Vector3> Normals = new();
        internal readonly List<Vector2> Uvs = new();
        internal readonly List<int> Triangles = new();
        internal readonly Dictionary<(int V, int Vt, int Vn), int> IndexMap = new();

        internal GroupBucket(string materialName)
        {
            MaterialName = materialName;
        }
    }

    internal static ParsedModelAsset? Load(string slotId, string objPath)
    {
        if (!File.Exists(objPath))
        {
            return null;
        }

        string directory = Path.GetDirectoryName(objPath) ?? string.Empty;
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var materials = new Dictionary<string, ParsedMaterialData>(StringComparer.OrdinalIgnoreCase);
        var buckets = new Dictionary<string, GroupBucket>(StringComparer.OrdinalIgnoreCase);

        string activeMat = "default";
        GroupBucket currentBucket = GetOrCreateBucket(buckets, activeMat);

        foreach (string rawLine in File.ReadLines(objPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line.StartsWith("mtllib ", StringComparison.OrdinalIgnoreCase))
            {
                string mtlRel = line.Substring(7).Trim();
                string mtlPath = Path.Combine(directory, mtlRel);
                LoadMtl(mtlPath, directory, materials);
                continue;
            }

            if (line.StartsWith("usemtl ", StringComparison.OrdinalIgnoreCase))
            {
                activeMat = line.Substring(7).Trim();
                if (string.IsNullOrEmpty(activeMat))
                {
                    activeMat = "default";
                }

                currentBucket = GetOrCreateBucket(buckets, activeMat);
                continue;
            }

            if (line.StartsWith("v ", StringComparison.Ordinal))
            {
                string[] parts = SplitWhitespace(line, 4);
                if (parts.Length >= 4
                    && TryFloat(parts[1], out float x)
                    && TryFloat(parts[2], out float y)
                    && TryFloat(parts[3], out float z))
                {
                    // Flip X to convert standard right-handed OBJ to Unity left-handed space.
                    positions.Add(new Vector3(-x, y, z));
                }

                continue;
            }

            if (line.StartsWith("vn ", StringComparison.Ordinal))
            {
                string[] parts = SplitWhitespace(line, 4);
                if (parts.Length >= 4
                    && TryFloat(parts[1], out float nx)
                    && TryFloat(parts[2], out float ny)
                    && TryFloat(parts[3], out float nz))
                {
                    normals.Add(new Vector3(-nx, ny, nz).normalized);
                }

                continue;
            }

            if (line.StartsWith("vt ", StringComparison.Ordinal))
            {
                string[] parts = SplitWhitespace(line, 3);
                if (parts.Length >= 3
                    && TryFloat(parts[1], out float u)
                    && TryFloat(parts[2], out float v))
                {
                    uvs.Add(new Vector2(u, v));
                }

                continue;
            }

            if (line.StartsWith("f ", StringComparison.Ordinal))
            {
                string[] parts = SplitWhitespace(line, 4);
                if (parts.Length < 4)
                {
                    continue;
                }

                int cornerCount = parts.Length - 1;
                int[] faceIndices = new int[cornerCount];
                for (int i = 0; i < cornerCount; i++)
                {
                    faceIndices[i] = ResolveCorner(
                        parts[i + 1],
                        positions,
                        uvs,
                        normals,
                        currentBucket);
                }

                // Fan-triangulate polygon with reversed winding due to X flip.
                for (int i = 1; i + 1 < cornerCount; i++)
                {
                    int i0 = faceIndices[0];
                    int i1 = faceIndices[i];
                    int i2 = faceIndices[i + 1];
                    if (i0 >= 0 && i1 >= 0 && i2 >= 0)
                    {
                        currentBucket.Triangles.Add(i0);
                        currentBucket.Triangles.Add(i2);
                        currentBucket.Triangles.Add(i1);
                    }
                }
            }
        }

        var asset = new ParsedModelAsset
        {
            SlotId = slotId,
            FileName = Path.GetFileName(objPath),
            SourceKind = ModelSourceKind.Obj,
        };

        var bounds = new Bounds();
        bool hasBounds = false;

        foreach (GroupBucket bucket in buckets.Values)
        {
            if (bucket.Triangles.Count == 0 || bucket.Vertices.Count == 0)
            {
                continue;
            }

            Vector3[] verts = bucket.Vertices.ToArray();
            Vector3[] norms = bucket.Normals.Count == verts.Length
                ? bucket.Normals.ToArray()
                : Array.Empty<Vector3>();
            Vector2[] texCoords = bucket.Uvs.Count == verts.Length
                ? bucket.Uvs.ToArray()
                : Array.Empty<Vector2>();

            if (!materials.TryGetValue(bucket.MaterialName, out ParsedMaterialData? matData))
            {
                matData = new ParsedMaterialData
                {
                    Name = bucket.MaterialName,
                    BaseColor = new Color(0.75f, 0.77f, 0.74f, 1f),
                    Smoothness = 0.25f,
                };
            }

            for (int i = 0; i < verts.Length; i++)
            {
                if (!hasBounds)
                {
                    bounds = new Bounds(verts[i], Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(verts[i]);
                }
            }

            asset.SubMeshes.Add(new ParsedSubMesh
            {
                Name = $"{slotId}_{bucket.MaterialName}",
                Vertices = verts,
                Normals = norms,
                Uvs = texCoords,
                Triangles = bucket.Triangles.ToArray(),
                LocalTransform = Matrix4x4.identity,
                Material = matData,
            });
            asset.TotalVertices += verts.Length;
            asset.TotalTriangles += bucket.Triangles.Count / 3;
        }

        if (asset.SubMeshes.Count == 0)
        {
            AddonLog.Warn($"OBJ '{objPath}' had no valid geometry.");
            return null;
        }

        asset.RawBounds = bounds;
        return asset;
    }

    private static GroupBucket GetOrCreateBucket(
        Dictionary<string, GroupBucket> buckets,
        string materialName)
    {
        if (!buckets.TryGetValue(materialName, out GroupBucket? bucket))
        {
            bucket = new GroupBucket(materialName);
            buckets[materialName] = bucket;
        }

        return bucket;
    }

    private static int ResolveCorner(
        string token,
        List<Vector3> positions,
        List<Vector2> uvs,
        List<Vector3> normals,
        GroupBucket bucket)
    {
        string[] segs = token.Split('/');
        int vIdx = ParseIndex(segs[0], positions.Count);
        if (vIdx < 0 || vIdx >= positions.Count)
        {
            return -1;
        }

        int vtIdx = segs.Length > 1 && segs[1].Length > 0 ? ParseIndex(segs[1], uvs.Count) : -1;
        int vnIdx = segs.Length > 2 && segs[2].Length > 0 ? ParseIndex(segs[2], normals.Count) : -1;

        var key = (vIdx, vtIdx, vnIdx);
        if (bucket.IndexMap.TryGetValue(key, out int existing))
        {
            return existing;
        }

        int idx = bucket.Vertices.Count;
        bucket.Vertices.Add(positions[vIdx]);
        bucket.Uvs.Add(vtIdx >= 0 && vtIdx < uvs.Count ? uvs[vtIdx] : Vector2.zero);
        if (vnIdx >= 0 && vnIdx < normals.Count)
        {
            bucket.Normals.Add(normals[vnIdx]);
        }

        bucket.IndexMap[key] = idx;
        return idx;
    }

    private static int ParseIndex(string text, int count)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int raw) || raw == 0)
        {
            return -1;
        }

        return raw > 0 ? raw - 1 : count + raw;
    }

    private static void LoadMtl(
        string mtlPath,
        string baseDirectory,
        Dictionary<string, ParsedMaterialData> materials)
    {
        if (!File.Exists(mtlPath))
        {
            return;
        }

        ParsedMaterialData? current = null;
        foreach (string rawLine in File.ReadLines(mtlPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line.StartsWith("newmtl ", StringComparison.OrdinalIgnoreCase))
            {
                string name = line.Substring(7).Trim();
                current = new ParsedMaterialData { Name = name };
                materials[name] = current;
                continue;
            }

            if (current == null)
            {
                continue;
            }

            if (line.StartsWith("Kd ", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = SplitWhitespace(line, 4);
                if (parts.Length >= 4
                    && TryFloat(parts[1], out float r)
                    && TryFloat(parts[2], out float g)
                    && TryFloat(parts[3], out float b))
                {
                    current.BaseColor = new Color(r, g, b, current.BaseColor.a);
                }
            }
            else if (line.StartsWith("Ns ", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = SplitWhitespace(line, 2);
                if (parts.Length >= 2 && TryFloat(parts[1], out float ns))
                {
                    current.Smoothness = Mathf.Clamp01(ns / 250f);
                }
            }
            else if (line.StartsWith("map_Kd ", StringComparison.OrdinalIgnoreCase))
            {
                string texRel = line.Substring(7).Trim();
                string texPath = Path.IsPathRooted(texRel)
                    ? texRel
                    : Path.Combine(baseDirectory, texRel);
                if (File.Exists(texPath))
                {
                    current.BaseColorTextureBytes = File.ReadAllBytes(texPath);
                }
            }
        }
    }

    private static string[] SplitWhitespace(string line, int expectedMax)
    {
        return line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool TryFloat(string s, out float value)
    {
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
