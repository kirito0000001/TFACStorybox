# Toolbox Development Guidelines

This document is the working standard for GalExcleTools-style WinUI toolboxes. Use it when creating or extending any future toolbox, including CrossingVoidZDTool. The goal is a repeatable MVVM structure, consistent UI feel, and a verification workflow that catches runtime problems early.

## 1. Architecture Baseline

Every toolbox follows the same ownership split:

- `ApplicationViewModel`: the root ViewModel. It aggregates modules and cross-page state only. It currently owns `SettingsViewModel` and `GlobalProgressViewModel`; future modules should attach workbench, asset library, story editor, and Unreal sync ViewModels here.
- `Models`: serialized contracts, immutable summaries, result records, progress records, and domain data. Models must not reference WinUI controls.
- `Services`: filesystem work, JSON/CSV parsing, hashing, migration, import/export, backup, Unreal sync, image or audio processing, and other domain operations. Services may report `IProgress<T>` and accept `CancellationToken`, but they must not update controls directly.
- `ViewModels`: screen state, validation state, commands, selected items, user-facing status text, and orchestration between services. ViewModels should inherit `ObservableObject` and expose bindable public properties.
- `Views`: reusable controls, dialog content factories, and UI-only composition. Views can create WinUI elements, but they should not own business rules.
- `Styles` / resource dictionaries: button styles, cards, typography, spacing, icon button behavior, and reusable visual rules.
- `MainWindow`: shell and bridge only. It may own startup wiring, custom title bar, navigation selection, file/folder pickers, `XamlRoot`, page entrance animation, global progress host, and calls into ViewModels or dialog services.

`MainWindow.xaml.cs` must not become the feature implementation file. When a feature needs parsing, scanning, validation, migration, sync rules, export rules, naming rules, or persistent state, create or extend a ViewModel and Service first, then call them from the window.

Every module must have a category: workbench is `Project`, asset library is `Asset`, story editor is `Story`, Unreal sync is `Integration`, global settings is `Settings`, and title bar/navigation/progress/dialog bridges are `Shell`.

## 2. MainWindow Allowed Work

Allowed in `MainWindow`:

- Create the app shell and apply title bar/icon/window placement.
- Host `NavigationView` and switch visible pages through one centralized page-show method.
- Play page entrance animations when a page becomes visible.
- Open WinUI pickers that require the window handle.
- Pass `XamlRoot` to dialogs through a dialog service.
- Show, update, cancel, complete, and hide the global bottom progress bar.
- Bridge UI events to ViewModel commands or async methods.

Not allowed in `MainWindow`:

- Direct file copying, hashing, JSON/CSV parsing, archive generation, Unreal sync rules, asset indexing, or backup logic.
- Long validation blocks that belong in ViewModel or Service.
- Manual control updates for screen state that could be a binding.
- Duplicate helper logic already present in a service.
- One-off dialog or progress styles that bypass shared conventions.

If a new event handler exceeds a small bridge, stop and extract the work before continuing.

## 3. ViewModel Rules

- One main page should have one main ViewModel. Shared panels may have smaller child ViewModels.
- Do not migrate `MainWindow` into a different giant ViewModel. Split state into child ViewModels when it crosses one page responsibility.
- The root ViewModel only aggregates modules, navigation state, and cross-page service entry points. It must not scan files, build cards, or own domain validation.
- ViewModels own bindable state such as path text, selected item, status title/message/severity, busy state, command enabled state, and validation results.
- Use `RelayCommand` or `AsyncRelayCommand` for repeatable actions. Raw click handlers are acceptable only when WinUI infrastructure requires them, such as `FolderPicker` or `ContentDialog`.
- Async commands must guard against duplicate execution unless the feature explicitly supports parallel work.
- ViewModels may orchestrate Services, but the heavy work stays in Services.
- ViewModels should accept services through constructors. Avoid constructing domain services deep inside event handlers.
- Keep status text in ViewModels when the text reflects feature state; keep transient animation text in the global progress host.

## 4. Service Rules

- Services must be deterministic, testable units that can run without a window.
- Any operation that may take noticeable time must accept `CancellationToken`.
- Any operation that may scan, copy, import, export, sync, hash, compress, or delete multiple files must accept `IProgress<T>`.
- Progress records should include at least `Message`, `Percent`, and optional `Detail`. Use `IsIndeterminate` when the total is unknown.
- Service methods should return result records with counts, paths, warnings, and changed item summaries instead of mutating UI state.
- Destructive operations must verify the new state before deleting the old state. For migrations, copy first, verify size/hash, save settings, then delete old data.

## 5. Global Progress Standard

All long-running or potentially long-running operations use the bottom global progress bar. Do not create separate progress dialogs.

Global progress state belongs to `GlobalProgressViewModel`. The window layer keeps only animation, visibility transition, cancel click handling, and ring geometry drawing.

Required progress behavior:

- Start with `ShowGlobalProgress(title, targetName)`.
- Immediately report an initial step, even if the current data set is small.
- Update message, percent, detail, elapsed time, and ring progress during the operation.
- Provide cancellation through the circular progress control when the operation can safely stop.
- Complete with a clear success/failure/cancel message.
- Hide after a short delay so the user can see the result.

Examples that must use global progress:

- Project root migration.
- Folder backup/export/import/restore.
- Asset index sync and repair.
- Unreal sync.
- Image/audio batch processing.
- Any multi-file scan or hash verification.

## 6. Dialogs, Tips, And Shortcuts

- Tips and help content use `ContentDialog` through the dialog service.
- Help dialogs should use a `ScrollViewer` when content may exceed the visible area.
- Dialogs should support common shortcuts: `Enter` confirms the default action, `Esc` closes or cancels, and right-click can close non-destructive tip dialogs when that pattern already exists in the toolbox.
- Small page-level help buttons use the shared circular help icon style and tooltip.
- Do not replace detailed tips with a large persistent `InfoBar`. `InfoBar` is for current status, success, warning, or error.
- Every small utility page should have keyboard-friendly behavior. Document important shortcuts near the implementation or in the page ViewModel, not as noisy visible teaching text.

## 7. Navigation And Page Motion

- Main pages are switched through one centralized method that hides all inactive pages and shows only the target page.
- Every main page transition plays the same entrance animation: slide from left to resting position and fade to full opacity.
- Current baseline: offset `-96`, duration about `280ms`, opacity `0.82 -> 1`, `CubicEase` with `EaseOut`.
- Do not create unrelated page animation styles unless there is a strong product reason and the rule is added here.

## 8. UI Feel And Button Rules

- The shell uses a tall custom title bar, compact left navigation, Mica backdrop, restrained panels, and consistent spacing.
- Use shared styles such as `PanelBorderStyle`, `IconToolButtonStyle`, and `HelpIconButtonStyle`.
- Prefer icon buttons with tooltips for compact commands.
- Use text buttons for clear textual actions such as selecting a folder or closing a dialog.
- Do not place page sections inside decorative nested cards. Use panels for real grouped controls and repeated item cards only where they represent individual items.
- Text must fit inside controls at desktop and narrow widths. Avoid oversized headings inside compact panels.
- Keep the interface quiet and operational. These are production tools, not marketing pages.

## 8.1 Performance Rules

- Do not create very large visual lists in a single UI pass. Add items in batches and yield to the UI thread between batches.
- Cache image previews by path and cancel stale loads when the selected asset library or character changes.
- ViewModels must not decode images, hash files, compress archives, or scan CSV trees. Put that work in Services.
- Bindable property getters must stay lightweight; never scan the filesystem or run expensive calculations inside a getter.
- Drag sorting, batch import, name normalization, and sync checks must support cancellation and progress.
- Page switches should not rebuild stable data every time. Reuse caches and refresh only when needed.

## 9. Project Root Pattern

For every toolbox with a shared project folder:

- Keep settings in an `AppSettingsService`.
- Use a stable default path on `D:\`, for example `D:\CrossingVoidZDProject`.
- When the user chooses a location, treat it as a parent directory and append the fixed project folder name.
- If the target equals the current root, show an informational status and do nothing.
- If the target is inside the current root, block the migration.
- During migration, copy directories/files, verify every copied file by size/hash, update settings, ensure the new root exists, then delete the old root.
- Report progress for every phase, even if the current project is small.

## 10. Encoding And File Editing

- Keep `.cs`, `.xaml`, `.json`, and `.md` files UTF-8.
- Avoid PowerShell redirection for source edits because it can corrupt Chinese text or XAML escaping.
- Use small patches. Inspect nearby lines before editing XAML or Chinese strings.
- After editing Chinese text, re-open the file as UTF-8 and check for mojibake.
- Preserve unrelated user changes in the working tree.

## 11. Build, Run, And Notify Workflow

After any C#, XAML, project file, resource, or packaging change:

1. Build once.
   - For GalExcleTools: `powershell -NoProfile -ExecutionPolicy Bypass -File .\Scripts\Test-SourceHealth.ps1 -Build`
   - For a standard WinUI toolbox: `dotnet build <Project>.csproj --configuration Release --runtime win-x64 -p:Platform=x64`
2. After the build succeeds, immediately short-start the app from the build output. This catches runtime XAML, resource, icon, title bar, and startup wiring issues.
3. If a packaging task is requested, run the packaging script after source health passes, then inspect the package output.
4. Send a step-completion email notification with `C:\Users\liuyu\Documents\CodexTools\notify-step.ps1` after a meaningful step or final verification. If email credentials are unavailable, report that the notification could not be sent and continue.

Do not say the work is finished until build and short-start have been attempted or the exact blocker is reported.

## 12. Git Workflow

- Local commits are allowed when the change is coherent.
- Do not push to GitHub unless the user explicitly asks. For this toolbox family, pushing is usually reserved for a completed version checkpoint.
- Never revert unrelated user changes.
- Before committing, check `git status --short` and make sure only intended files are staged.

## 13. Reuse Checklist For New Toolboxes

When starting another toolbox, copy the pattern before adding features:

- App settings service with project root default.
- Dialog service and dialog content factory.
- Observable ViewModel base and command helpers.
- Shared button/panel/typography styles.
- Custom title bar with app icon and version.
- Compact navigation with page transition animation.
- Global bottom progress host with cancellation.
- Settings page copied from the GalExcleTools pattern, then trimmed to the new domain.
- Internal documentation file describing any project-specific deviations.
- Build, short-start, and email notification workflow.
- A `Docs/ToolboxArchitectureMigration.md` or equivalent document that records module categories and the continuing migration path.

## 14. Review Standard

Before marking work complete, check:

- Is `MainWindow` only shell and bridge work?
- Did new feature logic land in ViewModel/Service?
- Are long operations cancellable and progress-reporting?
- Are tips/dialogs using shared dialog patterns and shortcuts?
- Are page transitions consistent?
- Did the app build?
- Did the app short-start after build?
- Was the email notification attempted?
- Is Git local-only unless the user asked to push?

## 15. MainWindow Exit Standard

GalExcleTools is now in the "framework migrated, feature pages continuing to move out" phase. Every future feature change should reduce the business surface in `MainWindow.xaml.cs`.

In the final shape, `MainWindow` should contain mostly:

- constructor composition for services and ViewModels;
- title bar, icon, and initial window placement;
- navigation page switching and entrance animation;
- file/folder pickers;
- `ContentDialog` `XamlRoot` bridge;
- global progress animation, cancel click, and ring geometry;
- small event forwarding into ViewModel commands.

Move these out over time:

- CSV row logic, chapter import/export, index repair;
- asset library scanning, audio normalization, image conversion, character layer rules;
- Unreal sync validation, change plans, external process rules;
- project and asset library create/rename/delete/backup/restore orchestration;
- manual control updates for bindable state.
