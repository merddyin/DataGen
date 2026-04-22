# DataGen

DataGen is a synthetic enterprise data generation platform. It procedurally builds realistic enterprise datasets that teams can use for labs, validation, demos, exports, discovery-tool testing, and downstream integration work.

## Changelog

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

### 1. Build the solution

If you do not already have a local seeded catalog database, generate it first:

```powershell
.\scripts\build-catalog-artifact.ps1 -InstallToCatalogRoot
```

That command writes the canonical build output to `artifacts\catalog\catalogs.sqlite` and installs a local working copy to `catalogs\catalogs.sqlite` for module builds.

### 2. Build the solution

```powershell
dotnet build .\DataGen.slnx -v minimal
```

### 3. Run the tests

```powershell
dotnet test .\DataGen.slnx -v minimal /p:UseSharedCompilation=false -m:1
```

### 4. Import the PowerShell module

```powershell
$modulePath = Join-Path $PWD 'src\SyntheticEnterprise.PowerShell\bin\Debug\net8.0\SyntheticEnterprise.PowerShell.dll'
Import-Module $modulePath -Force
Get-Command -Module SyntheticEnterprise.PowerShell | Sort-Object Name
```

If you want a release-style module bundle with a real manifest, package it first:

```powershell
.\scripts\package-module.ps1 -Version 0.4.0 -Configuration Release
Import-Module .\artifacts\module\SyntheticEnterprise.PowerShell\0.4.0\SyntheticEnterprise.PowerShell.psd1 -Force
```

### 5. Generate a first world

```powershell
$scenario = New-SEScenarioFromArchetype -Archetype RegionalManufacturer
$scenario = Resolve-SEScenario -Scenario $scenario
$world = New-SEEnterpriseWorld -Scenario $scenario -Seed 4242
$world | Get-SEWorldSummary
```

### 6. Export normalized artifacts

```powershell
$world | Export-SEEnterpriseWorld `
  -OutputPath .\out\first-world `
  -Format Json `
  -Profile Normalized `
  -IncludeManifest `
  -IncludeSummary `
  -Overwrite
```

### 7. Review realism and quality

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
