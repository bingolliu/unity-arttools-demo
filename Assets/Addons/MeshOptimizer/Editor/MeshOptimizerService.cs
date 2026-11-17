using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityMeshSimplifier;

namespace MeshOptimizer
{
    /// <summary>
    /// 使用 UnityMeshSimplifier 在编辑器内完成减面，替代 InstaLODCmd + FBX 往返流程。
    /// </summary>
    public static class MeshOptimizerService
    {
        private const string LogPrefix = "[MeshOptimizer] ";
        private const string ReMeshSuffix = "_ReMesh";
        private const int FallbackMaxTriangles = 200;

        /// <summary>
        /// 预制体既不在 PrefabFolders 内、网格也不在 SourceFolders 内时使用的兜底阈值配置。
        /// </summary>
        private static readonly FolderReMeshConfig FallbackConfig = new FolderReMeshConfig
        {
            FolderPath = string.Empty,
            MaxTriangles = FallbackMaxTriangles,
            IgnoreFBXFiles = new List<string>(),
        };

        public static bool Optimize(MeshOptimizeRequest request)
        {
            var prefabPaths = CollectPrefabPaths(request);
            if (prefabPaths.Count == 0)
            {
                Debug.LogWarning(LogPrefix + "未找到可处理的预制体。");
                return false;
            }

            var configMap = MeshOptimizerConfig.BuildMap(MeshOptimizerConfig.Load());
            HashSet<string> meshAssetPaths = CollectReferencedMeshPaths(prefabPaths);

            int writtenMeshCount = 0;
            int updatedPrefabCount = 0;
            bool success = true;

            try
            {
                using (ModelReadabilityScope.Begin(meshAssetPaths))
                {
                    var plans = new List<PrefabPlan>();
                    foreach (string prefabPath in prefabPaths)
                    {
                        try
                        {
                            PrefabPlan plan = BuildPlan(prefabPath, configMap, request);
                            if (plan != null && plan.Targets.Count > 0)
                            {
                                plans.Add(plan);
                            }
                        }
                        catch (Exception e)
                        {
                            success = false;
                            Debug.LogError(LogPrefix + "分析预制体失败：" + prefabPath + "\n" + e);
                        }
                    }

                    if (plans.Count == 0)
                    {
                        Debug.Log(LogPrefix + "没有满足减面条件的网格（预制体上没有可减面的网格，或都低于配置的最大面数）。");
                        return false;
                    }

                    var cache = new Dictionary<string, Mesh>(StringComparer.Ordinal);
                    var usedAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    int total = plans.Sum(item => item.Targets.Count);
                    int done = 0;

                    foreach (PrefabPlan plan in plans)
                    {
                        try
                        {
                            if (ApplyPlan(plan, request, cache, usedAssetPaths, ref writtenMeshCount, ref done, total))
                            {
                                updatedPrefabCount++;
                            }
                        }
                        catch (Exception e)
                        {
                            success = false;
                            Debug.LogError(LogPrefix + "处理预制体失败：" + plan.PrefabPath + "\n" + e);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                success = false;
                Debug.LogError(LogPrefix + "减面失败：\n" + e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();

                // 只做 SaveAssets：CreateAsset / SaveAsPrefabAsset 已经把改动写进资产数据库，
                // 再调用 AssetDatabase.Refresh() 会触发一次全量导入流水线，
                // 进而让 Unity 的 Search 索引做 InvalidateDocuments（索引缓存损坏时会抛
                // ObjectDisposedException: Stream has been closed）。
                AssetDatabase.SaveAssets();
            }

            Debug.Log(LogPrefix + $"处理完成：生成/更新网格 {writtenMeshCount} 个，更新预制体 {updatedPrefabCount} 个。");
            return success;
        }

        private static bool ApplyPlan(
            PrefabPlan plan,
            MeshOptimizeRequest request,
            Dictionary<string, Mesh> cache,
            HashSet<string> usedAssetPaths,
            ref int writtenMeshCount,
            ref int done,
            int total)
        {
            GameObject root = null;
            bool referenceChanged = false;
            bool wroteMesh = false;

            try
            {
                root = PrefabUtility.LoadPrefabContents(plan.PrefabPath);
                foreach (MeshTarget target in plan.Targets)
                {
                    EditorUtility.DisplayProgressBar(
                        "Mesh Optimizer",
                        $"精简 {target.MeshName}",
                        total <= 0 ? 0f : (float)done / total);
                    done++;

                    Transform node = string.IsNullOrEmpty(target.HierarchyPath)
                        ? root.transform
                        : root.transform.Find(target.HierarchyPath);

                    if (node == null)
                    {
                        Debug.LogWarning(LogPrefix + "找不到网格节点，已跳过：" + target.HierarchyPath + " (" + plan.PrefabPath + ")");
                        continue;
                    }

                    Mesh sourceMesh = GetMesh(node, target.IsSkinned);
                    if (sourceMesh == null)
                    {
                        continue;
                    }

                    // 网格带 bindpose 但渲染器没有 bones（Optimize Game Objects 的精简预制体）：
                    // 顶点在 bind pose 空间里，直接静态渲染会躺倒/朝向错误，必须先烘焙成当前姿态。
                    bool baked = false;
                    if (target.IsSkinned && target.BoneCount == 0 && sourceMesh.bindposes.Length > 0)
                    {
                        Mesh bakedMesh = BakeFromModel(sourceMesh, target.ModelPath);
                        if (bakedMesh != null)
                        {
                            sourceMesh = bakedMesh;
                            baked = true;
                        }
                        else
                        {
                            Debug.LogWarning(LogPrefix +
                                $"无法烘焙蒙皮，LOD 网格的姿态/朝向可能与原模型不一致：{sourceMesh.name}（{target.ModelPath}）");
                        }
                    }

                    Mesh simplified;
                    try
                    {
                        simplified = GetOrCreateSimplifiedMesh(
                            target, sourceMesh, cache, usedAssetPaths, ref writtenMeshCount, baked);
                    }
                    finally
                    {
                        if (baked) UnityEngine.Object.DestroyImmediate(sourceMesh);
                    }

                    if (simplified == null)
                    {
                        continue;
                    }

                    wroteMesh = true;

                    if (AssignMesh(node, simplified, target.IsSkinned))
                    {
                        referenceChanged = true;
                    }
                }

                if ((referenceChanged || wroteMesh) && request.GenerateLodPrefab &&
                    !MeshOptimizerPaths.IsGeneratedAsset(plan.PrefabPath))
                {
                    string lodPath = MeshOptimizerPaths.GetLodPrefabPath(plan.PrefabPath);
                    EnsureAssetFolder(Path.GetDirectoryName(lodPath));
                    PrefabUtility.SaveAsPrefabAsset(root, lodPath);
                    Debug.Log(LogPrefix + "已生成 LOD 预制体：" + lodPath);
                }

                if (referenceChanged && request.ReplaceSourcePrefab)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, plan.PrefabPath);
                    Debug.Log(LogPrefix + "已更新源预制体：" + plan.PrefabPath);
                }
            }
            finally
            {
                if (root != null) PrefabUtility.UnloadPrefabContents(root);
            }

            return referenceChanged;
        }

        private static Mesh GetOrCreateSimplifiedMesh(
            MeshTarget target,
            Mesh sourceMesh,
            Dictionary<string, Mesh> cache,
            HashSet<string> usedAssetPaths,
            ref int writtenMeshCount,
            bool skinningAlreadyBaked)
        {
            string cacheKey = target.ModelPath + "|" + target.MeshName + "|" +
                              sourceMesh.GetInstanceID().ToString(CultureInfo.InvariantCulture) + "|" +
                              target.Quality.ToString("F4", CultureInfo.InvariantCulture);

            if (cache.TryGetValue(cacheKey, out Mesh cached))
            {
                return cached;
            }

            string meshName = BuildReMeshName(sourceMesh.name);
            string meshFolder = ResolveOutputFolder(target.ModelPath);
            EnsureAssetFolder(meshFolder);

            string assetPath = meshFolder + "/" + meshName + ".asset";
            if (usedAssetPaths.Contains(assetPath))
            {
                assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            }

            usedAssetPaths.Add(assetPath);

            Mesh simplified;
            try
            {
                var simplifier = new MeshSimplifier();
                SimplificationOptions options = SimplificationOptions.Default;
                options.PreserveBorderEdges = true;
                options.PreserveUVSeamEdges = true;
                options.PreserveUVFoldoverEdges = true;
                options.PreserveSurfaceCurvature = false;
                options.EnableSmartLink = true;
                options.MaxIterationCount = 100;
                options.Agressiveness = 7;

                simplifier.SimplificationOptions = options;
                simplifier.Initialize(sourceMesh);
                simplifier.SimplifyMesh(target.Quality);
                simplified = simplifier.ToMesh();
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + $"网格减面失败：{sourceMesh.name} ({target.ModelPath})\n{e}");
                return null;
            }

            if (simplified == null || CountTriangles(simplified) <= 0)
            {
                Debug.LogWarning(LogPrefix + "减面结果为空，已跳过：" + sourceMesh.name);
                if (simplified != null) UnityEngine.Object.DestroyImmediate(simplified);
                return null;
            }

            simplified.name = meshName;

            // 蒙皮兼容处理必须在写入资产之前完成，否则资产会短暂处于
            // “有骨骼权重、没有 bindpose” 的非法状态（Unity 会报 Bone index is not within the number of bones）。
            if (target.IsSkinned && target.BoneCount == 0)
            {
                // 源 SkinnedMeshRenderer 没有 bones（常见于 Optimize Game Objects 的精简预制体），
                // 此时网格必须是纯静态网格，否则会被错误蒙皮变形。
                ClearSkinningData(simplified);
                if (!skinningAlreadyBaked)
                {
                    Debug.LogWarning(LogPrefix + $"{target.MeshName} 的 SkinnedMeshRenderer 没有 bones，且未能烘焙蒙皮，已移除蒙皮数据（LOD 姿态可能与原模型不一致）。");
                }
            }
            else
            {
                CopyBoneNameHashes(sourceMesh, simplified);
            }

            Debug.Log(LogPrefix + $"{target.MeshName}: {target.TriangleCount} -> {CountTriangles(simplified)} 三角面" +
                      (skinningAlreadyBaked ? "（已烘焙蒙皮姿态）" : string.Empty));

            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(simplified, existing);
                existing.name = meshName;
                UnityEngine.Object.DestroyImmediate(simplified);
                simplified = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(simplified, assetPath);
            }

            writtenMeshCount++;
            cache[cacheKey] = simplified;
            return simplified;
        }

        /// <summary>
        /// 从模型资产（FBX）里找到同名网格，用它的渲染器把蒙皮后的姿态烘焙成静态网格。
        /// 精简预制体的 SkinnedMeshRenderer 没有 bones，直接 BakeMesh 是空操作，
        /// 只有模型自带的预制体实例才能正确蒙皮。
        /// </summary>
        private static Mesh BakeFromModel(Mesh sourceMesh, string modelAssetPath)
        {
            if (sourceMesh == null || !MeshOptimizerPaths.IsModelAsset(modelAssetPath))
            {
                return null;
            }

            var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelAssetPath);
            if (modelPrefab == null)
            {
                return null;
            }

            GameObject instance = null;
            try
            {
                try
                {
                    instance = PrefabUtility.InstantiatePrefab(modelPrefab) as GameObject;
                }
                catch
                {
                    instance = null;
                }

                if (instance == null)
                {
                    instance = UnityEngine.Object.Instantiate(modelPrefab);
                }

                instance.hideFlags = HideFlags.HideAndDontSave;

                foreach (SkinnedMeshRenderer renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Mesh mesh = renderer.sharedMesh;
                    if (mesh == null || mesh.name != sourceMesh.name) continue;

                    var baked = new Mesh { name = sourceMesh.name + "_Baked" };
                    try
                    {
                        renderer.BakeMesh(baked, false);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning(LogPrefix + $"烘焙蒙皮失败：{sourceMesh.name}\n{e.Message}");
                        UnityEngine.Object.DestroyImmediate(baked);
                        return null;
                    }

                    if (baked.vertexCount == sourceMesh.vertexCount &&
                        CountTriangles(baked) > 0 &&
                        baked.bounds.size != Vector3.zero &&
                        !Approximately(baked.bounds, sourceMesh.bounds))
                    {
                        return baked;
                    }

                    // 顶点没变化说明这个渲染器也没能蒙皮
                    UnityEngine.Object.DestroyImmediate(baked);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + $"从模型烘焙蒙皮失败：{modelAssetPath}\n{e.Message}");
            }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            }

            return null;
        }

        private static bool Approximately(Bounds a, Bounds b)
        {
            const float epsilon = 0.0001f;
            return (a.center - b.center).sqrMagnitude < epsilon &&
                   (a.size - b.size).sqrMagnitude < epsilon;
        }

        private static void ClearSkinningData(Mesh mesh)
        {
            if (mesh == null) return;

            // 先清骨骼权重：权重里记录的骨骼索引会与后面的空 bindpose 冲突，
            // 不先清掉会报 "Bone index is not within the number of bones."，
            // 并且渲染时网格会被错误蒙皮（位置/朝向与原模型不一致）。
            mesh.boneWeights = new BoneWeight[0];

            var serialized = new SerializedObject(mesh);

            SerializedProperty bindposes = serialized.FindProperty("m_BindPose");
            if (bindposes != null) bindposes.arraySize = 0;

            SerializedProperty hashes = serialized.FindProperty("m_BoneNameHashes");
            if (hashes != null) hashes.arraySize = 0;

            SerializedProperty rootHash = serialized.FindProperty("m_RootBoneNameHash");
            if (rootHash != null) rootHash.longValue = 0;

            SerializedProperty bonesAabb = serialized.FindProperty("m_BonesAABB");
            if (bonesAabb != null) bonesAabb.arraySize = 0;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(mesh);
        }

        private static void CopyBoneNameHashes(Mesh source, Mesh target)
        {
            if (source == null || target == null) return;

            var sourceObject = new SerializedObject(source);
            var targetObject = new SerializedObject(target);

            SerializedProperty sourceHashes = sourceObject.FindProperty("m_BoneNameHashes");
            SerializedProperty targetHashes = targetObject.FindProperty("m_BoneNameHashes");
            if (sourceHashes != null && targetHashes != null && sourceHashes.arraySize > 0)
            {
                targetHashes.arraySize = sourceHashes.arraySize;
                for (int i = 0; i < sourceHashes.arraySize; i++)
                {
                    targetHashes.GetArrayElementAtIndex(i).longValue =
                        sourceHashes.GetArrayElementAtIndex(i).longValue;
                }
            }

            SerializedProperty sourceRoot = sourceObject.FindProperty("m_RootBoneNameHash");
            SerializedProperty targetRoot = targetObject.FindProperty("m_RootBoneNameHash");
            if (sourceRoot != null && targetRoot != null)
            {
                targetRoot.longValue = sourceRoot.longValue;
            }

            targetObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static List<string> CollectPrefabPaths(MeshOptimizeRequest request)
        {
            var result = new List<string>();

            if (request.OperationType == OperationType.Prefab)
            {
                if (request.Prefabs != null)
                {
                    foreach (GameObject prefab in request.Prefabs)
                    {
                        if (prefab == null) continue;

                        string path = AssetDatabase.GetAssetPath(prefab);
                        if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                        if (MeshOptimizerPaths.IsGeneratedAsset(path)) continue;
                        if (!result.Contains(path)) result.Add(path);
                    }
                }
            }
            else if (request.Folders != null)
            {
                foreach (string folder in request.Folders)
                {
                    string normalized = (folder ?? string.Empty).Replace('\\', '/').TrimEnd('/');
                    if (!AssetDatabase.IsValidFolder(normalized))
                    {
                        Debug.LogWarning(LogPrefix + "无效文件夹：" + folder);
                        continue;
                    }

                    foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { normalized }))
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                        if (MeshOptimizerPaths.IsGeneratedAsset(path)) continue;
                        if (!result.Contains(path)) result.Add(path);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 收集预制体自身引用的网格资产路径，仅用于临时开启 Read/Write。
        /// 不依赖资源目录约定，预制体引用什么网格就处理什么网格。
        /// </summary>
        private static HashSet<string> CollectReferencedMeshPaths(List<string> prefabPaths)
        {
            var meshPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string prefabPath in prefabPaths)
            {
                GameObject root = null;
                try
                {
                    root = PrefabUtility.LoadPrefabContents(prefabPath);
                    foreach (Mesh mesh in EnumerateMeshes(root))
                    {
                        string meshPath = AssetDatabase.GetAssetPath(mesh);
                        if (string.IsNullOrEmpty(meshPath)) continue;

                        meshPaths.Add(meshPath);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning(LogPrefix + "读取预制体失败：" + prefabPath + "\n" + e.Message);
                }
                finally
                {
                    if (root != null) PrefabUtility.UnloadPrefabContents(root);
                }
            }

            return meshPaths;
        }

        /// <summary>
        /// 阈值配置优先按预制体所在目录匹配，其次按网格所在目录匹配，都不匹配时用兜底配置。
        /// </summary>
        private static FolderReMeshConfig ResolveConfig(
            string prefabPath,
            string meshAssetPath,
            Dictionary<FolderKind, FolderReMeshConfig> configMap,
            out FolderKind kind)
        {
            kind = MeshOptimizerPaths.GetPrefabFolderKind(prefabPath);
            if (kind == FolderKind.None)
            {
                kind = MeshOptimizerPaths.GetFolderKind(meshAssetPath);
            }

            if (kind != FolderKind.None && configMap.TryGetValue(kind, out FolderReMeshConfig config) && config != null)
            {
                return config;
            }

            kind = FolderKind.None;
            return FallbackConfig;
        }

        private static PrefabPlan BuildPlan(
            string prefabPath,
            Dictionary<FolderKind, FolderReMeshConfig> configMap,
            MeshOptimizeRequest request)
        {
            GameObject root = null;
            var plan = new PrefabPlan { PrefabPath = prefabPath };
            int missingMeshCount = 0;
            bool warnedFallback = false;

            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                foreach (Component component in EnumerateMeshComponents(root))
                {
                    Mesh mesh = GetMesh(component);
                    if (mesh == null)
                    {
                        missingMeshCount++;
                        continue;
                    }

                    string modelPath = AssetDatabase.GetAssetPath(mesh);
                    if (string.IsNullOrEmpty(modelPath)) continue;

                    // 显式拖入预制体时直接处理它引用的网格，即使是之前生成的 *_ReMesh；
                    // 文件夹批量模式仍跳过生成物，避免反复减面。
                    if (request.OperationType != OperationType.Prefab &&
                        MeshOptimizerPaths.IsGeneratedAsset(modelPath))
                    {
                        continue;
                    }

                    FolderReMeshConfig config = ResolveConfig(prefabPath, modelPath, configMap, out FolderKind kind);
                    if (kind == FolderKind.None && !warnedFallback)
                    {
                        warnedFallback = true;
                        Debug.Log(LogPrefix + $"预制体未匹配到资源目录配置，按默认阈值 {FallbackMaxTriangles} 面处理：{prefabPath}");
                    }

                    if (IsIgnored(modelPath, mesh.name, config)) continue;

                    int triangles = CountTriangles(mesh);
                    if (triangles <= 0 || triangles <= config.MaxTriangles) continue;

                    float quality = ComputeQuality(triangles, request);
                    if (quality >= 0.999f) continue;

                    if (!mesh.isReadable)
                    {
                        Debug.LogWarning(LogPrefix + $"网格不可读（非模型资产且未开启 Read/Write），已跳过：{mesh.name} ({modelPath})");
                        continue;
                    }

                    plan.Targets.Add(new MeshTarget
                    {
                        HierarchyPath = GetRelativePath(component.transform, root.transform),
                        IsSkinned = component is SkinnedMeshRenderer,
                        BoneCount = component is SkinnedMeshRenderer skinned
                            ? (skinned.bones == null ? 0 : skinned.bones.Length)
                            : 0,
                        MeshName = mesh.name,
                        ModelPath = modelPath,
                        TriangleCount = triangles,
                        Quality = quality
                    });
                }

                if (missingMeshCount > 0)
                {
                    Debug.LogWarning(LogPrefix + $"预制体有 {missingMeshCount} 个渲染器的网格引用为空（Missing 或未指定），已跳过：{prefabPath}");
                }
            }
            finally
            {
                if (root != null) PrefabUtility.UnloadPrefabContents(root);
            }

            return plan;
        }

        private static float ComputeQuality(int triangleCount, MeshOptimizeRequest request)
        {
            if (request.ReMeshType == ReMeshType.Percentage)
            {
                return Mathf.Clamp(request.TargetPercentage / 100f, 0.01f, 1f);
            }

            float quality = (float)request.TargetTriangleCount / Mathf.Max(1, triangleCount);
            return Mathf.Clamp(quality, 0.01f, 1f);
        }

        private static int CountTriangles(Mesh mesh)
        {
            if (mesh == null) return 0;

            try
            {
                ulong indexCount = 0;
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    indexCount += mesh.GetIndexCount(i);
                }

                return (int)Math.Min((ulong)int.MaxValue, indexCount / 3UL);
            }
            catch
            {
                return mesh.isReadable ? mesh.triangles.Length / 3 : 0;
            }
        }

        private static IEnumerable<Mesh> EnumerateMeshes(GameObject root)
        {
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null) yield return filter.sharedMesh;
            }

            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh != null) yield return renderer.sharedMesh;
            }
        }

        private static IEnumerable<Component> EnumerateMeshComponents(GameObject root)
        {
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                yield return filter;
            }

            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                yield return renderer;
            }
        }

        private static Mesh GetMesh(Component component)
        {
            if (component is MeshFilter filter) return filter.sharedMesh;
            if (component is SkinnedMeshRenderer renderer) return renderer.sharedMesh;
            return null;
        }

        private static bool AssignMesh(Transform node, Mesh mesh, bool isSkinned)
        {
            if (isSkinned)
            {
                var renderer = node.GetComponent<SkinnedMeshRenderer>();
                if (renderer == null || renderer.sharedMesh == mesh) return false;

                renderer.sharedMesh = mesh;
                return true;
            }

            var filter = node.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == mesh) return false;

            filter.sharedMesh = mesh;
            return true;
        }

        private static Mesh GetMesh(Transform node, bool isSkinned)
        {
            if (isSkinned)
            {
                var renderer = node.GetComponent<SkinnedMeshRenderer>();
                return renderer != null ? renderer.sharedMesh : null;
            }

            var filter = node.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        private static string GetRelativePath(Transform transform, Transform root)
        {
            if (transform == null || transform == root) return string.Empty;

            var parts = new List<string>();
            Transform current = transform;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private static bool IsIgnored(string modelPath, string meshName, FolderReMeshConfig config)
        {
            if (config == null || config.IgnoreFBXFiles == null || config.IgnoreFBXFiles.Count == 0) return false;

            string fileName = Path.GetFileNameWithoutExtension(modelPath).Trim().ToLowerInvariant();
            string mesh = (meshName ?? string.Empty).Trim().ToLowerInvariant();
            foreach (string ignored in config.IgnoreFBXFiles)
            {
                if (string.IsNullOrEmpty(ignored)) continue;

                string value = ignored.Trim().ToLowerInvariant();
                if (value == fileName) return true;
                if (mesh.Length > 0 && value == mesh) return true;
            }

            return false;
        }

        /// <summary>
        /// 输出网格目录。源网格已经是本工具的生成物时（预制体已被替换过），就地覆盖，
        /// 避免再嵌套出 *_LOD1/Mesh_LOD1。
        /// </summary>
        private static string ResolveOutputFolder(string sourceMeshPath)
        {
            if (MeshOptimizerPaths.IsGeneratedAsset(sourceMeshPath))
            {
                string directory = Path.GetDirectoryName(sourceMeshPath);
                if (!string.IsNullOrEmpty(directory)) return directory.Replace('\\', '/');
            }

            return MeshOptimizerPaths.GetLodRoot(sourceMeshPath) + "/Mesh";
        }

        private static string BuildReMeshName(string sourceName)
        {
            string name = SanitizeFileName(sourceName);
            foreach (string suffix in new[] { ReMeshSuffix, "_Baked" })
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    // 反复减面 / 烘焙网格时沿用同一个输出资产名，避免生成 *_Baked_ReMesh_ReMesh。
                    name = name.Substring(0, name.Length - suffix.Length);
                }
            }

            return name + ReMeshSuffix;
        }

        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "Mesh";

            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalid, '_');
            }

            return fileName.Replace('/', '_').Replace('\\', '_');
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

        private sealed class PrefabPlan
        {
            public string PrefabPath;
            public readonly List<MeshTarget> Targets = new List<MeshTarget>();
        }

        private sealed class MeshTarget
        {
            public string HierarchyPath;
            public bool IsSkinned;
            public int BoneCount;
            public string MeshName;
            public string ModelPath;
            public int TriangleCount;
            public float Quality;
        }

        /// <summary>
        /// 临时开启模型的 Read/Write，处理完再恢复原设置。
        /// </summary>
        private sealed class ModelReadabilityScope : IDisposable
        {
            private readonly List<KeyValuePair<string, bool>> _restoreSettings =
                new List<KeyValuePair<string, bool>>();

            public static ModelReadabilityScope Begin(IEnumerable<string> modelAssetPaths)
            {
                var scope = new ModelReadabilityScope();
                try
                {
                    foreach (string path in modelAssetPaths.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                        if (importer == null || importer.isReadable) continue;

                        scope._restoreSettings.Add(new KeyValuePair<string, bool>(path, importer.isReadable));
                        importer.isReadable = true;
                        importer.SaveAndReimport();
                    }
                }
                catch
                {
                    scope.Dispose();
                    throw;
                }

                return scope;
            }

            public void Dispose()
            {
                foreach (KeyValuePair<string, bool> setting in _restoreSettings)
                {
                    try
                    {
                        var importer = AssetImporter.GetAtPath(setting.Key) as ModelImporter;
                        if (importer == null) continue;

                        importer.isReadable = setting.Value;
                        importer.SaveAndReimport();
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning(LogPrefix + "恢复模型导入设置失败：" + setting.Key + "\n" + e.Message);
                    }
                }

                _restoreSettings.Clear();
            }
        }
    }
}
