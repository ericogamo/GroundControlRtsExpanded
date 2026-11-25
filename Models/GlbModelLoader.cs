using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal static class GlbModelLoader
{
    private const uint GlbMagic = 0x46546C67; // "glTF"
    private const uint ChunkTypeJson = 0x4E4F534A; // "JSON"
    private const uint ChunkTypeBin = 0x004E4942; // "BIN\0"

    private sealed class NodeTransformOverride
    {
        internal Vector3? Translation;
        internal Quaternion? Rotation;
        internal Vector3? Scale;
        internal bool HasAny => Translation.HasValue || Rotation.HasValue || Scale.HasValue;
    }

    internal static ParsedModelAsset? Load(string slotId, string glbPath, bool sampleDeployEndFrame = false)
    {
        if (!File.Exists(glbPath))
        {
            return null;
        }

        byte[] fileBytes = File.ReadAllBytes(glbPath);
        if (fileBytes.Length < 20)
        {
            AddonLog.Warn($"GLB '{glbPath}' is too small ({fileBytes.Length} bytes).");
            return null;
        }

        using var ms = new MemoryStream(fileBytes, writable: false);
        using var reader = new BinaryReader(ms);

        uint magic = reader.ReadUInt32();
        uint version = reader.ReadUInt32();
        uint totalLength = reader.ReadUInt32();

        if (magic != GlbMagic || version != 2)
        {
            AddonLog.Warn($"GLB '{glbPath}' has unsupported header (magic=0x{magic:X8}, version={version}).");
            return null;
        }

        string? jsonText = null;
        byte[]? binChunk = null;

        long endPos = Math.Min(fileBytes.Length, (long)totalLength);
        while (ms.Position + 8 <= endPos)
        {
            uint chunkLength = reader.ReadUInt32();
            uint chunkType = reader.ReadUInt32();
            long remaining = endPos - ms.Position;
            if (chunkLength > remaining)
            {
                break;
            }

            byte[] chunkData = reader.ReadBytes((int)chunkLength);
            if (chunkType == ChunkTypeJson && jsonText == null)
            {
                jsonText = Encoding.UTF8.GetString(chunkData);
            }
            else if (chunkType == ChunkTypeBin && binChunk == null)
            {
                binChunk = chunkData;
            }
        }

        if (string.IsNullOrEmpty(jsonText) || binChunk == null)
        {
            AddonLog.Warn($"GLB '{glbPath}' is missing JSON or BIN chunk.");
            return null;
        }

        JObject root = JObject.Parse(jsonText!);
        JArray? extensionsRequired = root["extensionsRequired"] as JArray;
        if (extensionsRequired != null)
        {
            foreach (JToken ext in extensionsRequired)
            {
                if (string.Equals(ext.Value<string>(), "KHR_draco_mesh_compression", StringComparison.OrdinalIgnoreCase))
                {
                    AddonLog.Warn(
                        $"GLB '{Path.GetFileName(glbPath)}' uses Draco compression (unsupported at runtime). " +
                        "Re-export from Blender with Draco disabled, or use OBJ.");
                    return null;
                }
            }
        }

        JArray? nodes = root["nodes"] as JArray;
        JArray? meshes = root["meshes"] as JArray;
        JArray? accessors = root["accessors"] as JArray;
        JArray? bufferViews = root["bufferViews"] as JArray;
        JArray? materials = root["materials"] as JArray;
        JArray? textures = root["textures"] as JArray;
        JArray? images = root["images"] as JArray;

        if (meshes == null || accessors == null || bufferViews == null)
        {
            AddonLog.Warn($"GLB '{glbPath}' contains no mesh/accessor data.");
            return null;
        }

        var parsedMaterials = ParseMaterials(materials, textures, images, bufferViews, binChunk);

        var asset = new ParsedModelAsset
        {
            SlotId = slotId,
            FileName = Path.GetFileName(glbPath),
            SourceKind = ModelSourceKind.Glb,
        };

        Dictionary<int, NodeTransformOverride>? animOverrides = sampleDeployEndFrame
            ? ParseDeployEndFrameOverrides(root, accessors, bufferViews, binChunk)
            : null;

        JArray? scenes = root["scenes"] as JArray;
        int sceneIndex = root["scene"]?.Value<int>() ?? 0;

        if (scenes != null && sceneIndex >= 0 && sceneIndex < scenes.Count && nodes != null)
        {
            JArray? rootNodes = scenes[sceneIndex]?["nodes"] as JArray;
            if (rootNodes != null)
            {
                foreach (JToken nodeIdxToken in rootNodes)
                {
                    int nodeIdx = nodeIdxToken.Value<int>();
                    TraverseNode(
                        nodeIdx,
                        Matrix4x4.identity,
                        nodes,
                        meshes,
                        accessors,
                        bufferViews,
                        binChunk,
                        parsedMaterials,
                        animOverrides,
                        asset);
                }
            }
        }
        else
        {
            for (int m = 0; m < meshes.Count; m++)
            {
                ExtractMeshPrimitives(
                    m,
                    Matrix4x4.identity,
                    meshes,
                    accessors,
                    bufferViews,
                    binChunk,
                    parsedMaterials,
                    asset);
            }
        }

        if (asset.SubMeshes.Count == 0)
        {
            AddonLog.Warn($"GLB '{glbPath}' yielded zero primitives.");
            return null;
        }

        var bounds = new Bounds();
        bool hasBounds = false;
        for (int s = 0; s < asset.SubMeshes.Count; s++)
        {
            ParsedSubMesh sub = asset.SubMeshes[s];
            Matrix4x4 xform = sub.LocalTransform;
            for (int v = 0; v < sub.Vertices.Length; v++)
            {
                Vector3 world = xform.MultiplyPoint3x4(sub.Vertices[v]);
                if (!hasBounds)
                {
                    bounds = new Bounds(world, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(world);
                }
            }
        }

        asset.RawBounds = bounds;
        return asset;
    }

    private static List<ParsedMaterialData> ParseMaterials(
        JArray? materials,
        JArray? textures,
        JArray? images,
        JArray bufferViews,
        byte[] binChunk)
    {
        var list = new List<ParsedMaterialData>();
        if (materials == null)
        {
            return list;
        }

        for (int i = 0; i < materials.Count; i++)
        {
            JObject? matObj = materials[i] as JObject;
            var mat = new ParsedMaterialData
            {
                Name = matObj?["name"]?.Value<string>() ?? $"mat_{i}",
                BaseColor = Color.white,
                Metallic = 0f,
                Smoothness = 0.25f,
            };

            JObject? pbr = matObj?["pbrMetallicRoughness"] as JObject;
            if (pbr != null)
            {
                if (pbr["baseColorFactor"] is JArray bcf && bcf.Count >= 4)
                {
                    mat.BaseColor = new Color(
                        bcf[0].Value<float>(),
                        bcf[1].Value<float>(),
                        bcf[2].Value<float>(),
                        bcf[3].Value<float>());
                }

                if (pbr["metallicFactor"] != null)
                {
                    mat.Metallic = Mathf.Clamp01(pbr["metallicFactor"]!.Value<float>());
                }

                if (pbr["roughnessFactor"] != null)
                {
                    float roughness = Mathf.Clamp01(pbr["roughnessFactor"]!.Value<float>());
                    mat.Smoothness = 1f - roughness;
                }

                int? texIndex = pbr["baseColorTexture"]?["index"]?.Value<int?>();
                if (texIndex.HasValue && textures != null && images != null
                    && texIndex.Value >= 0 && texIndex.Value < textures.Count)
                {
                    int? imgIndex = textures[texIndex.Value]?["source"]?.Value<int?>();
                    if (imgIndex.HasValue && imgIndex.Value >= 0 && imgIndex.Value < images.Count)
                    {
                        int? bvIndex = images[imgIndex.Value]?["bufferView"]?.Value<int?>();
                        if (bvIndex.HasValue && bvIndex.Value >= 0 && bvIndex.Value < bufferViews.Count)
                        {
                            JObject? bv = bufferViews[bvIndex.Value] as JObject;
                            if (bv != null)
                            {
                                int offset = bv["byteOffset"]?.Value<int>() ?? 0;
                                int length = bv["byteLength"]?.Value<int>() ?? 0;
                                if (offset >= 0 && length > 0 && offset + length <= binChunk.Length)
                                {
                                    byte[] imgBytes = new byte[length];
                                    Buffer.BlockCopy(binChunk, offset, imgBytes, 0, length);
                                    mat.BaseColorTextureBytes = imgBytes;
                                }
                            }
                        }
                    }
                }
            }

            list.Add(mat);
        }

        return list;
    }

    private static void TraverseNode(
        int nodeIndex,
        Matrix4x4 parentTransform,
        JArray nodes,
        JArray meshes,
        JArray accessors,
        JArray bufferViews,
        byte[] binChunk,
        List<ParsedMaterialData> materials,
        Dictionary<int, NodeTransformOverride>? animOverrides,
        ParsedModelAsset asset)
    {
        if (nodeIndex < 0 || nodeIndex >= nodes.Count)
        {
            return;
        }

        if (nodes[nodeIndex] is not JObject node)
        {
            return;
        }

        Matrix4x4 local = ReadNodeMatrix(node, nodeIndex, animOverrides);
        Matrix4x4 world = parentTransform * local;

        int? meshIndex = node["mesh"]?.Value<int?>();
        if (meshIndex.HasValue)
        {
            ExtractMeshPrimitives(
                meshIndex.Value,
                world,
                meshes,
                accessors,
                bufferViews,
                binChunk,
                materials,
                asset);
        }

        if (node["children"] is JArray children)
        {
            foreach (JToken child in children)
            {
                TraverseNode(
                    child.Value<int>(),
                    world,
                    nodes,
                    meshes,
                    accessors,
                    bufferViews,
                    binChunk,
                    materials,
                    animOverrides,
                    asset);
            }
        }
    }

    private static Dictionary<int, NodeTransformOverride>? ParseDeployEndFrameOverrides(
        JObject root,
        JArray accessors,
        JArray bufferViews,
        byte[] binChunk)
    {
        if (root["animations"] is not JArray animations || animations.Count == 0)
        {
            return null;
        }

        JObject? targetAnim = null;
        for (int i = 0; i < animations.Count; i++)
        {
            if (animations[i] is JObject animObj &&
                string.Equals(animObj["name"]?.Value<string>(), "Deploy", StringComparison.OrdinalIgnoreCase))
            {
                targetAnim = animObj;
                break;
            }
        }

        targetAnim ??= animations[0] as JObject;
        if (targetAnim == null)
        {
            return null;
        }

        JArray? channels = targetAnim["channels"] as JArray;
        JArray? samplers = targetAnim["samplers"] as JArray;
        if (channels == null || samplers == null)
        {
            return null;
        }

        var result = new Dictionary<int, NodeTransformOverride>();
        for (int c = 0; c < channels.Count; c++)
        {
            if (channels[c] is not JObject ch)
            {
                continue;
            }

            int? samplerIdx = ch["sampler"]?.Value<int?>();
            JObject? target = ch["target"] as JObject;
            int? nodeIdx = target?["node"]?.Value<int?>();
            string? path = target?["path"]?.Value<string>();

            if (!samplerIdx.HasValue || !nodeIdx.HasValue || string.IsNullOrEmpty(path) ||
                samplerIdx.Value < 0 || samplerIdx.Value >= samplers.Count)
            {
                continue;
            }

            int? outputAccIdx = samplers[samplerIdx.Value]?["output"]?.Value<int?>();
            if (!outputAccIdx.HasValue)
            {
                continue;
            }

            if (!TryResolveAccessor(outputAccIdx.Value, accessors, bufferViews, binChunk, out _, out int count, out int compType, out _, out int baseOffset, out int stride) ||
                compType != 5126 ||
                count <= 0)
            {
                continue;
            }

            if (!result.TryGetValue(nodeIdx.Value, out NodeTransformOverride? ov))
            {
                ov = new NodeTransformOverride();
                result[nodeIdx.Value] = ov;
            }

            if (string.Equals(path, "translation", StringComparison.OrdinalIgnoreCase))
            {
                int elemStride = stride > 0 ? stride : 12;
                int pos = baseOffset + ((count - 1) * elemStride);
                if (pos + 12 <= binChunk.Length)
                {
                    float x = BitConverter.ToSingle(binChunk, pos);
                    float y = BitConverter.ToSingle(binChunk, pos + 4);
                    float z = BitConverter.ToSingle(binChunk, pos + 8);
                    ov.Translation = new Vector3(-x, y, z);
                }
            }
            else if (string.Equals(path, "rotation", StringComparison.OrdinalIgnoreCase))
            {
                int elemStride = stride > 0 ? stride : 16;
                int pos = baseOffset + ((count - 1) * elemStride);
                if (pos + 16 <= binChunk.Length)
                {
                    float x = BitConverter.ToSingle(binChunk, pos);
                    float y = BitConverter.ToSingle(binChunk, pos + 4);
                    float z = BitConverter.ToSingle(binChunk, pos + 8);
                    float w = BitConverter.ToSingle(binChunk, pos + 12);
                    var q = new Quaternion(x, -y, -z, w);
                    float normSq = (x * x) + (y * y) + (z * z) + (w * w);
                    ov.Rotation = normSq > 0.00001f ? Quaternion.Normalize(q) : Quaternion.identity;
                }
            }
            else if (string.Equals(path, "scale", StringComparison.OrdinalIgnoreCase))
            {
                int elemStride = stride > 0 ? stride : 12;
                int pos = baseOffset + ((count - 1) * elemStride);
                if (pos + 12 <= binChunk.Length)
                {
                    float x = BitConverter.ToSingle(binChunk, pos);
                    float y = BitConverter.ToSingle(binChunk, pos + 4);
                    float z = BitConverter.ToSingle(binChunk, pos + 8);
                    ov.Scale = new Vector3(x, y, z);
                }
            }
        }

        return result.Count > 0 ? result : null;
    }

    private static Matrix4x4 ReadNodeMatrix(JObject node, int nodeIndex, Dictionary<int, NodeTransformOverride>? animOverrides)
    {
        NodeTransformOverride? ov = null;
        bool hasAnimOverride = animOverrides != null && animOverrides.TryGetValue(nodeIndex, out ov) && ov.HasAny;

        if (!hasAnimOverride && node["matrix"] is JArray m && m.Count == 16)
        {
            var mat = new Matrix4x4();
            for (int col = 0; col < 4; col++)
            {
                for (int row = 0; row < 4; row++)
                {
                    mat[row, col] = m[(col * 4) + row].Value<float>();
                }
            }

            // Convert right-handed matrix into Unity left-handed X-negated basis: F * M * F where F = diag(-1, 1, 1, 1)
            Matrix4x4 flip = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
            return flip * mat * flip;
        }

        Vector3 t = Vector3.zero;
        if (ov?.Translation != null)
        {
            t = ov.Translation.Value;
        }
        else if (node["translation"] is JArray ta && ta.Count >= 3)
        {
            t = new Vector3(-ta[0].Value<float>(), ta[1].Value<float>(), ta[2].Value<float>());
        }

        Quaternion r = Quaternion.identity;
        if (ov?.Rotation != null)
        {
            r = ov.Rotation.Value;
        }
        else if (node["rotation"] is JArray ra && ra.Count >= 4)
        {
            // Right-handed (x, y, z, w) -> left-handed X-mirrored (x, -y, -z, w)
            r = new Quaternion(
                ra[0].Value<float>(),
                -ra[1].Value<float>(),
                -ra[2].Value<float>(),
                ra[3].Value<float>());
        }

        Vector3 s = Vector3.one;
        if (ov?.Scale != null)
        {
            s = ov.Scale.Value;
        }
        else if (node["scale"] is JArray sa && sa.Count >= 3)
        {
            s = new Vector3(sa[0].Value<float>(), sa[1].Value<float>(), sa[2].Value<float>());
        }

        return Matrix4x4.TRS(t, r, s);
    }

    private static void ExtractMeshPrimitives(
        int meshIndex,
        Matrix4x4 worldTransform,
        JArray meshes,
        JArray accessors,
        JArray bufferViews,
        byte[] binChunk,
        List<ParsedMaterialData> materials,
        ParsedModelAsset asset)
    {
        if (meshIndex < 0 || meshIndex >= meshes.Count)
        {
            return;
        }

        JObject? meshObj = meshes[meshIndex] as JObject;
        JArray? primitives = meshObj?["primitives"] as JArray;
        if (primitives == null)
        {
            return;
        }

        string meshName = meshObj?["name"]?.Value<string>() ?? $"mesh_{meshIndex}";
        for (int p = 0; p < primitives.Count; p++)
        {
            if (primitives[p] is not JObject prim)
            {
                continue;
            }

            if (prim["extensions"]?["KHR_draco_mesh_compression"] != null)
            {
                AddonLog.Warn($"GLB '{asset.FileName}' uses Draco on mesh '{meshName}'. Re-export without Draco.");
                continue;
            }

            int mode = prim["mode"]?.Value<int>() ?? 4; // 4 = TRIANGLES
            if (mode != 4)
            {
                continue;
            }

            JObject? attrs = prim["attributes"] as JObject;
            int? posAcc = attrs?["POSITION"]?.Value<int?>();
            if (!posAcc.HasValue)
            {
                continue;
            }

            Vector3[] verts = ReadVec3Array(posAcc.Value, accessors, bufferViews, binChunk, negateX: true);
            if (verts.Length == 0)
            {
                continue;
            }

            int? normAcc = attrs?["NORMAL"]?.Value<int?>();
            Vector3[] norms = normAcc.HasValue
                ? ReadVec3Array(normAcc.Value, accessors, bufferViews, binChunk, negateX: true)
                : Array.Empty<Vector3>();

            int? uvAcc = attrs?["TEXCOORD_0"]?.Value<int?>();
            Vector2[] uvs = uvAcc.HasValue
                ? ReadVec2Array(uvAcc.Value, accessors, bufferViews, binChunk)
                : Array.Empty<Vector2>();

            int? colorAcc = attrs?["COLOR_0"]?.Value<int?>();
            Color32[] colors = colorAcc.HasValue
                ? ReadColor32Array(colorAcc.Value, accessors, bufferViews, binChunk)
                : Array.Empty<Color32>();

            int[] triangles;
            int? idxAcc = prim["indices"]?.Value<int?>();
            if (idxAcc.HasValue)
            {
                triangles = ReadIndices(idxAcc.Value, accessors, bufferViews, binChunk);
            }
            else
            {
                triangles = new int[verts.Length];
                for (int i = 0; i < verts.Length; i++)
                {
                    triangles[i] = i;
                }
            }

            // Reverse triangle winding because X was negated.
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int tmp = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = tmp;
            }

            int? matIdx = prim["material"]?.Value<int?>();
            ParsedMaterialData matData = matIdx.HasValue && matIdx.Value >= 0 && matIdx.Value < materials.Count
                ? materials[matIdx.Value]
                : new ParsedMaterialData { Name = "default", BaseColor = new Color(0.8f, 0.8f, 0.8f, 1f), Smoothness = 0.25f };

            asset.SubMeshes.Add(new ParsedSubMesh
            {
                Name = $"{meshName}_p{p}",
                Vertices = verts,
                Normals = norms,
                Uvs = uvs,
                Colors = colors,
                Triangles = triangles,
                LocalTransform = worldTransform,
                Material = matData,
            });
            asset.TotalVertices += verts.Length;
            asset.TotalTriangles += triangles.Length / 3;
        }
    }

    private static bool TryResolveAccessor(
        int accessorIndex,
        JArray accessors,
        JArray bufferViews,
        byte[] binChunk,
        out JObject accessor,
        out int count,
        out int componentType,
        out string type,
        out int baseOffset,
        out int stride)
    {
        accessor = null!;
        count = 0;
        componentType = 0;
        type = string.Empty;
        baseOffset = 0;
        stride = 0;

        if (accessorIndex < 0 || accessorIndex >= accessors.Count)
        {
            return false;
        }

        if (accessors[accessorIndex] is not JObject acc)
        {
            return false;
        }

        int? bvIndex = acc["bufferView"]?.Value<int?>();
        if (!bvIndex.HasValue || bvIndex.Value < 0 || bvIndex.Value >= bufferViews.Count)
        {
            return false;
        }

        if (bufferViews[bvIndex.Value] is not JObject bv)
        {
            return false;
        }

        accessor = acc;
        count = acc["count"]?.Value<int>() ?? 0;
        componentType = acc["componentType"]?.Value<int>() ?? 0;
        type = acc["type"]?.Value<string>() ?? string.Empty;
        int bvOffset = bv["byteOffset"]?.Value<int>() ?? 0;
        int accOffset = acc["byteOffset"]?.Value<int>() ?? 0;
        baseOffset = bvOffset + accOffset;
        stride = bv["byteStride"]?.Value<int>() ?? 0;
        return count > 0 && baseOffset >= 0 && baseOffset < binChunk.Length;
    }

    private static Vector3[] ReadVec3Array(
        int accessorIndex,
        JArray accessors,
        JArray bufferViews,
        byte[] bin,
        bool negateX)
    {
        if (!TryResolveAccessor(accessorIndex, accessors, bufferViews, bin, out _, out int count, out int compType, out _, out int baseOffset, out int stride))
        {
            return Array.Empty<Vector3>();
        }

        if (compType != 5126) // FLOAT
        {
            return Array.Empty<Vector3>();
        }

        int elemStride = stride > 0 ? stride : 12;
        if (baseOffset + ((count - 1) * elemStride) + 12 > bin.Length)
        {
            return Array.Empty<Vector3>();
        }

        var result = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            int pos = baseOffset + (i * elemStride);
            float x = BitConverter.ToSingle(bin, pos);
            float y = BitConverter.ToSingle(bin, pos + 4);
            float z = BitConverter.ToSingle(bin, pos + 8);
            result[i] = new Vector3(negateX ? -x : x, y, z);
        }

        return result;
    }

    private static Vector2[] ReadVec2Array(
        int accessorIndex,
        JArray accessors,
        JArray bufferViews,
        byte[] bin)
    {
        if (!TryResolveAccessor(accessorIndex, accessors, bufferViews, bin, out _, out int count, out int compType, out _, out int baseOffset, out int stride))
        {
            return Array.Empty<Vector2>();
        }

        if (compType != 5126)
        {
            return Array.Empty<Vector2>();
        }

        int elemStride = stride > 0 ? stride : 8;
        if (baseOffset + ((count - 1) * elemStride) + 8 > bin.Length)
        {
            return Array.Empty<Vector2>();
        }

        var result = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            int pos = baseOffset + (i * elemStride);
            float u = BitConverter.ToSingle(bin, pos);
            float v = BitConverter.ToSingle(bin, pos + 4);
            result[i] = new Vector2(u, 1f - v);
        }

        return result;
    }

    private static Color32[] ReadColor32Array(
        int accessorIndex,
        JArray accessors,
        JArray bufferViews,
        byte[] bin)
    {
        if (!TryResolveAccessor(accessorIndex, accessors, bufferViews, bin, out _, out int count, out int compType, out string type, out int baseOffset, out int stride))
        {
            return Array.Empty<Color32>();
        }

        int components = type == "VEC4" ? 4 : type == "VEC3" ? 3 : 0;
        if (components == 0)
        {
            return Array.Empty<Color32>();
        }

        int compBytes = compType switch
        {
            5121 => 1, // UNSIGNED_BYTE
            5123 => 2, // UNSIGNED_SHORT
            5126 => 4, // FLOAT
            _ => 0,
        };
        if (compBytes == 0)
        {
            return Array.Empty<Color32>();
        }

        int elemBytes = components * compBytes;
        int elemStride = stride > 0 ? stride : elemBytes;
        if (baseOffset + ((count - 1) * elemStride) + elemBytes > bin.Length)
        {
            return Array.Empty<Color32>();
        }

        var result = new Color32[count];
        for (int i = 0; i < count; i++)
        {
            int pos = baseOffset + (i * elemStride);
            float r = ReadNormalizedComponent(bin, pos, compType);
            float g = ReadNormalizedComponent(bin, pos + compBytes, compType);
            float b = ReadNormalizedComponent(bin, pos + (2 * compBytes), compType);
            float a = components == 4 ? ReadNormalizedComponent(bin, pos + (3 * compBytes), compType) : 1f;
            result[i] = new Color(r, g, b, a);
        }

        return result;
    }

    private static float ReadNormalizedComponent(byte[] bin, int pos, int compType)
    {
        return compType switch
        {
            5121 => bin[pos] / 255f,
            5123 => BitConverter.ToUInt16(bin, pos) / 65535f,
            5126 => Mathf.Clamp01(BitConverter.ToSingle(bin, pos)),
            _ => 1f,
        };
    }

    private static int[] ReadIndices(
        int accessorIndex,
        JArray accessors,
        JArray bufferViews,
        byte[] bin)
    {
        if (!TryResolveAccessor(accessorIndex, accessors, bufferViews, bin, out _, out int count, out int compType, out _, out int baseOffset, out int stride))
        {
            return Array.Empty<int>();
        }

        int bytesPerIndex = compType switch
        {
            5121 => 1, // UNSIGNED_BYTE
            5123 => 2, // UNSIGNED_SHORT
            5125 => 4, // UNSIGNED_INT
            _ => 0,
        };
        if (bytesPerIndex == 0)
        {
            return Array.Empty<int>();
        }

        int elemStride = stride > 0 ? stride : bytesPerIndex;
        if (baseOffset + ((count - 1) * elemStride) + bytesPerIndex > bin.Length)
        {
            return Array.Empty<int>();
        }

        var indices = new int[count];
        for (int i = 0; i < count; i++)
        {
            int pos = baseOffset + (i * elemStride);
            indices[i] = compType switch
            {
                5121 => bin[pos],
                5123 => BitConverter.ToUInt16(bin, pos),
                5125 => (int)BitConverter.ToUInt32(bin, pos),
                _ => 0,
            };
        }

        return indices;
    }
}
