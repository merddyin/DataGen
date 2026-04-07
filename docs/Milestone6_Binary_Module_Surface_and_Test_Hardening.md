# Milestone 6: Binary Module Surface Consolidation and Test Harness Hardening

## Objective
Consolidate the binary PowerShell module surface into a predictable, production-oriented shape and harden the validation/test harness around world generation, layer enrichment, persistence, export, scenario authoring, and anomaly workflows.

## Goals
- standardize cmdlet naming and parameter-set design
- formalize service boundaries behind cmdlets
- define module composition and dependency registration
- add test categories for contract, integration, pipeline, and regression coverage
- define deterministic test fixtures and golden-output baselines
- strengthen validation of manifests, exports, snapshots, and scenario resolution

## Proposed cmdlet surface
### Generation and enrichment
- `New-SEEnterpriseWorld`
- `Add-SEIdentityLayer`
- `Add-SEInfrastructureLayer`
- `Add-SERepositoryLayer`
- `Invoke-SEAnomalyProfile`

### Inspection and authoring
- `Get-SEWorldSummary`
- `Get-SEWorldSession`
- `Test-SEScenario`
- `Resolve-SEScenario`
- `Get-SEScenarioTemplate`
- `New-SEScenarioFromTemplate`
- `Merge-SEScenarioOverlay`

### Persistence and export
- `Save-SEEnterpriseWorld`
- `Import-SEEnterpriseWorld`
- `Export-SEEnterpriseWorld`

## Module design decisions
- object-first, pipeline-native commands remain the default model
- commands only materialize to disk when explicitly asked
- service injection is isolated from cmdlet logic
- cmdlets should produce rich objects and use warnings for soft issues
- parameter sets should minimize ambiguous combinations
- seed-driven workflows should be deterministic in tests

## Test harness categories
- **Contract tests**: DTO/schema compatibility and serialization contracts
- **Cmdlet tests**: parameter binding, pipeline behavior, pass-thru behavior, warnings
- **Integration tests**: generation to layering to export to snapshot round-trips
- **Golden-file tests**: stable CSV/JSON/manifests for known seeded scenarios
- **Validation tests**: referential integrity and anomaly normalization checks
- **Regression tests**: fixed bug cases captured as small fixtures

## Deliverables
- consolidated module manifest/service registration scaffold
- cmdlet base helpers and shared validation utilities
- test fixture builder scaffolding
- golden-output comparer scaffolding
- deterministic integration workflow example
