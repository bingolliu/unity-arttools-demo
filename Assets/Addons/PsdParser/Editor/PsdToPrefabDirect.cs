#if NGUI_AVAILABLE
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using PsdParser;

/// <summary>
/// PSD 直接解析生成预制体编辑器窗口
/// </summary>
public class PsdToPrefabDirect : EditorWindow
{
    // --- 编辑器窗口变量 ---
    private string _psdPath;
    private PsdDocument _psdDoc;
    private List<PsdLayerInfo> _flatLayers;
    private Vector2 _scrollPosition;

    // --- 资源分配 (分开存储 Texture 和 spriteName) ---
    private Dictionary<string, ImageSourceType> _imageTypeAssignments;
    private Dictionary<string, Texture> _textureAssignments;      // TextureFolder 模式
    private Dictionary<string, string> _spriteNameAssignments;    // Atlas 模式
    private Dictionary<string, GameObject> _atlasPrefabAssignments;

    // --- 预览 ---
    private GameObject _previewRoot;

    // --- 全局 depth 计数器 ---
    private int _globalDepth = 0;

    // --- 字体预制体 ---
    private GameObject _fontPrefab;

    // --- 默认设置 ---
    private GameObject _defaultAtlasPrefab;
    private string _defaultTextureFolderPath;

    [MenuItem("Tools/PSD to Prefab")]
    public static void ShowWindow()
    {
        PsdToPrefabDirect window = GetWindow<PsdToPrefabDirect>("PSD to Prefab");
        window.Init();
    }

    private void Init()
    {
        _flatLayers = new List<PsdLayerInfo>();
        _imageTypeAssignments = new Dictionary<string, ImageSourceType>();
        _textureAssignments = new Dictionary<string, Texture>();
        _spriteNameAssignments = new Dictionary<string, string>();
        _atlasPrefabAssignments = new Dictionary<string, GameObject>();

        _fontPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Res/Font/YaHei.prefab");
    }

    private void OnDisable()
    {
        ClearPreview();
    }

    void OnGUI()
    {
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

        GUILayout.Label("PSD to Prefab Generator", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("直接读取 PSD 文件，提取图层结构、文本和效果信息生成 NGUI 预制体。", MessageType.Info);

        // 1. PSD 文件选择
        GUILayout.Label("1. 选择并加载 PSD 文件", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        _psdPath = EditorGUILayout.TextField("PSD File Path", _psdPath);
        if (GUILayout.Button("Browse", GUILayout.Width(80)))
        {
            _psdPath = EditorUtility.OpenFilePanel("Select PSD File", "", "psd;psb");
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Load PSD and Parse Layers", GUILayout.Height(30)))
        {
            LoadAndParsePsd();
        }

        if (_psdDoc != null && _flatLayers != null && _flatLayers.Count > 0)
        {
            EditorGUILayout.Space();
            GUILayout.Label($"画布尺寸: {_psdDoc.width} x {_psdDoc.height}", EditorStyles.helpBox);

            // 2. 配置资源引用
            GUILayout.Label("2. 配置资源引用", EditorStyles.boldLabel);
            _fontPrefab = (GameObject)EditorGUILayout.ObjectField("Font Prefab", _fontPrefab, typeof(GameObject), false);

            EditorGUILayout.Space();
            GUILayout.Label("Default Settings (Optional)", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _defaultAtlasPrefab = (GameObject)EditorGUILayout.ObjectField("Default Atlas Prefab", _defaultAtlasPrefab, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck())
            {
                ReAssignDefaultAtlas();
            }

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            _defaultTextureFolderPath = EditorGUILayout.TextField("Default Texture Folder", _defaultTextureFolderPath);
            if (GUILayout.Button("Browse", GUILayout.Width(80)))
            {
                string path = EditorUtility.OpenFolderPanel("Select Default Texture Folder", "Assets", "");
                if (!string.IsNullOrEmpty(path) && path.StartsWith(Application.dataPath))
                {
                    _defaultTextureFolderPath = "Assets" + path.Substring(Application.dataPath.Length);
                }
            }
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck())
            {
                ReAssignDefaultTextures();
            }
            EditorGUILayout.Space();

            // 逐个指定图片资源
            if (_flatLayers.Count > 0)
            {
                GUILayout.Label("Image Layer Assignments:", EditorStyles.boldLabel);
                foreach (var layer in _flatLayers)
                {
                    if (layer.type != PsdLayerType.Image) continue;

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(layer.name, GUILayout.Width(150));

                    var currentType = _imageTypeAssignments[layer.name];
                    var newType = (ImageSourceType)EditorGUILayout.EnumPopup(currentType, GUILayout.Width(100));

                    if (newType != currentType)
                    {
                        if (newType == ImageSourceType.TextureFolder)
                        {
                            _textureAssignments[layer.name] = null;
                            _spriteNameAssignments.Remove(layer.name);
                            _atlasPrefabAssignments[layer.name] = null;
                            TryToAssignDefaultTexture(layer);
                        }
                        else // Atlas
                        {
                            _spriteNameAssignments[layer.name] = layer.name;
                            _textureAssignments.Remove(layer.name);
                            if (_defaultAtlasPrefab != null)
                            {
                                _atlasPrefabAssignments[layer.name] = _defaultAtlasPrefab;
                            }
                        }
                        _imageTypeAssignments[layer.name] = newType;
                    }

                    if (newType == ImageSourceType.TextureFolder)
                    {
                        var tex = _textureAssignments.ContainsKey(layer.name) ? _textureAssignments[layer.name] : null;
                        tex = (Texture)EditorGUILayout.ObjectField(tex, typeof(Texture), false);
                        _textureAssignments[layer.name] = tex;
                    }
                    else // Atlas
                    {
                        EditorGUILayout.BeginVertical();
                        var atlasPrefab = _atlasPrefabAssignments.ContainsKey(layer.name) ? _atlasPrefabAssignments[layer.name] : null;
                        atlasPrefab = (GameObject)EditorGUILayout.ObjectField("Atlas", atlasPrefab, typeof(GameObject), false);
                        _atlasPrefabAssignments[layer.name] = atlasPrefab;

                        if (atlasPrefab != null)
                        {
                            UIAtlas atlas = atlasPrefab.GetComponent<UIAtlas>();
                            if (atlas != null)
                            {
                                string currentSpriteName = _spriteNameAssignments.ContainsKey(layer.name) ? _spriteNameAssignments[layer.name] : "";
                                var localLayerName = layer.name;
                                NGUIEditorTools.DrawSpriteField("Sprite", atlas, currentSpriteName, (spriteName) => {
                                    _spriteNameAssignments[localLayerName] = spriteName;
                                });
                            }
                        }
                        EditorGUILayout.EndVertical();
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            // 3. 生成预制体
            EditorGUILayout.Space();
            GUILayout.Label("3. 生成预制体", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("预览", GUILayout.Height(30)))
            {
                PreviewPrefab();
            }
            if (GUILayout.Button("清除预览", GUILayout.Height(30)))
            {
                ClearPreview();
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Generate Prefab", GUILayout.Height(40)))
            {
                if (ValidateInput())
                {
                    GeneratePrefab();
                }
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void LoadAndParsePsd()
    {
        if (string.IsNullOrEmpty(_psdPath) || !File.Exists(_psdPath))
        {
            EditorUtility.DisplayDialog("错误", "请选择一个有效的 PSD 文件。", "OK");
            _psdDoc = null;
            return;
        }

        ClearPreview();

        PsdFileReader reader = new PsdFileReader();
        _psdDoc = reader.Read(_psdPath);

        if (_psdDoc == null || _psdDoc.layers == null)
        {
            EditorUtility.DisplayDialog("错误", "无法解析 PSD 文件，请检查文件格式。", "OK");
            _psdDoc = null;
            return;
        }

        _flatLayers = new List<PsdLayerInfo>();
        FlattenLayers(_psdDoc.layers);

        _imageTypeAssignments = new Dictionary<string, ImageSourceType>();
        _textureAssignments = new Dictionary<string, Texture>();
        _spriteNameAssignments = new Dictionary<string, string>();
        _atlasPrefabAssignments = new Dictionary<string, GameObject>();

        foreach (var layer in _flatLayers)
        {
            if (layer.type != PsdLayerType.Image) continue;

            var atlasResult = FindAtlasAndSpriteBySpriteName(layer.name);
            if (atlasResult != null)
            {
                _imageTypeAssignments[layer.name] = ImageSourceType.Atlas;
                _atlasPrefabAssignments[layer.name] = atlasResult;
                _spriteNameAssignments[layer.name] = layer.name;
                continue;
            }

            Texture foundTex = FindTextureByName(layer.name);
            if (foundTex != null)
            {
                _imageTypeAssignments[layer.name] = ImageSourceType.TextureFolder;
                _textureAssignments[layer.name] = foundTex;
                _atlasPrefabAssignments[layer.name] = null;
                continue;
            }

            _imageTypeAssignments[layer.name] = ImageSourceType.TextureFolder;
            _textureAssignments[layer.name] = null;
            _atlasPrefabAssignments[layer.name] = null;
            TryToAssignDefaultTexture(layer);
        }

    }

    private void FlattenLayers(List<PsdLayerInfo> layers)
    {
        foreach (var layer in layers)
        {
            if (layer.type == PsdLayerType.Image)
            {
                _flatLayers.Add(layer);
            }
            if (layer.children != null && layer.children.Count > 0)
            {
                FlattenLayers(layer.children);
            }
        }
    }

    private GameObject FindAtlasAndSpriteBySpriteName(string spriteName)
    {
        string[] atlasDirs = { "Assets/Res/ComAtlas" };
        foreach (var dir in atlasDirs)
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { dir });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    UIAtlas atlas = prefab.GetComponent<UIAtlas>();
                    if (atlas != null)
                    {
                        var spriteList = atlas.GetListOfSprites();
                        if (spriteList != null && spriteList.Contains(spriteName))
                        {
                            return prefab;
                        }
                    }
                }
            }
        }
        return null;
    }

    private Texture FindTextureByName(string name)
    {
        string[] texDirs = { "Assets/UITex", "Assets/UITexLang/CN" };
        foreach (var dir in texDirs)
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture", new[] { dir });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (fileName == name)
                {
                    return AssetDatabase.LoadAssetAtPath<Texture>(path);
                }
            }
        }
        return null;
    }

    private void TryToAssignDefaultTexture(PsdLayerInfo layer)
    {
        if (string.IsNullOrEmpty(_defaultTextureFolderPath)) return;

        string[] extensions = { ".png", ".jpg", ".jpeg", ".tga", ".psd" };
        foreach (var ext in extensions)
        {
            string texturePath = Path.Combine(_defaultTextureFolderPath, layer.name + ext);
            Texture texture = AssetDatabase.LoadAssetAtPath<Texture>(texturePath);
            if (texture != null)
            {
                _textureAssignments[layer.name] = texture;
                break;
            }
        }
    }

    private void ReAssignDefaultAtlas()
    {
        if (_flatLayers == null || _defaultAtlasPrefab == null) return;
        UIAtlas atlas = _defaultAtlasPrefab.GetComponent<UIAtlas>();
        if (atlas == null) return;
        var spriteList = atlas.GetListOfSprites();

        foreach (var layer in _flatLayers)
        {
            if (layer.type != PsdLayerType.Image) continue;

            string spriteName = layer.name;
            if (_spriteNameAssignments.ContainsKey(layer.name) && _imageTypeAssignments[layer.name] == ImageSourceType.Atlas)
            {
                spriteName = _spriteNameAssignments[layer.name];
            }

            if (spriteList != null && spriteList.Contains(spriteName))
            {
                _imageTypeAssignments[layer.name] = ImageSourceType.Atlas;
                _spriteNameAssignments[layer.name] = spriteName;
                _atlasPrefabAssignments[layer.name] = _defaultAtlasPrefab;
            }
        }
    }

    private void ReAssignDefaultTextures()
    {
        if (_flatLayers == null) return;

        foreach (var layer in _flatLayers)
        {
            if (layer.type != PsdLayerType.Image) continue;

            if (_imageTypeAssignments.ContainsKey(layer.name) && _imageTypeAssignments[layer.name] == ImageSourceType.TextureFolder)
            {
                if (!_textureAssignments.ContainsKey(layer.name) || _textureAssignments[layer.name] == null)
                {
                    TryToAssignDefaultTexture(layer);
                }
            }
        }
    }

    private bool ValidateInput()
    {
        if (_psdDoc == null)
        {
            EditorUtility.DisplayDialog("错误", "请先加载 PSD 文件。", "OK");
            return false;
        }

        if (_fontPrefab == null || _fontPrefab.GetComponent<UIFont>() == null)
        {
            EditorUtility.DisplayDialog("错误", "请指定一个包含 UIFont 组件的预制体。", "OK");
            return false;
        }

        foreach (var layer in _flatLayers)
        {
            if (layer.type != PsdLayerType.Image) continue;

            var type = _imageTypeAssignments[layer.name];

            if (type == ImageSourceType.TextureFolder)
            {
                var tex = _textureAssignments.ContainsKey(layer.name) ? _textureAssignments[layer.name] : null;
                if (tex == null)
                {
                    EditorUtility.DisplayDialog("错误", $"请为图层 '{layer.name}' 分配一个 Texture 资源。", "OK");
                    return false;
                }
            }
            else // Atlas
            {
                var atlasPrefab = _atlasPrefabAssignments.ContainsKey(layer.name) ? _atlasPrefabAssignments[layer.name] : null;
                if (atlasPrefab == null || atlasPrefab.GetComponent<UIAtlas>() == null)
                {
                    EditorUtility.DisplayDialog("错误", $"请为图层 '{layer.name}' 指定一个 Atlas 预制体。", "OK");
                    return false;
                }

                var spriteName = _spriteNameAssignments.ContainsKey(layer.name) ? _spriteNameAssignments[layer.name] : "";
                if (string.IsNullOrEmpty(spriteName))
                {
                    EditorUtility.DisplayDialog("错误", $"请为图层 '{layer.name}' 指定一个 Sprite 名称。", "OK");
                    return false;
                }
            }
        }

        return true;
    }

    private void PreviewPrefab()
    {
        ClearPreview();

        if (_psdDoc == null)
        {
            EditorUtility.DisplayDialog("错误", "请先加载 PSD 文件。", "OK");
            return;
        }

        string previewName = "[PSD预览]" + Path.GetFileNameWithoutExtension(_psdPath);
        _previewRoot = new GameObject(previewName);
        var root = GameObject.Find("UIRootPrefab");
        if (root != null)
        {
            _previewRoot.transform.SetParent(root.transform);
        }
        _previewRoot.layer = LayerMask.NameToLayer("UI");
        _previewRoot.AddComponent<UIPanel>();
        _previewRoot.transform.localPosition = Vector3.zero;
        _previewRoot.transform.localScale = Vector3.one;

        // 重置 depth 计数器
        _globalDepth = 0;

        // 递归创建图层（保持 PSD 原始顺序）
        foreach (var layer in _psdDoc.layers)
        {
            CreateLayerRecursive(layer, _previewRoot.transform, _psdDoc.height, _psdDoc.width, null);
        }

        Selection.activeGameObject = _previewRoot;
        EditorGUIUtility.PingObject(_previewRoot);
        SceneView.lastActiveSceneView.FrameSelected();
    }

    private void ClearPreview()
    {
        if (_previewRoot != null)
        {
            DestroyImmediate(_previewRoot);
            _previewRoot = null;
        }
    }

    private void GeneratePrefab()
    {
        GameObject selectedRoot = Selection.activeGameObject;
        if (selectedRoot == null)
        {
            selectedRoot = GameObject.Find("UIRootPrefab");
            if (selectedRoot == null)
            {
                EditorUtility.DisplayDialog("错误", "请在 Hierarchy 中选择一个父对象。", "OK");
                return;
            }
        }

        string prefabName = Path.GetFileNameWithoutExtension(_psdPath);
        GameObject prefabRoot = new GameObject(prefabName);
        prefabRoot.layer = LayerMask.NameToLayer("UI");
        prefabRoot.AddComponent<UIPanel>();
        prefabRoot.transform.SetParent(selectedRoot.transform, false);
        prefabRoot.transform.localPosition = Vector3.zero;

        // 重置 depth 计数器
        _globalDepth = 0;

        // 递归创建图层（保持 PSD 原始顺序）
        foreach (var layer in _psdDoc.layers)
        {
            CreateLayerRecursive(layer, prefabRoot.transform, _psdDoc.height, _psdDoc.width, null);
        }

        string savePath = EditorUtility.SaveFilePanelInProject("Save Prefab", prefabName + ".prefab", "prefab", "请选择保存预制体的位置");
        if (!string.IsNullOrEmpty(savePath))
        {
            PrefabUtility.SaveAsPrefabAssetAndConnect(prefabRoot, savePath, InteractionMode.UserAction);
            EditorUtility.DisplayDialog("成功", $"预制体已成功生成！\n路径: {savePath}", "OK");
        }

        DestroyImmediate(prefabRoot);
    }

    private void CreateLayerRecursive(PsdLayerInfo layerData, Transform parent, float psdHeight, float psdWidth, PsdLayerInfo parentLayerData = null, bool parentVisible = true)
    {
        // 如果图层不可见或父节点不可见，跳过整个子树
        bool isVisible = layerData.visible && parentVisible;
        if (!isVisible)
        {
            // 仍然需要递增 depth 计数器以保持一致性
            _globalDepth++;
            // 不创建节点，直接返回
            return;
        }

        // 使用全局 depth 计数器，保证渲染顺序与 PSD 一致
        int currentDepth = _globalDepth;
        _globalDepth++;

        GameObject go = new GameObject(layerData.name);
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        go.SetActive(true); // 只有可见的才会创建

        Vector3 nguiPos = layerData.GetNguiPosition(psdHeight, psdWidth);
        if (parentLayerData != null)
        {
            Vector3 parentNguiPos = parentLayerData.GetNguiPosition(psdHeight, psdWidth);
            go.transform.localPosition = nguiPos - parentNguiPos;
        }
        else
        {
            go.transform.localPosition = nguiPos;
        }

        switch (layerData.type)
        {
            case PsdLayerType.Group:
                // 组只是容器，不需要添加组件
                break;

            case PsdLayerType.Image:
                CreateImageLayer(go, layerData);
                break;

            case PsdLayerType.Text:
                CreateTextLayer(go, layerData);
                break;

            case PsdLayerType.Shape:
                CreateImageLayer(go, layerData);
                break;
        }

        // 设置 Widget 通用属性
        UIWidget widget = go.GetComponent<UIWidget>();
        if (widget != null)
        {
            widget.depth = currentDepth;
            widget.width = (int)layerData.bounds.width;
            widget.height = (int)layerData.bounds.height;
            widget.pivot = UIWidget.Pivot.Center;
            widget.alpha = layerData.opacity;
        }

        // 递归处理子图层（保持 PSD 原始顺序）
        if (layerData.children != null && layerData.children.Count > 0)
        {
            foreach (var child in layerData.children)
            {
                CreateLayerRecursive(child, go.transform, psdHeight, psdWidth, layerData, isVisible);
            }
        }
    }

    private void CreateImageLayer(GameObject go, PsdLayerInfo layerData)
    {
        var imageType = _imageTypeAssignments.ContainsKey(layerData.name) ? _imageTypeAssignments[layerData.name] : ImageSourceType.TextureFolder;

        if (imageType == ImageSourceType.TextureFolder)
        {
            UITexture tex = go.AddComponent<UITexture>();
            tex.mainTexture = _textureAssignments.ContainsKey(layerData.name) ? _textureAssignments[layerData.name] : null;
        }
        else // Atlas
        {
            UISprite sprite = go.AddComponent<UISprite>();
            var atlasPrefab = _atlasPrefabAssignments.ContainsKey(layerData.name) ? _atlasPrefabAssignments[layerData.name] : null;
            if (atlasPrefab != null)
            {
                sprite.atlas = atlasPrefab.GetComponent<UIAtlas>();
            }
            sprite.spriteName = _spriteNameAssignments.ContainsKey(layerData.name) ? _spriteNameAssignments[layerData.name] : "";
            sprite.MakePixelPerfect();
        }
    }

    private void CreateTextLayer(GameObject go, PsdLayerInfo layerData)
    {
        UILabel label = go.AddComponent<UILabel>();
        label.font = _fontPrefab.GetComponent<UIFont>();

        bool hasLayerStroke = layerData.stroke != null && layerData.stroke.enabled;
        bool hasLayerGradient = layerData.gradient != null && layerData.gradient.enabled;
        bool hasLayerShadow = layerData.dropShadow != null && layerData.dropShadow.enabled;

        if (layerData.textInfo != null)
        {
            label.text = layerData.textInfo.text;
            label.fontSize = layerData.textInfo.fontSize > 0 ? layerData.textInfo.fontSize : 24;
            label.color = layerData.textInfo.color;

            switch (layerData.textInfo.alignment)
            {
                case 0: label.alignment = NGUIText.Alignment.Left; break;
                case 1: label.alignment = NGUIText.Alignment.Right; break;
                case 2: label.alignment = NGUIText.Alignment.Center; break;
            }

            // 渐变效果 - 优先使用图层渐变
            if (hasLayerGradient)
            {
                label.applyGradient = true;
                label.gradientTop = layerData.gradient.startColor;
                label.gradientBottom = layerData.gradient.endColor;
                label.color = Color.white;
            }
            else if (layerData.textInfo.hasGradient)
            {
                label.applyGradient = true;
                label.gradientTop = layerData.textInfo.gradientTop;
                label.gradientBottom = layerData.textInfo.gradientBottom;
                label.color = Color.white;
            }

            // 描边效果 - 优先使用图层描边，其次使用 textInfo 中的描边
            if (hasLayerStroke)
            {
                ApplyStroke(label, layerData.stroke);
            }
            else if (layerData.textInfo != null && layerData.textInfo.hasStroke)
            {
                label.effectStyle = UILabel.Effect.Outline;
                int strokeWidth = layerData.textInfo.strokeWidth;
                if (strokeWidth <= 0) strokeWidth = 2; // 默认大小
                label.effectDistance = new Vector2(strokeWidth, strokeWidth);
                label.effectColor = layerData.textInfo.strokeColor;
            }
        }

        label.overflowMethod = UILabel.Overflow.ShrinkContent;

        // 投影效果 - 优先使用图层投影，其次使用 textInfo 中的投影
        // 注意：如果同时有描边和投影，需要最后应用投影（会覆盖 effectStyle）
        if (hasLayerShadow)
        {
            ApplyShadow(label, layerData.dropShadow);
        }
        else if (layerData.textInfo != null && layerData.textInfo.hasDropShadow)
        {
            // 从 textInfo 创建临时投影效果
            PsdDropShadowEffect textShadow = new PsdDropShadowEffect
            {
                enabled = true,
                color = layerData.textInfo.shadowColor,
                distance = layerData.textInfo.shadowDistance,
                blur = layerData.textInfo.shadowBlur,
                angle = layerData.textInfo.shadowAngle,
                opacity = 50
            };
            ApplyShadow(label, textShadow);
        }
    }

    private void ApplyStroke(UILabel label, PsdStrokeEffect stroke)
    {
        if (stroke == null || !stroke.enabled || stroke.size <= 0) return;

        // 如果已经有投影效果，警告用户 NGUI 限制
        if (label.effectStyle == UILabel.Effect.Shadow)
        {
        }

        label.effectStyle = UILabel.Effect.Outline;
        float dist = Mathf.Clamp(stroke.size, 1, 8);
        label.effectDistance = new Vector2(dist, dist);

        // 应用描边颜色（考虑透明度）
        Color effectColor = stroke.color;
        if (stroke.opacity > 0 && stroke.opacity < 100)
        {
            effectColor.a = stroke.opacity / 100f;
        }
        else
        {
            effectColor.a = 1f;
        }
        label.effectColor = effectColor;

    }

    private void ApplyShadow(UILabel label, PsdDropShadowEffect shadow)
    {
        if (shadow == null || !shadow.enabled || shadow.distance <= 0) return;

        // 如果已经有描边效果，警告用户 NGUI 限制
        if (label.effectStyle == UILabel.Effect.Outline)
        {
        }

        label.effectStyle = UILabel.Effect.Shadow;
        float angleRad = shadow.angle * Mathf.Deg2Rad;
        float offsetX = Mathf.Cos(angleRad) * shadow.distance;
        float offsetY = -Mathf.Sin(angleRad) * shadow.distance;
        label.effectDistance = new Vector2(offsetX, offsetY);

        // 应用阴影颜色（考虑透明度）
        Color effectColor = shadow.color;
        if (shadow.opacity > 0 && shadow.opacity < 100)
        {
            effectColor.a = shadow.opacity / 100f;
        }
        else if (shadow.color.a <= 0)
        {
            // PSD 默认投影透明度约 50%
            effectColor.a = 0.5f;
        }
        label.effectColor = effectColor;

    }
}
#else
using UnityEditor;

/// <summary>
/// 未定义 NGUI_AVAILABLE 时的占位入口：仅提示如何启用完整工具。
/// </summary>
public static class PsdToPrefabDirect
{
    [MenuItem("Tools/PSD to Prefab")]
    private static void ShowUnavailableMessage()
    {
        EditorUtility.DisplayDialog("PSD to Prefab",
            "本工具依赖 NGUI。\n请将 NGUI 导入 Assets/Addons/NGUI，并在 Player Settings > Scripting Define Symbols 中添加 NGUI_AVAILABLE。",
            "OK");
    }
}
#endif