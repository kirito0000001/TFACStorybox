using System;
using System.Text.Json;

namespace GalExcleTools.Services;

/// <summary>
/// 解 <c>loudnorm=print_format=json</c> 吐在 stderr 上的那一段 JSON。
///
/// **为什么不能直接 <c>JsonDocument.Parse(stderr)</c>**：那一段 JSON 后面还跟着 ffmpeg
/// 自己的收尾日志（summary、size=N/A、speed=…），整段喂给解析器会报
/// "Additional text encountered after finished reading JSON content"。
/// 必须<b>按大括号配对</b>把 JSON 那一段抠出来，而且要注意它前面还有几十行进度日志
/// —— 所以是「结束位置最靠后的那个对象」，不是第一个 <c>{</c>。
///
/// 实测踩过的坑（写在这里省得下次再踩）：
/// ① 用 <c>LastIndexOf('{')</c> 当起点 → 被字符串里的花括号骗到，解析失败；
/// ② 拿纯正弦当测试素材 → BS.1770 的 gate 把整段判成静音，量出 <c>I = -70 LUFS</c>，
///    看起来像「程序坏了」，其实是素材没有可测的响度（真实语音才有）。
/// </summary>
internal static class AudioLoudnormJson
{
    /// <summary>
    /// 从 ffmpeg 的 stderr 里解出测量值。解不出来返回 null
    /// （调用方据此退回单遍动态模式 / 判成「测不出」）。
    /// </summary>
    public static VoiceLoudnessMeasurement? Parse(string ffmpegOutput)
    {
        if (string.IsNullOrWhiteSpace(ffmpegOutput))
        {
            return null;
        }

        var json = ExtractLastJsonObject(ffmpegOutput);
        if (json is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return new VoiceLoudnessMeasurement(
                ReadDouble(root, "input_i") ?? double.NaN,
                ReadDouble(root, "input_tp") ?? double.NaN,
                ReadDouble(root, "input_lra") ?? double.NaN)
            {
                ThresholdLufs = ReadDouble(root, "input_thresh") ?? double.NaN,
                TargetOffsetLu = ReadDouble(root, "target_offset") ?? 0d
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 抠出最后一段**配对完整**的 JSON 对象。
    ///
    /// 三个写错的版本都记在这里（都是实测撞出来的）：
    /// · 第一版 <c>LastIndexOf('{')</c> 当起点 —— 被字符串里的花括号骗到；
    /// · 第二版「从后往前扫、按引号奇偶判断在不在串内」—— 更糟：尾随普通文本会把
    ///   引号计数带偏，真正那个 <c>{</c> 被判成「在字符串里」而跳过；
    /// · 第三版从后往前找候选 —— 栽在嵌套对象上（<c>{"a":{"b":1}}</c> 只抠出 <c>{"b":1}</c>）。
    ///
    /// 现在这版：**候选从左往右走，保留「结束位置最靠后」的那个**。
    /// 结束最靠后 ⇒ 它是最后一个完整的顶层对象（先出现的嵌套对象结束得更早，会被盖掉）。
    /// 复杂度最坏 O(n²)，而实际输入只有几十行日志，不值得为它写更聪明的解析。
    /// </summary>
    internal static string? ExtractLastJsonObject(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        string? best = null;
        var bestEnd = -1;
        var candidate = text.IndexOf('{');
        while (candidate >= 0)
        {
            var length = MeasureBalancedObjectLength(text, candidate);
            if (length > 0)
            {
                var end = candidate + length - 1;
                if (end > bestEnd)
                {
                    bestEnd = end;
                    best = text.Substring(candidate, length);
                }

                // 跨过这个对象继续找：后面可能有结束得更晚的对象。
                candidate = text.IndexOf('{', end + 1);
            }
            else
            {
                candidate = text.IndexOf('{', candidate + 1);
            }
        }

        return best;
    }

    /// <summary>
    /// 从 <paramref name="start"/> 开始能不能配成一个完整对象；能就返回它的长度，不能返回 -1。
    /// 括号计数会跳过字符串内容并正确处理 <c>\"</c> 转义 ——
    /// 所以 <c>{"a":"}"}</c> 里字符串中那个 <c>}</c> 不会被当成结束。
    /// </summary>
    private static int MeasureBalancedObjectLength(string text, int start)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = start; index < text.Length; index++)
        {
            var character = text[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return index - start + 1;
                    }

                    if (depth < 0)
                    {
                        // 配不上了：这个候选不是对象开头。
                        return -1;
                    }

                    break;
            }
        }

        return -1;
    }

    /// <summary>
    /// loudnorm 的 JSON 里数字是**字符串**（<c>"input_i" : "-16.00"</c>，带引号），
    /// 所以两种形态都要吃。认不出来返回 null。
    /// </summary>
    private static double? ReadDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number)
        {
            return property.GetDouble();
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            var raw = property.GetString();
            if (double.TryParse(
                    raw,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var value))
            {
                return value;
            }
        }

        return null;
    }
}
