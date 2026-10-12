using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GalExcleTools.Services;

/// <summary>
/// 变量写入的**路线**。同一个"写变量"的动作，两种变量要走两条完全不同的路。
/// </summary>
internal enum UnrealVariableWriteRoute
{
    /// <summary>
    /// **C++ 声明的 UPROPERTY**：值写在蓝图子类的 CDO 上（Python <c>set_editor_property</c> + 保存）。
    ///
    /// 为什么这条能持久：C++ 属性的"声明"在 C++ 里，编辑器不会去重建它；
    /// 蓝图子类上的取值作为 CDO 的序列化数据存进包，重编译不会冲掉。
    /// 这条路线**已经跑通**：MusicPlayer 的 <c>VoiceMaps</c> 一直是这么写的。
    /// </summary>
    CdoProperty,

    /// <summary>
    /// **蓝图自建的变量**：必须写 <c>FBPVariableDescription::DefaultValue</c>。
    ///
    /// 只改 CDO 不稳：蓝图下次编译会用声明里的默认值重建生成类，把 CDO 上的改动盖掉。
    /// 走 GALLibrary 的 <c>UGALLBlueprintVariableWriter</c>。
    /// </summary>
    BlueprintDefault,
}

/// <summary>这条请求携带的是哪种载荷。</summary>
internal enum UnrealVariableValueKind
{
    /// <summary>资产软引用数组，用 <see cref="UnrealVariableWriteRequest.AssetObjectPaths"/>。</summary>
    AssetList,

    /// <summary>浮点数组，用 <see cref="UnrealVariableWriteRequest.Numbers"/>。</summary>
    FloatList,
}

/// <summary>
/// 一条变量写入请求。
///
/// 【为什么载荷是扁平的几个字段】
/// 几种载荷本来可以做成子类，但这份计划要**序列化进同步清单、逐条给人核对**，
/// 扁平结构在清单里最好读；分支由 <see cref="Kind"/> 决定，读哪个字段也是一眼的事。
/// </summary>
/// <param name="TargetAssetPath">目标资产的对象路径（<c>/Game/.../Asset.Asset</c>）。</param>
/// <param name="VariableName">变量名 —— **C++/蓝图的真实名字，不是 DisplayName**。</param>
/// <param name="Route">走哪条路写。</param>
/// <param name="Kind">载荷种类。</param>
/// <param name="ReferenceClassName">
/// <see cref="UnrealVariableValueKind.AssetList"/> 拼导出文本时的类名前缀（<c>Texture2D</c> / <c>SoundWave</c>）。
/// 它同时是"这批资产该以什么类型引用"的声明。
/// </param>
/// <param name="AssetObjectPaths">规范对象路径，**顺序即数组顺序**（游戏侧按下标取用）。</param>
/// <param name="Numbers">浮点数组的值。</param>
/// <param name="Description">给人看的说明，进日志和界面。</param>
internal sealed record UnrealVariableWriteRequest(
    string TargetAssetPath,
    string VariableName,
    UnrealVariableWriteRoute Route,
    UnrealVariableValueKind Kind,
    string ReferenceClassName,
    IReadOnlyList<string> AssetObjectPaths,
    IReadOnlyList<float> Numbers,
    string Description)
{
    /// <summary>
    /// 按 <see cref="Route"/> 需要的形态产出载荷文本。
    ///
    /// <see cref="UnrealVariableWriteRoute.BlueprintDefault"/> 要的是导出文本；
    /// <see cref="UnrealVariableWriteRoute.CdoProperty"/> 那条路走 Python、
    /// 由 Python 逐个 <c>load_asset</c>，所以要的是**裸对象路径**列表 —— 这里对它返回空串，
    /// 让调用方明确地去读 <see cref="AssetObjectPaths"/>，而不是误用一段导出文本。
    /// </summary>
    public string BuildPayloadText() => Kind switch
    {
        UnrealVariableValueKind.AssetList when Route == UnrealVariableWriteRoute.BlueprintDefault =>
            UnrealVariableExportText.BuildReferenceArray(
                AssetObjectPaths.Select(path => $"{ReferenceClassName}'{path}'").ToArray()),
        UnrealVariableValueKind.FloatList when Route == UnrealVariableWriteRoute.BlueprintDefault =>
            UnrealVariableExportText.BuildFloatArray(Numbers),
        _ => string.Empty,
    };
}

/// <summary>
/// 一次同步要写的全部变量。这是「写什么」的唯一清单。
/// </summary>
/// <param name="Requests">按写入顺序排好的请求。</param>
/// <param name="Hash">
/// 计划内容的哈希，用来做变更检测 —— 与上一轮一致就可以整段跳过
/// （变量写入里最重的是"编译蓝图"，能不碰就不碰）。
/// </param>
internal sealed record UnrealSyncVariablePlan(
    IReadOnlyList<UnrealVariableWriteRequest> Requests,
    string Hash);

/// <summary>
/// 由同步输入构建变量写入计划。
///
/// 【它替掉了什么】
/// 老做法是把索引/映射表写进 <c>ExcelTexts</c>（BGIndexMap 背景图、BGMap 其实是 BGM、
/// SceneIndexMap 环境音），游戏侧再查表。现在这三样改成**直接写变量列表** ——
/// 所以这里产出的是"变量请求"，不再是"表格"。
///
/// 【为什么这里没有语音】
/// 语音的 <c>VoiceMaps</c> 走的是**另一条已经跑通的路径**（清单里的 <c>VoiceMap</c> 段 +
/// 脚本里那段 CDO 写入），而且那条还额外负责"清理 ExcelTexts 下过期的章节表"。
/// 把它挪进这里只会把两套逻辑搅在一起、还得再实现一遍清理 —— 所以先不动。
/// 将来若要统一，方向是把那边整段搬过来，而不是两边各写一半。
///
/// 【一个刻意的缺席：特殊音效】
/// 它没有对应的变量（口述：「特殊音效和环境音位置一致，但是不需要置入变量」），
/// 所以**这里不会为它生成任何请求**；它仍然靠 <c>ExsIndexMap</c> 表，
/// 那张表属于变更计划（<c>UnrealAssetIndexTableSyncEntry</c>）而不是变量计划。
/// </summary>
internal static class UnrealSyncVariablePlanBuilder
{
    public static UnrealSyncVariablePlan Build(
        IReadOnlyCollection<string> backgroundPaths,
        IReadOnlyCollection<string> musicPaths,
        IReadOnlyCollection<string> ambientPaths)
    {
        ArgumentNullException.ThrowIfNull(backgroundPaths);
        ArgumentNullException.ThrowIfNull(musicPaths);
        ArgumentNullException.ThrowIfNull(ambientPaths);

        var requests = new List<UnrealVariableWriteRequest>
        {
            // 1. 背景图 → 剧情 UI 蓝图的自建变量（蓝图变量，所以走 BlueprintDefault）
            new(
                UnrealSyncPathPolicy.StoryWidgetAsset,
                UnrealSyncPathPolicy.StoryWidgetBackgroundListVariable,
                UnrealVariableWriteRoute.BlueprintDefault,
                UnrealVariableValueKind.AssetList,
                "Texture2D",
                BuildObjectPaths(UnrealSyncPathPolicy.BackgroundFolder, backgroundPaths),
                [],
                "背景图列表（剧情 UI 用它按下标取背景）"),

            // 2. BGM → MusicActor 的 C++ 列表
            new(
                UnrealSyncPathPolicy.MusicPlayerAsset,
                UnrealSyncPathPolicy.MusicPlayerBgmListVariable,
                UnrealVariableWriteRoute.CdoProperty,
                UnrealVariableValueKind.AssetList,
                "SoundWave",
                BuildObjectPaths(UnrealSyncPathPolicy.BgmFolder, musicPaths),
                [],
                "BGM 列表"),

            // 3. BGM 循环起始 → 必须与上一条**等长**，全 0
            new(
                UnrealSyncPathPolicy.MusicPlayerAsset,
                UnrealSyncPathPolicy.MusicPlayerBgmLoopStartVariable,
                UnrealVariableWriteRoute.CdoProperty,
                UnrealVariableValueKind.FloatList,
                string.Empty,
                [],
                Enumerable.Repeat(0f, musicPaths.Count).ToArray(),
                "BGM 循环起始（与 BGM 列表等长，默认全 0，防止索引缺失）"),

            // 4. 环境音 → 同上的列表。
            //    注意写的是 SceneMaps（列表），**不是** SceneIndex ——
            //    后者与 AudioIndex 对称，是运行时游标（MusicPlayerC.cpp 里 BGMaps[AudioIndex] 为证）。
            new(
                UnrealSyncPathPolicy.MusicPlayerAsset,
                UnrealSyncPathPolicy.MusicPlayerSceneListVariable,
                UnrealVariableWriteRoute.CdoProperty,
                UnrealVariableValueKind.AssetList,
                "SoundWave",
                BuildObjectPaths(UnrealSyncPathPolicy.SceneEffectFolder, ambientPaths),
                [],
                "环境音列表"),
        };

        return new UnrealSyncVariablePlan(requests, ComputeHash(requests));
    }

    /// <summary>
    /// 本地文件 → 规范对象路径（<c>/Game/.../名字.名字</c>）。
    ///
    /// 命名规则**复用老服务的实现**（<see cref="UnrealSyncService.BuildAssetObjectPath"/>）而不是
    /// 在这里重写一遍：导入端用的就是那套规则，两边一旦不一致，
    /// 变量里写的路径就会指向不存在的资产，而且是静默的。
    /// 等老服务拆开时这个方法跟着搬过来，届时只留一份。
    /// </summary>
    private static IReadOnlyList<string> BuildObjectPaths(string destinationFolder, IReadOnlyCollection<string> sourcePaths) =>
        sourcePaths.Select(path => UnrealSyncService.BuildAssetObjectPath(destinationFolder, path)).ToArray();

    private static string ComputeHash(IReadOnlyList<UnrealVariableWriteRequest> requests)
    {
        var builder = new StringBuilder("unreal-variable-plan-v2");
        foreach (var request in requests)
        {
            builder.Append('\n')
                .Append(request.TargetAssetPath).Append('|')
                .Append(request.VariableName).Append('|')
                .Append(request.Route).Append('|')
                .Append(request.Kind).Append('|')
                .Append(request.ReferenceClassName).Append('|')
                .Append(string.Join(',', request.AssetObjectPaths)).Append('|')
                .Append(string.Join(',', request.Numbers));
        }

        return FileSystemUtility.ComputeSha256Hex(builder.ToString());
    }
}
