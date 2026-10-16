param(
    [switch]$Build
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

$utf8Strict = [System.Text.UTF8Encoding]::new($false, $true)
$filesToCheck = @(
    "MainWindow.xaml",
    "MainWindow.xaml.cs",
    "Services/AppVersionService.cs",
    "README.md",
    "Docs/CodexInternalNotes.md"
)

$issues = [System.Collections.Generic.List[string]]::new()

foreach ($relativePath in $filesToCheck) {
    if (-not (Test-Path -LiteralPath $relativePath)) {
        continue
    }

    $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $relativePath))
    try {
        $text = $utf8Strict.GetString($bytes)
    }
    catch {
        $issues.Add("$relativePath is not strict UTF-8: $($_.Exception.Message)")
        continue
    }

    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
        $issues.Add("$relativePath is UTF-16 LE; keep repository text files as UTF-8.")
    }

    if ($text.Contains([char]0xFFFD)) {
        $issues.Add("$relativePath contains replacement characters.")
    }
}

$xamlText = $utf8Strict.GetString([System.IO.File]::ReadAllBytes((Resolve-Path "MainWindow.xaml")))
try {
    [xml]$xamlText | Out-Null
}
catch {
    $issues.Add("MainWindow.xaml is not well-formed XML: $($_.Exception.Message)")
}

$mojibakeChars = @(
    0x934A,
    0x675E,
    0x7EF1,
    0x6924,
    0x94CF,
    0x9417,
    0x93C4,
    0x6D63,
    0x6D7C,
    0xFFFD
) | ForEach-Object { [char]$_ }

$xamlCodeText = $utf8Strict.GetString([System.IO.File]::ReadAllBytes((Resolve-Path "MainWindow.xaml.cs")))
foreach ($entry in @(
    @{ Path = "MainWindow.xaml"; Text = $xamlText },
    @{ Path = "MainWindow.xaml.cs"; Text = $xamlCodeText }
)) {
    $lineNumber = 0
    foreach ($line in ($entry.Text -split "\r?\n")) {
        $lineNumber++
        foreach ($marker in $mojibakeChars) {
            if ($line.Contains($marker)) {
                $issues.Add("$($entry.Path):$lineNumber contains likely mojibake: $($line.Trim())")
                break
            }
        }

        if ($line -match '\?\s+(Style|Click|Tag|FontSize|Content|Header|Text|VerticalAlignment|HorizontalAlignment)=') {
            $issues.Add("$($entry.Path):$lineNumber may have a broken XAML attribute: $($line.Trim())")
        }
    }
}

function Require-SourcePattern {
    param(
        [string]$Path,
        [string]$Pattern,
        [string]$Description,
        [switch]$NotMatch
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        $issues.Add("$Description missing source file: $Path")
        return
    }

    $source = $utf8Strict.GetString([System.IO.File]::ReadAllBytes((Resolve-Path $Path)))
    if ($NotMatch ? ($source -match $Pattern) : ($source -notmatch $Pattern)) {
        $issues.Add("$Description")
    }
}

function Require-MethodPattern {
    param(
        [string]$Path,
        [string]$MethodName,
        [string]$Pattern,
        [string]$Description,
        [switch]$NotMatch
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        $issues.Add("$Description missing source file: $Path")
        return
    }

    $source = $utf8Strict.GetString([System.IO.File]::ReadAllBytes((Resolve-Path $Path)))
    $methodPattern = '(?ms)^\s*(?:public|private|internal|protected)[^\r\n]*\b' + [regex]::Escape($MethodName) + '\s*\(.*?(?=^\s*(?:public|private|internal|protected)\s+|\z)'
    $methodMatch = [regex]::Match($source, $methodPattern)
    if (-not $methodMatch.Success -or ($NotMatch ? ($methodMatch.Value -match $Pattern) : ($methodMatch.Value -notmatch $Pattern))) {
        $issues.Add("$Description")
    }
}

Require-SourcePattern "MainWindow.xaml" 'x:Name="FloatingTipsPanel"' "MainWindow.xaml is missing the global FloatingTipsPanel."
Require-SourcePattern "MainWindow.xaml" 'x:Name="FloatingTipsPanel"[\s\S]*?MaxWidth="520"[\s\S]*?Margin="0,18,28,0"[\s\S]*?Canvas\.ZIndex="330"[\s\S]*?Spacing="6"' "FloatingTipsPanel does not match the Tips layout specification."
Require-SourcePattern "MainWindow.Tips.cs" 'InfoBarSeverity\.Informational\s*=>\s*InfoBarSeverity\.Informational' "Floating Tips must preserve neutral informational severity."
Require-SourcePattern "MainWindow.Tips.cs" 'InfoBarSeverity\.Error\s*=>\s*TimeSpan\.FromSeconds\(5\)' "Error Tips must remain visible for 5 seconds."
Require-SourcePattern "MainWindow.Tips.cs" 'InfoBarSeverity\.Warning\s*=>\s*TimeSpan\.FromSeconds\(3\.2\)' "Warning Tips must remain visible for 3.2 seconds."
Require-SourcePattern "MainWindow.Tips.cs" 'TimeSpan\.FromSeconds\(2\.6\)' "Success Tips must remain visible for 2.6 seconds."
Require-SourcePattern "MainWindow.Tips.cs" 'MaxFloatingTipCount\s*=\s*2' "Floating Tips must be limited to two visible items."
Require-SourcePattern "MainWindow.Tips.cs" 'CopyErrorLogToClipboard' "Error Tips must copy their corresponding Log text to the clipboard."
Require-SourcePattern "MainWindow.AssetLibrary.Editing.cs" '已替换背景图素材[\s\S]*?ShowFloatingTip' "Replacing a background image must show a result Tip."
Require-SourcePattern "MainWindow.AssetLibrary.Editing.cs" 'logMessage\s*=\s*\$"已替换\{displayName\}素材：\{Path\.GetFileName\(layerPath\)\}[\s\S]*?ShowFloatingTip' "Replacing a character layer must show a result Tip."
Require-SourcePattern "MainWindow.AssetLibrary.Editing.cs" 'logMessage\s*=\s*\$"已替换\{displayName\}素材：\{Path\.GetFileName\(musicPath\)\}[\s\S]*?ShowFloatingTip' "Replacing an audio asset must show a result Tip."
Require-MethodPattern "MainWindow.AssetLibrary.DragDrop.cs" "AudioDropZone_Drop" 'ShowFloatingTip' "Dropping audio assets must show a result Tip."
Require-MethodPattern "MainWindow.Workbench.Import.cs" "ProjectsGridView_Drop" 'ShowFloatingTip' "Dropping project archives must show a result Tip."
Require-MethodPattern "MainWindow.Workbench.Import.cs" "AssetLibrariesGridView_Drop" 'ShowFloatingTip' "Dropping asset-library archives must show a result Tip."
Require-MethodPattern "MainWindow.Workbench.TextTool.cs" "CreateProjectTextVoiceTableRow" 'ShowFloatingTip' "Selecting or replacing a voice file must show a result Tip."
Require-MethodPattern "MainWindow.Workbench.TextTool.cs" "CreateProjectTextVoiceTableRow" 'CreateProjectVoicePreviewButton' "Voice rows must include the preview column button."
Require-SourcePattern "MainWindow.Workbench.TextTool.cs" 'PlayProjectVoicePreviewAsync' "The text voice tool is missing voice preview playback."
Require-SourcePattern "MainWindow.Workbench.TextTool.cs" 'new GridLength\(72\)' "The voice preview column must use the fixed narrow width."
Require-SourcePattern "MainWindow.xaml" 'x:Name="ProjectTextToolVoiceFilterButton"' "The text voice tool is missing its configured-voice filter button."
Require-MethodPattern "MainWindow.Workbench.TextTool.cs" "ProjectTextToolVoiceFilterButton_Click" 'ShowOnlyConfiguredVoices' "The voice filter button must toggle configured-only rows."
Require-MethodPattern "MainWindow.Workbench.TextTool.cs" "RefreshProjectTextToolPage" 'ShowOnlyConfiguredVoices' "The text voice table must apply the configured-voice filter."
Require-MethodPattern "Services/ProjectVoiceAssetService.cs" "WriteVoice" 'int\.TryParse\(row\.RowName[\s\S]*?BuildTargetPath\(folderPath, rowNumber' "Voice file numbering must come from the text row index."
Require-MethodPattern "MainWindow.Workbench.TextTool.cs" "RefreshProjectTextToolPage" 'CreateProjectTextVoiceTableRow\(row, value, chapterRows\.Count\)' "Voice file numbering width must use the complete chapter row set."
Require-MethodPattern "Services/ProjectTextDataService.cs" "ReadVoiceMap" 'ResolveVoiceMapPaths' "Voice map reads must resolve project-relative paths."
Require-MethodPattern "Services/ProjectTextDataService.cs" "WriteVoiceMap" 'CreatePortableVoiceMap' "Voice map writes must store portable project-relative paths."
Require-SourcePattern "Services/ProjectTextDataService.cs" 'Path\.GetRelativePath\(project\.Path' "Portable voice maps must be relative to the project directory."
Require-SourcePattern "MainWindow.xaml.cs" '_storyVoicePlayback\s*=\s*new\(looping:\s*false\)' "The story editor must own a dedicated voice playback service."
Require-MethodPattern "MainWindow.Story.cs" "LoadStoryRowIntoUi" 'PlayCurrentStoryVoiceFromUiAsync' "Loading a story row must trigger its configured voice."
Require-MethodPattern "MainWindow.Story.Media.cs" "PlayCurrentStoryVoiceFromUiAsync" 'StopStoryVoicePlayback\(\)[\s\S]*?_storyVoicePlayback\.PlayAsync' "A new story voice request must stop the previous voice before loading the next file."
Require-MethodPattern "Services/StoryAudioPlaybackService.cs" "PlayAsync" 'requestId\s*=\s*\+\+_requestId[\s\S]*?requestId\s*!=\s*_requestId' "Story audio playback must reject stale asynchronous requests."
Require-MethodPattern "MainWindow.Story.Media.cs" "StopStoryEditorAudio" 'StopStoryVoicePlayback' "Closing the story editor must stop story voice playback."
Require-MethodPattern "Services/StoryAssetFieldService.cs" "BuildForwardContiguousPlan" 'ValidateForwardReplacement[\s\S]*?for \(var index = startIndex; index < rows\.Count; index\+\+\)[\s\S]*?break;' "Forward asset replacement planning must stop at the first different value without mutating rows."
Require-MethodPattern "Services/StoryAssetFieldService.cs" "ApplyForwardContiguousPlan" 'plan\.Count[\s\S]*?plan\.OriginalIndex[\s\S]*?throw new InvalidOperationException[\s\S]*?rows\[index\]\.Set' "Forward asset replacement must reject stale plans before applying the range."
Require-MethodPattern "MainWindow.Story.Functions.cs" "ChooseStoryAssetIndexAsync" 'BuildForwardContiguousPlan[\s\S]*?ConfirmStoryAssetReplacementAsync[\s\S]*?CaptureStoryUndoState[\s\S]*?ApplyForwardContiguousPlan' "Changing a basic story asset must confirm the planned range before creating one undo state and applying it."
Require-SourcePattern "Models/UnrealSyncModels.cs" 'bool IsFullSync[\s\S]*?List<string> ChangeItems[\s\S]*?List<string> UnchangedItems' "Unreal sync preview copy must separate mode, changes, and unchanged items."
Require-SourcePattern "Services/UnrealSyncService.cs" '本次同步将处理' "Unreal sync summary must clearly state what will be processed."
Require-SourcePattern "Services/UnrealSyncService.cs" '新增或更新素材[\s\S]*?清理虚幻中的多余素材[\s\S]*?更新剧情表[\s\S]*?更新素材索引表' "Unreal sync summary must use clear action-oriented copy."
# 【2026-10-12 同步台重建】预览从"把每个文件名列出来"（几百行）改成**一张对比表**：
# 一行 = 一个类别变了多少，"无变化"时明说不会启动 Unreal。
# 另外 AppendUnrealSyncDiffDetails 已经并进这个方法了（改名 + 合流），
# 所以下面两条都锚在 ApplyUnrealSyncChangePlan 上。
Require-MethodPattern "MainWindow.UnrealSync.cs" "ApplyUnrealSyncChangePlan" 'ClearDiffRows[\s\S]*?!changePlan\.HasChanges[\s\S]*?AddDiffRow\("（无变化）"[\s\S]*?changePlan\.ImportGroups[\s\S]*?GetUnrealSyncDestinationDisplayName[\s\S]*?changePlan\.DeleteGroups[\s\S]*?changePlan\.StoryBranchesChanged' "Unreal sync preview must render readable information groups."
Require-MethodPattern "MainWindow.UnrealSync.cs" "ApplyUnrealSyncChangePlan" 'AddDiffRow\(\s*GetUnrealSyncDestinationDisplayName\([\s\S]*?group\.Destination\);' "Unreal sync details must show friendly asset names before technical paths."
Require-SourcePattern "Models/UnrealSyncModels.cs" 'Dictionary<string, string> SourceHashes' "Unreal sync state must persist source SHA-256 hashes."
Require-SourcePattern "Models/UnrealSyncModels.cs" 'UnrealSyncExecutionReport[\s\S]*?Completed[\s\S]*?Success[\s\S]*?Errors[\s\S]*?Warnings' "Unreal sync must expose a structured execution report."
Require-MethodPattern "Services/UnrealSyncService.cs" "WriteManifest" 'ProtocolVersion[\s\S]*?ProgressPath[\s\S]*?ResultPath' "Unreal sync manifest must include structured progress and result paths."
Require-MethodPattern "Services/UnrealSyncService.cs" "WritePythonScript" 'atomic_write_json[\s\S]*?os\.replace[\s\S]*?write_progress[\s\S]*?write_result' "Unreal Python must atomically report real progress and its structured result."
Require-MethodPattern "Services/UnrealSyncService.cs" "WritePythonScript" 'except PermissionError[\s\S]*?time\.sleep' "Unreal Python atomic JSON replacement must retry transient Windows file locks."
Require-MethodPattern "Services/UnrealSyncService.cs" "WritePythonScript" 'def save_loaded_asset_with_retry[\s\S]*?save_loaded_asset[\s\S]*?time\.sleep[\s\S]*?add_error' "Unreal asset saves must retry and report final failures."
Require-MethodPattern "Services/UnrealSyncService.cs" "WritePythonScript" 'if save_loaded_asset_with_retry\(data_table, table_asset_path\):[\s\S]*?story_table_count \+= 1' "Story tables must only be confirmed after the package is saved."
Require-SourcePattern "Models/UnrealSyncModels.cs" 'int StateVersion' "Unreal sync state must include a migration version."
Require-MethodPattern "Services/UnrealSyncService.cs" "BuildChangePlan" 'requiresVerifiedTableSync[\s\S]*?storyTables[\s\S]*?assetIndexTablesChanged' "Older sync state must force a verified table refresh."
Require-MethodPattern "Services/UnrealSyncService.cs" "RunAsync" 'UnrealSyncProcessRunner\.RunAsync\([\s\S]*?progressPath[\s\S]*?ReadExecutionReport\(resultPath\)' "Unreal sync runner must consume structured progress and result files."
Require-MethodPattern "Services/UnrealSyncService.cs" "ReadProgressUpdate" 'ReadSharedText' "Unreal progress polling must use replace-safe shared file reads."
Require-MethodPattern "Services/UnrealSyncService.cs" "ReadSharedText" 'FileShare\.ReadWrite\s*\|\s*FileShare\.Delete' "Structured Unreal sync JSON reads must allow atomic replacement on Windows."
Require-MethodPattern "Services/UnrealSyncService.cs" "RunAsync" 'elapsed\.TotalSeconds' "Unreal sync runner still uses elapsed time to estimate progress." -NotMatch
Require-MethodPattern "Services/UnrealSyncService.cs" "BuildChangePlan" 'ComputeSourceHashes[\s\S]*?SourceHashes' "Unreal sync planning must compare source SHA-256 hashes."
Require-MethodPattern "Services/UnrealSyncService.cs" "BuildAssetObjectPath" 'NormalizeImportedAssetName' "Imported Unreal object paths must use the same sanitized asset name as Unreal."
Require-MethodPattern "Services/UnrealSyncService.cs" "BuildTextureReference" 'NormalizeImportedAssetName' "Unreal texture references must use sanitized imported asset names."
Require-MethodPattern "Services/UnrealSyncService.cs" "BuildSoundWaveReference" 'NormalizeImportedAssetName' "Unreal sound references must use sanitized imported asset names."
Require-MethodPattern "Services/UnrealSyncService.cs" "NormalizeImportedAssetName" 'char\.IsWhiteSpace[\s\S]*?InvalidUnrealObjectNameCharacters' "Imported asset-name normalization must cover Unreal invalid object-name characters."
Require-MethodPattern "Services/UnrealSyncService.cs" "SourceFileNeedsImport" 'BuildLegacyAssetObjectPath[\s\S]*?SourceFileNeedsAnyAssetUpdate' "Source hash checks must recognize state keys written before asset-name normalization."
# 同步的入口方法 2026-10-12 改名：RunUnrealSyncButton_Click → RunUnrealSyncFlowAsync
# （检测、全量重同步各有自己的入口）。下面这几条原来锚的是旧名字，
# 方法找不到 → Require-MethodPattern 直接判失败，于是**正反两条一起红**（-NotMatch 那条也是）。
Require-MethodPattern "MainWindow.UnrealSync.cs" "RunUnrealSyncFlowAsync" 'reportSuccess\s*=\s*report is \{ ProtocolVersion: 1, Completed: true, Success: true \}[\s\S]*?verificationPassed;' "Unreal sync completion must use the structured report."
Require-MethodPattern "MainWindow.UnrealSync.cs" "RunUnrealSyncFlowAsync" 'GalExcleTools updated|GalExcleTools failed' "Unreal sync UI still parses fixed English log messages." -NotMatch
# 进程检测那套（WMI 找别的工程的编辑器 + 建议先关掉它）2026-10-12 整个删掉了：
# 在线执行上线之后，"编辑器开着"才是要的状态 —— 那条建议正好是反的。
# 所以不再钉"检测要限定在绑定的 .uproject 上"，改钉两件现在为真的事：
#   · 工具箱不会再去杀/关别人的编辑器（连 WMI 查询都没有了）；
#   · 编辑器开着仍是首选路径（在线执行），这才是那段建议退休的原因。
Require-SourcePattern "MainWindow.UnrealSync.Process.cs" 'Win32_Process|CloseMainWindow' "Unreal sync must never hunt for or close the editor processes of another project." -NotMatch
Require-SourcePattern "Services/UnrealSyncService.cs" 'launch\.UsesRunningEditor[\s\S]*?RunWithOfflineFallbackAsync' "Keeping the editor open must stay the preferred online execution path."
# 回退点 2026-10-12 从"给工程打 zip（留最近 5 份）"改成"同步前全量本地提交"，
# 见 MainWindow.UnrealSync.Process.cs 里的退休说明与 Services/UnrealSync/UnrealSyncGitCommitService.cs。
# 覆盖范围不再是一份手写清单（AllRoad / MusicPlayer / 剧情表天然都在 `git add -A` 里），
# 口径是只提交、不推送。
Require-SourcePattern "Services/UnrealSync/UnrealSyncGitCommitService.cs" 'CommitEverythingAsync[\s\S]*?\["add", "-A"\][\s\S]*?\["commit", "-m", commitMessage\][\s\S]*?TryReadHeadShaAsync' "The pre-sync rollback point must commit every local change and record the revision."
Require-SourcePattern "MainWindow.xaml" 'x:Name="AppTitleVersionText"' "The title bar version must be populated from the shared version service."
Require-SourcePattern "MainWindow.xaml" 'x:Name="SettingsAppVersionText"' "The settings version must be populated from the shared version service."
Require-SourcePattern "MainWindow.xaml.cs" 'AppVersionService\.GetDisplayVersion\(\)[\s\S]*?AppTitleVersionText\.Text[\s\S]*?SettingsAppVersionText\.Text' "The title bar and settings page must use the same resolved app version."
Require-SourcePattern "Services/AppVersionService.cs" 'OverrideFileName\s*=\s*"ax-app-version\.json"' "The shared version service must expose the Ax-managed override file."
Require-SourcePattern "Services/AppVersionService.cs" 'Assembly\.GetEntryAssembly[\s\S]*?AssemblyInformationalVersionAttribute' "The shared version service must fall back to the built assembly version."
Require-SourcePattern "GalExcleTools.csproj" 'ax-app-version\.json[\s\S]*?CopyToOutputDirectory[\s\S]*?CopyToPublishDirectory' "The Ax-managed version file must be copied to build and publish output."
Require-SourcePattern "Scripts/Package-App.ps1" 'Get-AppVersion[\s\S]*?ProjectPath[\s\S]*?<Version>' "Packaging must resolve its default version from the project instead of XAML display text."
Require-SourcePattern "Models/UnrealSyncModels.cs" 'UnrealVoiceMapSyncEntry[\s\S]*?TableAsset[\s\S]*?List<string\?> Waves[\s\S]*?AssignedCount' "Unreal sync must model sparse voice arrays per story table."
Require-SourcePattern "Models/UnrealSyncModels.cs" 'bool VoiceMapsChanged[\s\S]*?List<UnrealVoiceMapSyncEntry> VoiceMaps[\s\S]*?string VoiceMapsHash' "Unreal sync plans must track voice-map changes and their hash."
Require-MethodPattern "Services/UnrealSyncService.cs" "BuildVoiceMapSyncEntries" 'BuildExportRowKey[\s\S]*?BuildStableTextRowId[\s\S]*?GetProjectVoiceDestinationPath[\s\S]*?BuildAssetObjectPath[\s\S]*?BuildSparseVoiceWaveSlots' "Voice-map plans must resolve stable rows against their exported story table slots."
Require-MethodPattern "Services/UnrealSyncService.cs" "BuildSparseVoiceWaveSlots" 'int\.TryParse[\s\S]*?maxRowNumber \+ 1[\s\S]*?waves\[rowNumber\]\s*=\s*pair\.Value' "Sparse voice arrays must retain slot zero, the maximum story index, and all empty gaps."
Require-MethodPattern "Services/UnrealSyncService.cs" "BuildChangePlan" 'VoiceMapsHash[\s\S]*?voiceMapsChanged[\s\S]*?更新剧情语音映射' "Voice-map changes must participate in Unreal diff planning."
Require-MethodPattern "Services/UnrealSyncService.cs" "WriteManifest" 'VoiceMap[\s\S]*?BlueprintAsset[\s\S]*?MapProperty[\s\S]*?ShouldUpdate[\s\S]*?Entries' "The Unreal manifest must carry the MusicPlayer voice-map update."
Require-MethodPattern "Services/UnrealSyncService.cs" "WritePythonScript" 'load_blueprint_class[\s\S]*?get_default_object[\s\S]*?unreal\.TableVoices[\s\S]*?save_loaded_asset_with_retry[\s\S]*?verify_voice_map' "Unreal Python must update, verify, and save the MusicPlayer class defaults."
Require-SourcePattern "Models/UnrealSyncModels.cs" 'bool VoiceMapsUpdated' "Structured Unreal results must report whether MusicPlayer voice maps were saved."
Require-MethodPattern "MainWindow.UnrealSync.cs" "RunUnrealSyncFlowAsync" '!changePlan\.VoiceMapsChanged \|\| report\.VoiceMapsUpdated' "The UI must not record a voice-map sync as successful unless Unreal confirms it."
Require-MethodPattern "Services/UnrealSyncService.cs" "WriteState" 'VoiceMapsHash\s*=\s*changePlan\.VoiceMapsHash' "Successful sync state must persist the voice-map hash."
Require-SourcePattern "Services/UnrealSync/UnrealSyncGitCommitService.cs" 'filesToBackup|\["push"' "The pre-sync rollback point must stay a whole-workspace local commit (no hand-kept file list, no push)." -NotMatch
Require-SourcePattern "Models/StoryModels.cs" 'StoryAssetChoice\(int Index, string Name, string\? PreviewPath = null\)' "Story asset choices must support an optional image preview path."
Require-MethodPattern "MainWindow.Story.Assets.cs" "GetStoryBackgroundChoices" 'new StoryAssetChoice\(index, Path\.GetFileNameWithoutExtension\(path\), path\)' "Story background choices must retain their source image path for hover previews."
Require-SourcePattern "Services/StoryDialogService.cs" 'ImagePath: choice\.PreviewPath' "Story asset selection must feed preview images into the image-strip picker (no ToolTip popups)."
Require-MethodPattern "Views/StoryDialogContentFactory.cs" "CreateLazyImagePreviewToolTip" 'toolTip\.Opened[\s\S]*?LoadPreviewImageAsync[\s\S]*?toolTip\.Closed[\s\S]*?image\.Source\s*=\s*null' "Story image preview tooltips must load on hover and release the bitmap after closing."
Require-MethodPattern "Services/StoryEditorService.cs" "InsertAfterCurrent" 'InsertRelativeToCurrent\([\s\S]*?insertAfter:\s*true' "Right insertion must insert after the current story row."
Require-MethodPattern "Services/StoryEditorService.cs" "InsertRelativeToCurrent" 'insertIndex\s*=\s*currentRowIndex\s*\+\s*\(insertAfter\s*\?\s*1\s*:\s*0\)[\s\S]*?rows\.Insert\(insertIndex, insertedRow\)[\s\S]*?StoryRowsEditResult\(insertIndex' "Story row insertion must return and select the actual left or right insertion index."
Require-SourcePattern "ViewModels/StoryEditorViewModel.cs" 'InsertAfterRowCommand[\s\S]*?!IsDebugModeEnabled' "The story editor must expose a debug-safe right-insert command."
Require-SourcePattern "MainWindow.xaml" 'x:Name="StoryInspectorToolsScrollViewer"[\s\S]*?Header="行与小节"[\s\S]*?AddSectionCommand[\s\S]*?DeleteRowCommand[\s\S]*?x:Name="StoryEditorCanvasHost"[\s\S]*?Content="左插入"[\s\S]*?Command="\{Binding InsertRowCommand\}"[\s\S]*?Content="右插入"[\s\S]*?Command="\{Binding InsertAfterRowCommand\}"' "Low-frequency section creation and row deletion must stay in tools while left and right insertion remain in the editor dock."
Require-SourcePattern "MainWindow.xaml" 'x:Name="StoryEditorPage"[\s\S]*?PreviewKeyDown="StoryEditorPage_PreviewKeyDown"' "The story editor must intercept Ctrl+Z before focused input controls consume it."
Require-SourcePattern "MainWindow.xaml" 'x:Name="StoryEditorPage"[\s\S]*?<RowDefinition Height="48"\s*/>[\s\S]*?<RowDefinition Height="\*"\s*/>' "The story editor must use a fixed toolbar row above the workspace."
Require-SourcePattern "MainWindow.xaml" 'x:Name="StorySettingsPane"[\s\S]*?Width="360"[\s\S]*?x:Name="StorySidePanelContent"' "The story editor inspector must remain at a readable stable width."
Require-SourcePattern "MainWindow.xaml" 'Grid\.Row="1"[\s\S]*?Height="220"[\s\S]*?x:Name="StorySpeakerTextBox"' "The story editor text dock must use a stable bottom row instead of a negative overlay margin."
Require-SourcePattern "MainWindow.xaml" 'StoryEditorDocumentPathText|Margin="0,-170,0,0"' "The story editor still exposes the full path or uses the unstable negative dock margin." -NotMatch
Require-SourcePattern "MainWindow.Story.Interaction.cs" 'ToggleStorySettingsPaneButton_Click|StoryPaneAnimationTimer_Tick' "The permanent story inspector must not retain width-toggle animation logic." -NotMatch
Require-SourcePattern "MainWindow.xaml" 'Text="本句检查器"[\s\S]*?Text="转场"[\s\S]*?StoryCurrentBackgroundText[\s\S]*?ItemsSource="\{Binding CurrentFunctionItems\}"' "The story inspector must show transition state before assets and ordered functions."
Require-SourcePattern "MainWindow.xaml" 'StoryFunctionTipsPanel' "Persistent story functions must not share the transient Tips surface." -NotMatch
Require-MethodPattern "MainWindow.Story.Media.cs" "UpdateStoryFunctionInspector" 'TryParseBackgroundTransitionMode[\s\S]*?continue;[\s\S]*?functionNames\.AddRange[\s\S]*?SetCurrentFunctionItems' "The story inspector must evaluate transition functions before listing other functions."
Require-SourcePattern "MainWindow.xaml" 'x:Name="StoryFloatingTipsPanel"[\s\S]*?Grid\.Row="1"[\s\S]*?Margin="0,12,382,0"' "Transient story Tips must stay over the canvas and clear of the inspector."
Require-SourcePattern "MainWindow.xaml" 'StoryTransientTipsPanel' "The fixed story toolbar must not contain a second transient Tips host." -NotMatch
Require-SourcePattern "MainWindow.Tips.cs" 'MaxFloatingTipCount\s*=\s*2[\s\S]*?InfoBarSeverity\.Informational\s*=>\s*InfoBarSeverity\.Informational' "Floating Tips must stay limited and preserve neutral informational severity."
Require-SourcePattern "MainWindow.xaml" 'Height="36" ColumnSpacing="8"[\s\S]*?<ColumnDefinition Width="\*"\s*/>[\s\S]*?<ColumnDefinition Width="\*"\s*/>[\s\S]*?Content="上一句"[\s\S]*?Content="下一句"[\s\S]*?Height="36" ColumnSpacing="8"[\s\S]*?<ColumnDefinition Width="\*"\s*/>[\s\S]*?<ColumnDefinition Width="\*"\s*/>[\s\S]*?Content="左插入"[\s\S]*?Content="右插入"' "Story navigation and insertion commands must use stable equal-width pairs."
Require-SourcePattern "MainWindow.xaml" 'x:Name="ChaptersGridView"[\s\S]*?HorizontalAlignment="Stretch"[\s\S]*?SelectionMode="None"' "Chapter cards must use the full available project-detail width."
Require-SourcePattern "MainWindow.xaml" 'x:Name="ChaptersGridView"[\s\S]{0,320}?MaxWidth="980"' "Chapter cards must not be limited to the old three-column width." -NotMatch
Require-SourcePattern "MainWindow.xaml" 'Grid\.Row="0" Grid\.RowSpan="2" Background="Black"[\s\S]*?Grid\.Row="1"[\s\S]*?Background="#99FFFFFF"[\s\S]*?StorySpeakerTextBox[\s\S]*?Background="#66FFFFFF"[\s\S]*?StoryTextTextBox[\s\S]*?Background="#66FFFFFF"' "The stable story text dock must remain translucent over the full background and character canvas."
Require-SourcePattern "MainWindow.Story.Interaction.cs" '《F12打开快捷键提示》' "Large story preview slots must not show the old intrusive F12 tooltip." -NotMatch
Require-SourcePattern "MainWindow.xaml" 'Canvas\.ZIndex="100"[\s\S]*?x:Name="StoryEditorCanvasHost"[\s\S]*?SizeChanged="StoryEditorCanvasHost_SizeChanged"' "The story toolbar must stay above a clipped preview canvas."
Require-MethodPattern "MainWindow.Story.Preview.cs" "StoryEditorCanvasHost_SizeChanged" 'RectangleGeometry[\s\S]*?e\.NewSize\.Width[\s\S]*?e\.NewSize\.Height' "The story preview canvas must clip oversized character art to its bounds."
Require-SourcePattern "App.xaml" 'StoryInspectorTitleStyle[\s\S]*?FontSize" Value="22"[\s\S]*?StoryInspectorSectionTitleStyle[\s\S]*?FontSize" Value="13"[\s\S]*?StoryInspectorLabelStyle[\s\S]*?TextFillColorSecondaryBrush[\s\S]*?StoryInspectorValueStyle[\s\S]*?FontSize" Value="14"' "The story inspector must use a readable Apple-style typographic hierarchy."
Require-SourcePattern "MainWindow.xaml" 'Text="素材状态"[\s\S]*?Text="背景"[\s\S]*?CurrentBackgroundValue[\s\S]*?Text="BGM"[\s\S]*?CurrentBgmValue[\s\S]*?Text="环境音"[\s\S]*?CurrentSceneValue' "Story inspector asset values must use aligned label/value rows."
Require-SourcePattern "MainWindow.xaml" 'Text="函数"[\s\S]*?CurrentFunctionCountText[\s\S]*?ItemsSource="\{Binding CurrentFunctionItems\}"' "Story inspector functions must expose count and ordered values with separate hierarchy."
Require-SourcePattern "MainWindow.xaml" 'StoryInspectorStateTabButton[\s\S]*?StoryInspectorToolsTabButton[\s\S]*?x:Name="StorySidePanelContent"[\s\S]*?x:Name="StoryInspectorToolsScrollViewer"' "Story state and editing tools must use separate inspector tabs."
Require-SourcePattern "MainWindow.xaml" 'StoryInspectorStateTabButton[\s\S]*?HorizontalAlignment="Stretch"[\s\S]*?StoryInspectorToolsTabButton[\s\S]*?HorizontalAlignment="Stretch"' "Story inspector tabs must fill their equal-width columns for easy clicking."
Require-SourcePattern "MainWindow.xaml" 'HasCurrentTransitionFunction[\s\S]*?Text="转场"' "Transition state must appear only when the current row explicitly contains a transition function."
Require-MethodPattern "MainWindow.Story.Interaction.cs" "StoryInspectorTabButton_Click" 'StorySidePanelContent\.Visibility[\s\S]*?StoryInspectorToolsScrollViewer\.Visibility' "Story inspector tabs must switch between state and tools without a shared long scroll."
Require-MethodPattern "MainWindow.Story.Interaction.cs" "StoryEditorPage_PreviewKeyDown" 'IsControlKeyDown\(\)[\s\S]*?VirtualKey\.Z[\s\S]*?UndoStoryEditorOperation\(\)[\s\S]*?e\.Handled\s*=\s*true' "The story editor preview key handler must route Ctrl+Z to the unified undo stack."
# Tab 是焦点导航键：挂在冒泡的 KeyDown 上永远执行不到（会被焦点管理器先吃掉），
# 表现就是"按 Tab 没清空立绘，反而跳焦点"（用户 2026-10-16 报的）。所以它必须在隧道层。
Require-MethodPattern "MainWindow.Story.Interaction.cs" "StoryEditorPage_PreviewKeyDown" 'VirtualKey\.Tab[\s\S]*?ClearStoryCharacterSlot\(hoveredSlotIndex\)[\s\S]*?e\.Handled = true' "Tab must clear the hovered character slot from the tunneling handler."
Require-MethodPattern "MainWindow.Story.Interaction.cs" "StoryEditorPage_KeyDown" 'case Windows\.System\.VirtualKey\.Tab' "The dead Tab case must not come back to the bubbling handler." -NotMatch
# ── 热更新（2026-10-16 重新对锚）─────────────────────────────────────────────
# 这块被**整体重做过**：`Services/UpdateService.cs` 拆成了 `Services/Update/` 一整套
# （Gitee 客户端 / 通道选择 / 下载器 / 校验器 / 覆盖计划 / 启动交接 / 白名单），
# 设置页也从 MainWindow.xaml 挪进了 Controls/UpdateSettingsPanel.xaml。
# 老的那几条钉的是"Gitee + GitHub 双源 + 一个 UpdateService 类"那套，按设计已经作废：
# **现在只有 Gitee 一个更新源**（模型里的原话：设置里存 URL 只会留下"settings 里还是旧地址"的坑），
# GitHub 那半不存在了 —— 所以这里不再要求"先 Gitee 后 GitHub"。
Require-SourcePattern "Models/UpdateModels.cs" 'GiteeRepository[\s\S]*?ApiRoot[\s\S]*?ReleasesApiUrl[\s\S]*?ToolboxUpdateManifestDto[\s\S]*?Sha256[\s\S]*?ToolboxPackageManifestDto[\s\S]*?ToolboxStableKey[\s\S]*?EntryExe' "The toolbox update models must describe the Gitee repository, the remote manifest with its SHA-256, and the in-package identity."
Require-SourcePattern "Models/UpdateModels.cs" 'new\("xiaojie578", "TFACStorybox"\)[\s\S]*?gitee\.com/api/v5/repos/\{Owner\}/\{Name\}' "The update repository coordinates must stay the TFACStorybox Gitee repo (the only source)."
Require-SourcePattern "Services/Update/GiteeReleaseClient.cs" 'ReleasesApiUrl\}\?per_page=100' "The release list must be fetched with per_page=100, otherwise the second page is unreachable."
Require-SourcePattern "Services/Update/UpdateService.cs" 'CheckForUpdateAsync[\s\S]*?UpdateChannelSelector\.SelectRelease\(releases, request\.Channel\)' "Release selection must go through the channel selector so stable never picks a prerelease."
Require-SourcePattern "Services/Update/UpdateDownloader.cs" 'NormalizeSha256\(payload\.Sha256\)[\s\S]*?StringComparison\.Ordinal' "Every downloaded payload must be compared against the manifest SHA-256 exactly."
Require-SourcePattern "Services/Update/UpdatePackageValidator.cs" 'ToolboxStableKey[\s\S]*?UpdateVersion\.CompareSemantic\(manifest\.Version, expectedVersion\)[\s\S]*?expectedRuntime' "The package validator must verify identity, version and runtime before anything is applied."
Require-SourcePattern "Services/Update/UpdateLaunchService.cs" 'Environment\.ProcessId[\s\S]*?"-InstallDir", install[\s\S]*?"-ExpectedSha256", plan\.Sha256' "Installing must hand off to the external updater with our process id and the expected hash."
Require-SourcePattern "Services/Update/UpdateLaunchService.cs" 'ScriptFileName = "热更新覆盖\.ps1"' "The updater handoff must run the 热更新覆盖 script."
# 「不许乱覆盖」这条换了个判据：老的锚点是"Debug 版绝不覆盖开发产物"，
# 而新实现里 Debug 只挡**启动时的自动检查**（手动点「下载并更新」不受影响，代码注释明写）。
# 现在真正的护栏是**白名单**：覆盖计划只处理 update-whitelist.json 允许镜像的路径。
Require-SourcePattern "Services/Update/UpdateApplyPlan.cs" 'IsMirrored\(path\)' "The apply plan must decide what to touch from the whitelist, never from the package alone."
Require-SourcePattern "MainWindow.Update.cs" '#if DEBUG[\s\S]*?await Task\.CompletedTask;[\s\S]*?#else[\s\S]*?CheckOnStartupAsync\(\)' "Debug builds must skip the automatic startup update (a Release package would be laid over bin\)."
Require-SourcePattern "Controls/UpdateSettingsPanel.xaml" 'Text="更新通道"[\s\S]*?Content="检查更新"[\s\S]*?Content="下载并更新"[\s\S]*?Content="测试 Gitee 连接"' "The settings page must expose the update channel and the check/download/test actions."
Require-SourcePattern "Scripts/Package-App.ps1" 'update-package\.json' "Packaging must embed an update-package identity manifest."
Require-SourcePattern "Scripts/Package-App.ps1" 'toolbox-update\.json' "Packaging must generate the remote update manifest."
Require-SourcePattern "Scripts/Package-App.ps1" 'Get-FileHash[\s\S]*?SHA256' "Packaging must generate a SHA-256 for the update ZIP."
Require-SourcePattern "Scripts/Publish-GiteeRelease.ps1" 'GITEE_TOKEN[\s\S]*?toolbox-update\.json[\s\S]*?attach_files' "Gitee publishing must use an environment token and upload the manifest plus package assets."
Require-SourcePattern "Models/StoryModels.cs" 'class StoryRow[\s\S]*?StableId[\s\S]*?Guid\.NewGuid[\s\S]*?clone\.StableId\s*=\s*StableId' "Story rows must own a stable identity that survives cloning."
Require-MethodPattern "Services/StoryEditorService.cs" "MoveNext" 'new StoryRowsEditResult\(Math\.Min\(currentRowIndex \+ 1, rows\.Count - 1\), false' "Next-row navigation must never create story data."
Require-MethodPattern "Services/StoryEditorService.cs" "InsertRelativeToCurrent" 'insertedRow\.StableId\s*=\s*StoryRow\.CreateStableId\(\)' "Inserted story rows must receive a new stable identity."
Require-MethodPattern "Services/StoryEditorService.cs" "NormalizeContiguousSections" 'nextSection[\s\S]*?previousRawSection[\s\S]*?ApplySectionsInRowOrder' "Story sections must be normalized by ordered boundaries without regrouping rows."
Require-MethodPattern "Services/StoryEditorService.cs" "SplitSectionAtCurrent" 'CanSplitSectionAtCurrent[\s\S]*?sections\[index\]\s*=\s*sections\[index\]\s*\+\s*1' "Creating a section must split the current ordered section instead of assigning an arbitrary group."
Require-SourcePattern "Models/StoryModels.cs" 'ProjectTextRow\([\s\S]*?string Id[\s\S]*?string StableRowId' "Project text rows must use stable row identifiers."
Require-MethodPattern "Services/StoryCsvExporter.cs" "ExportTransactionally" 'story-csv-export-transaction[\s\S]*?staged[\s\S]*?backup[\s\S]*?commit\.started' "Story CSV export must use a recoverable transaction."
Require-MethodPattern "MainWindow.Story.Preview.cs" "RefreshStoryPreviewAsync" 'CancellationTokenSource[\s\S]*?ThrowIfCancellationRequested' "Story preview refreshes must cancel stale row requests."
Require-MethodPattern "MainWindow.Story.Preview.cs" "RefreshStoryPreviewAsync" 'PersistCurrentStoryRowsToFiles' "Rendering a story preview must never persist or repair story data." -NotMatch
Require-MethodPattern "MainWindow.Workbench.cs" "GetCachedStoryPreviewImageAsync" 'MaxStoryPreviewImageCacheCount[\s\S]*?_storyPreviewImageCache\.Remove' "Story preview image caching must have a hard capacity limit."
Require-MethodPattern "MainWindow.Story.cs" "UpdateStoryDebugModeUi" 'StorySpeakerTextBox\.IsEnabled\s*=\s*!_isStoryDebugModeEnabled[\s\S]*?StoryTextTextBox\.IsReadOnly' "Story debug mode must disable speaker suggestions and make story text read-only."
Require-SourcePattern "ViewModels/StoryEditorViewModel.cs" 'CommandExecutionFailed[\s\S]*?ExecutionFailed\s*\+=' "Story async command failures must be surfaced to the window."
Require-SourcePattern "ViewModels/StoryEditorViewModel.cs" 'DeleteRowCommand\s*=.*!IsDebugModeEnabled[\s\S]*?AddSectionCommand\s*=.*!IsDebugModeEnabled[\s\S]*?ChangeBackgroundCommand\s*=.*!IsDebugModeEnabled' "Story debug mode must disable destructive and asset-edit commands."
Require-SourcePattern "MainWindow.xaml" 'AutoSuggestBox[\s\S]*?StorySpeakerTextBox[\s\S]*?TextChanged="StorySpeakerTextBox_TextChanged"[\s\S]*?LostFocus="StoryTextEditor_LostFocus"[\s\S]*?StoryTextTextBox[\s\S]*?BeforeTextChanging="StoryTextEditor_BeforeTextChanging"[\s\S]*?LostFocus="StoryTextEditor_LostFocus"' "Speaker suggestions and story text must coalesce undo by focus session."
Require-MethodPattern "MainWindow.Story.TextEditing.cs" "StorySpeakerTextBox_TextChanged" 'AutoSuggestionBoxTextChangeReason\.ProgrammaticChange[\s\S]*?_storyTextEditUndoCaptured[\s\S]*?CaptureStoryRowUndoState[\s\S]*?AutoSuggestionBoxTextChangeReason\.UserInput' "Speaker-name undo must capture the first typed or selected non-programmatic change."
Require-MethodPattern "MainWindow.Story.TextEditing.cs" "StoryTextEditor_BeforeTextChanging" '_storyTextEditUndoCaptured[\s\S]*?CaptureStoryRowUndoState' "Story text undo must capture only the current row before the first change in a focus session."
Require-MethodPattern "Services/StoryUndoService.cs" "BeginRowEdit" '_pendingRowBefore[\s\S]*?\.Clone\(\)' "Text undo must not snapshot the full story document."
Require-SourcePattern "Models/StoryDocument.cs" 'class StoryDocument[\s\S]*?List<StoryRow> Rows[\s\S]*?Dictionary<string, int> RowSections[\s\S]*?CaptureSnapshot[\s\S]*?ApplyChange' "StoryDocument must own story rows, ordered sections, snapshots, and change application."
Require-SourcePattern "Models/StoryDocument.cs" 'StoryChangeSet[\s\S]*?RowChanges[\s\S]*?BeforeOrder[\s\S]*?SectionChanges[\s\S]*?ChoiceNoteChanges' "Story change sets must track row, order, section, and choice-note deltas."
Require-MethodPattern "Models/StoryDocument.cs" "RowsHaveSameContent" 'column\s*!=\s*"Name"' "Story change comparison must ignore export row numbering."
Require-SourcePattern "Services/StoryUndoService.cs" 'class StoryUndoService[\s\S]*?_undoStack[\s\S]*?_redoStack[\s\S]*?TryUndo[\s\S]*?TryRedo' "Story undo must support compact undo and redo change sets."
Require-SourcePattern "ViewModels/StoryEditorViewModel.cs" 'StoryDocument Document[\s\S]*?StoryUndoService UndoService[\s\S]*?RedoCommand' "The story editor view model must expose StoryDocument, compact undo, and redo."
Require-SourcePattern "ViewModels/StoryEditorViewModel.cs" 'List<StoryEditorUndoState> UndoStack' "The story editor still stores full-chapter undo snapshots." -NotMatch
Require-MethodPattern "MainWindow.Story.cs" "CaptureStoryUndoState" 'UndoService\.Begin' "Story edit capture must begin a document change transaction."
Require-MethodPattern "MainWindow.Story.cs" "CommitStoryUndoState" 'UndoService\.Commit' "Story edits must explicitly commit compact document changes."
Require-MethodPattern "MainWindow.Story.Functions.cs" "ChooseStoryFunctionAsync" 'QueueCurrentStoryDocumentSave\(choiceNotes:\s*_pendingStoryChoiceNotes\)[\s\S]*?_pendingStoryChoiceNotes\s*=\s*null' "Choice notes must commit with the function row transaction."
Require-MethodPattern "MainWindow.Story.Functions.cs" "ChooseStoryAssetIndexAsync" '_pendingStoryChoiceNotes' "Basic asset changes must not consume pending choice-note state." -NotMatch
Require-SourcePattern "MainWindow.xaml" 'Content="撤回"[\s\S]*?Command="\{Binding UndoCommand\}"[\s\S]*?Content="重做"[\s\S]*?Command="\{Binding RedoCommand\}"' "The story toolbar must expose compact undo and redo controls."
Require-MethodPattern "MainWindow.Story.Interaction.cs" "StoryEditorPage_PreviewKeyDown" 'VirtualKey\.Y[\s\S]*?VirtualKey\.Z[\s\S]*?IsShiftKeyDown[\s\S]*?RedoStoryEditorOperation' "Story redo must support Ctrl+Y and Ctrl+Shift+Z."
Require-SourcePattern "Models/StoryDocument.cs" 'using GalExcleTools\.Services' "Story domain models must not depend on service-layer types." -NotMatch
Require-SourcePattern "Models/StorySchema.cs" 'class StorySchema[\s\S]*?Columns[\s\S]*?NumericColumns' "Story schema must live in the domain layer instead of CSV persistence."
Require-SourcePattern "Services/StoryDocumentRepository.cs" 'story\.document\.json[\s\S]*?Load[\s\S]*?SaveAsync[\s\S]*?File\.Move' "Story documents must use an atomic JSON repository."
Require-SourcePattern "Services/StorySaveCoordinator.cs" 'class StorySaveCoordinator[\s\S]*?SemaphoreSlim[\s\S]*?QueueSave[\s\S]*?FlushAsync' "Story autosave must be serialized, debounced, and flushable."
Require-MethodPattern "MainWindow.Story.TextEditing.cs" "SaveCurrentStoryRow" 'QueueCurrentStoryDocumentSave' "Text edits must queue JSON persistence instead of writing CSV."
Require-SourcePattern "MainWindow.Story.TextEditing.cs" 'StoryCsvService|ReadRows|WriteRows|\.csv' "Text editing must not depend on CSV infrastructure." -NotMatch
Require-MethodPattern "MainWindow.Story.Persistence.cs" "QueueCurrentStoryDocumentSave" '_storySaveCoordinator\.QueueSave' "The editor must route persistence through the background story save coordinator."
Require-MethodPattern "MainWindow.Story.Persistence.cs" "QueueCurrentStoryDocumentSave" 'WriteRows|ExportChapter' "Normal editor saves must not write section CSV files." -NotMatch
Require-MethodPattern "Services/ProjectTextDataService.cs" "LoadTextRows" 'StoryDocumentRepository[\s\S]*?Load' "Text, voice, and localization views must read canonical story documents."
Require-MethodPattern "Services/ChapterRepairService.cs" "Scan" 'StoryDocumentRepository[\s\S]*?Save' "Chapter repair must update canonical story documents."
Require-SourcePattern "Services/StoryAssetIndexSyncService.cs" 'StoryDocumentRepository[\s\S]*?Save' "Asset-index repairs must update canonical story documents."
Require-MethodPattern "MainWindow.UnrealSync.cs" "RunUnrealSyncFlowAsync" 'PrepareStoryCsvForUnrealSyncAsync[\s\S]*?BuildUnrealSyncChangePlan' "Unreal sync must flush story JSON and export CSV before computing differences."
Require-SourcePattern "MainWindow.Story.TextEditing.cs" 'StoryCsvExporter|StoryCsvService|WriteRows' "Text editing must never generate CSV." -NotMatch
Require-SourcePattern "MainWindow.Story.Persistence.cs" 'StoryCsvExporter|ExportChapter' "Normal story persistence must never generate CSV." -NotMatch

if ($Build) {
    dotnet build GalExcleTools.csproj --configuration Release --runtime win-x64 -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true
    if ($LASTEXITCODE -ne 0) {
        throw "GalExcleTools build failed with exit code $LASTEXITCODE"
    }
}

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Host "  · $_" -ForegroundColor Red }
    Write-Error "$($issues.Count) 个检查未通过（明细见上面列出的条目）。"
    exit 1
}

Write-Host "Source health checks passed."
