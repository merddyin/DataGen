# Plugin SDK Guide

## Plugin Types

DataGen supports four plugin modes in the manifest model:
- `InProcess`
  - reserved for built-in/core plugins
- `PowerShellScript`
  - community-friendly external plugins executed in a constrained runspace
- `DotNetAssembly`
  - compiled external plugins executed in an isolated host process
- `MetadataOnly`
  - schema or planning packages that are not executable yet

## Runtime Contract

Every external plugin is expected to behave like a data transformer:
- input
  - resolved generation scenario metadata
  - generated world snapshot
  - plugin-local catalogs
  - plugin manifest metadata
- output
  - structured plugin-generated records
  - warnings

Plugins should not write files, change the environment, call the network, or start processes.

## Hard Invariants

Realism settings can introduce omissions, drift, stale data, and other intentionally messy conditions. They cannot violate core platform invariants.

Examples of hard invariants include:
- user principal names must stay unique within the generated environment
- plugin output must not introduce malformed references or structurally invalid records
- realism overlays must not corrupt the generated world into something a real platform could never represent

If a plugin introduces invariant-breaking data, generation is expected to fail rather than emit a "mostly valid" world.

## Architectural Constraint

DataGen exists to procedurally generate synthetic enterprise data. External plugins are part of that generation surface, which means they must stay on the data-generation side of the boundary.

External plugins are allowed to:
- add new synthetic records or metadata to the generated world
- enrich existing entities with additional synthetic detail
- introduce realism or deviation overlays that are still expressed as generated data
- generate synthetic operational history so long as it is modeled as data tied to the generated world, not as control flow into another product

External plugins are not allowed to:
- translate DataGen output into the import contract, API contract, cmdlet shape, or storage schema of a specific downstream system
- act as a system-specific bridge, exporter, or ingestion adapter
- invoke downstream product workflows as part of plugin execution
- couple the plugin's primary output to one consumer platform's internal object model

The simplest rule is:
- if the plugin makes the synthetic dataset richer, it is probably a good fit
- if the plugin makes the dataset easier to ingest into one particular system, it belongs outside the DataGen plugin ecosystem

## Good Fit Examples

- add synthetic employee badge identifiers, contractor registration numbers, or region-specific business identifiers
- generate synthetic audit events, operator activity records, or graph-change history that references real generated users, devices, applications, and repositories
- add richer realism deviations such as incomplete ownership metadata, stale lifecycle states, or neutral CMDB-like drift records
- emit synthetic manufacturing, healthcare, or finance-specific records that broaden the generated world without assuming one downstream consumer
- enrich generated applications and services with additional synthetic catalog, compliance, or lifecycle metadata

## Bad Fit Examples

- convert generated records into another product's node, edge, table, or import-envelope format
- invoke another tool's ingestion cmdlets or remote APIs from the plugin
- generate records whose only meaningful purpose is to satisfy a specific vendor's required ingestion schema
- reshape DataGen entities to match a downstream platform's proprietary object taxonomy instead of extending the synthetic world
- perform consumer-specific export, synchronization, reconciliation, or import automation

## Manifest Shape

Minimal script manifest:

```json
{
  "capability": "CountryTaxIds",
  "displayName": "Country Tax IDs",
  "description": "Adds country-aware synthetic identifiers for people.",
  "pluginKind": "SdkExample",
  "executionMode": "PowerShellScript",
  "entryPoint": "country-tax-ids.plugin.ps1",
  "localDataPaths": [ "data/tax-id-rules.csv" ],
  "security": {
    "dataOnly": true,
    "requestedCapabilities": [ "GenerateData", "ReadPluginData" ]
  }
}
```

Minimal binary manifest:

```json
{
  "capability": "CompanyRegistrationIds",
  "displayName": "Company Registration IDs",
  "description": "Adds synthetic registration identifiers to companies.",
  "pluginKind": "SdkExample",
  "executionMode": "DotNetAssembly",
  "entryPoint": "bin/Debug/net8.0/CompanyRegistrationIds.Binary.dll",
  "security": {
    "dataOnly": true,
    "requestedCapabilities": [ "GenerateData" ]
  }
}
```

## PowerShell Script Plugin Contract

The constrained host provides:
- `$InputWorld`
- `$PluginRequest`
- `$PluginCatalogs`
- `$PluginManifest`
- `New-PluginRecord`
- `New-PluginResult`

The recommended pattern is:

```powershell
$records = @()

foreach ($person in $InputWorld.People) {
  $records += New-PluginRecord -RecordType 'Something' -AssociatedEntityType 'Person' -AssociatedEntityId $person.Id -Properties @{
    Example = 'Value'
  }
}

New-PluginResult -Records $records -Warnings @()
```

## Binary Plugin Contract

Implement `IExternalGenerationAssemblyPlugin` from `SyntheticEnterprise.Contracts`.

```csharp
using SyntheticEnterprise.Contracts.Plugins;

public sealed class ExamplePlugin : IExternalGenerationAssemblyPlugin
{
    public string Capability => "ExampleCapability";

    public ExternalPluginExecutionResponse Execute(ExternalPluginExecutionRequest request)
    {
        return new ExternalPluginExecutionResponse
        {
            Executed = true
        };
    }
}
```

The request contains:
- `Manifest`
- `Request`
- `InputWorld`
- `PluginCatalogs`

## Inspection and Validation

Scaffold a starter package:

```powershell
New-SEGenerationPluginPackage `
  -Path 'E:\work\plugins\Contoso.RiskOps' `
  -Capability 'Contoso.RiskOps' `
  -DisplayName 'Contoso RiskOps'
```

Inspect discovered plugins:

```powershell
Get-SEGenerationPlugin -PluginRootPath 'E:\source\DataGen\sdk\examples\CountryTaxIds.Script'
```

Validate a package root:

```powershell
Test-SEGenerationPluginPackage -PluginRootPath 'E:\source\DataGen\sdk\examples\CountryTaxIds.Script'
```

Validate the stricter pack contract when authoring a scenario pack:

```powershell
Test-SEGenerationPluginPackage `
  -PluginRootPath 'E:\work\plugins\Contoso.RiskOps' `
  -ValidatePackContract
```

For binary plugins, inspect or validate with trust settings:

```powershell
Get-SEGenerationPlugin `
  -PluginRootPath 'E:\source\DataGen\sdk\examples\CompanyRegistrationIds.Binary' `
  -AllowAssemblyPlugins `
  -PluginAllowedContentHash '<hash>'
```

## Registration and Reuse

### What an approval binds

The approved content hash covers the built plugin, not its source:

| Component | Script plugin | Binary plugin |
| --- | --- | --- |
| Manifest bytes | yes | yes |
| Entry point (script / assembly) bytes | yes | yes |
| Plugin-local data files | yes | yes |
| Recursive inventory of the entry point's output directory | no | yes |

For a binary plugin the last row is the consequential one. The build copies DataGen's own assemblies (such as `SyntheticEnterprise.Contracts.dll`) into the entry point's output directory, and those files are inside the hash. Identical plugin source compiled against a different DataGen version therefore yields a different content hash.

**A DataGen version change invalidates every approved binary-plugin registration.** An approval cannot be carried across versions. This is correct for a trust boundary — the thing approved is the thing that runs, and an assembly compiled against different dependencies is a different artifact — but it means re-approval is part of taking an upgrade, not an optional follow-up.

At upgrade time, for each binary plugin:

1. Rebuild against the new DataGen version.
2. Read the new hash with `Get-SEGenerationPlugin -PluginRootPath '<path>' -AllowAssemblyPlugins`.
3. Re-run `Register-SEGenerationPlugin`, or update any hash you pin to `-PluginAllowedContentHash`. A pinned hash checked into source control is version-specific; update it in the same change that takes the upgrade.

Any rebuild changes the hash, so a Debug/Release switch or a non-deterministic compile has the same effect. If a hash changes when you did not expect it to, confirm the package is the one you intend to run before re-approving it.

Script plugins carry no compiled package and are unaffected by a DataGen upgrade.

### Commands

Register a plugin after review so its approved content hash can be reused later:

```powershell
Register-SEGenerationPlugin -PluginRootPath 'E:\source\DataGen\sdk\examples\CountryTaxIds.Script'
```

Install a plugin package into the managed plugin root and register it in one step:

```powershell
Install-SEGenerationPluginPackage -PluginRootPath 'E:\source\DataGen\sdk\examples\CountryTaxIds.Script'
```

List registered plugins:

```powershell
Get-SEGenerationPluginRegistration
```

Remove a registration:

```powershell
Unregister-SEGenerationPlugin -Capability CountryTaxIds
```

Execute using stored approvals:

```powershell
New-SEEnterpriseWorld `
  -ScenarioPath 'E:\source\DataGen\examples\regional-manufacturer.json' `
  -EnablePluginCapability CountryTaxIds `
  -UseRegisteredPlugins
```

## Execution

Script plugin example:

```powershell
$plugin = Get-SEGenerationPlugin -PluginRootPath 'E:\source\DataGen\sdk\examples\CountryTaxIds.Script'

New-SEEnterpriseWorld `
  -ScenarioPath 'E:\source\DataGen\examples\regional-manufacturer.json' `
  -PluginRootPath 'E:\source\DataGen\sdk\examples\CountryTaxIds.Script' `
  -EnablePluginCapability CountryTaxIds `
  -RequirePluginHashApproval `
  -PluginAllowedContentHash $plugin.ContentHash
```

Binary plugin example:

```powershell
dotnet build 'E:\source\DataGen\sdk\examples\CompanyRegistrationIds.Binary\CompanyRegistrationIds.Binary.csproj'

$plugin = Get-SEGenerationPlugin `
  -PluginRootPath 'E:\source\DataGen\sdk\examples\CompanyRegistrationIds.Binary' `
  -AllowAssemblyPlugins

New-SEEnterpriseWorld `
  -ScenarioPath 'E:\source\DataGen\examples\regional-manufacturer.json' `
  -PluginRootPath 'E:\source\DataGen\sdk\examples\CompanyRegistrationIds.Binary' `
  -EnablePluginCapability CompanyRegistrationIds `
  -AllowAssemblyPlugins `
  -PluginAllowedContentHash $plugin.ContentHash
```

## Example Plugins
- [CountryTaxIds.Script](E:\source\DataGen\sdk\examples\CountryTaxIds.Script)
- [CompanyRegistrationIds.Binary](E:\source\DataGen\sdk\examples\CompanyRegistrationIds.Binary)
