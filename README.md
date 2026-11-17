# 美术工具演示工程

一个独立的 Unity 示例工程，用于集中展示面向美术工作流的编辑器工具。工程内包含可直接运行的演示资源与配置，打开后即可在 Unity 菜单中体验各项工具，无需依赖其他项目。

仓库：`https://github.com/bingolliu/unity-arttools-demo`

当前演示的能力：

| 工具 | 菜单入口 | 用途 |
| --- | --- | --- |
| 模型减面 | **Tools > 模型 > 减面工具入口** | 调用 InstaLOD 对模型做 LOD 减面，并同步更新预制体 |
| PSD 转预制体 | **Tools > PSD to Prefab** | 解析 PSD 图层结构，生成 NGUI 预制体（需自备 NGUI） |
| LOD 预制体创建 | **Assets > Create LOD Prefab**（右键预制体） | 用已生成的 LOD 网格替换预制体中的网格与 Avatar |
| 命令行输出窗口 | 由减面工具内部调用 | 在编辑器内实时显示外部命令的输出与进度 |

## 环境要求

- Unity `2022.3.62f2`（LTS）
- Windows（减面工具通过 `cmd.exe` 调用命令行程序，暂不支持其他平台）

## 第三方组件安装

为遵守第三方授权并控制仓库体积，以下内容**不在仓库中**（`.gitignore` 已排除），克隆后需自行放入对应位置：

| 组件 | 放置位置 | 来源 |
| --- | --- | --- |
| InstaLOD Pipeline | `tool/InstaLODPipeline/` | InstaLOD 官方，商业授权 |
| NGUI | `Assets/Addons/NGUI/` | Unity Asset Store 付费插件，版本 `2023.08.01` |

其余内容（编辑器工具脚本、LitJson、演示模型与预制体、InstaLOD 工具面板配置）均已包含。

### 安装 InstaLOD Pipeline

1. 从 InstaLOD 官方获取 InstaLOD Pipeline（`InstaLODCmd` 命令行版），并按官方说明完成授权/激活。`tool/InstaLODPipeline/GettingStarted.txt` 只指向在线文档，激活方式与命令行参数以该文档为准。
2. 把**整个**发行目录放到工程根的 `tool/InstaLODPipeline/`，至少包含：

   ```text
   tool/InstaLODPipeline/
     InstaLODCmd.exe                 # 工具通过它执行减面
     Profiles/Optimize_SESC.json     # 减面参数 Profile，工具会写入该文件
     Data/  Images/  Python/  ...    # 运行时资源与同目录 DLL，需一并拷贝
   ```

3. `Profiles/Optimize_SESC.json` 必须存在且可写：每次执行都会把界面上的参数回写其中的 `MeshOperation.Entries[0].Settings.PercentTriangles` / `AbsoluteTriangles`（见 `InstaLODToolsManager.UpdateJsonData`），缺失会直接报错。若发行包里只有 `Optimize.json`，用 InstaLOD Pipeline 打开它另存为 `Optimize_SESC`，**文件名保持 `Optimize_SESC.json`**。
4. 路径不可改名：`Assets/Addons/InstaLODTools/Script/InstaLODToolsConst.cs` 把管线目录固定为 `{工程根}\tool\InstaLODPipeline`，实际执行的命令形如：

   ```bat
   cd <工程根>\tool\InstaLODPipeline && InstaLODCmd -profile Profiles\Optimize_SESC.json -file <输入fbx> <输出fbx> <配置名>
   ```

5. 验证：打开 **Tools > 模型 > 减面工具入口**，对演示模型执行一次，命令窗口应滚动显示 InstaLOD 的实时输出。若提示 `'InstaLODCmd' 不是内部或外部命令`，说明 exe 不在上述目录（工具不会预检文件是否存在，而是把失败信息打印在命令窗口里）。

### 安装 NGUI

1. 从 Unity Asset Store 获取 **NGUI: Next-Gen UI kit**，版本需为 `2023.08.01`。演示预制体按 GUID 引用 NGUI 的 `UIFont` / `UIAtlas` 脚本（`Assets/Res/Font/YaHei.prefab`、`Assets/Res/ComAtlas/Fantasy Demo Atlas.prefab`），只有同版本 Asset Store 包（自带 `.meta`）的 GUID 才能对上；用其它版本或手动拼装的库会出现 Missing Script。
2. 把包内的 NGUI 目录整体放到 `Assets/Addons/NGUI/`，目录名与层级不要改，确认 `Assets/Addons/NGUI/Scripts/UI/UIFont.cs` 存在。PSD 工具与提示文案都按该路径查找/说明。
3. 启用 PSD 工具：**Edit > Project Settings > Player > Other Settings > Scripting Define Symbols** 添加 `NGUI_AVAILABLE`，等待重新编译。未定义该符号时，`Tools > PSD to Prefab` 仍然存在，但只会弹窗提示需要 NGUI，不影响其他工具与整个工程编译。
4. 验证：**Tools > PSD to Prefab** 能打开窗口并加载 PSD、预览图层，即表示 NGUI 已生效。

> 本工程自制的 UI 预制体集合位于 `Assets/Addons/NGUI/SRSC_UIHelper/`，会随 NGUI 目录一起被 `.gitignore` 排除。如果希望它随仓库分发，需要先移到该目录之外（例如 `Assets/Addons/SRSC_UIHelper/`）再提交；当前仓库内的工具与演示资源都没有引用它，缺失不影响运行。

## 快速开始

```bash
git clone https://github.com/bingolliu/unity-arttools-demo.git
```

1. 用 Unity `2022.3.62f2` 打开工程根目录，等待资源导入完成。
2. （可选）按「[第三方组件安装](#第三方组件安装)」放入 InstaLOD Pipeline 与 NGUI；只用 LOD 预制体工具与命令窗口时无需安装。
3. 打开减面工具（**Tools > 模型 > 减面工具入口**）或 PSD 工具（**Tools > PSD to Prefab**）。
4. 使用自带的演示资源试跑一遍，确认流程后再替换为自己的资源。

演示资源一览：

- 角色预制体：`Assets/Res/Roles/ChangeRole/changerole_01.prefab`
- 角色模型与贴图：`Assets/Roles/changerole/changerole_01`（FBX、材质、贴图、Animator Controller）
- 角色动画：`Assets/Res/Animations/changerole_01`
- 字体预制体（含 `UIFont`，需 NGUI）：`Assets/Res/Font/YaHei.prefab`
- 演示 Atlas 预制体（需 NGUI）：`Assets/Res/ComAtlas/Fantasy Demo Atlas.prefab`

## 模型减面

### 目录约定

工具依据 FBX 所在的顶层目录判断资源类型，并按固定规则寻找对应预制体。使用前请把资源放到下列目录中：

| 类型 | FBX 目录 | 预制体目录 |
| --- | --- | --- |
| 角色 | `Assets/Roles` | `Assets/Res/Roles` |
| 装备 | `Assets/Equip` | `Assets/Res/Equip` |
| 场景 | `Assets/Scenes` | `Assets/Scenes` |
| 特效 | `Assets/Effect` | `Assets/Res/Effect` |

### 使用步骤

1. 打开 **Tools > 模型 > 减面工具入口**，工具会自动打开 `Assets/Addons/InstaLODTools/Scene/InstaLODToolsScene.unity` 场景。
2. 选择减面模式：
   - **百分比减面**：按目标百分比缩减面数（滑块 0–100）。
   - **目标面数减面**：直接指定减面后的目标三角面数。
3. 选择处理方式：
   - **多预制减面**：把预制体拖入列表，逐个处理。
   - **文件夹减面**：勾选需要处理的文件夹，按配置的三角面阈值批量处理。
4. 按需勾选附加选项：
   - **自动替换Prefab的Mesh**：用减面结果替换预制体上对应的网格。
   - **生成LOD预制体**：在预制体所在目录生成 `_LOD1` 预制体。
   - **覆盖源文件**：用减面结果覆盖原始 FBX（首次试用建议保持关闭）。
5. 点击执行，工具栏会通过命令窗口显示 InstaLOD 的实时输出；结束后资源会自动刷新。

### 输出结果

- LOD 网格：`{FBX所在目录}_LOD1/{FBX名}_LOD1.fbx`
- 网格资源：输出到与 FBX 同级的 `Mesh` 目录（`Effect` 类型输出到 `Effect/Mesh`）
- LOD 预制体（可选）：`{原预制体名}_LOD1.prefab`，位于原预制体同级目录

### 相关配置

| 文件 | 作用 |
| --- | --- |
| `tool/InstaLODPipeline/Profiles/Optimize_SESC.json` | InstaLOD Pipeline 的处理 Profile，工具执行时会把当前减面参数写回该文件（随 InstaLOD 一起提供） |
| `Assets/Addons/InstaLODTools/Config/InstaLODToolsFolderConfig.json` | 按文件夹配置三角面阈值（`MaxTriangles`）与需要跳过的 FBX（`IgnoreFBXFiles`） |
| `Assets/Addons/InstaLODTools/Script/InstaLODToolsConst.cs` | 目录映射、Profile 名称、输出路径等常量 |

> 减面能力由 InstaLOD Pipeline 提供，该程序与数据受 InstaLOD 自身许可协议约束，请自行获取并遵循其许可要求。

## PSD 转预制体

> 前置条件：已放入 NGUI 并定义 `NGUI_AVAILABLE`（见「[第三方组件安装](#第三方组件安装)」）。

1. 打开 **Tools > PSD to Prefab**。
2. 选择 PSD 文件并点击 **Load PSD and Parse Layers**，工具会读取画布尺寸、图层结构与文本信息。
3. 配置资源引用：
   - **Font Prefab**：用于文本图层的 `UIFont` 预制体（默认 `Assets/Res/Font/YaHei.prefab`）。
   - **Default Atlas Prefab**（可选）：为未单独指定的图层统一使用的 Atlas 预制体。
4. 逐图层指定图片来源：
   - **Texture**：使用纹理资源。
   - **Atlas**：从 NGUI Atlas 中选择 Sprite。
5. 点击 **预览** 在场景中查看还原效果，确认无误后点击 **Generate Prefab** 并选择保存路径。

生成的预制体使用 NGUI 的运行时组件，因此需要 NGUI 代码与资源；解析与图层结构读取部分不依赖 NGUI，可单独复用。

## 辅助工具

### Create LOD Prefab

在 Project 窗口中右键选中一个预制体，选择 **Assets > Create LOD Prefab**，即可基于已有的 LOD 网格生成 LOD 版本预制体。

- 前置条件：预制体同名 FBX 存在，且已有 `{FBX所在目录}_LOD1/{FBX名}_Lod1.fbx`
- 处理内容：复制原预制体为 `{预制体名}_LOD1.prefab`，并按顺序替换其中的 `SkinnedMeshRenderer` / `MeshFilter` 网格，同时同步 `Animator` 的 Avatar
- 减面工具的“生成LOD预制体”选项即调用此逻辑

### CommandWindow

`Assets/Editor/Tools/CommandWindow.cs` 提供一个通用的编辑器命令窗口：把命令行交给 `cmd.exe` 执行，实时滚动显示标准输出与错误输出，并支持结束回调。适合包装任意 CLI 工具，让美术在编辑器内直接看到执行进度。

## 目录结构

```text
Assets/
  Addons/
    InstaLODTools/    减面工具：编辑器窗口、配置、执行管理与演示场景
    PsdParser/        PSD 解析与转预制体工具
    Components/       LitJson 等公共依赖
    NGUI/             需自行准备（仓库不含）
  Editor/
    LODTool/          LOD 预制体创建工具
    Tools/            命令窗口等编辑器辅助工具
  Roles/              角色 FBX、材质、贴图、动画控制器
  Equip/  Scenes/  Effect/   其他类型的模型资源目录
  Res/
    Roles/            角色预制体
    ComAtlas/         Atlas 预制体（需 NGUI）
    Font/             字体预制体（需 NGUI）
    Animations/       动画资源
  Font/               字体文件
  UITex/ UITexLang/   UI 贴图资源
tool/
  InstaLODPipeline/   需自行准备（仓库不含）：InstaLOD 命令行程序与 Profile
Packages/             Unity 包清单
ProjectSettings/      Unity 工程设置
```

## 说明

- 本工程是工具演示用途的独立示例，不包含完整业务逻辑，仅保留运行各项工具所需的代码与资源。
- `.gitignore` 已排除 `Library/`、构建产物等 Unity 生成目录，以及「[第三方组件安装](#第三方组件安装)」中的 InstaLOD Pipeline、NGUI。
- 减面工具依赖 Windows 命令行环境与固定的目录约定；如需接入新的资源类型，请扩展 `InstaLODToolsConst` 中的目录映射与 `FolderType` 枚举。
- 覆盖源文件前建议先提交或备份资源，避免不可逆的修改。