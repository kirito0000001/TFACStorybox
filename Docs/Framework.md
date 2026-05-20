# GalExcleTools WinUI Framework

For the current maintenance checklist and unified mechanism rules, read
`Docs/MaintenanceNotes.md` first. That file records the shared flows that should
not fork into multiple implementations, especially story-editor tips, CSV
sections, story persistence, asset-index remapping, and Unreal sync.

## Goal

GalExcleTools is a visual novel data authoring tool for Unreal Engine. The app edits project files directly, keeps indexes consistent with the asset library, and exports story tables as CSV files that match Unreal `FTableRowBase` structs.

## UI Layers

1. Global shell
   - Project switcher
   - Create project
   - Quick backup
   - CSV export
   - Global settings

2. Project workspace
   - Project tree
   - Storylines
   - Chapters
   - Story CSV files
   - Asset library
   - Project settings
   - Backups
   - Project cards are loaded from folders under the workspace root.
   - The final card is a create-project entry.
   - A project must reference one existing asset library.
   - Project metadata is stored separately from asset library metadata and includes project display name, English code, target asset library, and last-opened time.
   - Project cards support right-click rename, delete, and changing the target asset library.
   - Opening a project card enters a secondary tab-style project detail page.
   - Project detail pages list chapter cards. A chapter is a story chapter with a Chinese display name and English code.
   - Chapter creation asks for a Chinese display name and chapter type. The app generates the chapter code using the project English code as a prefix.
   - Project, asset-library, and chapter cards support right-click backup and restore. Backups are folder zip snapshots stored under `Tools/ProjectBackups`, `Tools/AssetLibraryBackups`, and `Tools/ChapterBackups`; each backup can save a remark, shows detailed zip progress, only the latest 3 are kept per item, excludes nested backup folders, and restore opens a selection dialog before replacing content.
   - Chapter type code conventions:
     - Main Thread: `{ProjectCode}-M{MainIndex}-00`
     - Interlude: `{ProjectCode}-M{MainIndex}&L{InterludeIndex}-00`
     - Simulation: `{ProjectCode}-ST&{CustomCode}`
     - Event Activity: `{ProjectCode}-EA-{CustomCode}`
     - World Dialog: `{ProjectCode}-W1-{CustomCode}`
     - Minecraft NPC Dialog: `{ProjectCode}-MI-{CustomCode}`

3. Story editor
   - Main table editor
   - Row inspector
   - Index validation
   - Asset lookup
   - Unreal CSV export preview
   - Opening a chapter card enters the story editor page.
   - A chapter story file is stored in the chapter folder as `{ChapterCode}.csv`. Older `*.story.csv` files should be migrated to the shorter name when opened.
   - Story CSV rows follow the Unreal `FStoryStruct` field names, including the existing `Tesxt` spelling.
   - The first CSV column is the Unreal row-name column. The tool stores it internally as `Name`, accepts `---`, blank, or `Name` as the imported header, and writes it back as `---`.
   - Row names are normalized to plain numbers (`1`, `2`, `3`, ...), so Unreal can use them as stable table row names.
   - Text and speaker edits use delayed saving, currently about `650ms`, so typing does not rewrite the CSV on every key press.
   - `Next sentence` creates a new CSV row when the current row is the last row. The new row copies the previous row and clears only `Tesxt`.
- The current subsection is selected in the editor's lower-right controls. New rows copy the previous row's subsection.
- Subsection data is tool-only metadata stored beside the chapter as `story.sections.json`; it is not written into the Unreal story CSV columns.
- Chapter cards support right-click `导入小节`, which imports one or more compatible story CSV files into the existing chapter and records their subsection numbers. If files named like `WHK-M2-01.csv` are manually placed in the chapter folder, opening the chapter imports them as subsections and then removes the loose files.
- `Previous sentence`, `Next sentence`, and `Delete sentence` are the primary row navigation controls. Standalone export is not shown in the current editor milestone.
   - Background, BGM, and character-layer indexes are 0-based in story CSV rows. `0` means the first asset in the normalized asset order.
   - Entering the story editor automatically plays the current row BGM when the bound asset library has a matching music index.
   - Sentence navigation refreshes BGM and environment sound from the active row. If the resolved file is already playing, playback continues instead of restarting.
   - Leaving the story editor stops story BGM and environment sound.
   - The story editor uses a dedicated right-side settings column for chapter settings and asset actions, including background, BGM, and show/hide toggles for character slots 4 and 5. The compact rail stays visible and expanding the pane resizes the editor content instead of overlaying it.
   - Character slots 1-5 are laid out on a half-column grid, so 3, 4, or 5 visible slots stay centered across the stage. Slots 4 and 5 are hidden by default.
   - Character slot preview uses the same layer order as the asset library character preview: `DN_Cloth`, `FC_Face`, `AD_Adorn`, `VFX`.
   - BGM playback loops by default in the story editor.
   - Character slots show only their slot number by default. Hovering over a slot gives visual feedback.
   - Character slots support a right-click menu for `Character`, `Costume`, `Face`, `Adorn`, and `Filter`; filter is currently a reserved placeholder until the asset library filter workflow is implemented.
   - The sidebar includes `Show full character art`. It defaults off, so character art is cropped/scaled toward an upper-body view. When enabled, the full layered character art is shown.
   - The story editor keeps an empty keyboard handling surface reserved for later numpad-driven character-layer switching.

4. Asset library
   - Backgrounds
   - Character art
   - Music
   - Ambience
   - Sound effects
   - Functions
   - Character filters
   - Asset library cards are loaded from folders with `Tools/asset-library.meta.json`.
   - The final card is a create-asset-library entry.
   - Asset libraries should be created before projects, because story tables will use asset indexes from the selected library.
   - Opening an asset library shows collapsible category sections mapped to real folders.
   - Asset library cards support right-click rename and delete. Renaming updates project references; deleting clears references from projects that used that library.

5. Settings
   - Workspace root path
   - Workspace root folder picker: user chooses a parent folder, and the app uses `{SelectedFolder}/GalExcelProject`
   - Workspace root migration: copy all files from the old root to the new root, verify copied file hashes, then delete the old root
   - Backup prefix
   - Auto-save behavior
   - Export encoding and delimiter
   - Naming rules

## Tips Rule

Long tips should be attached to feature section titles, not page titles. Page titles only identify the current page.

- Use a small help button beside a feature title for longer documentation-style explanations.
- Use hover tooltips only for short interaction hints on buttons, inputs, and compact controls.
- Do not put a documentation help button beside page-level titles such as `整体设置`.
- Feature blocks should prefer collapsible sections with an arrow indicator. The feature title sits in the section header, and the long-tip help button sits after that title.

## Page Scroll Rule

Pages made from multiple collapsible feature sections should scroll as one whole page.

- Each feature section keeps its own expand/collapse behavior.
- Do not give each section an independent vertical scrollbar.
- Put the vertical scrollbar on the page container so expanding one section can naturally push later sections downward while the user still reaches them by scrolling the full page.

## Auxiliary Display Rule

Auxiliary display settings control UI-only helpers. They should not change project data or asset files.

Settings currently control:

- Show or hide workspace path/status bars.
- Enable or disable log output globally.
- Output user operations, such as creating projects, opening libraries, importing images, sorting assets, editing remarks, and changing the workspace root.
- Output warnings for risky or non-standard operations.
- Output errors with the failure reason.
- Asset library whole-page scroll speed, defaulting to `1.5x` the base wheel distance.

The bottom output panel is a global runtime log. It keeps page switches visible and is meant for progress, program triggers, user operations, warnings, and errors.

The user-operation log should be kept structured enough to become a foundation for future undo support.

## Runtime Diagnostics Rule

Unhandled WinUI exceptions should be written to:

```text
%AppData%/GalExcleTools/crash.log
```

This file is a best-effort crash log for runtime issues that do not appear in the bottom output panel. It is especially useful for WinUI crashes where Windows Event Viewer only reports `Microsoft.UI.Xaml.dll` and `0xc000027b`.

Do not treat `crash.log` as project data. It is diagnostic state only and should not influence project files, asset libraries, or CSV output.

## Suggested Project Files

```text
ProjectName/
  Tools/
    project.meta.json
    thumbnail.png
  Excel/
  project.galexcle.json
  Storylines/
    Main/
      Chapter_001/
        001_RainStation.story.csv
        001_RainStation.meta.json
  Assets/
    asset-index.json
    Backgrounds/
    Characters/
      Mio/
        character.json
        Costumes/
        Faces/
        Adorns/
        Vfx/
    Audio/
      BGM/
      Scene/
    Filters/
  Export/
    Story/
  Backups/
```

The first project creation milestone only creates:

```text
ProjectName/
  Tools/
    project.meta.json
    thumbnail.<ext> (optional)
  Excel/
  Chapters/
```

The first asset library creation milestone creates:

```text
AssetLibraryName/
  Tools/
    asset-library.meta.json
    thumbnail.<ext> (optional)
  Excel/
  背景图/
  立绘/
  音乐/
  环境音/
  特殊音效/
  函数/
  角色滤镜/
```

Additional folders will be added later as the story and asset workflows are implemented.

The first implemented asset category is `背景图`:

- Opening an asset library loads image files from `背景图/`.
- Importing background images stores them in `背景图/` as PNG files.
- The whole background-image section is a drop target: dropping files from outside the app imports supported images at the end of the current order, while dropping an internally dragged image into the trailing blank area moves it to the last position.
- Imported background images use the original source file name as the default remark suffix after sanitization.
- Source files in `JPG/JPEG` and `WebP` format are decoded and converted to PNG by the app.
- Existing or imported `.PNG` files keep their image data and only normalize the extension casing to lowercase `.png`.
- Images are shown as square thumbnails, five items per row.
- File names are normalized by current order: `BG00.png`, `BG01.png`, etc.
- The number of digits grows with the asset count, with a minimum of two digits.
- Dragging images uses whole-grid reorder interaction: the dragged item temporarily leaves its original slot, overlapping another image reorders with the native low-latency interaction, and dragging into the trailing blank area of the current row moves the image directly to the last position before dropping triggers automatic renaming.
- Right-clicking an image can set a remark suffix (`BG00_Remark.png`) or delete the image.
- The remark dialog treats `Enter` as confirm, `Esc` as cancel, and right click as cancel.
- Left-clicking an image opens an image-viewer page with a top tab-style header, mouse-wheel zoom centered on the pointer location, bounded left-button drag panning, double-click reset, `Esc` to exit, `Left/Right`, `A/D`, or numpad `4/6` to switch images, and a horizontally scrolling bottom command strip for actions such as remark editing and deletion.

The implemented audio categories are `音乐`, `环境音`, and `特殊音效`:

- Audio categories only accept `.wav` files from both folder scans and imports.
- Importing or externally dropping audio appends supported files to the current order.
- Imported audio uses the original source file name as the default remark suffix after sanitization.
- Audio cards use compact rectangular tiles. Dragging cards reorders them and triggers automatic renaming.
- Right-clicking an audio card can set a remark suffix or delete the file.
- Left-clicking an audio card opens a lightweight player page with previous track, play/pause, next track, remark editing, and deletion.
- Music normalizes names by order: `BGM00.wav`, `BGM01_Remark.wav`, etc.
- Environment sound normalizes names by order: `Sc00.wav`, `Sc01_Remark.wav`, etc. Story rows bind this category through the `Scene` column.
- Sound effects normalize names by order: `SE00.wav`, `SE01_Remark.wav`, etc. Story-row binding is reserved for a later column or command design.
- Function cards live in the asset-library `函数` folder as `functions.json`. Each entry stores a Chinese name, a function indicator string, a category, and optional choice-note text. Cards are compact rectangular tiles and do not support sorting.
- Trigger-option function cards use category `触发选项` and indicator names like `{ChapterSectionCode}-Choice{N}`; for example `M2-04-Choice2`, where `M2` is the chapter and `04` is the zero-based subsection code derived from the editor section selector (`第 5 小节` -> `04`). The visible story-editor header should use the same effective chapter-section code, including the project prefix (`WHK-M2-04`). The built-in `创建触发选项` template is auto-added to older libraries, prompts for one or more option-note rows, and writes only the next available current chapter-section choice trigger when used from the story function picker. Multiple option notes belong to that single trigger and are stored in the chapter-local `story.choice-notes.json`; they never write to story CSV fields or asset-library function cards. The story toolbar shows `查看选项` only when the current row already has choice functions.
- Default function indicators include `Scene_`, `BGLerpMode_`, `VFXON_`, `VFXOFF_`, `TransAnim_`, `TransAnim_END`, `MedPlay_`, `BGM_Start`, `BGM_Stop`, `TitleShowMode`, `CloseAllFX`, a trigger-option entry, and a custom-function entry.

The first implemented character-art category is `立绘`:

- Character cards are vertical cards with the character name shown below.
- Characters are fixed by name/code and do not support drag sorting.
- Story rows clear `Body/Face/Adorn/Vfx` layer indexes back to `0` when the matching character field is empty, contains a Chinese display name, or no longer resolves to a character card. Chapter repair applies the same rule as an automatic fix.
- In the story editor, hovering a character slot or basic asset control enables an internal clipboard: `Ctrl+C` copies the hovered data, `Ctrl+V` pastes it into a compatible hovered target, and `F12` opens the shortcut help dialog.
- The character area keeps drag-and-drop hooks reserved for future character archive recognition, but the current milestone does not import archives.
- A default `+` character card opens a creation dialog for character name, English code, and representative color.
- Creating a character generates a folder named by the English code and creates subfolders: `DN_Cloth`, `FC_Face`, `AD_Adorn`, and `VFX`.
- Character metadata is stored in `character.json`.
- Right-clicking a character card supports rename only.
- Opening a character card shows a tab-style detail page with an implemented layered preview. Layer order from bottom to top is `DN_Cloth`, `FC_Face`, and `AD_Adorn`; missing layers are skipped instead of drawing placeholders.
- Below the character preview, collapsible layer sections list `DN_Cloth`, `FC_Face`, and `AD_Adorn`.
- `DN_Cloth`, `FC_Face`, and `AD_Adorn` use square image cards similar to the background-image layer.
- Global character filters live in the asset library `角色滤镜` category. They use compact rectangular index cards, do not reference files, and only act as story-table VFX indexes.
- New asset libraries initialize filters as `VFX00_空`, `VFX01_冷色调（下雨）`, `VFX02_暖色调（黄昏）`, and `VFX03_上半身黑遮罩`.
- Costume is treated as the parent layer. Face and adorn assets include a costume-scope suffix so the editor can later filter compatible options per selected costume.
- The preview respects costume scopes: after a costume is selected, incompatible face/adorn layers are not drawn in the stacked preview.
- Costume files can be imported by button or external drag-and-drop, append to the end by default, and support drag sorting.
- Costume files normalize to `{CharacterCode}_DN00_Remark.ext`; changing the character English code renames existing costume files to the new prefix.
- Right-clicking a costume card can set its remark suffix or delete the costume.
- Left-clicking a costume card opens a secondary image-viewer tab from the character detail page, reusing the same zoom, pan, adjacent switching, remark, and delete actions as the background-image viewer.
- Face files normalize to `FC00_Remark.ext`; costume availability is stored in `FC_Face/face-scope.meta.json` instead of being embedded in the file name.
- Right-clicking a face card can set its remark suffix, delete the face, or open `可用范围`, where costume thumbnails are shown with checkboxes.
- Adorn story index `0` means no adorn. Imported adorn files still normalize from `AD00_Remark.ext`, but story-table adorn indexes point to files starting at `1`.
- Right-clicking an adorn card can set its remark suffix, delete the adorn, or open `可用范围`, where costume thumbnails are shown with checkboxes.

## File Sync Rule

Every user-visible edit should map to one project file. The UI should avoid keeping hidden app-only state.

- Story row edits write to the chapter CSV or its sidecar metadata.
- Asset note edits write to `asset-index.json`.
- Character naming rules write to the character card file.
- Project settings write to `project.galexcle.json`.
- Backup creates a zip from the current project files.

The app should not continuously watch and react to all external folder edits. To avoid loops and unnecessary file churn:

- Refreshes are triggered by explicit in-app operations such as create, import, reorder, rename, or settings changes.
- A delayed refresh timer waits 1 second before reloading UI data, so a batch of file writes is treated as one change.
- Direct manual changes made in File Explorer are not auto-refreshed; users can use the refresh buttons or reopen the page.
- Normalization logic should be idempotent: if filenames are already correct, do not rewrite them.

When the workspace root changes, the migration must be treated as an atomic project operation:

- Copy all directories and files to the new `{SelectedFolder}/GalExcelProject` root.
- Verify every source file exists in the new root with matching size and SHA-256 hash.
- Save the new setting only after verification succeeds.
- Delete the old root only after verification succeeds.
- Block migration if the new root is inside the old root.

## Backup Rule

Quick backup should create a zip with a stable prefix:

```text
{BackupPrefix}_{ProjectName}_{yyyyMMdd_HHmmss}.zip
```

The backup should include tables, settings, index files, and naming maps. Large original art/audio files can be optional, because Unreal imports those separately.

## Naming Rule Draft

Background:

```text
BG_{Index:D4}_{Remark}
```

Character body:

```text
{CharacterCode}_DN00_{Remark}
```

Character face:

```text
FC00_{Remark}
```

Character adorn:

```text
AD00_{Remark}
```

Character filter:

```text
VFX00_{Remark}
```

Audio:

```text
BGM{Index:D2}_{Remark}
Sc{Index:D2}_{Remark}
SE{Index:D2}_{Remark}
```

## Implementation Direction

Start with a small, file-first architecture:

- Models: project, story row, asset entry, character card, backup profile.
- Services: project storage, CSV import/export, asset index, backup, validation.
- Views: shell, project tree, story editor, asset library, settings.

MVVM can be added once the screens stabilize. The first useful milestone is a single project workspace that can load one project folder, edit one story CSV, and export it unchanged except for edited rows.

## Story Editor Notes

- Chapter story CSV files use `{ChapterCode}.csv`; the older `{ChapterCode}.story.csv` name is only a migration source.
- Dropping `.csv` files onto the chapter grid starts an import dialog. The dialog first checks whether the CSV header is compatible with the current `FStoryStruct`, then reuses the chapter creation fields for Chinese name, English code, and story type. Imported rows keep their original indexes and get default subsection `1`.
- Opening a chapter does not rewrite the CSV immediately. The CSV is written only when rows are edited, navigated into a newly-created row, deleted, imported, or exported into subsection files.
- The subsection selector starts with only the existing section count, normally just section `1`; the editor's `+` control creates additional sections dynamically.
- Subsection CSV exports for Unreal sync are generated into the toolbox cache under `{ToolProject}/Tools/UnrealStorySections`, so the chapter folder keeps only the master CSV. Old visible files such as `{ChapterCode}_小节1.csv` are treated as legacy generated output and cleaned when possible.
- Story row names in the first row-name column are pure numbers (`1`, `2`, `3`...) so Unreal data table import can recognize them cleanly. The generated CSV header for that column is `---`, while older `Name` headers are still accepted on import.
- Story asset indexes are zero-based. `0` points to the first background, BGM, environment sound, body, and face entries. `Adorn=0` means no adorn; `Vfx=0` means no filter.
- Drag-sorting bound asset-library items remaps affected story CSV indexes automatically: backgrounds update `BGindex`, music updates `BGM`, environment sound updates `Scene`, character clothes/faces/adorns update only rows that reference that character, and character filters update `TalkVfx` plus `Vfx1` to `Vfx5`.
- Sorting remap shows detailed progress while scanning linked projects and writing CSV files, then shows a before/after comparison of changed cells. Out-of-range or mismatched data is reported instead of being guessed.
- Chapter card right-click `修复` runs a single-chapter index inspection. Safe numeric out-of-range values can be reset to `0`; unknown characters or missing assets stay as warnings for manual review.
- Story function shortcuts write the selected indicator into the current row's `Custom` column. `Scene_` opens a sound-effect picker and appends the selected special-sound index; `VFXON_`/`VFXOFF_`, `TransAnim_`, `MedPlay_`, and other trailing-underscore indicators ask for a suffix before writing.
- `Scene` stores the zero-based environment sound index from the bound asset library `环境音` folder.
- `Chara1` to `Chara5` store the character English code, not the Chinese display name. Existing Chinese names are normalized to codes when a chapter is opened.
- `TalkChar` is still typed through the speaker text box. If the typed value resolves to a character name or code, the CSV stores the English code; unresolved text is kept as entered.
- The character picker has a `无角色` entry. Choosing it clears the character slot and resets body, face, adorn, and VFX indexes to `0`.
- Character preview slots render layered body, face, and adorn images with the Chinese display name above the slot label. The current global filter name is shown on the slot label when a character is present. The bottom-left speaker preview uses the `TalkChar`, `TalkBody`, `TalkFace`, `TalkAdorn`, and `TalkVfx` columns.
- Hovering a character preview card enables keyboard shortcuts: `Q/E` switch adorns, `A/D` switch faces, `Z/C` switch clothes, numpad left/right switches characters by folder order, numpad up/down switches VFX/filter, and `Tab` clears the current card.
- BGM and environment sound playback in the story editor use dedicated looping `MediaPlayer` instances. They refresh on row changes and are stopped when leaving the story editor.
- Runtime crashes are logged to `%AppData%\GalExcleTools\crash.log`.

## Packaging Notes

- The desktop build is published as an unpackaged WinUI app with `WindowsPackageType=None` and `WindowsAppSDKSelfContained=true`.
- Release publish keeps trimming disabled because the tool depends on JSON serialization for project, asset, and filter metadata.
- The friendly product/exe name is `TFAC剧情箱-轮椅版`; the C# root namespace remains `GalExcleTools`.
- The application icon is generated from `D:\Icon.jpg` into `Assets\AppIcon.ico`.
- `Assets\**\*` is copied to build and publish output so `ms-appx:///Assets/...` resources still resolve in unpackaged builds.
- Current publish target folder: `D:\DabaoV`.

## Unreal Sync Desk

The left navigation includes `虚幻同步台`. It is the first bridge between the toolbox project and an Unreal project.

Current workflow:

- Select a toolbox project from the project cards at the top of the page.
- Select an Unreal editor executable, preferably `UnrealEditor-Cmd.exe` or `UnrealEditor.exe`.
- Select a `.uproject` file.
- Select a target folder under the Unreal project's `Content` directory. The folder name must be exactly `Narrative`, such as `Content\AssetMaterial\Narrative`, to prevent syncing assets into the wrong content area.
- Unreal binding fields are saved immediately into the selected toolbox project's `project.meta.json`; changing cards auto-fills the engine, project, and target folder from that project's saved binding.
- The workspace page shows a sync tip when a saved Unreal binding exists.
- Sync is manual only. Users must click the large `确认同步到虚幻` button to avoid expensive Unreal imports during normal editing.
- Clicking sync first builds a change plan. If no source asset, story CSV, or lustration-data change is detected, the tool does not launch Unreal.
- `全部重新同步` intentionally ignores cached timestamps and the lustration hash, then sends all import groups, story tables, and lustration rows through the sync path.
- Sync manifests include only changed source files and changed story tables. A source file is considered changed when its destination `.uasset` is missing or older than the source file. Lustration data uses a cached hash stored in `{ToolProject}/Tools/unreal-sync-state.json`.
- Before sync, the tool asks whether to create a clean Unreal project backup. The backup is a zip that excludes cache/generated folders such as `Saved`, `Intermediate`, `DerivedDataCache`, `.vs`, and `Binaries`, and defaults to `{ToolProject}/Tools/UnrealBackups`.
- If an Unreal Editor process is already running, the tool warns before sync. Prefer closing the editor first; otherwise loaded assets may stay stale in memory and later saves can overwrite command-line sync results.
- The sync page shows staged progress: difference scan, backup, manifest/script generation, Unreal launch, Unreal import/save, and result collection. The percent is a toolbox-side stage estimate, not Unreal's per-asset internal import percentage.
- After the Unreal command exits, the toolbox shows a completion dialog with shortcuts to open the Unreal project or the project log folder.
- Unreal command-line runs do not always return Python `unreal.log(...)` lines through stdout/stderr. After the process exits, the toolbox also reads the newest `Saved/Logs/*.log` file and uses the `GalExcleTools` lines there to confirm story-table and lustration-data writes.

Reference Unreal layout observed in `D:\UnrealMap\MyWhiteHairedKiller\Content\AssetMaterial\Narrative`:

```text
BackGround
BGM
ExcelTexts
Lustration
Scene_Effect
```

The sync desk validates:

- Engine executable exists and can be resolved to an editor command executable.
- `.uproject` exists and has a `Content` folder.
- Target folder is inside that `Content` folder.
- Target folder name is `Narrative`.
- Toolbox project still exists and has a usable bound asset library.
- Unreal project name and toolbox project code may differ; this is no longer shown as a warning.
- Existing `.uasset` counts are compared with toolbox source counts as a quick consistency tip.

Import route:

- Raw files cannot become `.uasset` through file copy.
- On sync, the toolbox writes `Saved\GalExcleTools\gal-sync-manifest.json` and `Saved\GalExcleTools\gal_sync_import.py` under the Unreal project.
- The tool then launches Unreal with `-ExecutePythonScript=<script>`. This starts an editor command process automatically and normally exits after the script finishes; users do not need to manually open Unreal first.
- `gal-sync-manifest.json` must be written without a UTF-8 BOM. Unreal's Python reader also opens it with `utf-8-sig` so older BOM manifests do not fail before import starts.
- The Python script creates destination directories and uses `unreal.AssetImportTask` through `unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks(...)`.
- The script imports backgrounds, audio, story CSV files, and character layer images into the selected target path, then saves only assets returned by the import tasks instead of saving the whole Narrative tree.
- Story CSV data tables are refilled with `/Script/GALLibrary.StoryStruct`, which corresponds to `FStoryStruct` in `GALLRealize.h`. If the target DataTable is missing, the script tries to create it with `unreal.DataTableFactory` before filling it.
- Story table assets are grouped under `ExcelTexts` by chapter type: `MainStory`, `Interlude`, `Simulation`, `EventActivity`, `WorldDialog`, `Minecraft`, or `Other`. Single-section chapters sync the main CSV directly inside the type folder. Multi-section chapters sync each section CSV inside a chapter-code subfolder without the final section suffix, for example chapter `WHK-M2-00` uses folder `WHK-M2`.
- Section CSV/table names use the final chapter segment as the section index and keep hyphen naming. For chapter `WHK-M2-00`, sections are `WHK-M2-00`, `WHK-M2-01`, etc. The old `_小节1` suffix and underscore names are legacy and should be cleaned during export/sync.
- `Lustration/DA_LustrationInfor` is a `PDA_LustrationData` data asset, not a DataTable. The sync script loads the data asset and writes its `Infor` map directly as `string -> FLustrationStruct`, using `FLustrationStruct` from `GALLRealize.h`.
- `DA_LustrationInfor` is only written when the lustration hash says the mapping changed, or when the user explicitly runs `全部重新同步`; normal CSV/audio/image sync must not clear or rewrite it with empty rows.
- Character VFX indexes are driven by the toolbox filter list. Unreal sync does not create or modify VFX material assets; it only keeps `FLustrationStruct.Vfx` array length aligned with the toolbox filter count by writing empty soft references. Story CSV `Vfx` columns carry the real numeric index.
- If live editing inside an already-open Unreal Editor is required later, add a small Unreal editor-side bridge command/plugin so the current editor process performs the write. The current implementation is command-line sync and works best with the target project closed.

Current destination mapping:

```text
Background images -> {Target}/BackGround
BGM wav           -> {Target}/BGM
Ambient/SE wav    -> {Target}/Scene_Effect
Chapter CSV       -> {Target}/ExcelTexts/{ChapterTypeFolder} or {Target}/ExcelTexts/{ChapterTypeFolder}/{ChapterCodeWithoutSection} for multi-section chapters
Character clothes -> {Target}/Lustration/{CharacterCode}/DN_Cloths
Character faces   -> {Target}/Lustration/{CharacterCode}/FC_Face
Character adorns  -> {Target}/Lustration/{CharacterCode}/AD_Adorn
Character table   -> {Target}/Lustration/DA_LustrationInfor
```

Known risks and next steps:

- CSV-to-DataTable import may need Unreal-side options for the exact `FStoryStruct` row struct. If auto-detect is not enough, add a dedicated Unreal Python script or editor plugin command.
- Python execution requires the Unreal Python plugin to be enabled for the target engine/project.
- Reimport rules, asset naming collisions, redirectors, and source-control checkout are not solved yet.
- Imported `.uasset` files are editor assets, not cooked/pak output. A later packaging path can look at Unreal command-line cooking and `UnrealPak`, but that should be a separate step from editor sync.
- Deleting assets from the toolbox does not currently delete existing Unreal `.uasset` files; sync is additive/replace-existing only.
