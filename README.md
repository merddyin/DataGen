# Synthetic Enterprise Integrated Package

This package consolidates the recoverable scaffold work into a single merge-ready project layout.

## Included milestones
- Legacy slice notes: geography, identity, infrastructure, repositories, pipeline-first, pipeline-native, idempotency, catalog-aware pipeline, world/session manifest
- Phase 1: snapshot persistence, schema compatibility, catalog hashing
- Milestone 2: exporter v2
- Milestone 3: selective regeneration hardening
- Milestone 5: anomaly framework v2
- Milestone 6: binary module surface and test hardening

## Also included
- examples for each major milestone
- schema files
- test scaffolds
- recovered roadmap notes from earlier slices

## Important limitation
The original full repository tree was not present in the workspace when this package was assembled. This is therefore a **comprehensive merge-ready integrated package**, not a literal in-place patch of the original repository.

## Notable gap
The original Milestone 4 source package was not recoverable from the workspace at assembly time. A reconstructed summary for that milestone is included in `docs/Milestone4_Reconstructed_Summary.md`, and legacy notes are included under `legacy_notes/`.
