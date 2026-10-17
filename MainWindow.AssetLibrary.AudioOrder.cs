using System;
using System.Collections.Generic;
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
}
