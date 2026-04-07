# Milestone 5 Design Notes

## Severity model
Use a constrained enum:
- Informational
- Low
- Medium
- High
- Critical

## Confidence model
Confidence is numeric to support sorting and UI filters:
- decimal range 0.0 to 1.0

## Evidence model
Evidence records should stay generic so all layers can reuse them.
Examples:
- account MFA state
- group membership count
- stale last logon timestamp
- device compliance state
- repository access scope

## Remediation guidance
Remediation stays descriptive, not prescriptive automation.
Suggested fields:
- `Recommendation`
- `SuggestedOwnerRole`
- `EstimatedEffort`
- `ReferenceKey`

## Compatibility strategy
Keep legacy anomaly records internally if needed, but map them into `NormalizedAnomalyRecord` before export and summary generation.

## Future extension points
- analyst verdict fields
- suppression / exception metadata
- lifecycle state transitions
- ticket / workflow linkage
