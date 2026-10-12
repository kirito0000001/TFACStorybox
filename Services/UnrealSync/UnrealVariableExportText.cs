using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GalExcleTools.Services;

/// <summary>
/// 把变量的值拼成 **UE 的导出文本**（`FBPVariableDescription::DefaultValue` 那种形态）。
///
/// 【为什么单独抽出来】
/// 这是整条变量写入链里最容易写错、又最难在出错时看出来的地方 ——
/// 拼错了不会报错，只会静默地写进去一个 Unreal 解析不出东西的字符串。
/// 所以格式集中在这里，一处改、一处看。
///
/// 【格式依据】
/// 软引用数组用 `ClassName'Path'` 这种写法不是猜的：现有工具箱的
/// <c>UnrealSyncService.BuildTextureReference</c> / <c>BuildSoundWaveReference</c>
/// 就是这么拼的（`Texture2D'{目录}/{资产名}.{资产名}'`），而那条链路是跑通过的。
/// </summary>
internal static class UnrealVariableExportText
{
    /// <summary>软引用数组：`(Texture2D'/Game/.../A.A',Texture2D'/Game/.../B.B')`。</summary>
    /// <remarks>
    /// 空数组拼成 `()` 而不是空串 —— `FArrayProperty` 自己导出的空数组就是 `()`，
    /// 空串会让导入方以为"这个字段没给"，从而保留旧值。
    /// </remarks>
    public static string BuildReferenceArray(IReadOnlyList<string> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        return references.Count == 0 ? "()" : "(" + string.Join(",", references) + ")";
    }

    /// <summary>
    /// 浮点数组：`(0.0,0.0,0.0)`。
    /// </summary>
    /// <remarks>
    /// 每个元素写成 `0.0` 而不是 `0`，是为了跟 `FFloatProperty` 自己的导出形态一致
    /// （它走 <c>FString::SanitizeFloat</c>，整数也带小数点）。
    /// 两种写法导入端都认（`FCString::Atof`），但保持同形少一类"为什么这里不一样"的疑问。
    /// </remarks>
    public static string BuildFloatArray(IReadOnlyList<float> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return "()";
        }

        var builder = new StringBuilder(values.Count * 4 + 2);
        builder.Append('(');
        for (var index = 0; index < values.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(values[index].ToString("0.0###", CultureInfo.InvariantCulture));
        }

        builder.Append(')');
        return builder.ToString();
    }

    /// <summary>
    /// 全 0 的浮点数组，长度与给定数量一致。
    ///
    /// 用途是 `BGMapsLoopStart`：它必须与 `BGMaps` **等长**，
    /// 否则游戏侧按索引取循环起点会越界（口述原话：「为了防止索引缺失」）。
    /// 素材列表长度一变，这个数组就要跟着补 0 或截断，不能留旧长度。
    /// </summary>
    public static string BuildZeroFloatArray(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return count == 0 ? "()" : "(" + string.Join(",", System.Linq.Enumerable.Repeat("0.0", count)) + ")";
    }
}
