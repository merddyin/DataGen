# CompanyRegistrationIds.Binary

This example is a minimal logic-driven binary plugin.

## What it does
- reads generated companies from the input world
- derives a synthetic registration identifier from each company ID
- emits `CompanyRegistrationId` plugin records

## Build
```powershell
dotnet build 'E:\source\DataGen\sdk\examples\CompanyRegistrationIds.Binary\CompanyRegistrationIds.Binary.csproj'
```

## Inspect
```powershell
Get-SEGenerationPlugin -PluginRootPath 'E:\source\DataGen\sdk\examples\CompanyRegistrationIds.Binary' -AllowAssemblyPlugins
```

## Validate
```powershell
Test-SEGenerationPluginPackage -PluginRootPath 'E:\source\DataGen\sdk\examples\CompanyRegistrationIds.Binary' -AllowAssemblyPlugins
```

## Execute
```powershell
$plugin = Get-SEGenerationPlugin -PluginRootPath 'E:\source\DataGen\sdk\examples\CompanyRegistrationIds.Binary' -AllowAssemblyPlugins

New-SEEnterpriseWorld `
  -ScenarioPath 'E:\source\DataGen\examples\regional-manufacturer.json' `
  -PluginRootPath 'E:\source\DataGen\sdk\examples\CompanyRegistrationIds.Binary' `
  -EnablePluginCapability CompanyRegistrationIds `
  -AllowAssemblyPlugins `
  -PluginAllowedContentHash $plugin.ContentHash
```
