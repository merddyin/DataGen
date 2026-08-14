Set-StrictMode -Version Latest

$script:Schema = 'datagen-release-preflight-attestation-v3'
$script:EnvelopePrefix = 'datagen-release-preflight-v3'
$script:EvidenceSchema = 'datagen-release-preflight-evidence-v2'

function ConvertTo-Base64Url {
    param([Parameter(Mandatory)][byte[]]$Bytes)

    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function ConvertFrom-Base64Url {
    param([Parameter(Mandatory)][string]$Value)

    if ($Value -notmatch '^[A-Za-z0-9_-]+$') {
        throw 'The signed attestation contains an invalid base64url value.'
    }

    $base64 = $Value.Replace('-', '+').Replace('_', '/')
    switch ($base64.Length % 4) {
        0 { }
        2 { $base64 += '==' }
        3 { $base64 += '=' }
        default { throw 'The signed attestation contains an invalid base64url length.' }
    }

    try {
        $decoded = [Convert]::FromBase64String($base64)
    }
    catch {
        throw 'The signed attestation contains malformed base64url data.'
    }

    if ((ConvertTo-Base64Url -Bytes $decoded) -cne $Value) {
        throw 'The signed attestation contains a noncanonical base64url value.'
    }

    return $decoded
}

function ConvertTo-HexLower {
    param([Parameter(Mandatory)][byte[]]$Bytes)

    ([BitConverter]::ToString($Bytes)).Replace('-', '').ToLowerInvariant()
}

function Get-ReleasePreflightKeyId {
    param([Parameter(Mandatory)][System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate)

    $hashAlgorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return "sha256-$((ConvertTo-HexLower -Bytes $hashAlgorithm.ComputeHash($Certificate.RawData)))"
    }
    finally {
        $hashAlgorithm.Dispose()
    }
}

function Get-ReleasePreflightPublicCertificate {
    param([Parameter(Mandatory)][string]$PublicCertificatePath)

    if (-not (Test-Path -LiteralPath $PublicCertificatePath -PathType Leaf)) {
        throw "Pinned release-attestation public certificate was not found at '$PublicCertificatePath'."
    }

    $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new((Resolve-Path -LiteralPath $PublicCertificatePath).Path)
    $rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($certificate)
    if (-not $rsa) {
        $certificate.Dispose()
        throw 'Pinned release-attestation public certificate must contain an RSA public key.'
    }
    $rsa.Dispose()

    $now = [DateTimeOffset]::UtcNow
    if ($now -lt [DateTimeOffset]$certificate.NotBefore -or $now -gt [DateTimeOffset]$certificate.NotAfter) {
        $certificate.Dispose()
        throw 'Pinned release-attestation public certificate is not currently valid. Rotate the pinned key before release publication.'
    }

    return $certificate
}

function New-ReleasePreflightAttestationPayload {
    param(
        [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')][string]$Version,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$SourceCommit,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$SourceTreeId,
        [Parameter(Mandatory)][ValidatePattern('^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$')][string]$CompletedAtUtc,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$EvidenceHash,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$SourceArchiveSha256,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$SourceManifestSha256,
        [Parameter(Mandatory)][ValidateSet('NTFS')][string]$DFileSystem,
        [Parameter(Mandatory)][ValidateSet('passed')][string]$DResult,
        [Parameter(Mandatory)][ValidateSet('ReFS')][string]$GFileSystem,
        [Parameter(Mandatory)][ValidateSet('passed')][string]$GResult,
        [Parameter(Mandatory)][ValidatePattern('^sha256-[0-9a-f]{64}$')][string]$KeyId
    )

    $completedAt = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParseExact(
            $CompletedAtUtc,
            'yyyy-MM-ddTHH:mm:ssZ',
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal,
            [ref]$completedAt)) {
        throw "CompletedAtUtc '$CompletedAtUtc' must use UTC format yyyy-MM-ddTHH:mm:ssZ."
    }

    @(
        "schema=$script:Schema",
        "key_id=$KeyId",
        "version=$Version",
        "source=$($SourceCommit.ToLowerInvariant())",
        "tree=$($SourceTreeId.ToLowerInvariant())",
        "completed=$($completedAt.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))",
        "evidence=$($EvidenceHash.ToLowerInvariant())",
        "archive=$($SourceArchiveSha256.ToLowerInvariant())",
        "manifest=$($SourceManifestSha256.ToLowerInvariant())",
        "d_filesystem=$DFileSystem",
        "d_result=$DResult",
        "g_filesystem=$GFileSystem",
        "g_result=$GResult"
    ) -join "`n"
}

function New-ReleasePreflightAttestationEnvelope {
    param(
        [Parameter(Mandatory)][string]$Payload,
        [Parameter(Mandatory)][byte[]]$Signature
    )

    "$script:EnvelopePrefix|payload=$(ConvertTo-Base64Url -Bytes ([Text.Encoding]::UTF8.GetBytes($Payload)))|signature=$(ConvertTo-Base64Url -Bytes $Signature)"
}

function ConvertFrom-ReleasePreflightAttestation {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Attestation)

    $match = [regex]::Match(
        $Attestation,
        "^$script:EnvelopePrefix\|payload=(?<payload>[A-Za-z0-9_-]+)\|signature=(?<signature>[A-Za-z0-9_-]+)$",
        [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $match.Success) {
        throw 'The prepared-workstation signed release preflight attestation is malformed.'
    }

    $payloadBytes = ConvertFrom-Base64Url -Value $match.Groups['payload'].Value
    $payload = [Text.Encoding]::UTF8.GetString($payloadBytes)
    $payloadMatch = [regex]::Match(
        $payload,
        "^schema=$script:Schema`nkey_id=(?<keyid>sha256-[0-9a-f]{64})`nversion=(?<version>\d+\.\d+\.\d+(?:\.\d+)?)`nsource=(?<source>[0-9a-f]{40})`ntree=(?<tree>[0-9a-f]{40})`ncompleted=(?<completed>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z)`nevidence=(?<evidence>[0-9a-f]{64})`narchive=(?<archive>[0-9a-f]{64})`nmanifest=(?<manifest>[0-9a-f]{64})`nd_filesystem=(?<dfilesystem>NTFS)`nd_result=(?<dresult>passed)`ng_filesystem=(?<gfilesystem>ReFS)`ng_result=(?<gresult>passed)$",
        [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $payloadMatch.Success) {
        throw 'The signed release preflight payload is malformed or not canonical.'
    }

    [pscustomobject]@{
        Payload = $payload
        PayloadBytes = $payloadBytes
        Signature = ConvertFrom-Base64Url -Value $match.Groups['signature'].Value
        KeyId = $payloadMatch.Groups['keyid'].Value
        Version = $payloadMatch.Groups['version'].Value
        SourceCommit = $payloadMatch.Groups['source'].Value
        SourceTreeId = $payloadMatch.Groups['tree'].Value
        CompletedAtUtc = $payloadMatch.Groups['completed'].Value
        EvidenceHash = $payloadMatch.Groups['evidence'].Value
        SourceArchiveSha256 = $payloadMatch.Groups['archive'].Value
        SourceManifestSha256 = $payloadMatch.Groups['manifest'].Value
        DFileSystem = $payloadMatch.Groups['dfilesystem'].Value
        DResult = $payloadMatch.Groups['dresult'].Value
        GFileSystem = $payloadMatch.Groups['gfilesystem'].Value
        GResult = $payloadMatch.Groups['gresult'].Value
    }
}

function Get-ExactJsonObjectProperties {
    param(
        [Parameter(Mandatory)][System.Text.Json.JsonElement]$Element,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string[]]$AllowedProperties
    )

    if ($Element.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
        throw "Release preflight evidence property '$Path' must be a JSON object."
    }

    $properties = [Collections.Generic.Dictionary[string, System.Text.Json.JsonElement]]::new([StringComparer]::Ordinal)
    $seenNames = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($property in $Element.EnumerateObject()) {
        $existingName = $null
        if ($seenNames.TryGetValue($property.Name, [ref]$existingName)) {
            if ($existingName -ceq $property.Name) {
                throw "Release preflight evidence object '$Path' contains duplicate property '$($property.Name)'."
            }
            throw "Release preflight evidence object '$Path' contains case-colliding properties '$existingName' and '$($property.Name)'."
        }
        $seenNames.Add($property.Name, $property.Name)
        if (-not ($AllowedProperties -ccontains $property.Name)) {
            throw "Release preflight evidence object '$Path' contains unknown property '$($property.Name)'."
        }
        $properties.Add($property.Name, $property.Value.Clone())
    }

    foreach ($allowedProperty in $AllowedProperties) {
        if (-not $properties.ContainsKey($allowedProperty)) {
            throw "Release preflight evidence object '$Path' is missing required property '$allowedProperty'."
        }
    }
    return ,$properties
}

function Get-RequiredJsonString {
    param(
        [Parameter(Mandatory)][Collections.Generic.Dictionary[string, System.Text.Json.JsonElement]]$Properties,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Path
    )

    $element = $Properties[$Name]
    if ($element.ValueKind -ne [System.Text.Json.JsonValueKind]::String) {
        throw "Release preflight evidence property '$Path.$Name' must be a JSON string."
    }
    return $element.GetString()
}

function Get-ReleasePreflightEvidenceClaims {
    param(
        [Parameter(Mandatory)][string]$EvidencePath,
        [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')][string]$ExpectedVersion,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$ExpectedSourceCommit,
        [Parameter(Mandatory)][ValidatePattern('^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$')][string]$ExpectedCompletedAtUtc
    )

    if (-not (Test-Path -LiteralPath $EvidencePath -PathType Leaf)) {
        throw "Release preflight evidence was not found at '$EvidencePath'."
    }
    $evidenceJson = [IO.File]::ReadAllText($EvidencePath)
    try {
        $document = [System.Text.Json.JsonDocument]::Parse($evidenceJson)
    }
    catch {
        throw "Release preflight evidence at '$EvidencePath' is not valid JSON: $($_.Exception.Message)"
    }
    try {
        $evidence = Get-ExactJsonObjectProperties -Element $document.RootElement -Path '$' -AllowedProperties @(
            'Schema',
            'Version',
            'SourceCommit',
            'SourceTreeId',
            'SourceArchiveSha256',
            'SourceManifestSha256',
            'Branch',
            'CompletedAtUtc',
            'Workstation',
            'Volumes',
            'Contracts'
        )
        $schema = Get-RequiredJsonString -Properties $evidence -Name 'Schema' -Path '$'
        $version = Get-RequiredJsonString -Properties $evidence -Name 'Version' -Path '$'
        $sourceCommit = Get-RequiredJsonString -Properties $evidence -Name 'SourceCommit' -Path '$'
        $sourceTreeId = Get-RequiredJsonString -Properties $evidence -Name 'SourceTreeId' -Path '$'
        $sourceArchiveSha256 = Get-RequiredJsonString -Properties $evidence -Name 'SourceArchiveSha256' -Path '$'
        $sourceManifestSha256 = Get-RequiredJsonString -Properties $evidence -Name 'SourceManifestSha256' -Path '$'
        $branch = Get-RequiredJsonString -Properties $evidence -Name 'Branch' -Path '$'
        $completedAtUtc = Get-RequiredJsonString -Properties $evidence -Name 'CompletedAtUtc' -Path '$'
        $workstation = Get-RequiredJsonString -Properties $evidence -Name 'Workstation' -Path '$'

        $volumes = Get-ExactJsonObjectProperties -Element $evidence['Volumes'] -Path '$.Volumes' -AllowedProperties @('D', 'G')
        $volumeClaims = [ordered]@{}
        foreach ($drive in @('D', 'G')) {
            $volume = Get-ExactJsonObjectProperties -Element $volumes[$drive] -Path "$.Volumes.$drive" -AllowedProperties @('FileSystem', 'DriveType')
            $volumeClaims[$drive] = [pscustomobject]@{
                FileSystem = Get-RequiredJsonString -Properties $volume -Name 'FileSystem' -Path "$.Volumes.$drive"
                DriveType = Get-RequiredJsonString -Properties $volume -Name 'DriveType' -Path "$.Volumes.$drive"
            }
        }
        $contracts = Get-ExactJsonObjectProperties -Element $evidence['Contracts'] -Path '$.Contracts' -AllowedProperties @(
            'ReleaseVersion',
            'WindowsPublisherMetadataPortable',
            'WindowsPublisherMetadataCrossFilesystem'
        )
        $contractClaims = [ordered]@{}
        foreach ($contract in @('ReleaseVersion', 'WindowsPublisherMetadataPortable', 'WindowsPublisherMetadataCrossFilesystem')) {
            $contractClaims[$contract] = Get-RequiredJsonString -Properties $contracts -Name $contract -Path '$.Contracts'
        }
    }
    finally {
        $document.Dispose()
    }

    if ($schema -cne $script:EvidenceSchema) {
        throw "Release preflight evidence schema '$schema' is not '$script:EvidenceSchema'."
    }
    $parsedCompletedAt = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParseExact(
            $completedAtUtc,
            'yyyy-MM-ddTHH:mm:ssZ',
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal,
            [ref]$parsedCompletedAt)) {
        throw 'Release preflight evidence property CompletedAtUtc must use UTC format yyyy-MM-ddTHH:mm:ssZ.'
    }
    $canonicalCompletedAtUtc = $parsedCompletedAt.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    if ($version -cne $ExpectedVersion -or $sourceCommit -ine $ExpectedSourceCommit -or $canonicalCompletedAtUtc -cne $ExpectedCompletedAtUtc -or $branch -cne 'main') {
        throw 'Release preflight evidence version, source commit, completion time, or branch does not match the signing request.'
    }
    foreach ($hashClaim in @(
            [pscustomobject]@{ Name = 'SourceArchiveSha256'; Value = $sourceArchiveSha256 },
            [pscustomobject]@{ Name = 'SourceManifestSha256'; Value = $sourceManifestSha256 }
        )) {
        if ($hashClaim.Value -notmatch '^[0-9a-fA-F]{64}$') {
            throw "Release preflight evidence property '$($hashClaim.Name)' must be a SHA-256 value."
        }
    }
    if ($sourceTreeId -notmatch '^[0-9a-fA-F]{40}$') {
        throw 'Release preflight evidence property SourceTreeId must be a full Git tree id.'
    }
    if ([string]::IsNullOrWhiteSpace($workstation)) {
        throw 'Release preflight evidence property Workstation must be non-empty.'
    }
    foreach ($volumeClaim in @(@('D', 'NTFS'), @('G', 'ReFS'))) {
        $drive = $volumeClaim[0]
        $filesystem = $volumeClaim[1]
        if ($volumeClaims[$drive].FileSystem -cne $filesystem -or $volumeClaims[$drive].DriveType -cne 'Fixed') {
            throw "Release preflight evidence must record $drive`: FileSystem '$filesystem' and DriveType 'Fixed'."
        }
    }
    foreach ($contractClaim in $contractClaims.GetEnumerator()) {
        if ($contractClaim.Value -cne 'passed') {
            throw "Release preflight evidence must record contract '$($contractClaim.Key)' as passed."
        }
    }

    [pscustomobject]@{
        SourceTreeId = $sourceTreeId.ToLowerInvariant()
        SourceArchiveSha256 = $sourceArchiveSha256.ToLowerInvariant()
        SourceManifestSha256 = $sourceManifestSha256.ToLowerInvariant()
        DFileSystem = 'NTFS'
        DResult = 'passed'
        GFileSystem = 'ReFS'
        GResult = 'passed'
    }
}

function Get-ReleasePreflightSigningCertificate {
    param(
        [Parameter(Mandatory)][string]$Thumbprint,
        [Parameter(Mandatory)][System.Security.Cryptography.X509Certificates.X509Certificate2]$PinnedPublicCertificate
    )

    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new('My', 'CurrentUser')
    try {
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
        $matches = @($store.Certificates.Find(
                [System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
                $Thumbprint,
                $false))
        $certificate = $matches | Where-Object { $_.HasPrivateKey } | Select-Object -First 1
        if (-not $certificate) {
            throw "No private signing certificate matching thumbprint '$Thumbprint' exists in Cert:\CurrentUser\My."
        }
        if ((Get-ReleasePreflightKeyId -Certificate $certificate) -cne (Get-ReleasePreflightKeyId -Certificate $PinnedPublicCertificate)) {
            throw 'The requested signing certificate does not match the pinned release-attestation public certificate.'
        }
        return $certificate
    }
    finally {
        $store.Close()
    }
}

function New-SignedReleasePreflightAttestation {
    param(
        [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')][string]$Version,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$SourceCommit,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$SourceTreeId,
        [Parameter(Mandatory)][ValidatePattern('^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$')][string]$CompletedAtUtc,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$EvidenceHash,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$SourceArchiveSha256,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$SourceManifestSha256,
        [Parameter(Mandatory)][ValidateSet('NTFS')][string]$DFileSystem,
        [Parameter(Mandatory)][ValidateSet('passed')][string]$DResult,
        [Parameter(Mandatory)][ValidateSet('ReFS')][string]$GFileSystem,
        [Parameter(Mandatory)][ValidateSet('passed')][string]$GResult,
        [Parameter(Mandatory)][string]$SigningCertificateThumbprint,
        [Parameter(Mandatory)][string]$PublicCertificatePath
    )

    $pinnedCertificate = Get-ReleasePreflightPublicCertificate -PublicCertificatePath $PublicCertificatePath
    try {
        $signingCertificate = Get-ReleasePreflightSigningCertificate -Thumbprint $SigningCertificateThumbprint -PinnedPublicCertificate $pinnedCertificate
        try {
            $payload = New-ReleasePreflightAttestationPayload `
                -Version $Version `
                -SourceCommit $SourceCommit `
                -SourceTreeId $SourceTreeId `
                -CompletedAtUtc $CompletedAtUtc `
                -EvidenceHash $EvidenceHash `
                -SourceArchiveSha256 $SourceArchiveSha256 `
                -SourceManifestSha256 $SourceManifestSha256 `
                -DFileSystem $DFileSystem `
                -DResult $DResult `
                -GFileSystem $GFileSystem `
                -GResult $GResult `
                -KeyId (Get-ReleasePreflightKeyId -Certificate $pinnedCertificate)
            $rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($signingCertificate)
            if (-not $rsa) {
                throw 'The pinned release-attestation certificate does not expose an RSA private key.'
            }
            try {
                $signature = $rsa.SignData(
                    [Text.Encoding]::UTF8.GetBytes($payload),
                    [System.Security.Cryptography.HashAlgorithmName]::SHA256,
                    [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
                return New-ReleasePreflightAttestationEnvelope -Payload $payload -Signature $signature
            }
            finally {
                $rsa.Dispose()
            }
        }
        finally {
            $signingCertificate.Dispose()
        }
    }
    finally {
        $pinnedCertificate.Dispose()
    }
}

function Assert-SignedReleasePreflightAttestation {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Attestation,
        [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')][string]$ExpectedVersion,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$ExpectedSourceCommit,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$ExpectedSourceTreeId,
        [Parameter(Mandatory)][string]$PublicCertificatePath,
        [Parameter()][string]$NowUtc = [DateTimeOffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'),
        [Parameter()][ValidateRange(1, 168)][int]$MaximumAgeHours = 24
    )

    if ([string]::IsNullOrWhiteSpace($Attestation)) {
        throw 'The required prepared-workstation signed release preflight attestation is missing.'
    }

    $parsed = ConvertFrom-ReleasePreflightAttestation -Attestation $Attestation
    $pinnedCertificate = Get-ReleasePreflightPublicCertificate -PublicCertificatePath $PublicCertificatePath
    try {
        if ($parsed.KeyId -cne (Get-ReleasePreflightKeyId -Certificate $pinnedCertificate)) {
            throw 'The release preflight attestation key id does not match the pinned public certificate.'
        }
        $rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($pinnedCertificate)
        try {
            if (-not $rsa.VerifyData(
                    $parsed.PayloadBytes,
                    $parsed.Signature,
                    [System.Security.Cryptography.HashAlgorithmName]::SHA256,
                    [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)) {
                throw 'The release preflight attestation signature is invalid for the pinned public certificate.'
            }
        }
        finally {
            $rsa.Dispose()
        }
    }
    finally {
        $pinnedCertificate.Dispose()
    }

    if ($parsed.Version -cne $ExpectedVersion) {
        throw "The release preflight attestation version '$($parsed.Version)' does not match '$ExpectedVersion'."
    }
    if ($parsed.SourceCommit -ine $ExpectedSourceCommit) {
        throw "The release preflight attestation source '$($parsed.SourceCommit)' does not match '$ExpectedSourceCommit'."
    }
    if ($parsed.SourceTreeId -ine $ExpectedSourceTreeId) {
        throw "The release preflight attestation source tree '$($parsed.SourceTreeId)' does not match '$ExpectedSourceTreeId'."
    }
    if ($parsed.DFileSystem -cne 'NTFS' -or $parsed.DResult -cne 'passed' -or $parsed.GFileSystem -cne 'ReFS' -or $parsed.GResult -cne 'passed') {
        throw 'The release preflight attestation must contain passed D: NTFS and G: ReFS publisher-metadata claims.'
    }

    $completedAt = [DateTimeOffset]::MinValue
    $now = [DateTimeOffset]::MinValue
    foreach ($timestamp in @(
            [pscustomobject]@{ Name = 'attestation completion'; Value = $parsed.CompletedAtUtc; Parsed = [ref]$completedAt },
            [pscustomobject]@{ Name = 'validation time'; Value = $NowUtc; Parsed = [ref]$now }
        )) {
        if (-not [DateTimeOffset]::TryParseExact(
                $timestamp.Value,
                'yyyy-MM-ddTHH:mm:ssZ',
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::AssumeUniversal,
                $timestamp.Parsed)) {
            throw "The $($timestamp.Name) '$($timestamp.Value)' must use UTC format yyyy-MM-ddTHH:mm:ssZ."
        }
    }

    $age = $now.ToUniversalTime() - $completedAt.ToUniversalTime()
    if ($age -gt [TimeSpan]::FromHours($MaximumAgeHours)) {
        throw "The prepared-workstation release preflight attestation is stale (age $([Math]::Round($age.TotalHours, 2)) hours; maximum $MaximumAgeHours hours)."
    }
    if ($age -lt [TimeSpan]::FromMinutes(-5)) {
        throw 'The prepared-workstation release preflight attestation completion time is more than five minutes in the future.'
    }

    return $parsed
}

Export-ModuleMember -Function @(
    'Assert-SignedReleasePreflightAttestation',
    'ConvertFrom-ReleasePreflightAttestation',
    'Get-ReleasePreflightEvidenceClaims',
    'Get-ReleasePreflightKeyId',
    'New-ReleasePreflightAttestationEnvelope',
    'New-ReleasePreflightAttestationPayload',
    'New-SignedReleasePreflightAttestation'
)
