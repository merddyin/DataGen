# v11 Catalog-aware Pipeline

Referenced files
- synthetic_enterprise_catalog_aware_pipeline.zip
- synthetic_enterprise_module_scaffold_v11/SyntheticEnterprise_Catalog_Aware_Pipeline.md
- synthetic_enterprise_module_scaffold_v11/examples/pipeline_first_demo.ps1

What changed
- `GenerationResult` now carries `Catalogs`
- `WorldMetadata` now tracks:
  - `CatalogRootPath`
  - `CatalogKeys`
- added `ICatalogContextResolver`
- added `CatalogContextResolver`
- `New-SEEnterpriseWorld` now records catalog provenance in metadata
- `LayerProcessor` now resolves catalogs in a stable order:
  - existing catalogs on the object
  - reload from stored catalog root
  - fallback to default catalogs

Why this matters
- staged pipeline enrichment now preserves the same catalog context
- later `Add-*` and `Invoke-*` calls no longer have to operate with empty catalogs
- the world object now carries more of its own generation lineage
