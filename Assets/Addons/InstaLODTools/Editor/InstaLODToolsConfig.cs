using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SRSC.LitJson;
using UnityEngine;

namespace InstaLODTools
{
    public class InstaLODToolsConfig
    {
        /// <summary>
        /// json数据
        /// </summary>
        private static FolderConfigData _jsonData;

        /// <summary>
        /// 获取配置
        /// </summary>
        /// <returns></returns>
        public static FolderConfigData GetConfig()
        {
            if (_jsonData == null)
            {
                InitConfig();
            }

            return _jsonData;
        }

        /// <summary>
        /// 初始化配置
        /// </summary>
        public static void InitConfig()
        {
            string dataPath = Application.dataPath;
            string jsonPath = dataPath + InstaLODToolsConst.FolderConfigJsonFilePath;
            string text = File.ReadAllText(jsonPath);
            _jsonData = JsonMapper.ToObject<FolderConfigData>(text);

            List<FolderReMeshConfigData> configs = _jsonData.FolderReMeshConfigs;
            if (configs.Count == 0)
            {
                FolderType[] folderTypes = (FolderType[])System.Enum.GetValues(typeof(FolderType));
                for (var i = 0; i < folderTypes.Length; i++)
                {
                    if (folderTypes[i] == FolderType.None)
                    {
                        continue;
                    }

                    string folderPath = folderTypes[i].ToString();
                    FolderReMeshConfigData data = new FolderReMeshConfigData();
                    data.FolderPath = folderPath;
                    data.MaxTriangles = 10000;
                    data.IgnoreFBXFiles = new List<string>();
                    configs.Add(data);
                }

                _jsonData.FolderReMeshConfigs = configs;

                SaveConfig(_jsonData);
            }
        }

        /// <summary>
        /// 保存配置
        /// </summary>
        public static void SaveConfig(FolderConfigData saveData)
        {
            if (saveData == null)
            {
                return;
            }

            try
            {
                _jsonData = saveData;
                foreach (FolderReMeshConfigData data in _jsonData.FolderReMeshConfigs)
                {
                    data.MaxTriangles = Mathf.Max(1000, data.MaxTriangles);
                }

                string dataPath = Application.dataPath;
                string jsonPath = dataPath + InstaLODToolsConst.FolderConfigJsonFilePath;
                File.WriteAllText(jsonPath, JsonUtility.ToJson(_jsonData, true));

                UnityEditor.EditorUtility.DisplayDialog("保存结果", "保存成功！", "确定");
            }
            catch (Exception e)
            {
                UnityEditor.EditorUtility.DisplayDialog("保存结果", "保存失败！！！\n" + e, "确定");
                throw;
            }

        }
    }
}


