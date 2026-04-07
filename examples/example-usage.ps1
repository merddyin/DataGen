Import-Module .\SyntheticEnterprise.PowerShell.dll

$catalog  = Import-SECatalog -Path .\seed_data
$scenario = Import-SEScenario -Path .\examples\regional-manufacturer.json

$world = New-SEEnterpriseWorld -Catalog $catalog -Scenario $scenario |
    Add-SEIdentityLayer |
    Add-SEInfrastructureLayer |
    Add-SERepositoryLayer |
    Invoke-SEAnomalyProfile -Profile DefaultMessyHybrid

$world | Get-SEWorldSummary
$world | Export-SEWorld -OutputPath .\out -Format Csv -IncludeManifest
