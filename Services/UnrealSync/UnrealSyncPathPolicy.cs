using System;
using System.Collections.Generic;
using System.IO;

namespace GalExcleTools.Services;

/// <summary>
/// 虚幻同步**落点的唯一定义处**。要改落点，只改这个文件。
///
/// 【为什么单独抽出来】
/// 老代码把落点拼在 <c>UnrealSyncService</c> 的各处（<c>$"{context.TargetAssetRoot}/BackGround"</c>
/// 这种），而新工程的落点**跨了 /Game 下三个顶层目录**（AssetMaterial / BaseC / UIWidget），
/// 不再是"一个根加几个子目录"。继续用 TargetAssetRoot 拼会越拼越歪。
///
/// 【新旧落点对照】2026-10-12 按口述 + 目标工程磁盘实况核定：
///
/// 数据       老落点                                        新落点
/// 背景图     {根}/BackGround                               /Game/AssetMaterial/ImageS/Background_Image/PlotUse
/// BGM        {根}/BGM                                      /Game/AssetMaterial/Sound/BGM/PlotUse
/// 环境音     {根}/Scene_Effect                             /Game/AssetMaterial/Sound/Scene_Effect
/// 特殊音效   同环境音                                        同环境音（两者共用目录）
/// 语音       {根}/Voice/{章节}                              /Game/AssetMaterial/Sound/Voice/{章节}
/// 立绘       {根}/Lustration/{码}/{层}                      /Game/AssetMaterial/Lustration_Image/{码}/{层}
/// 小预览     {根}/Lustration/{码}/Log_Preview               /Game/AssetMaterial/LustrationPreview_Image
/// 剧情表     {根}/ExcelTexts/{分类}                         保持（ExcelTexts 下）
///
/// 另外：老代码硬性要求"目标内容夹末级必须叫 Narrative"，新工程**没有** Narrative 目录，
/// 所以那条校验连同 TargetContentFolderPath 一起退休 —— Content 根从 .uproject 推导
/// （见 <see cref="ResolveContentFolderPath"/>）。
/// </summary>
internal static class UnrealSyncPathPolicy
{
    // ────────────────────────────────────────────────────────────────
    //  /Game 下的落点
    // ────────────────────────────────────────────────────────────────

    public const string GameRoot = "/Game";

    /// <summary>素材总根。目标工程实况：Content/AssetMaterial/。</summary>
    public const string AssetMaterialRoot = GameRoot + "/AssetMaterial";

    public const string BackgroundFolder = AssetMaterialRoot + "/ImageS/Background_Image/PlotUse";

    public const string BgmFolder = AssetMaterialRoot + "/Sound/BGM/PlotUse";

    /// <summary>环境音与特殊音效**共用**这个目录（口述明确：「特殊音效和环境音位置一致」）。</summary>
    public const string SceneEffectFolder = AssetMaterialRoot + "/Sound/Scene_Effect";

    public const string VoiceFolder = AssetMaterialRoot + "/Sound/Voice";

    /// <summary>立绘图层根，下面按角色码再按图层分目录。</summary>
    public const string LustrationRoot = AssetMaterialRoot + "/Lustration_Image";

    /// <summary>
    /// 小预览落点。
    ///
    /// ⚠️ **待确认**：目标工程里 <c>Content/AssetMaterial/LustrationPreview_Image</c>
    /// 目前是**空的、没有角色子目录**，而口述只给了这一层路径、没给 {角色} 段，
    /// 所以这里按"平铺"处理。<see cref="GetPortraitPreviewFolder"/> 是唯一的改点，
    /// 若实际上要按角色分目录，只改那一个方法。
    /// </summary>
    public const string LustrationPreviewRoot = AssetMaterialRoot + "/LustrationPreview_Image";

    /// <summary>剧情表 / 索引表所在地（保持老位置，只是根换了）。</summary>
    public const string ExcelTextsRoot = AssetMaterialRoot + "/ExcelTexts";

    // ────────────────────────────────────────────────────────────────
    //  要写变量的目标资产（ObjectPath 形态）
    // ────────────────────────────────────────────────────────────────

    /// <summary>剧情 UI 蓝图 —— 背景图列表写它的 BGMaps。WidgetBlueprint，变量是蓝图自建的。</summary>
    public const string StoryWidgetAsset = GameRoot + "/UIWidget/PlotUIs/UI_Story.UI_Story";

    /// <summary>MusicActor 蓝图 —— BGM / 环境音 / 语音的列表都写它。</summary>
    /// <remarks>父类是 GALLibrary 插件的 AMusicPlayerC，所以那几个变量是 C++ UPROPERTY。</remarks>
    public const string MusicPlayerAsset = GameRoot + "/BaseC/EXActor/MusicPlayer.MusicPlayer";

    /// <summary>立绘信息数据资产（立绘与小预览共用）。纯蓝图数据资产，父类 PrimaryDataAsset。</summary>
    public const string LustrationInfoAsset = LustrationRoot + "/DA_LustrationInfor.DA_LustrationInfor";

    /// <summary>
    /// 小预览的数据资产。
    ///
    /// ⚠️ **待确认**：这个资产在 WHK 与 CrossingVoid **两边都不存在**（和那 4 张索引表一样，
    /// 是丢工程时一起没的），而 <c>FLustrationStruct</c> 里也没有预览字段
    /// （只有 Name / Color / Cloth / Face / Adorn / Vfx）。
    /// 所以小预览到底写哪儿还没定：补一个 DA_Portraits，还是并进 DA_LustrationInfor 的某个字段。
    /// 这里先按老结构保留同一个名字、只把路径挪到新布局下；**开启小预览前需要你确认**。
    /// </summary>
    public const string PortraitsInfoAsset = LustrationRoot + "/DA_Portraits.DA_Portraits";

    /// <summary>剧情选项分支表。</summary>
    public const string StoryBranchTableAsset = AssetMaterialRoot + "/AllRoad.AllRoad";

    /// <summary>
    /// 特殊音效索引表 —— 特殊音效**没有目标变量**，所以只能靠这张表。
    /// </summary>
    /// <remarks>
    /// 行结构体 <c>/Script/GALLibrary.WaveTable</c>（C++ 的 FWaveTable）在两个工程里都遗失过，
    /// 2026-10-12 已在 GALLibrary 的 GALLRealize.h 里补回。值列名就是结构体里那个字段名。
    /// </remarks>
    public const string EffectIndexTableAsset = ExcelTextsRoot + "/ExsIndexMap.ExsIndexMap";

    public const string EffectIndexTableRowStruct = "/Script/GALLibrary.WaveTable";

    public const string EffectIndexTableValueColumn = "Wave";

    // ────────────────────────────────────────────────────────────────
    //  变量名（C++ UPROPERTY 的**名字**，不是 DisplayName）
    // ────────────────────────────────────────────────────────────────

    public const string MusicPlayerBgmListVariable = "BGMaps";

    public const string MusicPlayerBgmLoopStartVariable = "BGMapsLoopStart";

    public const string MusicPlayerSceneListVariable = "SceneMaps";

    public const string MusicPlayerVoiceMapVariable = "VoiceMaps";

    public const string StoryWidgetBackgroundListVariable = "BGMaps";

    // 刻意**不**写 AudioIndex / SceneIndex：
    // MusicPlayerC.cpp:52 `BGMaps[AudioIndex].LoadSynchronous()` 证明那两个是
    // **运行时游标**（当前在放第几首），不是给人填的素材数据。工具箱只写列表。

    // ────────────────────────────────────────────────────────────────
    //  立绘图层目录名
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 这个图层是否走立绘同步链。
    ///
    /// <see cref="CharacterLayerKind.Vfx"/> 不在链上：本地素材库有它的目录（磁盘实证立绘/{码}/VFX），
    /// 但同步只搬 Cloth / Face / Adorn 三层，立绘信息表的列也就这三层。
    /// 遍历 <see cref="CharacterLayerKind"/> 的调用方**先过这里**，别直接调
    /// <see cref="GetCharacterLayerFolderName"/> —— 那个对 Vfx 是抛异常的。
    /// </summary>
    public static bool IsSyncableCharacterLayer(CharacterLayerKind layerKind) =>
        layerKind is CharacterLayerKind.Cloth or CharacterLayerKind.Face or CharacterLayerKind.Adorn;

    /// <summary>
    /// 立绘图层在 **Unreal 目标**下的目录名。
    ///
    /// ⚠️ **刻意和本地素材库不一样**，别"顺手统一"：
    ///
    ///   图层   本地素材库（磁盘实证）   Unreal 目标（磁盘实证）
    ///   服装   DN_Cloth（单数）         DN_Cloths（复数）
    ///   表情   FC_Face                 FC_Face
    ///   装饰   AD_Adorn                AD_Adorn
    ///
    /// 本地那套在 <c>CharacterLayerAssetService.GetFolderName</c>（它只服务本地布局与界面显示），
    /// 目标那套老代码硬编码在 `UnrealSyncService.cs:1546`（服装那支）与 `:1861`。
    /// 两处现在都收口到本方法。
    /// </summary>
    public static string GetCharacterLayerFolderName(CharacterLayerKind layerKind) => layerKind switch
    {
        CharacterLayerKind.Cloth => "DN_Cloths",
        CharacterLayerKind.Face => "FC_Face",
        CharacterLayerKind.Adorn => "AD_Adorn",
        _ => throw new ArgumentOutOfRangeException(
            nameof(layerKind),
            layerKind,
            $"图层 {layerKind} 没有 Unreal 目标目录。遍历枚举请先用 {nameof(IsSyncableCharacterLayer)} 过滤。"),
    };

    /// <summary>某个角色某一层的落点，例如 /Game/AssetMaterial/Lustration_Image/Qi/DN_Cloths。</summary>
    public static string GetCharacterLayerFolder(string characterCode, CharacterLayerKind layerKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterCode);
        return $"{LustrationRoot}/{characterCode}/{GetCharacterLayerFolderName(layerKind)}";
    }

    /// <summary>
    /// 小预览的落点。见 <see cref="LustrationPreviewRoot"/> 上的待确认说明 ——
    /// 要改成按角色分目录，**只改这里**。
    /// </summary>
    public static string GetPortraitPreviewFolder(string characterCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterCode);
        return LustrationPreviewRoot;
    }

    // ────────────────────────────────────────────────────────────────
    //  Content 根：从 .uproject 推导（取代"手选目标内容夹"）
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 由 <c>xxx.uproject</c> 的路径推出 <c>Content</c> 目录。
    ///
    /// 取代老代码那个"让用户手选目标内容夹"的输入框。老方案配了一条硬校验
    /// 「目标同步文件夹必须命名为 Narrative，避免素材同步到错误目录」——
    /// 新工程没有 Narrative、落点又跨了三个顶层目录，选一个"目标夹"已经没有意义，
    /// 所以连同那条校验一起退休。
    /// </summary>
    public static string ResolveContentFolderPath(string unrealProjectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unrealProjectPath);
        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(unrealProjectPath));
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            throw new ArgumentException("无法从 .uproject 路径推出所在目录。", nameof(unrealProjectPath));
        }

        return Path.Combine(projectDirectory, "Content");
    }

    /// <summary>
    /// 把 <c>/Game/...</c> 的对象路径换成磁盘路径。
    /// 不是 /Game 开头的一律拒绝，免得把引擎内容当成工程内容去写。
    /// </summary>
    public static string ResolveDiskPath(string contentFolderPath, string gameObjectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentFolderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameObjectPath);

        if (!gameObjectPath.StartsWith(GameRoot + "/", StringComparison.Ordinal))
        {
            throw new ArgumentException($"只接受 {GameRoot}/ 开头的工程内路径，收到：{gameObjectPath}", nameof(gameObjectPath));
        }

        var relative = gameObjectPath[(GameRoot.Length + 1)..].Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(contentFolderPath, relative);
    }

    // ────────────────────────────────────────────────────────────────
    //  同步前要求已存在的目录
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 同步前要求目标工程里**已经存在**的目录，相对 <c>Content</c>。
    ///
    /// 这条是老 <c>UnrealSyncService.ExpectedNarrativeFolders</c> 的替代品，两处不同：
    /// ① 整组换成新落点；② 改成相对 Content 的路径（Content 从 .uproject 推导）。
    ///
    /// 口径照零境第一步：**只读、只报告，绝不自动创建** ——
    /// 目录该由人在 Unreal 内容浏览器里整理，工具不该替他决定往哪塞。
    /// </summary>
    public static IReadOnlyList<string> ExpectedContentFolders { get; } =
    [
        "AssetMaterial",
        "AssetMaterial/ImageS/Background_Image/PlotUse",
        "AssetMaterial/Sound/BGM/PlotUse",
        "AssetMaterial/Sound/Scene_Effect",
        "AssetMaterial/Sound/Voice",
        "AssetMaterial/Lustration_Image",
        "AssetMaterial/LustrationPreview_Image",
        "AssetMaterial/ExcelTexts",
        "BaseC/EXActor",
        "UIWidget/PlotUIs",
    ];

    /// <summary>把 <see cref="ExpectedContentFolders"/> 展开成磁盘绝对路径。</summary>
    public static IReadOnlyList<string> ResolveExpectedContentFolderPaths(string contentFolderPath)
    {
        var result = new List<string>(ExpectedContentFolders.Count);
        foreach (var relative in ExpectedContentFolders)
        {
            result.Add(Path.Combine(contentFolderPath, relative.Replace('/', Path.DirectorySeparatorChar)));
        }

        return result;
    }
}
