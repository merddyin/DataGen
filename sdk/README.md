# DataGen Plugin SDK

This SDK is the starting point for writing external DataGen plugins without having to reverse-engineer the runtime.

## Included
- authoring guidance for manifests, safety rules, and execution flow
- a minimal script plugin example that uses plugin-local catalog data
- a minimal binary plugin example that uses only logic
- example commands for inspection, validation, build, and execution

## Quick Start
1. Inspect a plugin root:
   - `Get-SEGenerationPlugin -PluginRootPath 'E:\source\DataGen\sdk\examples\CountryTaxIds.Script'`
2. Validate a plugin package:
   - `Test-SEGenerationPluginPackage -PluginRootPath 'E:\source\DataGen\sdk\examples\CountryTaxIds.Script'`
3. Install and register an approved plugin into the managed plugin store:
   - `Install-SEGenerationPluginPackage -PluginRootPath 'E:\source\DataGen\sdk\examples\CountryTaxIds.Script'`
4. Register an approved plugin directly from its source root when install is not needed:
   - `Register-SEGenerationPlugin -PluginRootPath 'E:\source\DataGen\sdk\examples\CountryTaxIds.Script'`
5. Build the binary sample if needed:
   - `dotnet build E:\source\DataGen\sdk\examples\CompanyRegistrationIds.Binary\CompanyRegistrationIds.Binary.csproj`
6. Re-inspect or re-validate with trust settings:
   - `Get-SEGenerationPlugin -PluginRootPath 'E:\source\DataGen\sdk\examples\CompanyRegistrationIds.Binary' -AllowAssemblyPlugins -PluginAllowedContentHash '<hash>'`
7. Execute with `New-SEEnterpriseWorld` once the plugin is trusted.

## Documents
- [Plugin SDK Guide](E:\source\DataGen\sdk\PLUGIN_SDK.md)
- [Example Plugins](E:\source\DataGen\sdk\examples\README.md)

## Safety Model
- Plugins are data generators or enrichers, not automation scripts.
- Script plugins run in a constrained PowerShell host.
- Binary plugins run in an isolated host process.
- Binary plugins require explicit opt-in and hash approval by default.
- The orchestrator owns logging, export, and snapshot side effects.
- Registrations persist approved hashes so trusted plugins can be reused with `-UseRegisteredPlugins`.

## Architectural Boundary
- DataGen's job is to procedurally generate synthetic enterprise data.
- DataGen plugins may extend that generated dataset, enrich it, or introduce realism/deviation overlays that are still part of the synthetic world.
- DataGen plugins must not translate DataGen output into the contracts, cmdlets, APIs, or import envelopes of specific downstream systems.
- System-specific adapters, bridges, exporters, and ingestion shapers belong outside the DataGen plugin ecosystem, even when they consume DataGen output.

In practical terms:
- a good DataGen plugin adds more synthetic data that references the generated world
- a bad DataGen plugin reshapes that world for one particular consumer system

## Good Fits
- add country-specific registration identifiers for companies or tax identifiers for people
- generate synthetic maintenance tickets, audit-style events, or operational activity records tied to real generated users, systems, applications, and repositories
- introduce richer realism deviations such as stale ownership metadata, inconsistent classifications, or synthetic lifecycle drift
- add synthetic industry-specific records such as facility certifications, branch operating schedules, or neutral business-process evidence
- enrich generated applications, devices, or repositories with additional synthetic metadata sourced from plugin-local catalogs

## Bad Fits
- convert DataGen output into another product's import schema or API payload
- call another tool's ingestion cmdlets or web APIs directly from the plugin
- emit records whose primary purpose is to satisfy one vendor's table layout rather than to enrich the synthetic enterprise dataset itself
- perform system-specific post-processing such as remapping DataGen entities into a discovery platform's proprietary node and edge model
- automate downstream operational workflows such as importing, exporting, synchronizing, or reconciling with an external platform
