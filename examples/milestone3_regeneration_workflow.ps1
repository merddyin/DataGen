$world = New-SEEnterpriseWorld -ScenarioPath .\regional_manufacturer.scenario.json -CatalogRootPath .\catalogs

$world = $world |
    Add-SEIdentityLayer -RegenerationMode SkipIfPresent |
    Add-SEInfrastructureLayer -RegenerationMode ReplaceLayer |
    Add-SERepositoryLayer -RegenerationMode Merge |
    Invoke-SEAnomalyProfile -RegenerationMode Merge

$world | Get-SEWorldSession
$world | Export-SEEnterpriseWorld -Format Csv -OutputPath .\out -PassThru
