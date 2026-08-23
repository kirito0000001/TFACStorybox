using System.Collections.Generic;

namespace GalExcleTools;

internal enum StoryBranchSectionKind
{
    Sequential,
    Choice,
    Branch,
    Common,
    Custom,
    Terminal
}

internal sealed record StoryBranchFlowOption(
    string ChoiceFunction,
    int OptionIndex,
    string OptionKey,
    string Text,
    int? ExplicitTargetSection,
    int? EffectiveTargetSection,
    bool UsesInferredCommon);

internal sealed record StoryBranchSectionFlow(
    int Section,
    string FirstRowId,
    string FirstSentencePreview,
    StoryBranchSectionKind Kind,
    int? DefaultTargetSection,
    List<StoryBranchFlowOption> Options);

internal sealed record StoryChapterBranchFlow(
    List<StoryBranchSectionFlow> Sections,
    List<string> Issues);
