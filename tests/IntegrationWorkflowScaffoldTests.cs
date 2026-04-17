using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Core.Abstractions;
using SyntheticEnterprise.Core.DependencyInjection;
using SyntheticEnterprise.Core.Serialization;
using SyntheticEnterprise.Core.Services;

namespace SyntheticEnterprise.Tests;

public sealed class IntegrationWorkflowScaffoldTests
{
    [Fact]
    public void End_To_End_Workflow_Generates_Snapshot_And_Exports()
    {
        var repoRoot = ResolveRepoRoot();
        var scenarioPath = Path.Combine(repoRoot, "examples", "regional-manufacturer.json");
        var catalogPath = Path.Combine(repoRoot, "catalogs");
        var exportPath = Path.Combine(Path.GetTempPath(), $"datagen-export-{Guid.NewGuid():N}");
        var snapshotPath = Path.Combine(Path.GetTempPath(), $"datagen-snapshot-{Guid.NewGuid():N}.json");

        var services = new ServiceCollection()
            .AddSyntheticEnterpriseCore()
            .BuildServiceProvider();

        try
        {
            var scenarioLoader = services.GetRequiredService<IScenarioLoader>();
            var catalogLoader = services.GetRequiredService<ICatalogLoader>();
            var worldGenerator = services.GetRequiredService<IWorldGenerator>();
            var exporter = services.GetRequiredService<IExporter>();

            var scenario = scenarioLoader.LoadFromPath(scenarioPath);
            var catalogs = catalogLoader.LoadFromPath(catalogPath);
            var result = worldGenerator.Generate(new GenerationContext
            {
                Scenario = scenario,
                Seed = 424242,
                Metadata = new Dictionary<string, string?>
                {
                    ["CatalogRootPath"] = catalogPath
                }
            }, catalogs);

            Assert.True(result.Statistics.CompanyCount > 0);
            Assert.True(result.Statistics.PersonCount > 0);
            Assert.Contains("Organization", result.WorldMetadata!.AppliedLayers);
            Assert.Contains("Repository", result.WorldMetadata!.AppliedLayers);

            var export = exporter.Export(result, new ExportOptions
            {
                OutputPath = exportPath,
                Format = "Json",
                EmitManifest = true
            });

            Assert.True(Directory.Exists(export.OutputPath));
            Assert.Contains(export.Manifest.Artifacts, artifact => artifact.LogicalName == "people");

            var persistence = new SnapshotPersistenceService(new SnapshotSerializer(), new SchemaCompatibilityService());
            var envelope = persistence.CreateEnvelope(
                payload: result,
                sourceScenarioPath: scenarioPath,
                sourceScenarioName: scenario.Name);

            persistence.SaveSnapshot(envelope, snapshotPath, compress: false);
            var imported = persistence.ImportSnapshot<GenerationResult>(snapshotPath);

            Assert.Equal(result.Statistics.PersonCount, imported.Payload.Statistics.PersonCount);
            Assert.Equal(scenario.Name, imported.Envelope.Metadata.SourceScenarioName);
        }
        finally
        {
            if (Directory.Exists(exportPath))
            {
                Directory.Delete(exportPath, recursive: true);
            }

            if (File.Exists(snapshotPath))
            {
                File.Delete(snapshotPath);
            }
        }
    }

    private static string ResolveRepoRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
