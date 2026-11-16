using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace InstaLODTools
{
    public class InstaLODToolsConst
    {
        /// <summary>
        /// 配置文件名
        /// </summary>
        public static readonly string JsonFileName = "Optimize_SESC";
        /// <summary>
        /// 配置名
        /// </summary>
        public static readonly string ProfileName = "OptimizeSESC";
        /// <summary>
        /// 文件夹配置文件路径
        /// </summary>
        public static readonly string FolderConfigJsonFilePath = "\\Addons\\InstaLODTools\\Config\\InstaLODToolsFolderConfig.json";

        /// <summary>
        /// 配置路径格式
        /// 0：工程名
        /// 1：配置文件名 JsonFileName
        /// </summary>
        public static readonly string JsonPathFormat = "{0}\\tool\\InstaLODPipeline\\Profiles\\{1}.json";
        /// <summary>
        /// 输出文件路径格式
        /// 0：各文件夹FBX文件夹路径
        /// 1：FBX文件名
        /// </summary>
        public static readonly string OutPutFilePathFormat = "{0}\\{1}_LOD1.fbx";
        /// <summary>
        /// 输出网格文件夹路径格式
        /// 0：各文件夹输出网格文件夹路径
        /// </summary>
        public static readonly string OutPutMeshPathDirectoryFormat = "{0}\\Mesh\\";
        /// <summary>
        /// 输出网格名格式
        /// 0：网格名
        /// </summary>
        public static readonly string OutPutMeshNameFormat = "{0}.asset";

        /// <summary>
        /// 进入InstaLOD管线路径命令
        /// 0：项目路径
        /// </summary>
        public static readonly string CdCmdFormat = "cd {0}\\tool\\InstaLODPipeline && ";
        /// <summary>
        /// 配置文件命令
        /// 0：配置文件名
        /// </summary>
        public static readonly string ProfileCmdFormat = "InstaLODCmd -profile Profiles\\{0}.json ";
        /// <summary>
        /// 单文件处理命令
        /// 0：目标fbx文件绝对路径
        /// 1：减面后fbx文件保存路径
        /// 2：配置名
        /// </summary>
        public static readonly string FileCmdFormat = "-file {0} {1} {2} ";

        /// <summary>
        /// FBX所在文件夹类型对应路径
        /// </summary>
        public static readonly Dictionary<FolderType, string> FBXFolderPathDic = new Dictionary<FolderType, string>()
        {
            {FolderType.Roles, "Assets/Roles"},
            {FolderType.Equip, "Assets/Equip"},
            {FolderType.Scenes, "Assets/Scenes"},
            {FolderType.Effect, "Assets/Effect"},

            //{FolderType.AATest, "Assets/AATest"},
        };

        /// <summary>
        /// 预制体所在文件夹类型对应路径
        /// </summary>
        public static readonly Dictionary<FolderType, string> PrefabFolderPathDic = new Dictionary<FolderType, string>()
        {
            {FolderType.Roles, "Assets/Res/Roles"},
            {FolderType.Equip, "Assets/Res/Equip"},
            {FolderType.Scenes, "Assets/Scenes"},
            {FolderType.Effect, "Assets/Res/Effect"},

            //{FolderType.AATest, "Assets/AATest/Prefab"},
        };
    }
}


