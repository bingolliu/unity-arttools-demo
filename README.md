# 美术工具演示工程

一个独立的 Unity 示例工程，用于集中展示面向美术工作流的编辑器工具。工程内包含可直接运行的演示资源与配置，打开后即可在 Unity 菜单中体验各项工具，无需依赖其他项目。

仓库：`https://github.com/bingolliu/unity-arttools-demo`

当前演示的能力：

| 工具 | 菜单入口 | 用途 |
| --- | --- | --- |
| 模型减面 | **Tools > 模型 > 减面工具入口** | 在编辑器内用 UnityMeshSimplifier 做 LOD 减面，并同步更新预制体 |
| PSD 转预制体 | **Tools > PSD to Prefab** | 解析 PSD 图层结构，生成 uGUI + TextMeshPro 预制体 |
| LOD 预制体创建 | **Assets > Create LOD Prefab**（右键预制体） | 用外部已生成的 `_Lod1.fbx` 替换预制体中的网格与 Avatar |

## 环境要求

- Unity `2022.3.62f2`（LTS）
- 无需 Windows 命令行环境，减面在编辑器内完成

## 开源替代关系

原先依赖的两个付费插件已替换为免费/开源方案，**不再需要 InstaLOD 或 NGUI**：

| 原付费组件 | 替代方案 | 授权 | 说明 |
| --- | --- | --- | --- |
| InstaLOD Pipeline | [UnityMeshSimplifier](https://github.com/Whinarn/UnityMeshSimplifier) `3.1.1` | MIT | 已 vendor 到 `Assets/Addons/UnityMeshSimplifier/`，纯 C#、编辑器内运行，无需外部 exe |
| NGUI | Unity uGUI + TextMeshPro | Unity 随附 | `com.unity.ugui` 为工程已有依赖，TMP 通过 `Packages/manifest.json` 引入 |

> 本机原有的 `tool/InstaLODPipeline/`（约 350 MB）与 `Assets/Addons/NGUI/`（约 24 MB）已移动到根目录的 `_Deprecated_Plugins/`（`.gitignore` 已排除），不再参与编译；确认不再需要后可直接删除该目录。

## 快速开始

```bash
git clone https://github.com/bingolliu/unity-arttools-demo.git
```

1. 用 Unity `2022.3.62f2` 打开工程根目录，等待包解析与资源导入完成。
2. 工程已包含 TMP Essential Resources（`Assets/TextMesh Pro`），一般无需再导入；若该目录被删除，执行 **Tools > PSD to Prefab > 导入 TMP Essential Resources** 重新导入即可。PSD 工具会用 `Assets/Font/YaHei.TTF` 自动生成 `Assets/Res/Font/YaHei SDF.asset`（动态图集），也可点击窗口中的“生成 YaHei 字体资源”按钮手动生成。
3. 打开减面工具（**Tools > 模型 > 减面工具入口**）或 PSD 工具（**Tools > PSD to Prefab**）。
4. 使用自带的演示资源试跑一遍，确认流程后再替换为自己的资源。

演示资源一览：

- 角色预制体：`Assets/Res/Roles/ChangeRole/changerole_01.prefab`
- 角色模型与贴图：`Assets/Roles/changerole/changerole_01`（FBX、材质、贴图、Animator Controller）
- 角色动画：`Assets/Res/Animations/changerole_01`
- 字体源文件：`Assets/Font/YaHei.TTF`（用于生成 TMP 字体资源）

## 模型减面

### 目录约定

减面**直接使用预制体上引用的网格**（`MeshFilter.sharedMesh` / `SkinnedMeshRenderer.sharedMesh`），不要求网格位于任何固定目录，也不需要事先准备网格副本。

目录约定只用于决定 `MaxTriangles` 阈值，优先级为：预制体所在目录 > 网格所在目录 > 默认 200 面。

| 类型 | 模型目录 | 预制体目录 |
| --- | --- | --- |
| 角色 | `Assets/Roles` | `Assets/Res/Roles` |
| 装备 | `Assets/Equip` | `Assets/Res/Equip` |
| 场景 | `Assets/Scenes` | `Assets/Scenes` |
| 特效 | `Assets/Effect` | `Assets/Res/Effect` |

### 使用步骤

1. 打开 **Tools > 模型 > 减面工具入口**。
2. 选择减面模式：
   - **百分比减面**：保留目标比例的三角面（滑块 1–100）。
   - **目标面数减面**：直接指定减面后的目标三角面数。
3. 选择处理方式：
   - **多预制减面**：把预制体拖入列表，逐个处理。
   - **文件夹减面**：勾选需要处理的文件夹，按配置的三角面阈值批量处理。
4. 按需勾选输出选项：
   - **替换源预制体网格**：用减面结果替换原预制体上的网格。
   - **生成 _LOD1 预制体**：在预制体同级目录生成 `{预制体名}_LOD1.prefab`。
   - 两者都不勾选时，只生成减面网格资产。
5. 点击执行，处理进度显示在 Unity 进度条；结束后资产自动保存，结果同时输出到 Console。

### 输出结果

- 减面网格：`{模型文件}_LOD1/Mesh/{网格名}_ReMesh.asset`（FBX 取其所在目录与文件名，如 `Assets/Roles/changerole/changerole_01/changerole_01.FBX` → `Assets/Roles/changerole/changerole_01/changerole_01_LOD1/Mesh`）；若预制体已引用减面结果，则就地覆盖，不再嵌套新目录
- LOD 预制体（可选）：`{原预制体名}_LOD1.prefab`，位于原预制体同级目录
- 网格替换时保留骨骼、Bindpose、骨骼名字哈希（兼容 `Optimize Game Objects`）、Avatar、材质与子网格结构，因此不会像 FBX 往返流程那样破坏 Humanoid Avatar
- 若源渲染器本身没有 `bones`（本演示模型就是这种精简预制体），会先**从模型资产( FBX )烘焙蒙皮姿态**再减面，输出纯静态网格（无骨骼权重、无 bindposes）：既不会触发 `Bones do not match bindpose` / `Bone index is not within the number of bones`，姿态与朝向也与原模型一致

### 相关配置

| 文件 | 作用 |
| --- | --- |
| `Assets/Addons/MeshOptimizer/Config/MeshOptimizerFolderConfig.json` | 按文件夹配置三角面阈值（`MaxTriangles`）与需要跳过的模型（`IgnoreFBXFiles`，按文件名匹配） |
| `Assets/Addons/MeshOptimizer/Editor/MeshOptimizerPaths.cs` | 目录映射、`_LOD1` 命名、生成物识别等常量 |
| `Assets/Addons/MeshOptimizer/Editor/MeshOptimizerService.cs` | 减面流程：收集预制体引用的网格、临时开启 Read/Write、简化网格、写入资产、替换网格/生成预制体，结束后恢复模型导入设置 |

> 减面算法为 Fast Quadric Edge Collapse（UnityMeshSimplifier，MIT）。质量与 InstaLOD 的 Smart_v2 不完全等价；如对减面质量有硬性要求，可在 `MeshOptimizerService` 中扩展其它后端。处理带 BlendShape 的角色时，UnityMeshSimplifier 会保留 Blendshape 数据。

### 常见问题：蒙皮与 bindpose

模型使用 **Optimize Game Objects** 导入时，`SkinnedMeshRenderer.bones` 为空，Unity 依赖网格里的 `m_BoneNameHashes` 把 `bindposes` 映射到 Avatar。当前工具按以下规则处理：

- 渲染器仍有骨骼：把源网格的 `m_BoneNameHashes` / `m_RootBoneNameHash` 一并拷贝到 `_ReMesh.asset`，保持可蒙皮；
- `bones` 为 0 的精简预制体（本演示模型即此类）：FBX 里的顶点处于 bind pose 空间，直接当静态网格用会**躺倒/朝向错误**，所以先用模型自带的预制体实例 `BakeMesh` 烘焙成当前姿态，再减面，并清空骨骼权重与 `bindposes`，最终得到干净的静态网格。
- 由于走了烘焙流程，这类模型的 BlendShape 无法保留；需要表情动画时请保留骨骼渲染器，不要替换为减面网格。

常见报错与对应原因：

| 报错 | 原因 | 处理 |
| --- | --- | --- |
| `Bones do not match bindpose.` | 骨骼数量与 bindpose 数量不一致（精简预制体 + 带 bindpose 的网格） | 重新执行一次减面（生成烘焙后的静态网格） |
| `Bone index is not within the number of bones.` | 网格保留了骨骼权重但 bindpose 被清空 | 已修复：先清权重再清 bindpose，并对无骨骼渲染器走烘焙流程 |

再次执行减面即可覆盖旧的 `_ReMesh.asset`。如果之前勾选过“替换源预制体网格”，请先用 Undo 或版本管理恢复源预制体的原始网格，再重新执行。

> 本演示工程中源预制体 `changerole_01.prefab` 直接引用原始 FBX 内的网格（`Optimize Game Objects` 导入，`bones` 为空、`bindposes` 为 44），因此源预制体本身在 Inspector 里仍可能提示该警告；减面生成的 `_ReMesh.asset` 与 `_LOD1.prefab` 是干净的静态网格（`bindposes = 0`），不会报错。如需源预制体也不再提示，勾选一次“替换源预制体网格”，源预制体就会指向 `_ReMesh`。
>
> 如果预制体显示 `Missing` 网格（`sharedMesh == null`），工具会打印“预制体有 N 个渲染器的网格引用为空”，不会再误报“没有需要减面的网格”。

### 常见问题：`ObjectDisposedException: Stream has been closed`（Search 索引）

报错栈来自 `UnityEditor.Search.PropertyDatabaseFileStoreView.Dispose`，发生在 `SearchMonitor.InvalidateDocuments` 的去抖回调里：

1. 工具执行完会触发资产数据库变更，Unity 的 Search 索引随之做一次 `InvalidateDocuments`；
2. 如果 `Library/Search/propertyDatabase.db` 已损坏（表现为文件只有几个字节），关闭旧索引视图时就会抛这个异常。

处理方式：

- 代码侧：减面结束后不再调用 `AssetDatabase.Refresh()`，只做 `AssetDatabase.SaveAssets()`，避免每执行一次都触发全量导入流水线；
- 工程侧：退出 Unity，删除 `Library/Search` 目录（至少删除 `propertyDatabase.db*`、`propertyAliases.db*`），重新打开工程让索引重建。

该错误不影响减面结果，属于 Unity 自身的索引缓存问题。

## PSD 转预制体

前置条件：已导入 TMP Essential Resources（或在窗口中点击导入按钮）。

1. 打开 **Tools > PSD to Prefab**。
2. 选择 PSD 文件并点击 **加载并解析图层**，工具会读取画布尺寸、图层结构与文本信息。
3. 配置字体与默认资源：
   - **TMP 字体资源**：默认使用 `Assets/Res/Font/YaHei SDF.asset`，缺失时自动从 `Assets/Font/YaHei.TTF` 生成。
   - **默认 Sprite 文件夹 / 默认 Texture 文件夹**：用于按图层名自动匹配资源。
4. 逐图层指定图片来源：
   - **Sprite**：生成 `uGUI Image`，适合图集/Sprite 资源。
   - **Texture**：生成 `uGUI RawImage`，适合普通纹理。
5. 点击 **预览** 在场景中查看还原效果，确认无误后点击 **生成预制体** 并选择保存路径。

文本图层映射为 `TextMeshProUGUI`：PSD 的字号、颜色、对齐、粗体/斜体、渐变、描边与投影都会尽量还原；描边/投影通过共享 TMP 材质实现，材质生成在 `Assets/Addons/PsdParser/Generated/Materials/`。

> 旧版本的 `Assets/Res/Font/YaHei.prefab`（UIFont）与 `Assets/Res/ComAtlas/Fantasy Demo Atlas.prefab`（UIAtlas）已随 NGUI 依赖移除。需要图集资源时，直接把 Sprite 放到 `Assets/Res/ComAtlas` 或通过窗口指定即可。

## 辅助工具

### Create LOD Prefab

在 Project 窗口中右键选中一个预制体，选择 **Assets > Create LOD Prefab**，即可基于外部已准备好的 LOD 网格生成 LOD 版本预制体。

- 前置条件：预制体同名 FBX 存在，且已有 `{FBX所在目录}_LOD1/{FBX名}_Lod1.fbx`
- 处理内容：复制原预制体为 `{预制体名}_LOD1.prefab`，按顺序替换其中的 `SkinnedMeshRenderer` / `MeshFilter` 网格，并同步 `Animator` 的 Avatar
- 该工具独立于被移除的 InstaLOD，可继续配合第三方 DCC 导出的 LOD FBX 使用

## 目录结构

```text
Assets/
  Addons/
    MeshOptimizer/        减面工具：编辑器窗口、配置、UnityMeshSimplifier 后端
    UnityMeshSimplifier/  MIT 授权的减面库（vendor，含 LICENSE）
    PsdParser/            PSD 解析与 uGUI/TMP 预制体生成
    Components/           LitJson 等公共依赖
  Editor/
    LODTool/              LOD 预制体创建工具
  Roles/                  角色 FBX、材质、贴图、动画控制器
  Equip/  Scenes/  Effect/   其他类型的模型资源目录
  Res/
    Roles/                角色预制体
    ComAtlas/             Sprite/图集资源目录
    Font/                 TMP 字体资源（自动生成 YaHei SDF.asset）
    Animations/           动画资源
  Font/                   字体源文件（YaHei.TTF）
  TextMesh Pro/           TMP Essential Resources（随工程提供）
  UITex/ UITexLang/       UI 贴图资源
Packages/                 Unity 包清单（ugui + textmeshpro）与 packages-lock.json
ProjectSettings/          Unity 工程设置
```

## 更新记录

- **去 InstaLOD / NGUI 依赖**：减面改为编辑器内 `UnityMeshSimplifier`，PSD 转预制体改为 uGUI + TextMeshPro；删除 `Assets/Addons/InstaLODTools`、`Assets/Editor/Tools/CommandWindow.cs` 与两个 NGUI 图集/字体预制体。
- **减面工具直接使用预制体引用的网格**：不再要求网格位于固定目录，也不再需要预先准备网格副本；阈值按“预制体目录 > 网格目录 > 默认 200 面”匹配。
- **修复蒙皮兼容问题**：`Bones do not match bindpose` 与 `Bone index is not within the number of bones` 均已处理，无骨骼渲染器会先烘焙蒙皮姿态，LOD 网格姿态与朝向和原模型一致。
- **减面结束不再调用 `AssetDatabase.Refresh()`**：避免触发全量导入导致 Unity Search 索引抛 `ObjectDisposedException`。

## 说明

- 本工程是工具演示用途的独立示例，不包含完整业务逻辑，仅保留运行各项工具所需的代码与资源。
- `.gitignore` 已排除 `Library/`、构建产物等 Unity 生成目录，以及历史遗留的 `_Deprecated_Plugins/`、`tool/`、`Assets/Addons/NGUI/` 目录。
- 减面与 PSD 工具的产物（`*_LOD1/`、`*_LOD1.prefab`、`Assets/Addons/PsdParser/Editor/Generated/`）同样不入库，克隆工程后跑一遍工具即可生成。
- 减面工具只按目录约定取阈值；如需为新资源类型配置阈值，请扩展 `MeshOptimizerPaths.PrefabFolders` / `SourceFolders` 与 `FolderKind` 枚举，以及 `MeshOptimizerFolderConfig.json`。
- 修改源预制体前建议先提交或备份资源，避免不可逆的修改。
