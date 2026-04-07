namespace SyntheticEnterprise.Module.Services;

public sealed class DeterministicValueNormalizer
{
    public string Normalize(string input)
    {
        return input
            .Replace("\\", "/")
            .Replace("\r\n", "\n");
    }
}
