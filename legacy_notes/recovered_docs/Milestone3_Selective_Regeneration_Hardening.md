# Milestone 3: Selective Regeneration Hardening

## Objective
Harden rerun behavior for pipeline-native layer commands so repeated and partial workflows are predictable, inspectable, and safe for demo dataset authoring.

## Scope
This milestone focuses on three concerns:

1. **Explicit regeneration semantics**
   - Define what `SkipIfPresent`, `ReplaceLayer`, and `Merge` mean for each layer.
   - Make ownership boundaries explicit so a layer can replace only the artifacts it owns.

2. **Reference repair and downstream integrity**
   - Track which entities were replaced, preserved, or removed.
   - Repair cross-layer references when possible.
   - Surface warnings when repair is partial or impossible.

3. **Replay-safe execution history**
   - Record regeneration decisions and repair actions in session history.
   - Preserve enough detail to explain why a rerun changed the world.

## Concrete deliverables
- regeneration policy contracts
- per-layer ownership descriptors
- entity identity and remapping contracts
- reference repair service scaffolding
- revised layer execution record scaffolding
- merge result and warning model
- cmdlet scaffolding updates for `Add-*` and `Invoke-*`
- test scaffolds for replace, merge, and repair behavior

## Design principles
- object-first and pipeline-native
- clone-before-mutate remains the baseline
- layer replacement is **owned-artifact aware**
- merge is explicit, not best-effort magic
- no silent corruption of references
- warnings are first-class outputs in metadata/session history

## Recommended semantics

### SkipIfPresent
- if a layer is already marked as applied, do not mutate the world
- emit an execution record with `Skipped`
- preserve the existing world object shape and identifiers

### ReplaceLayer
- replace artifacts owned by the target layer
- preserve stable identifiers when deterministic remapping rules are available
- generate an `EntityRemappingSet`
- invoke reference repair for dependent entities
- append warnings when any reference cannot be repaired automatically

### Merge
- keep existing artifacts unless the merge policy allows replacement
- add newly generated artifacts that do not conflict
- when conflicts occur, choose one of:
  - preserve existing
  - replace existing
  - duplicate intentionally with a warning
- emit a `MergeResolutionRecord` for each conflict class

## Ownership model
Each layer should publish the artifact types it owns.

Example:
- identity owns: directory accounts, OUs, groups, memberships, identity anomalies
- infrastructure owns: devices, servers, networks, telephony, installations, infrastructure anomalies
- repository owns: databases, file shares, collaboration sites, repository grants, repository anomalies

This is what makes replacement bounded and intelligible.

## Repair examples
When identity is replaced:
- infrastructure device `PrimaryUserAccountId` may need remapping
- repository grant principals may need remapping
- anomaly targets referencing old account IDs may need remapping or invalidation

When infrastructure is replaced:
- repository ownership links to devices/servers may need remapping
- anomaly targets referencing retired assets may need remapping or removal

## Out of scope
- fully conflict-aware deep semantic merge for every entity type
- user-defined custom merge DSL
- persisted diff visualization UI

## Exit criteria
Milestone 3 is complete when:
- rerunning a layer no longer has ambiguous semantics
- replace flows can emit deterministic remapping output
- downstream references are repaired or clearly warned
- session history shows what happened during regeneration
