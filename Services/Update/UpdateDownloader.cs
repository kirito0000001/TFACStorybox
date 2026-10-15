using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace GalExcleTools.Services.Update;

/// <summary>
/// 把更新包下下来并验明正身（设计 §8.1 的 ①–⑩）。这里只做**网络 + 磁盘**，业务判断在
/// <see cref="UpdateService"/>：它给一个已经五项齐全的 <see cref="UpdatePackagePlan"/>，这里交回
/// 「包在哪、能不能用」。
///
/// 四条最容易被简化掉、但这里一条都不能省的规矩：
/// ① 先下到 <c>{packagePath}.download</c>，全部校验通过后才 <see cref="File.Move(string, string)"/> 改名 ——
///    正式包名存在 ⇒ 它一定是完整的（同分区改名是原子的）；
/// ② 进度**节流 350ms**（抄 Android 端 `LauncherUpdateDownloader.java:42`）——100MB 的包会读上千次；
/// ③ 只在「连续 30 秒一个字节都没收到」时判超时 —— 下载**没有**总时长上限（设计 §7.4）；
/// ④ 分卷（2026-10-06 契约 §4）：整包超过 Gitee 单附件 100 MiB 时，发布侧把它切成若干
///    <c>…partNN.zip</c>；这里按清单 <c>parts[]</c> 的顺序逐卷**字节级追加**到同一个 .download，
///    每卷各自过一遍大小 + SHA-256，最后再对拼好的整个文件过一遍大小 + SHA-256。
///    逐卷那道挡「哪一卷坏了」，整包那道挡「顺序/边界错了」——两道都要，缺一道等于没验。
/// </summary>
internal sealed class UpdateDownloader
{
    /// <summary>读写缓冲 128KB（设计 §8.1 ④）。</summary>
    public const int BufferSize = 128 * 1024;

    /// <summary>失败重试次数上限（设计 §8.1 ④）。</summary>
    public const int MaxAttempts = 3;

    /// <summary>进度节流间隔（设计 §8.1 ④）。</summary>
    public static readonly TimeSpan ProgressThrottle = TimeSpan.FromMilliseconds(350);

    /// <summary>重试间隔 = 这个值 × 第几次（设计 §8.1 ④：500ms、1000ms）。</summary>
    public static readonly TimeSpan RetryDelayUnit = TimeSpan.FromMilliseconds(500);

    /// <summary>连续无字节多久算卡死（设计 §8.1 ④）。</summary>
    public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    // 进度号是契约的一部分（设计 §8.1）：下载段 8% → 70%，后面每道校验各推一格，最后 100%。
    private const int DownloadStartPercent = 8;
    private const int DownloadEndPercent = 70;
    private const int SizeCheckPercent = 72;
    private const int ShaCheckPercent = 82;
    private const int StructureCheckPercent = 92;
    private const int DonePercent = 100;

    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    private readonly GiteeReleaseClient _client;
    private readonly UpdateCacheStore _store;

    public UpdateDownloader(GiteeReleaseClient client, UpdateCacheStore store)
    {
        _client = client;
        _store = store;
    }

    /// <param name="preflightTimeoutSeconds">HEAD 预检的超时（下载本身没有总时长限制）。</param>
    public async Task<UpdateDownloadResult> DownloadAndVerifyAsync(
        UpdatePackagePlan plan,
        string expectedStableKey,
        int preflightTimeoutSeconds,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        // fail-closed（设计 §11.6）：五项不全就一步都不往下走。
        if (!UpdatePackagePlan.IsComplete(plan))
        {
            return UpdateDownloadResult.Failure("更新包信息不完整，已停止更新，请到 Gitee 手动下载。", "plan 五项不全。");
        }

        var packagePath = _store.ResolvePackagePath(plan.FileName);
        if (packagePath is null)
        {
            // 包名是远端输入，绝不能被当成路径用（设计 §14）。
            return UpdateDownloadResult.Failure($"更新包的包名不合法（{plan.FileName}），已停止更新。", "fileName 不是纯文件名。");
        }

        ToolboxLog.Info($"开始准备更新包：{plan.FileName}（{plan.SizeBytes} 字节，SHA-256 {ShortSha(plan.Sha256)}）。");

        try
        {
            _store.EnsureRootDirectory();
            var downloadPath = UpdateCacheStore.ResolveDownloadPath(packagePath);

            // ① 复用：上次下好的包还在，且三样都对得上就直接用（省一次 100MB 流量）。
            var reused = TryReusePackage(plan, packagePath, expectedStableKey, progress);
            if (reused is not null)
            {
                return reused;
            }

            // ② 清理上次失败留下的半截包。
            _store.DeleteDownloadLeftovers();

            // ③④ 逐卷下载（契约 §4）：每卷先 HEAD 预检，再**字节级追加**到同一个 .download。
            //     不分卷时 payloads 只有一项（就是整包自己），与历史行为逐字节等价；
            //     分卷时顺序就是清单 parts[] 的数组顺序 —— 拼接顺序不靠猜，只认清单。
            var payloads = plan.Payloads;
            long completed = 0;

            for (var index = 0; index < payloads.Count; index++)
            {
                var payload = payloads[index];
                var label = FormatPartLabel(index, payloads.Count);

                // ③ HEAD 预检：别等 100MB 下完才发现远端拿错了文件（设计 §8.1 ③）。
                //   百分比要按**已完成的字节**算：写到第 2 卷时进度条已经在 49%，这里再报 8% 会让它往回跳。
                Report(progress, $"正在检查更新包{label}...", completed, plan.SizeBytes, MapDownloadPercent(completed, plan.SizeBytes));
                var remoteLength = await _client.HeadContentLengthAsync(payload.DownloadUrl, preflightTimeoutSeconds, cancellationToken);
                if (remoteLength is { } declaredLength && declaredLength != payload.SizeBytes)
                {
                    _store.DeleteDownloadLeftovers();
                    return UpdateDownloadResult.Failure(
                        $"远端包大小与清单不一致{label}：{declaredLength} / {payload.SizeBytes}。请稍后再试或到 Gitee 手动下载。",
                        $"HEAD Content-Length={declaredLength}，清单 sizeBytes={payload.SizeBytes}（{payload.FileName}）。");
                }

                // ④ 下载这一卷（内部负责重试与看门狗），同时算出该卷自己的 SHA-256。
                var downloaded = await DownloadWithRetriesAsync(plan, payload, index, payloads.Count, downloadPath, completed, progress, cancellationToken);
                if (downloaded.Failure is not null)
                {
                    _store.DeleteDownloadLeftovers();
                    return downloaded.Failure;
                }

                // ★ 逐卷校验（契约 §4 第 1 步）：坏卷立刻失败，绝不把「拼了一半」的文件当成品。
                var partBytes = downloaded.TotalBytes - completed;
                if (partBytes != payload.SizeBytes)
                {
                    _store.DeleteDownloadLeftovers();
                    return UpdateDownloadResult.Failure(
                        payloads.Count > 1
                            ? $"更新包第 {index + 1} 卷大小校验失败：{partBytes} / {payload.SizeBytes}"
                            : $"更新包大小校验失败：{partBytes} / {plan.SizeBytes}",
                        payloads.Count > 1
                            ? $"下载到的字节数与清单第 {index + 1} 卷 sizeBytes 不一致（{payload.FileName}）。"
                            : "下载到的字节数与清单 sizeBytes 不一致。");
                }

                var expectedPartSha = UpdatePackagePlan.NormalizeSha256(payload.Sha256);
                if (!string.Equals(downloaded.Sha256, expectedPartSha, StringComparison.Ordinal))
                {
                    _store.DeleteDownloadLeftovers();
                    return UpdateDownloadResult.Failure(
                        payloads.Count > 1
                            ? $"更新包第 {index + 1} 卷 SHA-256 不一致：{downloaded.Sha256}"
                            : $"更新包 SHA-256 不一致：{downloaded.Sha256}",
                        payloads.Count > 1
                            ? $"清单里第 {index + 1} 卷的 SHA-256 是 {payload.Sha256}（{payload.FileName}）。"
                            : $"清单里的 SHA-256 是 {payload.Sha256}。");
                }

                completed = downloaded.TotalBytes;
                ToolboxLog.Info(payloads.Count > 1
                    ? $"第 {index + 1}/{payloads.Count} 卷已下载并校验：{payload.FileName}（{partBytes} 字节，SHA-256 {ShortSha(downloaded.Sha256)}）。"
                    : $"更新包已下载并校验：{payload.FileName}（{partBytes} 字节，SHA-256 {ShortSha(downloaded.Sha256)}）。");
            }

            // ⑤ 大小：拼起来之后的整包大小（契约 §4 第 2 步，与老流程一致，不能省）。
            Report(progress, "正在校验更新包大小...", completed, plan.SizeBytes, SizeCheckPercent);
            if (completed != plan.SizeBytes)
            {
                _store.DeleteDownloadLeftovers();
                return UpdateDownloadResult.Failure(
                    $"更新包大小校验失败：{completed} / {plan.SizeBytes}",
                    "下载到的字节数与清单 sizeBytes 不一致。");
            }

            // ⑥ zip magic：404 错误页在这一步就现原形（设计 §4.5）。
            if (!HasZipMagic(downloadPath))
            {
                _store.DeleteDownloadLeftovers();
                return UpdateDownloadResult.Failure("下载到的不是有效的 zip 文件（可能是错误页）。", "前四字节不是 50 4B 03 04。");
            }

            // ⑦ 整包 SHA-256：★ 分卷时这一道**必须**还按整个文件算 —— 逐卷校验挡的是"哪卷坏了"，
            //    这一道挡的是"顺序/边界错了"（契约 §4 第 2 步）。
            Report(progress, "正在校验 SHA-256...", completed, plan.SizeBytes, ShaCheckPercent);
            var actualSha = ComputeSha256(downloadPath);
            if (!string.Equals(actualSha, UpdatePackagePlan.NormalizeSha256(plan.Sha256), StringComparison.Ordinal))
            {
                _store.DeleteDownloadLeftovers();
                return UpdateDownloadResult.Failure($"更新包 SHA-256 不一致：{actualSha}", $"清单里的 SHA-256 是 {plan.Sha256}。");
            }

            // ⑧ 包身份：这是本工具的包吗、是这一版吗、主程序真的在里面吗（设计 §8.2）。
            Report(progress, "正在校验更新包结构...", completed, plan.SizeBytes, StructureCheckPercent);
            var validation = UpdatePackageValidator.ValidatePackage(downloadPath, expectedStableKey, plan.Version, plan.Runtime);
            if (!validation.Ok)
            {
                _store.DeleteDownloadLeftovers();
                ToolboxLog.Warn($"更新包结构校验失败：{validation.Error}（条目 {validation.EntryCount} 个）");
                return UpdateDownloadResult.Failure(validation.Error ?? "更新包结构校验失败。", $"条目数 {validation.EntryCount}。");
            }

            // ⑨ 落地：同一个分区里的改名是原子的，所以「正式包名存在 ⇒ 包是完整的」。
            File.Move(downloadPath, packagePath, overwrite: true);
            Report(progress, "更新包已准备就绪。", plan.SizeBytes, plan.SizeBytes, DonePercent);

            // ⑩ 清理：别人的 *.download 一律扫掉，包只留最近两个（设计 §8.1 ⑩）。
            _store.DeleteDownloadLeftovers();
            _store.TrimPackages();

            ToolboxLog.Info($"更新包已就绪：{packagePath}（SHA-256 {ShortSha(actualSha)}，条目 {validation.EntryCount} 个）。");
            return UpdateDownloadResult.Success("更新包已准备就绪。", packagePath, reused: false);
        }
        catch (OperationCanceledException)
        {
            // §11.5：取消不是失败，但半截包必须删掉 —— 否则下次会看到一个「看起来完整」的坏包。
            _store.DeleteDownloadLeftovers();
            ToolboxLog.Info("下载更新包被取消，已清理临时文件。");
            throw;
        }
        catch (Exception error)
        {
            _store.DeleteDownloadLeftovers();
            ToolboxLog.Error($"下载更新包失败：{error.Message}", error);
            return UpdateDownloadResult.Failure("下载更新包失败，请稍后重试或到 Gitee 手动下载。", error.Message);
        }
    }

    /// <summary>① 复用分支：大小 → SHA-256 → 包结构，全过才算数；任一不过就删掉重下。</summary>
    private UpdateDownloadResult? TryReusePackage(
        UpdatePackagePlan plan,
        string packagePath,
        string expectedStableKey,
        IProgress<UpdateDownloadProgress>? progress)
    {
        var size = UpdateCacheStore.TryGetFileSize(packagePath);
        if (size is null)
        {
            return null;
        }

        try
        {
            if (size.Value != plan.SizeBytes)
            {
                ToolboxLog.Info($"缓存包大小不符（{size} / {plan.SizeBytes}），删掉重下。");
                UpdateCacheStore.DeleteQuietly(packagePath);
                return null;
            }

            var actualSha = ComputeSha256(packagePath);
            if (!string.Equals(actualSha, UpdatePackagePlan.NormalizeSha256(plan.Sha256), StringComparison.Ordinal))
            {
                ToolboxLog.Info($"缓存包 SHA-256 不符（{ShortSha(actualSha)}），删掉重下。");
                UpdateCacheStore.DeleteQuietly(packagePath);
                return null;
            }

            var validation = UpdatePackageValidator.ValidatePackage(packagePath, expectedStableKey, plan.Version, plan.Runtime);
            if (!validation.Ok)
            {
                ToolboxLog.Info($"缓存包结构不符（{validation.Error}），删掉重下。");
                UpdateCacheStore.DeleteQuietly(packagePath);
                return null;
            }

            Report(progress, "已复用缓存更新包。", plan.SizeBytes, plan.SizeBytes, DonePercent);
            ToolboxLog.Info($"已复用缓存更新包：{packagePath}");
            return UpdateDownloadResult.Success("已复用缓存更新包。", packagePath, reused: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ToolboxLog.Warn($"复用缓存包时读文件失败，改为重新下载：{packagePath}", error);
            UpdateCacheStore.DeleteQuietly(packagePath);
            return null;
        }
    }

    /// <summary>④ 带重试地下载**一卷**。返回「文件里的累计字节数 + 该卷 SHA-256」或「该给用户看的那句话」。</summary>
    private async Task<(long TotalBytes, string Sha256, UpdateDownloadResult? Failure)> DownloadWithRetriesAsync(
        UpdatePackagePlan plan,
        UpdatePackagePart payload,
        int partIndex,
        int partCount,
        string downloadPath,
        long startOffset,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        var label = FormatPartLabel(partIndex, partCount);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (attempt > 1)
            {
                var delay = RetryDelayUnit * (attempt - 1);
                Report(progress, $"连接中断，正在重试 {attempt - 1} / {MaxAttempts}...", startOffset, plan.SizeBytes, DownloadStartPercent);
                ToolboxLog.Warn($"下载更新包{label}失败，{delay.TotalMilliseconds:F0}ms 后重试（第 {attempt} 次）：{lastError?.Message}");
                await Task.Delay(delay, cancellationToken);
            }

            try
            {
                var downloaded = await DownloadOnceAsync(plan, payload, partIndex, partCount, downloadPath, startOffset, progress, cancellationToken);
                return (downloaded.TotalBytes, downloaded.Sha256, null);
            }
            catch (OperationCanceledException)
            {
                // 用户取消：**不重试**（设计 §8.1 ④），交给上层显示「已取消…」。
                throw;
            }
            catch (InvalidDataException error)
            {
                // 远端声明的大小就和清单不符：这是发布侧的问题，重试没有意义。
                ToolboxLog.Warn($"更新包{label}大小与清单不一致：{error.Message}");
                return (0, string.Empty, UpdateDownloadResult.Failure(error.Message, "远端 Content-Length 与清单 sizeBytes 不符。"));
            }
            catch (GiteeRequestException error) when (error.Kind is GiteeFailureKind.Network or GiteeFailureKind.Timeout or GiteeFailureKind.ServerError)
            {
                // 连接类问题：可以重试。
                lastError = error;
            }
            catch (GiteeRequestException error)
            {
                // 404 / 403 / 限流：再试也是同一个结果。
                ToolboxLog.Warn($"下载更新包被 Gitee 拒绝：{error.Message}");
                return (0, string.Empty, UpdateDownloadResult.Failure(DescribeDownloadFailure(error), error.Message));
            }
            catch (Exception error) when (error is IOException or HttpRequestException or TimeoutException)
            {
                lastError = error;
            }
        }

        var detail = lastError?.Message ?? "未知原因";
        ToolboxLog.Warn($"下载更新包{label}连续失败 {MaxAttempts} 次：{detail}");
        return (0, string.Empty, UpdateDownloadResult.Failure($"下载更新包失败（已重试 {MaxAttempts} 次），请稍后重试或到 Gitee 手动下载。", detail));
    }

    /// <summary>
    /// ④ 单次下载一卷（含看门狗）。**追加写**：前几卷已经校验过的字节一个都不动，
    /// 这一卷失败重试时只把它自己那截尾巴截掉重写（<c>SetLength(startOffset)</c>）——
    /// 这就是契约 §4 说的「逐个下载到同一个临时文件（追加写）」。
    /// 该卷的 SHA-256 **边写边算**（<see cref="IncrementalHash"/>），省掉回头再读一遍 30MB。
    /// </summary>
    private async Task<(long TotalBytes, string Sha256)> DownloadOnceAsync(
        UpdatePackagePlan plan,
        UpdatePackagePart payload,
        int partIndex,
        int partCount,
        string downloadPath,
        long startOffset,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var attemptSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // 看门狗：只在「连续 StallTimeout 没有字节」时掐断；每读到一段就重新上弦。
        using var watchdog = new Timer(_ => attemptSource.Cancel(), null, StallTimeout, Timeout.InfiniteTimeSpan);

        HttpResponseMessage? response = null;
        try
        {
            response = await _client.OpenDownloadResponseAsync(payload.DownloadUrl, attemptSource.Token);

            var declaredLength = response.Content.Headers.ContentLength;
            if (declaredLength is { } declared && declared != payload.SizeBytes)
            {
                throw new InvalidDataException(partCount > 1
                    ? $"远端第 {partIndex + 1} 卷大小与清单不一致：{declared} / {payload.SizeBytes}"
                    : $"远端包大小与清单不一致：{declared} / {payload.SizeBytes}");
            }

            var buffer = new byte[BufferSize];
            long received = 0;
            var stopwatch = Stopwatch.StartNew();

            // ★ 必须是 null，绝不能写 TimeSpan.MinValue：`elapsed - TimeSpan.MinValue` 一定溢出，
            //   抛 "TimeSpan overflowed because the duration is too long"（.NET 的 TimeSpan 溢出）。
            //   M3 就是这么写的，而当时的用例都传 progress=null 才侥幸没炸 —— 真实界面**永远**传进度条，
            //   于是这条路一走就立刻报「下载更新包失败」。分卷用例第一次带进度收集器跑才把它照出来。
            TimeSpan? lastReportAt = null;
            var lastPercent = DownloadStartPercent;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            await using var source = await response.Content.ReadAsStreamAsync(attemptSource.Token);
            await using var destination = new FileStream(
                downloadPath,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                useAsync: true);

            // 前几卷已经校验过的字节保持原样，只把这一卷上一把没写完的尾巴截掉。
            destination.SetLength(startOffset);
            destination.Position = startOffset;

            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), attemptSource.Token)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), attemptSource.Token);
                hash.AppendData(buffer, 0, read);
                received += read;
                watchdog.Change(StallTimeout, Timeout.InfiniteTimeSpan);

                if (progress is null)
                {
                    continue;
                }

                // 进度按**整个拼接文件**算（契约 §4：逐卷下但用户看到的是一个包）。
                var total = startOffset + received;
                var elapsed = stopwatch.Elapsed;
                var percent = MapDownloadPercent(total, plan.SizeBytes);
                if (lastReportAt is { } last && elapsed - last < ProgressThrottle && percent < DownloadEndPercent)
                {
                    // ★ 节流（设计 §8.1 ④）：不节流的话 100MB 的包会刷出上千次进度回调。
                    continue;
                }

                if (percent <= lastPercent && percent < DownloadEndPercent)
                {
                    continue;
                }

                lastReportAt = elapsed;
                lastPercent = percent;
                progress.Report(new UpdateDownloadProgress(
                    partCount > 1 ? $"正在下载更新包（第 {partIndex + 1}/{partCount} 卷）..." : "正在下载更新包...",
                    total,
                    plan.SizeBytes,
                    percent));
            }

            await destination.FlushAsync(attemptSource.Token);
            return (startOffset + received, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 看门狗掐断的（用户没取消）→ 当成可重试的超时。
            throw new TimeoutException($"连续 {StallTimeout.TotalSeconds:F0} 秒没有收到任何数据");
        }
        finally
        {
            response?.Dispose();
        }
    }

    /// <summary>进度/日志里的卷标签：只有一卷时不加后缀，老文案一个字都不变。</summary>
    private static string FormatPartLabel(int partIndex, int partCount) =>
        partCount > 1 ? $"（第 {partIndex + 1}/{partCount} 卷）" : string.Empty;

    private static int MapDownloadPercent(long received, long total)
    {
        if (total <= 0)
        {
            return DownloadStartPercent;
        }

        var ratio = Math.Clamp((double)received / total, 0d, 1d);
        var percent = DownloadStartPercent + (int)Math.Round((DownloadEndPercent - DownloadStartPercent) * ratio);
        return Math.Clamp(percent, DownloadStartPercent, DownloadEndPercent);
    }

    private static string DescribeDownloadFailure(GiteeRequestException error) => error.Kind switch
    {
        GiteeFailureKind.RateLimited => "Gitee 接口访问过于频繁，请稍后再试。",
        GiteeFailureKind.Forbidden => "Gitee 拒绝了对该仓库的访问。",
        GiteeFailureKind.Unauthorized => "Gitee 拒绝了对该仓库的访问。",
        GiteeFailureKind.NotFound => "找不到更新包，可能已被发布侧删除，请到 Gitee 手动下载。",
        GiteeFailureKind.ServerError => $"Gitee 服务器返回错误（HTTP {error.StatusCode}），请稍后重试。",
        _ => "下载更新包失败，请稍后重试或到 Gitee 手动下载。"
    };

    private static bool HasZipMagic(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[4];
            var count = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            return count == header.Length && header.SequenceEqual(ZipMagic);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ToolboxLog.Warn($"读取更新包头失败：{path}", error);
            return false;
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ShortSha(string? sha) =>
        string.IsNullOrWhiteSpace(sha) ? "(空)" : sha[..Math.Min(8, sha.Length)];

    private static void Report(IProgress<UpdateDownloadProgress>? progress, string stage, long bytes, long total, int percent) =>
        progress?.Report(new UpdateDownloadProgress(stage, bytes, total, percent));
}
