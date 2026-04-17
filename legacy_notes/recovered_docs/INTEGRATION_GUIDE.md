# Integration Guide

## Recommended merge order
1. Merge `src/SyntheticEnterprise.Core` snapshot and persistence contracts.
2. Merge exporter contracts and writers.
3. Merge regeneration ownership and repair services.
4. Merge anomaly normalization contracts and services.
5. Merge module surface and validation utilities.
6. Fold examples and tests into the actual solution structure.

## Expected top-level solution areas
- `SyntheticEnterprise.Core`
- `SyntheticEnterprise.Exporting`
- `SyntheticEnterprise.PowerShell`
- `SyntheticEnterprise.Tests`

## Immediate follow-up work
- Align namespaces to the real repository.
- Wire services into DI / composition root.
- Resolve duplicate command definitions where later milestones supersede earlier cmdlets.
- Add project files for newly introduced folders where absent.
- Run and harden round-trip and golden-file tests.
