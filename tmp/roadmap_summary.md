# Roadmap Summary

Recovered progression across recent iterations:

1. Geography
2. Identity
3. Infrastructure
4. Repository
5. Exporter / pipeline-first output
6. Pipeline-native layer processing
7. Idempotent and selective regeneration behavior
8. Catalog-aware enrichment
9. World/session manifest lineage

Current architectural direction:
- pipeline-first
- object-first cmdlet design
- explicit export materialization
- clone-before-mutate enrichment
- lineage tracking in metadata/session manifest
- practical demo-oriented anomaly injection

Best next implementation targets:
- content-based catalog hashing
- persisted snapshot/import support
- comparison of worlds across runs
- richer merge semantics
- deeper directory modeling
