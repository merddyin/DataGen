# DataGen Release-by-Release Engineering Plan

## Purpose

This document translates the product roadmap into an execution-oriented engineering plan.

It is intentionally release-shaped rather than strategy-shaped. The goal is to give engineering, product, and architecture work a shared sequence of execution with clear workstreams, deliverables, repo impact, and release gates.

## Planning assumptions

1. DataGen remains a synthetic source-environment platform, not a consumer-specific adapter platform.
2. The current enterprise world model remains the base substrate for new domain and temporal work.
3. New capability areas should be extensible through a pack-oriented model instead of one-off hardcoding.
4. Export contracts and test harnesses must grow alongside generation capability, not after it.
5. Reality and quality checks should keep pace with new scope so DataGen does not expand faster than it can remain believable.

## Execution order

1. Release 0.4.x: Domain Packs Foundation
2. Release 0.5.x: Temporal Simulation Foundation
3. Release 0.6.x: Scenario Productization
4. Parallel track: Pack SDK hardening
5. Parallel track: Quality and realism diagnostics

## Release 0.4.x: Domain Packs Foundation

### Release outcome

DataGen can generate a core enterprise plus one or more first-class operating domains through a pack model that integrates with generation, export, and tests.

### Engineering themes

- pack lifecycle and registration
- pack-aware scenario composition
- new entity families and relationship surfaces
- export extension points
- first-party pack implementations

### Workstreams

#### Workstream 0.4.1: Pack platform contract

Scope:

- define pack manifest shape
- define pack registration/discovery model
- define pack execution hooks by generation phase
- define pack dependency declaration and ordering
- define pack-scoped configuration input model

Expected repo impact:

- `src/SyntheticEnterprise.Contracts/`
- `src/SyntheticEnterprise.Core/Generation/`
- `src/SyntheticEnterprise.Core/Plugins/`
- `src/SyntheticEnterprise.PowerShell/`
- `schemas/`

Key deliverables:

- pack manifest contract
- pack loader/resolver
- generation pipeline insertion points
- pack configuration serialization support

Release gate:

- a no-op sample pack can be registered, enabled in a scenario, and flow through generation without modifying core business logic

#### Workstream 0.4.2: Export extensibility for packs

Scope:

- allow pack-defined entity tables
- allow pack-defined link tables
- extend manifest and summary generation for pack artifacts
- keep normalized export conventions coherent

Expected repo impact:

- `src/SyntheticEnterprise.Exporting/`
- `src/SyntheticEnterprise.Contracts/`
- `tests/SyntheticEnterprise.Exporting.Tests/`

Key deliverables:

- export registration surface for pack entities
- pack-aware manifest metadata
- export tests for dynamically added entity families

Release gate:

- a sample pack can emit at least one entity artifact and one relationship artifact through the normalized export surface

#### Workstream 0.4.3: ITSM pack

Scope:

- incidents
- service requests
- change records
- approvals
- assignment groups
- SLA and queue metadata

Expected repo impact:

- new pack implementation under a first-party pack location
- new scenarios and walkthroughs
- tests for generation and exporting

Suggested entities:

- `incidents`
- `service_requests`
- `change_records`
- `change_approvals`
- `assignment_groups`

Release gate:

- a scenario can generate ITSM records that link coherently to users, groups, applications, devices, and offices

#### Workstream 0.4.4: SecOps pack

Scope:

- alerts
- cases
- detections
- evidence artifacts
- remediation actions

Expected repo impact:

- new pack implementation
- relationship additions to assets, users, applications, repositories, and policies
- docs and walkthrough coverage

Suggested entities:

- `security_alerts`
- `security_cases`
- `detection_rules`
- `security_evidence`
- `remediation_actions`

Release gate:

- a scenario can generate alert-to-case-to-asset flows that are exportable and coherent

#### Workstream 0.4.5: BusinessOps pack

Scope:

- vendors
- contracts
- procurement records
- invoices
- business asset ownership

Expected repo impact:

- new pack implementation
- docs and export additions

Suggested entities:

- `vendors`
- `contracts`
- `purchase_orders`
- `invoices`
- `business_assets`

Release gate:

- a scenario can generate a business-operations domain that links vendors, applications, offices, and organizational units

### Cross-cutting engineering tasks

- scenario syntax for pack enablement
- pack-aware sample scenarios
- docs site updates for pack concepts
- first-party pack naming and namespace conventions
- compatibility tests for pack ordering and pack dependencies

### Risks

- unclear pack boundaries leading to leakage into core
- export sprawl without naming conventions
- first-party packs being built before the pack platform is stable enough

### Exit criteria

1. At least one pack can be enabled/disabled via scenario input.
2. At least one pack-defined entity family exports successfully.
3. ITSM and SecOps prototypes work end-to-end.
4. Pack tests run as part of the normal CI path.

## Release 0.5.x: Temporal Simulation Foundation

### Release outcome

DataGen can generate reproducible timeline-based environments, including events and time-sliced snapshots.

### Engineering themes

- event contracts
- timeline generation engine
- snapshot derivation
- temporal pack integration

### Workstreams

#### Workstream 0.5.1: Event and timeline substrate

Scope:

- core event contract
- timeline configuration model
- seed-stable event generation
- effective window modeling

Expected repo impact:

- `src/SyntheticEnterprise.Contracts/`
- `src/SyntheticEnterprise.Core/Generation/`
- `schemas/`

Release gate:

- one scenario and seed can deterministically regenerate the same event stream

#### Workstream 0.5.2: Identity and workforce drift

Scope:

- hires
- transfers
- manager changes
- entitlement growth
- terminations

Expected repo impact:

- `src/SyntheticEnterprise.Core/Generation/Identity/`
- `src/SyntheticEnterprise.Core/Generation/Organization/`
- exports/tests

Release gate:

- user lifecycle events can be emitted and reflected in time-sliced snapshots

#### Workstream 0.5.3: Infrastructure and policy change

Scope:

- device replacement
- software rollout
- baseline changes
- policy evolution
- server role changes

Expected repo impact:

- `src/SyntheticEnterprise.Core/Generation/Infrastructure/`
- `src/SyntheticEnterprise.Core/Generation/Observed/`
- export/tests

Release gate:

- infrastructure state differs coherently across two generated points in time

#### Workstream 0.5.4: Temporal pack behaviors

Scope:

- incident creation and closure
- alert bursts
- change request progression
- repository/document churn

Expected repo impact:

- pack implementations
- export profile updates

Release gate:

- at least one pack emits time-aware history in addition to static entities

### Cross-cutting engineering tasks

- snapshot-at-date command surface
- event export surfaces
- history-focused walkthroughs
- determinism tests and golden fixtures

### Exit criteria

1. timeline generation is deterministic by seed
2. snapshot-at-date works for base world state
3. at least one pack emits history
4. temporal exports are schema-stable and tested

## Release 0.6.x: Scenario Productization

### Release outcome

Users can compose environments through industry archetypes, overlays, and persona presets with less low-level authoring.

### Engineering themes

- archetype authoring model
- overlay composition
- preset-driven scenario authoring
- recommendation/defaulting logic

### Workstreams

#### Workstream 0.6.1: Archetype catalog

Scope:

- define archetype contract
- ship first-party archetypes
- align example scenarios and walkthroughs

Expected repo impact:

- `src/SyntheticEnterprise.Core/Scenarios/`
- `examples/`
- `website/docs/`

Release gate:

- users can select from a stable set of first-party archetypes

#### Workstream 0.6.2: Overlay model

Scope:

- growth overlay
- post-merger overlay
- compliance-heavy overlay
- under-governed overlay
- modernization overlay

Release gate:

- overlays can modify baseline scenario behavior without creating incompatible state

#### Workstream 0.6.3: Guided scenario composition

Scope:

- stronger defaults
- preset recommendations
- scenario wizard improvements
- pack recommendations by archetype

Expected repo impact:

- `src/SyntheticEnterprise.PowerShell/`
- scenario wizard paths
- docs and walkthroughs

Release gate:

- most common scenario creation flows require fewer advanced knobs to produce useful output

### Exit criteria

1. archetypes are first-class and documented
2. overlays are composable and tested
3. wizard/preset paths are simpler than raw authoring for common use cases

## Parallel track: Pack SDK hardening

### Objective

Make pack development a supported engineering workflow.

### Work items

- pack scaffold command
- example pack repository shape
- pack contract tests
- pack validation command
- compatibility guidance and versioning rules

### Success signal

- a new pack can be created by scaffold and validated in CI with minimal manual wiring

## Parallel track: Quality and realism diagnostics

### Objective

Turn dataset quality into a productized engineering surface.

### Work items

- quality report contract
- realism/completeness/exportability score inputs
- pack-aware warnings
- international fidelity checks
- CI-friendly validation outputs

### Success signal

- quality checks become part of routine development rather than only manual audits

## Testing strategy by release

### 0.4.x

- pack registration tests
- pack export tests
- first-party pack integration tests
- scenario enablement tests

### 0.5.x

- timeline determinism tests
- event schema tests
- snapshot-at-date regression tests
- pack history tests

### 0.6.x

- preset/archetype composition tests
- overlay compatibility tests
- wizard and authoring flow tests

## Suggested implementation sequence inside 0.4.x

1. pack manifest and registration contract
2. export registration contract
3. sample no-op pack
4. ITSM pack prototype
5. SecOps pack prototype
6. BusinessOps pack prototype
7. docs and walkthrough support
8. release hardening

## Recommendation

Treat Release 0.4.x as the platform broadening release.

If 0.4.x ships with a clean pack model and two credible first-party packs, DataGen will have crossed the line from a strong synthetic enterprise generator into a more general synthetic operating environment platform.
