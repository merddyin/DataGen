# SyntheticEnterprise Milestone 2 — Exporter v2 Implementation

## Objective
Make the synthetic enterprise world immediately usable in demo workflows by materializing stable, predictable export bundles.

This milestone adds:

- normalized CSV bundle export
- normalized JSON bundle export
- link-table export
- summary/manifest output
- export profile support
- deterministic artifact naming
- pipeline-friendly `Export-SEEnterpriseWorld -PassThru`

## Design goals

1. **Stable output contracts**
   - predictable filenames
   - repeatable column ordering
   - deterministic bundle structure

2. **Object-first PowerShell behavior**
   - exporter accepts `GenerationResult` from the pipeline
   - file creation only occurs when export is explicitly invoked
   - `-PassThru` returns the updated `GenerationResult`

3. **Consumer-friendly bundles**
   - one folder per export run
   - normalized entity files
   - dedicated link tables for graph and analytics tools
   - machine-readable manifest

4. **Traceability**
   - every export updates session/export history
   - every artifact is tracked in the manifest with row counts and hashes
   - export profile and serialization settings are recorded

## Proposed output layout

```text
out/
  csv/
    companies.csv
    business_units.csv
    departments.csv
    teams.csv
    people.csv
    offices.csv
    directory_accounts.csv
    directory_groups.csv
    directory_group_memberships.csv
    managed_devices.csv
    server_assets.csv
    network_assets.csv
    telephony_assets.csv
    software_packages.csv
    device_software_installations.csv
    server_software_installations.csv
    database_repositories.csv
    file_share_repositories.csv
    collaboration_sites.csv
    repository_access_grants.csv
    anomalies.csv
    person_manager_links.csv
    team_membership_links.csv
    account_person_links.csv
    device_owner_links.csv
    repository_access_links.csv
    export_summary.json
    manifest.json
  json/
    world.json
    entities/
      people.json
      accounts.json
      devices.json
      repositories.json
      anomalies.json
    links/
      person_manager_links.json
      team_membership_links.json
      account_person_links.json
      device_owner_links.json
      repository_access_links.json
    export_summary.json
    manifest.json
```

## Export profiles

### `Normalized`
Default. One entity per file, explicit link tables.

### `Flattened`
Adds convenience files for demo tools that want broad denormalized views:
- `people_directory_flat.csv`
- `device_inventory_flat.csv`
- `repository_access_flat.csv`

### `Graph`
Optimized for graph import:
- `nodes_people.csv`
- `nodes_accounts.csv`
- `nodes_devices.csv`
- `edges_manages.csv`
- `edges_member_of.csv`
- `edges_owns.csv`
- `edges_has_access.csv`

## Contracts to add

- `ExportProfileKind`
- `ExportSerializationFormat`
- `ExportArtifactKind`
- `ExportRequest`
- `ExportBundleOptions`
- `ExportArtifactDescriptor`
- `ExportManifestV2`
- `EntityTableDescriptor`
- `LinkTableDescriptor`
- `ExportSummary`

## Service boundaries

- `IWorldExportCoordinator`
- `IEntityTableProvider`
- `ILinkTableProvider`
- `IArtifactWriter`
- `IExportManifestBuilder`
- `IExportSummaryBuilder`
- `IExportPathResolver`

## Cmdlet surface

```powershell
Export-SEEnterpriseWorld `
  -InputObject $world `
  -Format Csv `
  -OutputPath .\out `
  -Profile Normalized `
  -IncludeManifest `
  -IncludeSummary `
  -PassThru
```

Recommended parameter additions:

- `-Profile` (`Normalized`, `Flattened`, `Graph`)
- `-IncludeManifest`
- `-IncludeSummary`
- `-ArtifactPrefix`
- `-Overwrite`
- `-PassThru`

## Ordering and determinism rules

### File naming
Use invariant lowercase snake_case names.

### Column ordering
Use explicit descriptor-based ordering; never reflection order.

### Row ordering
Sort by primary identifier before writing.

### Hashing
Hash final artifact bytes using SHA256 after serialization.

## Integration with Phase 1

Milestone 2 consumes the world/session manifest introduced earlier and appends an `ExportExecutionRecord` with:

- export id
- profile
- format
- output path
- timestamp
- artifact count
- manifest path
- summary path

## Acceptance criteria

- A single cmdlet call can emit a full CSV bundle.
- A single cmdlet call can emit a full JSON bundle.
- Manifest contains one entry per artifact with row counts and hashes.
- Exported filenames are stable across repeated runs with identical input.
- `-PassThru` returns the world with export history appended.
- Tests cover manifest generation, artifact determinism, and link table integrity.
