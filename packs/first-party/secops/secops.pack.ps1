$alertCount = 8
$rawAlertCount = $PluginRequest.PluginSettings['AlertCount']
if ($null -ne $rawAlertCount -and $rawAlertCount -ne '') {
  try {
    $alertCount = [int]$rawAlertCount
  } catch {
    $alertCount = 8
  }
}
if ($alertCount -lt 1) {
  $alertCount = 1
}

$records = @()
$company = $InputWorld.Companies | Select-Object -First 1
$people = @($InputWorld.People)
$accounts = @($InputWorld.Accounts)
$devices = @($InputWorld.Devices)

if ($null -ne $company) {
  for ($index = 0; $index -lt $alertCount; $index++) {
    $person = if ($people.Count -gt 0) { $people[$index % $people.Count] } else { $null }
    $account = if ($accounts.Count -gt 0) { $accounts[$index % $accounts.Count] } else { $null }
    $device = if ($devices.Count -gt 0) { $devices[$index % $devices.Count] } else { $null }
    $alertId = 'SEC-ALERT-{0:000}' -f ($index + 1)
    $caseId = 'SEC-CASE-{0:000}' -f ($index + 1)
    $alertTitle = if ($null -ne $account) { "Impossible travel for $($account.UserPrincipalName)" } else { "Suspicious sign-in activity $($index + 1)" }
    $severity = if (($index % 3) -eq 0) { 'High' } else { 'Medium' }
    $deviceId = if ($null -ne $device) { $device.Id } else { '' }
    $accountId = if ($null -ne $account) { $account.Id } else { '' }
    $analystPersonId = if ($null -ne $person) { $person.Id } else { '' }

    $records += New-PluginRecord -RecordType 'SecurityAlert' -AssociatedEntityType 'Company' -AssociatedEntityId $company.Id -Properties @{
      AlertId = $alertId
      CaseId = $caseId
      AlertTitle = $alertTitle
      Severity = $severity
      DetectionSource = 'Synthetic XDR'
      DeviceId = $deviceId
      AccountId = $accountId
      AnalystPersonId = $analystPersonId
    }

    if ($null -ne $person) {
      $records += New-PluginRecord -RecordType 'SecurityAlertOwnership' -AssociatedEntityType 'Person' -AssociatedEntityId $person.Id -Properties @{
        relationship_type = 'security_alert_owned_by_analyst'
        source_entity_type = 'SecurityAlert'
        source_entity_id = $alertId
        target_entity_type = 'Person'
        target_entity_id = $person.Id
        case_id = $caseId
      }
    }
  }
}

New-PluginResult -Records $records -Warnings @()
