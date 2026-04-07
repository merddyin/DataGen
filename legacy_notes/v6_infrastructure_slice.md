# v6 Infrastructure Slice

Referenced files
- synthetic_enterprise_infrastructure_slice.zip
- synthetic_enterprise_module_scaffold_v6/SyntheticEnterprise_Infrastructure_Slice.md
- synthetic_enterprise_module_scaffold_v6/catalogs/software_catalog.csv

What this adds
- `SoftwarePackage`
- `ManagedDevice`
- `ServerAsset`
- `NetworkAsset`
- `TelephonyAsset`
- `DeviceSoftwareInstallation`
- `ServerSoftwareInstallation`
- `InfrastructureAnomaly`

What now works
- workstation generation with assigned users
- server inventory generation
- network inventory generation
- telephony inventory generation
- software catalog loading
- device software installation generation
- server software installation generation
- first infrastructure anomaly injection pass

Supported infrastructure anomaly profiles
- `NonCompliantEndpoints`
- `UnownedServers`
- `InactiveDevices`

This gives the scaffold a concrete infrastructure layer tied to:
- people
- offices
- accounts
- teams
- software inventory

Next slice should be the repository layer so databases, file shares, and collaboration sites can attach to owners, groups, departments, and access models.
