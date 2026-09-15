# Zielkatalog — what the `verifier` proves on the running system

One row per observable goal. The `verifier` subagent (Constitution §4 Phase 4) measures against
this table, not against green exit codes. Status carries the revision at which the proof was
last produced. Rows are added per slice; a slice is not done while one of its rows is OPEN.

Layers: L0 = code reading only · L1 = HTTP against the running stack · L2 = real browser ·
L3 = database / process state observed directly.

## Slice #1 — crypto primitives + Argon2id benchmark (ADR-0006)

| ID | Goal (source) | Observable criterion | Layer | Proof step | Negative control | Status |
|---|---|---|---|---|---|---|
| P1-01 | `npm test` green in Node 24 (mvp-slice-plan.md, slice #1) | Vitest `39 passed`, exit 0 in `node:24-alpine` | L3 | `docker run … node:24-alpine sh -c 'npm ci && npm test'` in `src/frontend` | flip one expected byte of a KAT → exactly that test fails, exit 1 | PROVEN 0a3b60a (39 tests, node:24-alpine v24.21.0; NC: nfkc vector flipped → exactly that test fails, exit 1) |
| P1-02 | Argon2id KATs are bit-exact, in Node and in Chromium | 7 reference vectors + frozen own vectors (`853b272a…bab49e` ASCII, `641be819…1cd9fb` NFKC-sensitive) match at `KDF_DEFAULT`; the ASCII vector also matches when `/src/crypto/kdf.ts` is imported in a Chromium page | L3 + L2 | `kdf.test.ts` output; Playwright page importing `kdf.ts` | flip one byte of a frozen vector → that test fails | PROVEN 0a3b60a Node + Chromium 153 (7 reference vectors; ascii `853b272a…bab49e` and nfkc-sensitive `641be819…1cd9fb` derived in-page from `/src/crypto/kdf.ts` at `KDF_DEFAULT`; pre-normalised `fiancé 1` → same KEK, NFC bytes → different; ceiling+1 and non-integer `memoryKib` → `RangeError` in Node and Chromium) |
| P1-03 | Benchmark numbers recorded, one real Chromium run | `npm run bench:kdf` JSON in Node **and** `bench/kdf.html` result table from headless Chromium (Playwright image) with `KDF_DEFAULT` p50 ≤ 1 s | L2 | TESTING.md "Frontend crypto tests and KDF benchmark" | in the same run at least one cell (128 MiB, t=4) exceeds 1 s, otherwise the harness is not measuring | PROVEN 0a3b60a (Node p50 469 ms / 3 runs; Chromium 153 p50 488 ms / 1 run, console errors []; 128 MiB t=4 p=4 1856 ms > 1 s in the same run) |
| P1-04 | Production build green with the WASM CSP | `npm run build` exit 0; `dist/index.html` contains `script-src 'self' 'wasm-unsafe-eval'` and nothing else in `script-src`; no inline `<script>` | L3 | build in `node:24-alpine`, grep | `'unsafe-eval'` absent (grep on a mutated copy hits) | PROVEN 0a3b60a (`script-src 'self' 'wasm-unsafe-eval'`, single hit; no inline script; mutated copy hits `'unsafe-eval'`) |
| P1-05 | WASM runs under that CSP in a real browser | `bench/kdf.html` (same `script-src`) completes in Chromium with `console errors: []`; the built `index.html` (`vite preview`, not the dev server) shows only the `frame-ancestors`-in-meta notice | L2 | Playwright run of P1-03, console captured; `vite preview` in Chromium | reverse proxy stripping `'wasm-unsafe-eval'` → `CompileError: WebAssembly.compile() … violates … "script-src 'self'"`, page never finishes; passthrough proxy → finishes | PROVEN 0a3b60a (Chromium 153: bench page console errors [] under `'wasm-unsafe-eval'`; reverse proxy stripping it → `CompileError … "script-src 'self'"`, page never done; passthrough → done; `vite preview` of the built `index.html` shows only the frame-ancestors-in-meta notice) |
| P1-06 | Tampered AAD / ciphertext / tag → decrypt fails (mvp-slice-plan.md, slice #1) | `aead.test.ts` tamper cases reject with `OperationError`; no plaintext returned | L3 | `npm test`; direct tsx script against `aead.ts` | untampered blob round-trips; removing the tamper lines makes the tests fail | PROVEN 0a3b60a (4 tamper cases green asserting `OperationError`; asserting `RangeError` instead fails with Received `OperationError`; tsx probe: AAD/ciphertext/tag/nonce → `OperationError`, no plaintext) |
| P1-07 | No passphrase or key material is logged or persisted | crypto modules contain no `console.*`, no `localStorage`/`sessionStorage`/`indexedDB`/`fetch`/`postMessage` | L0 | `grep -rE "console\.|localStorage|sessionStorage|indexedDB" src/frontend/src/crypto` → 0 | the same grep over `bench/` hits `kdf-node.ts` | PROVEN 0a3b60a (0 hits in `src/crypto` incl. fetch/postMessage/XMLHttpRequest; `bench/` control hits 3) |

Known noise, not a gap: on the Vite **dev server** `index.html` reports `Applying inline style
violates … style-src 'self'` because Vite injects `styles.css` inline in dev mode; the production
build does not. Tracked in STATE.md (CSP moves to response headers with the web image).

## Slice #2 — persistence foundation (ADR-0007)

| ID | Goal (source) | Observable criterion | Layer | Proof step | Negative control | Status |
|---|---|---|---|---|---|---|
| P2-01 | `dotnet test` green against a real Postgres (mvp-slice-plan.md, slice #2) | `Passed: N, Skipped: 0`, exit 0 in the `sdk:9.0` container with `PWDMGR_TEST_PG` | L3 | TESTING.md "Backend database tests" | without the variable: `Skipped: N`; with `CI=true` and no variable: `Failed` | PROVEN 117e0a8 (8 tests, Skipped 0; no var → Skipped 8; `CI=true` → Failed 8) |
| P2-02 | Compose stack starts, readiness green | `/health/ready` and `/health/live` 200 `Healthy`, `/api/v1/platform/info` 200 JSON | L1 | `docker compose -p pwdmgr up -d --build` + curl | P2-06 | PROVEN 117e0a8 |
| P2-03 | Migration creates the schema | `\dt` = `__EFMigrationsHistory`, `local_credentials`, `tenants`, `users`; history has `…_Identity` | L3 | psql `\dt`, `SELECT * FROM "__EFMigrationsHistory"` | empty database before start | PROVEN 117e0a8 (citext 1.6, `ak_users_tenant_id_id`, `fk_local_credentials_users_tenant_id_user_id`) |
| P2-04 | Second start is idempotent (§9) | log `No migrations were applied`, 0 Error lines, history unchanged | L3 | `docker compose -p pwdmgr restart api` + log segment | first run logs `Applying migration` | PROVEN 117e0a8 (no `HTTP_PORTS` override warning; NC with `ASPNETCORE_URLS` reproduces it) |
| P2-05 | Migration down on a DB copy | `dotnet-ef database update 0` exit 0 on the copy, only history table left, original untouched, forward again exit 0 | L3 | `pg_dump pwdmgr \| psql pwdmgr_copy`, dotnet-ef on the `pwdmgr_data` network (`CREATE DATABASE … TEMPLATE` fails while the API pool is open) | indexes before vs. after | PROVEN 117e0a8 (down/up on `pwdmgr_copy`, indexes identical; `citext` extension survives `Down()` by design) |
| P2-06 | Readiness detects a DB outage, liveness does not | ready 503 `Unhealthy`, live 200 `Healthy` with Postgres stopped; ready 200 again after start | L1 | `docker compose -p pwdmgr stop/start postgres` | P2-02 | PROVEN 117e0a8 |
| P2-07 | Unique constraints hold on the running artefact | duplicate slug → `23505 ix_tenants_slug`; same e-mail in different case → `23505 ix_users_tenant_id_email` | L3 | psql INSERT ×2 | first INSERT succeeds | PROVEN 117e0a8 (slug + e-mail case via citext) |
| P2-08 | Logs are JSON/UTC without connection strings (§8, §6.4) | 100 % lines `jq`-valid, `Timestamp` matches `…Z`, 0 hits for `Password=` / `Host=postgres` | L1 | `docker compose -p pwdmgr logs api` + jq/grep | the same grep against the container env hits | PROVEN 117e0a8 |
| P2-09 | Credential cannot reference a user of another tenant (ADR-0007) | INSERT into `local_credentials` with foreign `tenant_id` → `23503 fk_local_credentials_users_tenant_id_user_id` | L3 | xUnit `Credential_cannot_reference_a_user_of_another_tenant` + psql | matching tenant → `INSERT 0 1` | PROVEN 117e0a8 (psql `23503`; xUnit asserts SqlState only) |
| P2-10 | Migration has no destructive operations (§9) | `Up()` contains no `Drop*` / `AlterColumn` | L0 | `grep -E "Drop\|AlterColumn"` on `…_Identity.cs` `Up()` → 0 | — | PROVEN 117e0a8 (`Up()`: 0 Drop/AlterColumn; `Down()`: 3 DropTable) |

Known noise, not a gap: EF Core 9 logs one `Error`-level `Failed executing DbCommand … __EFMigrationsHistory`
line on the very first start against an empty database (it probes the history table before creating it).
It does not recur on restart.

## Slice #4 — local login + server sessions (ADR-0008)

| ID | Goal (source) | Observable criterion | Layer | Proof step | Negative control | Status |
|---|---|---|---|---|---|---|
| P4-01 | `dotnet test` green incl. API integration tests (mvp-slice-plan.md, slice #4) | `Pwdmgr.Api.Tests` 9 passed + `Pwdmgr.Infrastructure.Tests` 18 passed, Skipped 0, in the SDK container with `PWDMGR_TEST_PG` | L3 | TESTING.md "Backend database tests" | `CI=true` without variable → Failed | OPEN |
| P4-02 | Login sets a hardened cookie (ADR-0008) | `curl -c` login → 204, `Set-Cookie: pwdmgr_session=…; HttpOnly; SameSite=Strict; Path=/`; `Secure` present when `Auth:CookieSecurePolicy=Always` or the request is https | L1 | compose stack, `curl -v` | wrong password → 401, no `Set-Cookie` | OPEN |
| P4-03 | Rate limit per client + e-mail | 11th attempt within a minute → 429 (`Auth:LoginRateLimitPermits` default 10); another e-mail from the same client still 401 | L1 | curl loop | after the window a login succeeds again | OPEN |
| P4-04 | **Session expiry actually happens** (nex-im lesson) | compose override `Auth__SessionTtl=00:00:20`; login, `/auth/me` 200, wait 21 s, `/auth/me` 401 | L1 | curl + real wait | before the TTL `/auth/me` is 200 | OPEN |
| P4-05 | Logout revokes server-side | after `POST /auth/logout` the same cookie gets 401; `sessions.revoked_at` set | L1 + L3 | curl, psql | before logout 200 | OPEN |
| P4-06 | Storage holds only verifiers | `sessions.token_hash` is 32 bytes and ≠ cookie token; `local_credentials.password_hash` starts with `$argon2id$v=19$m=65536,t=3,p=4$` | L3 | psql | — | OPEN |
| P4-07 | Logs contain no password and no plaintext e-mail of a login attempt (§6.4) | `docker compose logs api` grep for the seed password and `admin@dev.local` → 0 hits | L1 | grep | the seed log line names only the tenant slug | OPEN |
| P4-08 | Unknown user / tenant answer in the same latency class as a wrong password | `Wrong_password_and_unknown_user_both_give_401_in_the_same_latency_class` green; curl timings within 3× | L1 | xUnit + `curl -w %{time_total}` | — | OPEN |
| P4-09 | Cross-origin unsafe request rejected | `POST /auth/login` with `Origin: https://evil.example` → 403; same-origin `Origin` → normal answer | L1 | curl | no `Origin` header → normal answer | OPEN |
| P4-10 | Tenant query filter isolates data | `Tenant_query_filter_hides_other_tenants_and_everything_without_context` green | L3 | xUnit | `IgnoreQueryFilters()` sees both tenants | OPEN |
| P4-11 | Running artefact identifies itself | `/api/v1/platform/info` returns `version` (assembly) and `commit` (`PWDMGR_COMMIT`, build arg `GIT_SHA`) | L1 | curl | local compose build → `commit: local` | OPEN |

## Slice #5 — keyring + vault API (ADR-0009, ciphertext-only)

| ID | Goal (source) | Observable criterion | Layer | Proof step | Negative control | Status |
|---|---|---|---|---|---|---|
| P5-01 | API tests green (mvp-slice-plan.md, slice #5) | `Pwdmgr.Api.Tests` 18 passed incl. `KeyringAndVaultTests`; migration `CryptoMetadata` applied by `MigrationTests` | L3 | SDK container `dotnet test` | `CI=true` without variable → Failed | OPEN |
| P5-02 | Enrolment is write-once | `PUT /me/keyring` → 201, second `PUT` → 409, `GET` returns the first record byte-for-byte | L1 | curl with a session cookie on the compose stack | — | OPEN |
| P5-03 | KDF downgrade rejected server-side (ADR-0006 floor) | `kdf.memoryKib=19455` or `iterations=1` → 400 naming `kdf` | L1 | curl | `65536/3/4` → 201 | OPEN |
| P5-04 | Vault creation needs a keyring; list shows only own vaults | `POST /vaults` before enrolment → 409; after → 201 with `keyVersion 1`; `GET /vaults` for another user of the same tenant → does not contain it | L1 | curl with two users (dev seed admin + a user created via psql) | — | OPEN |
| P5-05 | Cross-tenant invisibility | user of another tenant: `GET /me/keyring` 404, `GET /vaults` empty | L1 + L3 | curl + psql (`wrapped_keys` row exists exactly once) | — | OPEN |
| P5-06 | Server stores only opaque blobs | `psql`: `user_keyrings.public_key`, `encrypted_private_key`, `vaults.name_ciphertext`, `wrapped_keys.ciphertext` are `bytea`; no plaintext columns; `\d wrapped_keys` has `crypto_version` and the unique index over (tenant, resource, recipient, key_version) | L3 | psql | — | OPEN |
| P5-07 | Malformed input rejected without echo | 31-byte public key / non-base64 salt → 400 naming the field, response body does not contain the submitted value | L1 | curl | — | OPEN |

## Slice #6 — secret API (ciphertext-only, versioned)

| ID | Goal (source) | Observable criterion | Layer | Proof step | Negative control | Status |
|---|---|---|---|---|---|---|
| P6-01 | API tests green (mvp-slice-plan.md, slice #6) | `Pwdmgr.Api.Tests` 21 passed incl. `SecretTests`; migration `Secrets` applied by `MigrationTests` | L3 | SDK container `dotnet test` | `CI=true` without variable → Failed | OPEN |
| P6-02 | curl chain create → list → latest → new version → delete | `POST /vaults/{id}/secrets` 201 v1; `GET` list contains it; `GET /secrets/{id}/versions/latest` returns v1 blobs; `POST /secrets/{id}/versions` 201 v2 and latest = v2; `DELETE` 204, then latest 404 and list empty; psql keeps both `secret_versions` rows (soft delete) | L1 + L3 | compose stack, curl with the dev seed user after keyring + vault | PUT on a version → 404/405 | OPEN |
| P6-03 | Foreign vault / secret is 404 | second user (created via psql + login) gets 404 on list/create/latest/new version/delete of the first user's vault and secret; random vault id → 404 | L1 | curl | own vault → 200/201 | OPEN |
| P6-04 | Payload limit at the boundary | `payloadCiphertext` of 64 KiB + 1 → 413; 64 KiB → 201 | L1 | curl | — | OPEN |
| P6-05 | `aad_hash` stored as delivered | psql `encode(aad_hash,'base64')` equals the request field | L3 | psql | — | OPEN |
| P6-06 | **Plaintext marker never reaches the server** | verifier encrypts a known marker string client-side with `sealSecretPayload` from `src/frontend/src/crypto/keyring.ts` (tsx in `node:24-alpine`), uploads the blobs, then greps the whole database dump (`pg_dump`), the API logs and the Traefik logs for the marker → 0 hits; decrypting the downloaded blobs client-side yields the marker again | L3 | tsx + curl + `pg_dump | grep` | grep for the base64 payload prefix hits the dump (the grep works) | OPEN |
## Slice #3 — keyring crypto (ADR-0009)

| ID | Goal (source) | Observable criterion | Layer | Proof step | Negative control | Status |
|---|---|---|---|---|---|---|
| P3-01 | Vitest green in Node 24 (mvp-slice-plan.md, slice #3) | `npm test` → 52 passed (13 keyring tests), exit 0 | L3 | `docker run … node:24-alpine sh -c 'npm test'` | flip a byte of the frozen wrap KAT → exactly that test fails | PROVEN cd2f5cd (49 tests; NC: KAT.wrapped last hex flipped → exactly the frozen-vector test fails); re-prove at HEAD (52 tests after review fixes) |
| P3-02 | Enrol → lock → unlock round-trip; wrong KEK or substituted public key → `KeyringError` without key material | keyring tests `round-trips…`, `rejects a wrong passphrase…`, `binds the private key to the user id`, `rejects a substituted public key` green | L3 | `npm test` | — | PROVEN cd2f5cd (Node + Chromium 153: wrong passphrase → `KeyringError`, no hex ≥ 32); re-prove at HEAD (public key now in the AAD) |
| P3-03 | X25519 + wrap/unwrap KATs frozen | WebCrypto reproduces the RFC 7748 §6.1 shared secret; frozen blob unwraps to `00..1f`; recipient/context/tamper/low-order variants rejected | L3 | `npm test` | flipped ephemeral byte → `KeyringError` | PROVEN cd2f5cd (frozen RFC 7748 blob → 00..1f in Node and Chromium; flipped byte / wrong recipient / wrong context → `KeyringError`) |
| P3-04 | **X25519 WebCrypto works in a real browser** (Node support is no proof) | in headless Chromium on a **secure context** (`http://localhost:5173` via the vite container's network namespace, or TLS), `import("/src/crypto/keyring.ts")` then enrol/unlock/wrap/unwrap round-trip and the frozen KAT unwrap succeed; console errors `[]` | L2 | Playwright page against `vite dev` (TESTING.md recipe, secure-context note) | wrong passphrase in-page → `KeyringError` | PROVEN cd2f5cd Chromium 153.0.8010.12 (enrol/unlock/wrap/unwrap in-page, private key non-extractable X25519, frozen KAT → 00..1f, console only the two known dev-server notices). **Secure context required**: `crypto.subtle` is undefined on `http://<hostname>:5173`; prove via `http://localhost:5173` (share the vite container network namespace) or TLS |
| P3-05 | Error paths leak no key material | thrown messages contain no hex ≥ 32 chars, no base64 blobs; grep `console\.` in `src/crypto` → 0 | L0 + L3 | test `rejects a wrong passphrase…` + grep | — | PROVEN cd2f5cd (0 hits in src/crypto; 7 error paths → `KeyringError` without hex/base64 in message or stack; bench/ control hits 3) |
| P3-06 | Production build still green with the WASM CSP | `npm run build` exit 0, `script-src 'self' 'wasm-unsafe-eval'` unchanged | L3 | build + grep | — | PROVEN cd2f5cd (`script-src 'self' 'wasm-unsafe-eval'`, single hit; mutated copy hits `'unsafe-eval'`) |

