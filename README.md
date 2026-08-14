# DataGen

DataGen is a synthetic enterprise data generation platform. It procedurally builds realistic enterprise datasets that teams can use for labs, validation, demos, exports, discovery-tool testing, and downstream integration work.

## Changelog

### v0.11.0

- added opt-in representative multi-management observations by operating-system and endpoint cohort, while retaining the legacy fixed per-company sample when the feature is not requested
- normalized missing policy-setting source timestamps from explicit neighboring bounds and the supplied generation time; normalized exports carry the completed values without consulting a consumer runtime clock
- continuously gated portable Windows publisher-metadata preservation, with mandatory NTFS/ReFS cross-filesystem release evidence from the prepared workstation; current assertions cover owner, group, restrictive DACL, file attributes, and creation/access/write timestamps, while SACL and mandatory integrity-label handling remains best-effort implementation behavior outside that evidence
- hardened external plugin boundaries with bounded, authenticated manifest and catalog reads, bounded host output, and clear rejection diagnostics for malformed or excessive input
- stage approved assembly-plugin packages from validated handles, prove dependency-inclusive package provenance, and fail closed when required cleanup cannot be completed
- retained the existing trust model: in-process PowerShell remains trusted code and approved assembly plugins run under the host operating-system identity; these controls validate inputs and artifact provenance but are not an operating-system sandbox

### v0.10.1

- corrected representative server-management observations so hosted-compute evidence is attached only to servers whose generated hosting facts identify them as cloud-hosted
- retained the server observation budget when a generated company has fewer hosted or on-premises servers than the requested percentage, while preserving the actual hosting category and provider of every selected server
- made hosted-server selection deterministic and provider-diverse where the generated population permits it, without inventing providers or changing the provider-neutral observation contract
- ensured out-of-band guest deployment support is emitted only for a real hosted server and remains unavailable for on-premises servers

### v0.10.0

- added provider-neutral management-observation history for representative endpoint-management facts, with explicit `Current` and `Historical` lifecycle states rather than inferring age from health or check-in fields
- added `is_current` and `superseded_by_observation_id` so a historical record identifies its governing current observation without encoding a downstream provider, tenant, or product model
- separated `infrastructure.representativeManagementHistoryObservationCount` from the current-observation count; the history budget is additive, bounded per company, and `0` disables historical rows
- extended the normalized `endpoint_management_observations` export with `lifecycle_state`, `is_current`, and `superseded_by_observation_id`; consumers can retain their current-only behavior by filtering `is_current = true`
- made history chronology deterministic for a fixed scenario, seed, and generated time: historical observations precede their current successor and retain an earlier last check-in
- preserved compatibility for existing consumers: public defaults still describe current observations, the new normalized columns are additive, and adapters should treat unknown or missing lifecycle fields conservatively while they adopt history-aware processing

### v0.9.4

- corrected opt-in representative relationship history so `Application` references resolve to real same-company application records and `InstalledOn` evidence follows generated application-service hosting to a server with software inventory
- added deterministic active and removed `InstalledOn` history without requiring an owner, while emitting active and removed `Owns` history only when a real person in the application's owning department exists
- preserved scenario boundaries: disabled representative observations and an explicit observation count of zero now produce no representative management or relationship-history facts
- retained the product-neutral public contracts and normalized export schema; consumers can continue translating generic enterprise facts without DataGen carrying downstream identifiers or logic
- documented the current website dependency disposition: the Docusaurus `image-size@2.0.2` advisory remains open because no compatible upstream fix exists, while the supported build guard and read-only CI controls provide a bounded acceptance through 2026-09-30; see [docs/security/dependency-dispositions.md](docs/security/dependency-dispositions.md)
- made catalog artifacts reproducible across copied checkouts: source records now retain ordered logical root identifiers rather than machine-specific paths, and a caller may supply an explicit UTC build timestamp or `SOURCE_DATE_EPOCH`
- made missing catalog build-time provenance explicit rather than publishing a fabricated timestamp; the release workflow supplies one timestamp for all release catalog work

### v0.9.3

- replaced process-salted generation choices with stable hashing where those choices contribute to a seeded world, so the same supported inputs do not vary by process
- made snapshot and normalized-export provenance portable, with canonical cross-platform artifact ordering and newline behavior and one deterministic export-manifest identity
- added trusted-operator, unsigned deterministic-generation evidence tooling that issues two parent contracts, captures separate-run provenance, and writes a receipt only when the canonical payloads match
- documented the boundary: declared sensitive inputs are redacted before evidence is serialized or hashed, so they are excluded from the equality proof and intentionally generated credential material can still differ

### v0.9.2

- refreshed the documentation toolchain lockfile to patched transitive versions after repository security scanning identified critical, high, moderate, and low npm advisories in the Docusaurus build and development dependency graph
- preserved the existing Docusaurus and site behavior while removing all findings reported by the current npm audit

### v0.9.1

- updated the SQLite provider stack to remove the vulnerable native SQLite package from DataGen's resolved dependency graph while preserving the supported Windows, Linux, and macOS provider bundle

### v0.9.0

- added an opt-in, provider-neutral endpoint-management observation contract that models registration, control capability, check-in, and hosted-compute facts without embedding any downstream product or scenario vocabulary
- added normalized `endpoint_management_observations` and `relationship_history_observations` tables so consumers can export those generic facts through the public normalized profile
- made repeatable world, snapshot, and export timestamps explicit public PowerShell parameters, allowing callers to reproduce release-grade datasets deterministically
- preserved the existing default generation path: management observations are additive, configurable scenario data rather than a required deployment-specific overlay

### v0.8.4

- added source/target environment-role metadata across identity stores, OUs, containers, policy records, policy settings, and policy target links so exports can distinguish current-state evidence from modeled target-state controls
- added a target Active Directory / GPO slice with workstation, server, and privileged-access target baselines for downstream policy-parity and migration validation scenarios
- improved normalized export coverage for environment roles and policy target links, and tightened policy-setting path parity when registry-backed settings do not carry an explicit policy path
- expanded the website walkthroughs from generation-only recipes into practical AD, Entra, hybrid, and repeatable-run lab workflows with native PowerShell/Graph cmdlet patterns, validation steps, and cleanup guidance

### v0.8.3

- added an identity scenario option to include or omit generated AD/tenant environment defaults for lab-population workflows
- hardened Active Directory realism with default containers, built-in groups, default accounts, Domain Controllers OU placement, and Entra Connect sync evidence
- corrected user-focused password-never-expires defaults while preserving intentional service, shared mailbox, machine, and privileged-account deviations
- regenerated the Duckburg ingestion freeze from the updated generator output and refreshed AD lab guidance to avoid collisions with existing domains

### v0.8.2

- corrected AD realism so physical workstations and servers no longer carry direct OU placement; OU residency now remains on the directory object while the physical endpoint stays tied to location and its machine-account relationship
- improved OU realism by placing hybrid user and machine accounts into the location-aware OU branches that already existed, so location OUs are no longer mostly empty scaffolding
- expanded Active Directory ACL delegation evidence across workstation, server, user, group, service-account, and admin-account OUs so generated environments show more believable delegated administration patterns instead of only a thin policy-adjacent surface

### v0.8.1

- corrected repository realism so modern collaboration-heavy enterprises no longer emit one top-level file share per user home/profile path
- replaced the inflated personal-share model with a small set of realistic hidden roots such as `users$` / `profiles$` plus only limited owner-specific exception shares
- regenerated the Duckburg DTED package from the improved source contract, reducing actual file shares from an unrealistic `22k+` shape to a believable modern footprint

### v0.8.0

- added first-class Active Directory site, site-link, subnet, and IP-allocation realism so generated hybrid environments now include credible topology surfaces beyond OU structure alone
- hardened CMDB realism with more believable criticality spread across infrastructure, application, platform, data, collaboration, and software configuration items
- improved flagship repository, collaboration, and access-group realism so generated environments rely more clearly on group-centric resource access and less on synthetic naming artifacts
- refreshed the Duckburg DTED package from the updated source contract, including topology, plugin-record, CMDB, and repository realism improvements

### v0.7.0

- hardened identity and access realism around device accounts, shared resources, group-centric access, and OU-aware account repair semantics
- improved application and repository access evidence so major enterprise apps and shared resources more clearly flow through realistic governing groups
- eliminated remaining flagship naming artifacts such as duplicate `sAMAccountName` values, synthetic mailbox/access suffixes, and weak team/resource labels in Duckburg
- broadened realism validation and regenerated the Duckburg DTED bundle from the updated source contract

### v0.6.0

- hardened flagship realism across organization structure, reporting lines, team naming, policy scope evidence, CMDB evidence, and Duckburg scenario composition
- added richer DTED-facing export evidence, including typed policy-setting source and behavior fields plus CMDB matching and recovery metadata such as `fqdn`, `unc_path`, `rto_hours`, and `rpo_hours`
- improved bridge-readiness for downstream consumers by aligning account lifecycle/state evidence and non-AD identity-store association inputs without baking DTED-specific inference into DataGen itself
- regenerated the Duckburg DTED demo package with the updated realism, policy, container, plugin-record, and CMDB surfaces

### v0.5.1

- removed the vulnerable transitive `uuid` path from the website toolchain by vendoring a patched `sockjs` copy that uses Node's built-in `crypto.randomUUID()`
- refreshed the website lockfile so `npm audit` is clean again without waiting on an upstream Docusaurus or webpack-dev-server release
- verified both `docusaurus build` and `docusaurus start` still work with the patched docs dependency tree

### v0.5.0

- fixed large-scenario person display-name collisions so flagship datasets no longer emit unrealistic repeated identity clusters
- added stronger account and device evidence, including exported account lifecycle timestamps and explicit application classification fields
- improved identity store realism with cleaner AD, Entra, and Okta naming/domain surfaces
- expanded policy realism to richer enterprise-scale policy families, path metadata, and identity-store scope evidence
- added acquired-company scenario support for Duckburg and related flagship scenarios
- tightened repository and collaboration realism so site and library metrics align with generated child content

### v0.4.4

- corrected the release tag lineage so the GitHub release workflow runs from the fixed flagship acceptance test revision
- preserves the `v0.4.3` portability and release-test fixes, but publishes them under a clean new release tag

### v0.4.3

- fixed the flagship realism acceptance test so release builds no longer depend on a local `artifacts\duckburg-subset.scenario.json` file
- tightened the repo portability validator so it no longer self-matches on its own detection pattern during CI and release runs

### v0.4.2

- added a repo portability validator and optional pre-push hook to catch machine-specific absolute paths before they break CI or releases
- updated the realism review defaults to use repo-stable scenarios instead of local artifact paths
- removed remaining local path defaults from the catalog build script and related docs

### v0.4.1

- replaced non-cryptographic machine-account password generation with cryptographically secure randomness
- added explicit read-only GitHub Actions workflow permissions so CI and release automation satisfy current security policy

### v0.4.0

- added first-class bundled domain packs for ITSM, SecOps, and BusinessOps, plus scenario-native pack enablement
- added temporal simulation foundations with timeline events, drift history, and normalized temporal export artifacts
- productized scenario authoring with archetypes, persona presets, smarter overlays, and an archetype-first wizard flow
- expanded end-to-end realism for organization structure, geography, identity, groups, policies, repositories, CMDB data, applications, and infrastructure
- added structured quality reporting, scored validation outputs, realism review automation, and CI quality artifacts
- tightened external-organization modeling so vendor metadata is no longer treated as a business relationship by default

### v0.3.0

- improved end-to-end realism for people, offices, applications, repositories, and architecture objects
- added curated country-specific name catalogs for the United States, United Kingdom, Canada, Australia, and New Zealand
- tightened international office locality, phone, and address generation, with focused upgrades for the UK, Canada, and Mexico
- made repository, collaboration, and application URLs more exportable and domain-consistent
- added first-class normalized export coverage for network assets and richer office address fields
- refreshed the Duckburg Industries DTED demo bundle with the newer realism and export improvements

## What DataGen does

DataGen is designed to generate believable enterprise structure without hand-authoring every user, group, device, application, repository, policy, or CMDB record.

Current product capabilities include:

- scenario-first world generation with archetypes, persona presets, overlays, JSON, and a terminal wizard
- identity, infrastructure, repository, application, policy, access-evidence, observed-data, and CMDB generation
- temporal simulation with change events and snapshot-oriented export surfaces
- hard identity invariants so duplicate user principal names are blocked instead of emitted as "realistic" flaws
- configurable realism through deviation profiles such as `Clean`, `Realistic`, and `Aggressive`
- normalized export and quality validation surfaces for downstream tooling and CI
- a plugin model for extending the synthetic dataset safely
- bundled first-party domain packs for ITSM, SecOps, and BusinessOps using the native scenario `packs` shape

## What DataGen is not

DataGen’s responsibility is to procedurally generate synthetic enterprise data.

That means:

- DataGen plugins may extend the generated dataset or add realism overlays
- DataGen plugins should not translate output into consumer-specific import contracts
- bridges, adapters, and import shapers for downstream systems belong outside the DataGen plugin ecosystem

## Common use cases

- populate Active Directory and Entra-focused labs
- create broad enterprise validation environments
- generate CMDB-rich and discovery-oriented datasets
- validate repository and collaboration tooling
- export normalized data for downstream consumers
- extend worlds with synthetic plugin-driven overlays

## Getting started

### Install from PowerShell Gallery

For normal module use, install the published package from PowerShell Gallery:

```powershell
Install-PSResource SyntheticEnterprise.PowerShell -Repository PSGallery
Import-Module SyntheticEnterprise.PowerShell
```

The Gallery package includes the seeded runtime catalog at `catalogs\catalogs.sqlite` inside the module. You do not need to download the separate `catalogs.sqlite` GitHub release asset for standard generation commands.

`New-SEEnterpriseWorld` loads the bundled catalog automatically when you omit `-CatalogRootPath`:

```powershell
$scenario = New-SEScenarioFromArchetype -Archetype RegionalManufacturer | Resolve-SEScenario
$world = New-SEEnterpriseWorld -Scenario $scenario -Seed 4242
```

Use `-CatalogRootPath` only when you want to override the bundled catalog with a custom catalog directory or SQLite database.

### Build from source

If you do not already have a local seeded catalog database, generate it first:

```powershell
.\scripts\build-catalog-artifact.ps1 -InstallToCatalogRoot
```

That command writes the canonical build output to `artifacts\catalog\catalogs.sqlite` and installs a local working copy to `catalogs\catalogs.sqlite` for source builds.

### Catalog provenance

Catalog source records use stable logical identifiers such as `catalog-root-001`, not local filesystem paths, so the same catalog inputs can produce byte-identical artifacts from different checkout locations. A catalog records `built_at_utc` only when the producer explicitly supplies a timestamp. `.\scripts\build-catalog-artifact.ps1 -BuildTimestampUtc 2026-08-09T15:30:00Z` passes an ISO-8601 UTC value through to the catalog tool; build orchestration can instead set `SOURCE_DATE_EPOCH` to whole Unix seconds. When neither is provided, the field is deliberately empty rather than pretending a build happened at an arbitrary time.

The separate `catalogs.sqlite` GitHub release asset is provided for inspection, custom catalog workflows, and direct consumers that want the SQLite file outside the module package.

### Build the solution

```powershell
dotnet build .\DataGen.slnx -v minimal
```

To enable the repo-managed pre-push hook that catches machine-specific path leaks before you publish changes:

```powershell
.\scripts\enable-git-hooks.ps1
```

### Run the tests

```powershell
dotnet test .\DataGen.slnx -v minimal /p:UseSharedCompilation=false -m:1
```

### Import the PowerShell module

```powershell
$modulePath = Join-Path $PWD 'src\SyntheticEnterprise.PowerShell\bin\Debug\net8.0\SyntheticEnterprise.PowerShell.dll'
Import-Module $modulePath -Force
Get-Command -Module SyntheticEnterprise.PowerShell | Sort-Object Name
```

If you want a release-style module bundle with a real manifest, package it first:

```powershell
.\scripts\package-module.ps1 -Version 0.11.0 -Configuration Release
Import-Module .\artifacts\module\SyntheticEnterprise.PowerShell\0.11.0\SyntheticEnterprise.PowerShell.psd1 -Force
```

### Generate a first world

```powershell
$scenario = New-SEScenarioFromArchetype -Archetype RegionalManufacturer
$scenario = Resolve-SEScenario -Scenario $scenario
$world = New-SEEnterpriseWorld -Scenario $scenario -Seed 4242
$world | Get-SEWorldSummary
```

### Export normalized artifacts

```powershell
$world | Export-SEEnterpriseWorld `
  -OutputPath .\out\first-world `
  -Format Json `
  -Profile Normalized `
  -IncludeManifest `
  -IncludeSummary `
  -Overwrite
```

### Review realism and quality

```powershell
.\scripts\invoke-realism-review.ps1 `
  -ScenarioPath .\examples\regional_manufacturer.scenario.json `
  -Seed 4242 `
  -OutputPath .\artifacts\quality\realism-review.md `
  -JsonOutputPath .\artifacts\quality\realism-review.json `
  -OutputFormat Both
```

That review emits a human-readable summary plus machine-readable quality validation output that can also be used in CI.

## Repository guide

The most important areas of the repository are:

- `src/`
  Core libraries, contracts, exporting, PowerShell module surface, and plugin host
- `catalogs/`
  Curated runtime catalog sources and packaged SQLite data
- `tests/`
  Core, exporting, integration, and workflow coverage
- `sdk/`
  Plugin SDK documentation and examples
- `website/`
  Docusaurus-based documentation site for GitHub Pages
- `docs/`
  Additional product and architecture documentation that informs the user-facing docs
- `examples/`
  Utility and helper scripts

## Documentation

The primary user-facing documentation now lives in the Docusaurus site under `website/`.

To work on the docs locally:

```powershell
Set-Location .\website
npm install
npm run start
```

To verify the production build:

```powershell
npm run build
```

The docs site includes:

- getting started guides
- cmdlet reference
- release notes and roadmap pages
- multiple end-to-end walkthroughs
- SDK and plugin architecture guidance
- contribution guidance
- integration and export patterns

## First-Party Packs

DataGen now includes bundled first-party packs under `packs/first-party/`.

These packs use the existing external plugin runtime and can be enabled directly from scenario JSON through the `packs` section. The current bundled set includes:

- `FirstParty.NoOp`
- `FirstParty.ITSM`
- `FirstParty.SecOps`
- `FirstParty.BusinessOps`

For a concrete example, see:

- `examples/regional_manufacturer_packs.scenario.json`
- `docs/FirstParty_Packs_Walkthrough.md`

The same scenario model also supports temporal outputs and quality reports directly on the generation result.

## Walkthrough assets

Reference walkthrough scenarios and scripts used by the docs site live under:

- `website/static/examples/scenarios/`
- `website/static/examples/scripts/`

These are intended to be practical starting points for common workflows such as:

- general enterprise lab generation
- Active Directory lab generation
- Entra-focused tenant generation
- hybrid identity generation
- repository and collaboration-heavy worlds
- plugin-extended dataset generation

## Contributing

Contributions are welcome across the product and the docs site.

Good contribution targets include:

- catalog improvements
- scenario and walkthrough coverage
- cmdlet help and examples
- SDK examples that respect the plugin boundary
- docs site polish and usability improvements

Before pushing changes, enable the repo-managed hooks once:

```powershell
.\scripts\enable-git-hooks.ps1
```

That pre-push hook runs `.\scripts\validate-repo-portability.ps1` so local absolute paths do not slip into tracked files.

When contributing, please keep the product boundary clear:

- DataGen core generates synthetic enterprise data
- DataGen plugins enrich that synthetic dataset
- downstream-system translation belongs in external adapters or companion integrations

## Publishing notes

The docs site is configured for GitHub Pages deployment through GitHub Actions. The workflow lives at:

- `.github/workflows/deploy-docs-site.yml`

Repository validation and module packaging are also automated through GitHub Actions:

- `.github/workflows/ci.yml`
- `.github/workflows/release-module.yml`

The release workflow creates both the versioned module bundle and a PowerShell Gallery `.nupkg`, then publishes to PSGallery by using the `PSGAL` repository secret.

Release publication is manual and requires fresh evidence from the prepared Windows workstation. Hosted CI continuously gates the portable publisher-metadata contract; it cannot exercise the real cross-filesystem path because GitHub-hosted runners do not provide the prepared `D:` NTFS and `G:` ReFS volumes. From a clean, committed `main` checkout on that workstation, use a fresh empty output directory and retain its evidence files:

```powershell
$evidenceRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('DataGenReleaseEvidence-v0.11.0-' + [Guid]::NewGuid().ToString('N'))))
.\scripts\invoke-release-preflight.ps1 `
  -OutputRoot $evidenceRoot `
  -CreateReleaseAttestation
```

The preflight resolves the full `HEAD` commit and tree, creates a deterministic `git archive` from that committed object, and extracts it beneath the requested output directory. Source-dependent contracts run from this isolated snapshot. The required publisher-metadata operations do not: their D: probe resolves beneath a validated D: root and their G: probe resolves beneath a validated G: root, each checked for reparse points, containment, drive identity, and the expected NTFS or ReFS filesystem before use and cleanup. The archive and a path/length/SHA-256 source manifest are re-hashed after contract execution and again after signing. The live branch, commit, tree, version, and clean state must also remain unchanged through completion.

The output includes `source-archive.tar`, `source-snapshot`, `source-manifest.json`, `release-preflight-evidence.json`, `release-preflight-summary.txt`, and `release-preflight-attestation.txt`. Before signing, the signer parses the exact evidence schema and verifies its requested version, source commit, completion time, committed tree, archive hash, manifest hash, and passed D: NTFS/G: ReFS contract facts. The canonical signed payload carries those same claims alongside the evidence SHA-256. A transient edit to the live working tree therefore cannot change the committed snapshot under test, even if that edit is restored before the final clean-state check. Dispatch the release from the same source commit within 24 hours:

```powershell
$attestation = (Get-Content "$evidenceRoot\release-preflight-attestation.txt" -Raw).Trim()
gh workflow run release-module.yml --ref main `
  -f version=0.11.0 `
  -f "publisher_metadata_attestation=$attestation" `
  -f publish_to_psgallery=true `
  -f create_github_release=true
```

The prepared-workstation release regression at `tests/ReleasePreflightAttestation.Integration.Tests.ps1` builds a temporary clean `main` candidate with an ephemeral non-exportable signing key, performs a transient live-checkout mutation while preflight runs, and requires signed output bound to the unchanged committed snapshot. Run it with a fresh output directory when changing release-attestation or source-snapshot behavior; it intentionally exercises the real `D:` NTFS and `G:` ReFS host contract and is therefore not run on GitHub-hosted workers.

The workflow rejects automatic/tag-triggered publication, non-`main` dispatches, noncanonical base64url, and missing, malformed, stale, wrong-version, wrong-source, or wrong-tree attestations before producing release artifacts. It also requires the signed D: NTFS `passed` and G: ReFS `passed` claims before any catalog build, package, or publication step. The attestation is a canonical payload signed with the non-exportable RSA private key in the prepared workstation's `Cert:\CurrentUser\My` store. GitHub Actions verifies the signature with the exact public certificate tracked at `release-trust/datagen-release-preflight-attestation.cer`; changing any signed field or inventing a new evidence hash invalidates the signature.

This is pinned-key verification, not CA-chain validation and not a claim that the certificate identifies an operator. The committed snapshot closes the transient-working-tree race; it does not defend against compromise of the prepared release account, private key, Git object database, or the preflight process in memory. Those remain inside the trusted workstation boundary. The private key must never be exported to a PFX, PEM, repository, build path, artifact, log, or ticket attachment. See [release-trust/README.md](release-trust/README.md) for the public identity, planned rotation procedure, and compromise or key-loss recovery steps.

The repository also ignores generated docs-site artifacts and local scratch inspection scripts so the publishable tree stays clean.
