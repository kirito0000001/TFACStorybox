using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GalExcleTools.Services;
using static GalExcleTools.Services.WorkspacePathUtility;

namespace GalExcleTools;

/// <summary>
/// 音频三类的"顺序落地"：音乐 → <c>BGM</c> 列、环境音 → <c>Scene</c> 列、
/// 特殊音效 → 函数串里的 <c>Scene_n</c>（它不在剧情列里，见 SyncSoundEffectIndexes）。
///
/// 【为什么单独一个方法】三类共用同一套动作，只差"列名 + 用哪个同步方法"：
/// 位置映射 → 记证据日志 → 按新顺序重命名 → 同步所有相关章节 → 重铺界面。
/// 写死某一类就会各写一份、然后慢慢走样（背景图那条已经踩过一次）。
///
/// 【和旧拖拽处理器并存】旧网格（可拖拽的 Expander 版）里那段同步逻辑暂时还留着一份，
/// 等旧网格按文档第二节退休时一并删掉，届时这个方法就是唯一入口。
/// </summary>
public sealed partial class MainWindow
{
    private async Task ApplyAudioOrderAsync(AudioAssetKind kind, IReadOnlyList<string> orderedPaths)
    {
        if (_currentAssetLibrary is null || orderedPaths.Count == 0)
        {
            return;
        }

        var folderPath = GetAudioFolderPath(_currentAssetLibrary, kind);
        var assetLabel = AudioAssetService.GetDisplayName(kind);

        // 映射按**位置**算（剧情里的 BGM / Scene / Scene_n 都是位置），
        // 旧位置 = 改名之前 GetFilePaths 的下标，新位置 = 这次顺序里的下标。
        var beforeOrderedPaths = AudioAssetService.GetFilePaths(folderPath);
        var indexRemap = BuildPositionalAssetIndexRemap(beforeOrderedPaths, orderedPaths);
        var assetLabels = BuildPositionalLabelMaps(beforeOrderedPaths, orderedPaths);
        LogAssetIndexRemapEvidence(assetLabel, beforeOrderedPaths, orderedPaths);

        SetAudioNormalizing(kind, true);
        try
        {
            await NormalizeAudioFilesAsync(kind, folderPath, orderedPaths);
        }
        finally
        {
            SetAudioNormalizing(kind, false);
        }

        AssetIndexSyncResult syncResult;
        if (kind == AudioAssetKind.SoundEffect)
        {
            syncResult = await SyncStorySoundEffectIndexesWithProgressAsync(
                _currentAssetLibrary,
                indexRemap,
                assetLabels.OldLabels,
                assetLabels.NewLabels,
                orderedPaths.Count);
        }
        else
        {
            var isMusic = kind == AudioAssetKind.Music;
            syncResult = await SyncStoryGlobalAssetIndexesWithProgressAsync(
                _currentAssetLibrary,
                assetLabel,
                isMusic ? "BGM" : "Scene",
                indexRemap,
                assetLabels.OldLabels,
                assetLabels.NewLabels,
                orderedPaths.Count);
        }

        TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
        // 只重铺新骨架那一份：旧的音频网格正在按文档第二节退休。
        if (AssetBrowserHost is not null)
        {
            FillAssetBrowserWithAudio(AssetBrowserHost, kind, _currentAssetLibrary);
        }

        RequestDelayedRefresh();
        if (syncResult.ChangedCsvCount > 0)
        {
            AppendLog(LogKind.Info, $"已同步 {syncResult.ChangedCsvCount} 个章节 CSV 的{assetLabel}索引。");
        }

        AppendLog(LogKind.User, $"已调整{assetLabel}顺序并触发自动命名。");
    }

    /// <summary>
    /// 音频三类的**规范化**（建目录 + 按规范顺序重命名），不含任何界面填充。
    ///
    /// 为什么单独抽出来：这段原来在 ReloadAudioFilesAsync 里，而它的调用者只有三个已死的
    /// async void 包装（旧 Expander 的展开入口）—— 也就是打开素材库时**音频不再被规范化** ✗。
    /// 剧情的 BGM / Scene / Scene_n 都是位置索引，位置认的是 GetFilePaths 的规范顺序，
    /// 所以规范化必须挂回打开素材库那条路（见 RefreshAssetLibraryDetailSections）。
    ///
    /// ⚠️ 音频这条**不需要索引同步**：它的重命名顺序就是 GetFilePaths（改名不动位置），
    /// 与背景图不同（背景图的排序把 jpg/webp 也算进去，而剧情的位置只数 png）。
    /// </summary>
    private async Task NormalizeAudioKindAsync(AssetLibraryInfo assetLibrary, AudioAssetKind kind)
    {
        var folderPath = GetAudioFolderPath(assetLibrary, kind);
        Directory.CreateDirectory(folderPath);

        if (IsAudioNormalizing(kind))
        {
            return;
        }

        SetAudioNormalizing(kind, true);
        try
        {
            await NormalizeAudioFilesAsync(kind, folderPath);
        }
        finally
        {
            SetAudioNormalizing(kind, false);
        }
    }
}
