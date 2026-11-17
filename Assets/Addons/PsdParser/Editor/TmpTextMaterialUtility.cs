using System.IO;
using PsdParser;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 为 PSD 文本的描边/投影效果创建共享 TMP 材质，避免每个 Label 生成材质实例。
/// </summary>
public static class TmpTextMaterialUtility
{
    private const string MaterialFolder = "Assets/Addons/PsdParser/Generated/Materials";

    public static Material GetOrCreate(Material baseMaterial, PsdStrokeEffect stroke, PsdDropShadowEffect shadow)
    {
        if (baseMaterial == null) return null;

        bool hasStroke = stroke != null && stroke.enabled && stroke.size > 0;
        bool hasShadow = shadow != null && shadow.enabled && (shadow.color.a > 0f || shadow.opacity > 0);
        if (!hasStroke && !hasShadow) return null;

        string key = BuildKey(baseMaterial, stroke, shadow);
        string assetPath = MaterialFolder + "/" + key + ".mat";

        Material existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (existing != null) return existing;

        var material = new Material(baseMaterial) { name = key };

        if (hasStroke)
        {
            material.EnableKeyword("OUTLINE_ON");
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, Mathf.Clamp(stroke.size * 0.02f, 0.01f, 1f));

            Color outlineColor = stroke.color;
            outlineColor.a *= Mathf.Clamp01((stroke.opacity > 0 ? stroke.opacity : 100) / 100f);
            material.SetColor(ShaderUtilities.ID_OutlineColor, outlineColor);
        }

        if (hasShadow)
        {
            material.EnableKeyword("UNDERLAY_ON");

            Color shadowColor = shadow.color;
            shadowColor.a *= Mathf.Clamp01((shadow.opacity > 0 ? shadow.opacity : 50) / 100f);
            material.SetColor(ShaderUtilities.ID_UnderlayColor, shadowColor);

            float radians = shadow.angle * Mathf.Deg2Rad;
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX,
                Mathf.Clamp(Mathf.Cos(radians) * shadow.distance * 0.01f, -1f, 1f));
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY,
                Mathf.Clamp(-Mathf.Sin(radians) * shadow.distance * 0.01f, -1f, 1f));
            material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, Mathf.Clamp01(shadow.blur * 0.02f));
        }

        EnsureFolder(MaterialFolder);
        AssetDatabase.CreateAsset(material, assetPath);
        return material;
    }

    private static string BuildKey(Material baseMaterial, PsdStrokeEffect stroke, PsdDropShadowEffect shadow)
    {
        string basePath = AssetDatabase.GetAssetPath(baseMaterial);
        string strokeKey = stroke != null && stroke.enabled
            ? $"S{stroke.size}_{ColorKey(stroke.color)}_{stroke.opacity}"
            : "S0";
        string shadowKey = shadow != null && shadow.enabled
            ? $"D{shadow.distance}_{shadow.blur}_{shadow.angle}_{ColorKey(shadow.color)}_{shadow.opacity}"
            : "D0";

        string hashSource = basePath + "|" + strokeKey + "|" + shadowKey;
        string hash = Hash128.Compute(hashSource).ToString().Substring(0, 12);
        return "TMP_" + Sanitize(Path.GetFileNameWithoutExtension(basePath)) + "_" + hash;
    }

    private static string ColorKey(Color color)
    {
        return ColorUtility.ToHtmlStringRGBA(color);
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value)) return "Font";

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value.Replace('/', '_').Replace('\\', '_');
    }

    private static void EnsureFolder(string assetFolder)
    {
        if (string.IsNullOrEmpty(assetFolder)) return;

        assetFolder = assetFolder.Replace('\\', '/').TrimEnd('/');
        if (AssetDatabase.IsValidFolder(assetFolder)) return;

        string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(parent)) return;

        EnsureFolder(parent);
        string name = Path.GetFileName(assetFolder);
        if (!string.IsNullOrEmpty(name))
        {
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
