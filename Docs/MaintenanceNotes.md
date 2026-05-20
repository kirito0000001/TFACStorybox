# GalExcleTools Maintenance Notes

This file records the current implementation shape and the rules that should stay unified during later changes. Keep it short enough to read before editing the tool.

## Current Shape

- The app is an unpackaged WinUI 3 desktop tool. `App.xaml.cs` only owns startup and best-effort crash logging to `%AppData%/GalExcleTools/crash.log`.
- Most behavior currently lives in `MainWindow.xaml.cs`; `MainWindow.xaml` contains all major pages and overlay layers.
- The app is file-first. Project, asset library, chapter, function, filter, and sync data are stored as folders, JSON sidecars, CSV files, and imported source files under the workspace root.
- The UI is currently screen-driven rather than MVVM-driven. When adding features, prefer reusing existing helpers and state fields instead of creating a parallel mini-framework.
- UI design core: keep visible buttons sparse, avoid clutter, keep layout geometry and visual style consistent, prefer smooth transitions/animations where practical, and lean on keyboard shortcuts plus hover tips for secondary actions.
- App version text uses semantic versioning: `major.feature.patch`. Current UI label is `1.2.0`; patch fixes increment the last number, grouped user-facing features increment the middle number, and breaking redesigns increment the first.

## Main Feature Areas

- **Workbench and projects**: load cards from workspace folders, create/edit/delete projects, bind one asset library, and open chapter cards.
- **Workbench backups**: project, asset-library, and chapter right-click backup/restore store zip snapshots under `Tools/ProjectBackups`, `Tools/AssetLibraryBackups`, and `Tools/ChapterBackups`, keep only the latest 3 backups per item, show detailed progress while zipping, save a short remark beside each zip, and restore must preserve the matching backup folder while replacing item content.
- **Chapter and story editor**: edit `FStoryStruct` CSV rows, manage sections, preview background/character layers, play BGM/environment sound, and write story functions into `Custom`.
- **Asset library**: manage backgrounds, BGM, environment sounds, sound effects, functions, character filters, and layered character art.
- **Character detail page**: previews layered `DN_Cloth -> FC_Face -> AD_Adorn -> VFX`, edits character metadata, imports/sorts layer files, and stores face/adorn costume scopes.
- **Unreal sync desk**: validates Unreal paths, builds a changed-only sync plan, optionally backs up the Unreal project, writes a manifest plus Python script, launches Unreal command-line import, and confirms results from logs.
- **Settings/logging**: stores app-wide settings in `%AppData%/GalExcleTools/settings.json`, controls helper display, editor text size, and runtime log visibility.

## Unified Tips Rule

The story editor must not create new one-off `InfoBar` patterns.

- Normal short-lived operation tips must go through `ShowStoryStatus(...)`.
- Story function trigger tips must go through `ShowStoryFunctionTriggeredStatus(...)`.
- Tip visuals are created by `CreateStoryTipBar(...)` and animated by `AddStoryTipWithEntrance(...)`.
- Normal operation tips live in `StoryFloatingTipsPanel`, float above the editor, and auto-dismiss after a short time.
- Function trigger tips live in `StoryFunctionTipsPanel`, stay visible for the current row, and are cleared in `LoadStoryRowIntoUi()` when the row changes.
- Function trigger text should be short: `触发函数：xxx`. Do not append long explanation text.
- Trigger-option function cards use category `触发选项` and indicator format `{ChapterSectionCode}-Choice{N}`; for example `M2-04-Choice2`, where `M2` is the chapter and `04` is the zero-based subsection code derived from the editor section selector (`第 5 小节` -> `04`). The story-editor title should display the effective prefixed code such as `WHK-M2-04`. Keep the built-in `创建触发选项` template available in old and new libraries; it should prompt for one or more option-note rows and generate exactly one next current chapter-section choice trigger at fill time. Option remarks are for viewing only, must stay out of story CSV writes, and must be stored chapter-locally in `story.choice-notes.json` rather than asset-library `functions.json`. Keep the story toolbar `查看选项` button conditional on the current row containing choice functions, and remove unused choice notes when the matching function is removed or cleared.
- Character layer columns (`Body/Face/Adorn/Vfx`, including `Talk*`) must be reset to `0` whenever the matching character column is empty, a Chinese display name, or cannot resolve to a valid character card. Apply the same rule during editor save/preview and as an auto-fix phase in chapter repair.
- Hovered story character slots and basic story asset controls support internal clipboards: `Ctrl+C` copies the hovered slot/asset data, and `Ctrl+V` pastes it into the currently hovered compatible target. This must not use or overwrite the OS clipboard. Hover tooltips should simply say `F12打开快捷键提示`, and `F12` opens the shortcut help dialog.
- Do not put transient tips into normal layout columns if they can resize the stage, text box, or background canvas.
- Before finishing any tip-related change, search for `InfoBar`, `ShowStoryStatus`, `ShowStoryFunctionTriggeredStatus`, and `???` to make sure no duplicate style or garbled text was introduced.

## Encoding Rule

Chinese UI text is allowed, but it must be edited safely.

- Keep source and Markdown files as UTF-8.
- Avoid shell-script string replacements for Chinese text unless the encoding is controlled end to end.
- Prefer `apply_patch` or the IDE for literal Chinese UI strings.
- After editing Chinese strings, run a quick search for obvious damage such as `???`, `锟`, or accidental mojibake in the touched area.
- Existing files contain some historical mojibake text. Do not spread it into new code; when touching nearby strings, restore the visible UI text to normal Chinese.

## Story CSV Rule

All story CSV work should use the central story row helpers.

- `StoryCsvColumns` is the source of truth for columns. Keep the Unreal typo `Tesxt`.
- `StoryRow` stores cell values by column name. Use `row.Get(...)` and `row.Set(...)`.
- The first column is internal `Name`; generated CSV writes the first header as `---` and row names as plain numbers.
- Imports should accept compatible old headers such as `Name`, blank, or `---`, then normalize through the shared CSV reader/writer.
- Numeric story asset indexes are zero-based unless a field has an explicit exception.
- `Adorn=0` means no adorn; adorn files start at index `1` for story usage.
- Character names in `Chara1` to `Chara5` should be English character codes. `TalkChar` stores an English code when the typed speaker matches a known character, otherwise it keeps the raw typed text.
- Do not call `WriteStoryRows(...)` directly from editor actions unless a helper is being implemented. Normal story editor persistence should go through `PersistCurrentStoryRowsToFiles(...)`.

## Section CSV Rule

Sections are real chapter CSV files now; do not merge them into a hidden master file.

- The editor presents all section rows as one continuous story while preserving each row's section.
- Section metadata is mirrored in `story.sections.json`, but physical CSV files are the source users see.
- Section 1 uses the chapter code file, for example `WHK-M2-00.csv`.
- Later sections increment the final section segment, for example `WHK-M2-01.csv`, `WHK-M2-02.csv`.
- A multi-section chapter syncs to Unreal under a folder without the final section suffix, for example chapter `WHK-M2-00` syncs under `WHK-M2`.
- If a section CSV has no meaningful row content, it should be deleted on load/save, except for the required first section placeholder.
- Chapter right-click section import must preserve separate sections instead of appending rows into the main CSV.

## Story Editor Save Rule

Navigation and rendering should be cheap.

- Typing in speaker/text boxes uses the delayed save timer, currently about `650ms`.
- Row navigation should load from in-memory `_storyRows`; it should not re-read all CSV files or flash the stage.
- Save only when data changes, a new row is created, a row is deleted, a section changes, or an explicit asset/function action modifies the row.
- When adding a story-row action, update the in-memory row first, call `PersistCurrentStoryRowsToFiles(...)`, refresh only the affected preview/status, and then save progress if needed.
- `LastEditedRowIndex` belongs in `chapter.meta.json`; opening a chapter should restore it safely within the current row count.
- Story editor data edits should create undo snapshots with a short user-operation log label before writing CSV. `Ctrl+Z` and the editor `撤回上一步` button restore story rows, section metadata, current row, and choice-note state; external reloads such as asset-index sync must clear the undo stack.
- Story editor debug mode is UI-only. When enabled, next-row navigation must not auto-create a row at the end of the chapter, and the inline `原地新建` button is hidden.

## Story Function and Media Rule

`Custom` can contain multiple functions separated by `/`.

- Adding a function appends `/{Function}` when `Custom` already has content.
- Removing a function should split only by `/`, let the user choose one existing function, then join the remainder with `/`.
- Built-in jump function cards are templates: `跳转章节` chooses from the current project's chapters and writes `IntoChapter_{ChapterCode}` after removing the project prefix and final section suffix, such as `IntoChapter_M2`; `跳转小节` chooses from the current chapter's section count and writes zero-based two-digit section codes, such as `IntoSegment_06`.
- Built-in BGM control is one compact template card, not separate start/stop cards. Choosing it opens a Start/Stop picker and writes only `BGM_Start` or `BGM_Stop` into `Custom`.
- `BGLerpMode_0/1/2` is persistent story-editor state. When the preview background index actually changes, the floating tip area should show the current background transition mode; repeated refreshes of the same background should not create a new tip.
- Trigger detection uses normalized keys so `BGM_Stop`, `BGMStop`, and similar forms can be compared consistently.
- `BGMStop` has priority over BGM index playback. If the current row has `BGMStop`, pause immediately and do not let a simultaneous BGM index restart playback.
- `BGMStart` clears the suppressed state and allows current-row BGM playback again.
- On load, previous-row navigation, and direct row jumps, rebuild persistent function state by scanning previous rows up to the current row.
- Next-row navigation should not do expensive full persistence checks unless it needs persistent state for correctness.
- BGM and environment sound use dedicated looping `MediaPlayer` instances and stop when leaving the editor.

## Asset Index Rule

Asset ordering is data, so sorting must remap story rows.

- Background reorder remaps `BGindex`.
- BGM reorder remaps `BGM`.
- Environment sound reorder remaps `Scene`.
- Character clothes/faces/adorns remap only rows that reference the affected character.
- Character filters remap `TalkVfx` and `Vfx1` to `Vfx5`.
- Character filter data is normalized on load and reorder: exactly one `空` entry must exist at `VFX00`; extra blank/empty entries are dropped. Adding/deleting filters must update `vfx-filters.json`, and deleting a non-empty filter remaps affected story rows to `VFX00` while shifting later filter indexes through the same progress/report flow as reorder.
- Reorder remapping must show a progress dialog while scanning linked projects and writing CSV files.
- After reorder remapping, show a before/after comparison for changed cells and warn about out-of-range or mismatched data that was not automatically changed.
- Chapter cards expose `修复` for per-chapter index inspection. Automatic repair is conservative: only safe out-of-range numeric indexes are reset to `0`; unknown characters or missing assets are reported for manual review.
- Face and adorn choices must respect the selected costume scope in both the asset-library preview and the story editor. This avoids mismatched face/hair/body combinations.
- VFX/filter cards are index-only toolbox data. Unreal sync must keep indexes aligned but does not create or modify VFX material assets.

## Character Preview Rule

Layered character preview should remain shared in spirit across asset library and story editor.

- Layer order is `DN_Cloth`, `FC_Face`, `AD_Adorn`, then VFX/filter label or placeholder.
- Missing layers should be skipped, not replaced by broken images.
- Hover feedback, right-click layer selection, preview-on-hover in choice dialogs, and keyboard shortcuts should behave the same in character detail and story editor where practical.
- The bottom-left speaker preview is tied to `TalkChar`, `TalkBody`, `TalkFace`, `TalkAdorn`, and `TalkVfx`; it does not have a separate character picker.

## Unreal Sync Rule

Unreal sync is a manual bridge, not a live file watcher.

- The selected target folder must be inside the Unreal project's `Content` directory and must be named `Narrative`.
- Raw assets must be imported through Unreal; copying files into `Content` is not enough to create `.uasset` files.
- Sync writes `Saved/GalExcleTools/gal-sync-manifest.json` and `Saved/GalExcleTools/gal_sync_import.py` under the Unreal project, then launches Unreal with `-ExecutePythonScript`.
- The manifest must be UTF-8 without BOM; the Python side should tolerate BOM with `utf-8-sig`.
- The sync plan should include only changed files unless the user clicks full resync.
- If no source asset, story table, or lustration-data change is detected, do not launch Unreal.
- `DA_LustrationInfor` is a data asset map: string key -> `FLustrationStruct`. It should only be rewritten when the lustration hash changes or full resync is requested. The tool may update name/color/cloth/face/adorn data, but must preserve each existing row's Unreal-side `Vfx` array because VFX materials are configured manually in Unreal.
- Story DataTables use `/Script/GALLibrary.StoryStruct`.
- Asset-library order sync writes four temporary CSV index maps under project `Tools/UnrealAssetIndexTables`, then fills existing DataTables in `ExcelTexts`: `BGIndexMap` uses `/Script/GALLibrary.Texture2DTable` and `Texture2D` references; `BGMap`, `SceneIndexMap`, and `ExsIndexMap` use `/Script/GALLibrary.WaveTable` and `Wave` references. Row names are zero-based numeric indexes matching story CSV values.
- Story tables sync under `ExcelTexts/{ChapterTypeFolder}`. Multi-section chapters get a chapter folder without the final section suffix.
- Sync should replace/update existing target assets, and legacy underscore or old `_小节` names should be treated as cleanup targets where possible.
- Imported `.uasset` files are editor assets, not cooked pak output. Cooking/pak generation should stay a separate future feature.
- Unreal sync bindings are selected by workbench project cards. Each project stores its own Unreal engine, `.uproject`, and target content folder in `project.meta.json`; choosing a card fills the sync settings and setting changes save immediately.
- Normal project backups must exclude nested backup folders, including `ProjectBackups`, `AssetLibraryBackups`, `ChapterBackups`, and `UnrealBackups`, so toolbox backups and Unreal project backups stay separate.

## Documentation Update Rule

When a feature changes one of the shared flows above, update this file in the same change.

- Update this file for new public rules, naming conventions, sync behavior, or shared UI patterns.
- Keep `Docs/Framework.md` as the broader project history/spec. Use this file as the quick maintenance checklist.
- If code and docs disagree, fix both before calling the task done.
