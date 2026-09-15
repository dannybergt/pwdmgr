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

