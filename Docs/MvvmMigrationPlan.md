# MVVM 迁移记录

本文用于记录 `GalExcleTools` 从单个 `MainWindow.xaml.cs` 渐进迁移到 MVVM 架构的过程。之后每次继续迁移前先看本文；每完成一步都更新“当前状态”和“步骤记录”，避免中途出错后不知道改到了哪里。

## 迁移原则

1. 每一步只做一类改动，保持可构建。
2. 优先搬离纯逻辑，最后再改 XAML 绑定。
3. 不一次性大拆 `MainWindow`，先用 `partial` 和 service 降低风险。
4. 不改变现有文件格式、目录结构和用户数据。
5. 每一步完成后至少运行 Release 构建：
   `dotnet build GalExcleTools.csproj --configuration Release --runtime win-x64 -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true`
6. 按用户要求，不主动启动程序做运行检查；如果用户发现问题，再针对问题修。
7. 迁移中遇到脏工作区，不回退用户改动，只处理本步骤相关文件。

## 目标架构

建议采用温和版 MVVM，不追求一次性“纯 MVVM”：

- `Models/`
  数据结构和轻量 DTO，例如项目、素材库、章节、角色、同步计划。
- `Services/`
  文件 IO、CSV、备份、打包、虚幻同步、素材导入、日志、设置读写。
- `ViewModels/`
  页面状态、命令、可绑定集合，例如 `AssetLibraryViewModel`、`UnrealSyncViewModel`。
- `Views/`
  以后可拆页面 XAML；短期仍保留 `MainWindow.xaml`，降低风险。
- `MainWindow.xaml.cs`
  最终只保留窗口初始化、导航、少量 WinUI 控件事件桥接和弹窗协调。

## 建议目录

```text
GalExcleTools/
  Models/
    ProjectInfo.cs
    AssetLibraryInfo.cs
    ChapterInfo.cs
    CharacterInfo.cs
    UnrealSyncModels.cs
  Services/
    AppSettingsService.cs
    ProjectWorkspaceService.cs
    AssetLibraryService.cs
    StoryCsvService.cs
    BackupService.cs
    UnrealSyncService.cs
    LogService.cs
    DialogService.cs
    ShortcutService.cs
    ThemeService.cs
    UiSoundService.cs
  ViewModels/
    ObservableObject.cs
    RelayCommand.cs
    AsyncRelayCommand.cs
    WorkbenchViewModel.cs
    AssetLibraryViewModel.cs
    AssetLibraryDetailViewModel.cs
    CharacterDetailViewModel.cs
    UnrealSyncViewModel.cs
    SettingsViewModel.cs
  Views/
    后续再拆页面 XAML
```

暂时不强制引入 `CommunityToolkit.Mvvm`。可以先写很薄的 `ObservableObject`、`RelayCommand`、`AsyncRelayCommand`，减少 NuGet 变动风险。等迁移稳定后，再决定是否替换成 Toolkit。

## 复用交互和风格清单

这些内容在迁移中要优先保留并逐步抽成可复用服务/组件。细节以 `Docs/MaintenanceNotes.md` 的 `Dialog, Shortcut, and Reusable UI Rule` 为准。

弹窗：
- 普通确认/编辑弹窗默认 `Enter` 确认。
- 普通确认/编辑/备注弹窗默认 `Esc` 取消。
- 轻量编辑/备注弹窗中，鼠标右键没有专用菜单时应取消/关闭。
- 普通按钮文案统一用 `确定` / `取消`；危险操作可以用更明确的动作词。
- 删除、替换、还原等会修改文件的操作必须先确认。
- 确认后进入耗时操作时，进度走全局底部进度条，不新建进度弹窗。

快捷键：
- `F12` 打开快捷键帮助。
- Story editor 的 `Ctrl+C` / `Ctrl+V` 是内部悬停目标剪贴板，不使用系统剪贴板。
- 图片查看页保留 `Esc` 退出，`Left/Right`、`A/D`、小键盘 `4/6` 切换。
- 未来新增快捷键要先记入维护文档，再接入共享帮助入口。

可复用 UI：
- 全局底部进度条。
- 统一 `Expander` 收缩卡样式，默认收起。
- `CodeBlockBorderStyle` / `CodeBlockTextStyle` 代码块样式。
- `AssetGridViewStyle` / `PaddedAssetGridViewStyle` 素材网格样式。
- Story transient tips：`ShowStoryStatus(...)` / `ShowStoryFunctionTriggeredStatus(...)`。
- 简单文本输入、备注输入、确认取消、取消当前操作、快捷键帮助应逐步抽成 `DialogService`。
- 卡片和 tile 工厂应逐步从 `MainWindow` 移到可复用 helper 或 View 层组件。

主题和音效：
- 夜间模式要作为全局主题能力处理，优先使用 WinUI theme resources 和 `ElementTheme`，不要在页面里散落硬编码颜色。
- 主题设置需要写入 app settings，并在启动时应用。
- 页面音效先记录素材路径，后续由 `UiSoundService` 统一播放：
  - 进入界面：`D:\INput（进入一个界面）.wav`
  - 退出界面：`D:\OUTput（退出一个界面）.wav`
  - 列表选择：`D:\ListSel（列表选择）.wav`
- 音效语义统一：
  - 正向动作使用 IN：确认、添加、创建、打开、进入页面、应用选择、确认后导入、开始流程。
  - 反向动作使用 OUT：取消、关闭、返回、退出页面/查看器、关闭弹窗、删除确认完成、撤回式退出。
  - 并列选项选择使用 ListSel：卡片、列表、tile、tab、分段选项、同级选项切换。
- 后续只要是能互动的控件，都应通过 `UiSoundService` 映射到上述三类音效；避免每个按钮自己播放 wav。
- 音效需要设置开关，避免默认打扰用户。

tips 和按钮提示：
- 早期 tips 设计要保留：长说明挂在功能标题旁，短说明用 hover tooltip。
- help/tips 图标走统一圆形信息按钮样式，不再使用普通 `?` 文本按钮。
- icon-only 按钮必须有悬停提示；文字不够明确的按钮也要补 `ToolTipService.ToolTip`。

## 当前状态

- `MainWindow.xaml.cs` 约 1.6 万行，承担 UI、文件 IO、业务规则、同步脚本、打包和日志。
- `MainWindow.xaml` 约 2200 行，页面都在同一个窗口里。
- 当前已存在较多功能性改动，迁移要避免和这些改动混在一起回退。
- 第 2 步已完成：已把纯数据模型、meta 类型、章节类型常量/选项迁移到 `Models/`，未改业务流程。
- 第 3 步第一批已完成：已把文件大小格式化、哈希、路径比较/路径包含判断迁移到 `Services/FileSystemUtility.cs`。
- 第 3 步第二批已完成：已把文件名/备注/CSV/长文本/时间格式化迁移到 `Services/TextUtility.cs`，把角色颜色标准化和解析迁移到 `Services/ColorUtility.cs`。
- 第 3 步第三批已完成：已把备份、章节、素材库、角色图层 meta、Unreal 状态等固定路径生成迁移到 `Services/WorkspacePathUtility.cs`。
- 第 4 步第一批已完成：已新增 `Services/AppSettingsService.cs`，承接设置读写、默认项目根目录解析和项目根目录准备。
- 第 4 步第二批已完成：已新增 `Services/ProjectWorkspaceService.cs`，承接项目/素材库目录扫描与 meta 读取。
- 第 4 步第二批清理已完成：`MainWindow.ReadProjectInfo()` / `ReadAssetLibraryInfo()` 已删除旧兜底代码，只保留服务委托。
- 第 4 步第三批第一口已完成：项目/素材库创建的文件系统写入已迁移到 `ProjectWorkspaceService`。
- 第 4 步第三批第二口已完成：项目/素材库重命名、项目设置保存、meta 更新时间已迁移到 `ProjectWorkspaceService`。
- 第 4 步第三批第三口已完成：项目/素材库删除、项目关联素材库、素材库引用批量更新/清空已迁移到 `ProjectWorkspaceService`。
- 第 4 步第三批第四口已完成：项目/素材库 zip 导入落盘逻辑已迁移到 `ProjectWorkspaceService`。
- Dialog 复用第一口已完成：新增 `WinUiDialogService`，基础命名输入和删除确认已通过 `IDialogService` 统一。
- Dialog 复用第二口已完成：备份备注、角色滤镜备注、背景图备注已通过 `IDialogService.PromptTextAsync(...)` 统一。
- Dialog 复用第三口第一步已完成：新增通用列表选择弹窗，备份还原选择已通过 `IDialogService.SelectAsync(...)` 统一。
- Shortcut 复用第一口已完成：F12 快捷键帮助入口已迁移到 `ShortcutService`。
- 下一步状态：等待用户确认是否继续迁移 Story/素材选择弹窗，或进入 `UiSoundService` / Theme 相关迁移。

## 分步计划

### 第 1 步：建立 MVVM 基础设施和服务骨架

目标：
- 新增 `Models/`、`Services/`、`ViewModels/` 目录。
- 新增 `ObservableObject`、`RelayCommand`、`AsyncRelayCommand`。
- 新增服务接口或空骨架，但不迁移业务逻辑。
- 预留 `DialogService` / `ShortcutService` 骨架，用来承接确认弹窗、备注弹窗、快捷键帮助和统一取消/确认行为。
- 预留 `ThemeService` / `UiSoundService` 骨架，用来承接夜间模式和页面音效。

改动范围：
- 新增文件为主。
- `MainWindow.xaml.cs` 尽量不动，最多只加注释或极少引用。

验收：
- Release 构建通过。
- 现有功能行为不变。

风险：
- 低。主要风险是命名空间或 Nullable 警告。

回滚点：
- 删除新增目录即可回到迁移前。

### 第 2 步：抽离 Models

目标：
- 把底部 record/class 数据结构搬到 `Models/`。
- 保持命名和 public/internal 可见性稳定。

优先搬：
- `ProjectInfo`
- `AssetLibraryInfo`
- `ChapterInfo`
- `CharacterInfo`
- `UnrealSync*` record
- 轻量配置/元数据 class

暂不搬：
- 强依赖 UI 控件或 `MainWindow` 字段的方法。

验收：
- Release 构建通过。
- 没有行为变化。

风险：
- 中低。主要风险是 private 类型被多个方法依赖，需要调整为 internal。

回滚点：
- 将对应类型移回 `MainWindow.xaml.cs`。

### 第 3 步：抽离纯工具函数

目标：
- 把不依赖 UI、不依赖窗口字段的函数搬到静态 helper/service。

优先搬：
- 路径规范化
- 文件名清洗
- Hash/大小格式化
- 颜色解析
- CSV 基础读写
- 资源索引解析

验收：
- Release 构建通过。
- 排序、导入、备份等入口仍使用原 UI。

风险：
- 中。纯函数看似简单，但很多函数会间接依赖常量。

回滚点：
- 每次只搬一小组，出错就只回退该小组。

### 第 4 步：抽离 AppSettings 和工作区服务

目标：
- 把设置读写、项目根目录创建、项目/素材库发现逻辑抽到 service。

建议服务：
- `AppSettingsService`
- `ProjectWorkspaceService`

验收：
- Release 构建通过。
- 项目/素材库列表仍可加载。

风险：
- 中。路径迁移和命名前缀逻辑必须保持一致。

回滚点：
- 保留原方法直到 service 稳定；先双轨调用，再删除旧方法。

### 第 5 步：抽离素材库服务

目标：
- 背景图、音乐、环境音、特殊音效、角色、滤镜的文件 IO 和排序规范化从窗口移出。

建议服务：
- `AssetLibraryService`
- `AssetOrderingService`

需要特别小心：
- 拖拽排序后的自动命名
- 索引同步到剧情 CSV
- 角色图层服装/表情/装饰兼容范围 metadata

验收：
- Release 构建通过。
- 用户手动确认排序相关功能如果出问题再修。

风险：
- 中高。素材排序会影响剧情表索引。

回滚点：
- 先只让 service 返回“计划”，由 `MainWindow` 执行；确认稳定后再让 service 执行写入。

### 第 6 步：抽离 Story CSV 服务

目标：
- 剧情 CSV 读写、行结构、索引修复、章节分节逻辑迁移到 service。

建议服务：
- `StoryCsvService`
- `StoryIndexRepairService`

验收：
- Release 构建通过。
- 章节编辑 UI 暂时不改。

风险：
- 高。剧情编辑和素材索引耦合很多。

回滚点：
- 分函数组迁移，不一次搬完整 StoryEditor。

### 第 7 步：抽离 Unreal 同步服务

目标：
- 虚幻同步预检、差异计划、manifest、Python 脚本生成、状态缓存移到 service。

建议服务：
- `UnrealSyncService`

保留在窗口：
- 弹窗确认
- 右下角通知
- 全局进度条 UI 绑定/调用

验收：
- Release 构建通过。
- 不主动启动 Unreal。

风险：
- 中高。虚幻路径、RowStruct、DataAsset 名字必须保持不变。

回滚点：
- 先抽“生成计划”和“写脚本”，最后再抽执行 Unreal 进程。

### 第 8 步：引入页面 ViewModel

目标：
- 每个页面逐步拥有 ViewModel，先绑定只读列表和标题，再绑定命令。

建议顺序：
1. `WorkbenchViewModel`
2. `AssetLibraryViewModel`
3. `AssetLibraryDetailViewModel`
4. `SettingsViewModel`
5. `UnrealSyncViewModel`
6. `CharacterDetailViewModel`
7. `StoryEditorViewModel`

验收：
- 每迁移一个页面，Release 构建通过。
- 页面仍能导航。

风险：
- 中高。WinUI 事件、拖拽、文件选择器不适合一次全 MVVM。

回滚点：
- 每个页面独立提交/记录，失败只回退该页面绑定。

### 第 8.5 步：抽离 Dialog 和 Shortcut 复用层

目标：
- 把重复的 `ContentDialog` 创建、确认/取消快捷键、备注输入、简单文本输入、快捷键帮助入口抽成统一服务。
- ViewModel 不直接 new WinUI `ContentDialog`，需要弹窗时通过窗口层或 `DialogService` 协调。

优先抽：
- 确认弹窗：支持 `Enter` 确认、`Esc`/右键取消。
- 备注弹窗：备份备注、素材备注、函数备注等。
- 取消当前操作弹窗：全局进度圈点击后的确认。
- 快捷键帮助弹窗：统一由 `F12` 打开。

验收：
- Release 构建通过。
- 已迁移弹窗的快捷键行为和原来一致。

风险：
- 中。WinUI 的 `XamlRoot` 和窗口上下文必须由 View 层提供，不能藏到纯 ViewModel 里。

回滚点：
- 保留旧弹窗方法，逐个切到新服务；出现问题只切回该弹窗。

### 第 8.6 步：抽离 Theme 和 UI Sound 复用层

目标：
- 增加全局夜间模式切换。
- 增加页面进入、退出、列表选择音效的统一入口和设置开关。
- 避免页面方法直接播放 wav 或直接改大量硬编码颜色。

优先抽：
- `ThemeService`：读取/保存主题设置，应用 `ElementTheme`。
- `UiSoundService`：加载并播放 `D:\INput（进入一个界面）.wav`、`D:\OUTput（退出一个界面）.wav`、`D:\ListSel（列表选择）.wav`。
- 设置页开关：夜间模式、UI 音效开关。
- 统一交互音效映射：正向/确认/添加 -> IN，反向/取消/退出 -> OUT，并列选项选择 -> ListSel。

验收：
- Release 构建通过。
- 未开启音效时不播放任何 UI 音。
- 切换主题后主要页面不出现明显白块或文字不可读。

风险：
- 中。当前界面仍有一些硬编码颜色，夜间模式前需要逐步改成 theme resources。

回滚点：
- 设置项默认关闭；出现问题可只禁用服务调用，不影响核心功能。

### 第 9 步：拆分 XAML Views

目标：
- 在 ViewModel 稳定后，再把 `MainWindow.xaml` 拆成 UserControl 页面。

建议：
- 先拆低风险页面：设置页、虚幻同步页。
- 最后拆故事编辑器。

验收：
- Release 构建通过。
- 页面切换正常。

风险：
- 高。XAML name scope、事件绑定、资源作用域容易出问题。

回滚点：
- 一页一页拆，不同时拆多个页面。

## 步骤记录

### 2026-05-20：建立迁移文档

状态：
- 已新增本文。
- 未改业务代码。

下一步：
- 等待用户确认是否开始第 1 步。

### 2026-05-20：补充弹窗、快捷键和复用 UI 规则

状态：
- 已在本文补充复用交互和风格清单。
- 已在 `Docs/MaintenanceNotes.md` 增加 `Dialog, Shortcut, and Reusable UI Rule`。
- 未改业务代码。

下一步：
- 等待用户确认是否开始第 1 步。

### 2026-05-20：补充夜间模式、tips 图标、悬停提示和 UI 音效记录

状态：
- 已在本文记录夜间模式、页面音效素材路径、tips 图标和按钮悬停提示要求。
- 已在 `Docs/MaintenanceNotes.md` 增加主题和 UI 音效规则。
- 已把设置页 help/tips 图标换成共享圆形信息按钮样式。

下一步：
- 等待用户确认是否开始第 1 步。

### 2026-05-20：第 1 步 - 建立 MVVM 基础设施和服务骨架

改动：
- 新增 `Models/README.md`，标记后续模型迁移位置。
- 新增 `ViewModels/ObservableObject.cs`。
- 新增 `ViewModels/RelayCommand.cs`。
- 新增 `ViewModels/AsyncRelayCommand.cs`。
- 新增 `Services/DialogContracts.cs`。
- 新增 `Services/IDialogService.cs`。
- 新增 `Services/IShortcutService.cs`。
- 新增 `Services/IThemeService.cs`。
- 新增 `Services/IUiSoundService.cs`。

验证：
- Release 构建通过，0 警告，0 错误。

风险/注意：
- 本步只新增骨架，不改变现有页面绑定和业务行为。
- `IDialogService`、`IShortcutService`、`IThemeService`、`IUiSoundService` 目前只是接口，后续要由窗口层或服务实现。
- `AsyncRelayCommand` 会吞掉主动取消产生的 `OperationCanceledException`，其他异常通过 `ExecutionFailed` 事件交给上层处理。

下一步建议：
- 等待用户确认后开始第 2 步：把底部 record/class 数据模型小批量搬到 `Models/`。

### 2026-05-20：第 2 步第一批 - 抽离纯数据 Models

改动：
- 新增 `Models/CoreModels.cs`：项目、章节、日志、章节类型选项、迁移结果等核心轻量模型。
- 新增 `Models/BackupModels.cs`：备份条目、备份进度、备份 meta。
- 新增 `Models/AssetLibraryModels.cs`：素材库、背景、音频、函数、角色、角色图层、角色滤镜等模型。
- 新增 `Models/StoryModels.cs`：故事行、分节状态、选项备注、故事编辑撤回、故事选择项和 CSV 兼容结果。
- 新增 `Models/AssetIndexModels.cs`：素材索引同步、章节修复、索引变化和警告模型。
- 新增 `Models/UnrealSyncModels.cs`：虚幻同步上下文、计划、导入组、DataTable、立绘和结果模型。
- 新增 `Models/AppSettingsModels.cs`：应用设置、项目/章节/素材库 meta。
- 从 `MainWindow.xaml.cs` 删除上述已搬迁的内嵌类型。

验证：
- Release 构建通过，0 警告，0 错误。

风险/注意：
- `UnrealSyncValidation` 仍留在 `MainWindow.xaml.cs`，因为它直接使用 WinUI 的 `InfoBarSeverity`。后续如果要继续抽离，需要先把 UI severity 转成模型层自己的枚举。
- 这些类型当前放在根命名空间 `GalExcleTools`，是为了保持本轮迁移改动小。后续如果要移动到 `GalExcleTools.Models`，需要一次性补 using 或 namespace alias。

下一步建议：
- 继续第 2 步第二批：将章节类型常量/选项搬入 `Models/`。
- 或进入第 3 步：先抽离纯工具函数，降低 `MainWindow.xaml.cs` 体积。

### 2026-05-20：第 2 步第二批 - 抽离章节类型边界模型

改动：
- 将 `ChapterKind` 搬到 `Models/CoreModels.cs`。
- 新增 `ChapterTypes.Options`，集中保存章节类型选项。
- `MainWindow.xaml.cs` 的章节类型读取改为使用 `ChapterTypes.Options`。

验证：
- Release 构建通过，0 警告，0 错误。

风险/注意：
- `UnrealSyncValidation` 仍留在窗口层，原因同上：它依赖 WinUI `InfoBarSeverity`。
- 第 2 步目前到此收尾，剩余 UI 相关模型会在 Unreal sync 服务化时再拆。

下一步建议：
- 开始第 3 步：抽离纯工具函数，例如路径、文件名清洗、格式化、hash、颜色解析等。

### 2026-05-20：第 3 步第一批 - 抽离文件系统纯工具函数

改动：
- 新增 `Services/FileSystemUtility.cs`。
- 将文件大小格式化、文件 hash、字符串 SHA256、路径比较、精确路径比较、路径包含判断、路径末尾分隔符清理迁移到 `FileSystemUtility`。
- `MainWindow.xaml.cs` 通过 `using static GalExcleTools.Services.FileSystemUtility;` 继续使用原方法名，减少本轮改动面。
- 删除 `MainWindow.xaml.cs` 中对应的重复静态方法定义。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批只迁移无 UI 依赖的纯函数，不改变排序、导入、备份、虚幻同步等调用流程。
- `FileSystemUtility` 当前放在 `GalExcleTools.Services` 命名空间；调用端暂时使用 static using，后续 service 化时可以逐步改成显式服务调用。

下一步建议：
- 继续第 3 步第二批：抽离文件名清理、颜色解析、通用文本格式化等纯工具函数。
- 如果下一批碰到依赖窗口常量的函数，优先只搬真正独立的部分，避免把 UI 状态一起带进 service。

### 2026-05-20：第 3 步第二批 - 抽离文本、文件名和颜色工具函数

改动：
- 新增 `Services/TextUtility.cs`。
- 将备份备注规范化、函数选项备注规范化、备份文件名清理、导入根文件夹名清理、项目/素材库前缀文件夹名构建、素材备注清理、角色文件夹名清理、Unreal 资产名清理、CSV 转义、整数解析、用时格式化、长文本截断迁移到 `TextUtility`。
- 新增 `Services/ColorUtility.cs`。
- 将角色颜色默认值、颜色 hex 规范化、旧颜色兼容、hex 到 `Windows.UI.Color` 解析迁移到 `ColorUtility`。
- `MainWindow.xaml.cs` 通过 static using 继续使用原方法名，避免本轮修改调用点。
- 删除 `MainWindow.xaml.cs` 中对应的重复静态方法定义，并移除不再需要的 `System.Security.Cryptography` using。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- `TextUtility.SanitizeImportedRootFolderName` 仍会用当前时间生成兜底名，这是为了保留旧行为；后续如需更可测试，可以把时间源抽成参数。
- `ColorUtility` 仍引用 `Windows.UI.Color`，这是当前 WinUI/Unreal 同步代码的实际依赖。若后续要让模型层完全脱离 UI 类型，需要再做一层自己的颜色 DTO。
- 本批没有主动启动程序做运行检查。

下一步建议：
- 继续第 3 步第三批：抽离路径生成、素材目录定位、章节 CSV 命名等仍然偏纯逻辑的 helper。
- 暂时不要碰拖拽排序、导入执行、Unreal 进程执行这类状态重的流程；等工具函数和路径规则稳定后，再进入 service 拆分。

### 2026-05-20：第 3 步第三批 - 抽离工作区固定路径工具函数

改动：
- 新增 `Services/WorkspacePathUtility.cs`。
- 将备份目录/meta 路径、章节目录路径、故事 sections/choice notes 路径、角色 face/adorn scope meta 路径、素材库各分类目录路径、音频目录分派、Unreal 备份目录、Unreal 同步状态路径、Content 路径转 `/Game` 路径等迁移到 `WorkspacePathUtility`。
- `MainWindow.xaml.cs` 通过 static using 继续使用原方法名，删除对应重复静态方法定义。
- 本批没有迁移 `GetChapterStoryCsvPath`，因为它会检查旧 `.story.csv` 并执行 `File.Move`，不是纯路径函数。
- 本批没有迁移 `GetUnrealStorySectionCacheFolder`，因为它依赖章节小节后缀规则；建议和章节 CSV/小节命名规则一起处理。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- `WorkspacePathUtility` 内部暂时保留与 `MainWindow` 相同的文件夹/文件名常量，避免本轮牵动全局常量位置；后续可以将这些常量集中成 `WorkspaceNames` 或配置类。
- 因为调用点仍使用原方法名，本批行为变化应仅限于代码组织。
- 本批没有主动启动程序做运行检查。

下一步建议：
- 进入第 4 步第一批：抽离 `AppSettings` 读写、默认项目根目录准备、项目/素材库发现这类工作区服务。
- 或继续第 3 步第四批：抽离章节 CSV 命名、小节 CSV 命名、章节 code 规则等仍偏纯逻辑但更靠近 Story 的 helper。

### 2026-05-20：第 4 步第一批 - 抽离 AppSettings 服务

改动：
- 新增 `Services/AppSettingsService.cs`。
- 将设置文件目录、设置文件路径、设置读取、设置保存、默认项目根目录解析、项目根目录创建、从用户选择的父目录生成 `GalExcelProject` 根目录路径迁移到 `AppSettingsService`。
- `MainWindow.xaml.cs` 新增 `_appSettingsService` 字段；启动时改为 `_appSettingsService.Load()` 和 `_appSettingsService.ResolveProjectRootPath(...)`。
- `MainWindow.SaveAppSettings()` 保留为过渡桥接方法，内部改为 `_appSettingsService.Save(_appSettings)`，避免一次性修改全部设置保存调用点。
- `EnsureProjectRootDirectory(...)` 仍保留 UI 刷新、日志和卡片加载；实际目录创建委托给 `_appSettingsService.EnsureProjectRootDirectory(...)`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有迁移项目/素材库发现逻辑，`GetProjects()` / `GetAssetLibraries()` 仍在 `MainWindow.xaml.cs`，避免同时牵动首页和素材库加载。
- `AppSettingsService` 当前直接使用 `AppSettings` 模型和 JSON 文件格式，不改变现有设置文件位置与内容。
- 本批没有主动启动程序做运行检查。

下一步建议：
- 第 4 步第二批：抽离项目/素材库发现与 meta 读取逻辑，例如 `GetProjects()`、`GetAssetLibraries()`、`ReadProjectInfo()`、`ReadAssetLibraryInfo()`、缩略图解析等。
- 迁移时保留窗口层负责 `GridView` 填充和页面导航，service 只返回模型列表。

### 2026-05-20：第 4 步第二批 - 抽离项目/素材库发现服务

改动：
- 新增 `Services/ProjectWorkspaceService.cs`。
- 将项目目录扫描、素材库目录扫描、项目 meta 读取、素材库 meta 读取、缩略图路径解析迁移到 `ProjectWorkspaceService`。
- `MainWindow.GetProjects()` / `MainWindow.GetAssetLibraries()` 改为委托服务返回模型列表。
- `MainWindow.ReadProjectInfo()` / `MainWindow.ReadAssetLibraryInfo()` 作为过渡包装保留，但入口已优先委托 `ProjectWorkspaceService`，避免本批大范围改动既有调用点。

验证：
- 第一次 Release 构建失败：`ProjectWorkspaceService.cs` 缺少 `System.Collections.Generic` using。
- 已补充 using 后重新构建通过：0 警告，0 错误。

风险/注意：
- 本批只迁移读取型工作区逻辑，窗口层仍负责 `GridView` 卡片、导航、弹窗、日志。
- 写入型逻辑仍在 `MainWindow.xaml.cs`：项目/素材库创建、重命名、删除、关联素材库、缩略图复制等还没有迁移。
- `MainWindow.ReadProjectInfo()` 和 `ReadAssetLibraryInfo()` 内部仍保留旧兜底代码，后续可以在确认服务稳定后删除，进一步瘦身。
- 本批没有主动启动程序做运行检查。

下一步建议：
- 第 4 步第三批：抽离项目/素材库创建、重命名、删除、关联素材库、缩略图复制等写入型工作区操作。
- 或先做一小步清理：删除 `ReadProjectInfo()` / `ReadAssetLibraryInfo()` 的旧兜底代码，让窗口包装只剩一行委托。

### 2026-05-20：第 4 步第二批清理 - 删除项目/素材库读取旧兜底代码

改动：
- `MainWindow.ReadProjectInfo()` 改为只保留 `_projectWorkspaceService.ReadProjectInfo(projectPath)`。
- `MainWindow.ReadAssetLibraryInfo()` 改为只保留 `_projectWorkspaceService.ReadAssetLibraryInfo(assetLibraryPath)`。
- 删除 `MainWindow.ResolveThumbnailPath(...)`，缩略图解析只保留在 `ProjectWorkspaceService` 内。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- `MainWindow.ReadJson<T>()` 仍保留，因为备份、章节、Story、函数、滤镜、Unreal 同步等功能还在使用。
- 本批只清理读取包装，没有迁移写入型工作区操作。

下一步建议：
- 第 4 步第三批：抽离项目/素材库创建、重命名、删除、关联素材库、缩略图复制等写入型工作区操作。

### 2026-05-20：第 4 步第三批第一口 - 抽离项目/素材库创建写入

改动：
- `ProjectWorkspaceService` 新增 `CreateProject(...)`，负责项目文件夹创建、Tools 目录创建、缩略图复制、项目 meta 写入、Chapters 目录创建，并返回 `ProjectInfo`。
- `ProjectWorkspaceService` 新增 `CreateAssetLibrary(...)`，负责素材库文件夹创建、Tools 目录创建、分类目录创建、缩略图复制、素材库 meta 写入，并返回 `AssetLibraryInfo`。
- `ProjectWorkspaceService` 新增 `BuildProjectFolderPath(...)` / `BuildAssetLibraryFolderPath(...)`，保留 `项目-` / `素材库-` 前缀规则。
- `ProjectWorkspaceService.EnsureAssetLibraryCategoryFolders(...)` 承接素材库分类目录创建。
- `MainWindow.CreateProjectButton_Click(...)` / `CreateAssetLibraryButton_Click(...)` 改为调用服务；UI 校验、错误提示、页面切换、日志仍留在窗口层。
- 删除窗口层 `CopyThumbnailToTools(...)`，避免缩略图复制逻辑重复。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批只迁移创建写入，重命名、删除、项目关联素材库、项目设置保存、导入包仍在窗口层。
- `MainWindow.EnsureAssetLibraryCategoryFolders(...)` 暂时保留为旧调用点的桥接方法，内部委托 `ProjectWorkspaceService.EnsureAssetLibraryCategoryFolders(...)`。
- 本批没有主动启动程序做运行检查。

下一步建议：
- 第 4 步第三批第二口：抽离项目/素材库重命名与 meta 更新时间。
- 之后再抽删除、关联素材库引用更新、导入包落盘。

### 2026-05-20：第 4 步第三批第二口 - 抽离重命名和 meta 更新时间

改动：
- `ProjectWorkspaceService` 新增 `RenameProject(...)`，负责项目目录重命名和项目 meta 更新时间。
- `ProjectWorkspaceService` 新增 `UpdateProjectInfo(...)`，负责项目详情页保存时的目录重命名、项目名称/代号写入和 meta 更新时间。
- `ProjectWorkspaceService` 新增 `RenameAssetLibrary(...)`，负责素材库目录重命名和素材库 meta 更新时间。
- `ProjectWorkspaceService` 新增 `TouchProjectLastEditedAt(...)` / `TouchAssetLibraryLastEditedAt(...)`。
- `MainWindow` 中对应方法改为委托服务；UI 校验、日志、页面刷新、章节前缀同步仍在窗口层。
- 调整 `SaveProjectSettingsAsync(...)` 顺序：先由服务移动/写项目 meta，再用返回的 `updatedProject.Path` 同步章节项目代号前缀，避免新路径尚未创建时同步章节。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 项目/素材库删除还未迁移。
- 项目关联素材库、素材库重命名后批量更新项目引用、素材库删除后清空项目引用还在 `MainWindow.xaml.cs`。
- 本批没有主动启动程序做运行检查。

下一步建议：
- 第 4 步第三批第三口：抽离 `ChangeProjectAssetLibraryAsync(...)` 的 meta 写入部分、`UpdateProjectAssetLibraryReferences(...)`、`ClearProjectAssetLibraryReferences(...)` 和删除操作。

### 2026-05-20：第 4 步第三批第三口 - 抽离删除和素材库引用关系写入

改动：
- `ProjectWorkspaceService` 新增 `DeleteProject(...)`。
- `ProjectWorkspaceService` 新增 `DeleteAssetLibrary(...)`，删除素材库前会清空引用它的项目 meta。
- `ProjectWorkspaceService` 新增 `SetProjectAssetLibrary(...)`，用于更改项目关联素材库。
- `ProjectWorkspaceService` 新增 `UpdateProjectAssetLibraryReferences(...)`，用于素材库重命名后批量更新项目引用。
- `ProjectWorkspaceService` 新增 `ClearProjectAssetLibraryReferences(...)`，用于素材库删除时批量清空项目引用。
- 删除 `MainWindow` 中 `UpdateProjectAssetLibraryReferences(...)` / `ClearProjectAssetLibraryReferences(...)` 桥接方法；窗口层直接调用 service。
- `MainWindow` 仍保留确认弹窗、当前页面状态判断、刷新和日志。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 项目/素材库 zip 导入落盘逻辑仍在 `MainWindow.xaml.cs`。
- `ReadJson<T>()` 仍留在窗口层供章节、Story、函数、滤镜、Unreal 同步等模块使用。
- 本批没有主动启动程序做运行检查。

下一步建议：
- 第 4 步第三批第四口：抽离项目/素材库 zip 导入的落盘逻辑。
- 或先收束第 4 步，进入页面 ViewModel/可复用 Dialog/Theme/Sound 相关迁移。

### 2026-05-20：第 4 步第三批第四口 - 抽离项目/素材库 zip 导入落盘

改动：
- `ProjectWorkspaceService` 新增 `ImportProjectArchive(...)`，负责解压项目包、识别 `Tools/project.meta.json`、计算目标目录、复制目录、修正 meta 并返回 `ProjectInfo`。
- `ProjectWorkspaceService` 新增 `ImportAssetLibraryArchive(...)`，负责解压素材库包、识别 `Tools/asset-library.meta.json`、计算目标目录、复制目录、补齐素材库分类目录、修正 meta 并返回 `AssetLibraryInfo`。
- `ProjectWorkspaceService` 内部新增导入专用 helper：`GetUniqueDirectoryPath(...)`、`CopyDirectoryContents(...)`、`FindImportedProjectRoot(...)`、`FindImportedAssetLibraryRoot(...)`。
- `MainWindow` 的拖入导入流程保留进度条、取消、刷新和日志；实际落盘改为调用 service。
- 删除 `MainWindow` 中项目/素材库导入落盘的大块实现和导入根目录查找 helper。

验证：
- 第一次构建命令输错项目名：`GalExTools.csproj`，MSBuild 报项目文件不存在；这是命令 typo，不是代码问题。
- 使用正确命令 `dotnet build GalExcleTools.csproj --configuration Release --runtime win-x64 -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true` 后，Release 构建通过：0 警告，0 错误。

风险/注意：
- `MainWindow.CopyDirectoryContents(...)` 仍保留，因为备份还原流程还在使用。
- 备份/导出 zip 逻辑还在窗口层，后续可以归入 Backup/Package service，而不是继续塞进 ProjectWorkspaceService。
- 本批没有主动启动程序做运行检查。

下一步建议：
- 第 4 步可以先收束，开始引入页面 ViewModel，例如 Workbench/AssetLibrary 列表页。
- 或优先抽可复用 Dialog/Shortcut 服务，因为用户明确要求弹窗快捷键和复用风格长期保持。

### 2026-05-20：Dialog 复用第一口 - 基础输入和确认服务

改动：
- 新增 `Services/WinUiDialogService.cs`，实现 `IDialogService`。
- `WinUiDialogService` 支持基础消息弹窗、确认弹窗和文本输入弹窗。
- 统一基础取消交互：`Esc` 隐藏弹窗，鼠标右键隐藏弹窗。
- `MainWindow` 新增 `_dialogService` 字段，使用 `new WinUiDialogService(() => Content.XamlRoot)` 绑定窗口 XAML root。
- `ShowNameInputDialogAsync(...)` 改为委托 `_dialogService.PromptTextAsync(...)`。
- `ShowDeleteConfirmDialogAsync(...)` 改为委托 `_dialogService.ConfirmAsync(...)`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批只迁移最基础的命名输入和删除确认。复杂弹窗仍在窗口层，例如备份备注、还原列表、章节编辑、Story 选择、Unreal 同步确认等。
- `WinUiDialogService` 现在尚未接入 `UiSoundService`，后续音效服务落地后应在确认/取消结果里统一播放 IN/OUT/ListSel。
- 本批没有主动启动程序做运行检查。

下一步建议：
- Dialog 复用第二口：迁移备份备注、还原选择、简单备注编辑弹窗。
- 后续再抽 `ShortcutService`，把 F12 快捷键帮助和通用 Esc/Enter/右键规则集中记录与调用。

### 2026-05-20：Dialog 复用第二口 - 简单备注输入弹窗

改动：
- 扩展 `TextInputDialogRequest`，增加 `Message`、`Width`、`MaxLength`，让服务能覆盖带说明文字、不同宽度、长度限制的文本输入弹窗。
- `WinUiDialogService.PromptTextAsync(...)` 支持说明文字和输入框配置。
- `ShowBackupNoteDialogAsync(...)` 改为通过 `_dialogService.PromptTextAsync(...)` 处理，保留 `NormalizeBackupNote(...)`。
- `ShowCharacterFilterRemarkDialogAsync(...)` 改为通过 `_dialogService.PromptTextAsync(...)` 处理，保留 `SanitizeRemark(...)`。
- `SetBackgroundImageRemarkAsync(...)` 的备注输入改为通过 `_dialogService.PromptTextAsync(...)` 处理。
- 将 `DialogContracts.cs` 的默认按钮文案改为 ASCII `OK` / `Cancel`，避免当前文件编码下默认中文变成 `??`；实际业务弹窗仍显式传入中文按钮文案。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 列表选择型弹窗仍在窗口层，例如备份还原列表、Story 选择、素材选择等。
- 本批没有主动启动程序做运行检查。

下一步建议：
- Dialog 复用第三口：抽列表选择弹窗能力，例如还原备份选择、Story/素材选择。
- 后续接入 `UiSoundService` 后，文本输入确认使用 IN 音效，取消/右键/Esc 使用 OUT 音效。

### 2026-05-20：Dialog 复用第三口第一步 - 通用列表选择弹窗

改动：
- `DialogContracts.cs` 新增 `SelectionDialogItem<T>` 和 `SelectionDialogRequest<T>`。
- `IDialogService` 新增 `SelectAsync<T>(...)`。
- `WinUiDialogService` 实现 `SelectAsync<T>(...)`，使用单选 `ListView`，默认选中第一项，支持标题、说明、确认/取消按钮、宽度和最大高度。
- `ShowFolderRestoreDialogAsync(...)` 改为通过 `_dialogService.SelectAsync(...)` 处理，项目/素材库/章节的备份还原选择都会走通用选择弹窗。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- Story 选择弹窗还没有迁移。它包含更多对象类型和预览信息，适合下一小步单独做。
- 本批没有主动启动程序做运行检查。

下一步建议：
- 继续 Dialog 复用第三口第二步：迁移 Story/素材选择弹窗。
- 或先抽 `ShortcutService`，把 F12 帮助和通用快捷键入口集中。

### 2026-05-20：Shortcut 复用第一口 - F12 帮助服务

改动：
- 新增 `Services/ShortcutService.cs`，实现 `IShortcutService`。
- `MainWindow` 新增 `_shortcutService` 字段，通过 `new ShortcutService(_dialogService)` 复用 Dialog 服务。
- `ShowStoryShortcutHelpDialogAsync(...)` 改为委托 `_shortcutService.ShowShortcutHelpAsync(...)`。
- 删除窗口层 `CreateShortcutHelpText(...)` 和本地 F12 帮助弹窗拼装。
- 快捷键帮助文案暂时使用 ASCII 英文，避免当前文件编码下中文字符串在新文件中出现乱码；后续资源化/本地化时再统一中文文案。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批只迁移 F12 帮助弹窗，不改变 Story 编辑器实际快捷键分发逻辑。
- ToolTip 里仍有 `F12` 中文提示，后续资源化时可一起处理。
- 本批没有主动启动程序做运行检查。

下一步建议：
- 继续 Dialog 复用第三口第二步：迁移 Story/素材选择弹窗。
- 或开始 `UiSoundService` 第一口：先建立音效服务实现和设置开关，但不一次性接入所有按钮。

### 2026-05-20：UiSound 复用第一口 - 音效服务与弹窗接线

改动：
- 新增 `Services/UiSoundService.cs`，实现 `IUiSoundService`。
- 音效路径按用户指定固定为：
  - `D:\INput（进入一个界面）.wav`：正向/确认/进入。
  - `D:\OUTput（退出一个界面）.wav`：取消/关闭/退出。
  - `D:\ListSel（列表选择）.wav`：并列选择/列表选择。
- 路径在源码里使用 Unicode escape 保存，避免当前命令行/补丁编码导致中文路径变形。
- `AppSettings` 新增 `UiSoundEnabled`，默认开启。
- 设置页新增“播放界面音效”复选框；开关会保存到 settings.json，并实时同步 `_uiSoundService.IsEnabled`。
- `WinUiDialogService` 构造函数新增可选 `IUiSoundService`，并在弹窗按钮/快捷取消触发时统一播放：
  - Primary：Positive / IN。
  - Cancel、Esc、右键关闭、Close：Negative / OUT。
  - Secondary：Selection / ListSel。
- `MainWindow` 新增 `_uiSoundService` 字段，并把它传入 `WinUiDialogService`。目前已迁移到 DialogService 的弹窗会自动获得音效。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 这一步只把音效接入已服务化的弹窗。普通按钮、导航、列表项、素材卡片等还没有逐个接入。
- 如果用户机器上缺少对应 wav 文件，服务会静默跳过，不阻塞主流程。
- 本批没有主动启动程序做运行检查。

下一步建议：
- 继续把高频交互接到 `IUiSoundService`：页面进入/返回、创建/保存、列表选择、素材卡片点击、导入导出等。
- 或先迁移 Story/素材选择弹窗，让更多现有弹窗自然吃到统一确认/取消音效。

### 2026-05-20：UiSound 复用第二口 - 高频交互接线

改动：
- `MainWindow` 新增 `PlayPositiveSound()` / `PlayNegativeSound()` / `PlaySelectionSound()` 三个过渡 helper，窗口层不直接散落 `_uiSoundService.Play(...)` 细节。
- 左侧 `NavigationView` 用户点击切页时播放 ListSel；程序内部同步选中项时通过 `_isChangingShellSelectionInternally` 静默，避免页面方法内部重复触发音效。
- 接入 Positive / IN：
  - 新建项目卡、新建素材库卡。
  - 项目创建成功、素材库创建成功。
  - 项目设置保存成功、角色设置保存成功。
  - 素材导入成功：背景图、音乐、环境音、特殊音效、服装、表情、装饰。
- 接入 Negative / OUT：
  - 创建项目取消、创建素材库取消。
  - 项目详情返回、章节编辑器返回、素材库详情返回、角色详情返回。
  - 背景/立绘图层查看器关闭、音乐播放器关闭。
- 接入 Selection / ListSel：
  - 项目卡、素材库卡、背景图卡、音频卡、角色卡、角色图层卡点击。
  - 刷新项目/素材库列表。
  - 缩略图选择成功。
  - 查看器上一张/下一张成功切换，音乐上一曲/下一曲成功切换，音乐播放/暂停。
- 添加角色、添加滤镜、新建章节等仍主要依赖已经迁移的 DialogService 音效，避免同一次确认出现双响。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批只覆盖高频和低风险入口；仍有很多零散按钮、菜单 flyout、Story 编辑器内部快捷操作尚未逐一接入。
- 当前没有主动运行程序做人工听感检查；如果某个操作听起来重复或时机不对，后续可以按入口微调。

下一步建议：
- 继续迁移 Story/素材选择弹窗到 `IDialogService`，让更多选择类操作统一走弹窗、快捷键和音效。
- 或继续补按钮音效第三口：菜单 flyout、Story 行操作、Unreal 同步、备份/还原、导入导出包等。

### 2026-05-20：UiSound 修正 - 弹窗音效改为按钮触发

改动：
- 修正 `WinUiDialogService` 的音效触发时机：不再等 `ContentDialog.ShowAsync()` 返回后按结果播放。
- Primary / Secondary / Close 现在绑定到 `ContentDialog` 的按钮点击事件，用户按下按钮时立即播放对应音效。
- Esc 和鼠标右键取消仍保持 OUT 音效，但改为在隐藏弹窗前播放。
- 这样更符合“按钮触发音效”的交互手感，也避免后续误把音效理解成业务结果提示。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- `ContentDialog` 按 Enter 触发默认 Primary 时，仍应走 Primary button click 事件；如果实际听感有偏差，再单独按键处理。
- 本批没有主动启动程序做运行检查。

下一步建议：
- 接下来补按钮音效时，以“用户点击/按键触发”为准；完成后业务是否成功只用于决定是否追加状态提示，不再作为按钮音效的主要触发点。
- 可以继续补菜单 flyout、Story 行操作、Unreal 同步、备份/还原、导入导出包等按钮入口。

### 2026-05-20：UiSound 修正 - 主窗口音效改为点击即反馈

改动：
- 修正上一批主窗口音效的语义：音效作为“用户操作反馈”，优先在按钮/卡片点击入口触发，而不是等业务成功后触发。
- 前移到点击入口的操作：
  - 刷新项目/素材库。
  - 选择项目/素材库缩略图。
  - 创建项目、创建素材库。
  - 保存项目设置、保存角色设置。
  - 导入背景图、音乐、环境音、特殊音效、服装、表情、装饰。
- 保留在有效选择之后触发的操作：
  - 项目卡、素材库卡、背景图卡、音频卡、角色卡、角色图层卡。
  - 查看器上一张/下一张、播放器上一曲/下一曲，这些仍只在确实切换成功时播放 ListSel，避免到头也响。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 这一步没有继续扩大覆盖范围，主要是把上一批已有接线的触发时机校正到按钮语义。
- 后续补新入口时，默认规则是：点击/按键立即播放；真正失败则用 InfoBar/进度条/日志表达，不靠音效延迟表达。
- 本批没有主动启动程序做运行检查。

下一步建议：
- 继续补菜单 flyout、Story 行操作、Unreal 同步、备份/还原、导入导出包等按钮入口。
- 或迁移 Story/素材选择弹窗，减少手写弹窗里音效和快捷键重复实现。

### 2026-05-20：Dialog 复用第四口第一步 - Story 基础素材选择弹窗

当前段落：
- 仍处在“可复用交互服务”阶段，还没有进入大规模 ViewModel 拆页。
- 已完成基础 `DialogService`、`ShortcutService`、`UiSoundService`，并修正音效为按钮/点击触发。
- 本步开始迁移 Story/素材选择弹窗。

改动：
- `SelectionDialogRequest<T>` 新增 `IsSelected`，用于通用选择弹窗默认选中当前项。
- `WinUiDialogService.SelectAsync<T>(...)` 支持默认选中项，并在用户切换列表选中项时播放 ListSel。
- `ChooseStoryAssetIndexAsync(...)` 不再手写 `ContentDialog + ListView`。
- 剧情编辑器中的基础素材选择已迁移到 `_dialogService.SelectAsync(...)`：
  - 更换背景图。
  - 更换 BGM。
  - 更换环境音。
- 选择项使用 `StoryAssetChoice` 对象作为返回值，避免 `int` 值类型在取消时和索引 `0` 混淆。

验证：
- 第一次构建暴露 `int` 选择值取消语义问题，已改为 `StoryAssetChoice`。
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 带图片预览 tooltip 的 Story 角色/立绘图层选择弹窗暂未迁移；这些需要先让 DialogService 支持可选 tooltip/自定义 item，再迁会更稳。
- Choice 选项备注弹窗、查看选项弹窗仍是手写 `ContentDialog`，后续可以单独迁移。
- 本批没有主动启动程序做运行检查。

下一步建议：
- Dialog 复用第四口第二步：迁移无预览的 Story 通用选择弹窗，例如 BGM 函数、跳转章节/小节、移除函数、特殊音效选择等。
- 或增强 `SelectAsync<T>` 的 item 模板/tooltip 能力，再迁移角色和图层选择弹窗。

### 2026-05-20：Dialog 复用第四口第二步 - Story 无预览选择弹窗

改动：
- 新增 `ShowStorySimpleChoiceDialogAsync(...)` 过渡方法，内部委托 `_dialogService.SelectAsync(...)`。
- 保留原 `ShowStoryChoiceDialogAsync(...)` 给带图片预览 tooltip 的选择弹窗使用，避免角色/图层选择丢失预览能力。
- 以下无预览 Story 选择已迁移到通用 DialogService：
  - 填写函数。
  - 移除函数。
  - BGM 函数 Start/Stop。
  - 背景切换模式。
  - 跳转章节。
  - 跳转小节。
  - 特殊音效索引。
- 这些弹窗现在共享统一的确认/取消快捷键、右键取消、按钮音效和列表选择音效。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- `ShowStorySimpleChoiceDialogAsync(...)` 当前说明文案是通用短句，后续可以扩展 request 参数，让不同弹窗显示更准确的说明。
- 带图片预览的 Story 角色/图层选择仍保留手写 `ContentDialog`。
- Choice 选项备注弹窗和查看选项弹窗仍是手写复杂弹窗。
- 本批没有主动启动程序做运行检查。

下一步建议：
- 增强 `SelectionDialogRequest<T>`，支持可选 `ToolTip`/预览内容，然后迁移角色选择、服装/表情/装饰/滤镜选择这些带预览的 Story 弹窗。
- 或先迁移 Choice 选项备注/查看选项弹窗，把复杂表单弹窗也纳入统一按钮音效和 Esc/右键规则。

### 2026-05-20：Dialog 复用第四口第三步 - Story 带预览选择弹窗

改动：
- `SelectionDialogItem<T>` 新增可选 `ToolTip`。
- `WinUiDialogService.SelectAsync<T>(...)` 在生成 `ListViewItem` 时会挂载 item tooltip。
- `ShowStoryChoiceDialogAsync(...)` 不再手写 `ContentDialog + ListView`，改为准备 `SelectionDialogItem<StoryObjectChoice>` 并委托 `_dialogService.SelectAsync(...)`。
- 原有 Story 预览 tooltip 继续复用 `CreateStoryChoicePreviewToolTipAsync(...)`，不会丢失角色/图层选择时的图片预览。
- 因为 `ShowStoryChoiceDialogAsync(...)` 已迁移，以下选择类 Story 弹窗都统一走 DialogService：
  - 角色选择。
  - 服装、表情、装饰选择。
  - 角色滤镜选择。
  - 角色详情里从图层路径选择当前预览层。
  - 之前已迁移的无预览函数/跳转/音效选择也继续走 DialogService。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 当前选择弹窗说明文案仍是通用“选择一个项目后确认。”，后续可把说明文本作为参数传入。
- Choice 选项备注、查看选项、章节/函数/角色编辑这类复杂表单弹窗仍是手写 `ContentDialog`。
- 本批没有主动启动程序做运行检查。

下一步建议：
- 迁移 Choice 选项备注和查看选项弹窗，让复杂 Story 表单也获得统一按钮音效、Esc/右键取消。
- 或抽出更通用的 `ShowCustomContentAsync(...)`，给章节编辑、函数编辑、角色编辑这些复杂表单弹窗逐步复用。

### 2026-05-20：Dialog 复用第五口第一步 - 自定义内容弹窗入口

改动：
- 新增 `ContentDialogRequest`，用于承载任意 WinUI `UIElement` 内容。
- `IDialogService` 新增 `ShowContentAsync(...)`。
- `WinUiDialogService.ShowContentAsync(...)` 复用统一的：
  - Primary / Secondary / Close 按钮音效。
  - Esc 取消。
  - 鼠标右键取消。
  - XamlRoot 绑定。
- 迁移 `ShowChoiceFunctionNoteDialogAsync(...)`：Choice 选项备注弹窗不再手写 `ContentDialog`。
- 迁移 `ShowCurrentStoryChoicesAsync(...)`：查看 Choice 选项弹窗不再手写 `ContentDialog`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- `ShowCurrentStoryChoicesAsync(...)` 是只读关闭弹窗，目前通过空 Primary 文案 + Close 按钮实现；构建通过，但后续实际运行如果显示异常，可以给 `ContentDialogRequest` 增加 `DefaultButton`/隐藏 primary 的更显式策略。
- 复杂编辑弹窗内部的按钮，例如 Choice 备注里的“+”“删除”，仍是弹窗内容自己的控件；它们还没有接入统一音效。
- 本批没有主动启动程序做运行检查。

下一步建议：
- 迁移章节编辑、函数编辑、角色编辑这类复杂表单弹窗到 `ShowContentAsync(...)`。
- 或先给 `ShowContentAsync(...)` 增强内容内按钮音效辅助方法，用于 Choice 备注里的“+”“删除”等内部按钮。

### 2026-05-20：Dialog 复用第五口第二步 - 复杂编辑弹窗收口

改动：
- Choice 备注弹窗内部按钮补音效：
  - “+” 添加选项备注行播放 IN。
  - “删除”移除选项备注行播放 OUT。
- 函数编辑弹窗内部按钮补音效：
  - “+” 添加备注行播放 IN。
  - “删除”移除备注行播放 OUT。
- `ShowChapterEditorDialogAsync(...)` 改为通过 `_dialogService.ShowContentAsync(...)` 显示章节编辑表单。
- `ShowFunctionEditorDialogAsync(...)` 改为通过 `_dialogService.ShowContentAsync(...)` 显示函数编辑表单。
- `ShowCharacterEditorDialogAsync(...)` 改为通过 `_dialogService.ShowContentAsync(...)` 显示角色编辑表单。
- 以上弹窗现在统一获得确认/取消音效、Esc/右键取消、XamlRoot 绑定。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批只迁移弹窗显示入口，不重构表单字段构造逻辑；这些 UI 构造仍在 `MainWindow.xaml.cs`。
- 复杂表单里 `ComboBox` 切换等并列选择控件暂未统一接 ListSel 音效。
- 本批没有主动启动程序做运行检查。

下一步建议：
- 继续迁剩余手写 `ContentDialog`，优先处理项目关联素材库、角色滤镜删除/重排、背景/音频/角色图层重命名删除等小弹窗。
- 或开始把表单构造 helper 拆出到服务/组件层，减少 `MainWindow.xaml.cs` 的体积。

### 2026-05-20：Dialog 复用第五口第三步 - 项目素材库关联与滤镜删除

改动：
- `ChangeProjectAssetLibraryAsync(...)` 不再手写 `ComboBox + ContentDialog`。
- 项目更改目标素材库改为 `_dialogService.SelectAsync(...)`，并保留当前素材库默认选中。
- `DeleteCharacterFilterAsync(...)` 不再手写删除确认 `ContentDialog`。
- 角色滤镜删除改为 `_dialogService.ConfirmAsync(...)`，统一确认/取消音效、Esc/右键取消。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 旧的删除确认弹窗使用了 destructive primary 样式；迁移到通用确认后，目前样式不再单独染色。后续若需要保留危险按钮样式，可以给 `DialogRequest` 增加 `IsDestructive`。
- 本批没有主动启动程序做运行检查。

下一步建议：
- 继续迁背景/音频/角色图层的备注和删除类弹窗；这些数量多、结构重复，适合先抽小 helper 再批量迁。
- 或增强 `DialogRequest` 支持 destructive 样式，再回补删除类确认弹窗视觉。

### 2026-05-20：Dialog 复用第五口第四步 - 危险确认样式与背景/音频弹窗

改动：
- `DialogRequest` 新增 `PrimaryButtonStyle`。
- `ContentDialogRequest` 新增 `PrimaryButtonStyle`。
- `WinUiDialogService` 会把请求里的 primary 按钮样式应用到实际 `ContentDialog`。
- `DeleteBackgroundImageAsync(...)` 改为 `_dialogService.ConfirmAsync(...)`，并保留 destructive primary 样式。
- `SetAudioRemarkAsync(...)` 改为 `_dialogService.PromptTextAsync(...)`。
- `DeleteAudioAsync(...)` 改为 `_dialogService.ConfirmAsync(...)`，并保留 destructive primary 样式。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 背景图备注此前已经走 `PromptTextAsync(...)`，本批主要迁背景图删除和音频备注/删除。
- 角色服装/表情/装饰的备注/删除弹窗仍未迁移。
- 本批没有主动启动程序做运行检查。

下一步建议：
- 继续迁角色图层的备注/删除弹窗：服装、表情、装饰三组结构重复，适合集中处理。
- 或先抽 `ShowRemarkInputAsync(...)` / `ConfirmDeleteAsync(...)` 这两个 MainWindow 过渡 helper，再批量替换角色图层弹窗。

### 2026-05-20：Dialog 复用第五口第五步 - 角色图层备注与删除弹窗

改动：
- `MainWindow` 新增过渡 helper：
  - `PromptRemarkAsync(...)`：统一备注输入。
  - `ConfirmDeleteAsync(...)`：统一危险删除确认，并复用 destructive primary 样式。
- 服装备注 `SetCharacterClothRemarkAsync(...)` 改为 `PromptRemarkAsync(...)`。
- 服装删除 `DeleteCharacterClothAsync(...)` 改为 `ConfirmDeleteAsync(...)`。
- 表情备注 `SetCharacterFaceRemarkAsync(...)` 改为 `PromptRemarkAsync(...)`。
- 表情删除 `DeleteCharacterFaceAsync(...)` 改为 `ConfirmDeleteAsync(...)`。
- 装饰备注 `SetCharacterAdornRemarkAsync(...)` 改为 `PromptRemarkAsync(...)`。
- 装饰删除 `DeleteCharacterAdornAsync(...)` 改为 `ConfirmDeleteAsync(...)`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 角色图层“可用范围”弹窗仍是手写 `ContentDialog`，因为它包含勾选卡片和滚动区域，适合下一步迁到 `ShowContentAsync(...)`。
- 本批没有主动启动程序做运行检查。

下一步建议：
- 迁移角色图层可用范围弹窗 `SetCharacterLayerAvailabilityAsync(...)` 到 `ShowContentAsync(...)`。
- 或继续清理剩余手写弹窗：CSV 导入、修复结果、Unreal 同步确认/完成等。

### 2026-05-20：Dialog 复用第五口第六步 - 角色图层可用范围弹窗

改动：
- `SetCharacterLayerAvailabilityAsync(...)` 不再手写 `ContentDialog`。
- 角色图层可用范围弹窗改为 `_dialogService.ShowContentAsync(...)`。
- 原有横向滚动卡片、服装缩略图、勾选逻辑保持不变。
- 勾选/取消勾选服装范围时播放 ListSel。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 这一步只迁移弹窗外壳，内容布局仍保留在 `MainWindow.xaml.cs`。
- 本批没有主动启动程序做运行检查。

下一步建议：
- 继续迁 CSV 导入相关手写弹窗，包括小节 CSV 导入和兼容性失败提示。

### 2026-05-20：Dialog 复用第五口第七步 - CSV 导入弹窗

改动：
- `ContentDialogRequest` 新增 `ConfigureDialog`，允许调用方在 dialog 显示前拿到 `ContentDialog` 实例。
- `WinUiDialogService.ShowContentAsync(...)` 会调用 `ConfigureDialog`。
- `ShowStorySectionImportDialogAsync(...)` 不再手写 `ContentDialog`，改为 `_dialogService.ShowContentAsync(...)`。
- 小节 CSV 导入弹窗保留原有点击选择、拖拽导入、选择成功后主动关闭弹窗逻辑。
- `ShowCsvCompatibilityFailedDialogAsync(...)` 不再手写 `ContentDialog`，改为 `_dialogService.ShowContentAsync(...)`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 当前只迁外壳；小节 CSV 导入弹窗内容仍在 `MainWindow.xaml.cs` 构造。
- 本批没有主动启动程序做运行检查。

下一步建议：
- 迁移修复/同步结果类弹窗：资产索引同步结果、章节修复结果等只读结果弹窗。

### 2026-05-20：Dialog 复用第五口第八步 - 修复/同步结果弹窗

改动：
- `ShowAssetIndexSyncResultDialogAsync(...)` 不再手写 `ContentDialog`，改为 `_dialogService.ShowContentAsync(...)`。
- `ShowChapterRepairResultDialogAsync(...)` 不再手写 `ContentDialog`，改为 `_dialogService.ShowContentAsync(...)`。
- 章节修复结果弹窗保留原有语义：
  - 有可自动修复项时，Primary 为“自动修复”。
  - Secondary 为“只查看”。
  - Close 为“取消”。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。

下一步建议：
- 迁移 Unreal 同步确认/完成弹窗：运行中的 Unreal Editor 确认、备份确认、同步完成操作选择。

### 2026-05-20：Dialog 复用第五口第九步 - Unreal 同步弹窗

改动：
- `ShowUnrealBackupDialogAsync(...)` 不再手写 `ContentDialog`，改为 `_dialogService.ShowAsync(...)`。
- `ShowRunningUnrealEditorDialogAsync(...)` 不再手写 `ContentDialog`，改为 `_dialogService.ShowAsync(...)`。
- `ShowUnrealSyncFinishedDialogAsync(...)` 不再手写 `ContentDialog`，改为 `_dialogService.ShowAsync(...)`。
- 保留原有语义：
  - Primary：备份并同步 / 关闭后同步 / 打开虚幻项目。
  - Secondary：直接同步 / 继续同步 / 打开日志目录。
  - Close：取消 / 知道了。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。

下一步建议：
- 迁移剩余简单手写弹窗：全局进度取消确认、日志/路径帮助提示等。

### 2026-05-20：Dialog 复用第五口第十步 - 简单提示与取消确认弹窗

改动：
- 全局底部进度圆环点击后的“取消当前操作？”确认，不再手写 `ContentDialog`，改为 `_dialogService.ConfirmAsync(...)`。
- 全局进度取消确认保留危险主按钮样式，确认后继续触发 `_globalProgressCancellation.Cancel()`。
- “整体项目位置说明”帮助弹窗改为 `_dialogService.ShowContentAsync(...)`。
- “辅助显示说明”帮助弹窗改为 `_dialogService.ShowContentAsync(...)`。
- `MainWindow.xaml.cs` 中已不再出现手写 `new ContentDialog`。

验证：
- Release 构建通过：0 警告，0 错误。
- 复查 `MainWindow.xaml.cs`：没有剩余 `new ContentDialog`。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 目前只是统一弹窗外壳；帮助内容仍由 `MainWindow.xaml.cs` 中的创建函数生成。

下一步建议：
- 继续做 Dialog 迁移收尾：把较复杂的弹窗内容构建 helper 按功能区分组清理，减少 `MainWindow.xaml.cs` 里的 UI 拼装代码。

### 2026-05-20：Dialog 收尾第一步 - 帮助说明内容工厂

改动：
- 新增 `Views/DialogContentFactory.cs`。
- “整体项目位置说明”的内容构建从 `MainWindow.xaml.cs` 移到 `DialogContentFactory.CreateProjectRootHelpContent()`。
- “辅助显示说明”的内容构建从 `MainWindow.xaml.cs` 移到 `DialogContentFactory.CreateLogHelpContent()`。
- `MainWindow.xaml.cs` 只保留按钮事件和 `_dialogService.ShowContentAsync(...)` 调用，不再自己拼帮助弹窗内容。

验证：
- Release 构建通过：0 警告，0 错误。
- 复查引用：帮助说明相关 `CreateHelp...` helper 已只存在于 `Views/DialogContentFactory.cs`。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 当前 `DialogContentFactory` 先承接帮助弹窗内容，后续可以继续放入结果类弹窗、选择类弹窗的内容构建。

下一步建议：
- 继续拆结果类弹窗内容：把资产索引同步结果和章节修复结果的 UI 构建移入 `DialogContentFactory`。

### 2026-05-20：Dialog 收尾第二步 - 结果类弹窗内容工厂

改动：
- `DialogContentFactory` 新增 `CreateAssetIndexSyncResultContent(...)`。
- `DialogContentFactory` 新增 `CreateChapterRepairResultContent(...)`。
- `CreateScrollableTextBlock(...)` 从 `MainWindow.xaml.cs` 移到 `DialogContentFactory`，作为结果类弹窗共用的滚动文本块 helper。
- `ShowAssetIndexSyncResultDialogAsync(...)` 现在只负责无变更时写日志、设置标题和按钮。
- `ShowChapterRepairResultDialogAsync(...)` 现在只负责按钮配置和返回是否自动修复。

验证：
- Release 构建通过：0 警告，0 错误。
- 复查引用：结果内容构建 helper 已集中在 `Views/DialogContentFactory.cs`。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 结果文本仍按原来的数量截断：资产索引变更/异常最多展示 80 条，章节修复问题最多展示 100 条。

下一步建议：
- 继续拆编辑类弹窗内容：把章节编辑、函数编辑、角色编辑的 UI 拼装移入 `DialogContentFactory` 或专门的编辑弹窗工厂。

### 2026-05-20：Dialog 收尾第三步 - 章节编辑内容对象

改动：
- `DialogContentFactory` 新增 `CreateChapterEditorContent(...)`。
- 新增 `ChapterEditorDialogContent`，负责创建章节名称输入框、章节类型下拉框、自定义代号输入框和实时生成代码预览。
- `ShowChapterEditorDialogAsync(...)` 不再直接拼章节编辑 UI，只负责打开弹窗、读取输入、做空值和章节代号校验。
- 章节代码生成仍由 `MainWindow` 的 `BuildChapterCodeSegment(...)` 提供，避免把章节编号扫描逻辑一起搬进视图工厂。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 章节编辑的实时预览仍依赖传入的委托；后续若抽 ViewModel，可以把这部分改成命令/属性绑定。

下一步建议：
- 继续拆函数编辑弹窗内容：把函数名称、指令、分类、选项备注行管理移入编辑内容对象。

### 2026-05-20：Dialog 收尾第四步 - 函数编辑内容对象

改动：
- `DialogContentFactory` 新增 `CreateFunctionEditorContent(...)`。
- 新增 `FunctionEditorDialogContent`，负责函数名称、函数指示器、分类、选项备注行的创建和读取。
- 选项备注的添加/删除按钮仍保留统一按钮触发音效：添加走 `PlayPositiveSound()`，删除走 `PlayNegativeSound()`。
- `ShowFunctionEditorDialogAsync(...)` 不再直接拼函数编辑 UI，只负责打开弹窗、读取输入、做空值校验。
- 删除了 `MainWindow.xaml.cs` 中只服务于函数编辑弹窗的 `RenumberFunctionChoiceNoteRows(...)`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- `FunctionEditorDialogContent` 目前通过委托播放按钮音效，后续可以改为注入 `IUiSoundService` 或统一交互行为。

下一步建议：
- 继续拆角色编辑弹窗内容：把角色名称、代码、颜色输入移入编辑内容对象。

### 2026-05-20：Dialog 收尾第五步 - 角色编辑内容对象

改动：
- `DialogContentFactory` 新增 `CreateCharacterEditorContent(...)`。
- 新增 `CharacterEditorDialogContent`，负责角色名字、英文代号、代表色输入框的创建和读取。
- 默认角色代表色改为读取 `ColorUtility.DefaultCharacterColorHex`，避免在弹窗内容里硬编码 `#008F8D`。
- `ShowCharacterEditorDialogAsync(...)` 不再直接拼角色编辑 UI，只负责打开弹窗、读取输入、做空值校验。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 颜色值仍沿用 `ColorUtility.NormalizeColorHex(...)` 的容错逻辑；非法颜色会归一到默认绿色。

下一步建议：
- 清理弹窗内容工厂结构：把章节/函数/角色编辑内容对象从 `DialogContentFactory.cs` 拆到专门的编辑弹窗内容文件。

### 2026-05-20：Dialog 收尾第六步 - 编辑弹窗内容工厂拆分

改动：
- 新增 `Views/EditorDialogContentFactory.cs`。
- 将 `CreateChapterEditorContent(...)`、`CreateFunctionEditorContent(...)`、`CreateCharacterEditorContent(...)` 从 `DialogContentFactory` 移到 `EditorDialogContentFactory`。
- 将 `ChapterEditorDialogContent`、`FunctionEditorDialogContent`、`CharacterEditorDialogContent` 移到 `EditorDialogContentFactory.cs`。
- `MainWindow.xaml.cs` 中章节/函数/角色编辑弹窗调用改为 `EditorDialogContentFactory`。
- `DialogContentFactory.cs` 回到只负责帮助说明和结果类弹窗内容。

验证：
- Release 构建通过：0 警告，0 错误。
- 复查引用：编辑类内容对象已只存在于 `Views/EditorDialogContentFactory.cs`。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 当前 `EditorDialogContentFactory` 仍直接构建 WinUI 控件；后续 ViewModel 化时，可以从这些内容对象继续拆状态和命令。

下一步建议：
- 继续清理 Story 相关弹窗内容：把故事资产选择、选项备注/当前选项查看这类内容拼装从 `MainWindow.xaml.cs` 移出。

### 2026-05-20：Dialog 收尾第七步 - Story 选项备注弹窗内容工厂

改动：
- 新增 `Views/StoryDialogContentFactory.cs`。
- 新增 `ChoiceFunctionNoteDialogContent`，负责“添加触发选项”弹窗中的选项备注行创建、删除、重编号和读取。
- `ShowChoiceFunctionNoteDialogAsync(...)` 不再直接拼选项备注 UI，只负责打开弹窗并读取备注。
- `StoryDialogContentFactory.CreateCurrentStoryChoicesContent(...)` 负责创建“查看选项”只读内容。
- `ShowCurrentStoryChoicesAsync(...)` 不再直接拼当前选项查看 UI。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 选项备注添加/删除仍通过委托播放统一按钮音效。

下一步建议：
- 继续清理 Story 资产选择弹窗：把带预览 Tooltip 的选择内容和简单选择内容集中到 Story 弹窗工厂。

### 2026-05-20：Dialog 收尾第八步 - Story 资产选择请求工厂

改动：
- `StoryDialogContentFactory` 新增 `CreateStorySimpleChoiceRequest(...)`。
- `StoryDialogContentFactory` 新增 `CreateStoryPreviewChoiceRequestAsync(...)`。
- Story 简单选择和带预览 Tooltip 的选择请求统一在 Story 弹窗工厂中构造。
- `CreateStoryChoicePreviewToolTipAsync(...)` 从 `MainWindow.xaml.cs` 移到 `StoryDialogContentFactory`。
- `MainWindow.xaml.cs` 的 `ShowStorySimpleChoiceDialogAsync(...)` 和 `ShowStoryChoiceDialogAsync(...)` 只负责调用工厂、交给 `_dialogService.SelectAsync(...)` 展示，并返回选中值。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- Tooltip 图片加载现在由 `StoryDialogContentFactory` 内部的 `LoadPreviewImageAsync(...)` 承担，加载失败时该预览图片会留空。

下一步建议：
- 继续清理 Story 相关内容：把小节 CSV 导入弹窗和剧情快捷键说明弹窗内容移入 Story 弹窗工厂。

### 2026-05-20：Dialog 收尾第九步 - Story 小节 CSV 导入弹窗内容

改动：
- `StoryDialogContentFactory` 新增 `CreateStorySectionImportContent(...)`。
- 新增 `StorySectionImportDialogContent`，负责小节 CSV 导入弹窗的点击区域、拖拽区域、拖拽提示和 CSV 路径筛选。
- `ShowStorySectionImportDialogAsync(...)` 不再直接拼 drop zone UI，只保留 FileOpenPicker、选中文件列表和关闭当前弹窗的流程。
- 剧情快捷键说明已在早前迁移到 `ShortcutService.ShowShortcutHelpAsync()`，本批没有重复移动。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 小节导入弹窗仍由 `MainWindow` 持有 `activeDialog`，因为选择文件/拖入文件成功后需要主动关闭当前弹窗。

下一步建议：
- 继续清理 Story CSV 兼容性提示内容：把 `CreateStoryCsvCompatibilityPanel(...)` 移入 Story 弹窗工厂。

### 2026-05-20：Dialog 收尾第十步 - Story CSV 兼容性内容

改动：
- `StoryDialogContentFactory` 新增 `CreateStoryCsvCompatibilityContent(...)`。
- `CreateStoryCsvCompatibilityPanel(...)` 从 `MainWindow.xaml.cs` 移除。
- 导入章节 CSV 弹窗的兼容性说明，以及 CSV 结构不兼容提示，都改为使用 `StoryDialogContentFactory.CreateStoryCsvCompatibilityContent(...)`。
- `MainWindow.xaml.cs` 只保留 CSV 兼容性检查、弹窗显示时机和导入业务流程。

验证：
- Release 构建通过：0 警告，0 错误。
- 复查引用：`CreateStoryCsvCompatibilityPanel(...)` 已无残留。

风险/注意：
- 本批没有主动启动程序做运行检查。
- CSV 兼容性判断逻辑没有移动，仍在 `MainWindow.xaml.cs`。

下一步建议：
- 清点剩余 `ShowContentAsync(...)` 的内容拼装，优先拆非 Story 区域中仍留在 `MainWindow.xaml.cs` 的大块 UI helper。

### 2026-05-20：Dialog 收尾第十一步 - 角色图层可用范围内容

改动：
- 新增 `Views/CharacterDialogContentFactory.cs`。
- 新增 `CharacterLayerAvailabilityDialogContent`，负责表情/装饰“可用范围”弹窗里的横向服装卡片、复选框、横向滚轮滚动和勾选读取。
- `SetCharacterLayerAvailabilityAsync(...)` 不再直接拼可用范围弹窗 UI，只保留读取服装列表、读取/写入 scope meta、刷新预览和日志。
- 勾选/取消勾选仍通过传入的 `PlaySelectionSound()` 播放统一选择音效。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 工厂通过 `createThumbnail` 委托复用现有缩略图创建逻辑，避免本批顺手迁移卡片缩略图系统。

下一步建议：
- 继续清点剩余 `ShowContentAsync(...)` 内容，优先拆角色/素材区域里仍留在 `MainWindow.xaml.cs` 的较大 UI helper。

### 2026-05-20：UI 收尾第一步 - 基础卡片内容工厂

改动：
- 新增 `Views/CardContentFactory.cs`。
- `CreateCardContent(...)` 的标题、副标题、底部文字布局移到 `CardContentFactory.CreateCardContent(...)`。
- `CreateAddCardContent(...)` 的加号卡片布局移到 `CardContentFactory.CreateAddCardContent(...)`。
- `MainWindow.xaml.cs` 暂时保留 `CreateThumbnail(...)` 和缩略图加载逻辑，并通过委托传给卡片工厂，避免本批同时迁移图片加载系统。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 当前 `MainWindow.xaml.cs` 仍保留同名过渡 helper，调用点不用大面积改动；后续可继续把缩略图 helper 一起迁出去。

下一步建议：
- 继续拆卡片相关 UI：把 `CreateThumbnail(...)` 和缩略图加载逻辑迁移到卡片/图片工厂，进一步减少 `MainWindow.xaml.cs` 的通用 UI 代码。

### 2026-05-20：UI 收尾第二步 - 缩略图工厂

改动：
- 新增 `Views/ThumbnailFactory.cs`。
- 默认缩略图 URI 移到 `ThumbnailFactory.DefaultThumbnailUri`。
- 通用缩略图控件创建逻辑移到 `ThumbnailFactory.CreateThumbnail(...)`。
- 文件缩略图加载逻辑移到 `ThumbnailFactory.LoadThumbnailFromFileAsync(...)`。
- `MainWindow.xaml.cs` 的 `CreateThumbnail(...)` 和 `LoadThumbnailFromFileAsync(...)` 暂时保留为薄封装，保证现有调用点不大面积改动。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 故事预览图片缓存逻辑仍在 `MainWindow.xaml.cs`，本批只迁通用缩略图，不碰缓存。

下一步建议：
- 继续清理缩略图调用点：让卡片和角色/素材卡直接调用 `ThumbnailFactory`，逐步删除 `MainWindow.xaml.cs` 里的过渡 helper。

### 2026-05-20：UI 收尾第三步 - 缩略图调用点直连

改动：
- `MainWindow.xaml.cs` 中原 `CreateThumbnail(...)` 过渡 helper 已删除。
- `MainWindow.xaml.cs` 中原 `LoadThumbnailFromFileAsync(...)` 过渡 helper 已删除。
- 卡片、背景图、角色图层、查看器和角色预览等调用点改为直接使用 `ThumbnailFactory.CreateThumbnail(...)` 或 `ThumbnailFactory.LoadThumbnailFromFileAsync(...)`。
- `DefaultThumbnailUri` 常量从 `MainWindow.xaml.cs` 删除，默认缩略图完全归 `ThumbnailFactory` 管理。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 故事预览图缓存仍在 `MainWindow.xaml.cs`，但默认图 fallback 改为 `ThumbnailFactory.CreateDefaultBitmap()`。

下一步建议：
- 继续拆素材/角色卡片 UI：优先把背景图卡片和角色图层图片卡片的重复布局移入专门工厂。

### 2026-05-20：UI 收尾第四步 - 图片素材卡片内容工厂

改动：
- 新增 `Views/AssetCardContentFactory.cs`。
- 新增 `CreateImageAssetCardContent(...)`，负责“缩略图 + 文件名”的图片素材卡片内容。
- 背景图卡片内容改为使用 `AssetCardContentFactory.CreateImageAssetCardContent(imagePath, 148, 148)`。
- 角色服装/表情/装饰卡片内容改为使用 `AssetCardContentFactory.CreateImageAssetCardContent(imagePath, 178, 152, tagWithPath: true)`。
- 点击、右键菜单、删除/备注/可用范围等业务逻辑仍留在 `MainWindow.xaml.cs`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 这里只抽重复内容布局，没有改素材排序、导入、删除或备注逻辑。

下一步建议：
- 继续拆非图片素材卡片内容：优先把音频卡片和角色 VFX 卡片的重复布局移入 `AssetCardContentFactory`。

### 2026-05-20：UI 收尾第五步 - 图标素材卡片内容工厂

改动：
- `AssetCardContentFactory` 新增 `CreateIconAssetCardContent(...)`。
- 音频卡片内容改为使用 `AssetCardContentFactory.CreateIconAssetCardContent(Symbol.Audio, ...)`。
- 角色 VFX 卡片内容改为使用 `AssetCardContentFactory.CreateIconAssetCardContent(Symbol.Filter, ...)`。
- 删除 `MainWindow.xaml.cs` 中只服务音频卡片的 `CreateMusicCardTitle(...)`。
- 音频播放、备注、删除、VFX 选择等业务逻辑仍留在 `MainWindow.xaml.cs`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 图标卡片只抽视觉内容，没有改变任何点击或右键菜单行为。

下一步建议：
- 继续拆文字类素材卡片内容：优先把函数卡片和角色滤镜卡片的重复布局移入 `AssetCardContentFactory`。

### 2026-05-20：UI 收尾第六步 - 文字素材卡片内容工厂

改动：
- `AssetCardContentFactory` 新增 `CreateFunctionCardContent(...)`。
- `AssetCardContentFactory` 新增 `CreateAddTextCardContent(...)`。
- `AssetCardContentFactory` 新增 `CreateCharacterFilterCardContent(...)`。
- `AssetCardContentFactory` 新增 `CreateAddCharacterFilterCardContent(...)`。
- 函数卡片、加号函数卡片、角色滤镜卡片、加号滤镜卡片的内容布局从 `MainWindow.xaml.cs` 移出。
- 删除 `MainWindow.xaml.cs` 中只服务角色滤镜卡片的 `CreateCharacterFilterCardTitle(...)`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 函数编辑、删除、角色滤镜删除、排序等行为逻辑没有改变。

下一步建议：
- 继续拆剩余素材/角色卡片内容：优先清理角色卡片和新增角色卡片的 UI 布局。

### 2026-05-20：UI 收尾第七步 - 角色卡片内容工厂

改动：
- `AssetCardContentFactory` 新增 `CreateCharacterCardContent(...)`，负责角色色块、角色代号和角色名的视觉布局。
- `AssetCardContentFactory` 新增 `CreateAddCharacterCardContent()`，负责“新建立绘”卡片的视觉布局。
- `AssetCardContentFactory` 新增内部复用的 `CreateCharacterColorBlock(...)`，统一角色色块和加号色块的尺寸、圆角、描边和文字居中规则。
- `MainWindow.xaml.cs` 中 `CreateCharacterCard(...)` 和 `CreateAddCharacterCard()` 改为只保留宽高、Tag、右键菜单、点击进入/创建等行为逻辑。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 角色重命名、创建角色、进入角色详情页、角色颜色解析结果没有改变；只是把卡片内容布局移入工厂。

下一步建议：
- 清理素材区剩余卡片容器 helper：优先抽取可复用的 `GridViewItem` 创建/右键菜单小工具，减少 `MainWindow.xaml.cs` 里重复的宽高、Margin、Tag 和 ContextFlyout 代码。

### 2026-05-20：UI 收尾第八步 - 卡片容器与菜单小工具

改动：
- 新增 `Views/GridViewItemFactory.cs`。
- `GridViewItemFactory.CreateCard(...)` 统一卡片 `GridViewItem` 的 Width、Height、Margin、Tag 和 Stretch 对齐配置。
- `GridViewItemFactory.CreateMenu(...)` 与 `CreateMenuItem(...)` 统一右键菜单和菜单项创建方式。
- 背景图、角色、函数、角色滤镜、音频、角色详情图层、角色 VFX 卡片改为复用 `GridViewItemFactory`。
- 卡片点击、排序、删除、备注、进入详情、预览刷新等业务行为没有迁出，只是改为通过统一 helper 挂接。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 项目卡、素材库卡、章节卡和 Story 角色槽的菜单还保留原写法；这些区域的菜单项更多，适合后续单独处理。

下一步建议：
- 继续收拢剩余菜单逻辑：优先处理 Story 角色槽和角色预览右键菜单，把重复的并列选项菜单接入 `GridViewItemFactory` 或拆到专门的菜单工厂。

### 2026-05-21：UI 收尾第九步 - Story 与角色预览菜单统一

改动：
- `CreateStoryCharacterSlotMenu(...)` 改为使用 `GridViewItemFactory.CreateMenu(...)` 和 `CreateMenuItem(...)`。
- `CreateStorySpeakerSlotMenu()` 改为使用统一菜单 helper。
- `CharacterPreviewSurface_RightTapped(...)` 的服装、表情、装饰菜单改为使用统一菜单 helper。
- Story 角色槽、说话人槽、角色详情预览的右键菜单仍留在 `MainWindow.xaml.cs`，但菜单项创建方式已经和素材卡片一致。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 角色选择、图层选择、滤镜选择和预览刷新逻辑没有改变。

下一步建议：
- 继续清理大菜单区域：优先处理项目卡、素材库卡和章节卡右键菜单，把它们接入统一菜单 helper，之后再考虑是否需要单独拆 `CardMenuFactory`。

### 2026-05-21：UI 收尾第十步 - 项目/素材库/章节菜单统一

改动：
- `CreateProjectCard(...)` 的重命名、更改目标素材库、打开文件夹、导出、备份、还原、删除菜单改为使用 `GridViewItemFactory.CreateMenu(...)`。
- `CreateAssetLibraryCard(...)` 的重命名、打开文件夹、导出、备份、还原、删除菜单改为使用统一菜单 helper。
- `CreateChapterCard(...)` 的修改、导入小节、备份、还原、修复、删除菜单改为使用统一菜单 helper。
- `MainWindow.xaml.cs` 中直接手写 `new MenuFlyout` / `new MenuFlyoutItem` 的地方已清空，菜单创建入口集中到 `GridViewItemFactory`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 项目、素材库、章节的菜单顺序和点击行为没有改变。
- 目前菜单 helper 名字仍放在 `GridViewItemFactory` 里；如果后续菜单逻辑继续变多，可以再拆出 `MenuFlyoutFactory`。

下一步建议：
- 清理基础卡片过渡 helper：优先处理 `CreateBaseCard(...)`、`CreateCardContent(...)`、`CreateAddCard(...)` 这些还留在 `MainWindow.xaml.cs` 的通用卡片入口，让项目/素材库/章节卡也更多复用已有工厂。

### 2026-05-21：UI 收尾第十一步 - 基础卡片入口直连工厂

改动：
- `GridViewItemFactory` 新增 `CreateDashboardCard(...)`，统一工作台/章节/虚幻同步项目卡使用的 260x318 大卡片尺寸、Margin 和 Tag。
- `CardContentFactory` 新增 `CreateDashboardCardContent(...)`，内置 236x178 缩略图规格，项目/素材库/章节卡不再需要从 `MainWindow.xaml.cs` 传缩略图委托。
- 项目卡、素材库卡、章节卡、虚幻同步项目卡改为直接使用 `GridViewItemFactory.CreateDashboardCard(...)` 和 `CardContentFactory.CreateDashboardCardContent(...)`。
- 删除 `MainWindow.xaml.cs` 中的 `CreateBaseCard(...)`、`CreateCardContent(...)`、`CreateAddCardContent(...)` 过渡 helper。
- `CreateAddCard(...)` 保留在 `MainWindow.xaml.cs`，因为它仍负责绑定不同页面的 Tapped 行为，但内部已经复用工厂。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 大卡片尺寸、缩略图尺寸、标题/副标题/页脚显示逻辑没有改变。

下一步建议：
- 继续清理工作台/章节卡片创建流程：优先把 `CreateAddCard(...)` 和项目/素材库/章节卡片内容组装再薄一层，观察是否值得拆出 `DashboardCardFactory` 来承接这些高层卡片创建。

### 2026-05-21：UI 收尾第十二步 - DashboardCardFactory

改动：
- 新增 `Views/DashboardCardFactory.cs`，承接工作台、章节页、虚幻同步页这类大卡片的高层组装。
- `DashboardCardFactory.CreateInfoCard(...)` 统一“容器 + 点击事件 + 右键菜单 + 缩略图内容”的组装。
- `DashboardCardFactory.CreateAddCard(...)` 统一“新增项目 / 新增素材库 / 新建章节”加号卡。
- `DashboardCardFactory.MarkSelected(...)` 统一虚幻同步项目卡选中描边。
- `MainWindow.xaml.cs` 中项目卡、素材库卡、章节卡、虚幻同步项目卡都改为调用 `DashboardCardFactory`，只保留菜单项和业务回调的传入。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 项目、素材库、章节和虚幻同步项目卡的视觉、菜单顺序、点击行为没有改变。

下一步建议：
- 继续收拢素材区卡片高层创建函数：优先观察背景图/音频/角色/函数/滤镜这些 `Create*Card(...)` 是否可以复用同一套“容器 + 内容 + 菜单 + 点击”的轻量工厂入口。

### 2026-05-21：UI 收尾第十三步 - AssetCardFactory

改动：
- 新增 `Views/AssetCardFactory.cs`，承接素材区小卡片的“容器 + 内容 + 菜单 + 点击 + Tooltip”外壳组装。
- 背景图、角色、新增角色、函数、新增函数、角色滤镜、新增滤镜、音频卡片改为使用 `AssetCardFactory.CreateCard(...)`。
- 角色详情页的图层图片卡片和 VFX 卡片也改为使用 `AssetCardFactory.CreateCard(...)`。
- 原有的 `AssetCardContentFactory` 继续只负责视觉内容，`AssetCardFactory` 负责把内容装入 `GridViewItem`。
- `MainWindow.xaml.cs` 中素材卡片方法仍保留业务回调、菜单项和条件判断，但不再直接手动设置通用卡片外壳。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 素材卡片尺寸、菜单项、点击行为、Tooltip、Tag 和拖拽排序依赖没有改变。

下一步建议：
- 开始收拢素材区业务流程：优先从背景图或音频的导入/备注/删除/归一化流程里挑一块，拆到 service，继续降低 `MainWindow.xaml.cs` 的业务负担。

### 2026-05-21：Service 拆分第一步 - AudioAssetService

改动：
- 新增 `Services/AudioAssetService.cs`。
- `AudioAssetService` 接管音频扩展名、显示名、文件名前缀、索引解析、文件列表、文件名解析、导入、归一化、备注改名、删除后归一化、重命名和重命名后路径计算。
- `MainWindow.xaml.cs` 的音频导入、备注、删除、拖入导入、归一化流程改为调用 `AudioAssetService`。
- `MainWindow.xaml.cs` 仍保留 UI 状态、确认弹窗、刷新卡片、日志、播放页路径更新和现有兼容包装方法。
- `MusicExtensions` 常量从窗口层移除，文件选择器和拖入筛选改用 `AudioAssetService.Extensions` / `IsValidAudioPath(...)`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 音频文件命名规则仍是 `BGM/Sc/SE + 序号 + 可选备注 + .wav`。
- 这一步只迁纯文件系统逻辑，没有迁 UI 刷新、日志、播放状态和故事/虚幻同步里的音频引用读取。

下一步建议：
- 继续清理音频兼容包装：优先把 `MainWindow.xaml.cs` 中只转调 `AudioAssetService` 的 `GetAudioFilePaths(...)`、`ParseAudioFileName(...)`、`RenameAudioEntriesAsync(...)` 等包装逐步替换到直接调用 service。

### 2026-05-21：Service 拆分第二步 - 音频包装清理

改动：
- 删除 `MainWindow.xaml.cs` 中只转调 `AudioAssetService` 的音频包装方法：
  `GetAudioAssetIndex(...)`、`GetAudioDisplayName(...)`、`GetAudioPrefix(...)`、`GetAudioFilePaths(...)`、`GetMusicFilePaths(...)`、`ParseAudioFileName(...)`、`ParseMusicFileName(...)`、`RenameAudioEntriesAsync(...)`、`RenameMusicEntriesAsync(...)`、`FindRenamedAudioPath(...)`、`FindRenamedMusicPath(...)`。
- Story、播放页、素材库计数、虚幻同步清单/导入分组等调用点改为直接调用 `AudioAssetService`。
- 保留 `NormalizeAudioFilesAsync(...)` / `NormalizeMusicFilesAsync(...)`，因为它们仍负责配合窗口层的归一化状态和拖拽排序流程。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 音频读取、计数、排序同步和虚幻导入使用的路径来源改为直接来自 service，但文件排序规则仍是按文件名排序。

下一步建议：
- 拆背景图文件流程：以 `AudioAssetService` 为参考，新建背景图 service，先迁出背景图文件列表、导入 PNG、备注改名、删除和自动命名逻辑。

### 2026-05-21：Service 拆分第三步 - BackgroundImageService

改动：
- 新增 `Services/BackgroundImageService.cs`。
- `BackgroundImageService` 接管背景图扩展名、可转换格式、索引解析、文件列表、文件名解析、导入、归一化、备注改名、删除后归一化、重命名和重命名后路径计算。
- `MainWindow.xaml.cs` 的背景图导入、拖入导入、刷新列表、排序归一化、备注和删除流程改为调用 `BackgroundImageService`。
- WinUI/WinRT 图片编码仍留在 `MainWindow.xaml.cs` 的 `ConvertImageToPngAsync(...)`，service 通过委托调用，避免把 UI 平台细节塞进文件服务。
- 角色图层导入/筛选仍复用同一套图片扩展名列表，引用改为 `BackgroundImageService.Extensions`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 背景图命名规则仍是 `BG + 序号 + 可选备注 + .png`。
- 这一步保留了 `GetBackgroundImagePaths(...)`、`ParseBackgroundImageFileName(...)`、`RenameBackgroundEntriesAsync(...)` 等窗口层兼容包装，后续再逐步清理。

下一步建议：
- 清理背景图兼容包装：把 `MainWindow.xaml.cs` 中只转调 `BackgroundImageService` 的 `GetBackgroundImagePaths(...)`、`ParseBackgroundImageFileName(...)`、`RenameBackgroundEntriesAsync(...)` 等包装替换为直接调用 service。

### 2026-05-21：Service 拆分第四步 - 背景图包装清理

改动：
- 删除 `MainWindow.xaml.cs` 中只转调 `BackgroundImageService` 的背景图包装方法：
  `GetBackgroundImageIndex(...)`、`GetBackgroundImagePaths(...)`、`ParseBackgroundImageFileName(...)`、`RenameBackgroundEntriesAsync(...)`。
- Story 背景预览、预热缓存、背景查看器相邻切换、素材库计数、虚幻同步清单/导入分组等调用点改为直接调用 `BackgroundImageService`。
- 保留 `NormalizeBackgroundImagesAsync(...)`、`ImportBackgroundImageAsPngAsync(...)` 和 `ConvertImageToPngAsync(...)`，因为它们仍承接窗口层归一化状态、日志和 WinUI/WinRT 图片编码。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 背景图读取、计数、排序同步和虚幻导入使用的路径来源改为直接来自 service，但文件排序规则仍是按文件名排序。

下一步建议：
- 拆角色图层文件流程：优先抽 `CharacterLayerAssetService`，迁出角色服装/表情/装饰/VFX 的文件列表、导入、备注改名、删除和自动命名逻辑。

### 2026-05-21：Service 拆分第五步 - CharacterLayerAssetService 初步抽取

改动：
- 新增 `Services/CharacterLayerAssetService.cs`。
- 角色图层的图片扩展名筛选、文件列表、文件名解析、目标文件名生成、索引/图层类型识别、作用范围解析与匹配、导入临时文件、基础重命名逻辑迁入 service。
- 服装导入改为由 service 完成导入与重命名。
- 表情/装饰导入改为由 service 生成导入 entries，再由 `MainWindow.xaml.cs` 调用原有 meta-aware 重命名，避免可用范围 meta 丢失。
- `MainWindow.xaml.cs` 中相关静态规则方法暂保留为 service 转发，降低本步改动面。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 表情/装饰的可用范围 meta 仍由窗口层维护；本步没有迁移 `Read/Write/RemapCharacterLayerScopeMeta(...)`。
- 拖拽排序、删除、备注修改目前仍大多保留在 `MainWindow.xaml.cs`，只是底层命名规则开始由 service 承接。

下一步建议：
- 继续清理角色图层包装：把 `MainWindow.xaml.cs` 中只转调 `CharacterLayerAssetService` 的静态命名/解析方法调用点替换为直接调用 service，并删除这些薄包装。

### 2026-05-21：Service 拆分第六步 - 角色图层包装清理

改动：
- 删除 `MainWindow.xaml.cs` 中只转调 `CharacterLayerAssetService` 的角色图层包装方法：
  `GetCharacterLayerImagePaths(...)`、`ParseCharacterLayerFileName(...)`、`BuildCharacterLayerFileName(...)`、`GetCharacterLayerTargetPath(...)`、`GetCharacterLayerDefaultScope(...)`、`NormalizeCharacterLayerScope(...)`、`IsCharacterLayerScope(...)`、`IsCharacterScopeMatchingCostume(...)`、`GetCharacterLayerIndex(...)`、`GetCharacterLayerKindFromPath(...)`、`GetCharacterLayerPrefix(...)`、`CharacterLayerUsesScope(...)`。
- 角色详情计数、拖拽排序、备注/范围重命名、角色预览可用范围判断、虚幻同步清单/导入分组等调用点改为直接调用 `CharacterLayerAssetService`。
- 保留 `NormalizeCharacterLayerFiles(...)`、`RenameCharacterFaceEntriesAndUpdateMeta(...)`、`RenameCharacterAdornEntriesAndUpdateMeta(...)` 等窗口层流程，因为它们仍负责排序状态、可用范围 meta 和 UI 刷新。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- `MainWindow.xaml.cs` 仍直接持有角色图层的 meta 读写、删除、备注修改和拖拽归一化流程；本步只清掉薄包装调用。

下一步建议：
- 继续拆角色图层业务流程：优先把服装/表情/装饰的删除、备注修改和排序归一化逐步迁入 `CharacterLayerAssetService`，窗口层只保留确认弹窗、日志和刷新 UI。

### 2026-05-21：Service 拆分第七步 - 角色图层文件操作迁移

改动：
- `CharacterLayerAssetService` 新增 `CreateRemarkEntries(...)`、`DeleteFileAndCreateRemainingEntries(...)`、`FindEntryIndex(...)`，承接备注改名、删除后重新收集 entries 和目标条目定位。
- 服装备注修改、服装删除、服装拖拽排序改为直接调用 `CharacterLayerAssetService.RenameEntries(...)`，窗口层不再自己实现临时文件重命名。
- 表情/装饰备注修改和删除改为由 service 生成或删除 entries，再由窗口层的 meta-aware 方法完成重命名和可用范围 meta 映射。
- 删除 `MainWindow.xaml.cs` 中原有 `RenameCharacterLayerEntries(...)` 文件重命名实现。
- `RenameCharacterFaceEntriesAndUpdateMeta(...)`、`RenameCharacterAdornEntriesAndUpdateMeta(...)` 的 renameMap 生成改为调用 `CharacterLayerAssetService.BuildRenameMap(...)`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 表情/装饰可用范围 meta 的读写、删除条目和 remap 仍保留在 `MainWindow.xaml.cs`。
- `NormalizeCharacterLayerFiles(...)` 仍保留在窗口层，作为排序状态、meta-aware 分发和 service 文件操作之间的过渡入口。

下一步建议：
- 继续拆角色图层 meta 流程：把表情/装饰的可用范围 meta 读写、删除条目和重命名 remap 收进专门 service 或 `CharacterLayerAssetService` 的 meta 区域，让窗口层只负责弹窗和刷新。

### 2026-05-21：Service 拆分第八步 - 角色图层可用范围 meta 迁移

改动：
- `CharacterLayerAssetService` 新增可用范围 meta API：`ReadScopeMeta(...)`、`WriteScopeMeta(...)`、`SaveScopeEntry(...)`、`RemoveScopeEntry(...)`、`RemapScopeMeta(...)`。
- 表情/装饰删除时的 meta 条目移除改为调用 service。
- 表情/装饰可用范围弹窗确认后，窗口层只生成 `CharacterLayerScopeEntry`，保存由 service 完成。
- 表情/装饰重命名后的 meta 文件 remap 改为调用 service。
- 删除 `MainWindow.xaml.cs` 中表情/装饰 scope meta 的读写、删除条目和 remap helper。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 可用范围弹窗 UI、服装缩略图生成、服装 hash 计算和预览刷新仍保留在窗口层。
- `CharacterLayerAssetService` 目前同时承担文件命名/重命名和 scope meta 读写，后续如果继续膨胀，再拆 `CharacterLayerScopeMetaService`。

下一步建议：
- 收拢角色图层归一化入口：把 `NormalizeCharacterLayerFiles(...)` 的分发逻辑继续下沉，让窗口层只在不同图层的拖拽/加载流程中设置状态标记和刷新 UI。

### 2026-05-21：Service 拆分第九步 - 角色图层归一化入口收拢

改动：
- `CharacterLayerAssetService.NormalizeFiles(...)` 改为调用新的 `RenameEntriesAndScopeMeta(...)`，让表情/装饰归一化时自动处理 scope meta remap。
- 新增 `CharacterLayerAssetService.RenameEntriesAndScopeMeta(...)`，统一执行 renameMap 生成、文件重命名和表情/装饰 meta remap。
- 删除 `MainWindow.xaml.cs` 中表情/装饰专用的 `RenameCharacterFaceEntriesAndUpdateMeta(...)`、`RenameCharacterAdornEntriesAndUpdateMeta(...)` 过渡方法。
- 表情/装饰导入、拖拽排序、备注修改、删除后的归一化均改为直接调用 `CharacterLayerAssetService.RenameEntriesAndScopeMeta(...)`。
- 窗口层 `NormalizeCharacterLayerFiles(...)` 更名为 `NormalizeCharacterLayerFilesWithUiState(...)`，只保留一层 UI 状态入口，核心分发交给 service。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 角色图层拖拽后的故事索引同步、日志和刷新仍在 `MainWindow.xaml.cs`。
- `NormalizeCharacterLayerFilesWithUiState(...)` 仍是过渡方法，后续可以把调用点直接改为 service，并在调用点附近保留 `_isNormalizingCharacter*` 状态标记。

下一步建议：
- 继续清理角色图层 UI 状态入口：把 `NormalizeCharacterLayerFilesWithUiState(...)` 的调用点改为直接调用 `CharacterLayerAssetService.NormalizeFiles(...)`，然后删除这个过渡方法。

### 2026-05-21：Service 拆分第十步 - 角色图层归一化过渡入口删除

改动：
- `MainWindow.xaml.cs` 中角色重命名后的服装归一化、角色详情加载时的服装/表情/装饰/VFX 归一化，全部改为直接调用 `CharacterLayerAssetService.NormalizeFiles(...)`。
- 删除 `NormalizeCharacterLayerFilesWithUiState(...)` 过渡方法。
- 角色图层归一化的命名、排序、表情/装饰 scope meta remap 入口统一落在 `CharacterLayerAssetService`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- `_isNormalizingCharacterClothes/Faces/Adorns` 状态标记仍保留在导入、拖拽、备注和删除等 UI 流程中，用于避免 GridView 重排事件递归触发。
- 角色详情加载时的归一化现在直接调用 service，不再经过窗口层包装。

下一步建议：
- 清理角色图层导入流程：把 `ImportCharacterClothesAsync(...)`、`ImportCharacterFacesAsync(...)`、`ImportCharacterAdornsAsync(...)` 中重复的状态标记、导入 entries 和刷新前置逻辑收成更小的窗口层 helper，继续减少 `MainWindow.xaml.cs` 的重复代码。

### 2026-05-21：Service 拆分第十一步 - 角色图层导入流程统一

改动：
- 新增窗口层通用 helper `ImportCharacterLayerAsync(...)`，统一服装/表情/装饰的导入 entries、状态标记、归一化重命名、素材库更新时间、详情刷新和延迟刷新流程。
- `ImportCharacterClothesAsync(...)`、`ImportCharacterFacesAsync(...)`、`ImportCharacterAdornsAsync(...)` 改为只传入文件夹名、图层类型、状态标记回调和服装角色 code。
- 服装导入也改为走 `CreateImportEntries(...)` + `RenameEntriesAndScopeMeta(...)`，与表情/装饰保持同一条导入路径。
- 删除 `CharacterLayerAssetService.ImportFiles(...)` 旧入口，避免角色图层导入保留两套实现。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 选择文件按钮和拖入事件仍分别负责弹窗、文件路径收集、日志文本和错误捕获。
- `_isNormalizingCharacterClothes/Faces/Adorns` 仍由窗口层维护，只是设置/恢复动作集中到了通用导入 helper 参数里。

下一步建议：
- 清理角色图层选择文件入口：把服装/表情/装饰三个“打开图片选择器并导入”的按钮流程合并成一个通用 helper，保留各自的日志标签和错误文本。

### 2026-05-21：Service 拆分第十二步 - 角色图层选择导入入口统一

改动：
- 新增窗口层 helper `PickAndImportCharacterLayerAsync(...)`，统一打开图片选择器、添加支持的图片扩展名、绑定窗口句柄、读取多选文件、导入、日志和异常捕获。
- `AddCharacterClothesButton_Click(...)`、`AddCharacterFacesButton_Click(...)`、`AddCharacterAdornsButton_Click(...)` 改为只传各自导入函数、日志标签和错误文案。
- 选择文件导入仍复用前一步的 `ImportCharacterLayerAsync(...)`，不改变导入后的归一化、scope meta remap 和刷新流程。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 拖入导入流程暂未合并，仍保留三段各自的拖入日志和错误捕获。
- 文件选择器仍使用 `BackgroundImageService.Extensions` 作为图片扩展名来源。

下一步建议：
- 清理角色图层拖入导入流程：把服装/表情/装饰三个 Drop 事件里重复的路径提取、导入、日志和错误捕获收成通用 helper。

### 2026-05-21：Service 拆分第十三步 - 角色图层拖入导入流程统一

改动：
- 新增窗口层 helper `CharacterLayerDropZone_Drop(...)`，统一处理角色图层 Drop 事件中的内部拖拽移到末尾、外部文件路径提取、图片扩展名过滤、导入、日志、异常捕获和 deferral 完成。
- `CharacterClothDropZone_Drop(...)`、`CharacterFaceDropZone_Drop(...)`、`CharacterAdornDropZone_Drop(...)` 改为只传当前拖拽项、移到末尾动作、导入函数和日志标签。
- 拖入导入继续复用 `ImportCharacterLayerAsync(...)`，不改变归一化、scope meta remap、刷新和素材库更新时间流程。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- DragEnter/DragOver 目前仍是三组独立方法，后续可以继续统一。
- 拖拽排序完成后的故事索引同步仍保留三段独立流程。

下一步建议：
- 清理角色图层拖拽排序完成流程：把服装/表情/装饰三个 `DragItemsCompleted` 中重复的 orderedPaths、indexRemap、service 重命名、故事索引同步、刷新和日志收成通用 helper。

### 2026-05-21：Service 拆分第十四步 - 角色图层拖拽排序完成流程统一

改动：
- 新增窗口层 helper `CharacterLayerGridView_DragItemsCompleted(...)`，统一处理角色图层拖拽排序完成后的 orderedPaths 收集、索引 remap、旧/新标签映射、service 重命名、故事 CSV 索引同步、素材库更新时间、刷新和日志。
- `CharacterClothGridView_DragItemsCompleted(...)`、`CharacterFaceGridView_DragItemsCompleted(...)`、`CharacterAdornGridView_DragItemsCompleted(...)` 改为只传 GridView、图层类型、状态标记回调、清理拖拽项回调、日志标签和服装角色 code。
- 服装排序也改为通过 `RenameEntriesAndScopeMeta(...)` 统一入口执行，服装仍传入角色 code，表情/装饰仍自动处理 scope meta remap。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- DragEnter/DragOver 和 DragItemsStarting 仍是三组独立入口。
- 故事 CSV 同步流程本身仍保留在 `MainWindow.xaml.cs`，本步只是合并角色图层排序完成后的调用壳。

下一步建议：
- 清理角色图层 DragEnter/DragOver 与移到末尾流程：把三组拖拽悬停判断和 `MoveDraggingCharacter*ToEnd()` 入口收成通用 helper。

### 2026-05-21：Service 拆分第十五步 - 角色图层拖拽悬停入口统一

改动：
- 新增 `CharacterLayerGridView_DragOver(...)`，统一服装/表情/装饰 GridView 拖拽悬停时的 Move 接受、尾部空白区判断和移到末尾行为。
- 新增 `CharacterLayerDropZone_DragEnterOrOver(...)`，统一 DropZone 在内部拖拽和外部文件拖入时的 Move/Copy 接受逻辑。
- `CharacterCloth/Face/AdornGridView_DragOver(...)` 改为调用统一 helper。
- `CharacterCloth/Face/AdornDropZone_DragEnter(...)` 和 `DragOver(...)` 改为调用统一 helper。
- 删除 `MoveDraggingCharacterClothToEnd(...)`、`MoveDraggingCharacterFaceToEnd(...)`、`MoveDraggingCharacterAdornToEnd(...)` 三个薄包装，Drop 事件直接传 `MoveGridViewItemToEnd(...)` 委托。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- DragItemsStarting 仍是三组独立入口，但只剩简单赋值，可以继续收口。
- 背景图和音频拖拽流程没有修改。

下一步建议：
- 清理角色图层 DragItemsStarting：把服装/表情/装饰三个拖拽起始方法接入通用 helper，统一从 GridView 和事件参数解析拖拽项。

### 2026-05-21：Service 拆分第十六步 - 角色图层拖拽起始入口统一

改动：
- 新增 `CharacterLayerGridView_DragItemsStarting(...)`，统一从 GridView 和 `DragItemsStartingEventArgs` 解析当前拖拽项。
- `CharacterClothGridView_DragItemsStarting(...)`、`CharacterFaceGridView_DragItemsStarting(...)`、`CharacterAdornGridView_DragItemsStarting(...)` 改为只传 GridView 和对应拖拽字段 setter。
- 角色图层拖拽的起始、悬停、Drop、排序完成四段流程现在都有统一 helper 承接。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 三个事件绑定方法仍保留在 `MainWindow.xaml.cs`，因为 XAML 事件入口仍需要明确方法名。
- 角色图层拖拽流程已经基本收口，后续更适合转向角色图层预览/查看器或 Story 同步逻辑拆分。

下一步建议：
- 清理角色图层预览/查看器流程：优先观察服装、表情、装饰查看器的打开、上一张/下一张、备注、删除等重复逻辑，抽成通用窗口层 helper。

### 2026-05-21：Service 拆分第十七步 - 角色图层查看器流程统一

改动：
- 新增 `ShowCharacterLayerViewerPage(...)`，统一服装/表情/装饰查看器打开流程，包括查看路径设置、选中路径设置、标题更新、图片加载、角色预览刷新、页面切换和日志。
- `ShowCharacterClothViewerPage(...)`、`ShowCharacterFaceViewerPage(...)`、`ShowCharacterAdornViewerPage(...)` 改为只传图层类型。
- 新增 `ShowAdjacentCharacterLayer(...)`，删除三组上一张/下一张重复方法。
- 新增 `GetViewingCharacterLayer(...)`、`SetViewingCharacterLayerRemarkAsync(...)`、`DeleteViewingCharacterLayerAsync(...)`，统一查看器备注和删除按钮对服装/表情/装饰的分发。
- 复用已有 `GetCharacterLayerDisplayName(...)`，避免查看器里重复维护图层显示名。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 查看器仍复用背景图查看页面和控件，只是角色图层分支的重复逻辑被收口。
- 背景图查看器自身的备注、删除、相邻切换没有改变。

下一步建议：
- 清理角色图层查看器状态：把 `_viewingCharacterClothPath`、`_viewingCharacterFacePath`、`_viewingCharacterAdornPath` 三个字段收成一个 `CharacterLayerViewerState` 或等价记录，进一步减少分支判断。

### 2026-05-21：Service 拆分第十八步 - 角色图层查看器状态收口

改动：
- 新增 `CharacterLayerViewerState(CharacterLayerKind Kind, string Path)`。
- `MainWindow.xaml.cs` 中 `_viewingCharacterClothPath`、`_viewingCharacterFacePath`、`_viewingCharacterAdornPath` 三个字段合并为 `_viewingCharacterLayer`。
- 背景图查看器打开、角色图层查看器打开、关闭查看器、上一张/下一张、备注和删除分支全部改为读取统一状态。
- `GetViewingCharacterLayer(...)` 改为直接返回 `CharacterLayerViewerState?`，不再临时拼 tuple。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 背景图查看器仍使用 `_viewingBackgroundImagePath` 独立状态；角色图层状态已合并。
- 查看器仍复用背景图页面控件，后续若要彻底拆 UI，可考虑独立 `CharacterLayerViewerController` 或 ViewModel。

下一步建议：
- 做角色图层区域收尾搜索：检查 `MainWindow.xaml.cs` 中角色图层相关 helper 是否还有明显薄包装或重复分支，优先删除无用过渡方法并更新文档。

### 2026-05-21：Service 拆分第十九步 - 角色图层查看器薄包装清理

改动：
- 删除 `ShowCharacterClothViewerPage(...)`、`ShowCharacterFaceViewerPage(...)`、`ShowCharacterAdornViewerPage(...)` 三个只转发的查看器入口，点击图层卡片时直接调用 `ShowCharacterLayerViewerPage(...)`。
- 删除 `GetViewingCharacterLayer(...)` 和 `SetViewingCharacterLayerPath(...)` 两个只访问 `_viewingCharacterLayer` 的薄包装。
- 查看器备注、删除和相邻切换逻辑直接读取统一的 `_viewingCharacterLayer` 状态。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 角色图层文件、拖拽、导入、查看器状态这组局部清理基本收尾。
- `MainWindow.xaml.cs` 中 Story 角色图层选择、同步和修复逻辑仍较集中，适合后续继续拆分。

下一步建议：
- 转向 Story 角色图层选择逻辑：优先梳理 `ChooseStoryCharacterLayerAsync(...)`、`CycleStoryCharacterLayerAsync(...)`、`GetStoryCharacterLayerChoicePaths(...)` 等方法，把选择候选和兼容性过滤整理到可复用 helper 或 service。

### 2026-05-21：Service 拆分第二十步 - Story 角色图层规格收口

改动：
- 新增 `StoryCharacterLayerSpec(CharacterLayerKind Kind, string FieldPrefix, string FolderName, string DisplayName)`，集中描述 Story 角色图层的类型、CSV 字段前缀、素材文件夹和显示名。
- 新增 `GetStoryCharacterLayerSpec(...)` 映射，`GetStoryLayerFieldPrefix(...)` 改为复用该映射。
- `CycleStoryCharacterLayerAsync(...)` 签名改为接收 `CharacterLayerKind`，内部通过 spec 获取字段、文件夹和显示名。
- `ChooseStoryCharacterLayerAsync(...)` 签名改为接收 `CharacterLayerKind`，Story 角色槽菜单和说话人菜单调用点不再手写 `Body/Face/Adorn/Vfx`、`DN_Cloth/FC_Face/AD_Adorn/VFX` 和中文标题三件套。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 选择候选构建、兼容性过滤和 CSV 写入逻辑行为未改变，只是入口参数和图层规格映射收口。
- `CreateStoryLayerChoices(...)`、`GetStoryCompatibleLayerIndexes(...)`、`NormalizeStoryLayerCompatibility(...)` 仍在用 fieldPrefix/folderName，后续可继续改为接收 `StoryCharacterLayerSpec`。

下一步建议：
- 继续整理 Story 图层候选：把 `CreateStoryLayerChoices(...)`、`GetStoryCompatibleLayerIndexes(...)` 和 `NormalizeStoryLayerCompatibility(...)` 改为接收 `StoryCharacterLayerSpec`，逐步减少字符串分支。

### 2026-05-21：Service 拆分第二十一步 - Story 图层候选接入规格

改动：
- `CreateStoryLayerChoices(...)`、`GetStoryCompatibleLayerIndexes(...)`、`NormalizeStoryLayerCompatibility(...)`、`GetStoryLayerChoiceDisplayName(...)` 改为接收 `StoryCharacterLayerSpec`。
- 候选构建、兼容索引和兼容性修正里的 `Adorn/Body/Face` 字符串判断改为基于 `CharacterLayerKind`。
- `NormalizeStoryRowLayerCompatibility(...)` 改为通过 `GetStoryCharacterLayerSpec(...)` 获取表情/装饰规格。
- 删除未使用的 `GetStoryLayerChoiceCount(...)`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 候选生成仍保留在 `MainWindow.xaml.cs`，只是参数从散落字符串改为规格对象。
- 兼容性判断仍调用现有 `IsCharacterLayerCompatibleWithCloth(...)`，没有迁移到 service。

下一步建议：
- 继续拆 Story 图层候选 helper：把 `CreateStoryLayerChoices(...)`、`GetStoryCompatibleLayerIndexes(...)`、`GetStoryLayerChoiceDisplayName(...)` 迁到单独的 Story/角色图层选择 service 或 factory，窗口层只负责弹窗和写入 CSV。

### 2026-05-21：Service 拆分第二十二步 - Story 图层选择工厂

改动：
- 新增 `Views/StoryCharacterLayerChoiceFactory.cs`。
- `StoryCharacterLayerChoiceFactory.CreateChoices(...)` 接管 Story 服装/表情/装饰/VFX 选择项生成。
- `StoryCharacterLayerChoiceFactory.GetCompatibleIndexes(...)` 接管快捷切换时的兼容索引生成。
- `StoryCharacterLayerChoiceFactory.GetDisplayName(...)` 接管选择结果显示名计算。
- `MainWindow.xaml.cs` 保留轻量 wrapper，用于从当前 `StoryRow` 解析 body/face/adorn 路径，并把 `IsCharacterLayerCompatibleWithCloth(...)` 与预览路径构建作为委托传入工厂。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 工厂只负责选择项和索引，不直接读取 CSV，也不修改 StoryRow。
- 兼容性规则本体仍在 `MainWindow.xaml.cs`，后续可以继续迁到 service。

下一步建议：
- 继续拆 Story 图层兼容性：把 `IsCharacterLayerCompatibleWithCloth(...)`、`IsCharacterLayerMetaCompatibleWithSelectedCloth(...)` 和相关 meta/hash 判断迁到角色图层 service，窗口层只传当前角色、服装路径和候选路径。

### 2026-05-21：Service 拆分第二十三步 - 角色图层兼容性规则迁移

改动：
- `CharacterLayerAssetService` 新增 `IsCompatibleWithCloth(...)`，接管服装与表情/装饰/VFX 的兼容性判断，包括文件存在、服装索引解析、scope meta、文件名 scope 和 costume index 匹配。
- `CharacterLayerAssetService` 新增 `IsScopeMetaCompatibleWithCloth(...)`，接管表情/装饰 scope meta 与当前服装 hash 的匹配判断。
- `CharacterLayerAssetService` 新增 `GetFolderName(...)`、`GetCharacterFolderPath(...)`，集中角色图层文件夹名映射。
- `MainWindow.xaml.cs` 中 `IsCharacterLayerCompatibleWithCloth(...)` 和 `IsCharacterLayerMetaCompatibleWithSelectedCloth(...)` 改为委托 service，窗口层只传当前角色、路径和 `ComputeFileHash`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- `MainWindow.xaml.cs` 仍保留兼容性方法名作为过渡包装，调用点尚未全部直连 service。
- hash 计算仍由窗口层提供，避免 service 直接依赖现有全局 hash helper。

下一步建议：
- 清理兼容性包装：把 `MainWindow.xaml.cs` 中 `IsCharacterLayerCompatibleWithCloth(...)` 和 `IsCharacterLayerMetaCompatibleWithSelectedCloth(...)` 的调用点改为直接调用 `CharacterLayerAssetService`，然后删除过渡方法。

### 2026-05-21：Service 拆分第二十四步 - 角色图层兼容性包装清理

改动：
- `MainWindow.xaml.cs` 中 Story 预览、Story 图层选择候选、兼容性修正、角色详情预览候选等调用点改为直接调用 `CharacterLayerAssetService.IsCompatibleWithCloth(...)`。
- 删除 `MainWindow.xaml.cs` 中只转调 service 的 `IsCharacterLayerCompatibleWithCloth(...)` 过渡包装。
- 删除已无调用的 `IsCharacterLayerMetaCompatibleWithSelectedCloth(...)` 过渡包装。
- 兼容性规则继续由 `CharacterLayerAssetService` 统一维护，窗口层只传 `ComputeFileHash` 委托。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- hash 计算仍由窗口层提供，service 不直接依赖全局 hash helper。
- Story CSV 写入和弹窗逻辑没有改变。

下一步建议：
- 继续拆 Story 图层路径解析：把 `GetCharacterLayerPath(...)`、`GetStoryCharacterLayerChoicePaths(...)` 和角色图层文件夹路径映射改为复用 `CharacterLayerAssetService.GetCharacterFolderPath(...)` / `GetFolderName(...)`。

### 2026-05-21：Service 拆分第二十五步 - Story 图层路径解析收口

改动：
- `CharacterLayerAssetService` 新增 `GetLayerPaths(CharacterInfo, CharacterLayerKind)`，统一从角色和图层类型解析文件夹并获取候选素材。
- `CharacterLayerAssetService` 新增 `GetStoryLayerPath(...)`，统一处理 Story CSV 索引到角色图层文件路径的转换，其中装饰仍按现有规则使用 `0` 表示无装饰，实际文件索引从 `1` 开始。
- `StoryCharacterLayerSpec` 去掉 `FolderName` 字段，只保留图层类型、CSV 字段前缀和显示名；文件夹名统一由 `CharacterLayerAssetService.GetFolderName(...)` 维护。
- `MainWindow.xaml.cs` 中 Story 预览预热、角色预览、图层选择、快捷切换和兼容性修正改为通过 `CharacterLayerAssetService.GetLayerPaths(...)` / `GetStoryLayerPath(...)` 解析路径。
- 删除 `GetCharacterLayerPath(...)` 和 `GetStoryCharacterLayerChoicePaths(...)` 过渡方法。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 角色素材页、角色详情页和虚幻同步里仍有部分直接使用 `DN_Cloth` / `FC_Face` / `AD_Adorn` / `VFX` 的路径代码，本步只收口 Story 图层路径解析。
- VFX 仍允许读取非图片文件，保持原有 `GetLayerPaths(..., CharacterLayerKind.Vfx)` 行为。

下一步建议：
- 继续清理角色素材页图层文件夹映射：把角色详情页和导入/删除/备注流程里手写的 `DN_Cloth`、`FC_Face`、`AD_Adorn`、`VFX` 路径改为复用 `CharacterLayerAssetService.GetCharacterFolderPath(...)`。

### 2026-05-21：Service 拆分第二十六步 - 角色素材页文件夹映射收口

改动：
- 角色图层导入流程 `ImportCharacterLayerAsync(...)` 不再接收手写文件夹名，改为通过 `CharacterLayerAssetService.GetCharacterFolderPath(...)` 从 `CharacterLayerKind` 解析目标目录。
- 服装/表情/装饰的备注、删除、可用范围设置流程改为复用 `CharacterLayerAssetService.GetCharacterFolderPath(...)`。
- 角色查看器相邻切换改为使用 `CharacterLayerAssetService.GetLayerPaths(...)`，同时删除窗口层旧的 `GetCharacterLayerFolderPath(...)` 包装。
- 角色详情页加载、选中路径修复、VFX 列表读取改为复用 `GetCharacterFolderPath(...)` / `GetLayerPaths(...)`。
- 角色子文件夹创建和章节修复用的角色图层数量统计也改为走 `CharacterLayerAssetService`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- Expander 标题里仍保留 `DN_Cloth` / `FC_Face` / `AD_Adorn` / `VFX` 文案，因为它们是给用户看的文件夹提示，不是路径规则。
- 虚幻同步相关代码仍有一批角色图层路径硬编码，下一步单独处理，避免和素材页操作混在一起。

下一步建议：
- 继续清理虚幻同步里的角色图层路径映射：把生成 Narrative/Lustration 导入组、结构体引用和已使用素材收集时的 `DN_Cloth`、`FC_Face`、`AD_Adorn` 路径读取改为复用 `CharacterLayerAssetService.GetLayerPaths(...)`。

### 2026-05-21：Service 拆分第二十七步 - 虚幻同步角色图层路径收口

改动：
- 虚幻同步中生成 Lustration 数据资产引用、导入组、`LustrationInfo.csv` 纹理引用和已使用角色图层素材收集时，不再手写 `Path.Combine(character.Path, "DN_Cloth/FC_Face/AD_Adorn")`。
- 新增 `GetUnrealCharacterLayerImportPaths(...)`，统一复用 `CharacterLayerAssetService.GetLayerPaths(...)` 获取本地角色图层文件。
- 新增 `GetUnrealLustrationLayerDestinationPath(...)`，把 Unreal 侧目标目录规则集中在一处；其中服装目标目录仍保留现有 `DN_Cloths` 特例，表情/装饰继续使用 service 的文件夹名映射。
- 保留 Expander 标题里的文件夹名提示和 VFX 指令文案，不把用户可见说明误当成本地路径规则。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- Unreal 目标目录 `DN_Cloths` 与本地文件夹 `DN_Cloth` 名字不一致，这是既有导入约定，本步只集中管理，没有改行为。
- 虚幻同步大流程仍在 `MainWindow.xaml.cs` 中，后续可以继续拆成独立 service。

下一步建议：
- 开始拆虚幻同步 service：优先把 Lustration 引用构建和导入组构建从 `MainWindow.xaml.cs` 迁到独立 `UnrealSyncService`，窗口层只负责读取设置、展示计划和触发同步。

### 2026-05-21：Service 拆分第二十八步 - UnrealSyncService 初始拆分

改动：
- 新增 `Services/UnrealSyncService.cs`，先承接虚幻同步中的纯数据构建逻辑。
- `UnrealSyncService.BuildLustrationSyncEntries(...)` 接管 Lustration 数据资产角色行构建。
- `UnrealSyncService.BuildImportGroups(...)` 接管背景图、音频和角色图层的导入组构建。
- `UnrealSyncService` 集中维护 `BuildTextureReference(...)`、`BuildSoundWaveReference(...)`、`BuildAssetObjectPath(...)` 和 Lustration 目标目录映射。
- `MainWindow.xaml.cs` 中同步计划构建改为调用 `_unrealSyncService`，并删除窗口层旧的 Lustration 行构建、导入组构建和相关小工具方法。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- `UnrealSyncService.WriteLustrationInfoCsv(...)` 也迁入 service，但当前主流程仍通过 manifest/Python 写入数据资产，旧窗口方法本身没有调用。
- Story DataTable、素材索引 DataTable、manifest 写入、Python 脚本生成和差异检测仍在 `MainWindow.xaml.cs`，后续继续拆。

下一步建议：
- 继续拆虚幻同步的素材索引表构建：把 `BuildUnrealAssetIndexTableSyncEntries(...)`、`CreateUnrealAssetIndexTableSyncEntry(...)` 和 `WriteUnrealAssetIndexTableCsv(...)` 迁入 `UnrealSyncService`，窗口层只提供项目缓存目录和素材路径。

### 2026-05-21：Service 拆分第二十九步 - 虚幻素材索引表构建迁移

改动：
- `UnrealSyncService` 新增 `BuildAssetIndexTableSyncEntries(...)`，接管 `BGIndexMap`、`BGMap`、`SceneIndexMap`、`ExsIndexMap` 四张素材索引 DataTable CSV 的生成。
- `UnrealSyncService` 新增 `ComputeAssetIndexTablesHash(...)`，统一计算素材索引表同步缓存 hash。
- `CreateUnrealAssetIndexTableSyncEntry(...)` 和 `WriteUnrealAssetIndexTableCsv(...)` 从 `MainWindow.xaml.cs` 迁入 service 私有方法。
- `MainWindow.xaml.cs` 的同步计划构建只负责计算缓存目录、收集四类素材路径，并调用 `_unrealSyncService.BuildAssetIndexTableSyncEntries(...)`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 素材索引 CSV 的 BOM、列名、行号和引用格式保持原逻辑。
- `UnrealAssetIndexTablesFolderName` 常量仍在窗口层，因为当前缓存目录仍由窗口层拼装；后续拆 manifest/执行流程时可以再收进 service。

下一步建议：
- 继续拆 Story DataTable 同步条目构建：把 `BuildStoryTableSyncEntries(...)`、章节类型到 Unreal 目录映射、旧表资产兼容路径生成这组逻辑迁入 `UnrealSyncService`。

### 2026-05-21：Service 拆分第三十步 - Story DataTable 同步条目迁移

改动：
- 新增 `UnrealStoryTableSource`，用于把窗口层整理好的章节和有效 Story CSV 列表传给同步 service。
- `UnrealSyncService.BuildStoryTableSyncEntries(...)` 接管 Story DataTable 同步条目构建。
- Story DataTable 的 Unreal 目录映射、章节类型分类、分段章节目标目录、旧 DataTable 资产兼容路径生成迁入 `UnrealSyncService`。
- `MainWindow.xaml.cs` 继续负责 Story 编辑器相关的 CSV 清理、空小节删除和有效小节判断，然后把结果交给 service。
- 删除窗口层中只服务虚幻同步的 `BuildUnrealStoryTableFolder(...)`、`BuildLegacyUnrealStoryTableAssets(...)`、`GetUnrealChapterCategoryFolder(...)`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- `TryParseStorySectionFromFileName(...)` 仍保留在窗口层，因为 Story 编辑器分段整理也在使用；`UnrealSyncService` 内部保留私有副本用于旧资产兼容路径生成。
- `GetChapterStoryCsvPathsForUnrealSync(...)` 仍在窗口层，后续如果要继续拆，需要先把 StoryRow 读取和空行判断抽到 Story service。

下一步建议：
- 继续拆虚幻同步 manifest 和 Python 脚本生成：把 `WriteUnrealSyncManifest(...)`、`WriteUnrealSyncPythonScript(...)` 和相关 manifest 数据组装迁入 `UnrealSyncService`。

### 2026-05-21：Service 拆分第三十一步 - 虚幻 Manifest 和 Python 脚本迁移

改动：
- `UnrealSyncService` 新增 `WriteManifest(...)`，接管 `gal-sync-manifest.json` 生成。
- `UnrealSyncService` 新增 `WritePythonScript(...)`，接管 `gal_sync_import.py` 生成。
- `MainWindow.xaml.cs` 的 `RunUnrealSync(...)` 改为读取角色滤镜后调用 `_unrealSyncService.WriteManifest(...)` / `_unrealSyncService.WritePythonScript(...)`。
- 删除窗口层旧的 `WriteUnrealSyncManifest(...)` 和 `WriteUnrealSyncPythonScript(...)`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- Python 脚本内容保持原行为，只迁移归属位置。
- 角色滤镜读取仍在窗口层，因为滤镜文件结构和素材库 UI 逻辑还没有拆成独立 service。

下一步建议：
- 继续拆虚幻执行流程：把 `RunUnrealSync(...)` 中写文件、启动 UnrealEditor-Cmd、收集 stdout/stderr 和读取日志片段迁入 `UnrealSyncService`，窗口层只负责传入角色滤镜、进度回调和展示结果。

### 2026-05-21：Service 拆分第三十二步 - 虚幻执行流程迁移

改动：
- `UnrealSyncService` 新增 `Run(...)`，接管同步文件写入、`UnrealEditor-Cmd` 启动、stdout/stderr 收集、超时终止和 Unreal 日志片段读取。
- `ReadLatestUnrealSyncLogSnippet(...)` 从窗口层迁入 service，作为 `Run(...)` 的私有日志收集逻辑。
- `MainWindow.xaml.cs` 的 `RunUnrealSync(...)` 变为薄包装：读取当前素材库角色滤镜，并调用 `_unrealSyncService.Run(...)`。
- 窗口层继续负责同步前的 UI 确认、备份选择、进度展示、结果提示、缓存状态写入和通知。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 进程参数、超时时间、日志过滤关键词保持原逻辑。
- `MainWindow.xaml.cs` 中仍有非虚幻同步用途的 `Process.Start(...)`，本步没有改动。

下一步建议：
- 继续拆虚幻同步状态与差异检测：把 `SourceFileNeedsUnrealImport(...)`、`SourceFileNeedsUnrealAssetUpdate(...)`、`UnrealAssetObjectPathToFilePath(...)`、`ReadUnrealSyncState(...)`、`WriteUnrealSyncState(...)` 迁入 `UnrealSyncService`。

### 2026-05-21：Service 拆分第三十三步 - 虚幻同步收尾迁移

改动：
- `UnrealSyncService.BuildChangePlan(...)` 接管虚幻同步差异检测，包括素材导入过滤、Story DataTable 变动判断、素材索引表 hash 判断、立绘数据资产 hash 判断和同步计划文案生成。
- `UnrealSyncService.ReadState(...)` / `WriteState(...)` 接管 `.gal-unreal-sync-state.json` 读写，窗口层不再直接维护虚幻同步缓存状态。
- `UnrealSyncService.AssetObjectPathToFilePath(...)`、`SourceFileNeedsImport(...)`、`SourceFileNeedsAssetUpdate(...)` 接管 Unreal object path 到本地 `.uasset` 路径换算和时间戳比较。
- `UnrealSyncService.ResolveEditorExecutable(...)`、`CountAssets(...)` 和 `ExpectedNarrativeFolders` 集中管理虚幻执行器解析、目标目录已有资产计数和 Narrative 参考结构。
- `MainWindow.xaml.cs` 的 `BuildUnrealSyncChangePlan(...)` 现在只负责收集 UI/项目侧输入：角色列表、四类素材路径、Story 表源和缓存目录，然后交给 `_unrealSyncService.BuildChangePlan(...)`。
- 删除窗口层旧的 `ReadUnrealSyncState(...)`、`WriteUnrealSyncState(...)`、`SourceFileNeedsUnrealImport(...)`、`SourceFileNeedsUnrealAssetUpdate(...)`、`UnrealAssetObjectPathToFilePath(...)`、`ResolveUnrealEditorExecutable(...)`、`CountUnrealAssets(...)` 和 Narrative 文件夹常量。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 同步前备份、运行中编辑器确认、同步完成通知、打开项目/日志目录仍保留在窗口层，因为这些属于 UI 编排和系统交互，不是核心同步算法。
- Story CSV 有效小节整理仍在窗口层，后续如果要继续压缩 `MainWindow.xaml.cs`，需要先把 Story 编辑器读写逻辑再拆一层 service。

下一步建议：
- 开始做一次同步外的整体清理扫描：优先抽离 Story/角色滤镜这类仍被窗口层频繁调用的业务逻辑，为后续夜间模式和音效统一接入腾位置。

### 2026-05-21：Service 拆分第三十四步 - 角色滤镜规则迁移

改动：
- 新增 `Services/CharacterFilterService.cs`，接管角色滤镜的读取、写入、默认数据生成、归一化、空滤镜判断、显示名生成和索引重映射。
- `MainWindow.xaml.cs` 的角色滤镜 UI、Story VFX 选择、章节修复统计和虚幻同步滤镜读取都改为调用 `_characterFilterService` / `CharacterFilterService`。
- 删除窗口层旧的 `ReadCharacterFilters(...)`、`ReadStoredCharacterFilters(...)`、`WriteCharacterFilters(...)`、`CreateDefaultCharacterFilters(...)`、`CreateEmptyCharacterFilter(...)`、`NormalizeCharacterFilters(...)`、`IsEmptyCharacterFilter(...)`、`GetCharacterFilterDisplayName(...)`、`BuildCharacterFilterIndexRemap(...)`。
- 清理窗口层不再使用的角色滤镜文件夹/索引文件名常量，路径统一走 `WorkspacePathUtility`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 新增、重命名、删除、拖拽排序的弹窗和同步进度仍在窗口层；service 只承接纯规则和文件读写。
- 删除/排序后同步 Story CSV 的流程保持原逻辑，只是索引重映射改由 `CharacterFilterService.BuildIndexRemap(...)` 计算。

下一步建议：
- 继续抽离 Story 索引同步逻辑：把 `SyncStoryGlobalAssetIndexes(...)`、`SyncStoryCharacterFilterIndexes(...)`、`SyncStoryCharacterLayerIndexes(...)` 和相关 row update helper 迁入独立 service。

### 2026-05-21：Service 拆分第三十五步 - Story 索引同步迁移

改动：
- 新增 `Services/StoryAssetIndexSyncService.cs`，接管 Story CSV 中背景/BGM/环境音、角色图层、角色滤镜的索引同步逻辑。
- `StoryAssetIndexSyncService.SyncGlobalAssetIndexes(...)`、`SyncCharacterLayerIndexes(...)`、`SyncCharacterFilterIndexes(...)` 接管带进度和结果报告的索引同步流程。
- `StoryAssetIndexSyncService` 内部集中维护关联项目 CSV 收集、行级 remap、装饰层 1-based 索引处理、越界 warning、变更记录和标签映射。
- `MainWindow.xaml.cs` 通过 `_storyAssetIndexSyncService` 执行同步；窗口层继续负责弹出进度窗口、刷新当前打开章节、展示同步结果。
- 删除窗口层旧的 `SyncStoryGlobalAssetIndexes(...)`、`SyncStoryCharacterFilterIndexes(...)`、`SyncStoryCharacterLayerIndexes(...)`、`SyncStoryRowsForAssetLibrary(...)`、`GetRelatedStoryCsvFiles(...)`、`TryRecordStoryLayerRemap(...)`、`TryRecordStoryIndexRemap(...)`、`FormatAssetIndexLabel(...)` 等同步算法方法。
- 删除窗口层已无引用的旧直接更新方法 `UpdateStoryGlobalAssetIndexes(...)`、`UpdateStoryCharacterLayerIndexes(...)`、`UpdateStoryCharacterFilterIndexes(...)` 和相关 `RemapStory...` helper。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- `StoryAssetIndexSyncService` 暂时通过委托复用现有项目、章节和 CSV 读写方法；这是为了避免同时重构整个 Story 编辑器读写层。
- 当前打开章节的 UI 刷新仍在窗口层处理，符合 MVVM 迁移中“业务先拆、界面后收”的节奏。

下一步建议：
- 继续抽 Story CSV 读写层：把 `ReadStoryRows(...)`、`WriteStoryRows(...)`、CSV 兼容检查、空行判断和章节小节 CSV 收集整理成 `StoryCsvService`。

### 2026-05-21：Service 拆分第三十六步 - Story CSV 读写层迁移

改动：
- 新增 `Services/StoryCsvService.cs`，集中管理 Story CSV 列定义、数值列定义、CSV 兼容检查、CSV 行解析、header 归一化、默认行创建、读写行、空行判断。
- `StoryCsvService` 接管章节主 CSV 路径解析、旧 `.story.csv` 迁移到 `{ChapterCode}.csv`、本地小节 CSV 收集、小节 CSV 路径生成、松散小节 CSV 候选判断和小节文件名解析。
- `MainWindow.xaml.cs` 中原有 `ReadStoryRows(...)`、`WriteStoryRows(...)`、`CreateDefaultStoryRow(...)`、`GetChapterStoryCsvPath(...)`、`GetLocalStorySectionCsvPaths(...)`、`StoryRowHasContent(...)` 等方法改为薄包装，内部转调 `StoryCsvService`。
- `StoryCsvColumns` 和 `StoryNumericColumns` 现在引用 `StoryCsvService.Columns` / `NumericColumns`，列定义只在 service 中维护。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 为了保持现有委托和调用点稳定，窗口层暂时保留了一层薄包装；后续可以逐步把调用点直接替换成 `_storyCsvService`。
- Story 小节导入/导出流程、当前章节状态同步、UI 提示仍在窗口层，本步只拆底层 CSV 规则和文件读写。

下一步建议：
- 继续收窄窗口层 Story 包装：把 `StoryAssetIndexSyncService` 的委托改为直接依赖 `StoryCsvService` 和项目查询服务，减少 `MainWindow.xaml.cs` 对 CSV helper 的中转。

### 2026-05-21：Service 拆分第三十七步 - Story 索引同步依赖收窄

改动：
- `StoryAssetIndexSyncService` 构造参数移除 CSV 相关委托：`GetLocalStorySectionCsvPaths`、`ReadStoryRows`、`WriteStoryRows`。
- `StoryAssetIndexSyncService` 现在直接依赖 `StoryCsvService` 读取小节 CSV、读取 Story rows、写回 Story rows。
- `MainWindow.xaml.cs` 创建 `_storyAssetIndexSyncService` 时只继续提供项目列表、项目素材库解析、项目 Story CSV 列表、章节目录和章节 meta 读取。
- `MainWindow.xaml.cs` 的 Story CSV 薄包装改为复用 `_storyCsvService` 实例，减少重复 `new StoryCsvService()`。
- 调整 `IsLooseStorySectionCsvCandidate(...)`、`DeleteInactiveLocalStorySectionCsvFiles(...)`、`CleanupVisibleStorySectionCsvFiles(...)` 为实例方法，使它们可以使用当前窗口持有的 `_storyCsvService` 包装。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 项目查询、章节 meta 读取和项目-素材库关联仍由窗口层委托传入；下一步可以继续把这部分做成 `ProjectQueryService` 或扩展 `ProjectWorkspaceService`。
- UI 刷新和当前打开章节状态仍留在窗口层。

下一步建议：
- 继续抽项目查询能力：把 `ResolveProjectAssetLibrary(...)`、`ReadChapterInfo(...)`、`GetProjectStoryCsvPaths(...)` 这类查询从窗口层迁到 service，进一步减少 `MainWindow.xaml.cs` 对 Story 同步的参与。

### 2026-05-21：Service 拆分第三十八步 - 项目与章节查询迁移

改动：
- `ProjectWorkspaceService` 新增 `ResolveProjectAssetLibrary(...)`，接管项目到素材库的关联解析。
- `ProjectWorkspaceService` 新增 `ReadChapterInfo(...)` 和 `GetChapters(...)`，接管章节 meta 读取和章节列表枚举。
- `MainWindow.xaml.cs` 的同名 `ResolveProjectAssetLibrary(...)`、`ReadChapterInfo(...)` 改为薄包装，内部转调 `_projectWorkspaceService`。
- `LoadChapters(...)`、章节跳转选项、章节代号同步、虚幻同步 Story 表收集、项目 Story CSV 统计等位置改为使用 `_projectWorkspaceService.GetChapters(...)`。
- `StoryAssetIndexSyncService` 仍通过窗口传入项目查询委托，但这些委托背后已由 `ProjectWorkspaceService` 承接，后续可继续减少中转。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- `GetProjectStoryCsvPaths(...)` 仍保留在窗口层，因为它依赖 `GetChapterStoryCsvPathsForUnrealSync(...)` 的虚幻同步过滤逻辑；后续可以把这组 Story 表源构建整体迁出。
- 写章节 meta、创建/重命名章节等带 UI 状态和提示的流程仍在窗口层。

下一步建议：
- 继续迁虚幻 Story 表源构建：把 `GetChapterStoryCsvPathsForUnrealSync(...)`、`GetProjectStoryCsvPaths(...)` 和相关小节清理逻辑收进 Story/Unreal 同步 service，进一步压缩窗口层同步代码。

### 2026-05-21：Service 拆分第三十九步 - 虚幻 Story 表源构建迁移

改动：
- `UnrealSyncService` 注入并持有 `StoryCsvService`，用于读取 Story 小节 CSV、判断空行和生成 Story 表资产名。
- `UnrealSyncService.BuildStoryTableSyncEntries(UnrealSyncContext, IReadOnlyList<ChapterInfo>)` 接管虚幻同步 Story 表源构建。
- `UnrealSyncService.GetProjectStoryCsvPaths(...)` 接管项目 Story CSV 统计/收集逻辑。
- 虚幻同步用的小节缓存清理、空小节 CSV 删除、单/多小节 DataTable asset name 选择迁入 `UnrealSyncService`。
- `MainWindow.xaml.cs` 的 `BuildStoryTableSyncEntries(...)` 和 `GetProjectStoryCsvPaths(...)` 变为薄包装，只提供项目章节列表并调用 `_unrealSyncService`。
- 删除窗口层旧的 `GetChapterStoryCsvPathsForUnrealSync(...)` 同步核心逻辑；保留 `GetUnrealStorySectionCacheFolder(...)` / `CleanupUnrealStorySectionCache(...)` 作为 Story 小节导出 UI 的缓存路径工具。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- `UnrealSyncService` 内部仍保留一份旧资产兼容命名 helper，用于清理/兼容旧 DataTable 路径；后续可以和 `StoryCsvService` 的命名 helper 再做统一。
- Story 小节导出流程仍在窗口层，属于编辑器 UI 状态管理，不在本步迁移范围。

下一步建议：
- 继续统一 Story/Unreal 命名 helper：让 `UnrealSyncService` 复用 `StoryCsvService` 的小节命名和解析，删除重复的 `BuildSectionCsvBaseName(...)`、`BuildSectionCsvChapterBaseName(...)`、`RemoveChapterSectionSuffix(...)` 等私有方法。

### 2026-05-21：Service 拆分第四十步 - Story/Unreal 小节命名去重

改动：
- `UnrealSyncService` 的 Story DataTable 目录、旧资产兼容名、小节缓存目录和同步 CSV asset name 统一复用 `StoryCsvService` 的小节命名规则。
- 删除 `UnrealSyncService` 内重复的 `TryParseStorySectionFromFileName(...)`、`BuildSectionCsvBaseName(...)`、`BuildSectionCsvChapterBaseName(...)`、`RemoveChapterSectionSuffix(...)`。
- `BuildLegacyStoryTableAssets(...)` 从 static 调整为实例方法，以便复用 `_storyCsvService.TryParseStorySectionFromFileName(...)`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 旧资产兼容名仍保留 `_小节N` 形式，行为保持兼容，只是解析和基础命名来源统一到 `StoryCsvService`。

下一步建议：
- 继续清理窗口层 Story CSV 薄包装：逐步把调用点直接替换为 `_storyCsvService`，删除 `MainWindow.xaml.cs` 中只转调的 `ReadStoryRows(...)`、`WriteStoryRows(...)`、`GetChapterStoryCsvPath(...)` 等包装方法。

### 2026-05-21：Service 拆分第四十一步 - Story CSV 薄包装清理

改动：
- `MainWindow.xaml.cs` 中 Story CSV 相关调用点直接改为 `_storyCsvService` / `StoryCsvService`。
- 删除窗口层只转调 service 的 `InspectStoryCsvCompatibility(...)`、`GetStorySectionCsvPath(...)`、`GetLocalStorySectionCsvPaths(...)`、`TryParseStorySectionFromFileName(...)`、`StoryRowHasContent(...)`、`GetChapterStoryCsvPath(...)`、`CreateDefaultStoryRow(...)`、`ReadStoryRows(...)`、`WriteStoryRows(...)`、`ParseCsvLine(...)`、`NormalizeStoryCsvHeaders(...)` 等包装方法。
- `MainWindow.xaml.cs` 保留 `StoryCsvColumns` / `StoryNumericColumns` 只作为兼容现有 UI 判断的别名，实际数据源仍来自 `StoryCsvService`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 本步做过一次机械替换，随后已清理被替换影响的方法声明，并通过 Release 构建确认。

下一步建议：
- 继续处理窗口层剩余 Story 编辑器状态逻辑：优先把 Story 小节状态读写、选择备注读写或章节修复逻辑继续拆到 service。

### 2026-05-21：Service 拆分第四十二步 - Story 状态文件迁移

改动：
- 新增 `Services/StoryStateService.cs`，接管 Story 小节状态和选择备注状态的文件读写。
- `StoryStateService.ReadSectionMap(...)` / `WriteSectionState(...)` 接管 `story.sections.json` 读写和小节值归一化。
- `StoryStateService.ReadChoiceNotes(...)` / `WriteChoiceNotes(...)` 接管 `story.choice-notes.json` 读写、key 清理和备注文本归一化。
- `StoryStateService.CopyChoiceNotes(...)`、`RemoveChoiceNotes(...)`、`CloneChoiceNotes(...)` 接管选择备注的复制、删除和深拷贝。
- `MainWindow.xaml.cs` 的相关方法改为薄包装或转调 `_storyStateService`；窗口层继续判断哪些 choice 仍被当前行使用。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 选择备注是否仍在当前章节行中使用，仍依赖窗口层 `_storyRows`，暂不迁移。
- 小节状态同步算法仍在窗口层；本步只迁文件状态读写和备注状态操作。

下一步建议：
- 继续拆章节修复逻辑：把章节索引检查/自动修复的纯扫描和修复部分迁到 service，窗口层只保留进度弹窗和结果展示。

### 2026-05-21：Service 拆分第四十三步 - 章节索引修复迁移

改动：
- 新增 `Services/ChapterRepairService.cs`，接管章节本地小节 CSV 的索引检查和自动修复。
- `ChapterRepairService.Scan(...)` 负责读取章节小节 CSV、检查背景图/BGM/环境音索引、说话人和 1-5 号位的身体/表情/装饰/滤镜索引，并在修复模式下把可自动修复的异常值归零后写回 CSV。
- 角色别名、角色素材数量、背景/BGM/环境音/滤镜数量收敛为 `ChapterRepairAssetContext`，窗口层只负责从当前素材库组装上下文。
- `MainWindow.xaml.cs` 删除章节修复的纯扫描/校验 helper，只保留 UI 进度、确认弹窗和结果展示流程。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 章节修复仍依赖窗口层当前素材库选择来构造 `ChapterRepairAssetContext`，这是 UI 状态边界，暂时保留在窗口层。

下一步建议：
- 继续拆 Story 编辑器行操作：把行插入/复制/删除、撤销快照、小节同步判断整理成 `StoryEditorService`，让窗口层只负责选中项、弹窗和控件刷新。

### 2026-05-21：Service 拆分第四十四步 - Story 编辑器行操作内核

改动：
- 新增 `Services/StoryEditorService.cs`，接管 Story 行列表的纯数据操作。
- `StoryEditorService` 负责克隆行、按顺序重命名 `Name`、按行顺序读取/应用小节、同步缺失小节状态、新建下一句、原地插入和删除当前句。
- 新增 `StoryRowsEditResult`，让 service 返回新的当前行、是否发生数据改动、需要清理的 choice 值。
- `MainWindow.xaml.cs` 的下一句/原地新建/删除当前句/小节同步 helper 已改为转调 service；窗口层继续负责调试模式拦截、撤销快照、持久化、预览刷新和状态提示。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- Story 编辑器仍有大量 UI 强相关逻辑在窗口层，例如预览、函数编辑、素材选择和快捷键；本步只迁纯数据内核。

下一步建议：
- 继续拆 Story 编辑器保存/加载会话：把 `LoadStoryRowsFromSectionFiles(...)`、`ImportStorySectionCsvFiles(...)`、`PersistCurrentStoryRowsToFiles(...)` 进一步整理成 `StorySessionService`，窗口层只接收加载结果和提示信息。

### 2026-05-21：Service 拆分第四十五步 - Story 会话加载与保存迁移

改动：
- 新增 `Services/StorySessionService.cs`，接管 Story 编辑器的章节小节 CSV 加载、松散小节 CSV 检测、导入小节 CSV、按小节保存当前行列表。
- 新增 `StoryRowsLoadResult`、`StorySectionImportResult`、`StoryRowsPersistResult`、`StorySessionLogEntry`，让 service 返回数据、清理数量、导入日志和活跃 CSV 数量。
- `MainWindow.xaml.cs` 的 `LoadStoryRowsFromSectionFiles(...)`、`ImportStorySectionCsvFiles(...)`、`PersistCurrentStoryRowsToFiles(...)` 改为薄包装，窗口层只负责刷新当前 `_storyRows` / `_storyRowSections`、输出日志和展示提示。
- 删除 `ExportStorySectionsAsync(...)` 中永远不可达的旧导出分支，以及它专用的旧 `SectionCsv` / `UnrealStorySections` 清理 helper，避免后续维护误读。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- Story 编辑器的函数编辑、素材选择弹窗、预览播放、快捷键等仍在窗口层，这些属于强 UI 行为，后续应按 ViewModel 页面边界逐步迁。

下一步建议：
- 进入真正的 ViewModel 化：优先做 `StoryEditorViewModel` 的状态镜像，把当前行、当前小节、行计数、调试模式、撤销可用状态从窗口字段迁出去，再逐步绑定 XAML。

### 2026-05-21：MVVM 拆分第四十六步 - StoryEditorViewModel 状态接入

改动：
- 新增 `ViewModels/StoryEditorViewModel.cs`，接管 Story 编辑器当前章节、素材库、CSV 路径、当前行索引、行列表、小节映射、撤销栈、调试模式、脏状态和持久化/加载标记。
- `MainWindow.xaml.cs` 中原 `_storyRows`、`_storyRowSections`、`_currentStoryRowIndex`、`_currentStoryCsvPath`、`_storyUndoStack` 等 Story 状态字段改为 ViewModel 属性代理，保留原事件流程。
- Story 顶部标题、CSV 路径、撤回按钮可用状态、行号和总数改为绑定 `StoryEditorViewModel`。
- 小节位置计算、当前小节读取、按当前行设置小节迁到 ViewModel，窗口层只负责 ComboBox 项生成和业务触发。
- 删除窗口层已经失效的 `StorySectionsFileName` 常量和旧小节状态读取残留。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 这是兼容式 ViewModel 接入：窗口事件仍在，先保证状态源迁移和基础绑定稳定，再继续迁命令和复杂 UI 行为。

下一步建议：
- 继续把 Story 编辑器按钮命令迁入 ViewModel：优先处理上一句/下一句/原地新建/删除/撤回这些已拥有 service 内核的操作，最后再处理素材选择、函数编辑和预览播放。

### 2026-05-21：MVVM 拆分第四十七步 - Story 编辑器命令与文本绑定

改动：
- `StoryEditorViewModel` 新增 `UndoCommand`、`PreviousRowCommand`、`NextRowCommand`、`InsertRowCommand`、`DeleteRowCommand`、`PreviousSectionCommand`、`NextSectionCommand`、`AddSectionCommand`。
- Story 编辑器的撤回、上一句、下一句、原地新建、删除此句、上一节、下一节、新增小节按钮改为 XAML `Command` 绑定。
- 删除这些按钮旧的 `Click` 事件壳，窗口层保留命令回调方法，继续承接保存、提示、预览刷新等 UI 副作用。
- `StoryEditorViewModel` 新增 `SpeakerText` 和 `StoryText`，说话人输入框和剧情文本框改为双向绑定；保存逻辑从 ViewModel 读取文本，不再直接读取 TextBox。
- 同步说话人逻辑改为写入 `StoryEditorViewModel.SpeakerText`，不再直接写 `StorySpeakerTextBox.Text`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 文本框仍保留 `TextChanged` 事件用于现有撤销快照和延迟保存；后续可以把这层变更追踪继续迁入 ViewModel。

下一步建议：
- 继续迁 Story 编辑器“当前素材/函数状态”显示：把当前背景、BGM、环境音、函数摘要、素材库状态这些 TextBlock 文本放进 ViewModel，再逐步处理素材选择和函数弹窗。

### 2026-05-21：MVVM 拆分第四十八步 - Story 工具栏状态绑定

改动：
- 新增 `ViewModels/BooleanToVisibilityConverter.cs`，在 `App.xaml` 注册为通用布尔可见性转换器。
- `StoryEditorViewModel` 新增当前背景、BGM、环境音、函数摘要、素材库状态、是否有当前函数、是否有当前选项等状态属性。
- Story 主工具栏中的当前背景/BGM/环境音/函数/素材库状态 TextBlock 改为绑定 ViewModel。
- 查看选项、移除函数、清空函数按钮的可见性改为绑定 `HasCurrentChoices` / `HasCurrentFunction`。
- `MainWindow.xaml.cs` 中对应位置不再直接写主工具栏 TextBlock 和按钮 Visibility，而是更新 ViewModel 状态。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 历史折叠隐藏工具栏仍保留少量旧 TextBlock，后续建议整体删除或统一接入同一套绑定。

下一步建议：
- 继续清理 Story 页面遗留/隐藏面板，然后把函数编辑和素材选择弹窗拆成可复用的 ViewModel command + dialog service 调用。

### 2026-05-21：MVVM 拆分第四十九步 - Story 遗留隐藏面板清理

改动：
- 删除 Workbench 内两个已折叠不用的 Story 侧边栏副本。
- 删除 Story 编辑器内两个已折叠不用的旧工具栏副本。
- 清理这些副本关联的旧控件名，包括 legacy 显示设置 CheckBox、旧素材状态 TextBlock 和旧侧边栏容器。
- 保留当前正在使用的 `StorySettingsPane`、`StoryFunctionTipsPanel`、`StoryFloatingTipsPanel`，后续继续围绕这一套 UI 做 MVVM 化。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- XAML 编译已通过，但 Story 区域局部缩进仍有历史痕迹，后续可以做一次小范围格式整理。

下一步建议：
- 继续迁素材选择/函数按钮命令：先为更换背景图/BGM/环境音、添加/移除/清空函数建立 ViewModel command，窗口回调继续负责弹窗。

### 2026-05-21：MVVM 拆分第五十步 - Story 素材/函数按钮命令化

改动：
- `StoryEditorViewModel` 新增更换背景图、BGM、环境音、添加函数、查看选项、移除函数、清空函数、清空当前行数据等命令。
- Story 主工具栏的素材和函数按钮改为绑定 ViewModel 命令。
- 删除这些按钮旧的 `Click` 事件壳；窗口层保留命令回调方法，继续负责弹窗、保存、预览刷新、音频停止和提示。
- 清空函数、清空当前行数据整理为可由命令调用的私有方法。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本批没有主动启动程序做运行检查。
- 素材选择和函数弹窗逻辑仍在窗口层；本步先完成命令入口迁移，后续再拆弹窗内容和选择流程。

下一步建议：
- 继续抽 Story 弹窗/选择流程：优先处理 `ShowStorySimpleChoiceDialogAsync(...)` 和素材索引选择，让素材/函数命令只依赖对话服务返回结果。

### 2026-05-21：MVVM 拆分第五十一步 - Story 选择弹窗服务化收尾

改动：
- 新增并接入 `Services/StoryDialogService.cs`，统一 Story 普通选择、带预览选择、素材索引选择、当前选项查看。
- `MainWindow.xaml.cs` 的填写函数、移除函数、BGM/跳转/转场/特殊音效选择，以及角色/图层/角色详情选择，改为直接调用 `_storyDialogService`。
- 删除 `MainWindow.xaml.cs` 中只转调的 `ShowStorySimpleChoiceDialogAsync(...)` / `ShowStoryChoiceDialogAsync(...)` 过渡方法，让 Story 选择弹窗入口集中到服务层。
- `StoryDialogContentFactory` 继续只负责构造弹窗内容，`StoryDialogService` 负责把内容接到统一 `IDialogService`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本步没有主动启动程序做运行检查。
- 导入章节 CSV、兼容性说明、选择备注编辑等强 UI 弹窗仍保留在窗口层或内容工厂中；它们和 Story 选择弹窗不同，后续要按业务边界单独拆。

下一步建议：
- 继续抽 Story 函数值构建和选择备注编辑：把 `BuildStoryFunctionValueAsync(...)`、章节/小节跳转值、选择备注读写组合成更清晰的函数编辑服务，窗口层只保留状态提示和保存触发。

### 2026-05-21：MVVM 拆分第五十二步 - Story 函数规则服务化

改动：
- 新增 `Services/StoryFunctionService.cs`，集中管理 Story 函数卡内置模板、模板识别、默认函数列表和历史 BGM 模板清理。
- 背景切换模式、BGM 控制、跳转章节、跳转小节的候选项生成迁入 `StoryFunctionService`。
- 章节跳转值 `IntoChapter_...`、小节跳转值 `IntoSegment_...`、函数选择卡显示文本、触发选项建议名生成迁入 `StoryFunctionService`。
- `MainWindow.xaml.cs` 删除对应的模板常量、默认函数创建、模板修复和函数值拼接 helper，只保留弹窗、保存、状态提示这些 UI 相关流程。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本步没有主动启动程序做运行检查。
- `BuildStoryFunctionValueAsync(...)` 仍在窗口层，因为它要串联输入弹窗、选择备注保存、特殊音效选择和当前行保存；后续如果继续拆，需要先定义函数编辑 request/result。

下一步建议：
- 收尾清理 Story 选择/函数相关残留搜索结果：确认窗口层没有旧模板 helper、旧 Story 选择 wrapper 和重复候选项构建，再把文档中的“下一步”更新为更大的后续方向。

### 2026-05-21：MVVM 拆分第五十三步 - 函数卡读写服务化收尾

改动：
- `StoryFunctionService.ReadFunctions(...)` 接管函数卡索引读取、默认函数初始化、函数条目归一化和内置模板修复。
- `StoryFunctionService.WriteFunctions(...)` 接管 `functions.json` 写入。
- `MainWindow.xaml.cs` 删除函数文件夹/索引文件常量，以及窗口层 `ReadFunctions(...)` / `WriteFunctions(...)` 包装。
- 函数卡加载、新建、编辑、删除、Story 选择函数、触发选项建议名全部改为调用 `StoryFunctionService`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本步没有主动启动程序做运行检查。
- `FunctionIndex` 模型仍放在 `AssetLibraryModels.cs`，因为它属于素材库数据结构；如果后续整理模型文件，可以再按素材类型拆分。

下一步建议：
- 本轮 Story/同步相关的可安全服务化收尾已经完成。后续更大的方向是把 Story 预览播放、角色槽快捷键和函数编辑 request/result 继续拆出窗口层，但这些会触碰交互细节，建议单独开新轮处理。

### 2026-05-21：MVVM 拆分第五十四步 - 函数编辑弹窗服务化

改动：
- 新增 `Services/FunctionDialogService.cs`。
- 函数卡新建/编辑弹窗从 `MainWindow.xaml.cs` 迁到 `FunctionDialogService.EditFunctionAsync(...)`。
- 触发选项备注弹窗从 `MainWindow.xaml.cs` 迁到 `FunctionDialogService.EditChoiceNotesAsync(...)`。
- `FunctionDialogService` 统一使用现有 `IDialogService` 和 `IUiSoundService`，保留按钮音效规则。
- `MainWindow.xaml.cs` 删除 `ShowFunctionEditorDialogAsync(...)` 和 `ShowChoiceFunctionNoteDialogAsync(...)`，只保留建议名计算、保存、刷新和状态提示。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本步没有主动启动程序做运行检查。
- 弹窗内容仍由 `EditorDialogContentFactory` / `StoryDialogContentFactory` 创建，本步只收拢弹窗打开与结果读取入口。

下一步建议：
- 继续把 Story 函数解析、函数 key 归一化、显示名枚举等静态 helper 迁入 `StoryFunctionService`，为后续拆预览播放和函数触发提示做准备。

### 2026-05-21：MVVM 拆分第五十五步 - Story 函数字符串解析服务化

改动：
- `StoryFunctionService` 新增 `SplitFunctionValues(...)`、`ContainsFunction(...)`、`EnumerateFunctionDisplayNames(...)`。
- `StoryFunctionService` 新增背景转场函数解析和显示：`TryParseBackgroundTransitionMode(...)`、`GetBackgroundTransitionModeDisplay(...)`、`GetBackgroundTransitionModeRemark(...)`。
- `MainWindow.xaml.cs` 删除函数 key 归一化、函数值拆分、函数显示名枚举、背景转场解析等静态 helper。
- Story 函数触发提示、BGMSTART/BGMSTOP 判断、背景转场状态扫描统一调用 `StoryFunctionService`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本步没有主动启动程序做运行检查。
- 媒体播放器、图片控件和 Story 预览布局仍在窗口层；本步只迁纯字符串解析和显示规则。

下一步建议：
- 继续抽 Story 预览状态计算：优先把“根据当前行算出背景/BGM/环境音/角色槽索引”的纯数据准备拆出来，暂时不要迁移 WinUI 图片加载和 MediaPlayer 播放。

### 2026-05-21：MVVM 拆分第五十六步 - Story 角色槽数据 helper 服务化

改动：
- 新增 `Services/StoryCharacterSlotService.cs`。
- 角色槽列名、图层列名、槽位显示名、剪贴板格式化、CJK 判断迁入 `StoryCharacterSlotService`。
- 角色槽剪贴板创建、匹配、应用、空槽判断、图层索引归零迁入 `StoryCharacterSlotService`。
- `MainWindow.xaml.cs` 的复制/粘贴/清空/快捷切换/预览准备继续负责 UI 和保存，但纯 StoryRow 数据读写规则改为调用服务。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本步没有主动启动程序做运行检查。
- 角色槽快捷键、兼容图层选择、预览图片加载仍在窗口层；本步只拆数据 helper。

下一步建议：
- 后续再拆角色槽快捷键时，可以基于 `StoryCharacterSlotService` 继续抽“复制/粘贴/清空/循环切换”的结果对象，让窗口层只负责提示、保存和刷新预览。

### 2026-05-21：MVVM 拆分第五十七步 - Story 基础素材字段剪贴板服务化

改动：
- 新增 `Services/StoryAssetFieldService.cs`。
- 背景图/BGM/环境音字段显示名、剪贴板创建、字段类型匹配、值匹配和应用迁入 `StoryAssetFieldService`。
- `MainWindow.xaml.cs` 的基础素材复制/粘贴流程继续负责快捷键响应、提示、保存、媒体刷新和预览刷新，纯字段规则改为调用服务。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本步没有主动启动程序做运行检查。
- Story 预览播放和角色槽循环切换仍在窗口层；它们已经接近 WinUI/MediaPlayer 行为边界，后续拆分建议按具体问题或功能点单独推进。

下一步建议：
- 当前轮建议停在稳定点。下一轮优先处理 Story 预览状态计算，或者转向清理项目/素材库创建、备份、打包脚本等非 UI 密集模块。

### 2026-05-21：MVVM 拆分第五十八步 - 文件夹备份服务化

改动：
- 新增 `Services/FolderBackupService.cs`。
- 项目、素材库、章节共用的备份创建、导出 zip、还原 zip、备份枚举、旧备份裁剪、备份 meta 读取写入迁入 `FolderBackupService`。
- `MainWindow.xaml.cs` 的项目/素材库/章节备份与还原流程改为调用 `_folderBackupService`。
- 窗口层继续负责备注弹窗、文件保存 picker、全局底部进度条、刷新列表和日志。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本步没有主动启动程序做运行检查。
- Unreal 同步前的临时备份仍在窗口/Unreal 流程附近，暂未纳入 `FolderBackupService`；它和同步状态、进度、运行中的 Unreal Editor 检查关系更紧。

下一步建议：
- 继续处理项目/素材库创建、导入归档、章节 meta 写入等非 UI 文件逻辑，让窗口层逐步只保留 picker、弹窗和导航刷新。

### 2026-05-21：MVVM 拆分第五十九步 - 创建名称校验下沉

改动：
- `ProjectWorkspaceService.CreateProject(...)` 内部校验项目名称和项目英文代号。
- `ProjectWorkspaceService.CreateAssetLibrary(...)` 内部校验素材库名称。
- 新增 `ProjectWorkspaceService.ValidateFolderName(...)`，统一处理空名称和非法文件夹字符。
- `MainWindow.xaml.cs` 删除创建项目/素材库前的本地 `ValidateFolderName(...)`，继续负责把服务异常显示到对应 InfoBar。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本步没有主动启动程序做运行检查。
- 因服务层抛出的仍是 `IOException`，现有 UI 错误处理路径保持不变。

下一步建议：
- 继续迁移章节 meta 写入与章节创建/编辑的文件逻辑，减少窗口层直接 `File.WriteAllText`。

### 2026-05-21：MVVM 拆分第六十步 - 章节 meta 写入服务化

改动：
- `ProjectWorkspaceService` 接管章节创建、修改、导入章节占位创建、章节 meta 写入和最后编辑行保存。
- `MainWindow.xaml.cs` 删除窗口层 `chapter.meta.json` 常量和章节 meta 写入 wrapper。
- CSV 导入章节、章节代号同步、故事进度保存改为调用 `ProjectWorkspaceService`。
- 章节创建/修改里的同名代号异常仍在窗口层显示为 warning，避免服务化后反馈语义变重。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本步没有主动启动程序做运行检查。
- 章节编辑弹窗仍是 WinUI 组合内容，暂时留在窗口层；后续可在迁移章节页 ViewModel 时再处理。

下一步建议：
- 继续迁移素材库角色文件夹、角色 meta 和角色枚举逻辑，减少素材页重复读目录代码。

### 2026-05-21：MVVM 拆分第六十一步 - 角色工作区服务化

改动：
- 新增 `Services/CharacterWorkspaceService.cs`。
- 角色 `character.json` 读取写入、角色文件夹创建/重命名、立绘子目录保证、按名称/代号/文件夹顺序枚举角色迁入服务。
- `MainWindow.xaml.cs` 的角色创建、重命名、素材库角色列表、Story 角色选择、Unreal 同步角色枚举改为调用 `CharacterWorkspaceService`。
- 删除窗口层 `ReadCharacterInfo`、`WriteCharacterMeta`、`EnsureCharacterSubfolders`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本步没有主动启动程序做运行检查。
- 角色图层排序、兼容范围、导入重命名仍由 `CharacterLayerAssetService` 负责；窗口层仍负责 GridView、预览和弹窗。

下一步建议：
- 继续清理窗口层剩余的直接文件读写，优先处理项目/素材库和 Unreal 绑定相关的 meta 读写。

### 2026-05-21：MVVM 拆分第六十二步 - 项目 Unreal 绑定读写下沉

改动：
- `ProjectWorkspaceService` 新增 `ReadProjectUnrealBinding(...)` 和 `SaveProjectUnrealBinding(...)`。
- Unreal 同步项目卡、同步页路径回填、同步设置保存改为调用项目工作区服务。
- `MainWindow.xaml.cs` 删除窗口层 `ReadProjectUnrealBinding(...)` / `SaveProjectUnrealBinding(...)` 和不再使用的项目/素材库 meta 常量。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本步没有主动启动程序做运行检查。
- 同步执行、差异检测、通知和备份仍在窗口层串联；这里只拆 meta 读写边界。

下一步建议：
- 继续检查 MainWindow 中剩余的直接 `Directory` / `File` 文件工作，优先下沉能独立成服务且不碰 WinUI 控件的部分。

### 2026-05-21：MVVM 拆分第六十三步 - Story 选择备注 wrapper 清理

改动：
- `MainWindow.xaml.cs` 删除 `ReadStoryChoiceNoteState(...)`、`WriteStoryChoiceNoteState(...)`、`CloneStoryChoiceNoteState(...)` 这些薄 wrapper。
- Story 选择备注读取、写入、复制、删除统一直接调用 `StoryStateService`。
- 删除窗口层已无调用的通用 `ReadJson<T>(...)`，窗口层不再直接 `File.ReadAllText` / `File.WriteAllText` 读写 JSON。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本步没有主动启动程序做运行检查。
- 选择备注弹窗和当前行联动仍在窗口层；数据持久化边界已经在 `StoryStateService`。

下一步建议：
- 继续收敛窗口层剩余 `Directory` / `File` 操作：章节删除/导入、素材文件存在性判断、项目根目录迁移、Unreal 备份等需要按风险分批处理。

### 2026-05-21：MVVM 拆分第六十四步 - 整体项目目录迁移服务化

改动：
- 新增 `Services/ProjectRootMigrationService.cs`。
- 整体项目目录迁移的目录创建、递归复制、文件大小校验、哈希校验、旧目录删除迁入服务。
- `MainWindow.xaml.cs` 的整体项目位置切换流程只保留 FolderPicker、全局底部进度条、设置保存、状态提示和日志。
- 迁移校验复用 `FileSystemUtility.HashesEqual(...)`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本步没有主动启动程序做运行检查。
- 目录迁移仍是会复制并删除旧目录的高影响操作；服务只搬迁现有逻辑，没有改变交互确认流程。

下一步建议：
- 继续检查 `MainWindow.xaml.cs` 剩余大块文件操作。若没有低风险纯服务块，优先转向最终整理：文档、构建、状态汇总。

### 2026-05-21：MVVM 拆分第六十五步 - 章节目录操作继续下沉

改动：
- `ProjectWorkspaceService` 新增 `DeleteChapter(...)`，章节删除目录操作从窗口层迁出。
- `ProjectWorkspaceService` 新增 `UpdateChapterProjectCodePrefix(...)`，项目英文代号修改后的章节目录重命名、重复目标检查、章节 meta 更新统一在服务层执行。
- `ProjectWorkspaceService.GetChapterCodeSegment(...)` 复用给章节编辑弹窗，保留旧逻辑：优先去掉当前项目前缀，否则取第一个 `-` 后面的片段。
- 新增 `ChapterRenamePlan` 记录，避免窗口层匿名对象承担业务计划。
- `MainWindow.xaml.cs` 删除未再使用的 `ChaptersFolderName`、`ReplaceChapterProjectCode(...)`、`GetChapterCodeSegment(...)`。

验证：
- Release 构建通过：0 警告，0 错误。

风险/注意：
- 本步没有主动启动程序做运行检查。
- CSV 拖入导入章节仍留在窗口层，因为它同时涉及兼容性弹窗、CSV 行读写、章节创建和 section 状态写入；后续若继续拆，建议设计成一个明确的 ImportChapterCsv 服务结果对象。

下一步建议：
- 当前重构已到可交付稳定点。后续若继续缩小 `MainWindow.xaml.cs`，建议优先处理 UI 密集块的 XAML code-behind 分拆，而不是继续把零散 `File.Exists` 判断强行抽服务。

## 每一步完成后的记录模板

```text
### 日期：第 N 步 - 标题

改动：
- 

验证：
- Release 构建：

风险/注意：
- 

下一步建议：
- 
```
