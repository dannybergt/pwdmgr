# MVP Slice Plan — first zero-knowledge vertical

Status: accepted as working plan, 2026-09-14. Refines the "MVP Foundation" work packages
(AP-010, AP-011, AP-014, AP-015) in [`work-packages.md`](work-packages.md) into reviewable
slices of ≤ ~2 h / one PR each. Source of truth for the product design remains
[`docs/architecture/product-plan.md`](../architecture/product-plan.md) (ADR-0005).

## Goal

Login → master-passphrase unlock in the browser → create and read a secret ciphertext-only,
proven on the running Compose stack. The server never sees plaintext (ADR-0002).

## Findings that shape the cut

- Backend skeleton has only `Tenant`, `Vault`, `TenantScopedEntity`; no `User`, no EF Core /
  Npgsql, no test projects, no structured logging. `Program.cs` carries a dummy
  `/tenants/{tenantId}/vaults` endpoint. `Tenant : TenantScopedEntity` inherits a `TenantId`
  it should not have — fixed in slice #2.
- Frontend is one static component; no router, no Vitest. `vite`, `typescript`,
  `@vitejs/plugin-react` sit under `dependencies` (fix in slice #1). CSP in `index.html` is
  already strict (`script-src 'self'`); WASM needs `'wasm-unsafe-eval'` — only that, never
  `'unsafe-eval'`.
- Compose: API behind Traefik (`/api`, `/health`), Postgres only on the internal `data`
  network, `ConnectionStrings__Postgres` already wired. No web image yet.
- CI builds only; no `dotnet test` / `vitest` yet.
- Plan §9/§10 key hierarchy: passphrase → Argon2id → KEK → user private key (X25519) → vault
  key → secret DEK → payload. Plan §28 leaves the web auth model open (JWT vs. PASETO) → ADR.

## Slices

| # | Slice | Depends on | ADR |
|---|---|---|---|
| 1 | Crypto primitives + Argon2id benchmark (frontend lib) | – | 0006 |
| 2 | Persistence foundation: EF Core + Npgsql, Tenant/User, migration 0001 | – | 0007 |
| 3 | Keyring crypto: X25519 user keys, vault-key wrapping, DEK per secret | 1 | 0009 |
| 4 | Local login + server sessions | 2 | 0008 |
| 5 | Keyring + Vault API (ciphertext-only) | 4 | – |
| 6 | Secret API (ciphertext-only, versioned) | 5 | – |
| 7 | React: login → enrolment/unlock → lock | 3, 5 | – |
| 8 | React: create / show secret | 6, 7 | – |
| 9 | Web image + Traefik route + CI (`dbergt/pwdmgr-web`, tests in CI) | 7 | – |

**Waves:** 1 = #1 ∥ #2 · 2 = #3 ∥ #4 · 3 = #5 → #6 ∥ #7 · 4 = #8 ∥ #9.
Critical path #2 → #4 → #5 → #6 → #8 (~10 h); frontend path #1 → #3 → #7 (~5.5 h).

**Start with #1** — highest technical uncertainty (WASM Argon2id runtime in a real browser),
its benchmark fixes the server-side KDF minimum parameters that #5 validates, and it runs
entirely on this host in a `node:24-alpine` container. Start #2 in parallel: independent and on
the critical path.

### #1 Crypto primitives + Argon2id benchmark

- `src/frontend/src/crypto/{kdf,aead,hkdf,encoding}.ts`: Argon2id (WASM) → 32-byte KEK;
  AES-256-GCM encrypt/decrypt with AAD; HKDF-SHA256 subkeys; Base64/UTF-8 helpers. Vitest
  setup. Move build tooling to `devDependencies`.
- Library (ADR-0006): `hash-wasm` (MIT, ~50 KB WASM, supports `parallelism`, WASM shipped as
  base64 inside JS — no separate asset). Rejected: `libsodium-wrappers` (Argon2id only p=1,
  larger), `@noble/hashes` Argon2 (pure JS, far too slow at 64 MiB), `argon2-browser`
  (maintenance doubtful, fragile WASM loading). Check maintenance / CVEs / licence on
  adoption (§10).
- Benchmark `npm run bench:kdf` for m ∈ {32, 64, 128} MiB, t ∈ {2, 3, 4}, p ∈ {1, 4}; results
  recorded in ADR-0006 as the parameter decision. Target p50 ≤ 1 s in Chromium on reference
  hardware. No bench UI (YAGNI).
- Tests: Argon2id known-answer tests (RFC 9106 vector + one own vector at m=64 MiB), AES-GCM
  round-trip, tamper test (changed AAD → `OperationError`), nonce uniqueness sanity (10k
  distinct). KATs become the cross-implementation anchor for the agent (TESTING.md).
- Verifier: (a) `npm test` green in `node:24-alpine`; (b) KAT bit-exact; (c) benchmark
  numbers recorded, one real Chromium run (Playwright image, headless) — not only Node;
  (d) `npm run build` green with CSP `'wasm-unsafe-eval'`; (e) negative control: tampered
  AAD → decrypt fails.
- Security: new WASM supply-chain surface → lockfile pin, `npm audit`, Dependabot already
  active. Rollback: revert, nothing persisted.

### #2 Persistence foundation

- `Npgsql.EntityFrameworkCore.PostgreSQL` 9.x, `PwdmgrDbContext`, entities `Tenant` (own base
  class), `User` (`tenant_id, source, external_id?, email, display_name, status`),
  `LocalCredential` (`user_id, password_hash, password_params, pepper_version`). snake_case
  naming, composite indexes with `tenant_id`, unique `(tenant_id, email)`, `(slug)`. Migration
  `0001_Identity`. `Database:MigrateOnStartup` (compose dev: `true`). `/health/ready` with
  Npgsql check. JSON console logging (§8).
- Tests: `tests/backend/Pwdmgr.Infrastructure.Tests` (xUnit): migration apply + rollback on a
  fresh DB, unique-constraint violation. DB via `PWDMGR_TEST_PG` connection string: CI
  `services: postgres:16`, locally `docker run --name pwdmgr-test-postgres` on its own
  network, no host port. Testcontainers deliberately not used (Docker socket inside the SDK
  container under SELinux is an extra failure source) — adjust the TESTING.md line.
- Verifier: (a) `dotnet test` green in `mcr.microsoft.com/dotnet/sdk:9.0`; (b) `docker
  compose up` (preflight 8080) → `curl /health/ready` 200 and `psql \dt` shows tables;
  (c) second start idempotent (§9); (d) migration down on a DB copy without error; (e) logs
  are JSON, no connection string in logs.
- Security: DB password stays env/compose; migration has no destructive operations. No
  `tenant_id` query filter yet — it arrives with the first authenticated endpoint (#4).
- Risk: `global.json` pins 9.0.100 with `rollForward: latestFeature` — SDK image 9.0.x
  suffices. Mount a NuGet cache volume (`-v pwdmgr-nuget:/tmp/.nuget`) for container builds.

### #3 Keyring crypto

- `crypto/keyring.ts`: X25519 keypair (WebCrypto); private key encrypted with KEK (AES-GCM,
  AAD = `user_id|crypto_version`); vault key (random 32 B) wrapped for a public key: ephemeral
  X25519 → HKDF-SHA256 → AES-256-GCM (`wrapped = ephPub||nonce||ct`); unwrap; secret DEK
  wrapped with vault key; payload encrypted with DEK + AAD
  (`tenant_id,vault_id,secret_id,version,crypto_version`). Vault key held as non-extractable
  `CryptoKey`.
- Why X25519 now (ADR-0009): group sharing via wrapped keys is an MVP must (§26); symmetric
  direct wrapping would force a re-wrap of every user before the sharing slice. Ed25519
  signatures stay out until sharing (YAGNI).
- Tests: round-trip enrol → lock → unlock; wrong KEK → error; wrap/unwrap vector frozen as
  KAT; error paths leak no key material.
- Verifier: Vitest green in Node 24 **and** once in Chromium (prove WebCrypto X25519 in a
  real browser; Node support is no proof).

### #4 Local login + server sessions

- `POST /api/v1/auth/login` (tenant slug, email, password) → Argon2id verify server-side
  (ADR-0008 library choice: `Isopoh.Cryptography.Argon2` for PHC string format in
  `password_params`; alternative Konscious) → `Session` row (id hash, user_id, tenant_id,
  expires_at, revoked_at) → `HttpOnly; Secure; SameSite=Strict` cookie. `POST /auth/logout`,
  `GET /auth/me`. Auth middleware sets `ICurrentTenant` / `ICurrentUser`; EF global query
  filter on `tenant_id`. Rate limit on `/auth/login` (ASP.NET `RateLimiter`, fixed window per
  IP+email). Dev-only seed (`Seed:Enabled` only in Development): tenant `dev`, one user —
  production onboarding is a later slice (AP-015), named as open in the PR. Remove the
  placeholder endpoint from `Program.cs`.
- Why cookie sessions instead of JWT (ADR-0008): same-origin SPA behind Traefik; server-side
  revocation trivial; no token in JS memory (XSS blast radius); plan §28 already lists
  server-side session records. Token auth for agent/CLI is a separate Phase-3 decision.
- Tests: WebApplicationFactory against test Postgres: happy login; wrong password → 401 in
  the same latency class (no user enumeration); rate limit → 429; `me` without cookie → 401;
  after logout → 401; expired session → 401.
- Verifier: (a) `curl -c` login → cookie flags; (b) 11th failure → 429; (c) **make session
  expiry actually happen**: `Auth:SessionTtl=60s` in a compose override, wait 61 s,
  `/auth/me` → 401 (nex-im lesson); (d) `psql`: `sessions` stores only hashes,
  `local_credentials.password_hash` starts with `$argon2id$`; (e) logs: no password, no
  plaintext email (grep).
- Security: §6.1/6.2/6.4 central. No pepper in this slice (`pepper_version` stays 0). CSRF:
  SameSite=Strict + `Origin` header check on mutating requests. Rollback: migration
  `0002_Sessions` down.

### #5 Keyring + Vault API

- Entities `UserKeyring` (`user_id, kdf_algo, kdf_params(json: m,t,p), kdf_salt, public_key,
  encrypted_private_key, crypto_version`), `WrappedKey` (`resource_type, resource_id,
  recipient_type, recipient_id, key_version, ciphertext`). Endpoints `PUT /me/keyring` (only
  when empty — enrolment), `GET /me/keyring`, `POST /vaults` (type Personal,
  `name_ciphertext`, wrapped vault key for own public key), `GET /vaults`. Server validates
  form only (base64, lengths, `kdf_params` ≥ minimums from ADR-0006 — prevents a downgrade
  attack via stored parameters). Migration `0003_CryptoMetadata`.
- Tests: enrolment twice → 409; other tenant's keyring invisible; `kdf_params` below minimum
  → 400; vault list only own.
- Verifier: `curl` enrolment + vault; `psql` shows only Base64 blobs; cross-tenant → 404;
  downgrade params → 400.

### #6 Secret API

- `Secret` (`vault_id, type, name_ciphertext, status`), `SecretVersion` (`version_no,
  payload_ciphertext, wrapped_dek, aad_hash, created_by`). `POST /vaults/{id}/secrets`,
  `GET /vaults/{id}/secrets` (metadata), `GET /secrets/{id}/versions/latest` (response per
  plan §34), `POST /secrets/{id}/versions` (update = new immutable version),
  `DELETE /secrets/{id}` (status `Deleted`, no hard delete). Payload limit (64 KiB) at the
  system boundary. Migration `0004_Secrets`.
- Tests: versions immutable (PUT on version → 405/404); access via foreign vault → 404;
  limit → 413; `aad_hash` stored as delivered.
- Verifier: `curl` chain create → list → latest → new version → delete; **plaintext marker
  test**: verifier encrypts a known marker string client-side with the #3 lib in a Node
  container, then greps DB and API logs for it — must be 0 hits.
- Open, named in PR: audit events (AP-019).

### #7 React: login → enrolment/unlock → lock

- Minimal router (routes `/login`, `/unlock`, `/vault` only), `auth/api.ts` (same-origin
  fetch, `credentials: 'include'`), login form, enrolment screen when keyring empty
  (passphrase twice, minimum length), unlock screen (Argon2id with progress indicator),
  `VaultSession` (module state: unwrapped vault key + private key as `CryptoKey`; idle lock
  after 10 min; `beforeunload` = gone). `vite.config.ts` with dev proxy `/api → api:8080`.
- Tests: Vitest + Testing Library: login error display; wrong passphrase → error; no network
  request carries the passphrase (inspect fetch mock). Playwright: golden path login → enrol
  → unlock → lock.
- Verifier (Chromium headless): (a) route intercept: no request contains passphrase or
  derived material; (b) `localStorage` / `sessionStorage` / IndexedDB empty after unlock;
  (c) reload → locked again; (d) wrong passphrase → error without server request;
  (e) console free of errors; (f) session expiry (60 s TTL) → UI falls back to `/login`, no
  hang.
- Security: CSP stays strict, no `dangerouslySetInnerHTML`; passphrase inputs use
  `autocomplete="new-password"` / `"current-password"`.

### #8 React: create / show secret

- List (decrypt names), form for type `password` (username/password/url/notes as JSON
  payload), detail with reveal toggle. No template system, no generator (own APs).
- Tests: component tests for the form; Playwright: create → reload → unlock → read; route
  intercept: request body does not contain the marker string.
- Verifier: marker string typed in the browser → `psql`, API logs, Traefik access log,
  network capture: 0 hits. Edge: secret readable from a second browser context of the same
  user only after unlocking there.

### #9 Web image + Compose route + CI

- `infra/compose/docker/web.Dockerfile` (node:24-alpine build → nginx-unprivileged, static),
  Traefik route `/` → web, `/api` + `/health` → api; CI job `docker-web` after the
  `docker-api` pattern (push `dbergt/pwdmgr-web`, ADR-0004); `dotnet test` + `npm test` in CI
  if not already added in #2/#1.
- Verifier: `docker compose up` preflight, `curl http://localhost:8080/` serves the SPA, CSP
  header present, Trivy scan of the web image without HIGH/CRITICAL.

## Cross-cutting test strategy

- Frontend: Vitest (unit/KAT/component) from #1, Playwright from #7; Chromium runs in the
  Playwright container, not only jsdom/Node.
- Backend: xUnit + WebApplicationFactory against real Postgres (CI `services:`, locally
  `docker run`), migration up/down test from #2.
- Verifier guidelines for every slice: **plaintext marker test** (known string encrypted
  client-side → grep DB, API log, proxy log, network capture = 0), **make session expiry
  real** (short TTL), **cross-tenant = 404**.

## Security impact (§6)

- New supply-chain surface: `hash-wasm` (frontend), `Isopoh.Cryptography.Argon2` or Konscious,
  `Npgsql.EFCore` (backend) — pins, audit, Dependabot in place.
- CSP change `'wasm-unsafe-eval'` — minimal, documented.
- Server-validated KDF minimum parameters prevent parameter downgrade via stored metadata.
- Login: Argon2id, rate limit, no user enumeration, cookie flags, server-side revocation.
- Logging: JSON, without email/password/cookie; never log request bodies on `/auth/*` and
  `/secrets/*`.
- Deliberately later: audit hash chain (AP-019), MFA (AP-012/013), pepper, production
  onboarding, recovery, sharing signatures (Ed25519), XChaCha20.

## Risks & rollback

- Argon2id at m=64 MiB on weak clients > 2 s → adjust parameters in ADR-0006 after the
  benchmark (lower t before lowering m). Parameters live per user in the keyring; a later
  increase = re-enrol on unlock (client-side, no server plaintext).
- WebCrypto X25519 missing in older browsers → browser support matrix in ADR-0009; fallback
  `@noble/curves` only on demonstrated need.
- .NET builds only in containers (slow restore) → NuGet cache volume.
- Ports: 8080 is held only by our own Compose stack; foreign containers on this host stay
  untouched. Test Postgres gets its own name `pwdmgr-test-postgres` and network, no host port.
- Rollback per slice: revert the PR; migrations have down paths; dev DB is disposable; no
  production data exists.

## ADRs to write

- **ADR-0006 Client crypto v1** — Argon2id via `hash-wasm`, parameters (from benchmark),
  AES-256-GCM (WebCrypto), HKDF-SHA256; XChaCha20 deferred; minimum parameters for server
  validation. Slice #1.
- **ADR-0007 Persistence** — EF Core 9 + Npgsql, snake_case, migration strategy (startup
  flag, up/down mandatory), test-DB approach instead of Testcontainers. Slice #2.
- **ADR-0008 Web auth model** — server-side sessions + HttpOnly cookie instead of
  JWT+refresh; Argon2id server library; CSRF protection; dev-seed rule; token auth for
  agent/CLI later. Slice #4.
- **ADR-0009 Key hierarchy v1** — passphrase → Argon2id → KEK → AES-GCM(X25519 private key);
  vault key wrapped to public key (ephemeral X25519 + HKDF + AES-GCM); DEK per secret; AAD
  composition; signatures only with sharing. Slice #3, referenced by #5/#6.
