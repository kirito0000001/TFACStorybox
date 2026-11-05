using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace GalExcleTools.Services;

/// <summary>
/// 语音素材的「统一规格」：WAV / PCM 16-bit / 48 kHz。
///
/// 【为什么要统一】UE 播放时会自己转成统一格式（采样率、位深都可能被重采样），
/// 每条语音都转一次就是白白吃掉运行时性能；素材侧先统一好，引擎那条路就只剩直接播。
///
/// 【为什么只统一采样率和位深，不动声道】声道数是**素材本身的属性**
/// （双人对话、立体声混响素材），擅自降成单声道会改变内容。要降是另一个决定。
/// </summary>
internal static class VoiceAudioFormat
{
    public const int SampleRate = 48000;

    public const int BitsPerSample = 16;

    /// <summary>导入时允许的源格式。最终都会被转成 <see cref="SampleRate"/> / <see cref="BitsPerSample"/> 的 wav。</summary>
    public static readonly string[] ImportableExtensions =
    [
        ".wav", ".mp3", ".flac", ".ogg", ".opus", ".m4a", ".aac", ".wma", ".aiff", ".aif", ".caf"
    ];

    /// <summary>
    /// 这个文件是不是已经是统一规格（WAV / 48k / 16bit / PCM）。
    ///
    /// 直接读 RIFF 头判断，**不调用 ffprobe**：导入是高频操作，
    /// 每选一个文件都起一个进程去问，慢且没必要 —— 头部里就写着答案。
    /// 读不懂（不是 RIFF / 结构异常）一律当成"需要转"。
    /// </summary>
    public static bool IsCanonicalWav(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

            if (stream.Length < 44)
            {
                return false;
            }

            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF")
            {
                return false;
            }

            reader.ReadUInt32(); // 文件长度，不关心
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE")
            {
                return false;
            }

            // 逐个 chunk 找 fmt：正常都是第一个，但 fact/LIST 也可能排在前面。
            while (stream.Position + 8 <= stream.Length)
            {
                var chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
                var chunkSize = reader.ReadUInt32();
                if (chunkId == "fmt ")
                {
                    var formatTag = reader.ReadUInt16();
                    reader.ReadUInt16(); // channels
                    var sampleRate = reader.ReadUInt32();
                    reader.ReadUInt32(); // byteRate
                    reader.ReadUInt16(); // blockAlign
                    var bitsPerSample = reader.ReadUInt16();

                    // 1 = PCM，0xFFFE = WAVE_FORMAT_EXTENSIBLE（16bit 时实质也是 PCM）。
                    var isPcm = formatTag is 1 or 0xFFFE;
                    return isPcm && sampleRate == SampleRate && bitsPerSample == BitsPerSample;
                }

                // chunk 按 2 字节对齐，奇数长度要跳一个填充字节。
                stream.Seek(chunkSize + (chunkSize % 2), SeekOrigin.Current);
            }

            return false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 读不开就当"需要转" —— 让 ffmpeg 去试，它给的错误信息比这里清楚。
            return false;
        }
    }

    /// <summary>
    /// 转成统一规格。**同步等待**：导入是一次一个文件的小操作，
    /// 异步反而要在 UI 层多传一层 CancellationToken，收益不抵复杂度。
    /// </summary>
    public static void Convert(
        string ffmpegPath,
        string sourcePath,
        string targetPath,
        out string errorOutput)
    {
        errorOutput = string.Empty;

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        foreach (var argument in BuildArguments(sourcePath, targetPath))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 ffmpeg。");

        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            errorOutput = error;
            throw new InvalidOperationException($"音频转码失败（ffmpeg 退出码 {process.ExitCode}）。");
        }
    }

    private static IEnumerable<string> BuildArguments(string sourcePath, string targetPath)
    {
        yield return "-hide_banner";
        yield return "-loglevel";
        yield return "error";
        yield return "-y";
        yield return "-i";
        yield return sourcePath;
        yield return "-ar";
        yield return SampleRate.ToString();
        yield return "-c:a";
        yield return "pcm_s16le";
        yield return targetPath;
    }
}
