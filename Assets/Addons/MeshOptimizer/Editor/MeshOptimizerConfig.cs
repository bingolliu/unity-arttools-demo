using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MeshOptimizer
{
    /// <summary>
    /// 文件夹阈值配置的读写。使用 JsonUtility，避免额外依赖 LitJson。
    /// </summary>
    public static class MeshOptimizerConfig
    {
        public static string AbsoluteConfigPath
        {
            get
            {
                return Path.Combine(Application.dataPath, "Addons/MeshOptimizer/Config/MeshOptimizerFolderConfig.json");
            }
        }

        public static MeshOptimizerConfigData Load()
        {
            try
            {
                if (File.Exists(AbsoluteConfigPath))
                {
                    string json = File.ReadAllText(AbsoluteConfigPath, Encoding.UTF8);
                    var data = JsonUtility.FromJson<MeshOptimizerConfigData>(json);
                    if (data != null)
                    {
                        Normalize(data);
                        return data;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MeshOptimizer] 读取配置失败，将使用默认配置：" + e.Message);
            }

            var defaults = CreateDefault();
            Save(defaults);
            return defaults;
        }

        public static void Save(MeshOptimizerConfigData data)
        {
            if (data == null) return;

            Normalize(data);
            string directory = Path.GetDirectoryName(AbsoluteConfigPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(AbsoluteConfigPath, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(MeshOptimizerPaths.ConfigPath);
        }

        public static Dictionary<FolderKind, FolderReMeshConfig> BuildMap(MeshOptimizerConfigData data)
        {
            var map = new Dictionary<FolderKind, FolderReMeshConfig>();
            if (data == null || data.FolderReMeshConfigs == null) return map;

            foreach (var config in data.FolderReMeshConfigs)
            {
                if (config == null || string.IsNullOrEmpty(config.FolderPath)) continue;
                if (Enum.TryParse(config.FolderPath, true, out FolderKind kind) && kind != FolderKind.None)
                {
                    map[kind] = config;
                }
            }

            return map;
        }

        private static void Normalize(MeshOptimizerConfigData data)
        {
            if (data.FolderReMeshConfigs == null)
            {
                data.FolderReMeshConfigs = new List<FolderReMeshConfig>();
            }

            foreach (var kind in new[] { FolderKind.Roles, FolderKind.Equip, FolderKind.Scenes, FolderKind.Effect })
            {
                var config = data.FolderReMeshConfigs.Find(
                    item => item != null && string.Equals(item.FolderPath, kind.ToString(), StringComparison.OrdinalIgnoreCase));

                if (config == null)
                {
                    config = new FolderReMeshConfig { FolderPath = kind.ToString() };
                    data.FolderReMeshConfigs.Add(config);
                }

                config.FolderPath = kind.ToString();
                config.MaxTriangles = Mathf.Max(1, config.MaxTriangles);
                if (config.IgnoreFBXFiles == null)
                {
                    config.IgnoreFBXFiles = new List<string>();
                }

                for (int i = 0; i < config.IgnoreFBXFiles.Count; i++)
                {
                    config.IgnoreFBXFiles[i] = (config.IgnoreFBXFiles[i] ?? string.Empty).Trim().ToLowerInvariant();
                }
            }
        }

        private static MeshOptimizerConfigData CreateDefault()
        {
            var data = new MeshOptimizerConfigData();
            foreach (var kind in new[] { FolderKind.Roles, FolderKind.Equip, FolderKind.Scenes, FolderKind.Effect })
            {
                data.FolderReMeshConfigs.Add(new FolderReMeshConfig
                {
                    FolderPath = kind.ToString(),
                    MaxTriangles = 200,
                    IgnoreFBXFiles = new List<string>()
                });
            }

            return data;
        }
    }
}
