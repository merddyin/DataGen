# DataGen Release Attestation Trust

`datagen-release-preflight-attestation.cer` is the pinned RSA public certificate used by the GitHub release workflow to verify a prepared-workstation preflight attestation. It is a public certificate only. It is not a certificate-authority trust chain, does not use Windows certificate-chain validation, and does not establish the identity of a person. GitHub Actions accepts an attestation only when its signature verifies against this exact tracked certificate and the signed payload matches the requested version, checked-out source commit and tree, completion time, evidence SHA-256, source archive SHA-256, source manifest SHA-256, and passed D: NTFS/G: ReFS publisher-metadata claims.

Before signing, the prepared workstation parses the exact preflight evidence schema and verifies its identity fields against the signing request. It resolves the clean `main` commit and tree, creates a deterministic archive from that committed object, and runs source-dependent contracts from an isolated snapshot below the preflight output root. The D: and G: metadata probes instead use explicit, validated roots on their respective volumes; they do not inherit the snapshot volume. Archive and snapshot identities are checked again after execution, and the live branch, commit, tree, version, and clean state must still match at completion.

The matching private key belongs only in `Cert:\CurrentUser\My` on the prepared release workstation. It must be non-exportable. Do not create a PFX, PEM private key, password, or private-key backup in this repository, `D:`, `E:`, `G:`, artifacts, logs, or a ticket attachment.

This boundary prevents transient working-tree edits from changing the committed source under test. It does not defend against an attacker running as the prepared release account who can tamper with the Git object database, signing process memory, or private-key operations. Workstation and account integrity remain required operational controls.

## Current pinned public identity

| Field | Value |
| --- | --- |
| Subject | `CN=DataGen Release Preflight Attestation` |
| SHA-1 certificate thumbprint | `49EB44CA462256931BB4BF018F0C2E02ABAD628B` |
| SHA-256 key id | `sha256-13f7231e55ee72d0421724d950c9ea43ed0584b5fe260784bc0d980296716bc0` |
| Intended use | RSA SHA-256 release-preflight attestation signatures |

## Rotation and recovery

Rotate before expiry, when the prepared workstation changes, or immediately on suspected key compromise. Create the replacement as a new non-exportable RSA signing certificate in the prepared workstation's `CurrentUser\My` store, export only its public `.cer` to replace this file, and submit that change through the normal reviewed `main` workflow. After the public certificate change is merged, rerun preflight from the resulting clean `main` commit so the attestation is signed by the matching replacement private key.

For recovery after private-key loss, do not weaken or bypass verification. Create a replacement key pair and update the pinned public certificate as a reviewed change. For suspected compromise, remove the old certificate from the prepared workstation store, replace the tracked public certificate, and treat previously issued but unused attestations as invalid. No release may be published until the new pinned public certificate is on `main` and a fresh clean-worktree preflight has completed.
