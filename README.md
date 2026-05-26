# TFAC剧情箱-轮椅版

## 版本更新

### 2.1.0

`2.1.0` 是 2026-05-26 小预览与文本工具功能版本。

- 素材库详情第一栏新增“素材库元数据”，可启用“本素材库是否启用小预览”，并显示当前素材库目录占用大小。
- 启用小预览后，立绘角色卡、服装、表情和装饰卡会在缺少小预览时显示右下角黄色感叹号，点击会弹出缺少预览的提示。
- 服装、表情、装饰右键菜单新增“设置预览”，预览图会以 `Preview-角色英文代号-DN00/FC00/AD00` 命名并保存到角色的 `Log_Preview` 文件夹；修改素材备注不会丢失映射。
- Unreal 同步会导入小预览贴图并写入 `Narrative/Lustration/DA_Portraits`，其 `Infor` Map 结构与 `DA_LustrationInfor` 保持一致。
- 同步预检会提示未设置小预览的立绘素材；同步清理会覆盖工具箱管理的 `Log_Preview` 资产，但不会清理角色数据资产里的滤镜索引或 VFX 配置。
- 项目详情新增“文本语音”和“文本本地化”卡片，按章节打开二级表格；左侧剧情文本只读，右侧分别维护 wav 语音映射或目标语言文本。
- 文本语音会把选择的 wav 复制到项目 `Voice/<ChapterCode>/`，并按 `Vo-<行号>-<备注>.wav` 规范自动命名；Unreal 同步只导入这些 wav 到 `Narrative/Voice`，不生成语音 DataTable。
- 项目数据会显示剧情总字数。

### 2.0.1

`2.0.1` 是 2026-05-26 修补版本。

- 音频分类加载和归一化时会自动删除 Adobe Audition 生成的 `.pkf` 元数据文件，覆盖音乐、环境音、特殊音效以及后续新增的语音等音频分类。
- `.pkf` 不会进入素材卡片、音频数量、Story 索引或 Unreal 同步计划。
- Unreal 同步启动后会持续显示引擎加载、资源导入和保存资产阶段；耗时统一显示在进度面板右侧，避免主标题重复。
- 取消 Unreal 同步时会把取消令牌传到备份和 Unreal 命令执行阶段；如果 Unreal 命令进程已经启动，工具箱会尝试终止进程树。
- 差异检测说明补充：同名源文件内容重新导出时，只要源文件写入时间晚于目标 `.uasset`，就会进入同步计划。
- Unreal 目标 `Narrative` 下由工具箱管理的背景、音频、剧情表和立绘贴图会按源数据镜像同步；目标里多余的旧 `.uasset` 会在同步时删除。
- 立绘详情里的整套分层预览，以及服装、表情、装饰卡，支持左键点击进入图片查看。
- 图片查看页打开后会聚焦查看页本身，`Esc`、左右方向键、`A/D` 和小键盘 `4/6` 等快捷键更稳定生效。
- 背景图和立绘图层等图片次级查看页支持右键退出。
- 背景图、音频和立绘图层等可导入素材右键支持“替换素材”，选择同类文件后会保留原文件名并直接替换文件内容。
- 背景图和立绘图层的图片查看页底部也提供“替换素材”按钮。

### 2.0.0

`2.0.0` 是结构整理版本。

- 统一打包流程，发布产物为可直接运行的文件夹。
- 引入底部全局进度条，长时间操作集中显示阶段、耗时、百分比和补充信息。
- 补充项目、素材库、章节的备份、还原、导出和拖入导入流程。
- 完善 Unreal 同步页，支持路径校验、差异计划、同步完成提示和日志结果收集。
- Story 编辑器支持真实小节 CSV、选项备注、函数触发提示、内部复制粘贴和撤回。
- 角色立绘支持服装、表情、装饰、滤镜分层管理，并在 Story 编辑器中按兼容范围预览。
- 开始并完成第二轮结构整理，代码已拆分到 `Models`、`Services`、`ViewModels` 和 `Views`。

版本命名采用 `主版本.功能版本.修订版本`：

- 小修复递增修订版本，例如 `2.0.1`。
- 成组功能递增功能版本，例如 `2.1.0`。
- 破坏性重构或大版本更新递增主版本，例如 `3.0.0`。

## 工具介绍

TFAC剧情箱-轮椅版是一个面向 Unreal Engine 视觉小说 / 剧情项目的 WinUI 3 桌面工具。它把项目、素材库、章节剧情 CSV、角色立绘分层、函数触发和 Unreal 同步整理到一个文件优先的工作台里，让策划填表、素材维护和引擎导入尽量保持同一套索引规则。

当前版本：`2.1.0`

## 核心功能

- 项目工作台：创建、重命名、删除项目，绑定素材库，管理章节卡。
- 素材库：管理背景图、音乐、环境音、特殊音效、函数卡、角色滤镜和分层立绘。
- 音频素材：仅加载 `.wav` 文件，加载音频分类时会自动清理 Adobe Audition 生成的 `.pkf` 元数据文件。
- 文本工具：按章节维护文本语音和文本本地化，剧情文本只读，映射数据保存在项目 `Tools` 目录。
- Story 编辑器：编辑 Unreal `FStoryStruct` 对应的剧情 CSV，支持分小节、行导航、自动保存、背景 / BGM / 环境音 / 角色预览。
- 函数与选项：维护剧情函数，支持触发选项备注，选项备注存储在章节本地 `story.choice-notes.json`。
- 角色立绘：按 `DN_Cloth`、`FC_Face`、`AD_Adorn`、`VFX` 分层管理，支持导入、排序、备注、服装适用范围和小预览。
- 备份与导入导出：项目、素材库、章节都支持右键备份 / 还原；项目和素材库支持导出 `.zip` 与拖入导入。
- Unreal 同步：校验 Unreal 路径，生成差异同步计划，可备份 Unreal 项目，调用 Unreal Editor 命令行导入素材、剧情表和立绘信息。
- 设置与辅助显示：支持工作区路径、日志显示、辅助提示、编辑器字号、UI 音效等全局设置。

## 工作区规则

默认工作区位于：

```text
D:\GalExcelProject
```

项目和素材库都以真实文件夹保存。新建时会自动加前缀：

```text
项目-项目名
素材库-素材库名
```

常见结构：

```text
GalExcelProject/
  项目-我的项目/
    Tools/project.meta.json
    Tools/story.voice-map.json
    Tools/story.localization.json
    Voice/
      WHK-M2-00/
        Vo-001-备注.wav
    Chapters/
      WHK-M2-00/
        chapter.meta.json
        WHK-M2-00.csv
        WHK-M2-01.csv
        story.sections.json
        story.choice-notes.json

  素材库-我的素材库/
    Tools/asset-library.meta.json
    背景图/
    音乐/
    环境音/
    特殊音效/
    函数/
    角色滤镜/
    立绘/
      Alice/
        character.json
        Log_Preview/
        DN_Cloth/
        FC_Face/
        AD_Adorn/
        VFX/
```

## Story 数据规则

- Story CSV 字段以 Unreal 结构体为准，并保留历史字段拼写 `Tesxt`。
- 第一列为 Unreal 行名列，工具内部按 `Name` 处理，导出时写作 `---`。
- 行名使用纯数字，例如 `1`、`2`、`3`。
- 背景、BGM、环境音、服装、表情、滤镜索引从 `0` 开始。
- `Adorn=0` 表示无装饰，装饰资源从故事索引 `1` 开始引用。
- `Chara1` 到 `Chara5` 保存角色英文代号；`TalkChar` 可保存角色英文代号，也可保留无法识别的原始输入。
- `Custom` 可用 `/` 分隔多个函数。
- 选项备注只用于查看，保存在章节本地 `story.choice-notes.json`，不会写入 Story CSV。
- 小节是真实章节 CSV 文件，例如 `WHK-M2-00.csv`、`WHK-M2-01.csv`，并由 `story.sections.json` 记录编辑器状态。

## Unreal 同步

同步目标必须位于当前 Unreal 项目的 `Content` 目录下，并且最后一级文件夹必须命名为 `Narrative`，例如：

```text
Content/AssetMaterial/Narrative
```

建议提前准备这些子目录：

```text
Narrative/
  BackGround/
  BGM/
  Scene_Effect/
  ExcelTexts/
  Lustration/
  Voice/
```

同步落点：

- 背景图：`Narrative/BackGround`
- BGM：`Narrative/BGM`
- 环境音和特殊音效：`Narrative/Scene_Effect`
- 项目文本语音：`Narrative/Voice`
- 剧情 CSV 和素材索引表：`Narrative/ExcelTexts`
- 立绘图层、立绘信息和小预览：`Narrative/Lustration`
- 差异检测会比较工具箱源文件和目标 `.uasset` 的写入时间；文件名不变但内容重新导出时，只要源文件写入时间更新，也会进入同步计划。
- 同步会清理 `Narrative` 中多余的工具箱素材资产，让背景、音频、剧情表和立绘贴图与当前素材库 / 项目数据对应。
- 角色数据资产里的 Unreal 侧 `Vfx` 数组和角色滤镜索引不由清理流程删除，它们属于角色数据资产内部配置。

Unreal 侧需要提前具备对应结构体 / 数据资产：

- `/Script/GALLibrary.StoryStruct`：剧情表 RowStruct，对应 C++ `FStoryStruct`
- `/Script/GALLibrary.Texture2DTable`：背景索引表 RowStruct，对应 `FTexture2DTable`
- `/Script/GALLibrary.WaveTable`：音频索引表 RowStruct，对应 `FWaveTable`
- `Narrative/Lustration/DA_LustrationInfor.DA_LustrationInfor`：立绘信息数据资产，包含名为 `Infor` 的 Map 属性
- `Narrative/Lustration/DA_Portraits.DA_Portraits`：小预览数据资产，启用小预览时写入

索引表约定：

- `BGIndexMap.uasset`：背景图索引表，使用 `Texture2D` 列
- `BGMap.uasset`：BGM 索引表，使用 `Wave` 列
- `SceneIndexMap.uasset`：环境音索引表，使用 `Wave` 列
- `ExsIndexMap.uasset`：特殊音效索引表，使用 `Wave` 列
- 文本语音不生成 DataTable，只导入 wav 文件。

## 界面与交互样式规范

- 长时间操作统一使用底部全局进度条，不新增一次性进度弹窗。
- 页面由多个折叠区组成时，使用整页滚动，不给每个区块单独加纵向滚动条。
- 常用确认 / 取消弹窗保留统一快捷键：`Enter` 确认，`Esc` 或右键取消。
- 图片查看页保留 `Esc` 退出，`Left/Right`、`A/D` 或小键盘 `4/6` 切换。
- 图片次级查看页支持右键退出。
- Story 编辑器内部复制粘贴使用悬停目标和 `Ctrl+C` / `Ctrl+V`，不占用系统剪贴板。
- `F12` 打开快捷键帮助。
- 长说明挂在功能标题旁的帮助按钮上，短提示放在悬停提示里，不把大段说明写进普通页面正文。
- 图标按钮必须有悬停提示；文字不够自解释的按钮也要补悬停提示。
- 新增 UI 优先复用已有卡片、弹窗、折叠区、代码块、素材网格和底部进度条样式。
- UI 音效由全局服务控制，用户可在设置里开关。

## 打包发布

使用仓库内脚本：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "D:\UnrealMap\GalExcleTools\Scripts\Package-App.ps1"
```

默认行为：

- 发布 `Release / win-x64`
- 输出到 `D:\DabaoV`
- 文件夹命名类似 `TFAC剧情箱-轮椅版V2.1.0`
- 产物包含程序文件夹和根目录快捷方式
- 不额外生成压缩包或安装器 `.exe`

可选参数示例：

```powershell
.\Scripts\Package-App.ps1 -Runtime win-arm64 -OutputRoot "D:\DabaoV" -Version "2.1.0" -Clean
```

## 开发环境

推荐环境：

- Windows 10 17763 或更高
- .NET 8 SDK
- Visual Studio 2022 或 Rider
- Windows App SDK / WinUI 3 依赖会通过 NuGet 还原

打开解决方案：

```text
GalExcleTools.sln
```

命令行构建：

```powershell
dotnet build GalExcleTools.csproj `
  --configuration Release `
  --runtime win-x64 `
  -p:Platform=x64 `
  -p:WindowsPackageType=None `
  -p:WindowsAppSDKSelfContained=true
```

## 项目目录说明

```text
Assets/       应用图标、默认缩略图等静态资源
Docs/         内部维护说明
Models/       项目、素材库、Story、同步等数据模型
Services/     文件工作区、CSV、素材、同步、弹窗、音效等服务
ViewModels/   可绑定状态、命令和基础命令类
Views/        可复用 UI 工厂、卡片、弹窗内容
Scripts/      打包脚本和验证脚本
```

## 许可

当前仓库未声明开源许可证。除非后续补充 `LICENSE`，否则默认保留所有权利。
