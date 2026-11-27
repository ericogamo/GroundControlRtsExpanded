using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GroundControlRtsExpanded;

internal struct RoadSample
{
    internal GlobalPosition Pos;
    internal bool IsBridge;
    internal float GroundY;
}

internal static class RoadMeshBuilder
{
    private const float LandHalfWidth = 4.25f;
    private const float LandShoulderWidth = 2.1f;
    private const float LandSkirtDrop = 2.2f;
    private const float LandSurfaceLift = 0.22f;

    private const float BridgeHalfWidth = 4.5f;
    private const float BridgeDeckLift = 0.22f;
    private const float BridgeDeckThickness = 0.65f;
    private const float BridgeRailWidth = 0.45f;
    private const float BridgeRailHeight = 0.90f;
    private const float BridgePierHalfSize = 0.85f;
    private const float BridgePierSpacingMeters = 26f;

    private static Material? asphaltMat;
    private static Material? shoulderMat;
    private static Material? bridgeDeckMat;
    private static Material? bridgeStructureMat;
    private static Material? blueprintMat;

    internal static GameObject BuildRoadObject(
        string objectName,
        IReadOnlyList<RoadSample> samples,
        int sampleCountLimit,
        bool isBlueprint,
        bool addColliders)
    {
        EnsureMaterials();

        var root = new GameObject(objectName);
        AttachToDatumOrigin(root);

        int count = Mathf.Min(samples.Count, sampleCountLimit);
        if (count < 2)
        {
            return root;
        }

        int staticsLayer = PhysicsLayers.Statics;
        int visualLayer = PhysicsLayers.IgnoreRaycast;

        var surfaceVerts = new List<Vector3>();
        var surfaceTris = new List<int>();
        var shoulderVerts = new List<Vector3>();
        var shoulderTris = new List<int>();
        var bridgeDeckVerts = new List<Vector3>();
        var bridgeDeckTris = new List<int>();
        var railAndPierVerts = new List<Vector3>();
        var railAndPierTris = new List<int>();

        float bridgeDistSincePier = BridgePierSpacingMeters * 0.5f;

        for (int i = 0; i < count - 1; i++)
        {
            RoadSample a = samples[i];
            RoadSample b = samples[i + 1];

            Vector3 pa = a.Pos.AsVector3();
            Vector3 pb = b.Pos.AsVector3();
            Vector3 delta = pb - pa;
            float segLen = delta.magnitude;
            if (segLen < 0.05f)
            {
                continue;
            }

            Vector3 flatFwd = new Vector3(delta.x, 0f, delta.z);
            if (flatFwd.sqrMagnitude < 0.0001f)
            {
                flatFwd = Vector3.forward;
            }
            else
            {
                flatFwd.Normalize();
            }

            Vector3 right = new Vector3(flatFwd.z, 0f, -flatFwd.x);
            bool segIsBridge = a.IsBridge || b.IsBridge;

            if (!segIsBridge)
            {
                float liftA = i == 0 ? 0.02f : LandSurfaceLift;
                float liftB = i == count - 2 ? 0.02f : LandSurfaceLift;
                Vector3 aTopL = pa - right * LandHalfWidth + Vector3.up * liftA;
                Vector3 aTopR = pa + right * LandHalfWidth + Vector3.up * liftA;
                Vector3 bTopL = pb - right * LandHalfWidth + Vector3.up * liftB;
                Vector3 bTopR = pb + right * LandHalfWidth + Vector3.up * liftB;
                AddQuad(surfaceVerts, surfaceTris, aTopL, bTopL, bTopR, aTopR);

                Vector3 aSkirtL = pa - right * (LandHalfWidth + LandShoulderWidth) + Vector3.down * LandSkirtDrop;
                Vector3 bSkirtL = pb - right * (LandHalfWidth + LandShoulderWidth) + Vector3.down * LandSkirtDrop;
                AddQuad(shoulderVerts, shoulderTris, aSkirtL, bSkirtL, bTopL, aTopL);

                Vector3 aSkirtR = pa + right * (LandHalfWidth + LandShoulderWidth) + Vector3.down * LandSkirtDrop;
                Vector3 bSkirtR = pb + right * (LandHalfWidth + LandShoulderWidth) + Vector3.down * LandSkirtDrop;
                AddQuad(shoulderVerts, shoulderTris, aTopR, bTopR, bSkirtR, aSkirtR);
            }
            else
            {
                Vector3 aTopL = pa - right * BridgeHalfWidth + Vector3.up * BridgeDeckLift;
                Vector3 aTopR = pa + right * BridgeHalfWidth + Vector3.up * BridgeDeckLift;
                Vector3 bTopL = pb - right * BridgeHalfWidth + Vector3.up * BridgeDeckLift;
                Vector3 bTopR = pb + right * BridgeHalfWidth + Vector3.up * BridgeDeckLift;

                Vector3 aBotL = aTopL - Vector3.up * BridgeDeckThickness;
                Vector3 aBotR = aTopR - Vector3.up * BridgeDeckThickness;
                Vector3 bBotL = bTopL - Vector3.up * BridgeDeckThickness;
                Vector3 bBotR = bTopR - Vector3.up * BridgeDeckThickness;

                // Top drive surface + underside + sides (all smooth for vehicle driving)
                AddQuad(bridgeDeckVerts, bridgeDeckTris, aTopL, bTopL, bTopR, aTopR);
                AddQuad(bridgeDeckVerts, bridgeDeckTris, aBotR, bBotR, bBotL, aBotL);
                AddQuad(bridgeDeckVerts, bridgeDeckTris, aBotL, bBotL, bTopL, aTopL);
                AddQuad(bridgeDeckVerts, bridgeDeckTris, aTopR, bTopR, bBotR, aBotR);

                // Non-collider decorative guardrails
                if (!isBlueprint)
                {
                    AddBoxBeam(railAndPierVerts, railAndPierTris, aTopL, bTopL, right, BridgeRailWidth, BridgeRailHeight, innerOffset: -0.25f);
                    AddBoxBeam(railAndPierVerts, railAndPierTris, aTopR, bTopR, right, BridgeRailWidth, BridgeRailHeight, innerOffset: 0.25f);

                    bridgeDistSincePier += segLen;
                    if (bridgeDistSincePier >= BridgePierSpacingMeters)
                    {
                        bridgeDistSincePier = 0f;
                        float bottomY = Mathf.Min(b.GroundY, Datum.SeaLevel.y - 12f) - 4f;
                        if (pb.y - BridgeDeckThickness > bottomY + 1.0f)
                        {
                            Vector3 pierCenter = new Vector3(pb.x, pb.y - BridgeDeckThickness, pb.z);
                            AddPier(railAndPierVerts, railAndPierTris, pierCenter - right * (BridgeHalfWidth * 0.55f), bottomY, flatFwd, right);
                            AddPier(railAndPierVerts, railAndPierTris, pierCenter + right * (BridgeHalfWidth * 0.55f), bottomY, flatFwd, right);
                        }
                    }
                }
            }
        }

        bool solidColliders = addColliders && !isBlueprint;
        if (surfaceVerts.Count > 0)
        {
            CreateMeshChild(
                root.transform,
                solidColliders ? "terrain_gce_AsphaltSurface" : "AsphaltSurface",
                surfaceVerts,
                surfaceTris,
                isBlueprint ? blueprintMat! : asphaltMat!,
                solidColliders,
                isBlueprint ? visualLayer : staticsLayer);
        }

        if (shoulderVerts.Count > 0)
        {
            CreateMeshChild(
                root.transform,
                solidColliders ? "terrain_gce_RoadShoulder" : "RoadShoulder",
                shoulderVerts,
                shoulderTris,
                isBlueprint ? blueprintMat! : shoulderMat!,
                solidColliders,
                isBlueprint ? visualLayer : staticsLayer);
        }

        if (bridgeDeckVerts.Count > 0)
        {
            CreateMeshChild(
                root.transform,
                solidColliders ? "terrain_gce_BridgeDeck" : "BridgeDeck",
                bridgeDeckVerts,
                bridgeDeckTris,
                isBlueprint ? blueprintMat! : bridgeDeckMat!,
                solidColliders,
                isBlueprint ? visualLayer : staticsLayer);
        }

        if (railAndPierVerts.Count > 0)
        {
            // Guardrails and support piers omit colliders so horizontal StaticsMask obstacle raycasts never block vehicles entering a bridge.
            CreateMeshChild(
                root.transform,
                "BridgeStructure",
                railAndPierVerts,
                railAndPierTris,
                bridgeStructureMat!,
                addCollider: false,
                visualLayer);
        }

        return root;
    }

    internal static void AttachToDatumOrigin(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        Transform? origin = Datum.origin;
        if (origin != null && root.transform.parent != origin)
        {
            root.transform.SetParent(origin, worldPositionStays: false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
        }
    }

    internal static void DestroyRoadObject(GameObject? root)
    {
        if (root == null)
        {
            return;
        }

        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(includeInactive: true);
        for (int i = 0; i < filters.Length; i++)
        {
            Mesh? mesh = filters[i].sharedMesh;
            if (mesh != null)
            {
                filters[i].sharedMesh = null;
                if (filters[i].TryGetComponent<MeshCollider>(out MeshCollider mc) && ReferenceEquals(mc.sharedMesh, mesh))
                {
                    mc.sharedMesh = null;
                }

                Object.Destroy(mesh);
            }
        }

        Object.Destroy(root);
    }

    internal static void ClearMaterials()
    {
        DestroyMat(ref asphaltMat);
        DestroyMat(ref shoulderMat);
        DestroyMat(ref bridgeDeckMat);
        DestroyMat(ref bridgeStructureMat);
        DestroyMat(ref blueprintMat);
    }

    private static void CreateMeshChild(
        Transform parent,
        string name,
        List<Vector3> verts,
        List<int> tris,
        Material mat,
        bool addCollider,
        int layer)
    {
        var child = new GameObject(name)
        {
            layer = layer,
        };
        child.transform.SetParent(parent, worldPositionStays: false);

        var mesh = new Mesh
        {
            name = $"{parent.name}_{name}",
            indexFormat = verts.Count > 60000 ? IndexFormat.UInt32 : IndexFormat.UInt16,
        };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        MeshFilter mf = child.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        MeshRenderer mr = child.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.On;
        mr.receiveShadows = true;

        if (addCollider)
        {
            MeshCollider mc = child.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            if (GameAssets.i != null && GameAssets.i.terrainMaterial != null)
            {
                mc.sharedMaterial = GameAssets.i.terrainMaterial;
            }
        }
    }

    private static void AddQuad(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int start = verts.Count;
        verts.Add(a);
        verts.Add(b);
        verts.Add(c);
        verts.Add(d);
        tris.Add(start);
        tris.Add(start + 1);
        tris.Add(start + 2);
        tris.Add(start);
        tris.Add(start + 2);
        tris.Add(start + 3);
    }

    private static void AddBoxBeam(
        List<Vector3> verts,
        List<int> tris,
        Vector3 baseA,
        Vector3 baseB,
        Vector3 right,
        float width,
        float height,
        float innerOffset)
    {
        float hw = width * 0.5f;
        Vector3 centerA = baseA + right * innerOffset;
        Vector3 centerB = baseB + right * innerOffset;

        Vector3 al = centerA - right * hw;
        Vector3 ar = centerA + right * hw;
        Vector3 bl = centerB - right * hw;
        Vector3 br = centerB + right * hw;

        Vector3 alt = al + Vector3.up * height;
        Vector3 art = ar + Vector3.up * height;
        Vector3 blt = bl + Vector3.up * height;
        Vector3 brt = br + Vector3.up * height;

        AddQuad(verts, tris, alt, blt, brt, art);
        AddQuad(verts, tris, al, bl, blt, alt);
        AddQuad(verts, tris, art, brt, br, ar);
    }

    private static void AddPier(
        List<Vector3> verts,
        List<int> tris,
        Vector3 topCenter,
        float bottomY,
        Vector3 fwd,
        Vector3 right)
    {
        Vector3 f = fwd * BridgePierHalfSize;
        Vector3 r = right * BridgePierHalfSize;
        Vector3 botCenter = new Vector3(topCenter.x, bottomY, topCenter.z);

        Vector3 t0 = topCenter - r - f;
        Vector3 t1 = topCenter + r - f;
        Vector3 t2 = topCenter + r + f;
        Vector3 t3 = topCenter - r + f;

        Vector3 b0 = botCenter - r - f;
        Vector3 b1 = botCenter + r - f;
        Vector3 b2 = botCenter + r + f;
        Vector3 b3 = botCenter - r + f;

        AddQuad(verts, tris, b0, b1, t1, t0);
        AddQuad(verts, tris, b1, b2, t2, t1);
        AddQuad(verts, tris, b2, b3, t3, t2);
        AddQuad(verts, tris, b3, b0, t0, t3);
    }

    private static void EnsureMaterials()
    {
        if (asphaltMat != null)
        {
            return;
        }

        asphaltMat = UrpMaterialFactory.CreateMaterial(
            "GCE_RoadAsphalt",
            new Color(0.22f, 0.23f, 0.25f, 1f),
            baseMap: null,
            metallic: 0.04f,
            smoothness: 0.16f,
            tintOverride: null);

        shoulderMat = UrpMaterialFactory.CreateMaterial(
            "GCE_RoadShoulder",
            new Color(0.36f, 0.34f, 0.29f, 1f),
            baseMap: null,
            metallic: 0.02f,
            smoothness: 0.08f,
            tintOverride: null);

        bridgeDeckMat = UrpMaterialFactory.CreateMaterial(
            "GCE_BridgeDeck",
            new Color(0.46f, 0.48f, 0.50f, 1f),
            baseMap: null,
            metallic: 0.05f,
            smoothness: 0.20f,
            tintOverride: null);

        bridgeStructureMat = UrpMaterialFactory.CreateMaterial(
            "GCE_BridgeStructure",
            new Color(0.35f, 0.37f, 0.39f, 1f),
            baseMap: null,
            metallic: 0.08f,
            smoothness: 0.15f,
            tintOverride: null);

        blueprintMat = UrpMaterialFactory.CreateMaterial(
            "GCE_RoadBlueprint",
            new Color(0.24f, 0.68f, 0.96f, 1f),
            baseMap: null,
            metallic: 0.1f,
            smoothness: 0.35f,
            tintOverride: null);
    }

    private static void DestroyMat(ref Material? mat)
    {
        if (mat != null)
        {
            Object.Destroy(mat);
            mat = null;
        }
    }
}
