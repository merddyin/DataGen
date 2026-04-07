namespace SyntheticEnterprise.Core.Abstractions;

using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;

public interface IExporter
{
    ExportResult Export(GenerationResult result, ExportOptions options);
}
