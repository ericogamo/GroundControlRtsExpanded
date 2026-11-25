using System.Collections.Generic;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal static class ProceduralFallbackModels
{
    internal static ParsedModelAsset Build(string slotId)
    {
        var asset = new ParsedModelAsset
        {
            SlotId = slotId,
            FileName = "<procedural-fallback>",
            SourceKind = ModelSourceKind.Fallback,
        };

        var olive = new ParsedMaterialData
        {
            Name = "military_olive",
            BaseColor = new Color(0.42f, 0.45f, 0.36f, 1f),
            Metallic = 0.1f,
            Smoothness = 0.25f,
        };

        var concrete = new ParsedMaterialData
        {
            Name = "concrete",
            BaseColor = new Color(0.55f, 0.56f, 0.54f, 1f),
            Metallic = 0f,
            Smoothness = 0.15f,
        };

        var steel = new ParsedMaterialData
        {
            Name = "steel",
            BaseColor = new Color(0.36f, 0.40f, 0.43f, 1f),
            Metallic = 0.5f,
            Smoothness = 0.4f,
        };

        var accent = new ParsedMaterialData
        {
            Name = "radar_dish",
            BaseColor = new Color(0.70f, 0.72f, 0.70f, 1f),
            Metallic = 0.25f,
            Smoothness = 0.35f,
        };

        switch (slotId)
        {
            case "command_hq":
                AddBox(asset, "base", Vector3.zero, new Vector3(18f, 5f, 14f), concrete);
                AddBox(asset, "bunker", new Vector3(-2f, 5f, 0f), new Vector3(12f, 4f, 10f), olive);
                AddBox(asset, "tower", new Vector3(5f, 5f, 3f), new Vector3(5f, 8f, 5f), steel);
                AddBox(asset, "antenna", new Vector3(5f, 13f, 3f), new Vector3(1.2f, 5f, 1.2f), accent);
                break;

            case "barracks":
                AddBox(asset, "pad", Vector3.zero, new Vector3(20f, 0.6f, 16f), concrete);
                AddBox(asset, "wing_a", new Vector3(-4.5f, 0.6f, 0f), new Vector3(7f, 4.5f, 12f), olive);
                AddBox(asset, "wing_b", new Vector3(4.5f, 0.6f, 0f), new Vector3(7f, 4.5f, 12f), olive);
                AddBox(asset, "hall", new Vector3(0f, 0.6f, 4.5f), new Vector3(14f, 3.5f, 3f), steel);
                break;

            case "pilot_academy":
                AddBox(asset, "apron", Vector3.zero, new Vector3(24f, 0.5f, 18f), concrete);
                AddBox(asset, "hangar", new Vector3(-2f, 0.5f, 0f), new Vector3(16f, 7f, 12f), steel);
                AddBox(asset, "ops_tower", new Vector3(8f, 0.5f, 4f), new Vector3(4f, 11f, 4f), accent);
                break;

            case "logistics_camp":
                AddBox(asset, "yard", Vector3.zero, new Vector3(22f, 0.4f, 18f), concrete);
                AddBox(asset, "depot_a", new Vector3(-4f, 0.4f, -3f), new Vector3(10f, 4.5f, 7f), olive);
                AddBox(asset, "depot_b", new Vector3(4f, 0.4f, 4f), new Vector3(10f, 4.5f, 6f), olive);
                AddBox(asset, "tank", new Vector3(-6f, 0.4f, 5f), new Vector3(4f, 4f, 4f), steel);
                break;

            case "naval_command":
                AddBox(asset, "pier", Vector3.zero, new Vector3(22f, 1.5f, 16f), concrete);
                AddBox(asset, "ops", new Vector3(0f, 1.5f, -2f), new Vector3(14f, 5.5f, 9f), steel);
                AddBox(asset, "bridge", new Vector3(4f, 7f, 0f), new Vector3(5f, 5f, 5f), accent);
                break;

            case "abm_radar":
                AddBox(asset, "plinth", Vector3.zero, new Vector3(14f, 3f, 14f), concrete);
                AddBox(asset, "array_housing", new Vector3(0f, 3f, 0f), new Vector3(9f, 9f, 7f), olive);
                AddBox(asset, "phased_face", new Vector3(0f, 5f, 3.8f), new Vector3(7f, 6f, 0.8f), accent);
                break;

            case "abm_silo":
                AddBox(asset, "pad", Vector3.zero, new Vector3(14f, 1.5f, 14f), concrete);
                AddBox(asset, "vls_cell_a", new Vector3(-3f, 1.5f, -3f), new Vector3(4.5f, 2.5f, 4.5f), steel);
                AddBox(asset, "vls_cell_b", new Vector3(3f, 1.5f, -3f), new Vector3(4.5f, 2.5f, 4.5f), steel);
                AddBox(asset, "vls_cell_c", new Vector3(-3f, 1.5f, 3f), new Vector3(4.5f, 2.5f, 4.5f), steel);
                AddBox(asset, "vls_cell_d", new Vector3(3f, 1.5f, 3f), new Vector3(4.5f, 2.5f, 4.5f), steel);
                break;

            case "abm_plant":
                AddBox(asset, "slab", Vector3.zero, new Vector3(26f, 0.8f, 20f), concrete);
                AddBox(asset, "assembly_hall", new Vector3(-2f, 0.8f, 0f), new Vector3(18f, 9f, 14f), steel);
                AddBox(asset, "cleanroom", new Vector3(8f, 0.8f, 0f), new Vector3(6f, 6f, 10f), olive);
                break;

            case "sam_kit":
            default:
                AddBox(asset, "skid", Vector3.zero, new Vector3(4.8f, 0.4f, 2.6f), steel);
                AddBox(asset, "container", new Vector3(0f, 0.4f, 0f), new Vector3(4.4f, 2.4f, 2.4f), olive);
                AddBox(asset, "frame", new Vector3(0f, 2.8f, 0f), new Vector3(4.6f, 0.3f, 2.5f), accent);
                break;
        }

        RecomputeBounds(asset);
        return asset;
    }

    private static void AddBox(
        ParsedModelAsset asset,
        string partName,
        Vector3 bottomCenter,
        Vector3 size,
        ParsedMaterialData material)
    {
        float hx = size.x * 0.5f;
        float hz = size.z * 0.5f;
        float y0 = bottomCenter.y;
        float y1 = bottomCenter.y + size.y;
        float cx = bottomCenter.x;
        float cz = bottomCenter.z;

        var verts = new List<Vector3>(24);
        var norms = new List<Vector3>(24);
        var uvs = new List<Vector2>(24);
        var tris = new List<int>(36);

        // Front (+Z)
        AddQuad(verts, norms, uvs, tris,
            new Vector3(cx - hx, y0, cz + hz), new Vector3(cx + hx, y0, cz + hz),
            new Vector3(cx + hx, y1, cz + hz), new Vector3(cx - hx, y1, cz + hz), Vector3.forward);
        // Back (-Z)
        AddQuad(verts, norms, uvs, tris,
            new Vector3(cx + hx, y0, cz - hz), new Vector3(cx - hx, y0, cz - hz),
            new Vector3(cx - hx, y1, cz - hz), new Vector3(cx + hx, y1, cz - hz), Vector3.back);
        // Right (+X)
        AddQuad(verts, norms, uvs, tris,
            new Vector3(cx + hx, y0, cz + hz), new Vector3(cx + hx, y0, cz - hz),
            new Vector3(cx + hx, y1, cz - hz), new Vector3(cx + hx, y1, cz + hz), Vector3.right);
        // Left (-X)
        AddQuad(verts, norms, uvs, tris,
            new Vector3(cx - hx, y0, cz - hz), new Vector3(cx - hx, y0, cz + hz),
            new Vector3(cx - hx, y1, cz + hz), new Vector3(cx - hx, y1, cz - hz), Vector3.left);
        // Top (+Y)
        AddQuad(verts, norms, uvs, tris,
            new Vector3(cx - hx, y1, cz + hz), new Vector3(cx + hx, y1, cz + hz),
            new Vector3(cx + hx, y1, cz - hz), new Vector3(cx - hx, y1, cz - hz), Vector3.up);
        // Bottom (-Y)
        AddQuad(verts, norms, uvs, tris,
            new Vector3(cx - hx, y0, cz - hz), new Vector3(cx + hx, y0, cz - hz),
            new Vector3(cx + hx, y0, cz + hz), new Vector3(cx - hx, y0, cz + hz), Vector3.down);

        asset.SubMeshes.Add(new ParsedSubMesh
        {
            Name = partName,
            Vertices = verts.ToArray(),
            Normals = norms.ToArray(),
            Uvs = uvs.ToArray(),
            Triangles = tris.ToArray(),
            LocalTransform = Matrix4x4.identity,
            Material = material,
        });
        asset.TotalVertices += verts.Count;
        asset.TotalTriangles += tris.Count / 3;
    }

    private static void AddQuad(
        List<Vector3> verts,
        List<Vector3> norms,
        List<Vector2> uvs,
        List<int> tris,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector3 normal)
    {
        int start = verts.Count;
        verts.Add(a);
        verts.Add(b);
        verts.Add(c);
        verts.Add(d);

        norms.Add(normal);
        norms.Add(normal);
        norms.Add(normal);
        norms.Add(normal);

        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(1f, 0f));
        uvs.Add(new Vector2(1f, 1f));
        uvs.Add(new Vector2(0f, 1f));

        // Unity clockwise front-face order when looking against the normal.
        tris.Add(start);
        tris.Add(start + 2);
        tris.Add(start + 1);
        tris.Add(start);
        tris.Add(start + 3);
        tris.Add(start + 2);
    }

    private static void RecomputeBounds(ParsedModelAsset asset)
    {
        var bounds = new Bounds();
        bool hasBounds = false;
        for (int i = 0; i < asset.SubMeshes.Count; i++)
        {
            Vector3[] verts = asset.SubMeshes[i].Vertices;
            for (int v = 0; v < verts.Length; v++)
            {
                if (!hasBounds)
                {
                    bounds = new Bounds(verts[v], Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(verts[v]);
                }
            }
        }

        asset.RawBounds = bounds;
    }
}
