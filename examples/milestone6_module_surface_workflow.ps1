# Example deterministic workflow scaffold
$scenario = Resolve-SEScenario -Path .\scenarios\regional_manufacturer.scenario.json

$world = New-SEEnterpriseWorld -Scenario $scenario -Seed 424242 -CatalogRootPath .\catalogs |
    Add-SEIdentityLayer |
    Add-SEInfrastructureLayer |
    Add-SERepositoryLayer |
    Invoke-SEAnomalyProfile -Profile Default

$world | Save-SEEnterpriseWorld -Path .\artifacts\regional-manufacturer.snapshot.json.gz
$world | Export-SEEnterpriseWorld -Format Csv -OutputPath .\artifacts\csv -PassThru |
    Get-SEWorldSummary

Get-SEModuleCommandSurface
