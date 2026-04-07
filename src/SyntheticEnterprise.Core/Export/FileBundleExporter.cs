namespace SyntheticEnterprise.Core.Export;

using System.Reflection;
using System.Text.Json;
using SyntheticEnterprise.Contracts.Abstractions;
using SyntheticEnterprise.Contracts.Configuration;
using SyntheticEnterprise.Contracts.Models;
using SyntheticEnterprise.Core.Abstractions;

public sealed class FileBundleExporter : IExporter
{
    public ExportResult Export(GenerationResult result, ExportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            throw new ArgumentException("OutputPath is required for file export.", nameof(options));
        }

        Directory.CreateDirectory(options.OutputPath);
        var artifacts = new List<ExportArtifact>();
        var format = (options.Format ?? "Csv").Trim();

        if (string.Equals(format, "Json", StringComparison.OrdinalIgnoreCase))
        {
            WriteJsonArtifacts(result, options, artifacts);
        }
        else
        {
            WriteCsvArtifacts(result, options, artifacts);
        }

        var manifest = new ExportManifest
        {
            ExportedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            Format = format,
            OutputPath = options.OutputPath!,
            Artifacts = artifacts
        };

        if (options.EmitManifest)
        {
            var manifestPath = Path.Combine(options.OutputPath!, "manifest.json");
            var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(manifestPath, json);
        }

        return new ExportResult
        {
            OutputPath = options.OutputPath!,
            Manifest = manifest
        };
    }

    private static void WriteJsonArtifacts(GenerationResult result, ExportOptions options, List<ExportArtifact> artifacts)
    {
        WriteJson("companies.json", result.World.Companies, options, artifacts);
        WriteJson("business_units.json", result.World.BusinessUnits, options, artifacts);
        WriteJson("departments.json", result.World.Departments, options, artifacts);
        WriteJson("teams.json", result.World.Teams, options, artifacts);
        WriteJson("people.json", result.World.People, options, artifacts);
        WriteJson("offices.json", result.World.Offices, options, artifacts);
        WriteJson("organizational_units.json", result.World.OrganizationalUnits, options, artifacts);
        WriteJson("accounts.json", result.World.Accounts, options, artifacts);
        WriteJson("groups.json", result.World.Groups, options, artifacts);
        WriteJson("group_memberships.json", result.World.GroupMemberships, options, artifacts);
        WriteJson("identity_anomalies.json", result.World.IdentityAnomalies, options, artifacts);
        WriteJson("devices.json", result.World.Devices, options, artifacts);
        WriteJson("servers.json", result.World.Servers, options, artifacts);
        WriteJson("network_assets.json", result.World.NetworkAssets, options, artifacts);
        WriteJson("telephony_assets.json", result.World.TelephonyAssets, options, artifacts);
        WriteJson("software_packages.json", result.World.SoftwarePackages, options, artifacts);
        WriteJson("device_software_installations.json", result.World.DeviceSoftwareInstallations, options, artifacts);
        WriteJson("server_software_installations.json", result.World.ServerSoftwareInstallations, options, artifacts);
        WriteJson("infrastructure_anomalies.json", result.World.InfrastructureAnomalies, options, artifacts);
        WriteJson("databases.json", result.World.Databases, options, artifacts);
        WriteJson("file_shares.json", result.World.FileShares, options, artifacts);
        WriteJson("collaboration_sites.json", result.World.CollaborationSites, options, artifacts);
        WriteJson("repository_access_grants.json", result.World.RepositoryAccessGrants, options, artifacts);
        WriteJson("repository_anomalies.json", result.World.RepositoryAnomalies, options, artifacts);
        WriteJson("world_summary.json", result.Statistics, options, artifacts);
    }

    private static void WriteCsvArtifacts(GenerationResult result, ExportOptions options, List<ExportArtifact> artifacts)
    {
        WriteCsv("companies.csv", result.World.Companies, options, artifacts);
        WriteCsv("business_units.csv", result.World.BusinessUnits, options, artifacts);
        WriteCsv("departments.csv", result.World.Departments, options, artifacts);
        WriteCsv("teams.csv", result.World.Teams, options, artifacts);
        WriteCsv("people.csv", result.World.People, options, artifacts);
        WriteCsv("offices.csv", result.World.Offices, options, artifacts);
        WriteCsv("organizational_units.csv", result.World.OrganizationalUnits, options, artifacts);
        WriteCsv("accounts.csv", result.World.Accounts, options, artifacts);
        WriteCsv("groups.csv", result.World.Groups, options, artifacts);
        WriteCsv("group_memberships.csv", result.World.GroupMemberships, options, artifacts);
        WriteCsv("identity_anomalies.csv", result.World.IdentityAnomalies, options, artifacts);
        WriteCsv("devices.csv", result.World.Devices, options, artifacts);
        WriteCsv("servers.csv", result.World.Servers, options, artifacts);
        WriteCsv("network_assets.csv", result.World.NetworkAssets, options, artifacts);
        WriteCsv("telephony_assets.csv", result.World.TelephonyAssets, options, artifacts);
        WriteCsv("software_packages.csv", result.World.SoftwarePackages, options, artifacts);
        WriteCsv("device_software_installations.csv", result.World.DeviceSoftwareInstallations, options, artifacts);
        WriteCsv("server_software_installations.csv", result.World.ServerSoftwareInstallations, options, artifacts);
        WriteCsv("infrastructure_anomalies.csv", result.World.InfrastructureAnomalies, options, artifacts);
        WriteCsv("databases.csv", result.World.Databases, options, artifacts);
        WriteCsv("file_shares.csv", result.World.FileShares, options, artifacts);
        WriteCsv("collaboration_sites.csv", result.World.CollaborationSites, options, artifacts);
        WriteCsv("repository_access_grants.csv", result.World.RepositoryAccessGrants, options, artifacts);
        WriteCsv("repository_anomalies.csv", result.World.RepositoryAnomalies, options, artifacts);
        WriteCsv("world_summary.csv", new[] { result.Statistics }, options, artifacts);
    }

    private static void WriteJson<T>(string fileName, IReadOnlyList<T> rows, ExportOptions options, List<ExportArtifact> artifacts)
    {
        var path = Path.Combine(options.OutputPath!, fileName);
        var json = JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        artifacts.Add(new ExportArtifact
        {
            LogicalName = Path.GetFileNameWithoutExtension(fileName),
            RelativePath = fileName,
            Format = "Json",
            RecordCount = rows.Count
        });
    }

    private static void WriteJson<T>(string fileName, T obj, ExportOptions options, List<ExportArtifact> artifacts)
    {
        var path = Path.Combine(options.OutputPath!, fileName);
        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        artifacts.Add(new ExportArtifact
        {
            LogicalName = Path.GetFileNameWithoutExtension(fileName),
            RelativePath = fileName,
            Format = "Json",
            RecordCount = 1
        });
    }

    private static void WriteCsv<T>(string fileName, IReadOnlyList<T> rows, ExportOptions options, List<ExportArtifact> artifacts)
    {
        var path = Path.Combine(options.OutputPath!, fileName);
        using var writer = new StreamWriter(path);
        var props = GetReadableProperties<T>();

        writer.WriteLine(string.Join(",", props.Select(p => EscapeCsv(p.Name))));
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(",", props.Select(p => EscapeCsv(ConvertToCsvValue(p.GetValue(row))))));
        }

        artifacts.Add(new ExportArtifact
        {
            LogicalName = Path.GetFileNameWithoutExtension(fileName),
            RelativePath = fileName,
            Format = "Csv",
            RecordCount = rows.Count
        });
    }

    private static PropertyInfo[] GetReadableProperties<T>() =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead)
            .ToArray();

    private static string ConvertToCsvValue(object? value)
    {
        if (value is null) return "";
        if (value is DateTimeOffset dto) return dto.ToString("O");
        return value.ToString() ?? "";
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
