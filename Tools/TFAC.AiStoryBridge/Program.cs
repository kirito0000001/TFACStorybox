using Tfac.AiStoryBridge;

return Run(args);

static int Run(string[] args)
{
    try
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return 0;
        }
        if (!string.Equals(args[0], "generate", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"未知命令：{args[0]}");
        }
        var package = GetOption(args, "--package") ?? throw new ArgumentException("缺少 --package。");
        var docx = GetOption(args, "--docx") ?? throw new ArgumentException("缺少 --docx。");
        var force = args.Contains("--force", StringComparer.OrdinalIgnoreCase);
        var summary = new StoryBridgeGenerator().Generate(
            Path.GetFullPath(package),
            Path.GetFullPath(docx),
            force);
        Console.WriteLine($"已生成：{summary.ResultPath}");
        Console.WriteLine($"来源 {summary.SourceCount} 条，写入 {summary.IncludeCount}，跳过 {summary.SkipCount}，待确认 {summary.ReviewCount}。");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"生成失败：{ex.Message}");
        return 1;
    }
}

static string? GetOption(IReadOnlyList<string> args, string name)
{
    for (var index = 0; index + 1 < args.Count; index++)
    {
        if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[index + 1];
        }
    }
    return null;
}

static void PrintUsage()
{
    Console.WriteLine("TFAC AI 剧情外接程序");
    Console.WriteLine();
    Console.WriteLine("generate --package <任务包目录> --docx <Word原稿.docx> [--force]");
    Console.WriteLine();
    Console.WriteLine("默认不会覆盖已有内容；--force 会先创建 .bak 再重新生成。");
}
