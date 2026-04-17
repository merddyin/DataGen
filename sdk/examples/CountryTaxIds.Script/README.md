# CountryTaxIds.Script

This example is a minimal catalog-backed script plugin.

## What it does
- reads a local CSV rules file
- maps generated people to country-specific identifier formats
- emits `NationalIdentifier` plugin records

## Inspect
```powershell
Get-SEGenerationPlugin -PluginRootPath 'E:\source\DataGen\sdk\examples\CountryTaxIds.Script'
```

## Validate
```powershell
Test-SEGenerationPluginPackage -PluginRootPath 'E:\source\DataGen\sdk\examples\CountryTaxIds.Script'
```

## Execute
```powershell
$plugin = Get-SEGenerationPlugin -PluginRootPath 'E:\source\DataGen\sdk\examples\CountryTaxIds.Script'

New-SEEnterpriseWorld `
  -ScenarioPath 'E:\source\DataGen\examples\regional-manufacturer.json' `
  -PluginRootPath 'E:\source\DataGen\sdk\examples\CountryTaxIds.Script' `
  -EnablePluginCapability CountryTaxIds `
  -RequirePluginHashApproval `
  -PluginAllowedContentHash $plugin.ContentHash
```
