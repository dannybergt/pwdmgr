# Security Policy

`pwdmgr`/`Privora` is security-sensitive software. All security-impacting work requires review before release.

## Baseline

- No plaintext secrets in server-side logs, telemetry, database rows, queues or caches.
- No secret-reading backdoors for platform, global, tenant or support admins.
- All authentication credentials must be non-reversibly hashed or externally verified.
- Vault payloads and protected files must remain client-side encrypted.
- Recovery must use explicit multi-party cryptographic controls.

## Implemented controls (MVP, 2026-09-15)

| Area | Control | Where |
|---|---|---|
| Zero knowledge | Passphrase → Argon2id (m=64 MiB, t=3, p=4) → KEK → X25519 private key → vault key → per-version DEK; every blob AAD-bound (tenant, vault, user, secret, version, crypto version); server stores `bytea` only | ADR-0006, ADR-0009, `src/frontend/src/crypto` |
| Login | Argon2id verifier (same parameters), decoy verification for unknown accounts, opaque session token (SHA-256 stored), `HttpOnly; Secure; SameSite=Strict` session cookie, absolute 8 h TTL, immediate revocation, user/tenant status checked per request | ADR-0008 |
| Brute force | Per client+account 10/min, per client 30/min, per account 20 failures/15 min across clients, global Argon2 concurrency gate (503), per-user write limit 60/min | `LoginThrottle`, `Program.cs` |
| CSRF / headers | `SameSite=Strict` + same-origin check on unsafe methods; CSP `script-src 'self' 'wasm-unsafe-eval'`, nosniff, DENY, no-referrer, HSTS, `Cache-Control: no-store` on the API | `SameOriginMiddleware`, `web.security-headers.conf`, Traefik |
| Tenant isolation | EF global query filter on every tenant-scoped entity + composite `(tenant_id, id)` foreign keys + check constraints in the schema | ADR-0007, migration `IntegrityConstraints` |
| Input | Base64/length/enum validation at the boundary, malformed JSON → 400 without an error log in every environment (`ThrowOnBadRequest` off), 64 KiB payload limit (413), Kestrel body limit 256 KiB, KDF parameter floor/ceiling on client, server and database | endpoints, `KdfLimits`, `Program.cs` |
| Audit | `Pwdmgr.Audit.Auth` / `Pwdmgr.Audit.Vault` events with ids and client address only | `AuditLog` |
| Supply chain | Lockfiles, Dependabot (npm, NuGet, Actions, Docker, Compose), Actions pinned by commit SHA, `npm audit` / `dotnet list package --vulnerable` clean, gitleaks in pre-commit and CI | `.github/` |

## Accepted residual risks (as of the MVP)

- The vault's security is the user's passphrase: an attacker with the database can run an offline Argon2id search. The client enforces length ≥ 12 and a zxcvbn score ≥ 3 with the user's e-mail/name as inputs; nothing more is possible without a second factor.
- Global primary keys: a caller who already knows another tenant's vault/secret UUID can learn that it exists by getting 409 on reuse (UUIDv4, not guessable). Closing it needs composite primary keys.
- No MFA, no per-tenant storage quota, no HSM-backed pepper yet (ROADMAP).
- The compose stack is a development/evaluation configuration (self-signed certificate, seed admin) and must not be exposed to a network as is (OPERATIONS.md).

## Reporting

Security reporting channels are not configured yet.

Before public release, define:

- Security contact.
- Vulnerability disclosure process.
- Supported versions.
- Patch SLA.
- CVE handling.

