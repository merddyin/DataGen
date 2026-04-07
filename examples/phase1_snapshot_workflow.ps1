# Example workflow for the Phase 1 persistence boundary.
# Replace the placeholder object with the real New-SEEnterpriseWorld output after integration.

$world = [pscustomobject]@{
    WorldId = 'world-001'
    GeneratedUtc = [DateTime]::UtcNow
    Notes = 'Placeholder object for scaffold workflow'
}

$savePath = Join-Path $PSScriptRoot '..rtifactsegional_manufacturer.snapshot.json.gz'

$world |
    Save-SEEnterpriseWorld         -Path $savePath         -CatalogRootPath (Join-Path $PSScriptRoot '..\catalogs')         -SourceScenarioPath '.\examplesegional_manufacturer.scenario.json'         -SourceScenarioName 'regional_manufacturer'         -Compress

$imported = Import-SEEnterpriseWorld -Path $savePath
$imported | Format-List *
