# v7 Repository Slice

Referenced files
- synthetic_enterprise_repository_slice.zip
- synthetic_enterprise_module_scaffold_v7/SyntheticEnterprise_Repository_Slice.md
- synthetic_enterprise_module_scaffold_v7/catalogs/repository_patterns.csv

What this adds
- `DatabaseRepository`
- `FileShareRepository`
- `CollaborationSite`
- `RepositoryAccessGrant`
- `RepositoryAnomaly`

What now works
- database inventory generation
- file share inventory generation
- collaboration site generation
- repository-to-group access grant generation
- first repository anomaly injection pass

Supported repository anomaly profiles
- `OpenShares`
- `OrphanedSites`
- `SensitiveDatabaseBroadAccess`

At this point, the scaffold now has working slices for:
- organization
- geography
- identity
- infrastructure
- repositories

That gives you a usable synthetic enterprise backbone for demo dataset generation.

Next best step is the exporter slice so the world can be emitted as:
- CSV bundles
- JSON bundles
- link tables
- manifest/summary output
