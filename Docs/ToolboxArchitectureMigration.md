# Toolbox Architecture Migration Map

This file tracks the MVVM migration shape for GalExcleTools. It is intentionally practical: use it before moving more code out of `MainWindow.xaml.cs`.

## Current Frame

The toolbox now has a root application ViewModel:

- `ApplicationViewModel`
  - owns module definitions and selected module state.
  - aggregates cross-cutting ViewModels.
- `SettingsViewModel`
  - owns global settings, project root state, settings persistence, project root migration orchestration, log filtering state, and user-facing settings status.
- `GlobalProgressViewModel`
  - owns global progress text, percent, elapsed text, detail, and indeterminate state.

`MainWindow` remains the WinUI shell and bridge. It still hosts legacy page code, but global settings and progress state no longer need direct control mutation.

## Module Categories

Use `ToolboxModuleDefinition` and `ToolboxModuleCategory` to keep new work classified:

- `Project`: workbench, project cards, chapter management, project import/export.
- `Asset`: asset library, background/audio/character/function material management.
- `Story`: story editor, CSV row state, preview playback, section import/export.
- `Integration`: Unreal sync, data table generation, external process control.
- `Settings`: root path, display preferences, logging, sound, app-wide preferences.
- `Shell`: title bar, navigation, page animation, global progress host, dialogs.

## Migration Priority

1. Shell and settings
   - Done for settings state and project root migration.
   - Continue by extracting navigation state if more pages are added.
2. Global progress
   - Done for progress state.
   - Keep ring geometry and animations in the view layer.
3. Project workbench
   - Move card loading, project selection, project import/export status into `ProjectWorkbenchViewModel`.
   - Keep `GridViewItem` creation in `Views/*Factory`.
4. Asset library
   - Split background, audio, character layers, and functions into child ViewModels.
   - Keep drag/drop event wiring in the view layer, but move ordering and normalization orchestration to ViewModels/Services.
5. Story editor
   - Continue expanding `StoryEditorViewModel`.
   - Avoid one huge ViewModel; use child ViewModels for row navigation, preview slots, audio preview, and function tips.
6. Unreal sync
   - Move selected project, path bindings, validation result, and change plan summary into `UnrealSyncViewModel`.
   - Keep pickers and process confirmation dialogs in the window or a view service.

## Performance Rules

- Do not bind thousands of card controls directly to heavy computed properties.
- Batch visual item creation for large grids and yield between batches.
- Cache images by path where previews are reused.
- Cancel stale loads when the selected asset library or character changes.
- Keep expensive file scans in Services and report progress/cancellation.
- Use ViewModels for state, not for image decoding, hashing, or direct control creation.

## MainWindow Exit Criteria

`MainWindow.xaml.cs` is considered fully migrated only when it contains mostly:

- constructor composition;
- title bar and window placement;
- NavigationView page switching;
- picker/dialog bridges;
- global progress host animation and ring geometry;
- minimal event forwarding to ViewModel commands;
- no CSV, archive, hash, material sync, or domain validation logic.

Until then, every feature change should reduce the legacy surface instead of adding new business logic to it.
