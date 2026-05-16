# TESTING — pwdmgr / Privora

Status: living document — strategy and current state.

Last update: 2026-05-16

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
- Pass unit tests (when projects exist).

The CI workflow currently in place ([.github/workflows/ci.yml](.github/workflows/ci.yml)) covers the build step. Test execution will be added as test projects come into the repo.

## Current state of tests

- No test projects exist yet under `tests/backend` or `tests/frontend`. Placeholders to be added with the first vertical slice. Tracked in [STATE.md](STATE.md) → "Open threads".
