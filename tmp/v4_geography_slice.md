# v4 Geography Slice

Referenced files
- synthetic_enterprise_geography_slice.zip
- synthetic_enterprise_module_scaffold_v4/SyntheticEnterprise_Geography_Slice.md
- synthetic_enterprise_module_scaffold_v4/catalogs/cities.csv

What this adds
- `Office` contract
- `Person.OfficeId`
- typed `World.Offices`
- scenario controls for:
  - `OfficeCount`
  - `AddressMode`
  - `IncludeGeocodes`
- working `BasicGeographyGenerator`
- `cities.csv` catalog for office placement

What the geography layer now does
- creates offices per company
- uses real city/state/postal/timezone values from catalog data
- supports hybrid fake addresses with real locality data
- can include lat/long for map-aware demos
- assigns people to offices, preferring country-aligned offices when available

This means the scaffold now has a real path from:
- scenario
- catalogs
- organization generation
- geography generation

Next up should be the identity slice: accounts, OUs, groups, memberships, and a first set of directory anomalies.
