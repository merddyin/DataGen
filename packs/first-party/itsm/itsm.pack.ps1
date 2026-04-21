$ticketCount = 12
$rawTicketCount = $PluginRequest.PluginSettings['TicketCount']
if ($null -ne $rawTicketCount -and $rawTicketCount -ne '') {
  try {
    $ticketCount = [int]$rawTicketCount
  } catch {
    $ticketCount = 12
  }
}
if ($ticketCount -lt 1) {
  $ticketCount = 1
}

$queueName = $PluginRequest.PluginSettings['QueueName']
if ($null -eq $queueName -or $queueName -eq '') {
  $queueName = 'Corporate IT Service Desk'
}

$records = @()
$company = $InputWorld.Companies | Select-Object -First 1
$team = $InputWorld.Teams | Select-Object -First 1
$people = @($InputWorld.People)
$devices = @($InputWorld.Devices)
$applications = @($InputWorld.Applications)

if ($null -ne $company) {
  $queueId = "ITSM-QUEUE-$($company.Id)"
  $records += New-PluginRecord -RecordType 'ItsmQueue' -AssociatedEntityType 'Company' -AssociatedEntityId $company.Id -Properties @{
    QueueId = $queueId
    QueueName = $queueName
    ServiceTier = 'Tier 1'
    SupportModel = 'SharedServices'
    CompanyId = $company.Id
  }

  if ($null -ne $team) {
    $records += New-PluginRecord -RecordType 'ItsmQueueOwnership' -AssociatedEntityType 'Team' -AssociatedEntityId $team.Id -Properties @{
      relationship_type = 'queue_owned_by_team'
      source_entity_type = 'ItsmQueue'
      source_entity_id = $queueId
      target_entity_type = 'Team'
      target_entity_id = $team.Id
      queue_name = $queueName
    }
  }

  for ($index = 0; $index -lt $ticketCount; $index++) {
    $person = if ($people.Count -gt 0) { $people[$index % $people.Count] } else { $null }
    $device = if ($devices.Count -gt 0) { $devices[$index % $devices.Count] } else { $null }
    $application = if ($applications.Count -gt 0) { $applications[$index % $applications.Count] } else { $null }
    $ticketId = 'ITSM-TICKET-{0:000}' -f ($index + 1)
    $summary = if ($null -ne $application) {
      "Access issue for $($application.Name)"
    } elseif ($null -ne $device) {
      "Endpoint support request for $($device.Hostname)"
    } else {
      "General help desk request $($index + 1)"
    }
    $status = if (($index % 4) -eq 0) { 'In Progress' } else { 'Open' }
    $priority = if (($index % 5) -eq 0) { 'High' } else { 'Medium' }
    $requesterPersonId = if ($null -ne $person) { $person.Id } else { '' }
    $deviceId = if ($null -ne $device) { $device.Id } else { '' }
    $applicationId = if ($null -ne $application) { $application.Id } else { '' }

    $records += New-PluginRecord -RecordType 'ItsmTicket' -AssociatedEntityType 'Company' -AssociatedEntityId $company.Id -Properties @{
      TicketId = $ticketId
      QueueId = $queueId
      QueueName = $queueName
      TicketNumber = 'INC{0}' -f (1000 + $index)
      Summary = $summary
      Status = $status
      Priority = $priority
      RequesterPersonId = $requesterPersonId
      DeviceId = $deviceId
      ApplicationId = $applicationId
    }
  }
}

New-PluginResult -Records $records -Warnings @()
