Import-Module 'E:\source\DataGen\src\SyntheticEnterprise.PowerShell\bin\Debug\net8.0\SyntheticEnterprise.PowerShell.dll' -Force

$scriptRoot = 'E:\source\DataGen\sdk\examples\CountryTaxIds.Script'
$binaryRoot = 'E:\source\DataGen\sdk\examples\CompanyRegistrationIds.Binary'
$scenarioPath = 'E:\source\DataGen\examples\regional-manufacturer.json'

Write-Host 'Inspecting script plugin...'
$scriptPlugin = Get-SEGenerationPlugin -PluginRootPath $scriptRoot
$scriptPlugin | Format-Table Capability,Parsed,Valid,Trusted,ContentHash -AutoSize

Write-Host 'Validating script plugin package...'
Test-SEGenerationPluginPackage -PluginRootPath $scriptRoot | Format-List

Write-Host 'Registering script plugin for reuse...'
Register-SEGenerationPlugin -PluginRootPath $scriptRoot | Format-List
Get-SEGenerationPluginRegistration | Format-Table Capability,RootPath,ContentHash -AutoSize

Write-Host 'Building binary plugin example...'
dotnet build (Join-Path $binaryRoot 'CompanyRegistrationIds.Binary.csproj')

Write-Host 'Inspecting binary plugin...'
$binaryPlugin = Get-SEGenerationPlugin -PluginRootPath $binaryRoot -AllowAssemblyPlugins
$binaryPlugin | Format-Table Capability,Parsed,Valid,Trusted,RequiresAssemblyOptIn,RequiresHashApproval,ContentHash -AutoSize

Write-Host 'Validating binary plugin package...'
Test-SEGenerationPluginPackage -PluginRootPath $binaryRoot -AllowAssemblyPlugins -PluginAllowedContentHash $binaryPlugin.ContentHash | Format-List

Write-Host 'Generating world with script plugin...'
$scriptResult = New-SEEnterpriseWorld -ScenarioPath $scenarioPath -EnablePluginCapability CountryTaxIds -UseRegisteredPlugins

$scriptResult.World.PluginRecords |
  Where-Object PluginCapability -eq 'CountryTaxIds' |
  Select-Object -First 5 |
  Format-Table PluginCapability,RecordType,AssociatedEntityType,AssociatedEntityId -AutoSize

Write-Host 'Generating world with binary plugin...'
$binaryResult = New-SEEnterpriseWorld `
  -ScenarioPath $scenarioPath `
  -PluginRootPath $binaryRoot `
  -EnablePluginCapability CompanyRegistrationIds `
  -AllowAssemblyPlugins `
  -PluginAllowedContentHash $binaryPlugin.ContentHash

$binaryResult.World.PluginRecords |
  Where-Object PluginCapability -eq 'CompanyRegistrationIds' |
  Format-Table PluginCapability,RecordType,AssociatedEntityType,AssociatedEntityId -AutoSize
