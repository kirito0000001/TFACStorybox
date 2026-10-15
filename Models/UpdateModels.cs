using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalExcleTools;

/// <summary>
/// 更新通道（设计 §7.1）。两条线共用同一套检查流程，差别只在「挑哪一条 Release」：
/// 正式版只认 <c>prerelease == false</c>，测试版认全部（平手时正式版优先）。
/// </summary>
internal enum UpdateChannel
{
    Stable,
    Beta
}

/// <summary>
/// 一次检查更新的结果分类。每种情况对应设计 §7.2 里的一句话——**那些文案本身也是契约**，
/// 所以分类和文案都写死在 <c>UpdateService</c> 里，不散落。
/// </summary>
internal enum UpdateCheckStatus
{
    /// <summary>还没查过（初始态）。</summary>
    NotChecked,

    /// <summary>确认当前就是最新版。</summary>
    Latest,

    /// <summary>确认有更高版本，且 plan 五项齐全（§11.6）——只有它能进入下载与覆盖。</summary>
    UpdateAvailable,

    /// <summary>列表为空 / 该通道内没有 Release。</summary>
    NoRelease,

    /// <summary>选中的 Release 里没有 <c>toolbox-update.json</c> 附件。</summary>
    ManifestMissing,

    /// <summary>清单里没有匹配本机 runtime 的包。</summary>
    AssetMissing,

    /// <summary>清单要求手动迁移。</summary>
    ManualMigrationRequired,

    /// <summary>本机版本比线上还新（绝不降级）。</summary>
    LocalIsNewer,

    /// <summary>Gitee 限流（403/401/429 + 限流文案）。</summary>
    RateLimited,

    /// <summary>403 但不是限流文案 = 拒绝访问。</summary>
    RepositoryForbidden,

    /// <summary>404 = 仓库不存在或无权访问。</summary>
    RepositoryMissing,

    /// <summary>其它失败（网络/超时/JSON 读不出来/清单不完整）。</summary>
    Failed
}

/// <summary>
/// Gitee 请求失败的分类。设计 §7.2 里「403 的两种含义」就在这里分开：
/// 同一个 403，body 带限流文案 = 限流，其它 = 拒绝访问。
/// </summary>
internal enum GiteeFailureKind
{
    None,
    RateLimited,
    Forbidden,
    Unauthorized,
    NotFound,
    ServerError,
    Network,
    Timeout,
    BadPayload
}

/// <summary>
/// 界面上的检查状态机（设计 §11.6，fail-closed）。
/// 只有 <see cref="UpdateAvailable"/> 允许进入下载与覆盖；<see cref="Failed"/> **绝不**退化成「已是最新版本」。
/// </summary>
internal enum UpdatePhase
{
    Idle,
    Checking,
    Ready,
    UpdateAvailable,
    Failed
}

/// <summary>
/// 本机更新包的 runtime 标识（设计 §19 已拍板：只发 win-x64，另两项是给将来留的口子）。
/// </summary>
internal static class UpdateRuntime
{
    public const string WinX64 = "win-x64";
    public const string WinX86 = "win-x86";
    public const string WinArm64 = "win-arm64";

    /// <summary>按进程架构挑包；认不出来的一律按 win-x64（发布侧也只发这一种）。</summary>
    public static string Current => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X86 => WinX86,
        Architecture.Arm64 => WinArm64,
        _ => WinX64
    };
}

/// <summary>
/// Gitee 仓库坐标。只有一个源（用户 m00001 的要求：只留 Gitee 更新渠道），
/// 所以这里是常量而不是设置项——设置里存 URL 只会留下「settings 里还是旧地址」的坑。
/// </summary>
internal sealed record GiteeRepository(string Owner, string Name)
{
    /// <summary>本工具的更新仓库。</summary>
    public static GiteeRepository Default { get; } = new("xiaojie578", "TFACStorybox");

    public string ApiRoot => $"https://gitee.com/api/v5/repos/{Owner}/{Name}";

    /// <summary>Release 列表。<c>?per_page=100</c> 由调用方拼（必须带，见设计 §4.3）。</summary>
    public string ReleasesApiUrl => $"{ApiRoot}/releases";

    public string ReleasePageUrl => $"https://gitee.com/{Owner}/{Name}/releases";

    public string BuildReleaseTagUrl(string tag) => $"https://gitee.com/{Owner}/{Name}/releases/tag/{tag}";

    public override string ToString() => $"{Owner}/{Name}";
}

/// <summary>
/// Gitee Release 对象。字段名是 snake_case，所以必须逐个写 <see cref="JsonPropertyNameAttribute"/>
/// ——大小写不敏感**不**能把 <c>tag_name</c> 认成 <c>TagName</c>，下划线是实打实的差异。
/// </summary>
internal sealed class GiteeReleaseDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    /// <summary>
    /// <c>prerelease</c> 原样留着，不直接声明成 <c>bool</c>。
    ///
    /// Gitee 官方 swagger 把它声明成 **string**，而实测响应里又是真 bool（设计 §4.7）——
    /// 官方 Response Class 整块不可信。源项目（FantasyTools）为此专门留了这个绕法，照抄。
    /// </summary>
    [JsonPropertyName("prerelease")]
    public JsonElement PrereleaseValue { get; set; }

    /// <summary>
    /// 是不是预发布版。★ 过滤**绝不能**写成 <c>!prerelease</c>：
    /// 字符串 <c>"false"</c> 在 C# 里是真值，一旦按真值算，正式版会被当预发布版全过滤掉，
    /// stable 通道从此永远挑不到包。
    /// </summary>
    [JsonIgnore]
    public bool Prerelease => UpdateJsonReading.ReadBooleanLoose(PrereleaseValue);

    [JsonPropertyName("assets")]
    public List<GiteeAttachmentDto>? Assets { get; set; }
}

/// <summary>
/// 发行版附件。注意：Gitee 的 <c>assets</c> 里**同时装着上传的附件和两个自动源码归档**
/// （<c>archive/refs/tags/{tag}.zip</c> / <c>.tar.gz</c>）——所以更新包一律走
/// <c>/releases/{id}/attach_files</c>，不从这个字段里猜（设计 §4.2）。
/// </summary>
internal sealed class GiteeAttachmentDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>字节数。Gitee 现在返回 Int32，用 long 接住将来变大。</summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
}

/// <summary>Release 附件里的更新清单 <c>toolbox-update.json</c>（设计 §6.1）。</summary>
internal sealed class ToolboxUpdateManifestDto
{
    public int SchemaVersion { get; set; }

    /// <summary>固定 <c>"GalExcleTools"</c>（ASCII）：用于路径与一致性校验。</summary>
    public string? ToolboxStableKey { get; set; }

    /// <summary>「TFAC剧情箱-轮椅版」——只用于界面显示，全角冒号在这里没问题。</summary>
    public string? DisplayName { get; set; }

    public string? Version { get; set; }

    /// <summary><c>stable</c> / <c>beta</c>。</summary>
    public string? Channel { get; set; }

    public string? PublishedAt { get; set; }

    /// <summary>为空时用 Release 的页面地址兜底。</summary>
    public string? ReleaseNotesUrl { get; set; }

    /// <summary>为空时用 Release 的 body 兜底；仍为空则显示「本次 Release 没有填写更新公告。」</summary>
    public string? ReleaseNotes { get; set; }

    public bool RequiresManualMigration { get; set; }

    public bool RequiresRestart { get; set; }

    public List<ToolboxUpdateAssetDto>? Assets { get; set; }

    /// <summary>按 runtime 找包。匹配用 OrdinalIgnoreCase：附件名的大小写随打包器变。</summary>
    public ToolboxUpdateAssetDto? FindAsset(string runtime) =>
        Assets?.FirstOrDefault(asset => string.Equals(asset.Runtime?.Trim(), runtime, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// ★ 增量包（2026-10-06）：相对某个**基准版**只带"变化/新增"文件的小包。
    /// **只在发布侧确实产出了增量包时才有这个字段** —— 老清单里根本没有它，解析必须容忍缺席；
    /// 本机版本不等于 <c>baseVersion</c> 时也不能用它（差异集不成立）。
    /// </summary>
    public ToolboxUpdateDeltaDto? Delta { get; set; }
}

/// <summary>清单里的增量包（artifact 名 + 哈希 + 基准版）。</summary>
internal sealed class ToolboxUpdateDeltaDto
{
    /// <summary>这个增量包是相对哪一版做的：本机版本必须正好等于它。</summary>
    public string? BaseVersion { get; set; }

    public string? FileName { get; set; }

    public string? Sha256 { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>变化/新增的文件数（只用于展示与诊断）。</summary>
    public int ChangedCount { get; set; }

    /// <summary>要删除的文件数（只用于展示与诊断）。</summary>
    public int DeleteCount { get; set; }
}

/// <summary>清单里的一个包（设计 §6.1）。<c>downloadUrl</c> 为空是正常的：
/// 真实地址由客户端拿 <c>fileName</c> 去 attach_files 列表里匹配，Gitee 附件 URL 不能猜。</summary>
internal sealed class ToolboxUpdateAssetDto
{
    public string? Runtime { get; set; }

    /// <summary>★ 恒为**拼接后的整包**名（分卷时它自己不是附件，契约 §3 铁律 1）。</summary>
    public string? FileName { get; set; }

    /// <summary>64 位小写十六进制；★ 恒为**拼接后整包**的哈希。</summary>
    public string? Sha256 { get; set; }

    /// <summary>★ 恒为**拼接后整包**的字节数。</summary>
    public long SizeBytes { get; set; }

    public string? DownloadUrl { get; set; }

    /// <summary>
    /// ★ 分卷（2026-10-06 契约 §3）：整包超过 Gitee 单附件 100 MiB 时，发布侧把同一个 zip
    /// 按字节切成若干卷，这里列出各卷 —— 数组顺序就是拼接顺序，每一项描述的是**该卷自己**。
    /// **只在分卷时存在**：不分卷（以及所有老清单）里根本没有这个字段，解析必须容忍它缺席。
    /// </summary>
    public List<ToolboxUpdatePartDto>? Parts { get; set; }
}

/// <summary>分卷里的一卷（契约 §2/§3）：卷名形如 <c>{整包基名}.partNN.zip</c>。</summary>
internal sealed class ToolboxUpdatePartDto
{
    public string? FileName { get; set; }

    /// <summary>64 位小写十六进制 —— 这一卷自己的哈希。</summary>
    public string? Sha256 { get; set; }

    /// <summary>这一卷自己的字节数。</summary>
    public long SizeBytes { get; set; }
}

/// <summary>
/// 一个「下载单元」：分卷时是清单里的一卷，不分卷时就是整包自己
/// （契约 §4 第 0 行：<c>payloads = parts 非空 ? parts : [整包]</c>）。
/// 下载器只认这个类型 —— 于是「一卷」和「整包」走的是同一条代码路径。
/// </summary>
internal sealed record UpdatePackagePart(string FileName, string Sha256, long SizeBytes, string DownloadUrl);

/// <summary>
/// 一次更新的「plan」：版本 / runtime / 文件名 / SHA-256 / 大小 / 下载地址。
/// 它必须五项齐全才允许进入覆盖（设计 §11.6 第 ③ 条：版本更高 **且** plan 齐 —— 两维判定，
/// 一维缺失就退回 <see cref="UpdatePhase.Failed"/>；Android 端 versionCode 事故就是这么来的）。
/// </summary>
internal sealed record UpdatePackagePlan(
    string Version,
    string Runtime,
    string FileName,
    string Sha256,
    long SizeBytes,
    string DownloadUrl,
    IReadOnlyList<UpdatePackagePart>? Parts = null)
{
    /// <summary>卷号上限（契约 §1：两位数字编号 `part01`–`part99`）。</summary>
    public const int MaxPartCount = 99;

    /// <summary>这一版是分卷发出来的（契约 §3）：空数组按不分卷处理。</summary>
    public bool IsSplit => Parts is { Count: > 0 };

    /// <summary>
    /// ★ 这一版走的是**增量包**（2026-10-06）：只下变化文件。
    /// 覆盖器会拿 <see cref="DeltaBaseVersion"/> 与本机版本对齐、并逐个核对旧内容指纹。
    /// </summary>
    public bool IsDelta { get; init; }

    /// <summary>增量包的基准版本（<see cref="IsDelta"/> 为真时才有值）。</summary>
    public string? DeltaBaseVersion { get; init; }

    /// <summary>真正要逐个下载的单元（契约 §4 第 0 行）。</summary>
    public IReadOnlyList<UpdatePackagePart> Payloads =>
        IsSplit ? Parts! : [new UpdatePackagePart(FileName, Sha256, SizeBytes, DownloadUrl)];

    /// <summary>
    /// 五项是否齐全。SHA-256 必须是 64 位十六进制，大小必须为正。
    /// 分卷时（契约 §3 铁律 1/3）另加三样：**每卷**都要有卷名 / 合法哈希 / 正的大小 / 下载地址，
    /// 卷数 ≤ <see cref="MaxPartCount"/>，且**各卷之和 == 整包大小** ——
    /// 最后这条是"清单被改坏 / 谁少下了一卷"最早能现形的地方。
    /// </summary>
    public static bool IsComplete(UpdatePackagePlan? plan)
    {
        if (plan is null
            || string.IsNullOrWhiteSpace(plan.Version)
            || string.IsNullOrWhiteSpace(plan.Runtime)
            || string.IsNullOrWhiteSpace(plan.FileName)
            || !IsSha256(plan.Sha256)
            || plan.SizeBytes <= 0)
        {
            return false;
        }

        if (!plan.IsSplit)
        {
            return !string.IsNullOrWhiteSpace(plan.DownloadUrl);
        }

        // ★ 分卷时整包本身不是附件（契约 §2），所以**不要求** plan.DownloadUrl 有值：
        //   它只在「不分卷」那条路上指着唯一的那个附件。
        var parts = plan.Parts!;
        if (parts.Count > MaxPartCount)
        {
            return false;
        }

        long total = 0;
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part.FileName)
                || !IsSha256(part.Sha256)
                || part.SizeBytes <= 0
                || string.IsNullOrWhiteSpace(part.DownloadUrl))
            {
                return false;
            }

            total += part.SizeBytes;
        }

        return total == plan.SizeBytes;
    }

    public static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    /// <summary>统一成小写，比较时才不会因为大小写误判。</summary>
    public static string NormalizeSha256(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}

/// <summary>一次检查的输入。</summary>
internal sealed record UpdateCheckRequest(
    UpdateChannel Channel,
    string CurrentVersion,
    string Runtime,
    int TimeoutSeconds);

/// <summary>
/// 一次检查的输出。<see cref="Message"/> 是**给用户看的那句话**（设计 §7.2）；
/// <see cref="Detail"/> 是给日志看的细节，两者不混。
/// </summary>
internal sealed record UpdateCheckResult(UpdateCheckStatus Status, string Message)
{
    /// <summary>线上最新版本（挑中的那条 Release / 清单里的版本）。</summary>
    public string? LatestVersion { get; init; }

    /// <summary>挑中的 Release tag。</summary>
    public string? ReleaseTag { get; init; }

    public string? ReleaseName { get; init; }

    public string? ReleaseNotes { get; init; }

    public string? ReleaseNotesUrl { get; init; }

    public bool RequiresManualMigration { get; init; }

    /// <summary>只有 <see cref="UpdateCheckStatus.UpdateAvailable"/> 才有值，且五项齐全。</summary>
    public UpdatePackagePlan? Plan { get; init; }


    /// <summary>响应头里的剩余配额（Gitee 在限流响应里反而不带这个头，所以只能记账）。</summary>
    public int? RateLimitRemaining { get; init; }

    /// <summary>日志细节（HTTP 状态、body 片段、列表条数……）。</summary>
    public string? Detail { get; init; }

    /// <summary>这一条结果是不是「有更新可用」——覆盖流程的唯一入口条件。</summary>
    public bool HasUpdate => Status == UpdateCheckStatus.UpdateAvailable && UpdatePackagePlan.IsComplete(Plan);
}

/// <summary>「测试 Gitee 连接」的结果。</summary>
internal sealed record UpdateConnectionTestResult(bool Ok, string Message)
{
    public int? RemainingQuota { get; init; }
}

/// <summary>
/// zip 包内的 <c>update-package.json</c>（设计 §6.2）—— 包自己的体检单。
/// 客户端在下载后校验一次，updater 解压后再校验一次（两道都要，防的是半截包与发错包）。
/// </summary>
internal sealed class ToolboxPackageManifestDto
{
    public int SchemaVersion { get; set; }

    /// <summary>固定 <c>"GalExcleTools"</c>（ASCII）：路径与一致性校验都用它。</summary>
    public string? ToolboxStableKey { get; set; }

    public string? Version { get; set; }

    /// <summary><c>win-x64</c> / <c>win-x86</c> / <c>win-arm64</c>。</summary>
    public string? Runtime { get; set; }

    /// <summary>主程序**文件名**（不是路径）。updater 靠它定位包内的程序根目录。</summary>
    public string? EntryExe { get; set; }

    /// <summary>框架依赖时要求的 Windows App Runtime（例如 <c>2.0</c>）；自包含为空。</summary>
    public string? WindowsAppSdkRuntime { get; set; }

    /// <summary>★ 这是个增量包（不是整包）：包内只有变化文件，且另带基准版与删除清单。</summary>
    public bool Delta { get; set; }

    /// <summary>增量包的基准版本。</summary>
    public string? BaseVersion { get; set; }

    /// <summary>增量包里的变化文件（<c>path</c> / <c>sizeBytes</c> / <c>sha256</c> / <c>baseSha256</c>）。</summary>
    public List<ToolboxPackageFileDto>? Files { get; set; }

    /// <summary>要删除的相对路径（相对基准版被移除的文件）。</summary>
    public List<string>? Delete { get; set; }

    public string? CreatedAt { get; set; }
}

/// <summary>增量包里的一个文件（<c>update-package.json</c> 的 <c>files[]</c>）。</summary>
internal sealed class ToolboxPackageFileDto
{
    public string? Path { get; set; }

    public long SizeBytes { get; set; }

    public string? Sha256 { get; set; }

    /// <summary>旧内容指纹（新增文件没有这一项）。覆盖器拿它核对本机文件，对不上就整体中止。</summary>
    public string? BaseSha256 { get; set; }
}

/// <summary>一次包结构校验的结果（设计 §8.2）。<see cref="Error"/> 就是给用户看的那句话。</summary>
internal sealed record UpdatePackageValidationResult(bool Ok, string? Error)
{
    /// <summary>校验通过时带出包内清单，供上层二次比对。</summary>
    public ToolboxPackageManifestDto? Manifest { get; init; }

    /// <summary>zip 里的条目数（诊断用）。</summary>
    public int EntryCount { get; init; }

    public static UpdatePackageValidationResult Pass(ToolboxPackageManifestDto? manifest, int entryCount) =>
        new(true, null) { Manifest = manifest, EntryCount = entryCount };

    public static UpdatePackageValidationResult Fail(string error, int entryCount = 0) =>
        new(false, error) { EntryCount = entryCount };
}

/// <summary>
/// 下载进度（设计 §8.1）。<see cref="Percent"/> 是**整条更新流程**的百分比：下载段占 8% → 70%。
/// 界面直接显示 <see cref="Stage"/> + <see cref="FormatBytes"/>，不要自己拼。
/// </summary>
internal sealed record UpdateDownloadProgress(string Stage, long BytesReceived, long TotalBytes, int Percent)
{
    /// <summary>「已下 / 总量」。</summary>
    public string FormatBytes() => $"{FormatSize(BytesReceived)} / {FormatSize(TotalBytes)}";

    /// <summary>给人看的大小文本（确认对话框的「包大小」也用它）。</summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (double)(1024L * 1024 * 1024):F2} GB",
        >= 1024L * 1024 => $"{bytes / (double)(1024L * 1024):F1} MB",
        >= 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B",
    };
}

/// <summary>一次「下载并校验」的结果（设计 §8.1）。<see cref="Reused"/> 为真表示走的是缓存复用分支。</summary>
internal sealed record UpdateDownloadResult(bool Ok, bool Reused, string Message)
{
    /// <summary>校验通过的包路径（<c>%LOCALAPPDATA%\GalExcleTools\Updates\{fileName}</c>）。</summary>
    public string? PackagePath { get; init; }

    /// <summary>给日志看的细节，不直接显示给用户。</summary>
    public string? Detail { get; init; }

    public static UpdateDownloadResult Success(string message, string packagePath, bool reused) =>
        new(true, reused, message) { PackagePath = packagePath };

    public static UpdateDownloadResult Failure(string message, string? detail = null) =>
        new(false, false, message) { Detail = detail };
}

/// <summary>覆盖动作的种类（设计 §9.6 的动作集合）。</summary>
internal enum UpdateApplyActionKind
{
    /// <summary>包内有、内容不同 → 整批腾位（改名成 <c>*.old-{版本}</c>）后落位新文件。</summary>
    Replace,

    /// <summary>安装目录有、包内没有、且在白名单内 → 走镜像语义删掉（先腾位，失败可回滚）。</summary>
    Delete,
}

/// <summary>一条覆盖动作（相对路径，一律用 <c>a\b\c</c> 写法）。</summary>
internal sealed record UpdateApplyAction(UpdateApplyActionKind Kind, string RelativePath);

/// <summary>一个文件在盘上的体检数据。<see cref="Sha256"/> 可以是 null（没算过就只比大小）。</summary>
internal sealed record UpdateFileEntry(string RelativePath, long SizeBytes, string? Sha256 = null);

/// <summary>动作集合（设计 §9.6「准备」段的输出）。</summary>
internal sealed record UpdateApplyPlanResult(
    IReadOnlyList<UpdateApplyAction> ToReplace,
    IReadOnlyList<UpdateApplyAction> ToDelete,
    IReadOnlyList<string> Skipped,
    string OldSuffix)
{
    /// <summary>本轮要动盘的文件总数（腾位阶段处理的就是它们）。</summary>
    public int TouchedCount => ToReplace.Count + ToDelete.Count;
}

/// <summary>按白名单把一批路径分成「程序文件 / 用户自己的东西」（给预检与清理用）。</summary>
internal sealed record UpdateWhitelistClassification(
    IReadOnlyList<string> Mirrored,
    IReadOnlyList<string> Untouched);

/// <summary>
/// 宽松读 JSON 的小工具。存在的原因只有一个：Gitee 官方把 <c>prerelease</c> 声明成 string，
/// 而实测是真 bool（设计 §4.7），两种都得认。
/// </summary>
internal static class UpdateJsonReading
{
    public static bool ReadBooleanLoose(JsonElement element, bool fallback = false)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.String:
                var text = element.GetString();
                if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) ? false : fallback;
            case JsonValueKind.Number:
                return element.TryGetDouble(out var number) && number != 0;
            default:
                return fallback;
        }
    }
}

/// <summary>
/// 时间戳的读写。settings.json 里存 ISO-8601 往返格式（<c>"O"</c>），
/// 比较一律用 <see cref="DateTimeOffset"/>，显示才转本地时间。
/// </summary>
internal static class UpdateTimestamp
{
    public static bool TryParse(string? text, out DateTimeOffset value) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    public static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    public static string FormatForDisplay(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
