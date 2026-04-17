# Synthetic Enterprise — Concrete Implementation Plan

## Objective
Move the current synthetic enterprise scaffold from a strong architecture prototype into a reusable generator platform for demo dataset creation.

## Current Position
The project already has working concepts for:
- organization
- geography
- identity
- infrastructure
- repositories
- pipeline-first generation/export
- layer idempotency scaffolding
- catalog-aware enrichment
- world/session manifest lineage

## Milestone 1 — Persisted Snapshot/Import Support
### Goals
- save a generated `GenerationResult` to disk
- reload it later without loss of lineage
- preserve schema version identity
- produce authoritative catalog identity using content hashing

### Deliverables
1. `Save-SEEnterpriseWorld` cmdlet
2. `Import-SEEnterpriseWorld` cmdlet
3. canonical snapshot envelope contracts
4. schema version marker and compatibility checks
5. content-based catalog hashing service
6. snapshot compression option
7. snapshot validation/reporting

### Acceptance Criteria
- a world can be saved and re-imported with no broken references
- session manifest survives round-trip
- metadata records snapshot read/write history
- schema mismatch produces clear warnings or a controlled failure
- catalog identity reflects file contents rather than only catalog keys

## Milestone 2 — Exporter Slice v2
- stable CSV bundle export
- stable JSON bundle export
- link-table export
- manifest summary export
- dataset profile export
- normalized vs flattened export modes

## Milestone 3 — Selective Regeneration Hardening
- hardened `Merge` behavior
- downstream reference repair rules
- explicit invalidation warnings
- richer anomaly metadata
