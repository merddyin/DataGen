# Milestone 5: Anomaly Framework V2

## Goal
Enrich anomaly generation so anomaly records are not just present, but explainable, rankable, and demo-ready.

## Outcomes
- Standard anomaly envelope across layers
- Severity and confidence scoring
- Rationale and evidence payloads
- Remediation guidance metadata
- Detection hint metadata for downstream demo UX
- Normalized anomaly export shape for CSV/JSON bundles

## Scope
This milestone introduces a cross-layer anomaly model that identity, infrastructure, repository, and future anomaly generators can all emit.

## Core design
Each anomaly record should include:
- `AnomalyId`
- `Category`
- `Type`
- `Title`
- `Summary`
- `Severity`
- `Confidence`
- `Status`
- `Rationale`
- `DetectionHints`
- `RemediationHints`
- `Evidence`
- `TargetEntities`
- `RelatedEntities`
- `SourceLayer`
- `GeneratedAtUtc`

## Key implementation points
1. Add shared anomaly contracts.
2. Introduce an anomaly enrichment service.
3. Update layer generators to emit the shared contract.
4. Add export mapping for anomalies and evidence.
5. Add validation tests for required fields and scoring ranges.

## Cmdlet surface impact
- `Invoke-SEAnomalyProfile` should emit anomalies in the new normalized shape.
- `Get-SEWorldSummary` should be able to summarize anomaly counts by severity, category, and status.
- `Export-SEEnterpriseWorld` should produce:
  - `anomalies.csv`
  - `anomaly_evidence.csv`
  - `anomaly_targets.csv`

## Risks
- Over-modeling anomalies before all layer generators are mature
- Divergence between legacy anomaly records and the new normalized record
- Weak evidence linking if entity identifiers are not stable enough

## Acceptance criteria
- All generated anomalies carry severity and confidence.
- Each anomaly can reference one or more target entities.
- Export includes normalized anomaly tables.
- Tests cover severity bounds, evidence serialization, and target linkage.
