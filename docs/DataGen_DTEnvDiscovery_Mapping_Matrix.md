# DataGen to DTEnvDiscovery Mapping Matrix

## Executive Summary

From a product perspective, I agree with your instinct.

There are two different categories of DTEnvDiscovery-aligned content:

1. **Enterprise-reality primitives** that improve `SyntheticEnterprise` on their own merits, even if `DTEnvDiscovery` did not exist.
2. **DTEnvDiscovery- or demo-specific overlays** that are best treated as optional extensions.

The first category should generally be pulled into the core DataGen model, because it improves realism, export interoperability, and lab usefulness across multiple downstream consumers.

The second category is a good fit for plugins, because it is:

- product-specific
- workflow-specific
- often narrative/demo-oriented rather than part of the enterprise source-of-truth model

## Product Recommendation

### Recommended Core Additions

These should be treated as core DataGen realism work, not DTED-only work:

- explicit `IdentityStore` entities
- generalized `Container` hierarchy
- first-class `Policy` and `PolicySetting` entities
- richer observed/discovery-time relationship evidence such as:
  - local admin determination inputs
  - software install evidence
  - service/web-pool execution identities
  - active connection evidence
  - CMDB-style records / source-system records
  - ACLs for container objects for admin delegation/access
  - NTFS ACLs for shares to determine groups or users with access
- a scenario-level deviation profile so rich enterprise worlds can be generated either with or without realism-oriented mistakes and drift
  
Note: ACLs captured should only include non-default entries
Note: ACLs and similar permissions data should be modeled as evidence first and inferred access/admin edges second

### Why These Belong in Core

These concepts are not really “DTED features.” They are common enterprise constructs:

- organizations really do have identity stores
- organizations really do operate inside container hierarchies such as OUs, admin units, subscriptions, resource groups, VPCs, and management scopes
- organizations really do have policy surfaces that materially shape access and operations
- discovery tools often infer relationships from observational evidence rather than authoritative truth, and that distinction is useful outside DTED too

Adding these concepts would improve:

- realism for lab-population scenarios
- realism for detection/discovery testing
- export utility for multiple consumers
- the gap between “source of truth” and “observed truth”

### Recommended Plugin Candidates

These are good candidates for a DTED-specific plugin or plugin family:

- fake ingestion logs
- fake collection runs
- analyst/demo user activities
- report views, dashboard usage, or workflow telemetry
- synthetic “tool events” that only exist because DTEnvDiscovery exists
- DTED-native export envelope generation, if we choose to keep it as a downstream-specific integration instead of a first-party adapter

### Why These Belong in Plugins

These items are not universal enterprise data. They are:

- product-behavior artifacts
- demo-storytelling artifacts
- environment-specific workflow outputs

They are useful, but they should not force the core world model to become “DTED-shaped” in places where the enterprise itself would not naturally own that data.

## Core vs Plugin Decision Rule

Use this rule of thumb:

- **Core** if the concept would still make sense in an enterprise digital twin even if DTEnvDiscovery disappeared tomorrow.
- **Plugin** if the concept mostly exists because DTEnvDiscovery, its demo workflow, or a specific downstream consumer expects it.

## Mapping Matrix

Legend:

- `Native`: represented directly in DataGen today
- `Derived`: not first-class in DataGen, but can be mapped or synthesized with reasonable transformation logic
- `Gap`: not well represented today and should be added if we want strong fidelity

| DTEnvDiscovery Concept | DTED Usage | Current DataGen Source | Status | Mapping / Derivation Strategy | Recommended Action |
|---|---|---|---|---|---|
| `DTOrgConstruct` Company | legal/organizational graph root | `Company` | Native | Direct map from `Company` | Keep core |
| `DTOrgConstruct` Department | organizational membership target | `Department` | Native | Direct map from `Department` | Keep core |
| `DTOrgConstruct` Team | organizational membership target | `Team` | Native | Direct map from `Team` | Keep core |
| `DTOrgConstruct` Business Unit / Division | higher org grouping | `BusinessUnit` | Native | Direct map from `BusinessUnit`; synthesize DTED type as needed | Keep core |
| `DTPerson` | personnel graph | `Person` | Native | Direct map from `Person` | Keep core |
| `DTLocation` | office / worksite / site context | `Office` | Native | Direct map from `Office` | Keep core |
| `DTAccount` employee/user account | identity graph | `DirectoryAccount` | Native | Direct map | Keep core |
| `DTAccount` guest / external / contractor account | extended identity graph | `DirectoryAccount` with user type, external metadata, lifecycle metadata | Native | Direct map with type translation | Keep core |
| `DTGroup` | security / distribution / RBAC group graph | `DirectoryGroup` | Native | Direct map | Keep core |
| `MemberOf` person/account/group relationships | org and identity hierarchy | `GroupMemberships`, org references, manager references | Native | Map native membership records and derive org membership edges | Keep core |
| `ReportsTo` / `ManagerOf` | reporting chain | `Person.ManagerPersonId`, `DirectoryAccount.ManagerAccountId` | Native | Direct relationship derivation | Keep core |
| `WorksAt` | person/account to location | `Person.OfficeId`, `Office` | Native | Direct relationship derivation | Keep core |
| `DTDevice` workstation | endpoint graph | `ManagedDevice` | Native | Direct map | Keep core |
| `DTDevice` server | server node | `ServerAsset` | Native | Direct map to DTDevice with server subtype | Keep core |
| device installed applications | application-to-host evidence | `DeviceSoftwareInstallations`, `ServerSoftwareInstallations`, `SoftwarePackages` | Native | Direct map to `InstalledOn` / install evidence | Keep core |
| device admin relationships | local admin / privileged access | `EndpointAdministrativeAssignments`, `EndpointLocalGroupMembers` | Native | Direct map to `AdminOf` and supporting evidence | Keep core |
| device policy posture | compliance and baseline state | `EndpointPolicyBaselines` | Partial Native | Can map to DTED policy or device metadata, but not as first-class DT policy today | Promote to first-class core policy model |
| recent interactive user usage | endpoint usage evidence | no direct first-class equivalent | Gap | Could be synthesized from observed snapshots only weakly today | Add as core observed evidence |
| service/web-app-pool execution identity | app/account/device relationship evidence | not modeled as explicit evidence records | Gap | Could infer some service hosting, but not runtime identities | Add as core observed evidence |
| active device-to-device connections | network behavior evidence | not modeled as explicit connections | Gap | No direct equivalent | Add as core observed evidence |
| `DTApplication` business or enterprise app | application graph | `ApplicationRecord` | Native | Direct map | Keep core |
| application ownership | ownership / responsibility | `OwnerDepartmentId`, service owner team, business-process links | Native | Direct map and relationship derivation | Keep core |
| application-to-device hosting | deployment evidence | `ApplicationServiceHostings` to server/device-like hosts | Native | Direct map via service-hosting relationships | Keep core |
| `DTDataRepo` database | repo graph | `DatabaseRepository` | Native | Direct map | Keep core |
| `DTDataRepo` file share | repo graph | `FileShareRepository` | Native | Direct map | Keep core |
| `DTDataRepo` SharePoint / Teams / OneDrive style workspace | collaboration/data store graph | `CollaborationSite`, `CollaborationChannel`, `DocumentLibrary`, `SitePage`, `DocumentFolder` | Native / Partial | Map sites/libraries/channels to DT repo or companion nodes depending adapter design | Keep core |
| repository access users/admins | permissions model | `RepositoryAccessGrants` | Native | Direct map to `Uses` / `AdminOf` depending grant level | Keep core |
| active repo connections (`ConnectsTo`) | live usage / client evidence | no direct equivalent | Gap | No direct equivalent today | Add as core observed evidence |
| SQL principals / live repo admins | discovery-time evidence | repository grants approximate this, but not source-specific evidence | Partial | Can derive coarse access model from grants | Add richer observed evidence if desired |
| `DTIdentityStore` AD domain | identity-source node | company/domain/account/OU data only | Gap | Can synthesize from company domain + hybrid directory metadata | Add as first-class core entity |
| `DTIdentityStore` Entra tenant | identity-source node | `CloudTenant` plus account/provider metadata | Derived | Can synthesize from `CloudTenant` with provider `Microsoft` and account metadata | Add as first-class core entity |
| `ResidesIn` account/device to admin unit/container | hierarchical placement | `DirectoryOrganizationalUnit` covers AD OUs only | Partial | AD OUs map well; Entra admin units and cloud containers need synthesis today | Add generic container hierarchy to core |
| generic `DTContainer` OU | hierarchical identity container | `DirectoryOrganizationalUnit` | Native / Partial | Direct map for OU scenarios | Keep current + unify under generic container model |
| generic `DTContainer` Entra Administrative Unit | identity container | none explicit | Gap | Could synthesize from future identity-store/container layer | Add to core |
| generic `DTContainer` Azure Subscription / Resource Group / Management Group | cloud scope | no first-class equivalent | Gap | Some cloud tenants exist, but not management scopes | Add to core |
| generic `DTContainer` AWS VPC / AWS Account | cloud scope | no first-class equivalent | Gap | No current first-class model | Add to core if cloud realism is a goal |
| `DTPolicy` Group Policy | policy graph | no first-class GPO object | Gap | Endpoint policy baselines are not enough | Add first-class core policy model |
| `DTPolicy` Conditional Access | policy graph | `CrossTenantAccessPolicyRecord` partially overlaps but is narrower | Partial | Some fields overlap, but not a general CA policy object | Add first-class core policy model |
| `DTPolicy` Intune compliance/config/policy sets | policy graph | `EndpointPolicyBaseline` partially overlaps | Partial | Device baseline data exists, but not reusable policy objects/assignments | Add first-class core policy model |
| `DTPolicy` Azure Policy | policy graph | none explicit | Gap | No direct equivalent | Add if cloud governance realism matters |
| `DTPolicySetting` | detailed policy configuration units | none explicit | Gap | Could derive from future policy model, not current world | Add first-class core policy-setting model |
| `AppliesTo` policy targeting groups/identity stores/containers | governance targeting edges | none explicit in unified form | Gap | Cross-tenant policies and endpoint baselines cover fragments only | Add with core policy model |
| access-control evidence for OUs, shares, SharePoint/Teams, Entra, and Azure scopes | delegated access/admin inference inputs | no unified first-class evidence model today | Gap | Model explicit allow/deny ACE-style evidence and let downstream tools infer `AdminOf`, `Uses`, and delegation edges | Add as first-class core observed evidence |
| CMDB record / source-system evidence | discovery provenance | first-class `ConfigurationItem`, `ConfigurationItemRelationship`, `CmdbSourceRecord`, `CmdbSourceLink`, and `CmdbSourceRelationship` plus `ObservedEntitySnapshot` | Native | Core now supports an optional three-layer model of canonical configuration items, source-facing CMDB/discovery/catalog records, and realistic drift between them; `ObservedEntitySnapshot` remains a separate generic observed-truth layer | Keep extending CI/source coverage and scenario-level deviation controls without replacing the broader canonical world graph |
| observed source-system snapshots | discovery-vs-truth testing | `ObservedEntitySnapshot` | Native | Direct map or adapter staging layer | Keep core |
| ingestion summary / run logs | tool telemetry | none, intentionally | Plugin | Should be generated by DTED-specific plugin if needed | Plugin candidate |
| analyst/demo-user actions | demo telemetry | none, intentionally | Plugin | Synthetic workflow overlay only | Plugin candidate |
| DTED-native environment export envelope | import convenience | normalized entity/link export only | Derived | Adapter can translate normalized export into DTED envelope | Prefer first-party adapter or companion package if DTED is a strategic consumer; otherwise plugin |

## Recommended Near-Term Product Backlog

### Priority 1: Core Realism and Interop

1. Add first-class `IdentityStore` entities.
2. Add a generalized `Container` hierarchy model.
3. Add first-class `Policy` and `PolicySetting` entities plus targeting links.
4. Add a richer observed-evidence layer for:
   - active connections
   - service/runtime execution identities
   - local-admin determination evidence
   - software install evidence provenance
   - CMDB/source-record style observations
   - ACL determination evidence
5. Add a scenario-level deviation profile so “clean baseline” and “realistically flawed” worlds use the same object model with different error/drift intensity.

### Priority 2: Integration Convenience

6. Add an export adapter profile targeted at DTEnvDiscovery shape.
7. Add a lab-population-oriented export profile optimized for AD/Entra/M365/endpoint provisioning scripts.

### Priority 3: Optional Ecosystem Extensions

8. Create a DTED-specific plugin for:
   - fake ingestion runs
   - fake operator activity
   - fake report/dashboard interaction
   - DTED-native demo telemetry

## Product Manager Recommendation

If the objective is to make DataGen a stronger enterprise digital twin product overall, then closing the `IdentityStore` / `Container` / `Policy` / `Observed evidence` gap is the right move and should be treated as **core product work**.

If the objective is to make DTEnvDiscovery demos richer, then fake ingestion logs and demo-user activity absolutely make sense, but they should be framed as a **DTED plugin layer** rather than a core DataGen responsibility.

If DTEnvDiscovery is a strategic first-party consumer, then its native import envelope is best treated as a first-party export adapter or companion package before treating it as a plugin concern.

Likewise, “realistic deviations” should be configurable separately from “enterprise richness.” A future `DeviationProfile` seam would let DataGen generate detailed but intentionally clean environments for baseline labs and regression testing, while still defaulting to realistically imperfect worlds for discovery and security exercises.

That split keeps the platform honest:

- DataGen owns the synthetic enterprise reality.
- Plugins own downstream product-specific overlays and demo narratives.
