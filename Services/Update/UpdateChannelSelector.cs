using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GalExcleTools.Services.Update;

/// <summary>
/// 从 Release 列表里挑出这一次要用的那一条（设计 §7.1）。
///
/// 纯逻辑：没有网络、没有文件、没有时间依赖——喂一个列表就能断言。这样「正式版只认
/// <c>prerelease == false</c>」「测试版平手时正式版优先」这些规则不必真的连 Gitee 才能验证。
/// </summary>
internal static class UpdateChannelSelector
{
    /// <summary>
    /// 正式版通道只取 <c>prerelease == false</c> 的；测试版通道取全部（含正式版）。
    /// 排序：版本降序 → 平手时正式版优先 → 再平手比 <c>created_at</c> 降序。
    /// </summary>
    public static GiteeReleaseDto? SelectRelease(IEnumerable<GiteeReleaseDto>? releases, UpdateChannel channel)
    {
        if (releases is null)
        {
            return null;
        }

        var candidates = channel == UpdateChannel.Stable
            ? releases.Where(release => release is not null && !release.Prerelease)
            : releases.Where(release => release is not null);

        return candidates.OrderBy(release => release, GiteeReleaseComparer.Instance).FirstOrDefault();
    }
}

/// <summary>
/// Release 的排序规则（设计 §7.1）。
///
/// 版本读不懂的 tag 会与所有版本「平手」（<see cref="UpdateVersion.CompareSemantic"/> 解析失败返回 0），
/// 于是交给后面的规则决定顺序——不会因为一条脏 tag 把整个列表打乱。
/// </summary>
internal sealed class GiteeReleaseComparer : IComparer<GiteeReleaseDto>
{
    public static GiteeReleaseComparer Instance { get; } = new();

    public int Compare(GiteeReleaseDto? x, GiteeReleaseDto? y)
    {
        var versionOrder = UpdateVersion.CompareSemantic(y?.TagName, x?.TagName);
        if (versionOrder != 0)
        {
            return versionOrder;
        }

        // 版本相同 → 正式版优先（false 排在 true 前面）。
        var stableOrder = (x?.Prerelease ?? false).CompareTo(y?.Prerelease ?? false);
        if (stableOrder != 0)
        {
            return stableOrder;
        }

        return CompareCreatedAt(x?.CreatedAt, y?.CreatedAt);
    }

    /// <summary>时间晚的排前面；有日期的排在没日期的前面。</summary>
    private static int CompareCreatedAt(string? left, string? right)
    {
        var hasLeft = TryParseCreatedAt(left, out var leftValue);
        var hasRight = TryParseCreatedAt(right, out var rightValue);

        if (hasLeft && hasRight)
        {
            return rightValue.CompareTo(leftValue);
        }

        if (hasLeft)
        {
            return -1;
        }

        return hasRight ? 1 : 0;
    }

    private static bool TryParseCreatedAt(string? text, out DateTimeOffset value) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
}
