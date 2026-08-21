using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GalExcleTools;

internal static class StorySchema
{
    public static readonly string[] Columns =
    [
        "Name",
        "Tesxt",
        "Custom",
        "BGindex",
        "BGM",
        "Scene",
        "TalkChar",
        "TalkBody",
        "TalkFace",
        "TalkAdorn",
        "TalkVfx",
        "Chara1",
        "Body1",
        "Face1",
        "Adorn1",
        "Vfx1",
        "Chara2",
        "Body2",
        "Face2",
        "Adorn2",
        "Vfx2",
        "Chara3",
        "Body3",
        "Face3",
        "Adorn3",
        "Vfx3",
        "Chara4",
        "Body4",
        "Face4",
        "Adorn4",
        "Vfx4",
        "Chara5",
        "Body5",
        "Face5",
        "Adorn5",
        "Vfx5"
    ];

    public static readonly HashSet<string> NumericColumns =
        new(Columns.Where(column =>
            column is "BGindex" or "BGM" or "Scene" or
                "TalkBody" or "TalkFace" or "TalkAdorn" or "TalkVfx" ||
            Regex.IsMatch(column, "^(Body|Face|Adorn|Vfx)\\d+$")), StringComparer.Ordinal);
}
