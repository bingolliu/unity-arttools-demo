using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace InstaLODTools
{
    public class InstaLODToolsUtil
    {
        /// <summary>
        /// 获取Mesh所在的文件夹类型
        /// </summary>
        /// <param name="mesh"></param>
        /// <returns></returns>
        public static FolderType GetFolderTypeByMesh(Mesh mesh)
        {
            FolderType folderType = FolderType.None;
            string path = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(path))
            {
                return folderType;
            }

            foreach (var (key, value) in InstaLODToolsConst.FBXFolderPathDic)
            {
                if (path.Contains(value))
                {
                    folderType = key;
                    break;
                }
            }

            return folderType;
        }

        /// <summary>
        /// 获取物体的层级路径
        /// </summary>
        /// <param name="gameObject"></param>
        /// <returns></returns>
        public static string GetHierarchyPath(GameObject gameObject)
        {
            string delimiter = "/";
            StringBuilder path = new StringBuilder();
            path.Append(gameObject.name);
            while (gameObject.transform.parent != null)
            {
                gameObject = gameObject.transform.parent.gameObject;
                path.Insert(0, gameObject.name + delimiter);
            }

            //去掉根节点
            string output = path.ToString();
            int firstIndexOf = output.IndexOf(delimiter, StringComparison.Ordinal);
            output = output.Substring(firstIndexOf + 1);

            return output;
        }

        /// <summary>
        /// 测试输出
        /// </summary>
        /// <param name="fileCmdData"></param>
        /// <param name="fbxMeshPrefabData"></param>
        /// <param name="cmdStr"></param>
        public static void TestOutPut(List<SourceWithTargetPath> fileCmdData, Dictionary<string, Dictionary<string, List<MeshPrefabData>>> fbxMeshPrefabData, string cmdStr)
        {
            StringBuilder sb = new StringBuilder();
            for (var i = 0; i < fileCmdData.Count; i++)
            {
                SourceWithTargetPath pathData = fileCmdData[i];
                sb.AppendLine("FBX源路径: " + pathData.SourcePath);
                sb.AppendLine("减面后的FBX输出路径: " + pathData.TargetPath);
                
                if (!fbxMeshPrefabData.TryGetValue(pathData.SourcePath, out Dictionary<string, List<MeshPrefabData>> meshPrefabData))
                {
                    continue;
                }
                
                sb.AppendLine("mesh在预制体中的信息:");
                foreach (var pair in meshPrefabData)
                {
                    sb.AppendLine("mesh名: " + pair.Key);
                    sb.Append("减面后的mesh输出路径: ");
                    sb.Append(pathData.MeshPathDirectory);
                    sb.AppendFormat(InstaLODToolsConst.OutPutMeshNameFormat, pair.Key);
                    sb.AppendLine();
                    List<MeshPrefabData> prefabData = pair.Value;
                    for (var j = 0; j < prefabData.Count; j++)
                    {
                        MeshPrefabData temp = prefabData[i];
                        sb.AppendLine("预制体路径: " + temp.PrefabPath);
                        sb.AppendLine("层级路径: " + temp.HierarchyPath);
                    }
                }

                sb.AppendLine();
            }

            sb.AppendLine("指令: ");
            sb.AppendLine(cmdStr);

            PopupWindow.ShowPopup("测试输出", sb.ToString());
        }
        
        /// <summary>
        /// fbx设置拷贝
        /// </summary>
        /// <param name="sourcePath">源路径</param>
        /// <param name="targetPath">目标路径</param>
        public static void FBXSettingCopy(string sourcePath, string targetPath)
        {
            // 生成的FBX文件拷贝原FBX文件的设置
            ModelImporter originImporter = AssetImporter.GetAtPath(sourcePath) as ModelImporter;
            ModelImporter nowImporter = AssetImporter.GetAtPath(targetPath) as ModelImporter;

            if (originImporter == null || nowImporter == null)
            {
                return;
            }
            
            EditorUtility.CopySerialized(originImporter, nowImporter);
            nowImporter.materialImportMode = ModelImporterMaterialImportMode.None;
            AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate);
        }
    }
}

