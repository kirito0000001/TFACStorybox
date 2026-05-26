# Codex Internal Notes

This file is for future Codex and maintainer work only. It records rules that
should guide edits but should not be treated as user-facing product copy.

## Current Baseline

- Current app version is `2.1.0`.
- The MVVM/service migration is considered complete enough for normal patch work.
- Do not revive the old migration log; patch concrete bugs and keep public release notes in `README.md`.
- The app is an unpackaged WinUI 3 desktop tool.
- `App.xaml.cs` owns startup and best-effort crash logging to `%AppData%/GalExcleTools/crash.log`.
- `MainWindow.xaml` and `MainWindow.xaml.cs` still exist as the shell and UI bridge.
- Business logic is now spread across `Models`, `Services`, `ViewModels`, and `Views`.

## Version Rule

- Version format is `major.feature.patch`.
- Patch fixes increment the last number, for example `2.0.1`.
- Same-day related patch fixes can be grouped under one patch version when the user requests it.
- Grouped user-facing features increment the middle number, for example `2.1.0`.
- Breaking redesigns increment the first number, for example `3.0.0`.
- Keep version text aligned in `README.md`, `GalExcleTools.csproj`, and visible XAML labels.
- For packaged builds, keep `Package.appxmanifest` identity version aligned too.

## Documentation Rule

- `README.md` is the public/user-facing source for web integration.
- Put release notes at the top of `README.md`.
- Put user-facing feature summaries, workflow rules, style rules, Unreal setup, packaging, and development commands in `README.md`.
- Put internal maintenance constraints only in this file.
- If code behavior changes a public rule, update `README.md` in the same patch.
- If code behavior changes an internal mechanism, update this file in the same patch.

## Step Completion Email Rule

- After completing each meaningful work step, send an email notification.
- Use the shared local script at `C:\Users\liuyu\Documents\CodexTools\notify-step.ps1`.
- This script was generalized from `D:\UnrealMap\HertzGames\scripts\notify-step.ps1`.
- Preferred environment variables are `CODEX_NOTIFY_TO`, `CODEX_NOTIFY_SMTP_USER`, `CODEX_NOTIFY_SMTP_PASSWORD`, and optionally `CODEX_NOTIFY_SMTP_HOST` / `CODEX_NOTIFY_SMTP_PORT`.
- The shared script also accepts the older `TFAC_NOTIFY_*` variables for compatibility.
- SMTP passwords and mailbox authorization codes must stay in local environment variables only. Never write them into code, docs, logs, Docker files, or commits.
- Use a concise title and summary that name what was completed, what was verified, and any residual blocker.
- If credentials are missing or SMTP fails, do not block the code/documentation task; report that the email notification could not be sent.

## Encoding Rule

- Keep source and Markdown files as UTF-8.
- Chinese UI text is allowed.
- Do not use PowerShell redirection (`>`, `>>`) to rewrite XAML, C# or Markdown files. It can silently write UTF-16 or corrupt Chinese text.
- Do not use `Get-Content` / `Set-Content` for Chinese source edits unless the encoding is explicitly controlled and immediately verified.
- Avoid shell-script string replacements for Chinese text unless the encoding is controlled end to end.
- Prefer literal patches for Chinese strings.
- If a file must be read by script, use explicit strict UTF-8 APIs such as `[System.IO.File]::ReadAllText(path, [System.Text.Encoding]::UTF8)`.
- After editing Chinese strings, search touched areas for `???`, `锟`, and obvious mojibake.
- Some historical mojibake may exist. Do not spread it; restore nearby visible strings when touching them.

## XAML Safety Rule

- Treat `MainWindow.xaml` as a high-risk file: small, literal patches only.
- Before editing XAML, inspect the nearby block from the current file and prefer reusing existing controls/styles.
- Do not perform broad regex rewrites over `MainWindow.xaml`.
- After every XAML edit, run `powershell -NoProfile -ExecutionPolicy Bypass -File .\Scripts\Test-SourceHealth.ps1 -Build`.
- The source-health script must pass before reporting a XAML task complete. It checks strict UTF-8, XML well-formedness, likely mojibake in XAML/C# and Release build.
- If the script flags mojibake in a touched area, restore from the last clean source and reapply the minimal patch.

## Packaging Rule

- Use `Scripts/Package-App.ps1` for release packaging.
- Packaging should publish a direct runnable folder under `D:\DabaoV`.
- Do not change packaging back to a compressed package, installer exe, or self-extracting exe unless explicitly requested.
- Keep `PublishSingleFile=false`.
- Keep unpackaged WinUI settings: `WindowsPackageType=None` and `WindowsAppSDKSelfContained=true`.
- After packaging changes, run a real test publish and verify the output contains the app `.pri`, `App.xbf`, and `MainWindow.xbf`.

## Shared UI Rule

- Long-running work goes through the global bottom progress bar.
- Do not add new progress `ContentDialog` instances for backup/export, restore, import, index sync, chapter repair, migration, or Unreal sync.
- Confirmation dialogs should treat `Enter` as confirm.
- Confirmation, remark, and simple edit dialogs should treat `Esc` as cancel.
- Right-clicking inside lightweight edit/remark dialogs should cancel or close when there is no specific right-click action.
- Prefer shared helpers for simple text input, confirm/cancel prompts, backup-remark prompts, cancel-current-operation prompts, shortcut help, cards, tiles, and dialog content.
- Icon-only buttons must always have a tooltip.
- Help/tips icons should use the shared compact help icon button style instead of raw `?` text buttons.
- Image secondary viewer pages should support `Esc` exit, `Left/Right`, `A/D`, or numpad `4/6` previous/next switching where adjacent images exist, and right-click exit.
- Character detail's full layered preview should open a composite image viewer when clicked; the viewer should keep character-layer switching shortcuts active.
- Character image layer cards for clothes, faces, and adorns should open the image viewer when the whole card is clicked. Do not rely only on nested image/content click hit testing.

## Story Tips Rule

- Do not create one-off Story `InfoBar` patterns.
- Normal short-lived operation tips must go through `ShowStoryStatus(...)`.
- Story function trigger tips must go through `ShowStoryFunctionTriggeredStatus(...)`.
- Tip visuals are created by `CreateStoryTipBar(...)` and animated by `AddStoryTipWithEntrance(...)`.
- Normal operation tips live in `StoryFloatingTipsPanel` and auto-dismiss.
- Function trigger tips live in `StoryFunctionTipsPanel`, stay visible for the current row, and clear in `LoadStoryRowIntoUi()` when the row changes.
- Function trigger text should stay short, such as `触发函数：xxx`.
- Do not put transient tips into normal layout columns if they can resize the stage, text box, or background canvas.
- Before finishing tip-related changes, search for `InfoBar`, `ShowStoryStatus`, `ShowStoryFunctionTriggeredStatus`, and `???`.

## Story CSV Rule

- `StoryCsvColumns` is the source of truth for columns.
- Keep the Unreal typo `Tesxt`.
- `StoryRow` stores cell values by column name. Use `row.Get(...)` and `row.Set(...)`.
- The first column is internal `Name`; generated CSV writes the first header as `---`.
- Imports should accept compatible old headers such as `Name`, blank, or `---`.
- Do not call `WriteStoryRows(...)` directly from editor actions unless implementing a helper.
- Normal story editor persistence should go through `PersistCurrentStoryRowsToFiles(...)`.
- Editor actions should update the in-memory row first, persist through the shared path, refresh only affected preview/status, and save progress if needed.
- Data edits should create undo snapshots with short user-operation labels before writing CSV.
- External reloads such as asset-index sync must clear the undo stack.

## Section CSV Rule

- Sections are real chapter CSV files, not a hidden master file.
- The editor presents all section rows as one continuous story while preserving each row's section.
- Section metadata is mirrored in `story.sections.json`, but physical CSV files are the user-visible source.
- Section 1 uses the chapter code file, for example `WHK-M2-00.csv`.
- Later sections increment the final section segment, for example `WHK-M2-01.csv`.
- Multi-section chapters sync to Unreal under a folder without the final section suffix, for example `WHK-M2`.
- Empty section CSV files should be deleted on load/save except for the required first section placeholder.
- Chapter right-click section import must preserve separate sections instead of appending rows into the main CSV.

## Story Function Rule

- `Custom` can contain multiple functions separated by `/`.
- Adding a function appends `/{Function}` when `Custom` already has content.
- Removing a function should split only by `/`, let the user choose one existing function, then join the remainder with `/`.
- Built-in jump function cards are templates.
- `跳转章节` writes `IntoChapter_{ChapterCode}` after removing the project prefix and final section suffix.
- `跳转小节` writes zero-based two-digit section codes, such as `IntoSegment_06`.
- Built-in BGM control is one compact template card and writes only `BGM_Start` or `BGM_Stop`.
- `BGM_Stop` has priority over BGM index playback.
- `BGM_Start` clears the suppressed state and allows current-row BGM playback.
- Trigger-option function cards use category `触发选项` and indicator format `{ChapterSectionCode}-Choice{N}`.
- Option remarks are for viewing only and must stay in chapter-local `story.choice-notes.json`.

## Asset Index Rule

- Asset ordering is data, so sorting must remap story rows.
- Background reorder remaps `BGindex`.
- BGM reorder remaps `BGM`.
- Environment sound reorder remaps `Scene`.
- Character clothes/faces/adorns remap only rows that reference the affected character.
- Character filters remap `TalkVfx` and `Vfx1` to `Vfx5`.
- `Adorn=0` means no adorn; adorn files start at story index `1`.
- Character filter data must normalize to exactly one `空` entry at `VFX00`.
- Reorder remapping must show progress while scanning linked projects and writing CSV files.
- After reorder remapping, show before/after changes and warn about out-of-range or mismatched data that was not automatically changed.
- Chapter card `修复` is conservative: safe out-of-range numeric indexes reset to `0`; unknown characters or missing assets are warnings.

## Replace Asset Rule

- User-importable file assets should expose a right-click `替换素材` action.
- Replacement must open a single-file picker filtered to the same supported source types as import.
- Replacement keeps the original normalized target file name and overwrites file contents only, so story indexes and Unreal references remain stable.
- Background replacement should accept supported image sources and write the target as PNG through the same conversion path as background import.
- Audio replacement should accept `.wav` and delete ignored sidecars such as `.pkf` after replacement.
- Character image layer replacement should accept supported image sources and preserve layer filename, scope metadata, and order.
- After replacement, refresh the relevant card list, preview/viewer surface, asset-library edited timestamp, and delayed workspace refresh.

## Audio Asset Rule

- All audio categories should load and import `.wav` files only.
- Adobe Audition can generate `.pkf` peak metadata beside audio files.
- Treat `.pkf` files as disposable sidecars for every audio category, including music, ambient sound, sound effects, and future voice categories.
- Delete `.pkf` sidecars when loading or normalizing an audio category.
- Do not include `.pkf` files in audio counts, cards, story indexes, backups intended as user audio data, or Unreal sync plans.

## Character Preview Rule

- Layer order is `DN_Cloth`, `FC_Face`, `AD_Adorn`, then VFX/filter label or placeholder.
- Missing layers should be skipped, not replaced by broken images.
- Face and adorn choices must respect selected costume scope in both asset-library preview and story editor.
- The bottom-left speaker preview is tied to `TalkChar`, `TalkBody`, `TalkFace`, `TalkAdorn`, and `TalkVfx`.
- Character layer columns must reset to `0` whenever the matching character column is empty, a Chinese display name, or cannot resolve to a valid character card.
- Apply the same reset rule during editor save/preview and chapter repair.
- Portrait previews are optional per asset library via `AssetLibraryMeta.IsPortraitPreviewEnabled`.
- When portrait previews are enabled, cloth, face, and adorn layers must have explicit preview mappings in `Log_Preview/portrait-preview.meta.json`.
- Portrait preview mapping keys are the stable layer codes `DN00`, `FC00`, and `AD00`; never key them by the full layer filename or remark text.
- Portrait preview files are named `Preview-<CharacterCode>-<LayerCode>.<ext>` so changing a layer remark does not break the mapping or create a stale missing-preview warning.
- Portrait preview source files are copied into each character's local `Log_Preview` folder and synced to `Narrative/Lustration/<CharacterCode>/Log_Preview`.

## Unreal Sync Rule

- Unreal sync is a manual bridge, not a live file watcher.
- The selected target folder must be inside the Unreal project's `Content` directory and named `Narrative`.
- Raw assets must be imported through Unreal; copying files into `Content` is not enough to create `.uasset` files.
- Sync writes `Saved/GalExcleTools/gal-sync-manifest.json` and `Saved/GalExcleTools/gal_sync_import.py`.
- The manifest must be UTF-8 without BOM; Python should tolerate BOM with `utf-8-sig`.
- The sync plan should include only changed files unless the user clicks full resync.
- Raw asset diff detection compares source file `LastWriteTimeUtc` with the existing `.uasset` `LastWriteTimeUtc` plus a one-second tolerance. Same-name content changes are detected when the source file timestamp becomes newer than the target asset.
- If no source asset, story table, or lustration-data change is detected, do not launch Unreal.
- Unreal startup can take minutes before a visible editor window appears because the command process may load the project, initialize plugins, scan assets, and execute Python first. Progress text should keep reporting the current stage, while elapsed time stays in the progress panel's right-side timer.
- Unreal sync cancellation must pass the global progress cancellation token into backup and Unreal command execution. If the Unreal command process has started, cancellation should try to kill the process tree.
- `DA_LustrationInfor` should only be rewritten when the lustration hash changes or full resync is requested.
- `DA_Portraits` uses the same `Infor` map shape as `DA_LustrationInfor`, but its Cloth/Face/Adorn arrays point to `Log_Preview` textures and are updated only when portrait preview is enabled.
- Preserve each existing Unreal-side `Vfx` array because VFX materials are configured manually in Unreal.
- Story DataTables use `/Script/GALLibrary.StoryStruct`.
- Sync should prune extra `.uasset` files in tool-owned Narrative asset folders so target background, audio, story table, and imported lustration texture assets mirror the current toolbox source data.
- When portrait preview is enabled, sync should also prune extra managed `Log_Preview` `.uasset` files and keep `DA_Portraits` in the expected root asset set.
- Do not prune Unreal-side `Vfx` arrays or character filter/index data inside `DA_LustrationInfor`; those are role data asset internals and may be maintained manually in Unreal.
- Imported `.uasset` files are editor assets, not cooked pak output.

## Portrait Preview UI Rule

- Story editor action buttons that are not meaningful in the current row should be hidden with `Visibility.Collapsed`, not left visible as disabled controls.
- If a button's visibility depends on row data, update both the visibility property and command `CanExecute` from the same ViewModel state during `UpdateStoryToolbarCurrentInfo`.
- Project text voice and localization use project-detail entry cards; do not embed their full tables directly in the project detail page.
- Opening a voice/localization card should show `ProjectTextToolPage`, with source text read from chapter section CSV files; the source text column is read-only and must not write back to Story CSV.
- Project text rows must be loaded through `StorySessionService.LoadRowsFromSectionFiles(...)`, the same aggregation path as the story editor.
- Project text tool tables use a fixed two-column split; each cell owns its horizontal scrolling so users can compare source text and voice/localization values without moving the whole table sideways.
- Project text voice selection copies wav files into project `Voice/<ChapterCode>/` and renames the copied file as `Vo-<rowIndex>-<remark>.wav`; the row-index digit width is based on the chapter text row count, and changing the remark must rename the managed wav file.
- Unreal sync for project text voice only imports wav files into `Narrative/Voice`; do not generate a global `ExcelTexts/VoiceMap` DataTable or per-section voice DataTables unless the integration design changes again.
- Project text statistics belong in `ProjectTextDataService`; keep `MainWindow` as the caller/display bridge.
- File-system statistics and byte formatting belong in `FileSystemUtility`; do not add page-local size formatters.
- When adding a new page to the page-visibility switch blocks, check the page's own open method so it does not set itself visible and then collapse itself later in the same method.
- Project text voice mappings are stored in project `Tools/story.voice-map.json`; localization mappings are stored in project `Tools/story.localization.json`.
- Text mapping row ids are `ChapterCode#Section#RowName`, so all section CSV files can be merged into one table while preserving stable row identity.
- Put asset library metadata as the first section on the asset library detail page.
- When portrait preview is enabled, character cards and Cloth/Face/Adorn layer cards must show a bottom-right yellow warning badge when their preview mapping is missing.
- Clicking the warning badge should show a user-visible warning message, not only write to the log.

## Service Map

- Settings and root path: `AppSettingsService`, `ProjectRootMigrationService`.
- Project, asset library, chapter metadata: `ProjectWorkspaceService`.
- Backups and zip restore/export: `FolderBackupService`.
- Backgrounds: `BackgroundImageService`.
- Audio: `AudioAssetService`.
- Character folders and metadata: `CharacterWorkspaceService`.
- Character layer files and scopes: `CharacterLayerAssetService`.
- Character filters: `CharacterFilterService`.
- Story CSV and sections: `StoryCsvService`, `StoryStateService`, `StorySessionService`.
- Story editor row operations: `StoryEditorService`.
- Story functions and dialogs: `StoryFunctionService`, `StoryDialogService`, `FunctionDialogService`.
- Story index sync and repair: `StoryAssetIndexSyncService`, `ChapterRepairService`.
- Unreal sync: `UnrealSyncService`.
- Dialogs, shortcuts, sound: `WinUiDialogService`, `ShortcutService`, `UiSoundService`.

## Git and Verification Rule

- Worktree may contain user changes. Do not revert changes you did not make.
- Prefer focused patches and Release builds after code changes.
- Recommended build:

```powershell
dotnet build GalExcleTools.csproj `
  --configuration Release `
  --runtime win-x64 `
  -p:Platform=x64 `
  -p:WindowsPackageType=None `
  -p:WindowsAppSDKSelfContained=true
```

- If a user asks for a git push, treat it as requiring a successful remote push.
- If push fails through `127.0.0.1:7890`, retry the push with proxy disabled for that command.
