namespace SyntheticEnterprise.Core.Generation;

using System.Text.Json;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Core.Abstractions;

public sealed class WorldCloner : IWorldCloner
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public GenerationResult Clone(GenerationResult input)
    {
        var json = JsonSerializer.Serialize(input, Options);
        return JsonSerializer.Deserialize<GenerationResult>(json, Options)
            ?? throw new InvalidOperationException("Failed to clone GenerationResult.");
    }
}
