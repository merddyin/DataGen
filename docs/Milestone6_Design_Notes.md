# Milestone 6 Design Notes

## Intended project shape
- `SyntheticEnterprise.Module`: PowerShell cmdlets and module registration
- `SyntheticEnterprise.Core`: domain contracts and orchestration services
- `SyntheticEnterprise.Export`: export coordination and writers
- `SyntheticEnterprise.Tests`: xUnit test projects with fixtures and golden baselines

## Service boundary guidance
Cmdlets should remain thin. They should validate input, resolve services, call orchestrators, emit pipeline objects, and surface warnings/errors clearly.

## Test strategy guidance
Golden-file tests should normalize timestamps, GUIDs, and transient paths before comparison when those values are not part of the contract under test.

## Open follow-up items
- decide whether module registration uses a lightweight internal service provider or manual composition root
- align warning/error taxonomy across cmdlets
- define stable seeded fixture scenarios for manufacturer, SaaS, and healthcare templates
