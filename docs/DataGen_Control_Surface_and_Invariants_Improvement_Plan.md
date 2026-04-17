# DataGen Control Surface and Invariants Improvement Plan

## Purpose

Implementation status as of April 17, 2026:

- hard identity uniqueness invariants are now enforced in the generation pipeline
- authored scenarios now preserve `Applications`, `Cmdb`, and `ObservedData`
- the scenario wizard now exposes applications, CMDB, and observed-data sections
- the DTED bridge was revalidated and did not require changes for this slice

This document captures a forward-looking improvement plan for two related platform concerns:

1. **correctness invariants are not consistently enforced as hard guarantees**
2. **the public PowerShell / scenario authoring surface does not fully expose the platform's generation capabilities**

The immediate trigger for this plan was a real dataset-generation exercise for a large synthetic enterprise named **Duckburg Industries**. That exercise surfaced two issues:

- the generated world reported duplicate UPNs
- producing an explicitly shaped, CMDB-rich enterprise with a fixed company name required bypassing the normal PowerShell scenario path and using a custom runner

This document is written to support future context recovery. It intentionally records:

- the current state
- why it is a problem
- the architectural direction
- phased implementation guidance
- acceptance criteria

## Executive Summary

The generator core is more capable than the current public control plane.

Today:

- `ScenarioDefinition` supports `Applications`, `Cmdb`, and `ObservedData` directly【F:/E:/source/DataGen/src/SyntheticEnterprise.Contracts/Configuration/ScenarioDefinition.cs†L5-L23】
- the scenario envelope and authoring pipeline only carry a subset of that shape, which means some major generation knobs are not fully expressible through authored JSON, template/overlay composition, or the wizard path【F:/E:/source/DataGen/src/SyntheticEnterprise.Core/Scenarios/ScenarioAuthoringServices.cs†L198-L231】【F:/E:/source/DataGen/src/SyntheticEnterprise.Core/Scenarios/ScenarioAuthoringServices.cs†L234-L255】
- the scenario wizard focuses on basic details, realism, identity, infrastructure, repositories, and plugins, but does not yet expose first-class application, CMDB, observed-data, or company-shaping sections【F:/E:/source/DataGen/src/SyntheticEnterprise.PowerShell/Cmdlets/NewSEScenarioWizardCommand.cs†L170-L179】【F:/E:/source/DataGen/src/SyntheticEnterprise.PowerShell/Cmdlets/NewSEScenarioWizardCommand.cs†L222-L260】

At the same time, identity generation still allows conditions that can violate real platform constraints. For example, directory accounts are built from generated UPNs and logged as warnings later rather than treated as hard failures when uniqueness breaks down.【F:/E:/source/DataGen/src/SyntheticEnterprise.Core/Generation/Identity/BasicIdentityGenerator.cs†L501-L524】【F:/E:/source/DataGen/src/SyntheticEnterprise.Core/Generation/Identity/BasicIdentityGenerator.cs†L633-L669】

The platform needs two coordinated improvements:

- a **hard-invariant enforcement layer**
- a **fully expressive public authoring and cmdlet surface**

## Current-State Findings

### 1. The resolved scenario model is richer than the authored scenario path

`ScenarioDefinition` includes first-class profiles for:

- `Applications`
- `Cmdb`
- `ObservedData`
- `ExternalPlugins`【F:/E:/source/DataGen/src/SyntheticEnterprise.Contracts/Configuration/ScenarioDefinition.cs†L13-L22】

However, when authored scenarios are resolved through `ScenarioDefaultsResolver`, the resolver only maps a subset of the envelope into `ScenarioDefinition`:

- `Identity`
- `Infrastructure`
- `Repositories`
- `ExternalPlugins`
- `Anomalies`
- `Companies`

and does **not** map:

- `Applications`
- `Cmdb`
- `ObservedData`【F:/E:/source/DataGen/src/SyntheticEnterprise.Core/Scenarios/ScenarioAuthoringServices.cs†L212-L229】

This means the public authoring surface is not isomorphic with the actual generation contract.

### 2. Materialized companies are intentionally convenient, but not sufficiently controllable

When explicit company definitions are not supplied, `MaterializeCompanies` derives company details from a higher-level envelope. That is convenient, but it also means important enterprise-shaping decisions are hidden behind heuristics:

- employee count midpointing
- business-unit/department/team counts
- office counts
- server, database, file-share, and collaboration counts
- telephony defaults【F:/E:/source/DataGen/src/SyntheticEnterprise.Core/Scenarios/ScenarioAuthoringServices.cs†L257-L289】

This is reasonable as a fallback, but it becomes a limitation when the user wants precise scenario intent, such as:

- a specific company name
- a specific scale
- CMDB enabled
- heavier application density
- stronger observed-data coverage

### 3. The current wizard is useful, but not yet a complete advanced control plane

The wizard currently emphasizes:

- basic details
- realism
- identity
- infrastructure
- repositories
- plugins【F:/E:/source/DataGen/src/SyntheticEnterprise.PowerShell/Cmdlets/NewSEScenarioWizardCommand.cs†L170-L179】

That is a good user experience for mainstream scenarios, but it does not expose enough of the actual platform model for advanced enterprise shaping.

### 4. Identity correctness constraints are not yet treated as platform invariants

Directory account generation is deterministic and fairly realistic, but the overall system still allows outcomes that should be impossible in a real environment, such as duplicate UPNs in the same directory scope.

The generator creates directory accounts for:

- employees
- service accounts
- shared mailboxes
- privileged accounts【F:/E:/source/DataGen/src/SyntheticEnterprise.Core/Generation/Identity/BasicIdentityGenerator.cs†L501-L524】【F:/E:/source/DataGen/src/SyntheticEnterprise.Core/Generation/Identity/BasicIdentityGenerator.cs†L540-L568】【F:/E:/source/DataGen/src/SyntheticEnterprise.Core/Generation/Identity/BasicIdentityGenerator.cs†L584-L612】【F:/E:/source/DataGen/src/SyntheticEnterprise.Core/Generation/Identity/BasicIdentityGenerator.cs†L643-L669】

The fact that duplicate UPNs can still emerge indicates that the system currently treats some identity collisions as quality-audit problems rather than hard-generation failures.

That is the wrong boundary.

## Architectural Position

### Principle 1: Separate hard invariants from soft realism deviations

DataGen should be allowed to generate *messy* worlds, but it should never generate *invalid* worlds.

#### Hard invariants

These must always hold:

- unique UPN per directory/tenant scope
- unique SAM account name per directory scope where required
- valid foreign-key-style references across generated entities
- no impossible identity-provider states
- no duplicate canonical identifiers where the modeled system would reject them

#### Soft realism deviations

These remain valid:

- missing owners
- stale or disabled accounts
- inconsistent CMDB records
- overlapping or conflicting policies
- incomplete criticality
- discovery drift
- outdated source-system records

### Principle 2: The public control plane must be as expressive as the real generation contract

The user-facing authoring paths should be able to represent every supported major generation surface without requiring:

- direct code access
- ad hoc runners
- internal-only object construction

That means authored JSON, cmdlets, and the wizard should all have a stable path to:

- applications
- CMDB
- observed data
- company-specific shaping
- realism/deviation choices

### Principle 3: Fallback synthesis is acceptable, but it must remain optional and explicit

Heuristic company materialization is valuable for speed and approachability.

But DataGen also needs a first-class “advanced explicit world shaping” path where the user can intentionally specify:

- company names
- size bands
- location counts
- application density
- infrastructure density
- repository density
- CMDB depth
- observed-data coverage

## Target-State Architecture

## A. Invariant Enforcement Layer

Introduce a dedicated post-generation stage with clear semantics:

- **detect**
- **repair safely where deterministic**
- **fail fast where unsafe**
- **emit a machine-readable invariant audit**

This should run *before* a world is considered finalized for export.

### Proposed responsibilities

- enforce identity uniqueness
- enforce key/reference integrity
- enforce model-specific constraints
- perform deterministic remapping where safe
- distinguish repaired vs unrepaired violations

### Example outcomes

- duplicate UPN candidate detected
  - if a deterministic suffixing rule is allowed, repair
  - otherwise fail generation
- impossible tenant association detected
  - fail generation
- duplicate CMDB source records
  - allow if modeled as source drift, do not fail canonical generation

## B. Unified Public Scenario Contract

Make the public authored scenario shape fully expressive.

### Option 1: preferred

Expand `ScenarioEnvelope` to carry all first-class generation surfaces currently present in `ScenarioDefinition`, including:

- `Applications`
- `Cmdb`
- `ObservedData`

and ensure template merge, overlay application, validation, serialization, and hydration all preserve them.

### Option 2: fallback

If envelope vs resolved scenario must remain separate, then introduce a clearly named advanced-authored shape and keep the mapping lossless.

### Non-goal

Do **not** leave the system in a state where:

- JSON can express less than code
- wizard can express less than JSON
- cmdlets can express less than the underlying generator

## C. Advanced PowerShell Authoring Controls

Provide high-value convenience paths for common enterprise-shaping requests.

Examples:

- explicit company naming
- approximate employee scale
- explicit office count
- enable/disable CMDB
- enable/disable observed data
- application density profile
- infrastructure density profile
- repository density profile

These do not need to replace JSON authoring. They should make common advanced cases easy from the shell.

## D. Advanced Wizard Mode

Keep the current wizard approachable, but add an advanced mode or additional sections for:

- company shaping
- applications
- CMDB
- observed data
- density controls
- optional direct scenario-company editing

## Recommended Implementation Plan

### Phase 1: Correctness and Invariants

Goal:

- prevent invalid identity and core-model output from escaping generation

Work:

- define invariant categories
- add invariant audit model
- enforce UPN uniqueness as a hard rule
- enforce other identity uniqueness rules as needed
- fail or deterministically repair before export

Acceptance criteria:

- generated worlds never contain duplicate UPNs within the same directory scope
- identity collisions are surfaced as invariant failures or deterministic repairs
- warnings no longer carry impossible directory states as “realism”

### Phase 2: Contract Unification

Goal:

- eliminate loss between authored scenarios and resolved scenarios

Work:

- expand `ScenarioEnvelope`
- update serialization helpers
- update merge logic
- update resolver mapping
- update validator coverage

Acceptance criteria:

- authored JSON can express `Applications`, `Cmdb`, and `ObservedData`
- resolved scenarios preserve authored values without internal-only workarounds
- scenario round-trips do not drop major generation surfaces

### Phase 3: PowerShell Surface Expansion

Goal:

- make advanced shaping accessible from cmdlets without custom runners

Work:

- extend scenario authoring cmdlets
- add convenience parameters or scenario mutation helpers
- improve examples for large-enterprise shaping

Acceptance criteria:

- a user can generate a named 15k-person enterprise with CMDB and observed data entirely through supported public surfaces
- no direct code runner is required for common advanced enterprise requests

### Phase 4: Wizard Expansion

Goal:

- make advanced scenario shaping accessible interactively

Work:

- add wizard sections for applications, CMDB, observed data, and company shaping
- add advanced mode prompts
- add richer preview summaries

Acceptance criteria:

- advanced users can shape a large enterprise interactively without losing major configuration control
- wizard output remains lossless relative to the scenario contract

### Phase 5: Product Presets

Goal:

- reduce friction for common high-value scenarios

Examples:

- large hybrid enterprise
- CMDB-heavy enterprise
- discovery-validation environment
- collaboration-heavy enterprise
- manufacturing enterprise at 10k-20k scale

Acceptance criteria:

- common advanced use cases can be reached through supported presets plus small edits

## Testing Strategy

### Invariants

- add dedicated invariant tests
- add regression cases for UPN collisions
- add deterministic repair tests where repair is allowed

### Contract fidelity

- add authored JSON -> resolved scenario -> authored JSON round-trip tests
- add tests proving `Applications`, `Cmdb`, and `ObservedData` survive authoring flows

### Cmdlet and wizard parity

- add integration coverage proving public PowerShell surfaces can express the same scenarios as direct object construction

### Dataset validation

- add a large-enterprise regression fixture that validates:
  - named company support
  - scale support
  - CMDB support
  - observed-data support
  - invariant cleanliness

## Risks if Left Unaddressed

- users will continue to perceive the shell surface as incomplete
- advanced generation scenarios will keep drifting toward internal-only code paths
- impossible identity states will reduce trust in the realism story
- scenario authoring will remain partly lossy
- support and onboarding costs will grow because “the real way” to get exact outcomes is not the same as the public product surface

## Immediate Recommended Next Steps

1. Treat duplicate UPNs as a blocking defect and fix them first.
2. Expand the public scenario envelope to include `Applications`, `Cmdb`, and `ObservedData`.
3. Add at least one supported PowerShell path for explicit company shaping at large-enterprise scale.
4. Extend the wizard to expose the new contract rather than leaving it behind.

## Context-Recovery Notes

This plan was produced after generating a large synthetic dataset for **Duckburg Industries** for DTEnvDiscovery import testing.

The important context cues were:

- the generated dataset included duplicate UPN warnings, which are not realistic because Active Directory and Entra enforce UPN uniqueness within their scopes
- the public PowerShell path did not cleanly expose enough scenario controls to produce the exact dataset shape needed
- a custom runner was therefore used to exercise the richer underlying generation model directly

The conclusion was not that the generator core is weak. It was that the **public control plane and invariant model need to catch up with the strength of the generator core**.
