# Deterministic Generation Evidence

DataGen reproducibility evidence uses two controlled generation invocations and a separate comparison receipt.

## Trust boundary

This is trusted-operator, unsigned QA evidence. It detects accidental reruns, stale or copied candidates, substituted tools/inputs, and ordinary process mistakes. It is **not cryptographic tamper attestation**, does not authenticate the operator, and cannot prove that a person with write access did not forge contracts, sidecars, or receipts. Parent challenges make accidental copying and partial sidecar editing visible; they are public correlation values, not secrets or signatures. No signing keys or key infrastructure are part of this design.

## Generation invocation contract

Use `scripts/new-generation-invocation-contract.ps1` to atomically issue separate `candidate-1` and `candidate-2` parent contracts before either run. Then run `scripts/invoke-deterministic-generation.ps1` once for each empty candidate root with its assigned contract. The wrapper invokes the supplied generation script with `OutputPath`, `ScenarioPath`, `Seed`, and `GeneratedAt`, then atomically writes `.datagen-generation-provenance.json` only after validation succeeds.

The sidecar schema version is `1.0.0`. It records:

- A non-empty, unique invocation ID.
- UTC invocation start/completion times and the wrapper process ID/name.
- The process start time in UTC text and ticks. Process identity is the PID plus process-start ticks, so PID reuse does not collapse two processes into one identity.
- Wrapper identity plus the generation script's portable logical identity, byte count, and SHA-256.
- PowerShell, .NET SDK, framework, operating-system, and architecture identity.
- DataGen version, branch, commit, dirty state, canonical source-tree identity, and source inclusion-set version.
- Scenario display name and SHA-256, seed, and fixed generation time.
- Full-path, explicitly supplied Git and .NET executable identities. Canonical evidence records only logical labels, hashes, sizes, and versions.
- The public canonical invocation argument digest and a safe structured argument vector governed by explicit sensitive option names, option-name patterns, and/or forwarded-vector indices.
- The assigned parent challenge ID/nonce and parent-contract hash.
- Payload file count, byte count, and canonical aggregate SHA-256.

The sidecar never stores the candidate root, scenario checkout path, or expected generation-script absolute path. Generation scripts inside the repository use `repo:<relative-path>`; external scripts use `external:<file-name>`, with SHA-256 and byte count providing content identity. The candidate payload inventory excludes only the root sidecar itself.

`GenerationArgumentList` accepts ordered `-Name value` or `-Name=value` pairs; additional positional values are preserved. It cannot override `OutputPath`, `ScenarioPath`, `Seed`, or `GeneratedAt`. The canonical vector uses `<candidate-root>` and `<scenario-path>` placeholders so independent roots have the same digest. Its remaining values are hashed as compact UTF-8 JSON under contract `datagen-generation-invocation-v1`.

Sensitive inputs selected by `SensitiveGenerationArgumentName`, `SensitiveGenerationArgumentPattern`, or `SensitiveGenerationArgumentIndex` retain their position while replacing the value with the constant `<redacted-sensitive-input>` **before hashing or serialization**. Different dictionary-attack candidate values therefore produce the same digest and never appear in contracts, sidecars, or receipts. `sensitiveInputsExcludedFromReproducibility=true` explicitly records that equality is not claimed for those values. Public deterministic arguments remain digest-bound.

The wrapper hashes the generation script, scenario, wrapper, and supplied Git/.NET executables before and after generation. It also checks versions, source identity, runtime identity, and public invocation inputs against the parent contract. Any change fails the invocation and emits no success sidecar. Evidence-critical Git and .NET calls use the supplied fully qualified executable paths; ambient aliases and `PATH` resolution are not used.

## Canonical aggregate contract

Payload files use normalized `/` relative paths and ordinal path order. Each inventory entry contains the relative path, byte length, and lowercase SHA-256. Size and SHA-256 come from the same read-only, single-open stream. The stream excludes concurrent writers where the platform honors file sharing, and pre/during/post length and last-write metadata must remain unchanged. The aggregate is the lowercase SHA-256 of the compact UTF-8 JSON array of those ordered entries.

Candidate roots and every descendant are rejected if marked as a filesystem reparse point, symlink, or junction. Included source files and every ancestor back to the source root receive the same check; ignored and excluded paths are outside the source set. Hard links are ordinary files, not reparse points. Sidecars are read once into a bounded byte buffer; parsing and SHA-256 use that exact buffer, followed by metadata stability checks.

Inventories default to at most 100,000 files, 1,024 characters per relative path, and 4 TiB total bytes. Canonical JSON defaults to 16 MiB, while parent contracts and sidecars use 4 MiB bounds. JSON publication writes a uniquely named temporary file in the destination directory, flushes it to disk, closes it, atomically moves/replaces the destination, and cleans residual temporary files.

The trusted-operator boundary still matters for filesystem races. Single-open reads and sharing restrictions prevent or detect ordinary concurrent mutation, and length/last-write metadata is checked before, during, and after hashing. A sufficiently privileged actor coordinating atomic replacement around those observations is outside this unsigned QA contract.

The source-tree inclusion set is versioned as:

`git-ls-files-z-v2:tracked+untracked-nonignored;exclude-prefix-ordinal=.beads/;path=/;order=ordinal`

It contains every NUL-delimited path returned by `git ls-files -z --cached --others --exclude-standard`, except paths beginning with the exact case-sensitive ordinal prefix `.beads/`. NUL parsing preserves embedded newlines. Paths are normalized to `/`, sorted with `StringComparer.Ordinal`, and hashed with the same inventory algorithm. Git metadata, ignored build products, operational lowercase bead-store files, and candidate evidence outside the repository are not included.

`scripts/DeterminismEvidence.psm1` is the single implementation used by the invocation and receipt tools. `tests/DeterminismEvidence.Tests.ps1` independently checks input-order invariance, ordinal output ordering, the inclusion-set identifier, and the `.beads/**` exclusion.

## Receipt validation contract

Run `scripts/new-determinism-receipt.ps1` only after both generation invocations complete. Receipt schema `2.0.0` requires:

- Two existing, distinct resolved candidate roots.
- Two valid sidecars with distinct invocation IDs and distinct PID/process-start identities.
- Two distinct preissued parent challenges, each matching its candidate sidecar and current expected environment.
- An explicit `ExpectedGenerationScriptPath` whose freshly read logical identity, byte count, and SHA-256 match both sidecars.
- An explicit expected invocation digest consistent with the expected argument/redaction contract and matching both sidecars.
- Sidecar source, version, runtime, and inputs equal to the receipt tool's current expected identities.
- Each sidecar payload hash/counts equal to a fresh inventory of its candidate root.
- Equal payload hashes/counts between candidates for `passed: true`.

The canonical receipt uses `candidate-1` and `candidate-2` labels and relative artifact inventories. It does not contain either absolute candidate root. The receipt output must be outside both candidate roots.

Copying a candidate and sidecar fails its candidate-specific parent challenge. Editing only copied GUID/PID values still fails that binding. A writer can also edit the public challenge and contract because nothing is signed; that is explicitly within the documented unsigned-tampering limitation, not evidence of a controlled independent run. Review still requires trusted operator execution and retained command receipts.

Final release evidence must be generated only after the intended version and commit are fixed. Sidecars and receipts from an earlier dirty source identity are test or review evidence, not release proof.
