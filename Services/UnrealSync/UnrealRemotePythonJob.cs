using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GalExcleTools.Services;

/// <summary>
/// 在线执行时交给 <c>Tools/UnrealBridge/run_remote_unreal_job.py</c> 的作业描述。
///
/// 字段名是**和脚本约定的 JSON 契约**，所以用 <see cref="JsonPropertyNameAttribute"/> 钉死 ——
/// 不靠 <c>JsonSerializerOptions</c> 的命名策略，免得哪天换个 options 就把协议改了。
/// 改这里的名字必须同步改两个 .py 脚本
/// （<c>run_remote_unreal_job.py</c> 读 engineRoot / projectPath，<c>execute_remote_unreal_job.py</c>
/// 读 scriptPath / environment）。
/// </summary>
internal sealed record UnrealRemotePythonJob(
    [property: JsonPropertyName("engineRoot")] string EngineRoot,
    [property: JsonPropertyName("projectPath")] string ProjectPath,
    [property: JsonPropertyName("scriptPath")] string ScriptPath,
    [property: JsonPropertyName("environment")] IReadOnlyDictionary<string, string> Environment)
{
    /// <summary>脚本认识的协议版本；对不上就直接拒绝执行，不做兼容猜测。</summary>
    public const int CurrentProtocolVersion = 1;

    [JsonPropertyName("protocolVersion")]
    public int ProtocolVersion { get; init; } = CurrentProtocolVersion;
}
