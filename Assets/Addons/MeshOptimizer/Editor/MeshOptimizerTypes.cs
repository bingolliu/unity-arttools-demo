using System;
using System.Collections.Generic;
using UnityEngine;

namespace MeshOptimizer
{
    /// <summary>
    /// 减面目标的计算方式。
    /// </summary>
    public enum ReMeshType
    {
        Percentage,
        AbsoluteCount
    }

    /// <summary>
    /// 处理入口类型。
    /// </summary>
    public enum OperationType
    {
        Prefab,
        Folder
    }

    /// <summary>
    /// 美术资源目录类型（与模型所在目录一一对应）。
    /// </summary>
    public enum FolderKind
    {
        None,
        Roles,
        Equip,
        Scenes,
        Effect
    }

    /// <summary>
    /// 单个资源目录的减面阈值配置。
    /// </summary>
    [Serializable]
    public class FolderReMeshConfig
    {
        public string FolderPath = string.Empty;
        public int MaxTriangles = 200;
        public List<string> IgnoreFBXFiles = new List<string>();
    }

    [Serializable]
    public class MeshOptimizerConfigData
    {
        public List<FolderReMeshConfig> FolderReMeshConfigs = new List<FolderReMeshConfig>();
    }

    public struct MeshOptimizeRequest
    {
        public OperationType OperationType;
        public List<GameObject> Prefabs;
        public List<string> Folders;
        public ReMeshType ReMeshType;
        public float TargetPercentage;
        public int TargetTriangleCount;
        public bool ReplaceSourcePrefab;
        public bool GenerateLodPrefab;
    }
}
