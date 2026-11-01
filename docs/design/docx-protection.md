# ADR B5-CRYPTO: explicit DOCX package protection

This is a historical proposal. The owner subsequently cancelled encryption,
decryption and signatures; no implementation or publication is authorized by it.

Design proposal for [#30](https://github.com/kirakosyan/word.net/issues/30),
2026-10-17. Password-protected documents remain unsupported. No cryptography,
password API, new dependency or unsupported-feature diagnostic changes here.
Implementation needs a separate scope-policy decision and security review.

## First profile and alternatives

Propose explicit encryption/decryption of **non-macro Transitional DOCX** using
MS-OFFCRYPTO Agile encryption (EncryptionInfo version 4.4) in a Compound File Binary
container with `EncryptionInfo` and `EncryptedPackage` streams. Encryption protects
the whole ZIP package; it is not ZIP entry encryption. Choose one allowlisted
combination for reading and writing: AES-256, CBC chaining, SHA-512, 16-byte salts and
block size, 64-byte hashes and password-derived key encryptor. Verify descriptor
consistency, encrypted lengths, verifier and the mandatory data-integrity HMAC before
releasing any plaintext. Follow the published 4,096-byte segment/IV rules and
little-endian unencrypted package-length prefix; do not substitute a generic CBC file
format, invent padding rules or reuse keys/salts between operations.

Require a single password key encryptor. Reject Standard/Extensible encryption,
RC4/XOR/legacy DOC, AES-128/192, SHA-1/other hash combinations, CFB cipher chaining,
certificate/IRM key encryptors, multiple encryptors, unsupported versions, missing
integrity records, macros and non-DOCX payloads. Reject encrypted ODT/PDF/ebooks under
their existing profiles. Expanding this list needs new test vectors and a decision.

Password encryption is distinct from `w:documentProtection` editing restrictions,
which are not confidentiality, and from digital signatures, which this design does
not verify or create. Decryption must not bypass editing restrictions. Encryption
preflight rejects packages containing signature parts or editing restrictions until
their preserve/reject policy is explicitly implemented; do not claim they are enforced
by a password or silently remove them. No password recovery, dictionary search,
credential storage, UI prompting or service integration is included.

Use BCL `Aes`, `SHA512`, `HMACSHA512`, `RandomNumberGenerator` and existing OpenMcdf
3.3.0 for bounded CFB storage, with specification-authored envelope logic. Both BCL
and the existing unmodified MPL-2.0 OpenMcdf dependency retain the required portable targets;
preserve its existing notices and corresponding-source link. No new native crypto dependency or production package is selected. Actual Windows/Linux
execution on all supported architectures remains mandatory, including legacy crypto
providers. Do not assume compiling BCL calls proves provider/FIPS compatibility.
Open XML SDK is a structural test tool, not an encryption engine. External MIT
`msoffcrypto-tool` and Apache POI (Apache-2.0) are independent test producers/readers,
not runtime dependencies; pin their versions/licenses/hashes before use. Re-evaluate
maintained crypto libraries if BCL portability fails rather than writing primitives.

## Proposed explicit API

Add an opt-in utility in core only after approval; the existing managed dependencies
already supply the boundary, so a speculative provider project is unnecessary. Names
below are proposed and unavailable; no password is added to `ConversionOptions`:

```text
DocxProtection.Decrypt(source, destination, char[] password,
    ProtectionOptions, cancellationToken) -> ProtectionResult
DocxProtection.Encrypt(source, destination, char[] password,
    ProtectionOptions, cancellationToken) -> ProtectionResult
```

Use new overloads and provider-neutral types identically on all five TFMs. Passwords
are required caller-supplied character arrays; null is a missing-password error and
an empty password is an explicit empty password, subject to caller policy. Do not
normalize, trim, case-fold or log it. Bound at 1,024 UTF-16 code units before copying;
reject ill-formed surrogate input and encode exactly as specified. Copy caller arrays
for the duration of the operation, never change/dispose caller memory or retain a
password in the result/session. Callers must keep their array stable during admission.
No callback retries or implicit fallback to unprotected output.

The caller explicitly decrypts into its chosen bounded in-memory stream, then uses
ordinary DOCX APIs. Encryption accepts a validated original DOCX ZIP and wraps its
bytes without a semantic round trip that could erase features. Structural/resource
preflight is not a claim that every DOCX feature is renderable. `WordConverter` and
`WordDocument.Load` remain unsupported for encrypted source until separately scoped;
no magic-format/password probing is added silently.

## Threat and resource model

Treat the container, descriptor, work factor and claimed plaintext size as hostile.
Defend against offset/sector-chain cycles, integer overflow, XML entity/depth attacks,
base64 bombs, truncated ciphertext, KDF denial of service, padding/integrity errors,
ZIP bombs after decryption and plaintext leakage before authentication. File encryption
does not prevent offline password guessing, malware inside a permitted file, host
memory inspection, compromised callers, swap/crash dumps or later plaintext copies.
Use host isolation and request rate limits for externally supplied files.

Proposed `ProtectionOptions` owns a `ConversionOptions` resource snapshot and these
additional **local design defaults**, not normative MS-OFFCRYPTO values:

| Budget | Proposed policy |
| --- | --- |
| Descriptor | At most 64 KiB, nesting at most 16 or the configured nesting limit, one allowlisted encryptor; bound decoded fields before allocation. |
| KDF spin count | Read 100,000–1,000,000 inclusive; write 100,000. Values below/above policy fail; never clamp or silently weaken a file. Raising the admission ceiling requires caller opt-in and qualification. |
| Aggregate KDF work | At most 3,000,000 hash iterations across all derivations for one operation. Count actual derivations, not only descriptor `spinCount`; fail before work if the admitted total would exceed the budget. |
| CFB | Input bytes and stream count bounded by input/expanded-byte and archive-entry analogues; traverse each sector at most once per validated chain, reject overlaps/cycles/out-of-file links. Bound all named streams in aggregate. |
| Cipher/plaintext | Cipher input uses `MaximumInputBytes`; claimed and actual plaintext use `MaximumExpandedBytes`; final ciphertext/plaintext output uses `MaximumOutputBytes`. Account for envelope overhead before encrypting. |
| Inner ZIP | Apply entry/path/actual expansion/XML/character limits after successful authentication; resource limits never relax with fidelity mode. |

Check cancellation before KDF, at most every 1,024 hash iterations, between 4,096-byte
segments, during HMAC processing and before final copy. Crypto primitive/provider calls
can be synchronous; process isolation owns hard deadlines. A legal file exceeding a
local work budget is unsupported under that configured policy, not corrupt data.

Use cryptographically random independent salts, verifier material, HMAC key and
package key for every encryption, with known-answer/randomness tests. Encryption
output must remain nondeterministic even with a fixed document clock; no public test
RNG or seed option. Fixed-time compare for equal-sized verifier/HMAC buffers needs a
reviewed compatibility implementation on legacy TFMs; do not use ordinary early-exit
sequence equality. This does not promise constant-time whole-file execution.

## Secret lifetime, failures and output safety

Keep copied password bytes, derived keys, verifier/HMAC material and plaintext staging
per operation; clear owned mutable buffers in `finally`, release crypto objects and
never cache derived material. Avoid creating immutable password strings. BCL/provider
internals, GC copies, caller arrays and process dumps mean managed code **cannot
guarantee secret erasure**. Do not use that phrase as a security guarantee. No default
temporary files; plaintext remains bounded in memory. Caller output may intentionally
contain plaintext and is the caller's responsibility.

Proposed error categories distinguish missing credentials, unsupported profile,
malformed envelope, local work/resource limit and authentication failure. Wrong
password and verifier/HMAC mismatch share a content-free authentication failure;
never include attempted passwords, plaintext, authors, salts/keys or raw descriptor
bytes in exceptions, diagnostics, telemetry or filenames. Only safe codes, stage and
counts are returned. Do not expose a password-validity probe or retry API.

Authenticate the **entire encrypted package** before parsing plaintext as ZIP or copying
to caller output. Then validate declared/actual plaintext length and package structure.
Use bounded private staging for both directions. Missing/wrong password, integrity,
malformed input, excessive work, cancellation before copy and serialization failure
leave a sentinel destination unchanged. Final-copy cancellation/I/O can leave partial
output under the existing stream contract; callers needing transactional files must
use their own replace-on-success wrapper. Streams stay open, start at current positions
and cannot reference the same resource. Never emit a partial unprotected fallback.

## Follow-ups and acceptance

1. **E1 scope/security review**: approve the exact profile, threat/work policy and API;
   review specification interpretation and independent known-answer vectors. Gate
   any implementation on this and the explicit AGENTS deferred-scope update.
2. **E2 reader**, depends on E1: CFB/descriptor admission, password verifier, HMAC,
   bounded decryption and authenticated ZIP preflight; all negative sentinel tests.
3. **E3 writer**, depends on E2: random encryption of independently authored DOCX,
   independent decryption/ZIP-byte equality and external opening with correct password.
4. **E4 qualification**, depends on E3: expert implementation/security review,
   mutation/fuzz campaigns, every source/packed TFM/runtime lane, logging and lifetime
   review, docs/API/diagnostic/sample updates. No general cryptographic certification
   or minimum-runtime qualification follows from passing one machine's tests.

The [acceptance/provenance plan](acceptance.md) includes independently produced files,
wrong/missing passwords, descriptor/work extremes, corruption at each segment and
untouched-output checks. This design does not change current rejection behavior.

## References

- [MS-OFFCRYPTO published specification](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-offcrypto/), revision 14.0 dated 2026-02-17 at design review; Agile sections 2.3.4.10–2.3.4.15.
- [Agile data-integrity generation](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-offcrypto/63d9c262-82b9-4fa3-a06d-d087b93e3b00).
- [Existing dependency policy](../dependencies.md), [stream/hosting limits](../hosting.md), [msoffcrypto-tool MIT license](https://github.com/nolze/msoffcrypto-tool/blob/master/LICENSE.txt).
