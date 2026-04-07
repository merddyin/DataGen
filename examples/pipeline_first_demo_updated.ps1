$world = New-SEEnterpriseWorld `
    -ScenarioPath .\regional_manufacturer.scenario.json `
    -CatalogRootPath ..\catalogs

$world | Get-SEWorldSummary

$world = $world |
    Add-SEIdentityLayer |
    Add-SEInfrastructureLayer |
    Add-SERepositoryLayer |
    Invoke-SEAnomalyProfile

$world | Export-SEEnterpriseWorld -Format Csv -OutputPath .\out -PassThru

$world.World.People | Select-Object DisplayName, Title, Country, OfficeId | Export-Csv .\people.csv -NoTypeInformation
