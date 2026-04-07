$world = New-SEEnterpriseWorld `
    -ScenarioPath .\regional_manufacturer.scenario.json `
    -CatalogRootPath ..\catalogs

$world | Get-SEWorldSummary

$world = $world |
    Add-SEIdentityLayer -RegenerationMode SkipIfPresent |
    Add-SEInfrastructureLayer -RegenerationMode SkipIfPresent |
    Add-SERepositoryLayer -RegenerationMode SkipIfPresent |
    Invoke-SEAnomalyProfile

$world.WorldMetadata | Select-Object CatalogRootPath, CatalogKeys, AppliedLayers, AppliedAnomalyProfiles
$world | Export-SEEnterpriseWorld -Format Csv -OutputPath .\out -PassThru
