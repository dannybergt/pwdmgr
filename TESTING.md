# TESTING — pwdmgr / Privora

Status: living document — strategy and current state.

Last update: 2026-09-15

## Principles

- **Tests are a release artifact, not an afterthought.** "Tests green" is necessary but not sufficient; manual verification of golden path and at least one edge case is required (Constitution AGENTS.md §4).
- **Never disable a failing test to make CI green.** Fix the code, not the test (AGENTS.md §17).
- **Security tests run on every PR.** Secret scan, dependency scan, container scan.
- **Cryptographic code is treated as critical.** Known-answer tests, cross-implementation interop tests (browser ↔ agent), parameter pinning.

## Coverage matrix (target)

### Backend (.NET)

| Layer | Test type | Tooling | Mandatory for MVP |
|---|---|---|---|
| Domain (entities, value objects, invariants) | Unit | xUnit + FluentAssertions | yes |
| Application services (use cases) | Unit + module-integration | xUnit + real Postgres (`PWDMGR_TEST_PG`) | yes |
| API controllers / endpoints | Integration | `WebApplicationFactory` + real Postgres (`PWDMGR_TEST_PG`) | yes |
| Persistence / EF Core mappings | Integration on real Postgres | xUnit v3 + `PWDMGR_TEST_PG` (see below) | yes |
| LDAP / AD adapters | Integration against `osixia/openldap` test container | compose service, same pattern as Postgres | Phase 2 |
| Crypto adapters | Known-answer tests + interop with browser WASM | xUnit + golden vectors | yes |
| Auth / MFA / step-up flows | Integration | xUnit + real Postgres (`PWDMGR_TEST_PG`) | yes |
| Audit hash chain | Property tests | FsCheck (or Bogus + xUnit) | yes |

### Frontend (React + TS)

| Layer | Test type | Tooling | Mandatory for MVP |
|---|---|---|---|
| Pure functions / hooks | Unit | Vitest | yes |
| Components | Component test | Vitest + Testing Library | yes |
| Crypto primitives (Argon2id KDF, HKDF, AES-GCM) | Known-answer + round-trip | Vitest (Node 24 WebCrypto + `hash-wasm`), Chromium run for the KDF benchmark | yes |
| Unlock flow (login → MFA → passphrase) | E2E | Playwright | yes |
| Vault CRUD (ciphertext only over the wire) | E2E | Playwright | yes |

### Browser extension

| Layer | Test type | Tooling | Mandatory for MVP |
|---|---|---|---|
| Content-script DOM scanning | Unit | Vitest + jsdom | Phase 2 |
| Background message handling | Unit | Vitest | Phase 2 |
| Full install + autofill on top sites | Manual | Recorded checklist | yes |

### Windows agent

| Layer | Test type | Tooling | Mandatory for MVP |
|---|---|---|---|
| Local IPC (named pipe / localhost mTLS) | Integration | xUnit | yes |
| Token storage (DPAPI / TPM) | Manual + unit where possible | xUnit | yes |
| RDP credential injection | Manual | Recorded checklist | Phase 3 |

### Container / deployment

| Aspect | Test type | Tooling | Mandatory for MVP |
|---|---|---|---|
| Image build reproducibility | CI build | GitHub Actions + buildx | yes |
| Compose up / health checks | CI smoke | GitHub Actions service | yes |
| Migration apply on fresh DB | CI smoke | EF Core migrate inside test container | yes |
| Image vulnerability scan | CI | `trivy` or `grype` | yes |
| SBOM | CI artifact | `syft` | yes |

### Security

| Check | Tooling | When |
|---|---|---|
| Secret scan | `gitleaks` | pre-commit + CI on every PR |
| Dependency vulnerability scan | `dotnet list package --vulnerable`, `npm audit`, `dependabot`/`renovate` | CI nightly + on PR |
| SAST | `CodeQL` | CI on PR |
| DAST | `OWASP ZAP` baseline | nightly on staging (Phase 2+) |
| Container scan | `trivy` | CI on PR for changed images |
| Threat-model review | manual ADR-style | per epic |

## Manual verification gates

A change is not "done" until it has been verified end-to-end. For UI changes:

1. Start the dev server (Vite for frontend, `dotnet run` for API, compose for full stack).
2. Walk the golden path in a real browser.
3. Walk at least one error / edge case (invalid input, expired session, network drop).
4. Watch the browser console for unexpected errors.
5. Watch the API logs — no secret content, no stack traces leaking internals.

For backend-only changes: at least one `curl` / HTTPie request hitting the live endpoint and verifying response shape, status, and absence of secret content in logs.

When manual verification is impossible (e.g. environment not available), say so explicitly in the PR rather than implying success.

## CI gates

Every PR must:

- Pass `dotnet build` for the backend solution.
- Pass `npm run build` for the frontend.
- Pass `gitleaks` scan.
- Pass `npm test` (frontend job, Vitest).
- Pass `dotnet test` (backend job, against a `postgres:16` service container).

## Backend database tests

Integration tests need a real Postgres. They read the admin connection string from
`PWDMGR_TEST_PG`, create one throw-away database per test class (`pwdmgr_test_<guid>`) and
drop it afterwards. When the variable is unset the tests are **skipped** (xUnit v3 dynamic
skip); under `CI=true` (GitHub Actions sets it) a missing variable **fails** the tests, so a
lost service container can never turn into a green job with zero database coverage.

Testcontainers is deliberately not used: the Docker socket inside the SDK container under
SELinux is an extra failure source, and one plain container on its own network does the job.

Locally (no host port, own network — Constitution §3):

```sh
docker network create pwdmgr-test
docker run -d --name pwdmgr-test-postgres --network pwdmgr-test \
  -e POSTGRES_USER=pwdmgr -e POSTGRES_PASSWORD=pwdmgr-test-only -e POSTGRES_DB=pwdmgr postgres:16-alpine
docker run --rm --network pwdmgr-test -u "$(id -u):$(id -g)" -e HOME=/tmp -e DOTNET_CLI_HOME=/tmp \
  -e NUGET_PACKAGES=/tmp/.nuget/packages -v pwdmgr-nuget:/tmp/.nuget -v "$PWD:/w:z" -w /w \
  -e "PWDMGR_TEST_PG=Host=pwdmgr-test-postgres;Database=pwdmgr;Username=pwdmgr;Password=pwdmgr-test-only" \
  mcr.microsoft.com/dotnet/sdk:9.0 dotnet test pwdmgr.slnx
```

(`pwdmgr-nuget` is a named volume for the package cache; `chown` it to your uid once.)

## Frontend crypto tests and KDF benchmark

`src/frontend/src/crypto/*.test.ts` run with `npm test` (Vitest, Node 24 — WebCrypto and the
`hash-wasm` Argon2id build behave the same as in browsers; a real-browser run is still required
for the benchmark, see below). Known-answer vectors:

- Argon2id: seven vectors from the reference implementation's `src/test.c` (v=0x13, no secret,
  no associated data) plus two frozen vectors at `KDF_DEFAULT` (ASCII and NFKC-sensitive),
  cross-checked with argon2-cffi — the cross-implementation anchor for the .NET agent. RFC 9106
  §5.3 is not used because its only Argon2id vector needs associated data, which neither
  `hash-wasm` nor this KDF exposes.
- HKDF-SHA256: RFC 5869 test cases 1 and 3.
- Key wrapping (ADR-0009): RFC 7748 §6.1 X25519 key pairs as recipient/ephemeral keys and a
  frozen wrapped blob that must unwrap to `00..1f`; X25519 itself is proven in Chromium by the
  verifier (WebCrypto X25519 needs Chrome ≥ 133).
- AES-256-GCM: round-trip, tampered AAD / ciphertext / tag / wrong key → rejection, 1 000
  distinct nonces (RNG sanity only).

Benchmark (`npm run bench:kdf [runs]` in Node; `bench/kdf.html` in a browser) measures the
m × t × p matrix from ADR-0006. Real-Chromium run on this host without `node`:

```sh
cd src/frontend
docker network create pwdmgr-bench
docker run -d --name pwdmgr-bench-web --network pwdmgr-bench -u "$(id -u):$(id -g)" -e HOME=/tmp \
  -e __VITE_ADDITIONAL_SERVER_ALLOWED_HOSTS=pwdmgr-bench-web -v "$PWD:/w:z" -w /w node:24-alpine \
  npx vite --host 0.0.0.0 --port 5173
# then drive http://pwdmgr-bench-web:5173/bench/kdf.html?runs=5 from a Playwright container on the
# same network and read <pre id="out"> once body[data-done="1"] is set.
```

WebCrypto (`crypto.subtle`, used by `aead.ts`, `hkdf.ts`, `keyring.ts`) exists only in a
**secure context**. `http://<container>:5173` is not one; for browser proofs of those modules
run the Playwright container with `--network container:pwdmgr-bench-web` and open
`http://localhost:5173/`, or terminate TLS in front of the dev server. The Argon2 bench page
works either way (`hash-wasm` does not need `crypto.subtle`).

## Current state of tests

- `tests/backend/Pwdmgr.Infrastructure.Tests` (xUnit v3, 18 tests): migrations `Identity` +
  `Sessions` apply on a fresh database, second apply is a no-op, rollback to `0` and forward
  again; unique constraints `tenants(slug)`, `users(tenant_id, email)` (case-insensitive via
  `citext`), `local_credentials(user_id)`; composite FK rejects a credential pointing into
  another tenant; cascade of credentials on user delete; tenant query filter; Argon2id
  hasher KATs (same frozen vectors as the browser), PHC parsing, decoy hash.
- `tests/backend/Pwdmgr.Api.Tests` (xUnit v3 + `WebApplicationFactory`, 23 tests): login cookie
  flags, case-insensitive e-mail, wrong password / unknown user / unknown tenant → 401 in the
  same latency class, `me` without or with garbage cookie → 401, logout revokes the row,
  **session expiry after a real 3-second TTL**, cross-origin POST → 403, storage holds only
  hashes, rate limit → 429 (own fixture with 5 permits), per-client spraying window → 429,
  verifier gate → 503, disabled user locks out sessions, idempotent logout, session purge keeps
  recent rows; keyring enrolment write-once, KDF floor/ceiling → 400, malformed fields → 400
  without echo, vault needs keyring, vault list per holder, cross-tenant invisibility; secret
  create/list/latest/new version/soft delete chain with client-chosen ids and version contract,
  foreign vault/secret → 404 (same and other tenant), 64 KiB payload limit → 413, malformed
  fields → 400. Runs in CI. Verification catalogue:
  [`docs/verification/zielkatalog.md`](docs/verification/zielkatalog.md).
- `src/frontend/src/crypto/*.test.ts` (Vitest): 52 tests — Argon2id KATs, two frozen own
  vectors, NFKC normalisation, parameter floor/ceiling, HKDF KATs, AES-GCM round-trip and tamper
  cases, nonce uniqueness; keyring enrol/unlock round-trip, wrong passphrase, user binding,
  vault-key wrap/unwrap (recipient, context, tamper and low-order rejection, substituted public
  key, RFC 7748 shared secret + frozen vector),
  DEK and payload seal/open. Keyring tests use `KDF_MINIMUM` to stay fast. Runs in CI.
  Verification catalogue: [`docs/verification/zielkatalog.md`](docs/verification/zielkatalog.md).
