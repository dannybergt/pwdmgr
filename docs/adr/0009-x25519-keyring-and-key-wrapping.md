# ADR-0009: X25519 user key pairs from day one; vault keys wrapped per recipient; one DEK per secret version

Status: accepted  
Date: 2026-09-15

## Context

The zero-knowledge design (ADR-0002, product plan §9/§10/§26) needs a key hierarchy the
server never sees in the clear. Two options for the MVP:

1. **Symmetric only:** wrap every vault key directly under the user's KEK. Simplest, but
   sharing a vault with a second user (a must for the MVP per §26) would require re-wrapping
   every vault key for every recipient by someone who holds it, and the first sharing slice
   would have to migrate every existing user.
2. **Asymmetric user keys:** each user owns an X25519 key pair; the private key is sealed
   under the KEK, the public key is stored in the clear. A vault key is wrapped *for a public
   key*, so anybody who holds the vault key can grant access to anybody whose public key the
   server publishes — including the user themselves at enrolment.

## Decision

- **Every user gets an X25519 key pair at enrolment** (`src/frontend/src/crypto/keyring.ts`).
  The private key is exported as PKCS#8, sealed with AES-256-GCM under the KEK with AAD
  `pwdmgr/v1/user-private-key|<user id>|<public key hex>`, and only ever exists in the browser
  as a non-extractable `CryptoKey` after unlock. Binding the **public key** into that AAD is
  what stops a compromised server from substituting the stored public key (the user would
  otherwise wrap vault keys to an attacker's key on the next vault creation): unlock fails on
  the GCM tag instead. The record stored server-side is
  `{ cryptoVersion, kdfParams, kdfSalt, publicKey, encryptedPrivateKey }`.
- **Key wrapping for a public key:** ephemeral X25519 key pair → `deriveBits` with the
  recipient's public key → HKDF-SHA256 with info
  `pwdmgr/v1/wrap|<ephemeral pub hex>|<recipient pub hex>` → AES-256-GCM key → seal the
  raw vault key with AAD `vault-key|<tenant>|<vault>|<recipient user>|v1`. Wire format
  `ephemeralPublic(32) || nonce(12) || ciphertext || tag(16)`. Binding both public keys into
  the HKDF info closes the "X25519 output is not bound to the peer" gap; the AAD binds the
  blob to tenant, vault and recipient so a blob cannot be replayed for another vault or user.
- **Vault keys** are random 32-byte AES-256-GCM keys, held non-extractable while unlocked.
- **One DEK per secret version**, wrapped under the vault key with AES-256-GCM and AAD
  `secret|<tenant>|<vault>|<secret>|<version>|v1`; the payload is encrypted under the DEK with
  the same AAD. Consequence: a vault key seals only DEKs (≤ 2^32 seals per key is never
  approached), and rotating a vault key re-wraps DEKs without touching payloads.
- **Not now (YAGNI):** Ed25519 signatures on wrapped keys / public keys. Until sharing lands
  the server is the only source of public keys and a malicious server could already withhold
  data; signing/fingerprint verification is the sharing slice's concern.
- `CRYPTO_VERSION = 1` is carried in every AAD and in the keyring record. The wire formats
  carry no version byte, so a future scheme change needs the version stored per row — the
  server keeps `crypto_version` on `user_keyrings`, `wrapped_keys` and `secret_versions`
  (slices #5/#6) and the client picks the read path from it.
- Known-answer anchors: the RFC 7748 §6.1 shared secret is asserted directly against WebCrypto
  X25519, and a wrapped blob produced with Alice as recipient and Bob as ephemeral key is
  frozen and must always unwrap to `00..1f`. The ephemeral pair is never injectable in
  production code — fresh per wrap is an invariant, not a convention.
- Every X25519 failure (low-order point, wrong length) surfaces as `KeyringError`, never as a
  raw `DOMException`.

## Consequences

Positive:

- Sharing later is "wrap the vault key for one more public key"; no re-enrolment, no migration.
- All blobs are self-describing about *what* they protect (AAD), the server stores opaque
  `Bytes` only, and every primitive is WebCrypto except Argon2id (ADR-0006).
- Proven in Node 24 and Chromium (X25519 in WebCrypto is Chrome ≥ 133 / Firefox ≥ 130 /
  Safari ≥ 17); older browsers are out of scope for the MVP.
- **WebCrypto requires a secure context.** `crypto.subtle` is undefined on a plain-http origin
  other than `localhost`, so the web client must be served over TLS (Traefik terminates it,
  slice #9) — the dev stack on `http://localhost:8080` works, `http://<hostname>:8080` does not.

Negative:

- X25519 in WebCrypto is recent; browser support is part of the supported-browser matrix, not
  a polyfill decision (a JS X25519 would reintroduce a supply-chain risk for the core secret).
- Non-extractable vault keys mean sharing has to unwrap the vault key from the user's own
  wrapped copy again (raw bytes exist briefly); accepted for the same hygiene reasons as
  ADR-0006.
- Key hygiene remains best-effort (see ADR-0006): raw keys are wiped after import, but JS
  cannot guarantee it.
