# v12 World/session Manifest

Referenced files
- synthetic_enterprise_world_session_manifest.zip
- synthetic_enterprise_module_scaffold_v12/SyntheticEnterprise_World_Session_Manifest.md
- synthetic_enterprise_module_scaffold_v12/examples/pipeline_first_demo.ps1

What this adds
- `ScenarioIdentity`
- `CatalogIdentity`
- `LayerExecutionRecord`
- `ExportExecutionRecord`
- `WorldSessionManifest`
- `IWorldSessionService`
- `WorldSessionService`
- `Get-SEWorldSession`

What changed
- `GenerationResult` now carries a `SessionManifest`
- initial world generation now records:
  - scenario identity
  - catalog identity
  - initial layer history
- layer processing now appends execution history
- export can now append export history when used with `-PassThru`
- `New-SEEnterpriseWorld` now records `ScenarioPath` in metadata when path-based input is used

This makes the workflow more inspectable:
```powershell
$world = New-SEEnterpriseWorld -ScenarioPath .egional_manufacturer.scenario.json -CatalogRootPath .\catalogs
$world = $world | Add-SEIdentityLayer | Add-SEInfrastructureLayer | Add-SERepositoryLayer | Invoke-SEAnomalyProfile
$world = $world | Export-SEEnterpriseWorld -Format Csv -OutputPath .\out -PassThru
$world | Get-SEWorldSession
```

Next strongest step
- content-based catalog hashing
- persisted snapshot/import support
- save/resume/compare across runs
