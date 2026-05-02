# DataGen

DataGen is a synthetic enterprise data generation platform. It procedurally builds realistic enterprise datasets that teams can use for labs, validation, demos, exports, discovery-tool testing, and downstream integration work.

## Changelog

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
.\scripts\package-module.ps1 -Version 0.7.0 -Configuration Release
Import-Module .\artifacts\module\SyntheticEnterprise.PowerShell\0.7.0\SyntheticEnterprise.PowerShell.psd1 -Force
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

The repository also ignores generated docs-site artifacts and local scratch inspection scripts so the publishable tree stays clean.
