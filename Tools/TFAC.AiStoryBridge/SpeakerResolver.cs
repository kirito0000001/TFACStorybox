namespace Tfac.AiStoryBridge;

internal sealed class SpeakerResolver
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public SpeakerResolver(SpeakerCatalog catalog)
    {
        foreach (var character in catalog.Characters)
        {
            if (!string.IsNullOrWhiteSpace(character.Name) && !string.IsNullOrWhiteSpace(character.Code))
            {
                _values.TryAdd(character.Name.Trim(), character.Code.Trim());
                _values.TryAdd(character.Code.Trim(), character.Code.Trim());
            }
        }
        foreach (var speaker in catalog.CustomSpeakers.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            _values.TryAdd(speaker.Trim(), speaker.Trim());
        }
    }

    public bool TryResolve(string speaker, out string value)
    {
        return _values.TryGetValue(speaker.Trim(), out value!);
    }
}
