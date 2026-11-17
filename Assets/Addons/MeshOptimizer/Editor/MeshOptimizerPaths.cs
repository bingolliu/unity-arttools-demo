using System;
using System.Collections.Generic;
using System.IO;

namespace MeshOptimizer
{
    /// <summary>
    /// 约定路径与目录映射，保持与原 InstaLOD 工具一致。
    /// </summary>
    public static class MeshOptimizerPaths
    {
        public const string RootFolder = "Assets/Addons/MeshOptimizer";
        public const string ConfigFolder = RootFolder + "/Config";
        public const string ConfigPath = ConfigFolder + "/MeshOptimizerFolderConfig.json";
        public const string LodSuffix = "_LOD1";

        public static readonly Dictionary<FolderKind, string> SourceFolders = new Dictionary<FolderKind, string>
        {
            { FolderKind.Roles, "Assets/Roles" },
            { FolderKind.Equip, "Assets/Equip" },
            { FolderKind.Scenes, "Assets/Scenes" },
            { FolderKind.Effect, "Assets/Effect" },
        };

        public static readonly Dictionary<FolderKind, string> PrefabFolders = new Dictionary<FolderKind, string>
        {
            { FolderKind.Roles, "Assets/Res/Roles" },
            { FolderKind.Equip, "Assets/Res/Equip" },
            { FolderKind.Scenes, "Assets/Scenes" },
            { FolderKind.Effect, "Assets/Res/Effect" },
        };

        private static readonly string[] ModelExtensions =
        {
            ".fbx", ".obj", ".dae", ".3ds", ".dxf", ".skp", ".blend",
        };

        public static FolderKind GetFolderKind(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return FolderKind.None;

            string path = assetPath.Replace('\\', '/');
            foreach (var pair in SourceFolders)
            {
                if (path.StartsWith(pair.Value + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Key;
                }
            }

            return FolderKind.None;
        }

        /// <summary>
        /// 由预制体路径推断资源类型（按 PrefabFolders 映射），用于确定减面阈值。
        /// </summary>
        public static FolderKind GetPrefabFolderKind(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath)) return FolderKind.None;

            string path = prefabPath.Replace('\\', '/');
            foreach (var pair in PrefabFolders)
            {
                if (path.StartsWith(pair.Value + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Key;
                }
            }

            return FolderKind.None;
        }

        public static bool IsModelAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;

            string extension = Path.GetExtension(assetPath);
            if (string.IsNullOrEmpty(extension)) return false;

            foreach (string modelExtension in ModelExtensions)
            {
                if (string.Equals(extension, modelExtension, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        /// <summary>
        /// 判断资源是否为本工具生成物，避免重复减面。
        /// </summary>
        public static bool IsGeneratedAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;

            string path = assetPath.Replace('\\', '/');
            return path.IndexOf(LodSuffix + "/", StringComparison.OrdinalIgnoreCase) >= 0
                   || path.EndsWith(LodSuffix + ".prefab", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// LOD 根目录：模型文件取其文件名（Assets/Roles/x/x.fbx → Assets/Roles/x/x_LOD1），
        /// 其它网格资产取其所在目录（Assets/Roles/x/m.asset → Assets/Roles/x_LOD1）。
        /// </summary>
        public static string GetLodRoot(string modelAssetPath)
        {
            if (string.IsNullOrEmpty(modelAssetPath)) return string.Empty;

            string path = modelAssetPath.Replace('\\', '/');
            string basePath = IsModelAsset(path)
                ? path.Substring(0, path.Length - Path.GetExtension(path).Length)
                : Path.GetDirectoryName(path) ?? string.Empty;

            return basePath.TrimEnd('/') + LodSuffix;
        }

        public static string GetLodPrefabPath(string prefabPath)
        {
            string directory = Path.GetDirectoryName(prefabPath) ?? "Assets";
            string name = Path.GetFileNameWithoutExtension(prefabPath);
            return directory.Replace('\\', '/') + "/" + name + LodSuffix + ".prefab";
        }
    }
}
