[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [AllowEmptyString()]
    [string]$Attestation,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$ExpectedVersion,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$ExpectedSourceCommit,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$ExpectedSourceTreeId,

    [Parameter()]
    [string]$NowUtc = [DateTimeOffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'),

    [Parameter()]
    [ValidateRange(1, 168)]
    [int]$MaximumAgeHours = 24,

    [Parameter()]
    [string]$PublicCertificatePath = (Join-Path $PSScriptRoot '..\release-trust\datagen-release-preflight-attestation.cer')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$releaseTrustModule = Join-Path $PSScriptRoot 'release-trust\DataGen.ReleasePreflightAttestation.psm1'
if (-not (Test-Path -LiteralPath $releaseTrustModule -PathType Leaf)) {
    throw "Release-attestation trust module was not found at '$releaseTrustModule'."
}
Import-Module $releaseTrustModule -Force

$validatedAttestation = Assert-SignedReleasePreflightAttestation `
    -Attestation $Attestation `
    -ExpectedVersion $ExpectedVersion `
    -ExpectedSourceCommit $ExpectedSourceCommit `
    -ExpectedSourceTreeId $ExpectedSourceTreeId `
    -NowUtc $NowUtc `
    -MaximumAgeHours $MaximumAgeHours `
    -PublicCertificatePath $PublicCertificatePath

Write-Host "Prepared-workstation signed release preflight attestation accepted for version $ExpectedVersion at source $($ExpectedSourceCommit.ToLowerInvariant()) and tree $($ExpectedSourceTreeId.ToLowerInvariant()) with passed D: NTFS and G: ReFS claims using key $($validatedAttestation.KeyId)."
