# ADR-0008: Server-side sessions in an HttpOnly cookie for the web client; Argon2id via Konscious for local login

Status: accepted  
Date: 2026-09-15

## Context

Slice #4 needs a login for the web client. The product plan (§28) recommends "short JWT access
tokens plus refresh-token rotation and server-side session records; PASETO as an ADR". The
web client is a same-origin SPA served behind the same Traefik entrypoint as the API
(`/` → web, `/api` → API from slice #9 on). Zero-knowledge (ADR-0002) makes the session token
low-value on its own — it never unlocks vault data — but session hijacking would still expose
ciphertext, metadata and the ability to act as the user.

A second decision is the server-side password verifier for local users. Slice plan named
`Isopoh.Cryptography.Argon2` (PHC string output) with Konscious as the alternative.

## Decision

### Sessions: opaque token in a cookie, record in Postgres — no JWT for the web client

- `POST /api/v1/auth/login` verifies the password and creates a `sessions` row
  (`token_hash` = SHA-256 of a random 32-byte token, `expires_at`, `revoked_at`, `last_seen_at`).
  The browser receives the raw token in cookie `pwdmgr_session`: `HttpOnly`, `SameSite=Strict`,
  `Path=/`, `Secure` per `Auth:CookieSecurePolicy` (default `Always`; the plain-http compose dev
  stack sets `SameAsRequest`).
- Every request is authenticated by hashing the cookie and loading the row; expiry and
  revocation are therefore immediate and server-side. `POST /auth/logout` revokes; `GET /auth/me`
  returns the principal. Absolute TTL (`Auth:SessionTtl`, 8 h default), no sliding renewal.
- Why not JWT: same-origin SPA → cookie is the natural transport; revocation must be
  immediate (compromised device, admin lock) which JWT only approximates with short lifetimes
  and refresh rotation; no token in JavaScript memory keeps the XSS blast radius to "act via
  the browser while it is open". Token auth for the Windows agent / CLI is a separate Phase-3
  decision and does not reuse this scheme.
- CSRF: `SameSite=Strict` plus `SameOriginMiddleware` — an unsafe-method request that carries an
  `Origin` header must be same-origin with the request `Host` (scheme-agnostic port compare).
  Requests without `Origin` (curl) pass; they are not a browser CSRF vector.
- Rate limit: `LoginThrottle`, fixed window per client address + lower-cased e-mail
  (`Auth:LoginRateLimitPermits`/`Window`, 10 per minute default). Keyed on the account too, so
  a distributed guesser is throttled per target and a NAT does not lock everybody out.
- Unknown user, unknown tenant, disabled user and wrong password all run the Argon2 verifier
  (against a decoy hash when there is no credential) and answer 401 in the same latency class.
- Tenant context: `RequestContext` (scoped) is populated by the authentication handler; the
  EF global query filter `tenant_id = current` applies to every `TenantScopedEntity`. Before
  authentication the tenant is `Guid.Empty`, so the filter matches nothing; login, seeding and
  session resolution use `IgnoreQueryFilters()` explicitly.
- Forwarded headers (`X-Forwarded-For/Proto`) are honoured only from `Forwarded:KnownNetworks`
  (compose: `172.16.0.0/12`); production must set its proxy network explicitly.

### Password verifier: `Konscious.Security.Cryptography.Argon2` 1.3.1 with an own PHC codec

- MIT, actively maintained (2024-06), widely used; pure managed. Isopoh (2.0.0, 2023-08)
  ships PHC formatting but was not chosen because the codec is ~40 lines and Konscious'
  release cadence is better.
- Parameters identical to the browser KDF (ADR-0006: m=64 MiB, t=3, p=4, 16-byte salt,
  32-byte hash). The two frozen vectors from TESTING.md are xUnit KATs
  (`Argon2PasswordHasherTests`), which proves Konscious == hash-wasm == argon2-cffi and gives
  the future .NET agent its NFKC anchor (`String.Normalize(FormKC)`).
- Stored as `$argon2id$v=19$m=65536,t=3,p=4$<salt>$<hash>` in `local_credentials.password_hash`;
  `pepper_version` stays 0 (no pepper in this slice).

### Development seed

`Seed:Enabled=true` is honoured **only** in the Development environment and requires
`Seed:AdminPassword` from configuration (compose sets a dev-only value). Creates tenant `dev`
and `admin@dev.local` once; never overwrites an existing password. Production onboarding is
a later slice (AP-015).

## Consequences

Positive:

- Immediate server-side revocation; sessions are visible and auditable rows.
- No bearer token in JS; XSS cannot exfiltrate a reusable credential.
- One Argon2 parameter set across browser, server and (later) agent, pinned by shared vectors.

Negative:

- One database read per authenticated request (index on `token_hash`); acceptable at MVP scale,
  a cache is a later optimisation.
- Cookie sessions do not serve non-browser clients; the agent gets its own token scheme.
- `SameSite=Strict` means a link from an external site opens the app logged-out until the SPA
  re-checks `/auth/me` — acceptable for a vault.
