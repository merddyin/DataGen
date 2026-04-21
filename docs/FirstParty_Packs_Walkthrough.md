# First-Party Packs Walkthrough

This walkthrough shows how to enable the bundled DataGen domain packs by using the native `packs` scenario shape.

## What the bundled packs provide

- `FirstParty.NoOp`
  Validates bundled pack discovery and registration
- `FirstParty.ITSM`
  Adds service desk queues, tickets, and queue ownership relationships
- `FirstParty.SecOps`
  Adds security alerts and analyst ownership relationships
- `FirstParty.BusinessOps`
  Adds vendors, purchase requests, and ownership relationships

## Example scenario

Start from [`examples/regional_manufacturer_packs.scenario.json`](../examples/regional_manufacturer_packs.scenario.json). It enables bundled packs without requiring a separate plugin install path.

```json
{
  "name": "Regional Manufacturer With Packs",
  "template": "RegionalManufacturer",
  "packs": {
    "includeBundledPacks": true,
    "enabledPacks": [
      {
        "packId": "FirstParty.ITSM",
        "settings": {
          "TicketCount": "14"
        }
      },
      {
        "packId": "FirstParty.SecOps",
        "settings": {
          "AlertCount": "9"
        }
      }
    ]
  }
}
```

## Generate a world with bundled packs

```powershell
$scenario = Get-Content .\examples\regional_manufacturer_packs.scenario.json -Raw | ConvertFrom-Json
$resolved = Resolve-SEScenario -Scenario $scenario
$world = New-SEEnterpriseWorld -Scenario $resolved -Seed 424242
```

When the bundled packs are enabled, plugin-generated records are attached to the world through the existing external plugin runtime.

## Export normalized artifacts

```powershell
$world | Export-SEEnterpriseWorld `
  -OutputPath .\out\regional-manufacturer-packs `
  -Format Json `
  -Profile Normalized `
  -IncludeManifest `
  -IncludeSummary `
  -Overwrite
```

The normalized export will include:

- `entities/plugin_generated_records.json`
- `links/plugin_generated_relationships.json`

These artifacts carry pack-generated entities and relationship rows without requiring consumer-specific shaping inside the pack itself.

## Where the bundled packs live

Bundled first-party packs are stored under [`packs/first-party/`](../packs/first-party/).

Each pack uses the same external plugin manifest model as any other plugin:

- `*.generator.json` for manifest and parameter metadata
- `*.pack.ps1` for the data-only generation script

## Why this shape was chosen

The production repo already had a secure external plugin runtime. The first-party pack model builds on that existing path so DataGen gains scenario-first pack enablement, normalized export surfaces, and bundled examples without introducing a second execution framework.
