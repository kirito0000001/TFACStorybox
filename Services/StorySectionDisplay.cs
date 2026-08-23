using System;

namespace GalExcleTools.Services;

internal static class StorySectionDisplay
{
    public static int ToDisplayIndex(int internalSection)
    {
        return Math.Max(0, internalSection - 1);
    }

    public static string FormatName(int internalSection)
    {
        return $"第 {ToDisplayIndex(internalSection)} 小节";
    }
}
