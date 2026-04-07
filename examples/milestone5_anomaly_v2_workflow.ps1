$world = New-SEEnterpriseWorld -ScenarioPath .\regional_manufacturer.scenario.json -CatalogRootPath .\catalogs
$world = $world |
    Add-SEIdentityLayer |
    Add-SEInfrastructureLayer |
    Add-SERepositoryLayer |
    Invoke-SEAnomalyProfile

$world | Get-SEWorldSummary
$world | Export-SEEnterpriseWorld -Format Csv -OutputPath .\out
