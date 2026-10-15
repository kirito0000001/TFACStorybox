using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace GalExcleTools.Services.Update;

/// <summary>
/// 白名单的 IO 层（设计 §9.6）：负责把 <c>Scripts\update-whitelist.json</c> 读成
/// <see cref="UpdateWhitelist"/>，读不出来就退回内置的 <see cref="UpdateWhitelist.Default"/>。
/// <para>
/// 另外还负责把「本次生效的规则」抄一份给 updater。理由是脚本住在一个独立进程里，
/// 而且它的工作目录**故意**不在安装目录里（§9.3），所以它读不到安装目录里那份 ——
/// 得有人把规则递到它手上。
/// </para>
/// <para>
/// 为什么读不出来只退回默认而不中止更新：规则文件是**数据**，内置规则是同一份数据编在程序里，
/// 两者由回归用例逐条比对。丢一个文件就让用户升不了级，是把「数据损坏」升级成「功能不可用」；
/// 反过来，规则文件少了几个扩展名才是真危险 —— 那种事只会发生在人手改错时，而
/// <see cref="UpdateWhitelist.FromJson"/> 对缺字段是**直接抛**的（不会悄悄变小）。
/// </para>
/// </summary>
internal static class UpdateWhitelistLoader
{
    /// <summary>
    /// 读规则。<paramref name="baseDirectory"/> 传安装目录（也就是 exe 所在目录）。
    /// 第二个返回值是来源说明，进日志用 —— 事后要能一眼看出这次是不是用了兜底规则。
    /// </summary>
    public static (UpdateWhitelist Whitelist, string Source) Load(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var path = Path.Combine(baseDirectory, UpdateWhitelist.RelativePath);
        if (!File.Exists(path))
        {
            ToolboxLog.Warn($"更新白名单 {path} 不在，本次用内置规则（脚本会拿到同一份内容，更新不受影响）。");
            return (UpdateWhitelist.Default, "内置规则（文件不在）");
        }

        try
        {
            var whitelist = UpdateWhitelist.FromJson(File.ReadAllText(path, Encoding.UTF8));
            return (whitelist, path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            ToolboxLog.Warn($"更新白名单 {path} 读不出来，本次用内置规则。", error);
            return (UpdateWhitelist.Default, "内置规则（文件读不出来）");
        }
    }

    /// <summary>
    /// 把生效中的规则写成 updater 要读的那一份（<c>UpdateRunners\update-whitelist.json</c>）。
    /// **每轮更新都重写**：安装目录里那份可能已被上一轮镜像按「包里没有」删掉了。
    /// </summary>
    public static string WriteRuntimeCopy(UpdateWhitelist whitelist, string runnersDirectory)
    {
        ArgumentNullException.ThrowIfNull(whitelist);
        ArgumentException.ThrowIfNullOrWhiteSpace(runnersDirectory);

        var path = Path.Combine(runnersDirectory, UpdateWhitelist.FileName);
        AtomicFileWriter.WriteAllText(path, Serialize(whitelist), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }

    /// <summary>
    /// 手写 JSON（不走序列化上下文）。一是这份结构归 PowerShell 的 <c>ConvertFrom-Json</c> 读，
    /// 字段名要稳定；二是项目里所有 JSON 都走源生成上下文，这里用 <see cref="Utf8JsonWriter"/>
    /// 反而更直白，也免得为了一个内部文件再注册一个上下文。
    /// </summary>
    private static string Serialize(UpdateWhitelist whitelist)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("toolboxStableKey", UpdateService.ToolboxStableKey);
            writer.WriteString(
                "note",
                "本文件由工具箱在启动热更新之前写出，内容就是安装目录 Scripts\\update-whitelist.json（或缺省内置规则）。"
                + "热更新覆盖器只读这一份 —— 它故意不工作在安装目录里。");
            WriteArray(writer, "extensions", whitelist.Extensions);
            WriteArray(writer, "directories", whitelist.Directories);
            WriteArray(writer, "excludedDirectories", whitelist.ExcludedDirectories);
            WriteArray(writer, "excludedExtensions", whitelist.ExcludedExtensions);
            WriteArray(writer, "excludedNameFragments", whitelist.ExcludedNameFragments);
            WriteArray(writer, "excludedNameSuffixes", whitelist.ExcludedNameSuffixes);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteArray(Utf8JsonWriter writer, string name, IReadOnlyCollection<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }
}
