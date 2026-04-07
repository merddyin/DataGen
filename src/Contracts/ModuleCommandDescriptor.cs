namespace SyntheticEnterprise.Module.Contracts;

public sealed record ModuleCommandDescriptor(
    string Name,
    string Category,
    string[] ParameterSets,
    bool AcceptsPipelineInput,
    bool SupportsPassThru);
