# DataGen Realism Hardening Plan

## Purpose

This plan defines the DataGen-first realism hardening work needed to make generated environments feel structurally and operationally believable before additional DTED-specific shaping.

The focus is source realism, not downstream masking. The intended effect is that improved DataGen output naturally improves DTED imports, exports, demos, and validation scenarios.

## Guiding Principles

- Higher-order business structure should drive lower-layer technical structure.
- Generated names should prefer contextual realism over generic numbering.
- Major entity families should agree with each other across org, identity, infra, repos, apps, and policy layers.
- Counts should scale with company size and operating model.
- DTED bridge fixes should not be used to compensate for weak source modeling unless the problem is truly consumer-specific.

## Scope

This hardening plan covers:

- organization structure realism
- location-aware staffing and resource placement
- identity and access realism
- policy and management-plane realism
- application and infrastructure realism
- repository and collaboration realism
- quality gates and review loops

This plan does not cover:

- consumer-specific DTED schema additions unless a true data gap is discovered
- UI/website roadmap work
- plugin SDK work except where realism diagnostics need shared hooks

## Workstreams

### 1. Organization Structure Realism

Goal:

- make business units, departments, teams, offices, and reporting lines look like a plausible enterprise instead of generated permutations

Key work:

- replace generic uniqueness suffixing with contextual naming across business units, departments, teams, sites, and shares
- rebalance department and team composition so functions align with real org structures
- ensure specialized teams roll up under plausible parent departments
- improve manager span and reporting-chain realism
- tighten external organization generation so partner, contractor, and acquired-company constructs are explicit and purposeful

Success indicators:

- team and department names no longer drift into repetitive numbered variants
- generated org charts read as coherent business structures
- external organizations map to real business relationships instead of vendor/manufacturer leakage

Primary code areas:

- `src/SyntheticEnterprise.Core/Generation/Organization/`
- `src/SyntheticEnterprise.Core/Generation/Applications/`
- `src/SyntheticEnterprise.Core/Generation/Identity/`

### 2. Geographic and Distribution Realism

Goal:

- make people, devices, servers, and support functions distribute credibly across offices and regions

Key work:

- model headquarters, regional hubs, manufacturing sites, and satellite offices differently
- bias IT, platform, and data-center-heavy roles toward major sites
- ensure each office has plausible local support footprint
- distribute outsourced and contractor roles realistically
- drive server role placement from site size, app affinity, and geography

Success indicators:

- location mix and staffing density reflect office purpose
- infra placement aligns with business and operational needs
- naming and role distribution make sense by region and site

Primary code areas:

- `src/SyntheticEnterprise.Core/Generation/Geography/`
- `src/SyntheticEnterprise.Core/Generation/Organization/`
- `src/SyntheticEnterprise.Core/Generation/Infrastructure/`

### 3. Identity, Groups, and Access Realism

Goal:

- make identity stores, accounts, groups, delegated admin, local admin membership, and service identities look like a real hybrid enterprise

Key work:

- refine identity-store naming and identifiers
- ensure at least one partner, one contracted org, and one acquired org are present in relevant scenarios
- broaden realistic security and distribution groups while tying them directly to org structure and apps
- ensure local admin and other endpoint group membership surfaces emphasize domain-backed principals
- generate Windows services and scheduled tasks that run under domain/service accounts
- add trust-style identity relationships for acquisition scenarios where appropriate

Success indicators:

- group structure mirrors org, app, repo, and admin reality
- service-account surfaces exist for systems that should have them
- identity-provider relationships reflect hybrid, partner, and acquisition use cases

Primary code areas:

- `src/SyntheticEnterprise.Core/Generation/Identity/`
- `src/SyntheticEnterprise.Core/Generation/Infrastructure/`
- `src/SyntheticEnterprise.Contracts/Models/`

### 4. Policy, OU, and Management-Plane Realism

Goal:

- bring GPO, OU, Intune/MDM, and policy-setting depth up to believable enterprise scale

Key work:

- expand OU structure to reflect policy boundaries, app/server segmentation, role-based admin boundaries, and geographic variation
- grow GPO count and setting count from baseline-only to enterprise scale
- add workload-specific server policies, workstation-type policies, kiosk/IT/admin policies, and location-specific variations
- introduce Intune/endpoint-management policy surfaces and align them with GPO intent where appropriate
- add Conditional Access policy generation and richer cross-tenant policy modeling

Success indicators:

- policy counts and setting counts scale with org size
- OU structure has obvious policy/application/admin rationale
- policy targeting and exceptions look like something an enterprise would actually manage

Primary code areas:

- `src/SyntheticEnterprise.Core/Generation/Identity/`
- `src/SyntheticEnterprise.Core/Generation/Infrastructure/`
- `src/SyntheticEnterprise.Contracts/Models/PolicyModels.cs`

### 5. Application and Infrastructure Realism

Goal:

- separate real applications from agents/features, and make compute/service deployment align with business and identity reality

Key work:

- tighten application catalog classification so built-ins, platform features, and agents are not emitted as first-class business apps
- improve cloud-tenant naming and identity-store overlap rules
- refine application-services modeling so it matches current downstream needs or split it into clearer constructs
- improve server naming with site/location indicators where appropriate
- place app services by data center, region, and business use

Success indicators:

- applications list feels like an app portfolio rather than installed software inventory
- server/app/service placement matches business geography and app criticality
- tenant and identity surfaces are distinct and believable

Primary code areas:

- `src/SyntheticEnterprise.Core/Generation/Applications/`
- `src/SyntheticEnterprise.Core/Generation/Infrastructure/`
- `src/SyntheticEnterprise.Core/Generation/Identity/`

### 6. Repository, Collaboration, and CMDB Realism

Goal:

- make shares, folders, libraries, channels, sites, and CMDB artifacts read like enterprise operational data instead of templates

Key work:

- replace generic share naming with realistic share path conventions
- generate meaningful top-level folder structures
- tie site and library structure to departments, teams, and business processes
- stop surfacing non-DTED-relevant collaboration sub-artifacts where they do not belong
- improve CMDB source record naming and CI source-record alignment
- ensure business processes do not leak into CI surfaces unless intentionally modeled

Success indicators:

- repositories feel owned, named, and structured by business function
- CMDB surfaces resemble discovery/import records, not user home-path artifacts
- collaboration artifacts align with org and application structure

Primary code areas:

- `src/SyntheticEnterprise.Core/Generation/Repository/`
- `src/SyntheticEnterprise.Core/Generation/Cmdb/`
- `src/SyntheticEnterprise.Exporting/`

### 7. Realism Diagnostics and Acceptance Loops

Goal:

- make realism measurable and harder to regress

Key work:

- add targeted realism assertions for each major generator family
- expand sample-based acceptance checks for large flagship scenarios like Duckburg
- add review-oriented artifact extraction for representative values by entity family
- define issue-driven acceptance criteria for repeated realism complaints

Success indicators:

- realism regressions are caught by tests before they reach demo datasets
- flagship scenario regeneration becomes a repeatable review gate

Primary code areas:

- `tests/SyntheticEnterprise.Core.Tests/`
- `tests/SyntheticEnterprise.Exporting.Tests/`
- `docs/`

## Execution Order

1. Organization Structure Realism
2. Geographic and Distribution Realism
3. Identity, Groups, and Access Realism
4. Policy, OU, and Management-Plane Realism
5. Application and Infrastructure Realism
6. Repository, Collaboration, and CMDB Realism
7. Realism Diagnostics and Acceptance Loops

## Dependency Notes

- geographic distribution depends on improved org structure
- identity/access should consume improved org and geography signals
- policy/OU realism depends on org, identity, and infra structure
- application/infra realism depends on org, geography, and policy segmentation
- repository/collaboration/CMDB realism depends on org/app/identity improvements
- diagnostics should grow in parallel, but final acceptance closes after the other workstreams land

## Acceptance Criteria

- flagship scenarios no longer show obvious numbered-name realism failures across org, share, folder, and collaboration artifacts
- policy counts and policy settings scale plausibly for large enterprise scenarios
- generated apps exclude obvious platform features and agents from first-class app inventory where not intended
- identity stores, external orgs, service identities, and trust-like relationships are plausible for the scenario
- location, staffing, and server placement reflect office purpose and enterprise scale
- representative generated outputs can be reviewed field-by-field without immediately exposing synthetic shortcuts
