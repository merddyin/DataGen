# v10 Idempotency and Regeneration

Referenced files
- synthetic_enterprise_idempotent_layers.zip
- synthetic_enterprise_module_scaffold_v10/SyntheticEnterprise_Idempotency_and_Regeneration.md
- synthetic_enterprise_module_scaffold_v10/examples/pipeline_first_demo.ps1

What this adds
- `LayerRegenerationMode`
- `LayerProcessingOptions`
- `IWorldCloner`
- `WorldCloner`
- revised `LayerProcessor`

What changed
- layer commands now use clone-before-mutate
- layer commands support regeneration modes:
  - `SkipIfPresent`
  - `ReplaceLayer`
  - `Merge`
- anomaly application now tracks previously applied profiles
- `WorldMetadata` now tracks:
  - applied layers
  - applied anomaly profiles

Caveat
- `Merge` is still a scaffold value right now and is not yet deeply conflict-aware
