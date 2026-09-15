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
| Application services (use cases) | Unit + module-integration | xUnit + Testcontainers (Postgres) | yes |
| API controllers / endpoints | Integration | `WebApplicationFactory` + Testcontainers | yes |
| Persistence / EF Core mappings | Integration on real Postgres | Testcontainers | yes |
| LDAP / AD adapters | Integration against `osixia/openldap` test container | Testcontainers | Phase 2 |
| Crypto adapters | Known-answer tests + interop with browser WASM | xUnit + golden vectors | yes |
| Auth / MFA / step-up flows | Integration | xUnit + Testcontainers | yes |
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

## Frontend crypto tests and KDF benchmark

`src/frontend/src/crypto/*.test.ts` run with `npm test` (Vitest, Node 24 — WebCrypto and the
`hash-wasm` Argon2id build behave the same as in browsers; a real-browser run is still required
for the benchmark, see below). Known-answer vectors:

- Argon2id: seven vectors from the reference implementation's `src/test.c` (v=0x13, no secret,
  no associated data) plus one frozen vector at `KDF_DEFAULT` — the cross-implementation anchor
  for the .NET agent. RFC 9106 §5.3 is not used because its only Argon2id vector needs
  associated data, which neither `hash-wasm` nor this KDF exposes.
- HKDF-SHA256: RFC 5869 test cases 1 and 3.
- AES-256-GCM: round-trip, tampered AAD / ciphertext / tag / wrong key → rejection, 10 000
  distinct nonces.

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

## Current state of tests

- `src/frontend/src/crypto/*.test.ts` (Vitest): 31 tests — Argon2id KATs, own frozen vector,
  NFKC normalisation, HKDF KATs, AES-GCM round-trip and tamper cases, nonce uniqueness. Runs in CI.
  Verification catalogue: [`docs/verification/zielkatalog.md`](docs/verification/zielkatalog.md).
- No backend test project yet (arrives with the persistence slice #2).
