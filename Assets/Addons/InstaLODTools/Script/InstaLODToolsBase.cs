using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace InstaLODTools
{
    /// <summary>
    /// 减面类型
    /// </summary>
    public enum ReMeshType
    {
        /// <summary>
        /// 百分比减面
        /// </summary>
        Percentage,
        /// <summary>
        /// 目标面数减面
        /// </summary>
        AbsoluteCount
    }

    /// <summary>
    /// 操作类型
    /// </summary>
    public enum OperationType
    {
        /// <summary>
        /// 多个预制体文件
        /// </summary>
        Prefab,
        /// <summary>
        /// 多个文件夹
        /// </summary>
        Folder
    }

    /// <summary>
    /// 资源所在的文件夹类型
    /// </summary>
    public enum FolderType
    {
        None,
        Roles,
        Equip,
        Scenes,
        Effect,

        //AATest,
    }

    /// <summary>
    /// 操作数据
    /// </summary>
    public struct OperationData
    {
        public OperationType OperationType;
        public List<GameObject> SelectedPrefab;
        public List<string> SelectedFolders;
    }

    /// <summary>
    /// 网格数据
    /// </summary>
    public struct MeshData
    {
        public Mesh Mesh;
        public string HierarchyPath;
    }

    /// <summary>
    /// 源路径，输出路径
    /// </summary>
    public class SourceWithTargetPath
    {
        /// <summary>
        /// FBX源路径
        /// </summary>
        public string SourcePath;
        /// <summary>
        /// 减面后的FBX输出路径
        /// </summary>
        public string TargetPath;
        /// <summary>
        /// mesh输出路径
        /// </summary>
        public string MeshPathDirectory;
        /// <summary>
        /// mesh在预制体中的信息
        /// key: mesh的名
        /// value: mesh在预制体中的信息
        /// </summary>
        // public Dictionary<string, SourceWithTargetMeshPrefabData> MeshPrefabData;
        
        /// <summary>
        /// 已改变的mesh名
        /// </summary>
        public List<string> ChangedMeshNames;
        /// <summary>
        /// 用于拼接的指令
        /// </summary>
        public string Command;

        /// <summary>
        /// 是否已生成Mesh文件
        /// </summary>
        public bool IsCreatedMesh;
    }

    /// <summary>
    /// mesh在预制体中的信息
    /// </summary>
    public class SourceWithTargetMeshPrefabData
    {
        public string OutPutMeshPath;
        /// <summary>
        /// mesh在预制体中的层级路径对应关系
        /// key: 预制体路径
        /// value：mesh在预制体中的层级路径
        /// </summary>
        public Dictionary<string, List<string>> MeshPathInHierarchy;
    }
    
    public class MeshPrefabData
    {
        public string PrefabPath;
        public string HierarchyPath;
    }

    /// <summary>
    /// 预制体变化数据
    /// </summary>
    public class PrefabChangedData
    {
        public GameObject Prefab;
        public bool isChanged;
        public GameObject OriginPrefab;
    }
    /******************************************************* ↓ JSON Class ↓ *************************************************************/

    /// <summary>
    /// Profile配置类 骨骼优化数据
    /// </summary>
    [Serializable]
    public class ProfileSkeletonOptimize
    {
        public float LeafBoneWeldDistance;
        public int MaximumBoneDepth;
        public int MaximumBoneInfluencesPerVertex;
        public float MinimumBoneInfluenceThreshold;
        public string IgnoreJointRegEx;
    }

    /// <summary>
    /// Profile配置类 网格优化入口设置数据
    /// </summary>
    [Serializable]
    public class ProfileMeshOperationEntrySettings
    {
        public string AlgorithmStrategy;
        public float PercentTriangles;
        public int AbsoluteTriangles;
        public int ScreenSizeInPixels;
        public float MaxDeviation;
        public bool OverrideSmoothingGroups;
        public float WeldingThreshold;
        public bool WeldingProtectDistinctUVShells;
        public float WeldingNormalAngleThreshold;
        public float HealTJunctionThreshold;
        public bool OptimizerVertexWeights;
        public bool OptimalPlacement;
        public bool RecalculateNormals;
        public float HardAngleThreshold;
        public bool WeightedNormals;
        public string NormalHealingMode;
        public bool LockBoundaries;
        public bool LockSplits;
        public bool ProtectSplits;
        public bool ProtectBoundaries;
        public string BoundaryImportance;
        public string TextureImportance;
        public string ShadingImportance;
        public string SilhouetteImportance;
        public string SkinningImportance;
        [SerializeField]
        public ProfileSkeletonOptimize SkeletonOptimize;
    }

    /// <summary>
    /// Profile配置类 网格优化入口数据
    /// </summary>
    [Serializable]
    public class ProfileMeshOperationEntry
    {
        public string Name;
        public string Type;
        public string MeshNameFilterRegEx;
        public bool ForceOptimization;
        public string OptimizerWeightsColorSetNameRegEx;
        public int OptimizerWeightsColorIndex;
        public string OptimizerWeightsSelectionSetNameRegEx;
        [SerializeField]
        public ProfileMeshOperationEntrySettings Settings;
    }

    /// <summary>
    /// Profile配置类 网格优化数据
    /// </summary>
    [Serializable]
    public class ProfileMeshOperation
    {
        public bool GlobalScope;
        public bool UsePreviousOutputAsInput;
        public bool PreserveInputMesh;
        [SerializeField]
        public ProfileMeshOperationEntry[] Entries;
    }

    /// <summary>
    /// Profile配置类 导出数据
    /// </summary>
    [Serializable]
    public class ProfileExport
    {
        public bool FBXEmbedMedia;
        public bool FBXGroupByMeshOperationEntry;
        public bool FBXGroupApplyLODGroupAttribute;
        public bool FBXGroupCreateInRoot;
        public bool FBXExportUVs;
        public bool FBXExportNormals;
        public bool FBXExportBinormalsAndTangents;
        public bool FBXExportSmoothingGroups;
        public bool FBXExportColors;
        public bool FBXExportEmptyMeshes;
        public bool FBXExportEmptyNodes;
    }

    /// <summary>
    /// Profile配置类 数据
    /// </summary>
    [Serializable]
    public class ProfileData
    {
        public string Version;
        public string Name;
        public string Description;
        [SerializeField]
        public ProfileExport Export;
        public string Operation;
        public bool CombineInputMeshes;
        public string SmoothingGroupImport;
        [SerializeField]
        public ProfileMeshOperation MeshOperation;
    }

    /// <summary>
    /// 文件夹网格处理配置数据
    /// </summary>
    [Serializable]
    public class FolderReMeshConfigData
    {
        /// <summary>
        /// 文件夹路径
        /// </summary>
        public string FolderPath;
        /// <summary>
        /// 最大三角面数
        /// </summary>
        public int MaxTriangles;
        /// <summary>
        /// 需要忽略的FBX文件名
        /// </summary>
        public List<string> IgnoreFBXFiles;
    }

    /// <summary>
    /// 文件夹配置数据
    /// </summary>
    [Serializable]
    public class FolderConfigData
    {
        public List<FolderReMeshConfigData> FolderReMeshConfigs;
    }

    /******************************************************* ↑ JSON Class ↑ *************************************************************/
}
