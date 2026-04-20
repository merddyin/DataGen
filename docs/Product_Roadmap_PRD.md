# DataGen Product Roadmap PRD

## Purpose

This document defines the next-stage product roadmap for DataGen as a broader synthetic operating environment platform, not only as a DTED-oriented synthetic enterprise dataset generator.

It is intended to guide release planning, architectural sequencing, and product tradeoff decisions across core generation, catalogs, simulation, SDK, and documentation surfaces.

## Product framing

### Current position

DataGen already generates coherent synthetic enterprise worlds with identity, infrastructure, repository, application, CMDB, observed-data, and export surfaces.

That makes DataGen strong at generating a believable point-in-time enterprise dataset.

### Target position

DataGen should evolve into a synthetic operating environment platform:

- enterprise structure
- domain-specific operational datasets
- temporal change over time
- realism and quality scoring
- extensible domain packs and SDKs

The shift is from:

- "generate a synthetic enterprise"

to:

- "generate a synthetic enterprise that behaves like a real operating environment"

## Problem statement

Today, DataGen is strongest when a user needs:

- a realistic enterprise snapshot
- normalized exports for downstream consumers
- scenario-driven world generation

The main gaps limiting broader product expansion are:

- limited first-class domain packs beyond core enterprise entities
- mostly point-in-time output rather than temporal simulation
- scenarios that are technically flexible but not yet fully productized as market-facing archetypes
- plugin architecture that is promising but not yet elevated into a pack-oriented SDK experience
- realism work that is strong but still mostly validated through engineering effort rather than productized quality reporting

## Product goals

1. Expand DataGen beyond single-use synthetic enterprise generation into a multi-domain operating environment generator.
2. Preserve the current architecture principle that DataGen generates source synthetic data, while consumer-specific shaping remains external.
3. Make major new value areas composable so users can add domain capability without needing custom forks.
4. Increase the usefulness of generated output for demos, labs, validation, analytics, simulation, and testing workflows.
5. Productize realism and dataset quality so it becomes inspectable, reportable, and improvable.

## Non-goals

1. Build consumer-specific import adapters into DataGen core.
2. Replace the current scenario and world-generation model with a completely new platform.
3. Build a heavy end-user UI before domain packs and temporal simulation are mature.
4. Chase endless low-value realism polish instead of expanding product surface area.

## Primary personas

### Product and solution teams

Need believable datasets to validate workflows, demos, integrations, and product experiences.

### Detection, security, and operations teams

Need synthetic but coherent environments with alerts, incidents, tickets, relationships, and change over time.

### Platform and integration engineers

Need stable normalized output, predictable schemas, and reusable pack/SDK patterns.

### Internal innovation and field engineering teams

Need rapid scenario composition for different industries, customer profiles, and demonstration narratives.

## Strategic themes

### 1. Domain expansion

Add first-class operating domains on top of the enterprise substrate.

### 2. Temporal realism

Represent how environments evolve instead of only what they look like at a single instant.

### 3. Productized scenarios

Package complexity into recognizable archetypes, overlays, and personas.

### 4. Extensibility

Turn the plugin model into a deliberate SDK and pack ecosystem.

### 5. Quality visibility

Make realism, consistency, and exportability measurable.

## Roadmap summary

### Release 0.4.x: Domain Packs Foundation

Primary outcome:

- DataGen can generate a core enterprise plus one or more operating domains as first-class packs.

### Release 0.5.x: Temporal Simulation Foundation

Primary outcome:

- DataGen can generate event history and time-sliced snapshots, not just a single state.

### Release 0.6.x: Scenario Productization

Primary outcome:

- DataGen becomes easier to adopt through archetypes, overlays, and guided composition.

### Parallel track: SDK and Quality

Primary outcome:

- pack creation becomes safer and easier, while realism becomes measurable and reportable.

## Release 0.4.x: Domain Packs Foundation

### Objective

Expand DataGen into adjacent operating domains without breaking its current architecture.

### Why now

- fastest path to broader product utility
- lowest disruption to the current world model
- aligned with the original generator-package intent from the legacy project

### Key epics

#### Epic 0.4.1: Pack model and pack lifecycle

Deliverables:

- pack registration model
- pack manifest and metadata contract
- pack-level generation hooks
- pack dependency declaration model
- pack-level tests and sample scenarios

Success criteria:

- a pack can be enabled without forking core world generation
- packs can declare required entities, catalogs, and generation phases

#### Epic 0.4.2: ITSM pack

Deliverables:

- incidents
- service requests
- change records
- approvals
- assignment groups
- SLA fields and timelines

Core value:

- makes DataGen relevant to ticketing, service operations, and workflow validation use cases

#### Epic 0.4.3: SecOps pack

Deliverables:

- alerts
- cases
- detections
- investigation artifacts
- remediation actions
- alert-to-asset, user, and application relationships

Core value:

- materially expands DataGen into security operations and detection engineering scenarios

#### Epic 0.4.4: BusinessOps pack

Deliverables:

- vendors
- contracts
- procurement records
- invoices
- asset ownership and lifecycle records

Core value:

- broadens use cases for ERP-adjacent and governance/compliance scenarios

### Example user stories

1. As a product demo owner, I want to generate an enterprise plus an ITSM pack so I can show ticket-to-asset-to-user workflows.
2. As a security engineer, I want to generate a SecOps pack so I can test alert and case handling against realistic infrastructure and identity data.
3. As a field engineer, I want to combine packs in a single scenario so I can tailor datasets to different customer conversations.

### Dependencies

- stable entity registration patterns
- export profile extension points
- scenario syntax for enabling packs

### Risks

- over-coupling packs to current internal generation order
- adding pack concepts without sufficient schema discipline
- premature consumer-specific logic leaking into core

### Metrics

- number of first-party packs shipped
- percent of walkthroughs that use packs
- time to create a new pack from scaffold
- number of downstream validation/demo flows enabled by packs

## Release 0.5.x: Temporal Simulation Foundation

### Objective

Add a time dimension to the generated world so DataGen can produce synthetic operating history.

### Why now

- this creates the biggest strategic differentiation
- many validation and analytics use cases require change over time
- domain packs become much more valuable when they can evolve

### Key epics

#### Epic 0.5.1: Timeline and event model

Deliverables:

- event schema
- event typing model
- effective date and validity windows
- snapshot generation by date

#### Epic 0.5.2: Workforce and identity drift

Deliverables:

- hires
- transfers
- manager changes
- privilege growth and cleanup
- terminations

#### Epic 0.5.3: Infrastructure and software change

Deliverables:

- device turnover
- server role changes
- software rollout waves
- policy changes
- patch and baseline drift

#### Epic 0.5.4: Operational history for packs

Deliverables:

- ticket creation and resolution history
- alert bursts and case progression
- change approvals and reversals
- repository/document activity history

### Example user stories

1. As an analyst, I want snapshots at two points in time so I can test delta ingestion and drift detection.
2. As a security team, I want alert and remediation history so I can validate case workflows instead of just importing static artifacts.
3. As an integration team, I want event streams and point-in-time exports so I can validate both sync and backfill workflows.

### Dependencies

- stable pack schemas
- versioned export semantics
- deterministic timeline generation from a seed

### Risks

- event volume exploding too quickly
- unclear distinction between world state and event history
- increased complexity in test fixtures and determinism

### Metrics

- number of event types supported
- ability to reproduce the same timeline from the same seed
- number of snapshots generated from one scenario
- downstream validation scenarios enabled by temporal output

## Release 0.6.x: Scenario Productization

### Objective

Make DataGen easier to understand, select, and adopt by packaging its flexibility into market-friendly scenario products.

### Why now

- this is how non-expert users will understand the platform
- domain packs and simulation become easier to consume when wrapped in strong defaults

### Key epics

#### Epic 0.6.1: Industry archetypes

Deliverables:

- manufacturer
- SaaS company
- healthcare provider
- public sector agency
- retail and distribution organization

#### Epic 0.6.2: Operating overlays

Deliverables:

- fast growth
- post-merger integration
- compliance-heavy
- under-governed
- modernization / zero-trust rollout

#### Epic 0.6.3: Guided scenario composition

Deliverables:

- guided composer inputs
- recommended defaults
- pack recommendations per archetype
- export recommendations per use case

#### Epic 0.6.4: Persona-driven presets

Deliverables:

- security lab preset
- IT operations preset
- compliance/audit preset
- engineering collaboration preset

### Example user stories

1. As a field seller, I want to choose "regional manufacturer + post-merger + ITSM" and get a believable dataset without hand-tuning.
2. As a PM, I want a security preset so I can quickly produce a demo environment tailored to alert and asset workflows.
3. As an internal engineer, I want scenario presets that reflect common use cases so less product knowledge is required to get useful output.

### Metrics

- time from zero to first useful scenario
- preset adoption rates
- reduction in low-level scenario parameter edits
- number of walkthroughs aligned to archetypes

## Parallel Track A: Pack SDK

### Objective

Turn the current plugin story into a first-class development surface for internal teams and ecosystem builders.

### Deliverables

- pack scaffold command
- manifest schema
- sample test harness
- sample catalogs
- compatibility contract and versioning guidance
- pack publishing and validation workflow

### Why it matters

- scales capability creation
- reduces pressure on core team
- makes DataGen a platform in practice, not just in architecture diagrams

## Parallel Track B: Quality and Realism Diagnostics

### Objective

Productize realism and exportability validation.

### Deliverables

- dataset quality report
- realism scorecard
- exportability checks
- consistency and completeness checks
- international fidelity warnings
- pack-level quality checks

### Why it matters

- shortens iteration loops
- makes quality visible to users and contributors
- creates trust in generated output

## Suggested sequencing

### Phase 1

- pack model
- ITSM pack
- SecOps pack
- quality report foundation

### Phase 2

- event model
- identity and infrastructure change simulation
- time-sliced export support

### Phase 3

- archetypes and overlays
- guided scenario composition
- persona presets

### Phase 4

- public SDK hardening
- pack publishing workflow
- expanded quality scoring and certification

## Architectural implications

### Core generation

Needs clearer phase boundaries and pack insertion points.

### Contracts and exporting

Need more formal extensibility for new entity families, relationships, and time-based output.

### Catalogs

Need stronger pack-scoped catalog sourcing, packaging, and validation.

### Scenario model

Needs syntax for packs, overlays, time windows, and preset composition.

### Testing

Needs pack-level, timeline-level, and quality-report-level harnesses.

## Open questions

1. Should pack enablement be additive only, or can packs disable or override parts of the base world?
2. Should time-based generation emit a single event log, multiple snapshots, or both by default?
3. How much of the pack SDK should be public before first-party pack patterns stabilize?
4. Should quality scoring be purely descriptive at first, or should it gate packaging/export in stricter modes?

## Recommendation

The next strategic investment should be:

1. domain packs
2. temporal simulation
3. scenario productization

That sequence broadens addressable value, deepens defensibility, and stays aligned with both the current architecture and the broader mission implied by the legacy DataGen work.
