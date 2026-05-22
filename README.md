# TFAC剧情箱-轮椅版

TFAC剧情箱-轮椅版是一个面向 Unreal Engine 视觉小说 / 剧情项目的 WinUI 3 桌面工具。它把项目、素材库、章节剧情 CSV、角色立绘分层、函数触发和 Unreal 同步整理到一个文件优先的工作台里，尽量让策划填表、素材维护和引擎导入保持同一套索引规则。

当前版本：`2.0.0`

## 主要功能

- 项目工作台：创建、重命名、删除项目，绑定素材库，管理章节卡。
- 素材库：管理背景图、音乐、环境音、特殊音效、函数卡、角色滤镜和分层立绘。
- Story 编辑器：编辑 Unreal `FStoryStruct` 对应的剧情 CSV，支持分小节、行导航、自动保存、背景 / BGM / 环境音 / 角色预览。
- 角色立绘：按 `DN_Cloth`、`FC_Face`、`AD_Adorn`、`VFX` 分层管理，支持导入、排序、备注和服装适用范围。
- 函数与选项：维护剧情函数，支持触发选项备注，选项备注存储在章节本地 `story.choice-notes.json`。
- 备份与导入导出：项目、素材库、章节都支持右键备份 / 还原；项目和素材库支持导出 `.zip` 与拖入导入。
- 底部进度条：长时间操作统一使用底部弹出式进度条，显示阶段、耗时、百分比和补充信息。
- Unreal 同步：校验 Unreal 路径，生成差异同步计划，可备份 Unreal 项目，调用 Unreal Editor 命令行导入素材、剧情表和立绘信息。

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
    Chapters/
      WHK-M2-00/
        chapter.meta.json
        WHK-M2-00.csv
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
        DN_Cloth/
        FC_Face/
        AD_Adorn/
        VFX/
```

## Unreal 同步准备

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
```

同步落点：

- 背景图：`Narrative/BackGround`
- BGM：`Narrative/BGM`
- 环境音和特殊音效：`Narrative/Scene_Effect`
- 剧情 CSV 和素材索引表：`Narrative/ExcelTexts`
- 立绘图层和立绘信息：`Narrative/Lustration`

需要在 Unreal 侧提前具备对应结构体 / 数据资产：

- `/Script/GALLibrary.StoryStruct`：剧情表 RowStruct，对应 C++ `FStoryStruct`
- `/Script/GALLibrary.Texture2DTable`：背景索引表 RowStruct，对应 `FTexture2DTable`
- `/Script/GALLibrary.WaveTable`：音频索引表 RowStruct，对应 `FWaveTable`
- `Narrative/Lustration/DA_LustrationInfor.DA_LustrationInfor`：立绘信息数据资产，包含名为 `Infor` 的 Map 属性

索引表约定：

- `BGIndexMap.uasset`：背景图索引表，使用 `Texture2D` 列
- `BGMap.uasset`：BGM 索引表，使用 `Wave` 列
- `SceneIndexMap.uasset`：环境音索引表，使用 `Wave` 列
- `ExsIndexMap.uasset`：特殊音效索引表，使用 `Wave` 列

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

## 打包发布

使用仓库内脚本：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "D:\UnrealMap\GalExcleTools\Scripts\Package-App.ps1"
```

默认行为：

- 发布 `Release / win-x64`
- 输出到 `D:\DabaoV`
- 文件夹命名类似 `TFAC剧情箱-轮椅版V2.0.0`
- 产物包含程序文件夹和根目录快捷方式
- 不再额外生成压缩包或安装器 `.exe`

可选参数示例：

```powershell
.\Scripts\Package-App.ps1 -Runtime win-arm64 -OutputRoot "D:\DabaoV" -Version "2.0.0" -Clean
```

## 项目目录说明

```text
Assets/       应用图标、默认缩略图等静态资源
Docs/         设计规则、维护记录、MVVM 重构记录
Models/       项目、素材库、Story、同步等数据模型
Services/     文件工作区、CSV、素材、同步、弹窗、音效等服务
ViewModels/   MVVM 迁移后的 ViewModel 和基础命令类
Views/        可复用 UI 工厂、卡片、弹窗内容
Scripts/      打包脚本和使用说明
```

当前代码仍保留较大的 `MainWindow.xaml` / `MainWindow.xaml.cs`，但 2.0.0 已经把大量业务逻辑拆到 `Models`、`Services`、`ViewModels` 和 `Views`。后续继续重构时请先阅读：

```text
Docs/MaintenanceNotes.md
Docs/MvvmMigrationPlan.md
```

## 维护约定

- 中文源文件和 Markdown 使用 UTF-8。
- Story CSV 字段以 Unreal 结构体为准，并保留历史字段拼写 `Tesxt`。
- 普通剧情素材索引从 `0` 开始；装饰 `Adorn=0` 表示无装饰。
- 长时间操作统一走底部全局进度条，不新增一次性进度弹窗。
- 常用确认 / 取消弹窗应保留统一快捷键：`Enter` 确认，`Esc` 或右键取消。
- UI 音效、主题、可复用卡片和弹窗优先走共享服务 / 工厂，避免每个页面各写一套。

## 版本说明

`2.0.0` 是一次结构整理版本，重点是：

- 修正并统一打包流程。
- 引入底部全局进度条。
- 补充素材库导入 / 导出、拖入导入等流程。
- 完善 Unreal 同步说明和同步完成通知。
- 开始 MVVM 方向重构，拆出大量模型、服务、视图工厂和 ViewModel。

## 许可

当前仓库未声明开源许可证。除非后续补充 `LICENSE`，否则默认保留所有权利。
