$requestCount = 6
$rawRequestCount = $PluginRequest.PluginSettings['RequestCount']
if ($null -ne $rawRequestCount -and $rawRequestCount -ne '') {
  try {
    $requestCount = [int]$rawRequestCount
  } catch {
    $requestCount = 6
  }
}
if ($requestCount -lt 1) {
  $requestCount = 1
}

$records = @()
$company = $InputWorld.Companies | Select-Object -First 1
$departments = @($InputWorld.Departments)
$applications = @($InputWorld.Applications)

if ($null -ne $company) {
  for ($index = 0; $index -lt $requestCount; $index++) {
    $department = if ($departments.Count -gt 0) { $departments[$index % $departments.Count] } else { $null }
    $application = if ($applications.Count -gt 0) { $applications[$index % $applications.Count] } else { $null }
    $vendorId = 'VENDOR-{0:000}' -f ($index + 1)
    $requestId = 'PROC-REQ-{0:000}' -f ($index + 1)
    $vendorName = if ($null -ne $application -and $application.Vendor) {
      "$($application.Vendor) Services"
    } else {
      "Strategic Supplier $($index + 1)"
    }
    $category = if (($index % 2) -eq 0) { 'Technology' } else { 'ProfessionalServices' }
    $criticality = if (($index % 3) -eq 0) { 'High' } else { 'Medium' }
    $linkedApplicationId = if ($null -ne $application) { $application.Id } else { '' }
    $status = if (($index % 4) -eq 0) { 'Approved' } else { 'Submitted' }
    $amountBand = if (($index % 3) -eq 0) { '25k-50k' } else { '5k-25k' }
    $departmentId = if ($null -ne $department) { $department.Id } else { '' }

    $records += New-PluginRecord -RecordType 'Vendor' -AssociatedEntityType 'Company' -AssociatedEntityId $company.Id -Properties @{
      VendorId = $vendorId
      VendorName = $vendorName
      Category = $category
      Criticality = $criticality
      LinkedApplicationId = $linkedApplicationId
    }

    $records += New-PluginRecord -RecordType 'PurchaseRequest' -AssociatedEntityType 'Company' -AssociatedEntityId $company.Id -Properties @{
      RequestId = $requestId
      VendorId = $vendorId
      RequestNumber = 'PR{0}' -f (4000 + $index)
      Status = $status
      AmountBand = $amountBand
      DepartmentId = $departmentId
      LinkedApplicationId = $linkedApplicationId
    }

    if ($null -ne $department) {
      $records += New-PluginRecord -RecordType 'VendorOwnership' -AssociatedEntityType 'Department' -AssociatedEntityId $department.Id -Properties @{
        relationship_type = 'vendor_owned_by_department'
        source_entity_type = 'Vendor'
        source_entity_id = $vendorId
        target_entity_type = 'Department'
        target_entity_id = $department.Id
      }
    }
  }
}

New-PluginResult -Records $records -Warnings @()
