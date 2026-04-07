$world = New-SEEnterpriseWorld -ScenarioPath .\regional_manufacturer.scenario.json -CatalogRootPath .\catalogs

$world = $world |
    Add-SEIdentityLayer |
    Add-SEInfrastructureLayer |
    Add-SERepositoryLayer |
    Invoke-SEAnomalyProfile

$manifest = $world | Export-SEEnterpriseWorld `
    -Format Csv `
    -Profile Normalized `
    -OutputPath .\out `
    -ArtifactPrefix regional_manufacturer_demo `
    -IncludeManifest `
    -IncludeSummary

$world = $world | Export-SEEnterpriseWorld `
    -Format Json `
    -Profile Graph `
    -OutputPath .\out `
    -ArtifactPrefix regional_manufacturer_graph `
    -IncludeManifest `
    -IncludeSummary `
    -PassThru
