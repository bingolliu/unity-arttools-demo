using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SRSC.LitJson;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InstaLODTools
{
    /// <summary>
    /// 减面工具管理器
    /// </summary>
    public class InstaLODToolsManager
    {
        /// <summary>
        /// json数据
        /// </summary>
        private static ProfileData _jsonData;
        /// <summary>
        /// 文件夹配置数据
        /// </summary>
        private static Dictionary<FolderType, FolderReMeshConfigData> _folderConfigData = new Dictionary<FolderType, FolderReMeshConfigData>();

        private static string _projectPath;

        /// <summary>
        /// fbx相关数据
        /// key: fbx path
        /// </summary>
        private static Dictionary<string, SourceWithTargetPath> _fbxDic = new Dictionary<string, SourceWithTargetPath>();

        /// <summary>
        /// 预制体暂存
        /// key: prefab path
        /// value: prefab Object
        /// </summary>
        private static Dictionary<string, PrefabChangedData> _prefabDic = new Dictionary<string, PrefabChangedData>();

        private static bool _isPause = false;
        private static bool _nowPause = false;
        
        /// <summary>
        /// 记录fbx文件中的网格和预制体的对应关系
        /// key: fbx路径
        /// value: { key: mesh名字, value: prefab路径 } 
        /// </summary>
        private static Dictionary<string, Dictionary<string, List<MeshPrefabData>>> _fbxMeshPrefabData = new Dictionary<string, Dictionary<string, List<MeshPrefabData>>>();

        /// <summary>
        /// 优化网格
        /// </summary>
        /// <param name="reMeshType">减面类型</param>
        /// <param name="targetPercentage">目标百分比</param>
        /// <param name="targetFaceCount">目标面数</param>
        /// <param name="operationData">操作数据</param>
        /// <param name="overwriteSource">是否覆盖源fbx文件</param>
        /// <param name="autoReplaceMesh">是否自动替换预制中的Mesh</param>
        /// <param name="generatePrefab">是否生成LOD预制体</param>
        public static void StartOptimize(ReMeshType reMeshType, float targetPercentage, int targetFaceCount,
            OperationData operationData, bool overwriteSource, bool autoReplaceMesh, bool generatePrefab)
        {
            UpdateJsonData(reMeshType, targetPercentage, targetFaceCount);
            UpdateFolderConfigData();
            Clear();

            //1. 拼接前缀
            string dataPath = Application.dataPath;
            _projectPath = Path.GetDirectoryName(dataPath);
            StringBuilder cmdBuilder = new StringBuilder();
            cmdBuilder.AppendFormat(InstaLODToolsConst.CdCmdFormat, _projectPath);
            cmdBuilder.AppendFormat(InstaLODToolsConst.ProfileCmdFormat, InstaLODToolsConst.JsonFileName);

            //2. 根据操作类型处理
            List<SourceWithTargetPath> fileCmdData = new List<SourceWithTargetPath>();
            if (operationData.OperationType == OperationType.Prefab)
            {
                fileCmdData.AddRange(OptimizePrefabList(operationData.SelectedPrefab));
            }else if (operationData.OperationType == OperationType.Folder)
            {
                fileCmdData.AddRange(OptimizeFolder(operationData.SelectedFolders));
            }

            if (fileCmdData.Count == 0)
            {
                return;
            }

            //3. 拼接命令
            for (var i = 0; i < fileCmdData.Count; i++)
            {
                SourceWithTargetPath cmdData = fileCmdData[i];
                cmdBuilder.Append(cmdData.Command);
            }

            Action<string> outPutCallBack = (output) =>
            {
                _isPause = output.Contains("tasks finished") || output.Contains("Writing");
            };

            Action endCallBack = () =>
            {
                InstaLODToolsWindow.Clear();
                EditorApplication.update -= Update;
                
                //1. fbx设置拷贝、预制替换
                foreach (SourceWithTargetPath cmdData in fileCmdData)
                {
                    InstaLODToolsUtil.FBXSettingCopy(cmdData.SourcePath, cmdData.TargetPath);
                    bool isCreatedMesh = ReplacePrefab(cmdData, autoReplaceMesh, overwriteSource);
                    cmdData.IsCreatedMesh = isCreatedMesh;
                }

                //2. 应用已修改的预制体
                ApplyChangedPrefab(generatePrefab);
                
                //3. 覆盖源文件（防止第一步时删除多引用fbx）
                foreach (SourceWithTargetPath cmdData in fileCmdData)
                {
                    OverwriteSource(cmdData, overwriteSource);
                }
                
                Clear();

                AssetDatabase.Refresh();
            };
            
            bool isTest = false;

            if (isTest)
            {
                InstaLODToolsUtil.TestOutPut(fileCmdData, _fbxMeshPrefabData,cmdBuilder.ToString());
                Clear();
                InstaLODToolsWindow.Clear();
                return;
            }

            _isPause = false;
            _nowPause = false;
            EditorApplication.update += Update;
            CommandWindow.Execute(cmdBuilder.ToString(), outPutCallBack, endCallBack);
        }

        private static void Update()
        {
            if (_nowPause != _isPause)
            {
                _nowPause = _isPause;
                if(!_nowPause)
                    EditorUtility.ClearProgressBar();
            }
            
            if (_nowPause)
            {
                EditorUtility.DisplayProgressBar("导出FBX中", "请等待导出完成...", 0.5f);
            }
        }

        /// <summary>
        /// 更新shader数据
        /// </summary>
        /// <param name="reMeshType">减面类型</param>
        /// <param name="targetPercentage">目标百分比</param>
        /// <param name="targetFaceCount">目标面数</param>
        private static void UpdateJsonData(ReMeshType reMeshType, float targetPercentage, int targetFaceCount)
        {
            string dataPath = Application.dataPath;
            string projectPath = Path.GetDirectoryName(dataPath);
            string jsonPath = string.Format(InstaLODToolsConst.JsonPathFormat, projectPath, InstaLODToolsConst.JsonFileName);
            string text = File.ReadAllText(jsonPath);
            _jsonData = JsonMapper.ToObject<ProfileData>(text);

            if (reMeshType == ReMeshType.Percentage)
            {
                targetFaceCount = 0;
            }
            _jsonData.MeshOperation.Entries[0].Settings.PercentTriangles = targetPercentage / 100;
            _jsonData.MeshOperation.Entries[0].Settings.AbsoluteTriangles = targetFaceCount;

            File.WriteAllText(jsonPath, JsonUtility.ToJson(_jsonData, true));
        }

        /// <summary>
        /// 更新文件夹配置数据
        /// </summary>
        private static void UpdateFolderConfigData()
        {
            _folderConfigData.Clear();

            FolderConfigData folderConfigData = InstaLODToolsConfig.GetConfig();
            List<FolderReMeshConfigData> configs = folderConfigData.FolderReMeshConfigs;
            foreach (FolderReMeshConfigData data in configs)
            {
                FolderType folderType;
                if (FolderType.TryParse(data.FolderPath, out folderType))
                {
                    _folderConfigData.Add(folderType, data);
                }
            }
        }

        /// <summary>
        /// 根据预制体列表优化
        /// </summary>
        /// <param name="selectedPrefab">已选择的预制体</param>
        /// <returns></returns>
        private static List<SourceWithTargetPath> OptimizePrefabList(List<GameObject> selectedPrefab)
        {
            List<SourceWithTargetPath> result = new List<SourceWithTargetPath>();
            for (var i = 0; i < selectedPrefab.Count; i++)
            {
                GameObject prefab = selectedPrefab[i];
                OptimizePrefab(prefab);
            }

            result.AddRange(_fbxDic.Values);

            return result;
        }

        /// <summary>
        /// 根据文件夹列表优化
        /// </summary>
        /// <param name="selectedFolders">已选择的文件夹</param>
        /// <returns></returns>
        private static List<SourceWithTargetPath> OptimizeFolder(List<string> selectedFolders)
        {
            List<SourceWithTargetPath> result = new List<SourceWithTargetPath>();
            for (var i = 0; i < selectedFolders.Count; i++)
            {
                string folderPath = selectedFolders[i];
                foreach (string file in Directory.GetFiles(folderPath, "*.prefab", SearchOption.AllDirectories))
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(file);
                    OptimizePrefab(prefab);
                }
            }

            result.AddRange(_fbxDic.Values);

            return result;
        }

        /// <summary>
        /// 处理单个预制
        /// </summary>
        /// <param name="prefab">目标预制体</param>
        private static void OptimizePrefab(GameObject prefab)
        {
            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            if (!prefabPath.EndsWith(".prefab"))
            {
                Debug.LogWarning(prefab.name +" 不是预制体文件");
                return;
            }

            //1. 构建网格数据
            List<Component> meshComponents = new List<Component>();
            meshComponents.AddRange(prefab.GetComponentsInChildren<MeshFilter>(true));
            meshComponents.AddRange(prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true));

            //2. 处理网格数据
            for (var j = 0; j < meshComponents.Count; j++)
            {
                Component component = meshComponents[j];
                Mesh mesh = component is MeshFilter
                    ? ((MeshFilter)component).sharedMesh
                    : ((SkinnedMeshRenderer)component).sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                //2.1 判断文件夹类型
                FolderType folderType = InstaLODToolsUtil.GetFolderTypeByMesh(mesh);
                if (folderType == FolderType.None)
                {
                    continue;
                }

                _folderConfigData.TryGetValue(folderType, out FolderReMeshConfigData folderReMeshConfigData);
                if (folderReMeshConfigData == null)
                {
                    continue;
                }

                //2.2 实例化预制并存储
                if (!_prefabDic.ContainsKey(prefabPath))
                {
                    //实例化预制
                    GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                    PrefabChangedData changedData = new PrefabChangedData()
                    {
                        Prefab = instance,
                        isChanged = false,
                        OriginPrefab = prefab
                    };
                    _prefabDic.Add(prefabPath, changedData);
                }
                
                string path = AssetDatabase.GetAssetPath(mesh);
                string meshName = mesh.name;

                //2.3 记录网格和预制体的对应关系
                if (!_fbxMeshPrefabData.TryGetValue(path, out Dictionary<string, List<MeshPrefabData>> meshPrefabDic))
                {
                    meshPrefabDic = new Dictionary<string, List<MeshPrefabData>>();
                    _fbxMeshPrefabData.Add(path, meshPrefabDic);
                }
                
                if (!_fbxMeshPrefabData[path].TryGetValue(meshName, out List<MeshPrefabData> meshPrefabDatas))
                {
                    meshPrefabDatas = new List<MeshPrefabData>();
                    _fbxMeshPrefabData[path].Add(meshName, meshPrefabDatas);
                }
                
                _fbxMeshPrefabData[path][meshName].Add(new MeshPrefabData()
                {
                    PrefabPath = prefabPath,
                    HierarchyPath = InstaLODToolsUtil.GetHierarchyPath(component.gameObject)
                });

                //2.4 目标网格小于等于最大面数，或者在忽略列表中
                int realTriangles = mesh.triangles.Length / 3;
                if (realTriangles <= folderReMeshConfigData.MaxTriangles
                    || folderReMeshConfigData.IgnoreFBXFiles.Contains(Path.GetFileNameWithoutExtension(path).ToLower()))
                {
                    continue;
                }

                string hierarchyPath = InstaLODToolsUtil.GetHierarchyPath(component.gameObject);

                //2.5 未处理过的网格，生成路径数据
                if (!_fbxDic.ContainsKey(path))
                {
                    SourceWithTargetPath data = OptimizeMesh(mesh, folderType);
                    if (data != null)
                    {
                        _fbxDic.Add(path, data);
                    }
                }

                _fbxDic.TryGetValue(path, out SourceWithTargetPath savedData);
                if (savedData == null)
                {
                    continue;
                }

                //2.6 记录已修改的网格名
                if (savedData.ChangedMeshNames == null)
                {
                    savedData.ChangedMeshNames = new List<string>();
                }

                if (savedData.ChangedMeshNames.Contains(meshName))
                {
                    continue;
                }
                
                savedData.ChangedMeshNames.Add(meshName);
            }
        }

        /// <summary>
        /// 处理单个FBX
        /// </summary>
        /// <param name="mesh">目标网格</param>
        /// <param name="folderType">文件夹类型</param>
        private static SourceWithTargetPath OptimizeMesh(Mesh mesh, FolderType folderType)
        {
            string path = AssetDatabase.GetAssetPath(mesh);
            //1. 判断文件夹类型
            if (string.IsNullOrEmpty(path) || folderType == FolderType.None)
            {
                return null;
            }

            string fileName = Path.GetFileNameWithoutExtension(path);
            string fullPath = Path.GetFullPath(path);
            string fullDirectory = Path.GetDirectoryName(fullPath) + "_LOD1";
            string outPutFilePath = string.Format(InstaLODToolsConst.OutPutFilePathFormat, fullDirectory, fileName);
            string outPutAssetPath = outPutFilePath.Replace(_projectPath, "").Substring(1);
            string outPutMeshPathDirectory = null, parentDirectory = null;
            //2. 根据文件夹类型
            switch (folderType)
            {
                case FolderType.Roles:
                    // 工程名/Assets/Roles/xxx/Mesh/网格名_ReMesh.asset
                    outPutMeshPathDirectory = string.Format(InstaLODToolsConst.OutPutMeshPathDirectoryFormat, fullDirectory, mesh.name);
                    break;
                case FolderType.Equip:
                    // 工程名/Assets/Equip/xxx/xxx/Mesh/网格名_ReMesh.asset
                    outPutMeshPathDirectory = string.Format(InstaLODToolsConst.OutPutMeshPathDirectoryFormat, fullDirectory, mesh.name);
                    break;
                case FolderType.Scenes:
                    // 工程名/Assets/Scenes/xxx/Mesh/网格名_ReMesh.asset
                    parentDirectory = Path.GetDirectoryName(fullDirectory);
                    outPutMeshPathDirectory = string.Format(InstaLODToolsConst.OutPutMeshPathDirectoryFormat, parentDirectory, mesh.name);
                    break;
                case FolderType.Effect:
                    // 工程名/Assets/Effect/Mesh/FBX名/网格名_ReMesh.asset
                    string directoryPath = _projectPath + "\\Effect\\";
                    outPutMeshPathDirectory = string.Format(InstaLODToolsConst.OutPutMeshPathDirectoryFormat, directoryPath, mesh.name);
                    outPutMeshPathDirectory += "fileName\\";
                    break;
                // case FolderType.AATest:
                //     parentDirectory = Path.GetDirectoryName(fullDirectory);
                //     outPutMeshPathDirectory = string.Format(InstaLODToolsConst.OutPutMeshPathDirectoryFormat, parentDirectory, mesh.name);
                //     break;
            }

            string fileCmd = string.Format(InstaLODToolsConst.FileCmdFormat, fullPath, outPutFilePath, InstaLODToolsConst.ProfileName);

            if (!string.IsNullOrEmpty(outPutMeshPathDirectory))
            {
                outPutMeshPathDirectory = outPutMeshPathDirectory.Replace(_projectPath + "\\", "");
            }
            // Debug.LogWarning(path);
            // Debug.LogWarning(fullPath);
            // Debug.LogWarning(outPutFilePath);
            // Debug.LogWarning(outPutAssetPath);
            // Debug.LogWarning(fileCmd);

            SourceWithTargetPath result = new SourceWithTargetPath();
            result.SourcePath = path;
            result.TargetPath = outPutAssetPath;
            result.MeshPathDirectory = outPutMeshPathDirectory;
            // result.MeshPrefabData = new Dictionary<string, SourceWithTargetMeshPrefabData>();
            result.Command = fileCmd;

            return result;
        }

        /// <summary>
        /// 预制替换
        /// </summary>
        /// <param name="cmdData">mesh输出数据</param>
        /// <param name="autoReplaceMesh">是否自动替换mesh</param>
        /// <param name="overwriteSource">是否覆盖源文件</param>
        /// <returns>是否已生成Mesh</returns>
        private static bool ReplacePrefab(SourceWithTargetPath cmdData, bool autoReplaceMesh, bool overwriteSource)
        {
            GameObject remeshFBX = AssetDatabase.LoadAssetAtPath<GameObject>(cmdData.TargetPath);
            Animator remeshAnimator = remeshFBX.GetComponent<Animator>();
            Avatar remeshAnimatorAvatar = null;
            if (remeshAnimator != null)
                remeshAnimatorAvatar = remeshAnimator.avatar;
            
            bool isCreatedMesh = false;
            //2. 对比生成后的FBX文件和原FBX文件的物理大小
            FileInfo originFileInfo = new FileInfo(cmdData.SourcePath);
            FileInfo nowFileInfo = new FileInfo(cmdData.TargetPath);
            if (nowFileInfo.Length >= originFileInfo.Length)
            {
                isCreatedMesh = true;
                //需要生成Mesh文件，则无需替换Avatar
                remeshAnimatorAvatar = null;
                Debug.LogWarning($"生成后的FBX文件(${nowFileInfo.Name})比原FBX(${originFileInfo.Name})文件大，请检查！");
                Debug.LogWarning($"${originFileInfo.Name}大小: " + originFileInfo.Length + $", 生成后的${nowFileInfo.Name}大小: " + nowFileInfo.Length);
            }
            
            List<Component> meshComponents = new List<Component>();
            meshComponents.AddRange(remeshFBX.GetComponentsInChildren<MeshFilter>(true));
            meshComponents.AddRange(remeshFBX.GetComponentsInChildren<SkinnedMeshRenderer>(true));

            _fbxMeshPrefabData.TryGetValue(cmdData.SourcePath, out Dictionary<string, List<MeshPrefabData>> fbxMeshPrefabData);
            if (fbxMeshPrefabData == null)
            {
                return false;
            }

            List<string> changedMeshNames = cmdData.ChangedMeshNames;

            foreach (Component component in meshComponents)
            {
                Mesh mesh = component is MeshFilter
                    ? ((MeshFilter)component).sharedMesh
                    : ((SkinnedMeshRenderer)component).sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                // 获取原始网格名
                string originMeshName = mesh.name.Replace("_ReMesh", "");
                if (!fbxMeshPrefabData.TryGetValue(originMeshName, out List<MeshPrefabData> meshPrefabDatas))
                {
                    continue;
                }

                //3. 是否需要生成Mesh
                if (isCreatedMesh)
                {
                    //检查文件夹是否存在
                    if (!Directory.Exists(cmdData.MeshPathDirectory))
                    {
                        Directory.CreateDirectory(cmdData.MeshPathDirectory);
                    }
                        
                    Mesh createdMesh = Object.Instantiate(mesh);
                    AssetDatabase.CreateAsset(createdMesh,
                        cmdData.MeshPathDirectory +
                        string.Format(InstaLODToolsConst.OutPutMeshNameFormat, mesh.name));
                    mesh = createdMesh;
                }
                
                //4. 自动替换mesh处理
                if (!autoReplaceMesh)
                {
                    continue;
                }

                //5. 如果不覆盖源文件，且该mesh未被修改过，则不替换
                //5.1 覆盖源文件，则所有mesh都需要替换
                if (!overwriteSource && !changedMeshNames.Contains(originMeshName))
                {
                    continue;
                }
                
                //6. 获取预制体路径
                for (var i = 0; i < meshPrefabDatas.Count; i++)
                {
                    MeshPrefabData temp = meshPrefabDatas[i];
                    string prefabPath = temp.PrefabPath;
                    string hierarchyPath = temp.HierarchyPath;
                    
                    PrefabChangedData changedData = _prefabDic[prefabPath];
                    GameObject prefab = changedData.Prefab;
                    if (prefab == null)
                    {
                        continue;
                    }
                    
                    //7 替换Avtar
                    Animator animator = prefab.GetComponent<Animator>();
                    if (remeshAnimatorAvatar != null && animator != null && animator.avatar != null)
                    {
                        Avatar avatar = animator.avatar;
                        string replaceAvatarName = avatar.name.Replace("Avatar", "");
                        if (remeshAnimatorAvatar.name.Contains(replaceAvatarName))
                        {
                            animator.avatar = remeshAnimatorAvatar;
                        }
                    }
                    
                    Transform child = prefab.transform.Find(hierarchyPath);
                    if (child == null)
                    {
                        continue;
                    }

                    //8. 替换mesh
                    MeshFilter filter = child.GetComponent<MeshFilter>();
                    SkinnedMeshRenderer renderer = child.GetComponent<SkinnedMeshRenderer>();
                    if (filter != null)
                    {
                        filter.sharedMesh = mesh;
                        changedData.isChanged = true;
                    }
                    else if (renderer != null)
                    {
                        renderer.sharedMesh = mesh;
                        changedData.isChanged = true;
                    }
                }
            }

            return isCreatedMesh;
        }

        /// <summary>
        /// 应用预制体
        /// </summary>
        private static void ApplyChangedPrefab(bool generatePrefab)
        {
            foreach (PrefabChangedData prefabChangedData in _prefabDic.Values)
            {
                if (prefabChangedData.isChanged)
                {
                    PrefabUtility.ApplyPrefabInstance(prefabChangedData.Prefab, InteractionMode.AutomatedAction);
                }

                if (generatePrefab) {
                    PrefabLODTool.CreateLOD1PrefabByPrefab(prefabChangedData.OriginPrefab);
                }
            }
        }

        /// <summary>
        /// 替换源文件
        /// </summary>
        /// <param name="cmdData">mesh输出数据</param>
        /// <param name="overwriteSource">是否需要覆盖源</param>
        private static void OverwriteSource(SourceWithTargetPath cmdData, bool overwriteSource)
        {
            if (cmdData.IsCreatedMesh)
            {
                //删除新文件
                AssetDatabase.DeleteAsset(cmdData.TargetPath);
                return;
            }
            
            if (overwriteSource)
            {
                //删除源文件
                AssetDatabase.DeleteAsset(cmdData.SourcePath);
                //移动新文件
                AssetDatabase.MoveAsset(cmdData.TargetPath, cmdData.SourcePath);
            }
        }

        private static void Clear()
        {
            _isPause = false;
            _nowPause = false;
            _fbxDic.Clear();
            foreach (PrefabChangedData data in _prefabDic.Values)
            {
                GameObject.DestroyImmediate(data.Prefab);
            }
            _prefabDic.Clear();
            _fbxMeshPrefabData.Clear();
        }
    }
}
