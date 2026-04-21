namespace SyntheticEnterprise.Core.Plugins;

using System.Text;
using System.Text.Json;
using SyntheticEnterprise.Contracts.Plugins;

public interface IGenerationPluginPackageScaffolder
{
    GenerationPluginPackageScaffoldResult Scaffold(GenerationPluginPackageScaffoldRequest request);
}

public sealed class GenerationPluginPackageScaffolder : IGenerationPluginPackageScaffolder
{
    public GenerationPluginPackageScaffoldResult Scaffold(GenerationPluginPackageScaffoldRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RootPath))
        {
            throw new ArgumentException("RootPath is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Capability))
        {
            throw new ArgumentException("Capability is required.", nameof(request));
        }

        var rootPath = Path.GetFullPath(request.RootPath);
        if (Directory.Exists(rootPath)
            && Directory.EnumerateFileSystemEntries(rootPath).Any()
            && !request.Force)
        {
            throw new InvalidOperationException($"Plugin package root '{rootPath}' already exists and is not empty. Use Force to overwrite the scaffold.");
        }

        Directory.CreateDirectory(rootPath);

        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? request.Capability
            : request.DisplayName.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description)
            ? $"Adds synthetic {displayName} records to the generated world."
            : request.Description.Trim();
        var fileStem = BuildFileStem(request.Capability);
        var defaultRecordPrefix = BuildRecordPrefix(request.Capability);
        var manifestPath = Path.Combine(rootPath, $"{fileStem}.generator.json");
        var entryPointPath = Path.Combine(rootPath, $"{fileStem}.pack.ps1");
        var readmePath = Path.Combine(rootPath, "README.md");

        WriteAllText(manifestPath, BuildManifestJson(request, displayName, description, fileStem));
        WriteAllText(entryPointPath, BuildEntryPointScript(request.Capability, defaultRecordPrefix));
        WriteAllText(readmePath, BuildReadme(rootPath, request, displayName, fileStem));

        return new GenerationPluginPackageScaffoldResult
        {
            RootPath = rootPath,
            Capability = request.Capability,
            DisplayName = displayName,
            ManifestPath = manifestPath,
            EntryPointPath = entryPointPath,
            ReadmePath = readmePath,
            CreatedPaths = new()
            {
                manifestPath,
                entryPointPath,
                readmePath
            }
        };
    }

    private static void WriteAllText(string path, string content)
    {
        File.WriteAllText(path, content.ReplaceLineEndings(Environment.NewLine), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string BuildManifestJson(
        GenerationPluginPackageScaffoldRequest request,
        string displayName,
        string description,
        string fileStem)
    {
        var manifest = new
        {
            capability = request.Capability,
            displayName,
            description,
            pluginKind = request.PluginKind,
            executionMode = "PowerShellScript",
            entryPoint = $"{fileStem}.pack.ps1",
            parameters = new object[]
            {
                new
                {
                    name = "SampleCount",
                    typeName = "System.Int32",
                    required = false,
                    defaultValue = 6
                },
                new
                {
                    name = "RecordPrefix",
                    typeName = "System.String",
                    required = false,
                    defaultValue = BuildRecordPrefix(request.Capability)
                }
            },
            security = new
            {
                dataOnly = true,
                requestedCapabilities = new[]
                {
                    "GenerateData"
                }
            },
            metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["packId"] = request.Capability,
                ["packPhase"] = request.PackPhase,
                ["category"] = request.Category
            }
        };

        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static string BuildEntryPointScript(string capability, string defaultRecordPrefix)
    {
        return $$"""
$sampleCount = 6
$rawSampleCount = $PluginRequest.PluginSettings['SampleCount']
if ($null -ne $rawSampleCount -and $rawSampleCount -ne '') {
  try {
    $sampleCount = [int]$rawSampleCount
  } catch {
    $sampleCount = 6
  }
}
if ($sampleCount -lt 1) {
  $sampleCount = 1
}

$recordPrefix = $PluginRequest.PluginSettings['RecordPrefix']
if ($null -eq $recordPrefix -or $recordPrefix -eq '') {
  $recordPrefix = '{{defaultRecordPrefix}}'
}

$records = @()
$company = $InputWorld.Companies | Select-Object -First 1
$people = @($InputWorld.People)

if ($null -ne $company) {
  for ($index = 0; $index -lt $sampleCount; $index++) {
    $person = if ($people.Count -gt 0) { $people[$index % $people.Count] } else { $null }
    $recordId = '{0}-{1:000}' -f $recordPrefix, ($index + 1)
    $ownerPersonId = if ($null -ne $person) { $person.Id } else { '' }

    $records += New-PluginRecord -RecordType 'CustomPackRecord' -AssociatedEntityType 'Company' -AssociatedEntityId $company.Id -Properties @{
      RecordId = $recordId
      PackId = '{{capability}}'
      Summary = 'Sample generated record ' + ($index + 1)
      CompanyId = $company.Id
      OwnerPersonId = $ownerPersonId
    }
  }
}

New-PluginResult -Records $records -Warnings @()
""";
    }

    private static string BuildReadme(
        string rootPath,
        GenerationPluginPackageScaffoldRequest request,
        string displayName,
        string fileStem)
    {
        var escapedRoot = rootPath.Replace("'", "''", StringComparison.Ordinal);

        return $$"""
# {{displayName}}

This package was scaffolded with `New-SEGenerationPluginPackage`.

## Files

- `{{fileStem}}.generator.json`
  Manifest, parameters, and pack metadata
- `{{fileStem}}.pack.ps1`
  Constrained PowerShell entry point that emits sample records

## Validate the package

```powershell
Test-SEGenerationPluginPackage -PluginRootPath '{{escapedRoot}}'
```

## Register or install the package

```powershell
Register-SEGenerationPlugin -PluginRootPath '{{escapedRoot}}'
```

```powershell
Install-SEGenerationPluginPackage -PluginRootPath '{{escapedRoot}}'
```

## Enable the pack in a scenario

```json
{
  "name": "Custom Pack Scenario",
  "template": "RegionalManufacturer",
  "packs": {
    "includeBundledPacks": false,
    "packRootPaths": [ "{{rootPath.Replace("\\", "\\\\", StringComparison.Ordinal)}}" ],
    "enabledPacks": [
      {
        "packId": "{{request.Capability}}",
        "settings": {
          "SampleCount": "8"
        }
      }
    ]
  }
}
```

## Next edits

- replace the sample `CustomPackRecord` output with a domain-specific record family
- add plugin-local catalogs through `localDataPaths` if the pack needs curated lookup data
- keep the package data-only; translation into downstream system contracts belongs outside DataGen
""";
    }

    private static string BuildFileStem(string capability)
    {
        var builder = new StringBuilder(capability.Length);
        var previousWasSeparator = false;

        foreach (var character in capability.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
                continue;
            }

            if (!previousWasSeparator)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        var stem = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(stem) ? "generation-pack" : stem;
    }

    private static string BuildRecordPrefix(string capability)
    {
        var segments = capability
            .Split(new[] { '.', '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(segment => new string(segment.Where(char.IsLetterOrDigit).ToArray()))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        if (segments.Length == 0)
        {
            return "PACK";
        }

        if (segments.Length == 1)
        {
            return segments[0].ToUpperInvariant();
        }

        return string.Concat(segments.Select(segment => char.ToUpperInvariant(segment[0])));
    }
}
