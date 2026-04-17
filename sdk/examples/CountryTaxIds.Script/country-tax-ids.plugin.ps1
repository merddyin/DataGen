$rulesByCountry = @{}

foreach ($row in $PluginCatalogs.CsvCatalogs['tax-id-rules']) {
  $rulesByCountry[$row.Country] = $row
}

$defaultRule = $rulesByCountry['United States']
$records = @()

foreach ($person in $InputWorld.People) {
  $country = if ($null -eq $person.Country -or $person.Country -eq '') { 'United States' } else { $person.Country }
  $rule = if ($rulesByCountry.ContainsKey($country)) { $rulesByCountry[$country] } else { $defaultRule }
  $requestedDigits = [int]$rule.Digits
  $digits = ($person.Id -replace '\D', '')

  if ($null -eq $digits -or $digits -eq '') {
    $digits = '0'
  }

  while ($digits.Length -lt $requestedDigits) {
    $digits += '0'
  }

  if ($digits.Length -gt $requestedDigits) {
    $digits = ($digits[0..($requestedDigits - 1)] -join '')
  }

  $identifierValue = '{0}-{1}' -f $rule.Prefix, $digits
  $records += New-PluginRecord -RecordType 'NationalIdentifier' -AssociatedEntityType 'Person' -AssociatedEntityId $person.Id -Properties @{
    Country = $rule.Country
    IdentifierType = $rule.IdType
    IdentifierValue = $identifierValue
    Source = 'sdk-example-script'
  }
}

New-PluginResult -Records $records -Warnings @('sdk-script-plugin-ok')
