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
| Crypto round-trip (Argon2id KDF → AES-GCM) | Browser-realistic | Vitest with jsdom + WebCrypto + Argon2 WASM | yes |
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

## Current state of tests

- `tests/backend/Pwdmgr.Infrastructure.Tests` (xUnit v3, 8 tests): migration `Identity` apply on
  a fresh database, second apply is a no-op, rollback to `0` and forward again; unique
  constraints `tenants(slug)`, `users(tenant_id, email)` (case-insensitive via `citext`),
  `local_credentials(user_id)`; composite FK rejects a credential pointing into another tenant;
  cascade of credentials on user delete. Runs in CI. Verification catalogue:
  [`docs/verification/zielkatalog.md`](docs/verification/zielkatalog.md).
- No frontend test project yet (arrives with the crypto slice #1).
