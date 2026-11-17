using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MeshOptimizer
{
    /// <summary>
    /// 模型减面工具面板（替代原 InstaLOD Tools Window）。
    /// </summary>
    public class MeshOptimizerWindow : EditorWindow
    {
        private static MeshOptimizerWindow _instance;

        private readonly string[] _tabs = { "多预制减面", "文件夹减面" };
        private readonly string[] _modes = { "百分比减面", "目标面数减面" };
        private readonly List<GameObject> _selectedPrefabs = new List<GameObject>();
        private readonly List<string> _selectedFolders = new List<string>();
        private readonly List<string> _availableFolders = new List<string>();

        private int _selectedTab;
        private int _selectedMode;
        private float _targetPercentage = 50f;
        private int _targetTriangleCount = 5000;
        private bool _replaceSourcePrefab;
        private bool _generateLodPrefab = true;
        private bool _isRunning;

        private Vector2 _prefabScroll;
        private Vector2 _folderScroll;

        [MenuItem("Tools/模型/减面工具入口")]
        public static void ShowWindow()
        {
            _instance = GetWindow<MeshOptimizerWindow>("Mesh Optimizer");
            _instance.minSize = new Vector2(420f, 540f);
            _instance.Show();
        }

        private void OnEnable()
        {
            _availableFolders.Clear();
            foreach (string folder in MeshOptimizerPaths.PrefabFolders.Values)
            {
                if (!_availableFolders.Contains(folder))
                {
                    _availableFolders.Add(folder);
                }
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("模型减面工具（UnityMeshSimplifier）", new GUIStyle("label")
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            }, GUILayout.Height(28f));

            EditorGUILayout.HelpBox(
                "在编辑器内直接减面，无需 InstaLOD 或外部命令行。\n" +
                "输出网格位于模型同级的 *_LOD1/Mesh 目录；可同时生成 _LOD1 预制体。",
                MessageType.Info);

            _selectedMode = EditorGUILayout.Popup("减面模式", _selectedMode, _modes);
            if (_selectedMode == 0)
            {
                _targetPercentage = EditorGUILayout.Slider("目标百分比", _targetPercentage, 1f, 100f);
            }
            else
            {
                _targetTriangleCount = Mathf.Max(1, EditorGUILayout.IntField("目标面数", _targetTriangleCount));
            }

            EditorGUILayout.Space();
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabs);
            if (_selectedTab == 0)
            {
                DrawPrefabTab();
            }
            else
            {
                DrawFolderTab();
            }

            EditorGUILayout.Space();
            _replaceSourcePrefab = EditorGUILayout.Toggle("替换源预制体网格", _replaceSourcePrefab);
            _generateLodPrefab = EditorGUILayout.Toggle("生成 _LOD1 预制体", _generateLodPrefab);

            EditorGUILayout.Space();
            EditorGUI.BeginDisabledGroup(_isRunning);
            if (GUILayout.Button(_isRunning ? "正在处理..." : "执行减面", GUILayout.Height(34f)))
            {
                Run();
            }
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("打开配置"))
            {
                MeshOptimizerConfigWindow.ShowWindow();
            }
        }

        private void DrawPrefabTab()
        {
            Rect dropArea = GUILayoutUtility.GetRect(0f, 56f, GUILayout.ExpandWidth(true));
            var boxStyle = new GUIStyle("Box")
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Italic
            };
            GUI.Box(dropArea, "拖拽预制体到这里", boxStyle);

            Event currentEvent = Event.current;
            if (dropArea.Contains(currentEvent.mousePosition))
            {
                if (currentEvent.type == EventType.DragUpdated)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    currentEvent.Use();
                }
                else if (currentEvent.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (Object draggedObject in DragAndDrop.objectReferences)
                    {
                        var prefab = draggedObject as GameObject;
                        if (prefab == null) continue;

                        string path = AssetDatabase.GetAssetPath(prefab);
                        if (!path.EndsWith(".prefab"))
                        {
                            EditorUtility.DisplayDialog("错误", "只能拖拽预制体文件。", "确定");
                            continue;
                        }

                        if (!_selectedPrefabs.Contains(prefab))
                        {
                            _selectedPrefabs.Add(prefab);
                        }
                    }

                    currentEvent.Use();
                }
            }

            EditorGUILayout.LabelField("预制体列表", EditorStyles.boldLabel);
            _prefabScroll = EditorGUILayout.BeginScrollView(_prefabScroll, GUILayout.Height(200f));
            for (int i = 0; i < _selectedPrefabs.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(_selectedPrefabs[i] != null ? _selectedPrefabs[i].name : "<Missing>");
                if (GUILayout.Button("-", GUILayout.Width(24f)))
                {
                    _selectedPrefabs.RemoveAt(i);
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawFolderTab()
        {
            EditorGUILayout.LabelField("预制体文件夹", EditorStyles.boldLabel);
            foreach (string folder in _availableFolders)
            {
                bool selected = _selectedFolders.Contains(folder);
                bool nowSelected = EditorGUILayout.ToggleLeft(folder, selected);
                if (nowSelected == selected) continue;

                if (nowSelected)
                {
                    _selectedFolders.Add(folder);
                }
                else
                {
                    _selectedFolders.Remove(folder);
                }
            }

            if (GUILayout.Button("选择其余文件夹"))
            {
                string fullPath = EditorUtility.OpenFolderPanel("选择预制体文件夹", Application.dataPath, "");
                if (!string.IsNullOrEmpty(fullPath) &&
                    fullPath.StartsWith(Application.dataPath, System.StringComparison.OrdinalIgnoreCase))
                {
                    string assetPath = "Assets" + fullPath.Substring(Application.dataPath.Length).Replace('\\', '/');
                    if (!_selectedFolders.Contains(assetPath))
                    {
                        _selectedFolders.Add(assetPath);
                    }
                }
            }

            EditorGUILayout.LabelField("已选择的文件夹", EditorStyles.boldLabel);
            _folderScroll = EditorGUILayout.BeginScrollView(_folderScroll, GUILayout.Height(150f));
            for (int i = 0; i < _selectedFolders.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(_selectedFolders[i]);
                if (GUILayout.Button("-", GUILayout.Width(24f)))
                {
                    _selectedFolders.RemoveAt(i);
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private void Run()
        {
            if (_isRunning) return;

            var request = new MeshOptimizeRequest
            {
                OperationType = _selectedTab == 0 ? OperationType.Prefab : OperationType.Folder,
                Prefabs = new List<GameObject>(_selectedPrefabs),
                Folders = new List<string>(_selectedFolders),
                ReMeshType = _selectedMode == 0 ? ReMeshType.Percentage : ReMeshType.AbsoluteCount,
                TargetPercentage = _targetPercentage,
                TargetTriangleCount = _targetTriangleCount,
                ReplaceSourcePrefab = _replaceSourcePrefab,
                GenerateLodPrefab = _generateLodPrefab
            };

            if (request.OperationType == OperationType.Prefab && request.Prefabs.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先添加预制体。", "确定");
                return;
            }

            if (request.OperationType == OperationType.Folder && request.Folders.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先选择预制体文件夹。", "确定");
                return;
            }

            if (!request.ReplaceSourcePrefab && !request.GenerateLodPrefab &&
                !EditorUtility.DisplayDialog("提示", "未勾选替换源预制体或生成 _LOD1 预制体，本次只会生成减面网格资产。是否继续？", "继续", "取消"))
            {
                return;
            }

            _isRunning = true;
            bool success = false;
            try
            {
                success = MeshOptimizerService.Optimize(request);
            }
            finally
            {
                _isRunning = false;
                Repaint();
            }

            EditorUtility.DisplayDialog("减面", success ? "处理完成，详情见 Console。" : "未处理任何网格，详情见 Console。", "确定");
        }

        public static void Clear()
        {
            if (_instance == null) return;

            _instance._selectedPrefabs.Clear();
            _instance._selectedFolders.Clear();
            _instance.Repaint();
        }
    }
}
