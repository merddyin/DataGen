# PowerShell Command Surface Audit

## Purpose

This document captures the current PowerShell command surface for DataGen, identifies where the module is appropriately expressive versus where it risks drift, and sets practical guidance for future additions.

The goal is not to minimize the command count at all costs. The goal is to keep the surface coherent, discoverable, and stable while DataGen continues expanding as a synthetic environment platform.

## Current exported surface

The current module exports 29 cmdlets:

- `Add-SEIdentityLayer`
- `Add-SEInfrastructureLayer`
- `Add-SERepositoryLayer`
- `Export-SEEnterpriseWorld`
- `Get-SEGenerationPlugin`
- `Get-SEGenerationPluginRegistration`
- `Get-SEModuleCommandSurface`
- `Get-SEScenarioArchetype`
- `Get-SEScenarioPersonaPreset`
- `Get-SEScenarioTemplate`
- `Get-SEWorldSummary`
- `Import-SEEnterpriseWorld`
- `Install-SEGenerationPluginPackage`
- `Invoke-SEAnomalyProfile`
- `Merge-SEScenarioOverlay`
- `Merge-SEScenarioPersonaPreset`
- `New-SECatalogDatabase`
- `New-SEEnterpriseWorld`
- `New-SEGenerationPluginPackage`
- `New-SEScenarioFromArchetype`
- `New-SEScenarioFromPersonaPreset`
- `New-SEScenarioFromTemplate`
- `New-SEScenarioWizard`
- `Register-SEGenerationPlugin`
- `Resolve-SEScenario`
- `Save-SEEnterpriseWorld`
- `Test-SEGenerationPluginPackage`
- `Test-SEScenario`
- `Unregister-SEGenerationPlugin`

## High-level assessment

The current surface is not yet excessive, but it is now large enough that unstructured growth would become a usability problem quickly.

The current surface is still defensible because it clusters into a few understandable workflows:

- world generation and lifecycle
- scenario authoring and composition
- export and persistence
- plugin and SDK operations
- diagnostics and discovery

The main risk is not raw count. The main risk is command proliferation inside already-crowded workflow areas, especially scenario authoring and plugin management.

## Workflow grouping

### 1. World generation and lifecycle

- `New-SEEnterpriseWorld`
- `Invoke-SEAnomalyProfile`
- `Add-SEIdentityLayer`
- `Add-SEInfrastructureLayer`
- `Add-SERepositoryLayer`
- `Get-SEWorldSummary`

Assessment:

- This group is healthy.
- The three `Add-SE*Layer` commands are acceptable because they map to clear, owned enrichment slices.
- If more layer-specific commands are added, this area should pivot toward a generalized layer command instead of continuing to expand one command per layer.

Recommendation:

- Keep current commands.
- Treat three layer-specific commands as the practical ceiling unless one is retired or a generalized replacement is introduced.

### 2. Export and persistence

- `Export-SEEnterpriseWorld`
- `Save-SEEnterpriseWorld`
- `Import-SEEnterpriseWorld`

Assessment:

- This group is clean and easy to reason about.
- The verbs align well with user expectations.

Recommendation:

- Keep as-is.
- Prefer adding format/profile parameters over introducing new export-oriented top-level cmdlets.

### 3. Scenario authoring and composition

- `Get-SEScenarioArchetype`
- `Get-SEScenarioPersonaPreset`
- `Get-SEScenarioTemplate`
- `New-SEScenarioFromArchetype`
- `New-SEScenarioFromPersonaPreset`
- `New-SEScenarioFromTemplate`
- `New-SEScenarioWizard`
- `Resolve-SEScenario`
- `Test-SEScenario`
- `Merge-SEScenarioOverlay`
- `Merge-SEScenarioPersonaPreset`

Assessment:

- This is the most crowded workflow area.
- It is still understandable, but it is also the easiest place to accidentally add one-off authoring commands that overlap with existing commands.
- The platform direction now favors archetypes, overlays, personas, and the wizard over raw template-centric authoring.

Recommendation:

- Keep all current commands for compatibility.
- De-emphasize template-first commands in docs over time.
- Prefer:
  - `New-SEScenarioWizard`
  - `New-SEScenarioFromArchetype`
  - `Merge-SEScenarioOverlay`
  - `New-SEScenarioFromPersonaPreset`
- Avoid introducing new top-level scenario commands unless they remove at least one existing workflow step or replace multiple commands cleanly.

### 4. Plugin and SDK operations

- `Get-SEGenerationPlugin`
- `Get-SEGenerationPluginRegistration`
- `Register-SEGenerationPlugin`
- `Unregister-SEGenerationPlugin`
- `Install-SEGenerationPluginPackage`
- `New-SEGenerationPluginPackage`
- `Test-SEGenerationPluginPackage`

Assessment:

- This group is functional but beginning to crowd.
- The package lifecycle is understandable, but this is where future SDK growth could create a “one new cmdlet per SDK concern” problem.

Recommendation:

- Keep current commands.
- Do not add more top-level plugin/package cmdlets unless they clearly collapse workflow complexity.
- Prefer:
  - richer parameters
  - stronger validation modes
  - scripts and CI helpers
  - SDK docs and walkthroughs

### 5. Discovery and diagnostics

- `Get-SEModuleCommandSurface`
- `Get-SEWorldSummary`
- `Test-SEScenario`
- `New-SECatalogDatabase`

Assessment:

- `Get-SEModuleCommandSurface` is useful and should be treated as the primary introspection entrypoint for the module.
- Diagnostics are better handled through report output, scripts, and structured export than by continuously adding more inspection cmdlets.

Recommendation:

- Keep `Get-SEModuleCommandSurface` and document it as the discovery entrypoint.
- Prefer scripts for realism reviews and CI checks instead of introducing many new diagnostic cmdlets.

## Consolidation opportunities

No immediate removals are recommended.

However, the following areas should be treated as consolidation candidates instead of growth areas:

### Scenario templates

The template commands are still useful for compatibility and for users who already know that workflow, but they should become secondary to archetypes and personas.

Guidance:

- keep `Get-SEScenarioTemplate`
- keep `New-SEScenarioFromTemplate`
- stop expanding template-specific command surface
- bias docs toward archetypes, personas, and the wizard

### Layer commands

The current set is still manageable, but it should not continue growing one command per layer indefinitely.

Guidance:

- keep the three existing layer commands
- if a fourth or fifth layer command becomes desirable, replace the pattern with a generalized layer mutation command instead

### Plugin package lifecycle

The current command set is acceptable, but future work should prefer deeper capability inside the existing commands over adding new command nouns.

Guidance:

- package scaffolding belongs in `New-SEGenerationPluginPackage`
- package validation belongs in `Test-SEGenerationPluginPackage`
- installation belongs in `Install-SEGenerationPluginPackage`
- avoid adding adjacent commands for narrow sub-cases unless they materially simplify the overall workflow

## Guardrails for future additions

Before adding a new cmdlet, the change should pass these checks:

1. Does the new command represent a distinct user-facing workflow, not just a narrower variant of an existing command?
2. Would richer parameters, an additional mode, or better defaults solve the problem instead?
3. Does the new command improve discoverability, or does it create another near-duplicate entrypoint?
4. Can the capability live in a script, walkthrough, or CI helper instead of the module surface?
5. If added, which existing command becomes less important, and will docs be updated accordingly?

If those answers are weak, the default should be to avoid adding the cmdlet.

## Recommended documentation posture

Public docs should present the surface as a smaller set of preferred workflows rather than a flat list of equivalent entrypoints.

Preferred emphasis:

- world generation:
  - `New-SEEnterpriseWorld`
  - `Get-SEWorldSummary`
  - `Export-SEEnterpriseWorld`
- scenario authoring:
  - `New-SEScenarioWizard`
  - `New-SEScenarioFromArchetype`
  - `Merge-SEScenarioOverlay`
  - `New-SEScenarioFromPersonaPreset`
  - `Resolve-SEScenario`
- plugin authoring:
  - `New-SEGenerationPluginPackage`
  - `Test-SEGenerationPluginPackage`
  - `Install-SEGenerationPluginPackage`

Compatibility-oriented or secondary entrypoints should remain documented, but not presented as the primary path unless they are the best path for new users.

## Conclusion

The current PowerShell module surface is acceptable, but it is at the point where governance matters.

The best near-term stance is:

- keep the current surface stable
- de-emphasize templates in favor of archetypes, personas, and the wizard
- avoid new one-off plugin or diagnostic cmdlets
- favor scripts, validation outputs, and richer parameters over new top-level commands
- treat scenario and plugin areas as the two highest-risk zones for future command sprawl
