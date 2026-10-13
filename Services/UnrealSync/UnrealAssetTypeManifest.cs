using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalExcleTools.Services;

/// <summary>清单里的一条：某个对象路径当时是什么类。</summary>
internal sealed class UnrealAssetTypeEntry
{
    [JsonPropertyName("objectPath")]
    public string ObjectPath { get; set; } = string.Empty;

    /// <summary>
    /// Unreal 报的 <c>asset_class</c>（就是 <c>asset_class_path</c> 的资产名那段）。
    ///
    /// ⚠️ 它是**类名**，不是"它逻辑上是什么"：蓝图化的数据资产在这里就是
    /// <c>Blueprint</c>。期望值必须按这个口径写，否则就是假红。
    /// </summary>
    [JsonPropertyName("assetClass")]
    public string AssetClass { get; set; } = string.Empty;
}

/// <summary>
/// 资产类型清单：**问 Unreal 要来的**类型，不是从文件里猜的。
///
/// 【为什么要它】
/// 同步前检测要能说"这一项应该是 X 类型"。类型从哪来？两条路：
/// - 扫 <c>.uasset</c> 文件头找标记（<see cref="UnrealAssetTypeProbe"/>）——
///   不用跑引擎，但只能认出粗类名（Blueprint / WidgetBlueprint / DataTable），
///   再细的（PaperZDAnimBP 那种）就认不出来了；
/// - **问 Unreal** —— 权威，而且零境走的就是这条：
///   它的 <c>assetClass</c> 来自 Unreal 侧导出时写的清单，检测时只读那份清单。
///
/// 所以这里做的是第二条：同步那个 Python 脚本**已经在 Unreal 里跑了**，
/// 顺手把每个资产的类名写进这份清单（多读几个属性不额外花时间）；
/// 检测时优先读它。
///
/// 【没有清单怎么办】
/// 第一次同步之前清单还不存在 —— 那时候**不判类型**（返回空串，
/// 由 <see cref="UnrealAssetTypeProbe.Matches"/> 当成"不判"），
/// 或者退回落到扫文件头那条路。**绝不能因为"没有清单"就判失败**：
/// 那是假红，比不查更糟。
/// </summary>
internal static class UnrealAssetTypeManifest
{
    /// <summary>清单文件名。同步脚本那边也要写同一个名字 —— 所以是 internal 常量，只有一份。</summary>
    internal const string FileName = "gal-asset-types.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>清单落在同步台自己的目录里，和清单 / 进度 / 结果那几个文件放一起。</summary>
    public static string ResolvePath(string unrealProjectPath)
    {
        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(unrealProjectPath))
            ?? throw new ArgumentException("无法定位虚幻项目目录。", nameof(unrealProjectPath));
        return Path.Combine(projectDirectory, "Saved", "GalExcleTools", FileName);
    }

    /// <summary>
    /// 读清单并做成"对象路径 → 类名"的字典。
    ///
    /// 读不到、读坏了都返回空字典 —— 调用方据此退回"不判类型"，
    /// 而不是把"读不到清单"当成"类型不对"。
    /// </summary>
    public static IReadOnlyDictionary<string, string> Read(string unrealProjectPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string path;
        try
        {
            path = ResolvePath(unrealProjectPath);
        }
        catch (ArgumentException)
        {
            return result;
        }

        if (!File.Exists(path))
        {
            return result;
        }

        try
        {
            var entries = JsonSerializer.Deserialize<List<UnrealAssetTypeEntry>>(
                File.ReadAllText(path),
                ReadOptions);
            if (entries is null)
            {
                return result;
            }

            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.ObjectPath))
                {
                    continue;
                }

                // ⚠️ **不信任磁盘上读来的字符串**。
                //
                // Python 那边 get_editor_property 拿不到字段时返回 None，而 str(None)
                // 就是字面量 "None" —— 旧版脚本写出来的清单里留着的正是这种值。
                // 它必须被当成**空**（= "不判"），而不是一个真实的类名；
                // 当成类名去比对，就会判出一颗假红。
                //
                // 2026-10-13 实测：脚本那侧已经修好了，可**旧清单还在磁盘上**，
                // 而检测读的就是它 —— 于是"改了两遍还是红的"。
                // 教训：**读取侧不能假设写入侧一定规范**，尤其是这种跨语言、
                // 跨进程、还带缓存的边界的。少信一点，少一颗假红。
                var className = entry.AssetClass ?? string.Empty;
                if (className.Trim() is "None" or "null" || className.Trim().Length == 0)
                {
                    className = string.Empty;
                }

                result[entry.ObjectPath] = className;
            }
        }
        catch (JsonException)
        {
            // 清单坏了就当没有：**不判**类型，而不是判失败。
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return result;
    }
}
