using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace GalExcleTools.Services;

/// <summary>
/// TODO 备注的落盘：<c>&lt;工具项目&gt;/Tools/story-todos.json</c>。
///
/// 【为什么放这儿】
/// 和 <c>unreal-sync-state.json</c> 同一个目录 —— 那里放的都是"工具箱自己的账"，
/// 而不是剧情数据。备注属于前者：它是**给你自己看的**，
/// 不能混进要导出给 Unreal 的 CSV 里。
///
/// 【一个项目一个文件】
/// 换项目就换一份备注。理由很直白：行号是"某章第几句"，跨项目没有意义。
///
/// 【读写都不抛】
/// 备注是辅助信息。文件坏了、没权限、JSON 半截 —— 一律当成"没有备注"，
/// 绝不让它把剧情编辑器搞挂。这条和日志文件是同一个规矩。
/// </summary>
internal static class StoryTodoStore
{
    private const string ToolsFolderName = "Tools";
    private const string FileName = "story-todos.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>备注文件的完整路径。项目还没选定的话返回空串。</summary>
    public static string ResolvePath(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return string.Empty;
        }

        return Path.Combine(projectPath, ToolsFolderName, FileName);
    }

    /// <summary>读回来。文件不在、读不动、格式不对 —— 一律返回空列表。</summary>
    public static List<StoryTodoNote> Load(string? projectPath)
    {
        var path = ResolvePath(projectPath);
        if (path.Length == 0 || !File.Exists(path))
        {
            return [];
        }

        try
        {
            var file = JsonSerializer.Deserialize<StoryTodoFile>(File.ReadAllText(path), JsonOptions);
            return file?.Notes?.Where(note => note is not null).ToList() ?? [];
        }
        catch (Exception)
        {
            // 坏了就当没有。**不要**在这里抛，也不要顺手把文件删掉或覆盖掉 ——
            // 万一只是格式升级没跟上，原文件还在，还能手工救回来。
            return [];
        }
    }

    /// <summary>写回去。**先写临时文件再改名**，免得半截文件把上一份盖掉。</summary>
    public static bool Save(string? projectPath, IEnumerable<StoryTodoNote> notes)
    {
        var path = ResolvePath(projectPath);
        if (path.Length == 0)
        {
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var file = new StoryTodoFile
            {
                Notes = notes
                    .OrderBy(note => note.ChapterCode, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(note => note.RowNumber)
                    .ToList()
            };

            var temporaryPath = path + ".tmp";
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(file, JsonOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
            return true;
        }
        catch (Exception)
        {
            // 写不进去也别打断编辑。下次改动还会再试一次。
            return false;
        }
    }
}
