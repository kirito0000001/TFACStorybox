using System.Collections.Generic;

namespace GalExcleTools.Models;

/// <summary>
/// 一个「图集分类」的同步输入：本地素材库文件夹里的图片 → 虚幻里的落点。
///
/// 【为什么把背景图和道具图片收成一条数据，而不是两个并列参数】
/// 它们在同步这条链上是**同一种东西**：一个文件夹一堆 png、序号即排序、备注写在文件名里。
/// 并列参数的话，加第三个分类要再穿一遍 6 个签名，每穿一次都得重新核对"有没有漏一处"；
/// 收成列表之后，加分类 = 在 <c>Runner</c> 里多造一个 input，**签名和循环都不动**。
///
/// 2026-10-14 定的。用户的原话：「以后肯定还会有其他图的分类」——
/// 所以这里按"会有第 N 个"来设计，不按"就这两个"来凑。
/// </summary>
/// <param name="DestinationFolder">虚幻落点，例如 <c>/Game/AssetMaterial/ImageS/PlotUse/Background</c>。</param>
/// <param name="Prefix">
/// 这个分类的文件名前缀（<see cref="ImageSetPrefix"/>）。同步链上不直接用它做什么，
/// 但有两处"只有背景图才需要"的老逻辑（素材索引表、写蓝图变量）要靠它把背景图挑出来 ——
/// 有前缀就不用写"第一个就是背景图"这种不敢改的假设。
/// </param>
/// <param name="FilePaths">本地素材库里这个分类的全部图片（已排序）。</param>
internal sealed record ImageSetSyncInput(
    string DestinationFolder,
    string Prefix,
    IReadOnlyList<string> FilePaths);
