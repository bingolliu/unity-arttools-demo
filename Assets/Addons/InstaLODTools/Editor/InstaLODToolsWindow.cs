using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace InstaLODTools
{
    public class InstaLODToolsWindow : EditorWindow
    {
        private readonly string[] _tabs = new string[] { "多预制减面", "文件夹减面" };
        private readonly string[] _modes = new string[] { "百分比减面", "目标面数减面" };

        private int _selectedTab = 0;
        private int _selectedMode = 0;
        private float _targetPercentage = 50f;
        private int _targetFaceCount = 7000;
        private bool _overwriteSource = false;
        private bool _autoReplaceMesh = false;
        private bool _generatePrefab = false;

        public List<GameObject> selectedPrefabs = new List<GameObject>();
        private Vector2 _prefabScPosition = Vector2.zero;

        public List<string> selectedFolders = new List<string>();
        private List<string> _allFolders = new List<string>();
        private Vector2 _folderScPosition = Vector2.zero;

        private ReMeshType _reMeshType = ReMeshType.Percentage;
        private OperationData _operationData = new OperationData();

        public static InstaLODToolsWindow _instance;

        [MenuItem("Tools/模型/减面工具入口")]
        public static void ShowWindow()
        {
            _instance = GetWindow<InstaLODToolsWindow>("InstaLOD Tools Window");
            //打开InstaLODToolsScene场景
            EditorApplication.OpenScene("Assets/Addons/InstaLODTools/Scene/InstaLODToolsScene.unity");
        }

        public static void Clear()
        {
            if (_instance == null)
            {
                return;
            }

            _instance.selectedPrefabs.Clear();
            _instance.selectedFolders.Clear();
        }

        private void OnEnable()
        {
            //根据FolderType，获取备选文件夹路径
            FolderType[] folderTypes = (FolderType[])System.Enum.GetValues(typeof(FolderType));
            _allFolders.Clear();
            foreach (var folderType in folderTypes)
            {
                InstaLODToolsConst.PrefabFolderPathDic.TryGetValue(folderType, out string folderPath);
                if (string.IsNullOrEmpty(folderPath))
                {
                    continue;
                }
                _allFolders.Add(InstaLODToolsConst.PrefabFolderPathDic[folderType]);
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("IntaLOD减面工具使用面板", new GUIStyle("label")
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            }, GUILayout.Height(30));

            EditorGUILayout.HelpBox("该工具会在同名的FBX文件夹下生成一个_LOD1文件，同时预制体也会附带生成放在指定位置", MessageType.Warning);
            
            _selectedMode = EditorGUILayout.Popup("选择模式", _selectedMode, _modes);

            if (_selectedMode == 0)
            {
                _targetPercentage = EditorGUILayout.Slider("目标百分比", _targetPercentage, 0, 100);
                _reMeshType = ReMeshType.Percentage;
            }
            else if (_selectedMode == 1)
            {
                _targetFaceCount = EditorGUILayout.IntField("目标面数", _targetFaceCount);
                _reMeshType = ReMeshType.AbsoluteCount;
            }

            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabs);
            if (_selectedTab == 0)
            {
                // EditorGUILayout.PropertyField(_selectedFbxProperty, new GUIContent("FBX 文件列表"), true);
                Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
                GUIStyle centeredStyle = GUI.skin.GetStyle("Box");
                centeredStyle.alignment = TextAnchor.MiddleCenter;
                GUI.Box(dropArea, "拖拽预制体到这里", centeredStyle);
                Handles.BeginGUI();
                Handles.color = Color.white;
                Handles.DrawDottedLine(new Vector3(dropArea.xMin, dropArea.yMin, 0), new Vector3(dropArea.xMax, dropArea.yMin, 0), 2);
                Handles.DrawDottedLine(new Vector3(dropArea.xMax, dropArea.yMin, 0), new Vector3(dropArea.xMax, dropArea.yMax, 0), 2);
                Handles.DrawDottedLine(new Vector3(dropArea.xMax, dropArea.yMax, 0), new Vector3(dropArea.xMin, dropArea.yMax, 0), 2);
                Handles.DrawDottedLine(new Vector3(dropArea.xMin, dropArea.yMax, 0), new Vector3(dropArea.xMin, dropArea.yMin, 0), 2);
                Handles.EndGUI();

                // 检查是否有拖拽操作
                Event currentEvent = Event.current;
                switch (currentEvent.type)
                {
                    case EventType.DragUpdated:
                    case EventType.DragPerform:
                        if (!dropArea.Contains(currentEvent.mousePosition))
                            break;

                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                        if (currentEvent.type == EventType.DragPerform)
                        {
                            DragAndDrop.AcceptDrag();

                            foreach (var draggedObject in DragAndDrop.objectReferences)
                            {
                                GameObject go = draggedObject as GameObject;
                                if (go != null && selectedPrefabs.IndexOf(go) == -1)
                                {
                                    string prefabPath = AssetDatabase.GetAssetPath(go);
                                    if (prefabPath.EndsWith(".prefab"))
                                    {
                                        selectedPrefabs.Add(go);
                                    }
                                    else
                                    {
                                        //弹出提示，不是预制体文件
                                        EditorUtility.DisplayDialog("错误", "只能拖拽预制体文件", "确定");
                                    }

                                }
                            }
                        }

                        Event.current.Use();
                        break;
                }

                EditorGUILayout.LabelField("预制体列表", EditorStyles.boldLabel);
                _prefabScPosition = EditorGUILayout.BeginScrollView(_prefabScPosition, GUILayout.Height(200));
                for (int i = 0; i < selectedPrefabs.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(selectedPrefabs[i].name);

                    if (GUILayout.Button("-"))
                    {
                        selectedPrefabs.RemoveAt(i);
                    }

                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }
            else if (_selectedTab == 1)
            {
                //以Toggle陈列PrefabFolderPathDic中的路径
                EditorGUILayout.LabelField("预制体文件夹列表", EditorStyles.boldLabel);
                for (int i = 0; i < _allFolders.Count; i++)
                {
                    bool isSelect = selectedFolders.Contains(_allFolders[i]);
                    bool newSelect = EditorGUILayout.Toggle(_allFolders[i], isSelect);
                    if (newSelect != isSelect)
                    {
                        if (newSelect)
                        {
                            selectedFolders.Add(_allFolders[i]);
                        }
                        else
                        {
                            selectedFolders.Remove(_allFolders[i]);
                        }
                    }
                }
                
                if (GUILayout.Button("选择其余文件夹"))
                {
                    string tempFolderPath = "Assets\\";
                    tempFolderPath = EditorUtility.OpenFolderPanel("预制体文件夹路径", tempFolderPath, "");
                    tempFolderPath = tempFolderPath.Replace(Application.dataPath, "Assets");
                    selectedFolders.Add(tempFolderPath);
                }
                
                EditorGUILayout.LabelField("已选择的文件夹", EditorStyles.boldLabel);
                _folderScPosition = EditorGUILayout.BeginScrollView(_folderScPosition, GUILayout.Height(150));
                for (int i = 0; i < selectedFolders.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(selectedFolders[i]);

                    if (GUILayout.Button("-"))
                    {
                        selectedFolders.RemoveAt(i);
                    }

                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }

            GUILayout.FlexibleSpace();

            _overwriteSource = EditorGUILayout.Toggle("覆盖源FBX文件？", _overwriteSource);
            EditorGUI.BeginDisabledGroup(_overwriteSource);
            if (_overwriteSource)
            {
                _autoReplaceMesh = true;
            }
            _autoReplaceMesh = EditorGUILayout.Toggle("自动替换Prefab的Mesh？", _autoReplaceMesh);
            EditorGUI.EndDisabledGroup();

            _generatePrefab = EditorGUILayout.Toggle("生成LOD预制体？", _generatePrefab);
            
            if (GUILayout.Button("执行压缩"))
            {
                _operationData.OperationType = _selectedTab == 0 ? OperationType.Prefab : OperationType.Folder;
                _operationData.SelectedPrefab = selectedPrefabs;
                _operationData.SelectedFolders = selectedFolders;
                InstaLODToolsManager.StartOptimize(_reMeshType, _targetPercentage, _targetFaceCount, _operationData, _overwriteSource, _autoReplaceMesh, _generatePrefab);
            }

            if (GUILayout.Button("打开配置"))
            {
                InstaLODToolsConfigWindow.ShowWindow();
            }
        }
    }
}
