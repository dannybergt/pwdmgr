# pwdmgr

Technical codename: `pwdmgr`  
Product working name: `Privora`

`pwdmgr` is the project foundation for a commercial, multi-tenant, zero-knowledge enterprise platform for password management, secrets management, credential handling and future privileged-access-management capabilities.

> **Status: project bootstrap.** Comprehensive planning is in [`docs/architecture/product-plan.md`](docs/architecture/product-plan.md). Code is skeleton-level and not yet runnable end-to-end.

## Start here

| Read first | Why |
|---|---|
| [PROJECT_BRIEF.md](PROJECT_BRIEF.md) | What we are building and what is in scope. |
| [ARCHITECTURE.md](ARCHITECTURE.md) | High-level topology, modules, tech baseline. |
| [STATE.md](STATE.md) | Current session state, what runs, open threads. |
| [ROADMAP.md](ROADMAP.md) | Phase 1 to Phase 5. |
| [DECISIONS.md](DECISIONS.md) | ADR index. |
| [TESTING.md](TESTING.md) | Test strategy and CI gates. |
| [OPERATIONS.md](OPERATIONS.md) | Deployment, secrets, backup, DR. |
| [SECURITY.md](SECURITY.md) | Security baseline and reporting. |
| [`docs/architecture/product-plan.md`](docs/architecture/product-plan.md) | Single source of truth, full plan (long). |

## Repository Layout

```text
src/backend/         ASP.NET Core backend modules (modular monolith)
src/frontend/        React web frontend (WebCrypto + Argon2id WASM)
src/extension/       Browser extension (MV3)
src/agent/           Windows agent, CLI and PowerShell module
infra/compose/       Docker Compose deployment (MVP / lab)
infra/k8s/           Future Kubernetes / Helm assets
docs/architecture/   Product and system architecture
docs/adr/            Architecture decision records
docs/admin/          Administrator documentation
docs/user/           User documentation
docs/developer/      Developer documentation
docs/operations/     Operations handbook
docs/security/       Threat model and security controls
tests/               Automated tests
tools/               Development and release tooling
```

## Repositories and sync

| Channel | Where | Notes |
|---|---|---|
| Source | <https://github.com/dannybergt/pwdmgr> | public; CI runs on every push and PR. |
| Container images | Docker Hub namespace [`dbergt`](https://hub.docker.com/u/dbergt) | pattern `dbergt/pwdmgr-<service>`. See [ADR-0004](docs/adr/0004-dockerhub-naming-and-sync-strategy.md). |

Planned images:

- `dbergt/pwdmgr-api` — the modular monolith API (this is what CI publishes first).
- `dbergt/pwdmgr-web` — static frontend assets behind a reverse proxy.
- `dbergt/pwdmgr-worker` — background workers (LDAP sync, rotation).
- `dbergt/pwdmgr-agent-gateway` — mTLS endpoint for the Windows agent (may start inside `pwdmgr-api` and extract later).

Tags: `:main` (moving), `:vX.Y.Z` (immutable releases), `:sha-<short>` (per-commit traceability). Multi-arch `linux/amd64` + `linux/arm64`.

## Important Security Principles

- Login passwords are never reversibly stored.
- Local login passwords use Argon2id with salt and optional pepper.
- Vault secrets and protected files are decryptable only on trusted clients.
- Server-side services must never receive plaintext secrets.
- Global admins, tenant admins and support admins must not have secret-reading backdoors.
- Recovery is possible only through explicit multi-party cryptographic enrolment.

See [ADR-0002](docs/adr/0002-zero-knowledge-is-non-negotiable.md) for the underlying constraint and [SECURITY.md](SECURITY.md) for the baseline.

## Contributing

- Branch off `main`. Names follow `feature/…`, `fix/…`, `security/…`, `docs/…`, `refactor/…`, `chore/…` per the project Constitution.
- Run `pre-commit install` once locally (requires Python). The hooks run gitleaks, EditorConfig, line-ending and large-file checks before every commit.
- PRs use the §15 template (Summary / Changes / Verification / Security Review / Documentation / Migration / Risks).
- Never commit secrets, even into examples — `.env.example` is the only sanctioned placeholder file.

## Next Work Packages

1. Configure `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN` GitHub Actions secrets so CI can publish images.
2. Build crypto spike for WebCrypto AES-GCM and Argon2id WASM round-trip.
3. Implement tenant + user domain and initial EF Core migrations.
4. Implement ciphertext-only Secret CRUD API.
5. Add React unlock-flow prototype.

