using System;
using System.Collections.Generic;
using System.IO;
using PsdParser;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PSD 直接解析生成 uGUI + TextMeshPro 预制体的编辑器窗口。
/// </summary>
public class PsdToPrefabDirect : EditorWindow
{
    private const string WindowTitle = "PSD to Prefab";

    private string _psdPath;
    private PsdDocument _psdDoc;
    private List<PsdLayerInfo> _flatLayers;
    private Vector2 _scrollPosition;

    // --- 资源分配 ---
    private TMP_FontAsset _fontAsset;
    private readonly Dictionary<string, ImageSourceType> _imageTypeAssignments = new Dictionary<string, ImageSourceType>();
    private readonly Dictionary<string, Texture> _textureAssignments = new Dictionary<string, Texture>();
    private readonly Dictionary<string, Sprite> _spriteAssignments = new Dictionary<string, Sprite>();

    private string _defaultSpriteFolderPath = "Assets/Res/ComAtlas";
    private string _defaultTextureFolderPath = "Assets/UITex";

    // --- 预览 ---
    private GameObject _previewRoot;
    private GameObject _previewCanvas;

    [MenuItem("Tools/PSD to Prefab")]
    public static void ShowWindow()
    {
        var window = GetWindow<PsdToPrefabDirect>(WindowTitle);
        window.minSize = new Vector2(480f, 560f);
        window.Init();
        window.Show();
    }

    private void Init()
    {
        _flatLayers = new List<PsdLayerInfo>();
        _fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TmpFontAssetUtility.DefaultFontAssetPath);
        if (_fontAsset == null)
        {
            _fontAsset = TmpFontAssetUtility.GenerateDefaultFontAsset(false);
        }
    }

    private void OnDisable()
    {
        ClearPreview();
    }

    private void OnGUI()
    {
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

        EditorGUILayout.LabelField("PSD to Prefab（uGUI + TextMeshPro）", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "读取 PSD 图层结构后生成 uGUI 预制体：Image / RawImage / TextMeshProUGUI。\n" +
            "不再依赖 NGUI。",
            MessageType.Info);

        GUILayout.Label("1. 选择并加载 PSD 文件", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        _psdPath = EditorGUILayout.TextField("PSD File Path", _psdPath);
        if (GUILayout.Button("Browse", GUILayout.Width(80f)))
        {
            _psdPath = EditorUtility.OpenFilePanel("Select PSD File", "", "psd;psb");
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("加载并解析图层", GUILayout.Height(28f)))
        {
            LoadAndParsePsd();
        }

        if (_psdDoc == null || _flatLayers == null || _flatLayers.Count == 0)
        {
            EditorGUILayout.EndScrollView();
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"画布尺寸：{_psdDoc.width} x {_psdDoc.height}", EditorStyles.helpBox);

        DrawFontSection();
        DrawDefaultFolderSection();
        DrawLayerAssignments();

        EditorGUILayout.Space();
        GUILayout.Label("3. 生成预制体", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("预览", GUILayout.Height(30f)))
        {
            PreviewPrefab();
        }
        if (GUILayout.Button("清除预览", GUILayout.Height(30f)))
        {
            ClearPreview();
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("生成预制体", GUILayout.Height(36f)))
        {
            if (ValidateInput())
            {
                GeneratePrefab();
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawFontSection()
    {
        EditorGUILayout.Space();
        GUILayout.Label("2. 字体与默认资源", EditorStyles.boldLabel);

        _fontAsset = (TMP_FontAsset)EditorGUILayout.ObjectField("TMP 字体资源", _fontAsset, typeof(TMP_FontAsset), false);
        if (_fontAsset != null) return;

        EditorGUILayout.HelpBox(
            "未找到 TMP 字体资源。请先导入 TMP Essential Resources，再生成 YaHei 字体资源。",
            MessageType.Warning);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("导入 TMP Essential Resources"))
        {
            TmpFontAssetUtility.ImportEssentialResources(true);
        }
        if (GUILayout.Button("生成 YaHei 字体资源"))
        {
            _fontAsset = TmpFontAssetUtility.GenerateDefaultFontAsset(true);
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawDefaultFolderSection()
    {
        EditorGUILayout.Space();
        EditorGUI.BeginChangeCheck();
        _defaultSpriteFolderPath = DrawFolderField("默认 Sprite 文件夹", _defaultSpriteFolderPath, "选择默认 Sprite 文件夹");
        _defaultTextureFolderPath = DrawFolderField("默认 Texture 文件夹", _defaultTextureFolderPath, "选择默认 Texture 文件夹");
        if (EditorGUI.EndChangeCheck())
        {
            ReAssignDefaultSprites();
            ReAssignDefaultTextures();
        }
    }

    private static string DrawFolderField(string label, string current, string dialogTitle)
    {
        EditorGUILayout.BeginHorizontal();
        current = EditorGUILayout.TextField(label, current);
        if (GUILayout.Button("Browse", GUILayout.Width(80f)))
        {
            string fullPath = EditorUtility.OpenFolderPanel(dialogTitle, Application.dataPath, "");
            if (!string.IsNullOrEmpty(fullPath) &&
                fullPath.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
            {
                current = "Assets" + fullPath.Substring(Application.dataPath.Length).Replace('\\', '/');
            }
        }
        EditorGUILayout.EndHorizontal();
        return current;
    }

    private void DrawLayerAssignments()
    {
        EditorGUILayout.Space();
        GUILayout.Label("图片图层资源", EditorStyles.boldLabel);

        foreach (PsdLayerInfo layer in _flatLayers)
        {
            if (!IsImageLikeLayer(layer)) continue;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(layer.name, GUILayout.Width(160f));

            ImageSourceType currentType = _imageTypeAssignments.TryGetValue(layer.name, out ImageSourceType type)
                ? type
                : ImageSourceType.Texture;
            var newType = (ImageSourceType)EditorGUILayout.EnumPopup(currentType, GUILayout.Width(90f));
            if (newType != currentType)
            {
                _imageTypeAssignments[layer.name] = newType;
                if (newType == ImageSourceType.Sprite)
                {
                    _textureAssignments.Remove(layer.name);
                    TryAutoAssignSprite(layer);
                }
                else
                {
                    _spriteAssignments.Remove(layer.name);
                    TryToAssignDefaultTexture(layer);
                }
            }

            if (newType == ImageSourceType.Sprite)
            {
                _spriteAssignments.TryGetValue(layer.name, out Sprite sprite);
                _spriteAssignments[layer.name] = (Sprite)EditorGUILayout.ObjectField(sprite, typeof(Sprite), false);
            }
            else
            {
                _textureAssignments.TryGetValue(layer.name, out Texture texture);
                _textureAssignments[layer.name] = (Texture)EditorGUILayout.ObjectField(texture, typeof(Texture), false);
            }

            EditorGUILayout.EndHorizontal();
        }
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

        var reader = new PsdFileReader();
        _psdDoc = reader.Read(_psdPath);
        if (_psdDoc == null || _psdDoc.layers == null)
        {
            EditorUtility.DisplayDialog("错误", "无法解析 PSD 文件，请检查文件格式。", "OK");
            _psdDoc = null;
            return;
        }

        _flatLayers = new List<PsdLayerInfo>();
        FlattenLayers(_psdDoc.layers);

        _imageTypeAssignments.Clear();
        _textureAssignments.Clear();
        _spriteAssignments.Clear();

        foreach (PsdLayerInfo layer in _flatLayers)
        {
            if (!IsImageLikeLayer(layer)) continue;

            Sprite sprite = FindSpriteByName(layer.name);
            if (sprite != null)
            {
                _imageTypeAssignments[layer.name] = ImageSourceType.Sprite;
                _spriteAssignments[layer.name] = sprite;
                continue;
            }

            Texture texture = FindTextureByName(layer.name);
            if (texture != null)
            {
                _imageTypeAssignments[layer.name] = ImageSourceType.Texture;
                _textureAssignments[layer.name] = texture;
                continue;
            }

            _imageTypeAssignments[layer.name] = ImageSourceType.Texture;
            _textureAssignments[layer.name] = null;
            TryToAssignDefaultTexture(layer);
        }

        if (_fontAsset == null)
        {
            _fontAsset = TmpFontAssetUtility.GenerateDefaultFontAsset(false);
        }
    }

    private void FlattenLayers(List<PsdLayerInfo> layers)
    {
        foreach (PsdLayerInfo layer in layers)
        {
            if (IsImageLikeLayer(layer))
            {
                _flatLayers.Add(layer);
            }

            if (layer.children != null && layer.children.Count > 0)
            {
                FlattenLayers(layer.children);
            }
        }
    }

    private Sprite FindSpriteByName(string spriteName)
    {
        string[] directories = { "Assets/Res/ComAtlas", "Assets/UITex", "Assets/UITexLang/CN" };
        foreach (string directory in directories)
        {
            Sprite sprite = FindSpriteInFolder(directory, spriteName);
            if (sprite != null) return sprite;
        }

        return null;
    }

    private Sprite FindSpriteInFolder(string folder, string spriteName)
    {
        if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder)) return null;

        foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Sprite direct = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (direct != null && string.Equals(direct.name, spriteName, StringComparison.OrdinalIgnoreCase))
            {
                return direct;
            }

            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Sprite sprite &&
                    string.Equals(sprite.name, spriteName, StringComparison.OrdinalIgnoreCase))
                {
                    return sprite;
                }
            }
        }

        return null;
    }

    private Texture FindTextureByName(string name)
    {
        string[] directories = { "Assets/UITex", "Assets/UITexLang/CN" };
        foreach (string directory in directories)
        {
            if (!AssetDatabase.IsValidFolder(directory)) continue;

            foreach (string guid in AssetDatabase.FindAssets("t:Texture", new[] { directory }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase))
                {
                    return AssetDatabase.LoadAssetAtPath<Texture>(path);
                }
            }
        }

        return null;
    }

    private void TryAutoAssignSprite(PsdLayerInfo layer)
    {
        Sprite sprite = FindSpriteInFolder(_defaultSpriteFolderPath, layer.name);
        if (sprite == null) sprite = FindSpriteByName(layer.name);
        if (sprite != null)
        {
            _spriteAssignments[layer.name] = sprite;
        }
    }

    private void TryToAssignDefaultTexture(PsdLayerInfo layer)
    {
        if (string.IsNullOrEmpty(_defaultTextureFolderPath)) return;

        string[] extensions = { ".png", ".jpg", ".jpeg", ".tga", ".psd" };
        foreach (string extension in extensions)
        {
            string texturePath = _defaultTextureFolderPath.TrimEnd('/') + "/" + layer.name + extension;
            Texture texture = AssetDatabase.LoadAssetAtPath<Texture>(texturePath);
            if (texture != null)
            {
                _textureAssignments[layer.name] = texture;
                return;
            }
        }
    }

    private void ReAssignDefaultSprites()
    {
        if (_flatLayers == null) return;

        foreach (PsdLayerInfo layer in _flatLayers)
        {
            if (!IsImageLikeLayer(layer)) continue;

            if (_imageTypeAssignments.TryGetValue(layer.name, out ImageSourceType type) &&
                type != ImageSourceType.Sprite)
            {
                continue;
            }

            Sprite sprite = FindSpriteInFolder(_defaultSpriteFolderPath, layer.name);
            if (sprite != null)
            {
                _imageTypeAssignments[layer.name] = ImageSourceType.Sprite;
                _spriteAssignments[layer.name] = sprite;
            }
        }
    }

    private void ReAssignDefaultTextures()
    {
        if (_flatLayers == null) return;

        foreach (PsdLayerInfo layer in _flatLayers)
        {
            if (!IsImageLikeLayer(layer)) continue;

            if (_imageTypeAssignments.TryGetValue(layer.name, out ImageSourceType type) &&
                type != ImageSourceType.Texture)
            {
                continue;
            }

            if (!_textureAssignments.TryGetValue(layer.name, out Texture texture) || texture == null)
            {
                TryToAssignDefaultTexture(layer);
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

        if (HasTextLayer(_psdDoc.layers) && _fontAsset == null)
        {
            EditorUtility.DisplayDialog("错误", "请先指定或生成 TMP 字体资源。", "OK");
            return false;
        }

        foreach (PsdLayerInfo layer in _flatLayers)
        {
            if (!IsImageLikeLayer(layer)) continue;

            ImageSourceType type = _imageTypeAssignments.TryGetValue(layer.name, out ImageSourceType value)
                ? value
                : ImageSourceType.Texture;

            if (type == ImageSourceType.Texture)
            {
                if (!_textureAssignments.TryGetValue(layer.name, out Texture texture) || texture == null)
                {
                    EditorUtility.DisplayDialog("错误", $"请为图层 '{layer.name}' 分配一个 Texture。", "OK");
                    return false;
                }
            }
            else
            {
                if (!_spriteAssignments.TryGetValue(layer.name, out Sprite sprite) || sprite == null)
                {
                    EditorUtility.DisplayDialog("错误", $"请为图层 '{layer.name}' 分配一个 Sprite。", "OK");
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsImageLikeLayer(PsdLayerInfo layer)
    {
        return layer != null && (layer.type == PsdLayerType.Image || layer.type == PsdLayerType.Shape);
    }

    private static bool HasTextLayer(IEnumerable<PsdLayerInfo> layers)
    {
        if (layers == null) return false;

        foreach (PsdLayerInfo layer in layers)
        {
            if (layer == null) continue;
            if (layer.type == PsdLayerType.Text) return true;
            if (layer.children != null && HasTextLayer(layer.children)) return true;
        }

        return false;
    }

    private void PreviewPrefab()
    {
        ClearPreview();
        if (_psdDoc == null)
        {
            EditorUtility.DisplayDialog("错误", "请先加载 PSD 文件。", "OK");
            return;
        }

        if (HasTextLayer(_psdDoc.layers) && _fontAsset == null)
        {
            EditorUtility.DisplayDialog("错误", "请先导入 TMP Essential Resources 并生成/指定 TMP 字体资源。", "OK");
            return;
        }

        _previewCanvas = new GameObject("[PSD Preview Canvas]",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        _previewCanvas.layer = LayerMask.NameToLayer("UI");

        var canvas = _previewCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = _previewCanvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(Mathf.Max(1, _psdDoc.width), Mathf.Max(1, _psdDoc.height));
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        string previewName = "[PSD预览]" + Path.GetFileNameWithoutExtension(_psdPath);
        _previewRoot = CreateRoot(previewName);
        _previewRoot.transform.SetParent(_previewCanvas.transform, false);

        foreach (PsdLayerInfo layer in _psdDoc.layers)
        {
            CreateLayerRecursive(layer, _previewRoot.GetComponent<RectTransform>(), _psdDoc.height, _psdDoc.width, null);
        }

        Selection.activeGameObject = _previewRoot;
        EditorGUIUtility.PingObject(_previewRoot);
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.FrameSelected();
        }
    }

    private GameObject CreateRoot(string name)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
        root.layer = LayerMask.NameToLayer("UI");

        var rect = root.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(Mathf.Max(1, _psdDoc.width), Mathf.Max(1, _psdDoc.height));
        rect.anchoredPosition = Vector2.zero;

        var group = root.GetComponent<CanvasGroup>();
        group.alpha = 1f;
        group.interactable = false;
        group.blocksRaycasts = false;

        return root;
    }

    private void ClearPreview()
    {
        if (_previewRoot != null)
        {
            DestroyImmediate(_previewRoot);
            _previewRoot = null;
        }

        if (_previewCanvas != null)
        {
            DestroyImmediate(_previewCanvas);
            _previewCanvas = null;
        }
    }

    private void GeneratePrefab()
    {
        if (_psdDoc == null) return;

        string prefabName = Path.GetFileNameWithoutExtension(_psdPath);
        GameObject prefabRoot = CreateRoot(prefabName);

        foreach (PsdLayerInfo layer in _psdDoc.layers)
        {
            CreateLayerRecursive(layer, prefabRoot.GetComponent<RectTransform>(), _psdDoc.height, _psdDoc.width, null);
        }

        string savePath = EditorUtility.SaveFilePanelInProject(
            "Save Prefab", prefabName + ".prefab", "prefab", "请选择保存预制体的位置");

        if (!string.IsNullOrEmpty(savePath))
        {
            EnsureAssetFolder(Path.GetDirectoryName(savePath));
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, savePath);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("成功", $"预制体已成功生成！\n路径：{savePath}", "OK");
        }

        DestroyImmediate(prefabRoot);
    }

    private void CreateLayerRecursive(
        PsdLayerInfo layerData,
        RectTransform parent,
        float psdHeight,
        float psdWidth,
        PsdLayerInfo parentLayerData)
    {
        if (layerData == null || !layerData.visible) return;

        var go = new GameObject(layerData.name, typeof(RectTransform), typeof(CanvasGroup));
        go.layer = LayerMask.NameToLayer("UI");

        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(
            Mathf.Max(1f, layerData.bounds.width),
            Mathf.Max(1f, layerData.bounds.height));

        Vector3 position = layerData.GetCanvasPosition(psdHeight, psdWidth);
        if (parentLayerData != null)
        {
            Vector3 parentPosition = parentLayerData.GetCanvasPosition(psdHeight, psdWidth);
            rect.anchoredPosition = new Vector2(position.x - parentPosition.x, position.y - parentPosition.y);
        }
        else
        {
            rect.anchoredPosition = new Vector2(position.x, position.y);
        }

        var group = go.GetComponent<CanvasGroup>();
        group.alpha = Mathf.Clamp01(layerData.opacity);
        group.interactable = false;
        group.blocksRaycasts = false;

        switch (layerData.type)
        {
            case PsdLayerType.Image:
            case PsdLayerType.Shape:
                CreateImageLayer(go, layerData);
                break;
            case PsdLayerType.Text:
                CreateTextLayer(go, layerData);
                break;
        }

        if (layerData.children == null) return;

        foreach (PsdLayerInfo child in layerData.children)
        {
            CreateLayerRecursive(child, rect, psdHeight, psdWidth, layerData);
        }
    }

    private void CreateImageLayer(GameObject go, PsdLayerInfo layerData)
    {
        ImageSourceType type = _imageTypeAssignments.TryGetValue(layerData.name, out ImageSourceType value)
            ? value
            : ImageSourceType.Texture;

        if (type == ImageSourceType.Sprite)
        {
            var image = go.AddComponent<Image>();
            _spriteAssignments.TryGetValue(layerData.name, out Sprite sprite);
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.raycastTarget = false;
        }
        else
        {
            var rawImage = go.AddComponent<RawImage>();
            _textureAssignments.TryGetValue(layerData.name, out Texture texture);
            rawImage.texture = texture;
            rawImage.raycastTarget = false;
        }
    }

    private void CreateTextLayer(GameObject go, PsdLayerInfo layerData)
    {
        var label = go.AddComponent<TextMeshProUGUI>();
        label.font = _fontAsset;
        label.raycastTarget = false;
        label.richText = true;
        label.enableWordWrapping = true;
        label.overflowMode = TextOverflowModes.Overflow;
        label.enableAutoSizing = true;
        label.fontSizeMin = 4f;
        label.enableVertexGradient = false;

        PsdTextInfo info = layerData.textInfo;
        if (info != null)
        {
            label.text = info.text ?? string.Empty;

            float fontSize = info.fontSize > 0 ? info.fontSize : 24f;
            label.fontSizeMax = fontSize;
            label.fontSize = fontSize;
            label.color = info.color;
            label.alignment = ToTextAlignment(info.alignment);

            if (info.bold) label.fontStyle |= FontStyles.Bold;
            if (info.italic) label.fontStyle |= FontStyles.Italic;

            bool hasLayerGradient = layerData.gradient != null && layerData.gradient.enabled;
            if (hasLayerGradient || info.hasGradient)
            {
                Color top = hasLayerGradient ? layerData.gradient.startColor : info.gradientTop;
                Color bottom = hasLayerGradient ? layerData.gradient.endColor : info.gradientBottom;
                label.enableVertexGradient = true;
                label.colorGradient = new VertexGradient(top, top, bottom, bottom);
                label.color = Color.white;
            }
        }
        else
        {
            label.text = layerData.name;
            label.fontSizeMax = 24f;
            label.fontSize = 24f;
            label.color = Color.white;
        }

        PsdStrokeEffect stroke = layerData.stroke;
        PsdDropShadowEffect shadow = layerData.dropShadow;
        if (info != null)
        {
            if ((stroke == null || !stroke.enabled) && info.hasStroke)
            {
                stroke = new PsdStrokeEffect
                {
                    enabled = true,
                    color = info.strokeColor,
                    size = Mathf.Max(1, info.strokeWidth),
                    opacity = 100
                };
            }

            if ((shadow == null || !shadow.enabled) && info.hasDropShadow)
            {
                shadow = new PsdDropShadowEffect
                {
                    enabled = true,
                    color = info.shadowColor,
                    distance = info.shadowDistance,
                    blur = info.shadowBlur,
                    angle = info.shadowAngle,
                    opacity = 50
                };
            }
        }

        Material material = TmpTextMaterialUtility.GetOrCreate(label.fontSharedMaterial, stroke, shadow);
        if (material != null)
        {
            label.fontSharedMaterial = material;
        }
    }

    private static TextAlignmentOptions ToTextAlignment(int alignment)
    {
        switch (alignment)
        {
            case 1: return TextAlignmentOptions.Right;
            case 2: return TextAlignmentOptions.Center;
            default: return TextAlignmentOptions.Left;
        }
    }

    private static void EnsureAssetFolder(string assetFolder)
    {
        if (string.IsNullOrEmpty(assetFolder)) return;

        assetFolder = assetFolder.Replace('\\', '/').TrimEnd('/');
        if (AssetDatabase.IsValidFolder(assetFolder)) return;

        string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(parent)) return;

        EnsureAssetFolder(parent);
        string name = Path.GetFileName(assetFolder);
        if (!string.IsNullOrEmpty(name))
        {
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
