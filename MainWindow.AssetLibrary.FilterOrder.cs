using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GalExcleTools.Services;

namespace GalExcleTools;

/// <summary>
/// 角色滤镜的"顺序落地"：滤镜的顺序**就是**剧情里的 <c>VFX</c> 索引，
/// 所以排序要写回滤镜清单、并同步所有章节（不然剧情引用的滤镜会错位）。
///
/// 和音频那套（ApplyAudioOrderAsync）同一个形状，差别只有三处：
///   · 来源是滤镜清单（不是文件夹里的文件）；
///   · 先 <c>Write</c> 清单（带 <c>_isReorderingCharacterFilters</c> 守卫）；
///   · 同步走 <c>SyncStoryCharacterFilterIndexesWithProgressAsync</c>。
///
/// 旧拖拽处理器（CharacterFilterGridView_DragItemsCompleted）里那份逻辑暂时还在，
/// 等旧网格按文档第二节退休时删掉，届时这个方法就是唯一入口。
/// </summary>
public sealed partial class MainWindow
{
    private async Task ApplyCharacterFilterOrderAsync(IReadOnlyList<CharacterFilterEntry> orderedFilters)
    {
        if (_currentAssetLibrary is null)
        {
            return;
        }

        var filters = CharacterFilterService.Normalize(orderedFilters);
        if (filters.Count == 0)
        {
            return;
        }

        var oldFilters = _characterFilterService.Read(_currentAssetLibrary);
        var indexRemap = CharacterFilterService.BuildIndexRemap(oldFilters, filters);
        // 标签字典按"位置 → 备注"取：同步链要靠它把新旧名字对上（和旧拖拽处理器同一口径）。
        var oldLabels = oldFilters
            .Select((filter, index) => (filter, index))
            .ToDictionary(item => item.index, item => item.filter.Remark);
        var newLabels = filters
            .Select((filter, index) => (filter, index))
            .ToDictionary(item => item.index, item => item.filter.Remark);

        _isReorderingCharacterFilters = true;
        try
        {
            _characterFilterService.Write(_currentAssetLibrary, filters);
        }
        finally
        {
            _isReorderingCharacterFilters = false;
        }

        var syncResult = await SyncStoryCharacterFilterIndexesWithProgressAsync(
            _currentAssetLibrary,
            indexRemap,
            oldLabels,
            newLabels,
            filters.Count);

        TouchAssetLibraryLastEditedAt(_currentAssetLibrary);
        // 两个界面都要重铺：旧的 Expander 网格 + 新骨架的浏览器。
        LoadCharacterFilters(_currentAssetLibrary);
        if (AssetBrowserHost is not null)
        {
            FillAssetBrowserWithCharacterFilters(AssetBrowserHost, _currentAssetLibrary);
        }

        RequestDelayedRefresh();
        if (syncResult.ChangedCsvCount > 0)
        {
            AppendLog(LogKind.Info, $"已同步 {syncResult.ChangedCsvCount} 个章节 CSV 的角色滤镜索引。");
        }

        AppendLog(LogKind.User, "已调整角色滤镜顺序。");
    }
}
