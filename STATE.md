# STATE — pwdmgr / Privora

> Living, per-session document. Read at session start, update at session end. Source of truth for "what is running, what is open, what is next."

Last update: 2026-09-15

## Snapshot

- **Phase:** MVP wave 1 in progress. Slice #2 (persistence foundation, ADR-0007) implemented on `feature/persistence-foundation`; slice #1 (crypto primitives, ADR-0006) on `feature/crypto-primitives`. Plan: [`docs/developer/mvp-slice-plan.md`](docs/developer/mvp-slice-plan.md).
- **Branch:** `main`. 2026-09-14: PR [#3](https://github.com/dannybergt/pwdmgr/pull/3) (dependency pins) plus Dependabot batch (#4, #6, #7, #9, #10, #13, #15) and #12/#20 squash-merged on `FREIGABE`. CI green on all five jobs. Foundation merged via PR [#1](https://github.com/dannybergt/pwdmgr/pull/1).
- **Remote:** GitHub `dannybergt/pwdmgr` (PUBLIC).
- **DockerHub namespace:** `dbergt`. **`dbergt/pwdmgr-api` is live** — first multi-arch push (`amd64` + `arm64`) at `:main` and `:sha-b41cfde`. <https://hub.docker.com/r/dbergt/pwdmgr-api>. Other images (`pwdmgr-web`, `pwdmgr-worker`, `pwdmgr-agent-gateway`) follow with their respective service slices.

## What runs / can run locally

| Component | Local build status | Notes |
|---|---|---|
| Backend (.NET 9) | requires .NET SDK 9.0.100 (pinned in `global.json`) | Not installed on host `dev-claude`; build/test in `mcr.microsoft.com/dotnet/sdk:9.0` with the `pwdmgr-nuget` cache volume (see TESTING.md). CI builds via `setup-dotnet@v6`. |
| Frontend (React) | requires Node 24 | No `node` on host `dev-claude`; build via `docker run --rm -u "$(id -u):$(id -g)" -e HOME=/tmp -v "$PWD/src/frontend:/w:z" -w /w node:24-alpine sh -c 'npm ci && npm run build'`. |
| Browser extension | requires Node 24 | Same container pattern with `src/extension`. |
| Windows agent | requires .NET SDK | Same as backend. |
| Docker Compose stack | requires Docker | Docker (native) on `dev-claude`; SELinux → bind mounts need `:z`. Started and verified 2026-09-15 (`docker compose -p pwdmgr up -d --build`). Traefik uses a file provider, no Docker socket. |

## Ports / shared resources (allocated)

| Port / resource | Owner | Purpose |
|---|---|---|
| 8080 | `infra/compose/compose.yaml` (Traefik) | HTTP entrypoint — preflight-check before `docker compose up`. |
| internal `app` network | compose | API ↔ Traefik. |
| internal `data` network | compose | API ↔ Postgres. |
| Postgres on `data` network | compose service `postgres` | not published to host. |
| volume `pgdata` | compose | Postgres data. |
| network `pwdmgr-test`, container `pwdmgr-test-postgres` (no host port) | backend tests | throw-away; may stay up between sessions. |
| volume `pwdmgr-nuget` | SDK container builds | NuGet package cache, owned by the host uid. |

No production environments allocated.

## Sync state — GitHub / Docker Hub

| Target | Status |
|---|---|
| GitHub `dannybergt/pwdmgr` | public, default branch `main`, CI green on every push. |
| Docker Hub `dbergt/pwdmgr-api` | **live**, public, multi-arch (`amd64` + `arm64`), tags `:main` and `:sha-<short>`. <https://hub.docker.com/r/dbergt/pwdmgr-api> |
| Docker Hub `dbergt/pwdmgr-web` | not yet created. Dockerfile pending — follows with first frontend vertical slice. |
| Docker Hub `dbergt/pwdmgr-worker` | not yet created. Service implementation pending (LDAP sync / rotation worker). |
| Docker Hub `dbergt/pwdmgr-agent-gateway` | not yet created. May start as a module inside `pwdmgr-api` and extract later (per ADR-0001). |

GitHub Actions secrets configured (verified 2026-05-16):

- `DOCKERHUB_USERNAME` (set 2026-05-16T22:08:57Z) — value `dbergt`.
- `DOCKERHUB_TOKEN` (set 2026-05-16T22:10:49Z) — Docker Hub PAT, `Read, Write` on `dbergt/pwdmgr-*`. Rotate as part of the regular credential lifecycle.

## Open threads / next steps

- [ ] MVP slices per [`docs/developer/mvp-slice-plan.md`](docs/developer/mvp-slice-plan.md). **#2 done** (PR pending): EF Core + Npgsql, `tenants`/`users`/`local_credentials`, migration `Identity`, readiness check, JSON logs, 8 DB tests, ADR-0007, verification catalogue `docs/verification/zielkatalog.md`. **#1 done** (PR pending, separate branch). **Next: wave 2** — #4 local login + sessions (ADR-0008, critical path) ∥ #3 keyring crypto.
- [ ] `/api/v1/platform/info` carries no version field; add one so the verifier can identify the running artefact (verifier finding).
- [ ] Add Dockerfiles for `pwdmgr-web`, `pwdmgr-worker`, `pwdmgr-agent-gateway` when their services have real content (do NOT add empty placeholder containers — see Constitution §2.5 YAGNI).
- [ ] Decide trademark / domain status for the product working name `Privora` (ADR-0003).
- [x] `dotnet build`/`dotnet test` run locally in the SDK container (TESTING.md); no host install needed.
- [ ] Decide on Docker Desktop vs. Rancher Desktop for local container work.
- [x] `"latest"` pins in `src/frontend` and `src/extension` replaced by explicit versions; extension lockfile + CI job + Dependabot added (PR #3, merged 2026-09-14 as `1595119`). Dependabot's first run started immediately after merge — expect a batch of update PRs (npm, NuGet, Actions, Docker) that need triage.
- [ ] Frontend `package.json` lists `vite`, `typescript`, `@vitejs/plugin-react` under `dependencies`; they belong in `devDependencies`. Deliberately not moved in the pin PR (keeps that diff to what it claims). Do it with the first real frontend slice.
- [ ] Extension is compiled with `tsc` only (no bundler); `moduleResolution: Bundler` is fine as long as `background.ts`/`content.ts` stay import-free. Revisit when a bundler is introduced.

## Assumptions / decisions deferred

- Argon2id parameters (memory cost, iterations, parallelism) will be benchmarked during the crypto spike; current placeholder is OWASP guidance (m=64MiB, t=3, p=4).
- OIDC/SAML federation: provider-side prep only; production-grade SSO is Phase 2.
- Mobile apps: Phase 4+; not addressed in current code.

## Session log

- 2026-05-16: foundation session. Plan-doc duplicate at `C:/data/codex/enterprise-zero-knowledge-pam-plan.md` deleted (byte-identical to repo copy; repo is SSoT per ADR-0005). Constitution root docs created. ADR-0004 (DockerHub naming) and ADR-0005 (plan SSoT) added. Pre-commit + gitleaks wired up. CI extended with image build/push for `pwdmgr-api`. GitHub `origin` set, initial push, PR <https://github.com/dannybergt/pwdmgr/pull/1> opened.
- 2026-05-16: three follow-up fix commits on the same branch resolved pre-existing CI breakage that surfaced on the first remote build:
  - missing `Microsoft.Extensions.{DependencyInjection,Configuration,Hosting,Hosting.WindowsServices}` package references in `Pwdmgr.Application`, `Pwdmgr.Infrastructure`, `Pwdmgr.Agent.Service`;
  - frontend `package-lock.json` generated and CI switched to `npm ci` (Constitution §10);
  - frontend `tsconfig.json` moduleResolution moved to `Bundler` (was deprecated `Node`); `vite-env.d.ts` added for CSS side-effect imports;
  - `*.tsbuildinfo` added to `.gitignore`.
  All four CI jobs (`backend`, `frontend`, `secret-scan`, `docker-api`) green at commit `360f14e`.
- 2026-05-16: PR #1 squash-merged to `main` as commit `b41cfde`. Maintainer configured `DOCKERHUB_USERNAME` + `DOCKERHUB_TOKEN` secrets. Post-merge `push` event triggered the `docker-api` job which built multi-arch with QEMU (4:08) and pushed `dbergt/pwdmgr-api` to Docker Hub with tags `:main` and `:sha-b41cfde`. First image is live at <https://hub.docker.com/r/dbergt/pwdmgr-api>.
- 2026-09-14: §10 clean-up session on host `dev-claude` (no `node`/`dotnet` on host; Node work runs in `node:24-alpine` containers). Branch `chore/pin-node-deps`: `"latest"` → caret ranges from the lockfile (frontend, no resolved version changed) and `^6.0.3` for the extension; first `src/extension/package-lock.json`. Pinning TypeScript 6 exposed that the extension never compiled: `moduleResolution: Node` deprecated (→ `Bundler`, like frontend) and TS 6 no longer auto-includes `@types` (→ `@types/chrome` + `types: ["chrome"]`). Added `extension` CI job and `.github/dependabot.yml` (npm×2, nuget, github-actions, docker). Verified `npm ci && npm run build` in container for both projects; gitleaks (v8.21.2, container) clean on staged changes. `reviewer`: no blockers (one nit applied). `verifier`: 4/4 criteria proven with negative controls, no gaps. PR #3 opened, all five CI checks green, squash-merged to `main` as `1595119` on `FREIGABE`; no release tag (chore). Next: plan the first vertical MVP slice (crypto spike) via `planner`.
- 2026-09-14 (cont.): Dependabot first run triaged. Closed platform majors with rationale (#5/#8 .NET base images 9→10, #11/#14 TypeScript 6→7, #16/#17/#19 `Microsoft.Extensions.*` 9→10) and added `ignore` rules for them (#20). Routine bumps merged on `FREIGABE`: frontend minor/patch group (#13: react 19.3, vite 8.3, eslint 10.10), NuGet 9.0.0→9.0.20 (#15), Actions majors (#4 gitleaks-action v3, #6 checkout v7, #7 setup-dotnet v6, #9 setup-node v7, #10 setup-qemu v4). #7/#9 needed a `@dependabot rebase` after the `checkout` bump. `planner` produced the MVP slice plan → `docs/developer/mvp-slice-plan.md`. No release tag (no product code yet).
- 2026-09-15: wave 1 of the MVP slice plan, two worktrees in parallel. **Slice #2** (`feature/persistence-foundation`): EF Core 9 + Npgsql 9.0.4 + EFCore.NamingConventions; `Entity`/`TenantScopedEntity` split (Tenant lost its bogus `TenantId`); `User`, `LocalCredential` (PHC hash only, no separate params column — ADR-0007); migration `Identity` with composite `(tenant_id, id)` FK convention and `citext` e-mail (reviewer findings); `Database:MigrateOnStartup`; `/health/live` (no checks) and `/health/ready` (Postgres); JSON console logging. Compose had never been started: Traefik ran the Docker provider without a socket → all routes 404; switched to a file provider. xUnit v3 tests on a throw-away DB per class via `PWDMGR_TEST_PG`, fail (not skip) under `CI=true`. `verifier`: 10/10 catalogue rows proven at `117e0a8` with negative controls (first run 6/6 at `e819c9b`). Known noise: EF 9 logs one Error line on first migrate against an empty DB.
