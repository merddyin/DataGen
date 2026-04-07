# Phase 1 Design — Snapshot, Import, Schema Versioning, and Catalog Hashing

## Summary
Phase 1 introduces a durable persistence boundary for the synthetic enterprise world model. The intent is to support long-lived demo assets, resumable workflows, and reproducible catalog provenance.

## New Contracts
- `SnapshotEnvelope<T>`
- `SnapshotMetadata`
- `CatalogContentFingerprint`
- `CatalogFileFingerprint`
- `SchemaCompatibilityAssessment`
- `ImportResult<T>`

## New Services
- `ISnapshotSerializer` / `SnapshotSerializer`
- `ICatalogFingerprintService` / `CatalogFingerprintService`
- `ISchemaCompatibilityService` / `SchemaCompatibilityService`
- `ISnapshotPersistenceService` / `SnapshotPersistenceService`

## New Cmdlets
- `Save-SEEnterpriseWorld`
- `Import-SEEnterpriseWorld`

## Snapshot Envelope Rules
- no ad hoc direct serialization of runtime objects from cmdlets
- all snapshots are serialized through the envelope
- envelope carries format identity and future upgrade hooks

## Import Rules
- reject unsupported snapshot formats
- evaluate compatibility before materializing the final object
- return compatibility messages for soft mismatches
- optionally bypass compatibility checks for controlled migration work
