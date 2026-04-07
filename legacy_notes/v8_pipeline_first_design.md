# v8 Pipeline-first Design

Referenced files
- synthetic_enterprise_pipeline_first.zip
- synthetic_enterprise_module_scaffold_v8/SyntheticEnterprise_Pipeline_First_Design.md
- synthetic_enterprise_module_scaffold_v8/examples/pipeline_first_demo.ps1

What changed
- `New-SEEnterpriseWorld` now writes a `GenerationResult` object to the pipeline
- `Export-SEEnterpriseWorld` accepts pipeline input and only writes files when explicitly called
- `Get-SEWorldSummary` accepts pipeline input and returns summary objects
- `IExporter` now returns an `ExportResult`
- added `FileBundleExporter` for explicit CSV/JSON bundle creation
- added export manifest contracts:
  - `ExportArtifact`
  - `ExportManifest`
  - `ExportResult`

Intended flow
```powershell
$world = New-SEEnterpriseWorld -ScenarioPath .egional_manufacturer.scenario.json -CatalogRootPath .\catalogs
$world | Get-SEWorldSummary
$world | Export-SEEnterpriseWorld -Format Csv -OutputPath .\out
```

This puts file creation where it belongs: as an explicit materialization step, not the default behavior of the generator.
