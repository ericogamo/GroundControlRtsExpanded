using System;
using UnityEngine;

namespace GroundControlRtsExpanded;

internal static class UrpMaterialFactory
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
    private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

    private static Material? encyclopediaTemplate;

    internal static Shader? ResolveLitShader()
    {
        return Shader.Find("Universal Render Pipeline/Lit")
            ?? Shader.Find("Universal Render Pipeline/Simple Lit")
            ?? Shader.Find("Standard");
    }

    internal static Material CreateMaterial(
        string name,
        Color baseColor,
        Texture2D? baseMap,
        float metallic,
        float smoothness,
        Color? tintOverride)
    {
        Material mat;
        Shader? shader = ResolveLitShader();
        if (shader != null)
        {
            mat = new Material(shader);
        }
        else
        {
            Material? template = FindEncyclopediaMaterial();
            if (template != null)
            {
                mat = new Material(template);
            }
            else
            {
                Shader? fallback = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Sprites/Default");
                mat = fallback != null ? new Material(fallback) : new Material(Shader.Find("Standard"));
            }
        }

        mat.name = name;
        mat.hideFlags = HideFlags.DontUnloadUnusedAsset;

        Color finalColor = baseColor;
        if (AddonConfig.ApplyMilitaryTint.Value && tintOverride.HasValue)
        {
            Color tint = tintOverride.Value;
            // Soft blend with tint so bright models harmonize with Nuclear Option terrain & units.
            finalColor = new Color(
                finalColor.r * Mathf.Lerp(1f, tint.r, 0.75f),
                finalColor.g * Mathf.Lerp(1f, tint.g, 0.75f),
                finalColor.b * Mathf.Lerp(1f, tint.b, 0.75f),
                finalColor.a);
        }

        if (mat.HasProperty(BaseColorId))
        {
            mat.SetColor(BaseColorId, finalColor);
        }

        if (mat.HasProperty(ColorId))
        {
            mat.SetColor(ColorId, finalColor);
        }

        mat.color = finalColor;

        if (baseMap != null)
        {
            if (mat.HasProperty(BaseMapId))
            {
                mat.SetTexture(BaseMapId, baseMap);
            }

            if (mat.HasProperty(MainTexId))
            {
                mat.SetTexture(MainTexId, baseMap);
            }

            mat.mainTexture = baseMap;
        }

        if (mat.HasProperty(MetallicId))
        {
            mat.SetFloat(MetallicId, Mathf.Clamp01(metallic));
        }

        if (mat.HasProperty(SmoothnessId))
        {
            mat.SetFloat(SmoothnessId, Mathf.Clamp01(smoothness));
        }

        return mat;
    }

    internal static bool TryParseHexColor(string? hex, out Color color)
    {
        color = Color.white;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        string trimmed = hex!.Trim();
        if (!trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            trimmed = "#" + trimmed;
        }

        return ColorUtility.TryParseHtmlString(trimmed, out color);
    }

    private static Material? FindEncyclopediaMaterial()
    {
        if (encyclopediaTemplate != null)
        {
            return encyclopediaTemplate;
        }

        Encyclopedia? enc = Encyclopedia.i;
        if (enc == null || enc.buildings == null)
        {
            return null;
        }

        for (int i = 0; i < enc.buildings.Count; i++)
        {
            BuildingDefinition? def = enc.buildings[i];
            if (def == null || def.unitPrefab == null)
            {
                continue;
            }

            Renderer? renderer = def.unitPrefab.GetComponentInChildren<Renderer>(true);
            if (renderer != null && renderer.sharedMaterial != null)
            {
                encyclopediaTemplate = renderer.sharedMaterial;
                return encyclopediaTemplate;
            }
        }

        return null;
    }
}
