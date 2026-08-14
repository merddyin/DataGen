[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$EvidencePath,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$SourceCommit,

    [Parameter()]
    [string]$CompletedAtUtc = [DateTimeOffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'),

    [Parameter()]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$SigningCertificateThumbprint,

    [Parameter()]
    [string]$PublicCertificatePath = (Join-Path $PSScriptRoot '..\release-trust\datagen-release-preflight-attestation.cer')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$evidencePath = (Resolve-Path -LiteralPath $EvidencePath).Path
if (-not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) {
    throw "Release preflight evidence was not found at '$evidencePath'."
}

$parsedCompletedAt = [DateTimeOffset]::MinValue
if (-not [DateTimeOffset]::TryParseExact(
        $CompletedAtUtc,
        'yyyy-MM-ddTHH:mm:ssZ',
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal,
        [ref]$parsedCompletedAt)) {
    throw "CompletedAtUtc '$CompletedAtUtc' must use UTC format yyyy-MM-ddTHH:mm:ssZ."
}

$releaseTrustModule = Join-Path $PSScriptRoot 'release-trust\DataGen.ReleasePreflightAttestation.psm1'
if (-not (Test-Path -LiteralPath $releaseTrustModule -PathType Leaf)) {
    throw "Release-attestation trust module was not found at '$releaseTrustModule'."
}
Import-Module $releaseTrustModule -Force

$canonicalCompletedAtUtc = $parsedCompletedAt.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$evidenceClaims = Get-ReleasePreflightEvidenceClaims `
    -EvidencePath $evidencePath `
    -ExpectedVersion $Version `
    -ExpectedSourceCommit $SourceCommit `
    -ExpectedCompletedAtUtc $canonicalCompletedAtUtc

if ([string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)) {
    if (-not (Test-Path -LiteralPath $PublicCertificatePath -PathType Leaf)) {
        throw "Pinned release-attestation public certificate was not found at '$PublicCertificatePath'."
    }

    $pinnedCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new((Resolve-Path -LiteralPath $PublicCertificatePath).Path)
    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new('My', 'CurrentUser')
    try {
        $pinnedKeyId = Get-ReleasePreflightKeyId -Certificate $pinnedCertificate
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
        $signingCertificate = @($store.Certificates | Where-Object {
                $_.HasPrivateKey -and (Get-ReleasePreflightKeyId -Certificate $_) -ceq $pinnedKeyId
            } | Sort-Object NotAfter -Descending | Select-Object -First 1)
        if (-not $signingCertificate) {
            throw 'No private signing certificate in Cert:\CurrentUser\My matches the pinned release-attestation public certificate.'
        }
        $SigningCertificateThumbprint = $signingCertificate.Thumbprint
    }
    finally {
        $store.Close()
        $pinnedCertificate.Dispose()
    }
}

New-SignedReleasePreflightAttestation `
    -Version $Version `
    -SourceCommit $SourceCommit `
    -SourceTreeId $evidenceClaims.SourceTreeId `
    -CompletedAtUtc $canonicalCompletedAtUtc `
    -EvidenceHash (Get-FileHash -LiteralPath $evidencePath -Algorithm SHA256).Hash.ToLowerInvariant() `
    -SourceArchiveSha256 $evidenceClaims.SourceArchiveSha256 `
    -SourceManifestSha256 $evidenceClaims.SourceManifestSha256 `
    -DFileSystem $evidenceClaims.DFileSystem `
    -DResult $evidenceClaims.DResult `
    -GFileSystem $evidenceClaims.GFileSystem `
    -GResult $evidenceClaims.GResult `
    -SigningCertificateThumbprint $SigningCertificateThumbprint `
    -PublicCertificatePath $PublicCertificatePath
