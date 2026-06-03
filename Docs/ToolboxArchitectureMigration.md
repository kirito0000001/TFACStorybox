# Toolbox Architecture Migration Map

This file tracks the MVVM migration shape for GalExcleTools. It is intentionally practical: use it before moving more code out of `MainWindow.xaml.cs`.

## Current Frame

The toolbox now has a root application ViewModel:

- `ApplicationViewModel`
  - owns module definitions and selected module state.
  - aggregates cross-cutting and feature module ViewModels.
- `SettingsViewModel`
  - owns global settings, project root state, settings persistence, project root migration orchestration, log filtering state, and user-facing settings status.
- `GlobalProgressViewModel`
  - owns global progress text, percent, elapsed text, detail, and indeterminate state.
- `ProjectWorkbenchViewModel`
  - owns selected project, chapter/text-tool state, localization state, and project voice map state.
- `AssetLibraryViewModel`
  - owns selected asset library, selected character, viewer state, current audio state, normalization flags, and selected character layer paths.
- `StoryEditorViewModel`
  - owns the open chapter, row collection, section map, undo state, row text, asset status text, preview cache, media playback state, and shortcut hover/clipboard state.
- `UnrealSyncViewModel`
  - owns Unreal sync busy/request flags and is the landing point for future validation/result bindings.

`MainWindow.xaml.cs` is now the shell constructor and startup bridge. Feature UI event code is split into focused partial files such as `MainWindow.Workbench.*.cs`, `MainWindow.Story.*.cs`, `MainWindow.AssetLibrary.*.cs`, and `MainWindow.UnrealSync.*.cs`. These partials may create controls and call pickers/dialogs, but persistent screen state belongs to ViewModels and domain rules belong to Services.

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
   - Done for settings state, startup auxiliary-display synchronization, and project root migration.
   - Continue by extracting navigation state if more pages are added.
2. Global progress
   - Done for progress state.
   - Keep ring geometry and animations in the view layer.
3. Project workbench
   - Core state has moved into `ProjectWorkbenchViewModel`.
   - Continue moving card factories into `Views/*Factory` when a card layout changes.
   - Keep `GridViewItem` creation in `Views/*Factory`.
4. Asset library
   - Core selection/viewer/normalization state has moved into `AssetLibraryViewModel`.
   - Feature code is split by creation, loading, drag/drop, editing, viewers, and character detail.
   - Continue splitting background, audio, character layers, and functions into child ViewModels.
   - Keep drag/drop event wiring in the view layer, but move ordering and normalization orchestration to ViewModels/Services.
5. Story editor
   - Core row, section, undo, preview, clipboard, hover, and media state has moved into `StoryEditorViewModel`.
   - Feature code is split by sections, functions, interaction, media, preview, assets, and sync.
   - Avoid one huge ViewModel; use child ViewModels for row navigation, preview slots, audio preview, and function tips.
6. Unreal sync
   - Request/loading flags have moved into `UnrealSyncViewModel`.
   - Feature code is split by page bridge, dialogs, process helpers, and runner logic.
   - Continue moving selected project, path bindings, validation result, and change plan summary into `UnrealSyncViewModel`.
   - Keep pickers and process confirmation dialogs in the window or a view service.

## Current MainWindow Partial Layout

- `MainWindow.xaml.cs`: composition root, settings load/apply, app icon/title bar, log routing, cancellation hosts, and shared batched UI loading.
- `MainWindow.Settings.cs`: project root picker bridge, settings save, and shell navigation selection.
- `MainWindow.Navigation.cs`: page switching, help dialogs, and entrance animation.
- `MainWindow.Progress.cs`: global progress host, cancellation token, timer text, speed text, and ring geometry.
- `MainWindow.Dialogs.cs`: dialog bridge and project/card editing dialogs that still require `XamlRoot`.
- `MainWindow.Workbench*.cs`: project/asset-library cards, project detail, chapters, text/voice/localization panels, import/backup/restore bridges.
- `MainWindow.Story*.cs`: story editor row bridge, sections, functions, keyboard shortcuts, media, preview, asset choices, and index sync bridge.
- `MainWindow.AssetLibrary*.cs`: asset library creation, lazy loading, drag/drop, editing, viewers, character detail, utility conversion.
- `MainWindow.UnrealSync*.cs`: Unreal sync page bridge, confirmation dialogs, process helpers, validation and runner bridge.

When adding or editing a feature, place it in the narrowest existing partial file. If a partial grows into unrelated responsibilities, split it before adding new behavior.

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
