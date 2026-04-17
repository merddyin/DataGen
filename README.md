# DataGen

DataGen is a synthetic enterprise data generation platform. It procedurally builds realistic enterprise datasets that teams can use for labs, validation, demos, exports, discovery-tool testing, and downstream integration work.

## What DataGen does

DataGen is designed to generate believable enterprise structure without hand-authoring every user, group, device, application, repository, policy, or CMDB record.

Current product capabilities include:

- scenario-first world generation with templates, overlays, JSON, and a terminal wizard
- identity, infrastructure, repository, application, policy, access-evidence, observed-data, and CMDB generation
- configurable realism through deviation profiles such as `Clean`, `Realistic`, and `Aggressive`
- normalized export surfaces for downstream tooling
- a plugin model for extending the synthetic dataset safely

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
.\scripts\package-module.ps1 -Version 0.1.0 -Configuration Release
Import-Module .\artifacts\module\SyntheticEnterprise.PowerShell\0.1.0\SyntheticEnterprise.PowerShell.psd1 -Force
```

### 5. Generate a first world

```powershell
$scenario = New-SEScenarioFromTemplate -Template RegionalManufacturer
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
- multiple end-to-end walkthroughs
- SDK and plugin architecture guidance
- contribution guidance
- integration and export patterns

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
