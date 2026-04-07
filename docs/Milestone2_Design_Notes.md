# Milestone 2 Design Notes

## Why exporter v2 now
The project already has working generation, enrichment, lineage, and snapshot scaffolding. The missing piece is frictionless materialization for demos.

That makes exporter v2 the next highest-value phase.

## Data product model

The world should be exportable in three ways:

1. **Canonical world snapshot**
   - already covered by Phase 1 save/import contracts

2. **Operational demo bundle**
   - CSV/JSON collections used by dashboards, scripts, notebooks, and UI demos

3. **Graph/relationship bundle**
   - nodes and edges optimized for graph loaders and lineage demos

Milestone 2 focuses on items 2 and 3.

## Entity groups

### Organizational
- companies
- business units
- departments
- teams
- people
- offices

### Identity
- organizational units
- directory accounts
- directory groups
- directory group memberships

### Infrastructure
- managed devices
- server assets
- network assets
- telephony assets
- software packages
- device software installations
- server software installations

### Repository
- database repositories
- file share repositories
- collaboration sites
- repository access grants

### Cross-cutting
- anomalies
- session/export metadata

## Link table rules

Link tables should exist even when the corresponding relationship can be inferred from an entity table. This lowers friction for downstream consumers.

Examples:
- `person_manager_links`
- `team_membership_links`
- `account_person_links`
- `device_owner_links`
- `repository_access_links`

## Flattened views
These are optional and intentionally secondary. They are convenience artifacts, not the canonical output model.

Recommended flats:
- `people_directory_flat`
- `device_inventory_flat`
- `repository_access_flat`

## PowerShell behavior
`Export-SEEnterpriseWorld` should remain the only command that materializes files. All other commands should remain object-first and pipeline-first.

## Implementation sequence

1. add export contracts and request model
2. add descriptor-based entity/link providers
3. add CSV/JSON artifact writers
4. add manifest and summary builders
5. update cmdlet surface
6. add tests
7. add examples
