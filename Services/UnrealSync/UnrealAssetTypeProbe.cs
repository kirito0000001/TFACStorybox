using System;
using System.IO;
using System.Text;

namespace GalExcleTools.Services;

/// <summary>
/// 从 <c>.uasset</c> 里**读出它是什么类型的资产**（Blueprint / WidgetBlueprint / DataTable…）。
///
/// 【为什么要读它】
/// 同步前检测光知道"文件在不在"是不够的：把 <c>UI_Story</c> 建成了普通 Blueprint、
/// 或者把 <c>DA_LustrationInfor</c> 建成了 DataTable —— 文件都在、同步也"成功"，
/// 但游戏侧取不到东西。那种错最难查，所以检测得能说出"这一项**应该是**什么类型"。
///
/// 【怎么读的】
/// Unreal 把资产的**名称表**放在包文件靠前的位置，里面就有类型名
/// （<c>/Script/UMGEditor.WidgetBlueprint</c> 这种）。扫开头一段字节找标记就够了 ——
/// **不跑 Unreal**，也不需要完整解析包格式。
/// 这跟零境"类型来自磁盘上的导出清单、不跑引擎"是同一个取舍。
///
/// 【读不到 ≠ 不对】
/// 这是从零境那边学来的最要紧一条。它有一句注释原话：
/// 「拿"没读到"当成"父类不对"，会把角色 Item 永久判成不合规 —— 那是**假红**」。
/// 所以这里读不到就返回空串，由调用方决定"不判"（而不是判失败）。
/// </summary>
internal static class UnrealAssetTypeProbe
{
    /// <summary>
    /// 只扫开头这么多字节。名称表在包文件前部，整份读进来没必要 ——
    /// 立绘那种贴图一份好几 MB，而一次检测要扫十几个。
    /// </summary>
    private const int ProbeByteCount = 256 * 1024;

    /// <summary>
    /// 类型标记 → 给人看的类型名。**顺序有意义**：越具体的放前面。
    ///
    /// 例如 WidgetBlueprint 的文件里同时含有 <c>Blueprint</c>（它的基类），
    /// 先命中 WidgetBlueprint 才不会把它认成普通 Blueprint。
    /// 同理 AnimBlueprint 要排在 Blueprint 前面。
    /// </summary>
    private static readonly (string Marker, string DisplayName)[] Markers =
    [
        ("WidgetBlueprint", "WidgetBlueprint"),
        ("AnimBlueprint", "AnimBlueprint"),
        ("Blueprint", "Blueprint"),
        ("DataTable", "DataTable"),
        ("DataAsset", "DataAsset"),
        ("SoundWave", "SoundWave"),
        ("Texture2D", "Texture2D"),
        ("UserDefinedStruct", "UserDefinedStruct"),
        ("MaterialInstance", "MaterialInstance"),
        ("Material", "Material"),
    ];

    /// <summary>
    /// 判断这份资产是什么类型；**读不出来就返回空串**（不是"未知"这种假值，
    /// 空串在调用方那边一律当"不判"）。
    /// </summary>
    public static string Detect(string? diskPath)
    {
        if (string.IsNullOrWhiteSpace(diskPath) || !File.Exists(diskPath))
        {
            return string.Empty;
        }

        string head;
        try
        {
            using var stream = new FileStream(
                diskPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            var buffer = new byte[Math.Min(ProbeByteCount, stream.Length)];
            var read = stream.Read(buffer, 0, buffer.Length);
            // Latin1：一字节一字符，不会因为碰到非 UTF-8 的二进制段而抛异常或丢字节。
            // 这里要的是"这串 ASCII 标记在不在"，不需要真的解码出文字。
            head = Encoding.Latin1.GetString(buffer, 0, read);
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }

        foreach (var (marker, displayName) in Markers)
        {
            if (head.Contains(marker, StringComparison.Ordinal))
            {
                return displayName;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// 读到的类型与期望的是否相符。
    ///
    /// **读不到就返回 true**（当"不判"）：这一条是刻意的 ——
    /// 把读不到当成不对，就会出现零境注释里那种假红。
    /// </summary>
    public static bool Matches(string expectedType, string actualType) =>
        string.IsNullOrWhiteSpace(expectedType) ||
        string.IsNullOrWhiteSpace(actualType) ||
        string.Equals(expectedType, actualType, StringComparison.OrdinalIgnoreCase);
}
