# pwdmgr

Technical codename: `pwdmgr`  
Product working name: `Privora`

`pwdmgr` is the project foundation for a commercial, multi-tenant, zero-knowledge enterprise platform for password management, secrets management, credential handling and future privileged-access-management capabilities.

> **Status: MVP zero-knowledge vertical runnable.** Login → passphrase enrolment/unlock in the browser → create and read secrets, ciphertext-only on the server. Planning is in [`docs/architecture/product-plan.md`](docs/architecture/product-plan.md); what is proven at the running system is in [`docs/verification/zielkatalog.md`](docs/verification/zielkatalog.md).

## Quick start (Docker Compose)

```sh
cd infra/compose
cp .env.example .env                       # dev-only credentials (git-ignored)
docker compose up -d --build               # https on 8443 (self-signed); http on 127.0.0.1:8080 redirects
```

Open <https://localhost:8443/> (accept the self-signed certificate), sign in with the dev seed
(tenant `dev`, `admin@dev.local`, password = `SEED_ADMIN_PASSWORD` from your `.env`), choose a
vault passphrase — it never leaves the browser — and store your first secret. The API is reachable
at `https://localhost:8443/api/v1/…`, health at `/health/ready`. This stack is a development /
evaluation configuration; do not expose it to a network as is ([OPERATIONS.md](OPERATIONS.md)).
Test recipes without `node`/`dotnet` on the host: [TESTING.md](TESTING.md).

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

The MVP vertical (slices #1–#9 of [`docs/developer/mvp-slice-plan.md`](docs/developer/mvp-slice-plan.md)) is done. What follows, per [ROADMAP.md](ROADMAP.md) and [STATE.md](STATE.md): tenant/user onboarding without the dev seed, vault sharing (wrap the vault key for another user's public key), audit events, MFA, the browser extension and the Windows agent.

