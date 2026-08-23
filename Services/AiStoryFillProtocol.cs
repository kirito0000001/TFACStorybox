using System;

namespace GalExcleTools.Services;

internal static class AiStoryFillProtocol
{
    public static AiStoryFillProtocolDefinition GetDefinition(int version)
    {
        return version switch
        {
            1 => new AiStoryFillProtocolDefinition(
                1,
                "V1",
                IsImplemented: true,
                ["story.text", "story.speaker"],
                ["TalkChar", "Tesxt"]),
            2 => new AiStoryFillProtocolDefinition(
                2,
                "V2",
                IsImplemented: false,
                ["story.text", "story.speaker", "story.characters", "story.portrait-layers"],
                [
                    "TalkChar", "Tesxt", "TalkBody", "TalkFace", "TalkAdorn", "TalkVfx",
                    "Chara1", "Body1", "Face1", "Adorn1", "Vfx1",
                    "Chara2", "Body2", "Face2", "Adorn2", "Vfx2",
                    "Chara3", "Body3", "Face3", "Adorn3", "Vfx3",
                    "Chara4", "Body4", "Face4", "Adorn4", "Vfx4",
                    "Chara5", "Body5", "Face5", "Adorn5", "Vfx5"
                ]),
            3 => new AiStoryFillProtocolDefinition(
                3,
                "V3",
                IsImplemented: false,
                ["story.text", "story.speaker", "story.characters", "story.portrait-layers", "story.functions"],
                [
                    "TalkChar", "Tesxt", "TalkBody", "TalkFace", "TalkAdorn", "TalkVfx",
                    "Chara1", "Body1", "Face1", "Adorn1", "Vfx1",
                    "Chara2", "Body2", "Face2", "Adorn2", "Vfx2",
                    "Chara3", "Body3", "Face3", "Adorn3", "Vfx3",
                    "Chara4", "Body4", "Face4", "Adorn4", "Vfx4",
                    "Chara5", "Body5", "Face5", "Adorn5", "Vfx5", "Custom"
                ]),
            _ => throw new ArgumentOutOfRangeException(nameof(version), "AI 剧情填表协议版本不存在。")
        };
    }
}
