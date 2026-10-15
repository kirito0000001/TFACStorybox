using System.Collections.Generic;

namespace GalExcleTools.ViewModels;

internal sealed class ApplicationViewModel : ObservableObject
{
    private ToolboxModuleKey _selectedModule = ToolboxModuleKey.Workbench;

    public ApplicationViewModel(SettingsViewModel settings, GlobalProgressViewModel globalProgress)
    {
        Settings = settings;
        GlobalProgress = globalProgress;
        Workbench = new ProjectWorkbenchViewModel();
        AssetLibrary = new AssetLibraryViewModel();
        StoryEditor = new StoryEditorViewModel();
        UnrealSync = new UnrealSyncViewModel();

        // 热更新面板：ViewModel 自己不发请求、不起进程，只把用户动作翻成对
        // Services/Update/UpdateService 的一次调用。进度条与「退出前收尾」由窗口提供
        // （窗口构造完调 AttachUpdateHost 把它自己接上）。
        Update = new UpdateViewModel(settings);
    }

    public SettingsViewModel Settings { get; }

    public GlobalProgressViewModel GlobalProgress { get; }

    public ProjectWorkbenchViewModel Workbench { get; }

    public AssetLibraryViewModel AssetLibrary { get; }

    public StoryEditorViewModel StoryEditor { get; }

    public UnrealSyncViewModel UnrealSync { get; }

    public UpdateViewModel Update { get; }

    public IReadOnlyList<ToolboxModuleDefinition> Modules { get; } =
    [
        new(ToolboxModuleKey.Workbench, "Workbench", "项目工作台", ToolboxModuleCategory.Project, "项目、章节、文本与备份入口"),
        new(ToolboxModuleKey.AssetLibrary, "AssetLibrary", "素材库", ToolboxModuleCategory.Asset, "背景、音频、角色与函数素材管理"),
        new(ToolboxModuleKey.StoryEditor, "StoryEditor", "剧情编辑器", ToolboxModuleCategory.Story, "章节 CSV 和剧情播放预览"),
        new(ToolboxModuleKey.UnrealSync, "UnrealSync", "虚幻同步台", ToolboxModuleCategory.Integration, "工具箱数据与 Unreal 资源同步"),
        new(ToolboxModuleKey.Settings, "Settings", "整体设置", ToolboxModuleCategory.Settings, "全局路径、辅助显示与工具箱偏好")
    ];

    public ToolboxModuleKey SelectedModule
    {
        get => _selectedModule;
        set => SetProperty(ref _selectedModule, value);
    }

    public ToolboxModuleDefinition? FindModuleByTag(string tag)
    {
        foreach (var module in Modules)
        {
            if (string.Equals(module.Tag, tag, System.StringComparison.Ordinal))
            {
                return module;
            }
        }

        return null;
    }
}
