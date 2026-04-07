# v5 Identity Slice

Referenced files
- synthetic_enterprise_identity_slice.zip
- synthetic_enterprise_module_scaffold_v5/SyntheticEnterprise_Identity_Slice.md
- synthetic_enterprise_module_scaffold_v5/examples/regional_manufacturer.scenario.json

What this adds
- `DirectoryOrganizationalUnit`
- `DirectoryAccount`
- `DirectoryGroup`
- `DirectoryGroupMembership`
- `IdentityAnomaly`

What now works
- OU hierarchy generation
- user account generation
- service account generation
- shared mailbox generation
- privileged admin account generation
- security/distribution/M365-style group generation
- group membership generation
- first identity anomaly injection pass

Supported anomaly profiles
- `PrivilegedNoMfa`
- `DisabledUsersInGroups`
- `StaleServiceAccounts`

This gives the scaffold a real identity layer tied back to:
- people
- departments
- titles
- reporting structure
- company domains
- office-aligned geography

Caveat
- still intentionally v1
- not yet modeling nested groups, external guests, dynamic groups, device objects, or policy linkage

Next slice should be the infrastructure layer: workstations, servers, software inventory, ownership, and directory-connected assets.
