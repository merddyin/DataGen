# Milestone 3 Design Notes

## Proposed flow
1. Resolve the target layer policy.
2. Clone the current `GenerationResult`.
3. Generate candidate artifacts for the layer.
4. Build an ownership-scoped change set.
5. Apply policy-specific mutation rules.
6. Produce an `EntityRemappingSet`.
7. Run downstream reference repair.
8. Append warnings and execution history.
9. Return the updated `GenerationResult`.

## Key internal abstractions
- `ILayerOwnershipRegistry`
- `IRegenerationPlanner`
- `ILayerMergeService`
- `IReferenceRepairService`
- `IEntityRemappingService`
- `IExecutionWarningSink`

## Suggested metadata additions
- `WorldMetadata.LastRegenerationModeByLayer`
- `WorldMetadata.ReferenceRepairWarnings`
- `WorldMetadata.EntityRemappingSummaries`

## Suggested session history additions
- execution outcome
- replaced entity counts
- merged entity counts
- removed entity counts
- repaired reference counts
- unrepaired reference counts
- warning count

## Practical advice
Start conservative.

For the first implementation of `Merge`, prefer:
- preserve existing by default
- append only truly new artifacts
- warn on unresolved conflicts

That gives predictable demo behavior without pretending the merge engine is smarter than it is.
