using System;

namespace GalExcleTools;

/// <summary>
/// 项目包 / 素材库包里的清单（包里的 <c>package-manifest.json</c>）。
///
/// 【为什么要放】
/// 这两个包原本只是一份"按目录压出来的 zip"，没有任何自描述：对方拖进工具箱时，
/// 工具只能靠"目录里有没有 <c>Tools/project.meta.json</c>"去猜类型，也答不出
/// "这个项目配套的素材库包是哪一个、你导入了没有"。清单把这几件事写清楚：
///
///   1. **这是什么包**（<see cref="PackageType"/> + <see cref="SchemaVersion"/>）；
///   2. **谁导的、什么时候导的**（工具箱版本 + 时间 + <see cref="PackageId"/>）；
///   3. **对象的身份**（名字 / 代号 / 导出时那个文件夹名）；
///   4. **配套关系**（项目包记着它绑定的素材库名字与文件夹名 —— 对方按同一个名字导入素材库，
///      项目的关联就不会断）；
///   5. **规模**（文件数 / 总字节），对方一眼知道要传多大、导多久。
///
/// 【和章节编辑包的区别】那个是"剧情数据交换件"（<c>TFAC.ChapterEdit</c>，用来差异合并）；
/// 这两个是"整个工作区对象"（项目 / 素材库），发出去是给对方**开局**用的。
/// </summary>
internal sealed class WorkspacePackageManifest
{
    public const string ProjectPackageTypeValue = "TFAC.Project";
    public const string AssetLibraryPackageTypeValue = "TFAC.AssetLibrary";

    /// <summary>清单在 zip 里的条目名。放在**第一个**条目，读取方扫一下条目表就能拿到。</summary>
    public const string FileName = "package-manifest.json";

    public int SchemaVersion { get; set; } = 1;

    /// <summary>包类型：<see cref="ProjectPackageTypeValue"/> 或 <see cref="AssetLibraryPackageTypeValue"/>。</summary>
    public string PackageType { get; set; } = string.Empty;

    public string PackageId { get; set; } = string.Empty;

    public DateTimeOffset ExportedAt { get; set; }

    public string ToolVersion { get; set; } = string.Empty;

    /// <summary>项目名 / 素材库名（给人看的那个）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>项目代号；素材库包留空。</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>导出时那个文件夹名（项目是 <c>项目-xxx</c>，素材库是 <c>素材库-xxx</c>）。</summary>
    public string FolderName { get; set; } = string.Empty;

    /// <summary>项目包才有：它绑定的素材库名字。</summary>
    public string? AssetLibraryName { get; set; }

    /// <summary>项目包才有：它绑定的素材库**文件夹名** —— 对方按同名导入素材库，关联就不会断。</summary>
    public string? AssetLibraryFolderName { get; set; }

    /// <summary>项目包才有：章节数。</summary>
    public int ChapterCount { get; set; }

    /// <summary>打进包里的文件数（不含清单自己）。</summary>
    public int FileCount { get; set; }

    /// <summary>这些文件的总字节。</summary>
    public long TotalBytes { get; set; }
}
