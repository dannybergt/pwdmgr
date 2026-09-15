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

## Slice #3 — keyring crypto (ADR-0009)

| ID | Goal (source) | Observable criterion | Layer | Proof step | Negative control | Status |
|---|---|---|---|---|---|---|
| P3-01 | Vitest green in Node 24 (mvp-slice-plan.md, slice #3) | `npm test` → 49 passed (10 keyring tests), exit 0 | L3 | `docker run … node:24-alpine sh -c 'npm test'` | flip a byte of the frozen wrap KAT → exactly that test fails | OPEN |
| P3-02 | Enrol → lock → unlock round-trip; wrong KEK → `KeyringError` without key material | keyring tests `round-trips…`, `rejects a wrong passphrase…`, `binds the private key to the user id` green | L3 | `npm test` | — | OPEN |
| P3-03 | Wrap/unwrap KAT frozen | RFC 7748 keys + frozen blob unwrap to `00..1f`; recipient/context/tamper variants rejected | L3 | `npm test` | flipped ephemeral byte → `KeyringError` | OPEN |
| P3-04 | **X25519 WebCrypto works in a real browser** (Node support is no proof) | in headless Chromium, `import("/src/crypto/keyring.ts")` then enrol/unlock/wrap/unwrap round-trip and the frozen KAT unwrap succeed; console errors `[]` | L2 | Playwright page against `vite dev` (TESTING.md recipe) | wrong passphrase in-page → `KeyringError` | OPEN |
| P3-05 | Error paths leak no key material | thrown messages contain no hex ≥ 32 chars, no base64 blobs; grep `console\.` in `src/crypto` → 0 | L0 + L3 | test `rejects a wrong passphrase…` + grep | — | OPEN |
| P3-06 | Production build still green with the WASM CSP | `npm run build` exit 0, `script-src 'self' 'wasm-unsafe-eval'` unchanged | L3 | build + grep | — | OPEN |

