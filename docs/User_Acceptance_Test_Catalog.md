# User Acceptance Test Catalog

## Purpose

This document defines user acceptance tests for `SyntheticEnterprise` as it exists today.

The goal is to verify that the product is usable for two major operator outcomes:

1. Producing realistic enterprise data sets that can drive **lab-population workflows** for environments such as Active Directory, Entra ID, endpoint/device estates, application estates, and collaboration/data repositories.
2. Producing structured enterprise data that can drive **downstream discovery/graph workflows**, including seeding or adapting data for `DTEnvDiscovery` at `E:\source\DTEnvDiscovery`.

## Product Boundary

`SyntheticEnterprise` currently generates a rich enterprise world model, scenario authoring flow, export artifacts, regeneration behavior, anomaly injection, and plugin-driven extensions.

It does **not** currently provision Active Directory, Entra ID, M365, Intune, SQL Server, or DTEnvDiscovery directly.

Because of that, the acceptance tests below focus on:

- correctness and usability of the **generated model**
- completeness and quality of the **exported artifacts**
- suitability of those artifacts for **downstream provisioning or ingestion adapters**

Where a translation/provisioning step is still required, the test calls that out explicitly.

## Test Approach

The tests are organized by operator journey:

- scenario authoring and UX
- world generation fidelity
- lab-population readiness
- export and interoperability
- DTEnvDiscovery handoff readiness
- regeneration and operational safety
- plugin and extension safety

Each test includes:

- `ID`
- `Goal`
- `Primary Persona`
- `Preconditions`
- `Steps`
- `Expected Result`
- `Notes / Risks`

## Recommended Test Environments

### Environment A: Local Authoring Workstation

- Windows workstation with PowerShell 7+
- Built `SyntheticEnterprise.PowerShell` module
- Access to packaged seeded catalog database

### Environment B: Lab Validation Environment

- Disposable AD / Entra / workstation / server simulation environment
- Optional downstream provisioning scripts or IaC wrappers

### Environment C: DTEnvDiscovery Validation Environment

- `DTEnvDiscovery` source tree at `E:\source\DTEnvDiscovery`
- SQL Server available for DTEnvDiscovery ingestion tests
- Ability to run `Deploy-DTSqlDatabase`, `Import-DTEnvironment`, and `Invoke-DTDataIngestion`

## Global Acceptance Criteria

The product should be considered acceptable for its current phase if:

- non-expert operators can author and refine scenarios without editing raw JSON unless they choose to
- generated worlds are structurally rich enough to represent identity, infrastructure, applications, repositories, collaboration, and external ecosystem details
- exported normalized artifacts are complete enough to support downstream lab-provisioning and graph-ingestion adapters
- merge/regeneration does not cause uncontrolled duplication of identities, infrastructure, repositories, or shared application/ecosystem entities
- plugin execution remains bounded and does not destabilize the core generation path

## UAT-01: Seeded Catalog Availability

- `Goal`: Confirm the module works with the packaged seeded SQLite catalog and does not require raw catalog folders at runtime.
- `Primary Persona`: Platform engineer / release engineer
- `Preconditions`:
  - Module is built/published into a test folder.
- `Steps`:
  1. Import the module from a clean output folder.
  2. Run `New-SEEnterpriseWorld` without explicitly passing `-CatalogRootPath`.
  3. Generate a small scenario from a bundled example.
- `Expected Result`:
  - Generation succeeds using the packaged `catalogs.sqlite`.
  - No runtime dependency on raw catalog CSV/JSON files is required.
- `Notes / Risks`:
  - This is a release-blocking test because packaging is part of the product promise.

## UAT-02: Scenario Template Discovery

- `Goal`: Verify that operators can discover starting points without knowing internal JSON structure.
- `Primary Persona`: Security engineer / lab engineer
- `Preconditions`:
  - Module is imported.
- `Steps`:
  1. Run `Get-SEScenarioTemplate`.
  2. Review available templates, overlays, plugin hints, and defaults.
  3. Create a scenario with `New-SEScenarioFromTemplate`.
- `Expected Result`:
  - Templates are understandable and materially different.
  - Returned objects contain enough metadata to choose a plausible starting point.
- `Notes / Risks`:
  - This is a strong predictor of first-run usability.

## UAT-03: Terminal Wizard Greenfield Flow

- `Goal`: Confirm that a new user can build a scenario interactively from the terminal.
- `Primary Persona`: Consultant / lab builder
- `Preconditions`:
  - Module is imported.
- `Steps`:
  1. Run `New-SEScenarioWizard`.
  2. Select a template and overlays.
  3. Enter business, identity, infrastructure, and repository settings.
  4. Review validation output.
  5. Save the scenario.
- `Expected Result`:
  - A usable `ScenarioEnvelope` is produced without manual JSON editing.
  - Validation issues and hints are understandable.
  - Saved output can be passed directly to `New-SEEnterpriseWorld`.
- `Notes / Risks`:
  - This is a key UX test for non-developer operators.

## UAT-04: Terminal Wizard Edit Existing Scenario

- `Goal`: Confirm the wizard is useful for scenario maintenance, not just initial creation.
- `Primary Persona`: Lab maintainer
- `Preconditions`:
  - Existing scenario JSON file is available.
- `Steps`:
  1. Run `New-SEScenarioWizard -ScenarioPath <existing scenario>`.
  2. Confirm the wizard enters edit mode.
  3. Change one section only, such as Identity or Plugins.
  4. Save and exit.
- `Expected Result`:
  - Existing values are preloaded.
  - Template reselection is skipped.
  - Only the changed section is modified.
- `Notes / Risks`:
  - This reduces operational friction for repeated lab refreshes.

## UAT-05: Targeted Wizard Maintenance Modes

- `Goal`: Verify that the wizard supports surgical maintenance workflows.
- `Primary Persona`: Operations engineer
- `Preconditions`:
  - Existing scenario JSON file is available.
- `Steps`:
  1. Run `New-SEScenarioWizard -ScenarioPath <path> -StartSection Identity -SkipSectionReview`.
  2. Run `New-SEScenarioWizard -ScenarioPath <path> -ReviewOnly`.
- `Expected Result`:
  - `-StartSection` jumps directly into the requested area.
  - `-SkipSectionReview` avoids the full edit loop.
  - `-ReviewOnly` validates and previews without forcing re-entry into section prompts.
- `Notes / Risks`:
  - This is especially important for large scenarios used repeatedly in labs.

## UAT-06: Scenario Validation Quality

- `Goal`: Ensure the scenario validator catches meaningful issues before generation.
- `Primary Persona`: Product owner / QA
- `Preconditions`:
  - Scenario with intentionally incomplete or conflicting fields.
- `Steps`:
  1. Run `Test-SEScenario` on a malformed scenario.
  2. Run `Resolve-SEScenario`.
  3. Re-run `Test-SEScenario`.
- `Expected Result`:
  - Unknown, missing, or invalid settings are surfaced clearly.
  - Plugin hints/defaults are visible when relevant.
  - Resolved scenarios are more complete and easier to generate.
- `Notes / Risks`:
  - Validation quality strongly affects trust in the tool.

## UAT-07: Baseline World Generation

- `Goal`: Confirm that the product can generate a realistic enterprise world from a scenario.
- `Primary Persona`: Platform engineer
- `Preconditions`:
  - Valid scenario file such as `regional-manufacturer.json`.
- `Steps`:
  1. Run `New-SEEnterpriseWorld -ScenarioPath <scenario>`.
  2. Pipe to `Get-SEWorldSummary`.
- `Expected Result`:
  - World generation succeeds without manual intervention.
  - Summary shows non-zero counts across identity, infrastructure, repositories, and applications.
- `Notes / Risks`:
  - This is the baseline smoke test for every release.

## UAT-08: Realism Coverage for Enterprise Estate

- `Goal`: Confirm the generated world contains the major enterprise domains expected today.
- `Primary Persona`: Enterprise architect
- `Preconditions`:
  - Mid-size or large enterprise scenario.
- `Steps`:
  1. Generate a world.
  2. Inspect the output summary and exported artifacts.
  3. Verify presence of:
     - companies and offices
     - organizational units
     - people, accounts, and groups
     - devices and servers
     - applications, application services, and cloud tenants
     - databases, file shares, collaboration sites/channels/tabs/libraries/pages/folders
     - external organizations, business processes, observed snapshots
- `Expected Result`:
  - The world spans the enterprise landscape rather than a narrow data slice.
- `Notes / Risks`:
  - This is a core product-value test.

## UAT-09: Identity Fidelity for AD / Entra Lab Use

- `Goal`: Validate identity data is rich enough to drive downstream AD/Entra provisioning.
- `Primary Persona`: Identity engineer
- `Preconditions`:
  - Scenario with hybrid directory, M365 groups, external workforce, and B2B guests enabled.
- `Steps`:
  1. Generate a world.
  2. Export normalized JSON.
  3. Inspect `entities/accounts`, `entities/groups`, `entities/organizational_units`, and `links/group_memberships`.
- `Expected Result`:
  - Data includes enough information to drive provisioning logic for:
    - employee, service, privileged, contractor, guest-like accounts
    - nested and tiered groups
    - OU structure
    - B2B guest lifecycle and cross-tenant governance metadata
  - Password artifacts are handled safely and are masked by default in exports.
- `Notes / Risks`:
  - Current product creates synthetic identity data; a separate provisioning adapter is still needed to stamp it into AD/Entra.

## UAT-10: Endpoint and Server Fidelity for Lab Use

- `Goal`: Validate infrastructure data is rich enough to drive downstream server/workstation population.
- `Primary Persona`: Endpoint / infrastructure engineer
- `Preconditions`:
  - Scenario with servers and workstations enabled.
- `Steps`:
  1. Generate a world.
  2. Export normalized JSON.
  3. Inspect `entities/devices`, `entities/servers`, `entities/software_packages`, `entities/endpoint_policy_baselines`, `entities/endpoint_local_group_members`, and installation link tables.
- `Expected Result`:
  - Data includes endpoint identities, software inventory, local admin posture, policy baselines, and administrative assignments.
  - The model is sufficient to drive downstream image/build scripts or endpoint simulation tooling.
- `Notes / Risks`:
  - This is about provisioning readiness, not direct provisioning inside DataGen itself.

## UAT-11: Repository and Collaboration Fidelity for Lab Use

- `Goal`: Validate repository/collaboration data is rich enough to populate M365/SharePoint/Teams/file-share style labs.
- `Primary Persona`: Collaboration engineer
- `Preconditions`:
  - Scenario with databases, file shares, and collaboration sites enabled.
- `Steps`:
  1. Generate a world.
  2. Export normalized JSON.
  3. Inspect:
     - `entities/databases`
     - `entities/file_shares`
     - `entities/collaboration_sites`
     - `entities/collaboration_channels`
     - `entities/collaboration_channel_tabs`
     - `entities/document_libraries`
     - `entities/site_pages`
     - `entities/document_folders`
     - `links/repository_access_grants`
     - `links/collaboration_channel_tab_targets`
     - `links/site_page_library_links`
     - `links/document_folder_lineage`
- `Expected Result`:
  - Collaboration and repository topology is rich enough to drive realistic workspace population logic.
- `Notes / Risks`:
  - This is one of the strongest differentiators of the current tool.

## UAT-12: Application and Service Topology Fidelity

- `Goal`: Validate that the generated application estate is useful for architecture, attack-path, and operational testing.
- `Primary Persona`: Application owner / security architect
- `Preconditions`:
  - Mid-size or large enterprise scenario.
- `Steps`:
  1. Generate a world.
  2. Export normalized JSON.
  3. Inspect:
     - `entities/applications`
     - `entities/application_services`
     - `entities/cloud_tenants`
     - `entities/business_processes`
     - `links/application_dependencies`
     - `links/application_service_dependencies`
     - `links/application_service_hostings`
     - `links/application_business_process_links`
     - `links/application_tenant_links`
     - `links/application_repository_links`
- `Expected Result`:
  - The estate includes business apps, supporting services, hosting relationships, cloud tenancy, dependencies, and process alignment.
- `Notes / Risks`:
  - This is especially important for cyber-range and enterprise discovery use cases.

## UAT-13: External Ecosystem and B2B Fidelity

- `Goal`: Validate that external organizations and cross-tenant interactions are rich enough for realistic partner/vendor scenarios.
- `Primary Persona`: IAM architect / third-party risk analyst
- `Preconditions`:
  - Scenario with external workforce and B2B guests enabled.
- `Steps`:
  1. Generate a world.
  2. Export normalized JSON.
  3. Inspect:
     - `entities/external_organizations`
     - `entities/cross_tenant_access_policies`
     - `entities/cross_tenant_access_events`
     - `links/application_counterparty_links`
     - `links/business_process_counterparty_links`
- `Expected Result`:
  - Vendors, customers, partners, guest access, entitlement/access-review signals, and cross-tenant policies/events are represented.
- `Notes / Risks`:
  - This is valuable for Entra B2B and supply-chain simulation exercises.

## UAT-14: Observed-vs-Ground-Truth Usefulness

- `Goal`: Validate that the product can represent discovery drift and source-system perspectives, not just idealized truth.
- `Primary Persona`: Detection engineer / discovery engineer
- `Preconditions`:
  - Standard scenario with observed layer enabled by default generation.
- `Steps`:
  1. Generate a world.
  2. Export normalized JSON.
  3. Inspect `entities/observed_entity_snapshots`.
- `Expected Result`:
  - Observed snapshots exist for identity, endpoint, application, repository, and cross-tenant surfaces.
  - The artifacts are rich enough to emulate data collected from tools such as Entra, Intune, SharePoint admin, CMDB, or M365 admin portals.
- `Notes / Risks`:
  - This is especially useful when testing tools that rely on observed inventory rather than authoritative source-of-truth systems.

## UAT-15: Anomaly Injection Usability

- `Goal`: Confirm anomaly profiles make the world measurably messier in useful ways.
- `Primary Persona`: SOC engineer / purple-team operator
- `Preconditions`:
  - Scenario with anomaly profile entries.
- `Steps`:
  1. Generate a clean-ish baseline world.
  2. Generate a second world with anomaly profile enabled.
  3. Compare summary and exported artifacts.
- `Expected Result`:
  - The anomaly-enabled world contains visible identity, infrastructure, or repository deviations that are operationally meaningful.
- `Notes / Risks`:
  - Acceptance should focus on useful signal, not just random noise.

## UAT-16: Determinism Expectations

- `Goal`: Confirm repeatability is good enough for regression workflows.
- `Primary Persona`: QA automation engineer
- `Preconditions`:
  - Fixed scenario and fixed seed.
- `Steps`:
  1. Run generation twice with the same scenario and same `-Seed`.
  2. Compare counts, major IDs, and non-credential export artifacts.
- `Expected Result`:
  - Structural outputs are materially stable.
  - Credential material may differ where cryptographic password generation is intentionally nondeterministic.
- `Notes / Risks`:
  - This test should explicitly ignore password value equality.

## UAT-17: Save / Import / Export Round Trip

- `Goal`: Confirm generated worlds can move through the product lifecycle cleanly.
- `Primary Persona`: Automation engineer
- `Preconditions`:
  - Generated world object.
- `Steps`:
  1. Generate a world.
  2. Save it with `Save-SEEnterpriseWorld`.
  3. Reload it with `Import-SEEnterpriseWorld`.
  4. Export normalized JSON or CSV.
- `Expected Result`:
  - Save/import/export succeeds without structural corruption.
- `Notes / Risks`:
  - Important for CI pipelines and artifact archiving.

## UAT-18: Merge Regeneration Safety

- `Goal`: Confirm regeneration does not inflate the graph or destabilize stable identities.
- `Primary Persona`: Lab operator / platform maintainer
- `Preconditions`:
  - Existing generated world.
- `Steps`:
  1. Apply identity-layer merge regeneration.
  2. Apply infrastructure-layer merge regeneration.
  3. Apply repository-layer merge regeneration.
  4. Re-export and compare entity/link counts.
- `Expected Result`:
  - Stable artifacts are preserved.
  - No uncontrolled duplication of people, accounts, devices, servers, repositories, or shared entities occurs.
- `Notes / Risks`:
  - This is one of the most important operational hardening tests.

## UAT-19: Ownership and Shared-Entity Reconciliation

- `Goal`: Confirm shared application/ecosystem entities reconcile correctly after generation/regeneration.
- `Primary Persona`: Product QA lead
- `Preconditions`:
  - World with substantial applications, cloud tenants, processes, and external organizations.
- `Steps`:
  1. Generate a world.
  2. Trigger applicable regeneration paths.
  3. Re-export normalized artifacts.
- `Expected Result`:
  - Duplicate applications, services, business processes, cloud tenants, and external organizations are reconciled onto canonical IDs.
- `Notes / Risks`:
  - This protects downstream graph tools from false cardinality inflation.

## UAT-20: Plugin Host Safety

- `Goal`: Confirm plugin execution remains bounded and safe for production-adjacent use.
- `Primary Persona`: Platform owner
- `Preconditions`:
  - At least one script plugin and one assembly plugin package.
- `Steps`:
  1. Inspect plugins.
  2. Test package trust rules and provenance behavior.
  3. Run generation with plugin diagnostics and payload limits enabled.
- `Expected Result`:
  - Plugin execution obeys time, payload, and diagnostic limits.
  - Disallowed or tampered assembly plugins are rejected.
  - Generation remains stable when plugins emit warnings or diagnostics.
- `Notes / Risks`:
  - This is secondary to core generation, but still important for extension safety.

## UAT-21: Lab-Population Data Pack Readiness

- `Goal`: Validate that DataGen can act as a source system for downstream lab-population tooling.
- `Primary Persona`: Lab automation engineer
- `Preconditions`:
  - Generated world exported in normalized JSON.
  - Downstream provisioning scripts or adapters available.
- `Steps`:
  1. Export normalized JSON with masked credentials.
  2. Feed exported entities/links into a downstream provisioning adapter for:
     - OU creation
     - account and group creation
     - server/workstation population
     - application/service catalog population
     - Teams/SharePoint/file-share simulation
  3. Review downstream logs and resulting lab objects.
- `Expected Result`:
  - DataGen exports are sufficient to drive the adapter without major schema gaps.
  - Any missing fields are limited to adapter-specific provisioning concerns, not missing enterprise semantics.
- `Notes / Risks`:
  - Current product boundary: DataGen is the synthetic source of truth, not the provisioning engine itself.

## UAT-22: DTEnvDiscovery Structural Compatibility Assessment

- `Goal`: Validate that DataGen exports contain enough structure to be transformed into DTEnvDiscovery’s node/edge ingestion format.
- `Primary Persona`: Discovery platform engineer
- `Preconditions`:
  - `DTEnvDiscovery` source available at `E:\source\DTEnvDiscovery`
  - Normalized DataGen export available
- `Steps`:
  1. Review DTEnvDiscovery import expectations:
     - `Import-DTEnvironment`
     - `Invoke-DTDataIngestion`
  2. Map DataGen exports to DT node families such as:
     - people/accounts/groups
     - devices/servers
     - applications
     - data repositories
     - identity stores
     - org constructs / locations
     - policies where applicable
  3. Identify required translation rules for nodes and relationships.
- `Expected Result`:
  - A clear mapping exists from DataGen normalized exports into DTEnvDiscovery importable concepts.
  - No major enterprise domain is missing for graph seeding.
- `Notes / Risks`:
  - DataGen does **not** currently emit the native `Export-DTEnvironment` envelope format, so an adapter/transform remains part of the workflow.

## UAT-23: DTEnvDiscovery Seed Adapter Proof Test

- `Goal`: Prove that a practical DTEnvDiscovery seed path is feasible from DataGen output.
- `Primary Persona`: Product owner / integration engineer
- `Preconditions`:
  - Normalized DataGen export available
  - DTEnvDiscovery SQL database deployed
  - Lightweight translation script or manual mapping harness available
- `Steps`:
  1. Transform a representative subset of DataGen entities and links into DTEnvDiscovery node/relationship objects or envelope format.
  2. Run `Invoke-DTDataIngestion`.
  3. Query the resulting graph with `Get-DTGraphNode`.
- `Expected Result`:
  - DTEnvDiscovery contains a coherent environment slice populated from DataGen-derived data.
  - Representative queries return expected companies, people, accounts, groups, systems, applications, and repositories.
- `Notes / Risks`:
  - This is the strongest integration-value acceptance test for your discovery platform.
  - If this fails because of envelope/adapter gaps rather than missing DataGen data, that should be treated as an integration backlog item rather than a core-generation failure.

## UAT-24: Operator Documentation Usability

- `Goal`: Confirm that an operator can discover how to use the tool without oral transfer from the development team.
- `Primary Persona`: New team member
- `Preconditions`:
  - Clean workstation with repo checked out.
- `Steps`:
  1. Follow examples and scenario flows from repo docs/examples.
  2. Build a scenario, generate a world, export it, and inspect results.
- `Expected Result`:
  - A competent technical operator can complete the flow without reverse-engineering internals.
- `Notes / Risks`:
  - If operators repeatedly need to inspect source code to succeed, the UX/documentation bar has not been met.

## Exit Criteria for Current Phase

The current phase should be considered acceptable if:

- UAT-01 through UAT-08 pass consistently.
- UAT-09 through UAT-15 pass with only adapter-layer gaps, not missing-data gaps.
- UAT-16 through UAT-20 pass consistently in CI or repeatable local runs.
- UAT-21 through UAT-23 show that DataGen is a viable synthetic source for lab-population and DTEnvDiscovery seeding, even if a transformation layer is still required.

## Suggested Next Product Actions Based on UAT Outcomes

If the primary target is **lab population**, the next likely product move is:

- a first-party export profile specifically optimized for AD / Entra / endpoint / M365 provisioning adapters

If the primary target is **DTEnvDiscovery seeding**, the next likely product move is:

- a first-party `DTEnvDiscovery` export adapter that emits either:
  - DT-native node/relationship objects
  - or the native `Export-DTEnvironment` JSON envelope format expected by `Import-DTEnvironment`

If the primary target is **general enterprise realism**, the next likely product move is:

- additional golden scenarios and golden exports used as release-candidate acceptance fixtures
