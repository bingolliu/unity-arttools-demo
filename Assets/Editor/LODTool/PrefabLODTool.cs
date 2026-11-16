using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class PrefabLODTool : MonoBehaviour
{
    public static string prefabRootPath = "Assets/Roles";
    public static string equipPrefabRootPath = "Assets/Equip";

    [MenuItem("Assets/Create LOD Prefab", true)]
    private static bool ValidateCreateLOD1Prefab()
    {
        // 仅当选择的是预制体时才启用菜单项
        return Selection.activeObject is GameObject && PrefabUtility.GetPrefabAssetType(Selection.activeObject) == PrefabAssetType.Regular;
    }

    [MenuItem("Assets/Create LOD Prefab")]
    private static void CreateLOD1Prefab()
    {
        // 获取选中的预制体
        GameObject selectedPrefab = Selection.activeObject as GameObject;
        if (selectedPrefab == null)
        {
            Debug.LogError("请选择一个预制体");
            return;
        }

        CreateLOD1PrefabByPrefab(selectedPrefab);
    }

    public static void CreateLOD1PrefabByPrefab(GameObject selectedPrefab)
    {
        if (selectedPrefab == null)
        {
            Debug.LogError("请选择一个预制体");
            return;
        }

        // 获取预制体的路径
        string prefabPath = AssetDatabase.GetAssetPath(selectedPrefab);
        string prefabDirectory = Path.GetDirectoryName(prefabPath);
        string prefabName = Path.GetFileNameWithoutExtension(prefabPath);

        
        // 查找FBX文件
        string fbxPath = FindFBX(prefabPath);
        if (!File.Exists(fbxPath))
        {
            Debug.LogError("未找到与预制体同名的FBX文件! ，预制体路径为: "+prefabPath + "\nfbxPath:" + fbxPath);
            return;
        }

        // 复制预制体到LOD1目录
        string lod1PrefabPath = Path.Combine(prefabDirectory, prefabName + "_LOD1" + ".prefab");
        AssetDatabase.CopyAsset(prefabPath, lod1PrefabPath);
        AssetDatabase.Refresh();
        
        // 加载LOD1预制体并实例化
        GameObject lod1Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(lod1PrefabPath);
        if (lod1Prefab == null) {
            Debug.LogError("未找到预制体：" + lod1PrefabPath);
            return;
        }
        GameObject lod1PrefabInstance = PrefabUtility.InstantiatePrefab(lod1Prefab) as GameObject;
        if (lod1PrefabInstance == null) {
            AssetDatabase.DeleteAsset(prefabPath);
            Debug.LogError("无法实例化预制体, 请联系程序解决");
            return;
        }
        
        // 查找FBX的LOD1文件
        string fbxDirectory = Path.GetDirectoryName(fbxPath) + "_LOD1";
        string fbxName = Path.GetFileNameWithoutExtension(fbxPath);
        string lod1FbxPath = Path.Combine(fbxDirectory, fbxName + "_Lod1.fbx");
        if (!File.Exists(lod1FbxPath))
        {
            Debug.LogError("未找到FBX的Lod1.fbx文件");
            AssetDatabase.DeleteAsset(prefabPath);
            DestroyImmediate(lod1PrefabInstance);
            return;
        }
        
        // 加载LOD1 FBX文件中的Mesh
        GameObject lod1Fbx = AssetDatabase.LoadAssetAtPath<GameObject>(lod1FbxPath);
        SkinnedMeshRenderer[] lod1MeshFilters = lod1Fbx.GetComponentsInChildren<SkinnedMeshRenderer>();
        
        // 替换LOD1预制体中的Mesh
        SkinnedMeshRenderer[] prefabMeshFilters = lod1PrefabInstance.GetComponentsInChildren<SkinnedMeshRenderer>();
        for (int i = 0; i < prefabMeshFilters.Length; i++)
        {
            if (i < lod1MeshFilters.Length)
            {
                prefabMeshFilters[i].sharedMesh = lod1MeshFilters[i].sharedMesh;
            }
        }
        // 替换LOD1预制体中的Avatar
        Animator a = lod1PrefabInstance.GetComponent<Animator>();
        if ( a != null && a.avatar != null)
        {
            Animator lod1Avatar = lod1Fbx.GetComponent<Animator>();
            a.avatar = lod1Avatar.avatar;
        }
        MeshFilter[] lod1MeshFilters1 = lod1Fbx.GetComponentsInChildren<MeshFilter>();
        MeshFilter[] meshFilters = lod1PrefabInstance.GetComponentsInChildren<MeshFilter>();
        for (int i = 0; i < meshFilters.Length; i++)
        {
            if (i < lod1MeshFilters1.Length)
            {
                meshFilters[i].sharedMesh = lod1MeshFilters1[i].sharedMesh;
            }
        }



        // 生成LOD1  Mat和Texture 
        CreateDependencies(prefabPath);
        // 替换LOD1  Mat
        foreach (var meshRenderer in prefabMeshFilters) {
            Material[] originalMaterials = meshRenderer.sharedMaterials;
            Material[] lod1Materials = new Material[originalMaterials.Length];

            for (int i = 0; i < originalMaterials.Length; i++) {
                string originalMaterialPath = AssetDatabase.GetAssetPath(originalMaterials[i]);
                string lod1MaterialPath = CopyAssetWithSuffix(originalMaterialPath, "_LOD1");
                lod1Materials[i] = AssetDatabase.LoadAssetAtPath<Material>(lod1MaterialPath);
            }

            meshRenderer.sharedMaterials = lod1Materials;
        }
        if (meshFilters.Length > 0)
        {
            MeshRenderer[] prefabMeshFilters1 = lod1PrefabInstance.GetComponentsInChildren<MeshRenderer>();

            foreach (var meshRenderer in prefabMeshFilters1)
            {
                Material[] originalMaterials = meshRenderer.sharedMaterials;
                Material[] lod1Materials = new Material[originalMaterials.Length];

                for (int i = 0; i < originalMaterials.Length; i++)
                {
                    string originalMaterialPath = AssetDatabase.GetAssetPath(originalMaterials[i]);
                    string lod1MaterialPath = CopyAssetWithSuffix(originalMaterialPath, "_LOD1");
                    lod1Materials[i] = AssetDatabase.LoadAssetAtPath<Material>(lod1MaterialPath);
                }

                meshRenderer.sharedMaterials = lod1Materials;
            }
        }

        // 保存修改后的LOD1预制体
        PrefabUtility.SaveAsPrefabAsset(lod1PrefabInstance, lod1PrefabPath);
        DestroyImmediate(lod1PrefabInstance);

        AssetDatabase.Refresh();
        Debug.Log("LOD1预制体创建成功：" + lod1PrefabPath);
    }

    
    public static string FindFBX(string prefabPath) {
        // 获取预制体的名称
        string prefabName = Path.GetFileNameWithoutExtension(prefabPath);

        // 获取目录中的所有FBX文件
        string[] fbxFiles = Directory.GetFiles(prefabRootPath, "*.fbx", SearchOption.AllDirectories);

        // 查找与预制体同名的FBX文件
        foreach (string fbxFile in fbxFiles)
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(fbxFile), prefabName, System.StringComparison.OrdinalIgnoreCase))
            {
                return fbxFile;
            }
        }

        // 获取目录中的所有FBX文件
        fbxFiles = Directory.GetFiles(equipPrefabRootPath, "*.fbx", SearchOption.AllDirectories);
        // 查找与预制体同名的FBX文件
        foreach (string fbxFile in fbxFiles)
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(fbxFile), prefabName, System.StringComparison.OrdinalIgnoreCase))
            {
                return fbxFile;
            }
        }

        // 如果未找到同名的FBX文件，返回null
        return null;
    }

    public static void CreateDependencies(string prefabPath)
    {

        Debug.LogWarning("CreateDependencies  " + prefabPath);
        string[] dependencies = AssetDatabase.GetDependencies(prefabPath, true);

        // 分类存储依赖的材质球、贴图的路径
        List<string> materialPaths = new List<string>();
        List<string> texturePaths = new List<string>();

        foreach (string dependency in dependencies) {
            if (dependency.EndsWith(".mat")) {
                materialPaths.Add(dependency);

            } else if (dependency.EndsWith(".png") || dependency.EndsWith(".jpg") || dependency.EndsWith(".tga")) {
                texturePaths.Add(dependency);
            }
        }
        Dictionary<string, string> textureMap = new Dictionary<string, string>();
        foreach (string texturePath in texturePaths) {
            string newTexturePath = CopyAssetWithSuffix(texturePath, "_LOD1");
            textureMap[texturePath] = newTexturePath; // 记录原贴图和新贴图的映射关系
        }

        // 复制资源并添加 _LOD1 后缀
        foreach (string materialPath in materialPaths) {
            string newMaterialPath = CopyAssetWithSuffix(materialPath, "_LOD1");

            // 加载新材质并替换其贴图
            Material newMaterial = AssetDatabase.LoadAssetAtPath<Material>(newMaterialPath);
            if (newMaterial != null) {
                // 遍历材质的所有贴图属性
                Shader shader = newMaterial.shader;
                int propertyCount = ShaderUtil.GetPropertyCount(shader);
                for (int i = 0; i < propertyCount; i++) {
                    if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv) {
                        string propertyName = ShaderUtil.GetPropertyName(shader, i);
                        Texture originalTexture = newMaterial.GetTexture(propertyName);

                        if (originalTexture != null) {
                            string originalTexturePath = AssetDatabase.GetAssetPath(originalTexture);
                            if (textureMap.ContainsKey(originalTexturePath)) {
                                // 替换为带有 _LOD1 后缀的贴图
                                Texture newTexture = AssetDatabase.LoadAssetAtPath<Texture>(textureMap[originalTexturePath]);
                                newMaterial.SetTexture(propertyName, newTexture);
                            }
                        }
                    }
                }
                AssetDatabase.SaveAssets();
            }
        }
    }

    private static string CopyAssetWithSuffix(string assetPath, string suffix)
    {
        string directory = Path.GetDirectoryName(assetPath);
        string fileName = Path.GetFileNameWithoutExtension(assetPath);
        string extension = Path.GetExtension(assetPath);

        if (directory.Contains("\\Textures"))
            directory = directory.Replace("\\Textures", "_LOD1\\Textures");
        else if (directory.Contains("\\Materials"))
            directory = directory.Replace("\\Materials", "_LOD1\\Materials");
        else if (directory.Contains("\\Material"))
            directory = directory.Replace("\\Material", "_LOD1\\Material");
        else if (directory.Contains("\\Texture"))
            directory = directory.Replace("\\Texture", "_LOD1\\Texture");

        Directory.CreateDirectory(directory);
        string newAssetPath = Path.Combine(directory, fileName + suffix + extension);
        if (File.Exists(newAssetPath)) { }
        else if (!AssetDatabase.CopyAsset(assetPath, newAssetPath)) {
            Debug.LogError($"无法复制资源: {assetPath} 到 {newAssetPath}");
        } else {
            Debug.Log($"成功复制资源: {assetPath} 到 {newAssetPath}");
        }
        return newAssetPath;
    }

}