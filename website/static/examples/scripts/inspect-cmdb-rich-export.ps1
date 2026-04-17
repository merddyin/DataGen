param(
    [string]$InputPath = (Join-Path $PWD 'out\general-enterprise-lab\normalized'),
    [string]$Format = 'Json'
)

# This walkthrough currently focuses on inspection because CMDB-specific toggles
# are not fully exposed on the authored scenario envelope used by the cmdlet-first
# authoring path. Use this script against a world or export where CMDB generation
# has already been enabled.

$candidateFiles = @(
    'configuration_items',
    'configuration_item_relationships',
    'cmdb_source_records',
    'cmdb_source_links',
    'cmdb_source_relationships'
) | ForEach-Object {
    Join-Path $InputPath ("{0}.{1}" -f $_, $Format.ToLowerInvariant())
}

foreach ($file in $candidateFiles) {
    if (Test-Path $file) {
        Write-Host "Found $file"
    }
    else {
        Write-Warning "Missing expected CMDB artifact: $file"
    }
}
