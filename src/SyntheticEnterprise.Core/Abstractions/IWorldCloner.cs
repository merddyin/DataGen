namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Abstractions;

public interface IWorldCloner
{
    GenerationResult Clone(GenerationResult input);
}
