# v9 Pipeline-native Layers

Referenced files
- synthetic_enterprise_pipeline_native_layers.zip
- synthetic_enterprise_module_scaffold_v9/SyntheticEnterprise_Pipeline_Native_Layers.md
- synthetic_enterprise_module_scaffold_v9/examples/pipeline_first_demo.ps1

What this adds
- `ILayerProcessor`
- pipeline-native cmdlet stubs for:
  - `Add-SEIdentityLayer`
  - `Add-SEInfrastructureLayer`
  - `Add-SERepositoryLayer`
  - `Invoke-SEAnomalyProfile`

What changed
- `GenerationResult` now carries `WorldMetadata`
- layer commands accept `GenerationResult` from the pipeline
- layer commands return updated `GenerationResult` objects
- the command surface is now consistently object-first:
  - generate
  - enrich
  - mutate
  - inspect
  - export

Intended shape
```powershell
$world = New-SEEnterpriseWorld -ScenarioPath .egional_manufacturer.scenario.json -CatalogRootPath .\catalogs

$world = $world |
    Add-SEIdentityLayer |
    Add-SEInfrastructureLayer |
    Add-SERepositoryLayer |
    Invoke-SEAnomalyProfile

$world | Get-SEWorldSummary
$world | Export-SEEnterpriseWorld -Format Csv -OutputPath .\out
```
