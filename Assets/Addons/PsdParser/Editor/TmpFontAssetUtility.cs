using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// 从 TTF 生成 TMP 字体资源，并处理 TMP Essential Resources 的导入提示。
/// </summary>
public static class TmpFontAssetUtility
{
    public const string DefaultFontPath = "Assets/Font/YaHei.TTF";
    public const string DefaultFontAssetPath = "Assets/Res/Font/YaHei SDF.asset";

    [MenuItem("Tools/PSD to Prefab/生成 YaHei TMP 字体资源")]
    private static void GenerateDefaultFontAssetMenu()
    {
        GenerateDefaultFontAsset(true);
    }

    [MenuItem("Tools/PSD to Prefab/导入 TMP Essential Resources")]
    private static void ImportEssentialResourcesMenu()
    {
        ImportEssentialResources(true);
    }

    public static TMP_FontAsset GenerateDefaultFontAsset(bool logErrors)
    {
        return Generate(DefaultFontPath, DefaultFontAssetPath, logErrors);
    }

    public static TMP_FontAsset Generate(string fontPath, string assetPath, bool logErrors)
    {
        TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        if (existing != null) return existing;

        Font font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
        if (font == null)
        {
            if (logErrors) Debug.LogError("[PSD to Prefab] 找不到字体文件：" + fontPath);
            return null;
        }

        if (Shader.Find("TextMeshPro/Distance Field") == null)
        {
            if (logErrors)
            {
                EditorUtility.DisplayDialog(
                    "TMP 资源缺失",
                    "未找到 TMP 着色器。请先执行菜单：\nTools > PSD to Prefab > 导入 TMP Essential Resources。",
                    "OK");
            }

            return null;
        }

        TMP_FontAsset fontAsset;
        try
        {
            fontAsset = TMP_FontAsset.CreateFontAsset(
                font,
                90,
                9,
                GlyphRenderMode.SDFAA,
                1024,
                1024,
                AtlasPopulationMode.Dynamic,
                true);
        }
        catch (Exception e)
        {
            Debug.LogError("[PSD to Prefab] 创建 TMP 字体资源失败：" + e);
            return null;
        }

        if (fontAsset == null)
        {
            if (logErrors) Debug.LogError("[PSD to Prefab] 创建 TMP 字体资源失败。");
            return null;
        }

        EnsureFolder(Path.GetDirectoryName(assetPath));
        fontAsset.name = Path.GetFileNameWithoutExtension(assetPath);
        fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        AssetDatabase.CreateAsset(fontAsset, assetPath);

        if (fontAsset.atlasTextures != null)
        {
            for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
            {
                Texture2D atlas = fontAsset.atlasTextures[i];
                if (atlas == null) continue;

                atlas.name = fontAsset.name + " Atlas " + i;
                atlas.hideFlags = HideFlags.None;
                AssetDatabase.AddObjectToAsset(atlas, fontAsset);
            }
        }

        if (fontAsset.material != null)
        {
            fontAsset.material.name = fontAsset.name + " Material";
            fontAsset.material.hideFlags = HideFlags.None;
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        }

        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[PSD to Prefab] 已生成 TMP 字体资源：" + assetPath);
        return fontAsset;
    }

    public static void ImportEssentialResources(bool logErrors)
    {
        try
        {
            TMP_PackageResourceImporter.ImportResources(true, false, false);
            Debug.Log("[PSD to Prefab] 已开始导入 TMP Essential Resources，完成后再生成字体资源。");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[PSD to Prefab] 导入 TMP Essential Resources 失败：" + e.Message);
            if (logErrors)
            {
                EditorUtility.DisplayDialog(
                    "无法导入 TMP Essential Resources",
                    "请通过菜单 Window > TextMeshPro > Import TMP Essential Resources 手动导入。",
                    "OK");
            }
        }
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
