# DataGen Realism Remediation Plan

Date: 2026-04-23

## Purpose

This plan captures the DataGen-side source realism work identified during the
2026-04-23 Duckburg review. It is intentionally scoped to issues that should be
fixed in generation, export shape, or scenario composition rather than in the
DTED bridge.

## Guiding principles

- Fix source realism in DataGen before compensating in DTED.
- Sample across multiple scenarios and seeds while implementing changes.
- Prefer coherent cross-layer structure over isolated fake-value improvements.
- Use representative external references for policy realism, not blind imports.

## Workstreams

### 1. Person and identity realism

Scope:

- reduce display-name collisions to a realistic rate
- improve name pool coverage and first/last-name pairing realism
- keep account identifiers unique while making display values believable
- align machine account display names with workstation/server hostnames

Current issues addressed:

- repeated person display names at unrealistic rates
- machine account display names not matching machine names

Acceptance targets:

- max duplicate full-name counts become rare outliers rather than common patterns
- large flagship scenarios no longer surface dense name duplication clusters
- computer account display names match the corresponding device/server name

### 2. Account and device identity evidence

Scope:

- generate/export stronger account activity fields such as last-logon style evidence
- preserve ownership cues needed to associate user-like accounts to people
- ensure device-associated accounts and login evidence are coherent

Current issues addressed:

- missing last-login style evidence in downstream projections
- incomplete ownership signals for person-like accounts

Acceptance targets:

- account exports include usable activity timestamps where DataGen can support them
- person-assigned accounts and machine accounts are distinguishable and consistent

### 3. Application and identity store realism

Scope:

- improve internal application classification inputs
- strengthen SaaS / workstation / server application distinctions
- clean up identity store naming and Entra tenant/domain naming
- ensure Duckburg and other flagship scenarios have realistic identity-store surfaces

Current issues addressed:

- incorrect Entra domain format
- identity store names still prefixed with company names
- application metadata too weak for downstream classification

Acceptance targets:

- Entra tenant domains use realistic `onmicrosoft.com` naming
- identity stores no longer carry noisy company prefixes
- application records expose enough metadata for stable DTED-side typing

### 4. Repository and collaboration realism

Scope:

- reduce template overuse in libraries and folders
- align site/library item counts and sizes with child relationships
- remove unrealistic repeated library constructs such as oversized `Active Initiatives`
- keep repository relationships consistent with generated structure

Current issues addressed:

- implausible library proliferation and count/size mismatches
- inconsistent library/site relationship surfaces

Acceptance targets:

- flagship repository samples show believable library counts and sizes
- parent/child repository totals no longer contradict one another materially

### 5. Policy and policy-setting realism

Scope:

- deepen representative enterprise policy coverage
- improve setting names and policy path generation
- align Group Policy, Intune, Conditional Access, and identity store context
- use `Windows11PolicySettings25H2.xlsx` as a representative reference for setting families and path realism

Current issues addressed:

- policy corpus still undersized relative to enterprise scale
- invalid or placeholder-like policy path values
- weak differentiation between policy families

Acceptance targets:

- flagship scenarios produce a materially richer policy corpus
- policy-setting paths look like real GPO / registry / management-plane paths
- settings no longer surface obviously invalid placeholder paths

### 6. Duckburg scenario enhancement

Scope:

- adjust the Duckburg scenario to include at least one acquired company
- ensure the resulting world produces credible acquisition-related structures

Current issues addressed:

- no explicit acquired-company scenario in the flagship Duckburg demo

Acceptance targets:

- Duckburg includes at least one acquired company with coherent downstream effects
- resulting org and identity structures remain believable after generation

## Execution order

1. Person and identity realism
2. Account and device identity evidence
3. Application and identity store realism
4. Repository and collaboration realism
5. Policy and policy-setting realism
6. Duckburg scenario enhancement

## Cross-repo note

Several user-facing issues surfaced in DTED are not owned by DataGen. Those are
tracked separately in the DTED bridge remediation plan and should follow the
DataGen work where they depend on richer source data.
