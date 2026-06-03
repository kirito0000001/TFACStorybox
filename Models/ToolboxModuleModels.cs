namespace GalExcleTools;

internal enum ToolboxModuleKey
{
    Workbench,
    AssetLibrary,
    StoryEditor,
    UnrealSync,
    Settings
}

internal enum ToolboxModuleCategory
{
    Shell,
    Project,
    Asset,
    Story,
    Integration,
    Settings
}

internal sealed record ToolboxModuleDefinition(
    ToolboxModuleKey Key,
    string Tag,
    string DisplayName,
    ToolboxModuleCategory Category,
    string Description);
