using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GalExcleTools.Services.Update;

/// <summary>
/// 热更新那条线专用的序列化上下文：Gitee 的 JSON 是 snake_case、更新清单是 camelCase，
/// 两者都靠 DTO 上显式的 <c>JsonPropertyName</c> 认字段，这里只负责生成序列化代码。
///
/// 大小写不敏感是为了清单里的大小写手误也能读出来（Gitee 官方把 <c>prerelease</c> 声明成 string，
/// 而实测又是真 bool，所以那个字段走 <see cref="System.Text.Json.JsonElement"/> 而不是 bool）。
///
/// 本项目其余 JSON 走反射序列化（<c>PublishTrimmed=False</c>，反射可用）；
/// 这里单独开一个源生成上下文，是为了让这条链路的 DTO 契约在编译期就固定下来。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GiteeReleaseDto))]
[JsonSerializable(typeof(List<GiteeReleaseDto>))]
[JsonSerializable(typeof(GiteeAttachmentDto))]
[JsonSerializable(typeof(List<GiteeAttachmentDto>))]
[JsonSerializable(typeof(ToolboxUpdateManifestDto))]
[JsonSerializable(typeof(ToolboxUpdatePartDto))]
[JsonSerializable(typeof(List<ToolboxUpdatePartDto>))]
[JsonSerializable(typeof(ToolboxPackageManifestDto))]
internal sealed partial class UpdateJsonContext : JsonSerializerContext
{
}
