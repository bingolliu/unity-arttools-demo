using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MeshOptimizer
{
    /// <summary>
    /// 文件夹减面阈值配置面板。
    /// </summary>
    public class MeshOptimizerConfigWindow : EditorWindow
    {
        private MeshOptimizerConfigData _data;
        private List<bool> _foldouts = new List<bool>();
        private List<string> _inputs = new List<string>();
        private Vector2 _scrollPosition;

        public static void ShowWindow()
        {
            GetWindow<MeshOptimizerConfigWindow>("Mesh Optimizer 配置");
        }

        private void OnEnable()
        {
            Reload();
        }

        private void Reload()
        {
            _data = MeshOptimizerConfig.Load();
            _foldouts = new List<bool>(new bool[_data.FolderReMeshConfigs.Count]);
            _inputs = new List<string>(new string[_data.FolderReMeshConfigs.Count]);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Mesh Optimizer 文件夹配置", new GUIStyle("label")
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            }, GUILayout.Height(28f));

            EditorGUILayout.HelpBox(
                "1. 最大面数按模型所在目录匹配（Roles/Equip/Scenes/Effect）。\n" +
                "2. 网格三角面数小于等于最大面数时不会被减面。\n" +
                "3. 忽略列表按 FBX 文件名匹配（无需 .fbx 后缀）。",
                MessageType.Info);

            if (_data == null)
            {
                Reload();
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(360f));
            for (int i = 0; i < _data.FolderReMeshConfigs.Count; i++)
            {
                FolderReMeshConfig config = _data.FolderReMeshConfigs[i];

                EditorGUILayout.LabelField(config.FolderPath, EditorStyles.boldLabel);
                config.MaxTriangles = Mathf.Max(1, EditorGUILayout.IntField("最大面数", config.MaxTriangles));

                EditorGUILayout.BeginHorizontal();
                _inputs[i] = EditorGUILayout.TextField("忽略的 FBX", _inputs[i]);
                if (GUILayout.Button("+", GUILayout.Width(24f)) && !string.IsNullOrWhiteSpace(_inputs[i]))
                {
                    config.IgnoreFBXFiles.Add(_inputs[i].Trim().ToLowerInvariant());
                    _inputs[i] = string.Empty;
                }
                EditorGUILayout.EndHorizontal();

                _foldouts[i] = EditorGUILayout.Foldout(_foldouts[i], "忽略列表（按名字匹配）", true);
                if (_foldouts[i])
                {
                    for (int j = 0; j < config.IgnoreFBXFiles.Count; j++)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(config.IgnoreFBXFiles[j]);
                        if (GUILayout.Button("-", GUILayout.Width(24f)))
                        {
                            config.IgnoreFBXFiles.RemoveAt(j);
                            j--;
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }

                EditorGUILayout.Space(10f);
            }
            EditorGUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("保存", GUILayout.Height(30f)))
            {
                MeshOptimizerConfig.Save(_data);
                Reload();
                EditorUtility.DisplayDialog("保存结果", "保存成功！", "确定");
            }
        }
    }
}
