# Example Plugins

## CountryTaxIds.Script
- type: script plugin
- style: catalog-backed
- purpose: emits synthetic country-aware personal identifier records
- useful for: demonstrating plugin-local data catalogs and constrained PowerShell execution

## CompanyRegistrationIds.Binary
- type: binary plugin
- style: logic-driven
- purpose: emits synthetic registration identifiers for companies
- useful for: demonstrating the assembly plugin contract and isolated host execution

## Recommended Workflow
1. Inspect the plugin root with `Get-SEGenerationPlugin`.
2. Validate the package with `Test-SEGenerationPluginPackage`.
3. Build the binary example if you are using it.
4. Approve content hashes.
5. Execute through `New-SEEnterpriseWorld`.

## Choosing The Right Kind Of Extension

Use a DataGen plugin when you want to make the generated dataset richer.

Good examples:
- add synthetic identifiers, lifecycle metadata, or compliance attributes
- generate synthetic operational history tied to the generated users, systems, applications, and repositories
- introduce realism overlays such as stale metadata, missing relationships, or neutral audit-style event streams

Do not use a DataGen plugin when the real goal is downstream integration.

Bad examples:
- convert output into another product's import schema
- call another tool's ingestion or synchronization workflow
- remap DataGen entities into a vendor-specific node or edge model as the plugin's primary purpose
