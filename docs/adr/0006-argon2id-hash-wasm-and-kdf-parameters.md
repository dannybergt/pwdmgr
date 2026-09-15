# ADR-0006: Argon2id via `hash-wasm`, KDF parameters m=64 MiB / t=3 / p=4, minimum m=19 MiB / t=2 / p=1

Status: accepted  
Date: 2026-09-15

## Context

The zero-knowledge design (ADR-0002, product plan §9/§10) derives every user's key-encryption
key (KEK) from the master passphrase in the browser: passphrase → Argon2id → KEK → wraps the
user's private key → vault keys → per-secret DEKs. The server only ever stores the KDF
parameters and salt next to the ciphertext. Two decisions were open:

1. Which Argon2id implementation runs in the browser (WebCrypto has none).
2. Which parameters new enrolments use, and which minimum the server enforces on stored
   parameters so tampered keyring metadata cannot downgrade the KDF (slice #5 validates this).

STATE.md carried OWASP's guidance (m=64 MiB, t=3, p=4) as a placeholder pending a benchmark.

## Decision

### Library: `hash-wasm` 4.12.0 (MIT)

- Argon2id with the memory-hard fill in WASM (the author's own C, modelled on the reference
  implementation) and H0 / H′ / initial lane blocks computed in JS on top of its BLAKE2b WASM;
  supports `parallelism`; ~50 KB WASM shipped base64-inline in the JS bundle, so no separate
  asset and no `WebAssembly.instantiateStreaming` cross-origin issue. Zero runtime
  dependencies.
- Proven bit-exact against seven Argon2 reference-suite vectors (`src/crypto/kdf.test.ts`)
  including m=64 MiB, and against an independent oracle for the two frozen own vectors
  (argon2-cffi → reference libargon2; command recorded in the test file), in Node 24 and in
  headless Chromium 153.
- Maintenance check (§10): last release 2024-11-19, `npm audit` clean, no known CVEs, single
  maintainer. Risk accepted because the API surface we use is one function and the test suite
  pins its output; a replacement would have to reproduce the same vectors.
- Rejected: `libsodium-wrappers` (Argon2id only with p=1, ~4× larger), `@noble/hashes` Argon2
  (pure JS, far too slow at 64 MiB), `argon2-browser` (fragile WASM loading, doubtful
  maintenance).
- CSP: `script-src` gains `'wasm-unsafe-eval'` — only that, never `'unsafe-eval'`.

### Parameters

| | memory | iterations | parallelism |
|---|---|---|---|
| `KDF_DEFAULT` (new enrolments) | 64 MiB (65536 KiB) | 3 | 4 |
| `KDF_MINIMUM` (server-enforced floor) | 19 MiB (19456 KiB) | 2 | 1 |
| `KDF_MAXIMUM` (client sanity ceiling) | 1 GiB | 16 | 16 |

`KDF_DEFAULT` is RFC 9106 §4's second recommended configuration and OWASP's current guidance.
`KDF_MINIMUM` is OWASP's smallest recommended configuration; anything below it is rejected
client-side (`deriveKek`) and server-side (slice #5). `KDF_MAXIMUM` only stops tampered
keyring metadata from turning every unlock into a tab-crashing allocation (user-local DoS);
values must be integers on every axis.

### Benchmark (this host: 16-core Linux workstation `dev-claude`, shared)

Node 24.21 (`npm run bench:kdf`, p50 of 3 runs):

| m | t=2 p=1 | t=2 p=4 | t=3 p=1 | t=3 p=4 | t=4 p=1 | t=4 p=4 |
|---|---|---|---|---|---|---|
| 32 MiB | 222 ms | 238 ms | 276 ms | 250 ms | 322 ms | 352 ms |
| 64 MiB | 346 ms | 347 ms | 566 ms | **551 ms** | 679 ms | 628 ms |
| 128 MiB | 1273 ms | 933 ms | 1285 ms | 1710 ms | 2310 ms | 1598 ms |

Headless Chromium 153 (Playwright image, `bench/kdf.html`, p50 of 5 runs; the host was
shared with a concurrent .NET build and other sessions, so the spread is wide — `min` is the
better indicator of uncontended cost):

| m | t=2 p=1 | t=2 p=4 | t=3 p=1 | t=3 p=4 | t=4 p=1 | t=4 p=4 |
|---|---|---|---|---|---|---|
| 32 MiB | 340 ms | 248 ms | 380 ms | 471 ms | 588 ms | 633 ms |
| 64 MiB | 2442 ms (min 803) | 687 ms | 812 ms | **837 ms (min 599)** | 848 ms | 894 ms |
| 128 MiB | 1210 ms | 942 ms | 1253 ms | 1586 ms | 2401 ms | 2410 ms |

Target was p50 ≤ 1 s in Chromium on reference hardware; 64 MiB / t=3 / p=4 meets it
(0.6–0.85 s on a loaded 16-core host, 0.55 s in Node), 128 MiB does not reliably. `parallelism` does not speed things up in the browser
(single-threaded WASM) but keeps the parameter set identical to what a multi-threaded native
agent (Phase 3) will use, so KATs stay portable.

### Other primitives fixed by this slice

- AES-256-GCM via WebCrypto, 12-byte random nonce, 16-byte tag, blob = `nonce || ct || tag`,
  AAD reconstructed from context and never stored in the blob.
- HKDF-SHA256 via WebCrypto for subkeys (X25519 wrapping in slice #3).
- Passphrase is NFKC-normalised before UTF-8 encoding; salt ≥ 16 random bytes. The .NET
  agent must use `String.Normalize(NormalizationForm.FormKC)`; the frozen `nfkc-sensitive`
  vector (`ﬁancé ①` with decomposed `é` → `fiancé 1`) detects any divergence.
- Key hygiene is best-effort: the UTF-8 passphrase copy is zeroed, the JS strings and the
  Argon2 working memory in the WASM heap are not. Callers wipe the KEK after use.

## Consequences

Positive:

- Unlock cost is ~0.5 s on desktop hardware, memory-hard at 64 MiB; a parameter floor blocks
  downgrade via stored metadata.
- The frozen own vectors (`correct horse battery staple` and the NFKC-sensitive one, salt
  `000102…0f`, `KDF_DEFAULT`) are the interoperability anchor for the .NET agent and any
  future library swap.

Negative:

- Low-end mobile devices will see multi-second unlocks at 64 MiB; a per-device parameter
  choice (still ≥ minimum) is a later product decision, not blocked by this ADR.
- `'wasm-unsafe-eval'` widens the CSP slightly; acceptable because no other eval path exists.
- Single-maintainer dependency; mitigated by the pinned lockfile, Dependabot and the KATs.
