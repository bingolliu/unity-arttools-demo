using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace InstaLODTools
{
    public class InstaLODToolsConfigWindow : EditorWindow
    {
        /// <summary>
        /// json数据
        /// </summary>
        private static FolderConfigData _jsonData;
        private static bool[] _foldouts;
        private static string[] _inputIgnores;
        private Vector2 _scPosition = Vector2.zero;

        public static void ShowWindow()
        {
            InstaLODToolsConfig.InitConfig();
            GetWindow<InstaLODToolsConfigWindow>("InstaLOD Tools Config Window");
        }

        private void OnEnable()
        {
            RefreshConfig();
        }

        private void RefreshConfig()
        {
            _jsonData = InstaLODToolsConfig.GetConfig();

            bool[] tempFoldOut = new bool[_jsonData.FolderReMeshConfigs.Count];
            if (_foldouts != null)
            {
                tempFoldOut.CopyTo(_foldouts, 0);
            }
            _foldouts = tempFoldOut;

            string[] tempInputIgnores = new string[_jsonData.FolderReMeshConfigs.Count];
            if (_inputIgnores != null)
            {
                tempInputIgnores.CopyTo(_inputIgnores, 0);
            }
            _inputIgnores = tempInputIgnores;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("IntaLOD文件夹配置面板", new GUIStyle("label")
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            }, GUILayout.Height(30));
            EditorGUILayout.LabelField("参数说明：", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("1. 想要添加或删除文件夹类型，请去代码内删除；\n" +
                                       "2. FBX内的网格的三角面，若超过最大面数（最大面数的取值由FBX源文件所在的文件夹为主），则被判定为需减面的目标；\n" +
                                       "3. 忽略的FBX文件按名字进行匹配（无需带.fbx后缀），如果目标FBX文件名字与忽略列表中的名字相等，则被判定为忽略。", GUILayout.Height(60));

            _scPosition = EditorGUILayout.BeginScrollView(_scPosition, GUILayout.Height(380));
            for (var i = 0; i < _jsonData.FolderReMeshConfigs.Count; i++)
            {
                FolderReMeshConfigData config = _jsonData.FolderReMeshConfigs[i];

                EditorGUILayout.LabelField(config.FolderPath, EditorStyles.boldLabel);

                int max = EditorGUILayout.IntField("最大面数", config.MaxTriangles);
                config.MaxTriangles = Mathf.Max(1, max);

                EditorGUILayout.BeginHorizontal();
                _inputIgnores[i] = EditorGUILayout.TextField("忽略的FBX", _inputIgnores[i]);
                if (GUILayout.Button("+"))
                {
                    config.IgnoreFBXFiles.Add(_inputIgnores[i].ToLower());
                }

                EditorGUILayout.EndHorizontal();

                _foldouts[i] = EditorGUILayout.Foldout(_foldouts[i], "忽略的FBX列表（按名字匹配）");
                if (_foldouts[i])
                {
                    for (var j = 0; j < config.IgnoreFBXFiles.Count; j++)
                    {
                        EditorGUILayout.BeginHorizontal();
                        // 左侧展示内容文本
                        EditorGUILayout.LabelField(config.IgnoreFBXFiles[j]);

                        if (GUILayout.Button("-"))
                        {
                            // 点击后则移除对应的元素
                            config.IgnoreFBXFiles.RemoveAt(j);
                        }

                        EditorGUILayout.EndHorizontal();
                    }
                }

                EditorGUILayout.Space(10);
            }

            EditorGUILayout.EndScrollView();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("保存"))
            {
                InstaLODToolsConfig.SaveConfig(_jsonData);
                RefreshConfig();
            }
        }
    }
}

