# STATE — pwdmgr / Privora

> Living, per-session document. Read at session start, update at session end. Source of truth for "what is running, what is open, what is next."

Last update: 2026-05-16

## Snapshot

- **Phase:** project bootstrap. Planning is comprehensive (see [`docs/architecture/product-plan.md`](docs/architecture/product-plan.md)); no production code yet. Skeleton projects compile on CI.
- **Branch:** `main`. Foundation merged via PR [#1](https://github.com/dannybergt/pwdmgr/pull/1) at commit `b41cfde`. CI fully green.
- **Remote:** GitHub `dannybergt/pwdmgr` (PUBLIC).
- **DockerHub namespace:** `dbergt`. **`dbergt/pwdmgr-api` is live** — first multi-arch push (`amd64` + `arm64`) at `:main` and `:sha-b41cfde`. <https://hub.docker.com/r/dbergt/pwdmgr-api>. Other images (`pwdmgr-web`, `pwdmgr-worker`, `pwdmgr-agent-gateway`) follow with their respective service slices.

## What runs / can run locally

| Component | Local build status | Notes |
|---|---|---|
| Backend (.NET 9) | requires .NET SDK 9.0.100 (pinned in `global.json`) | Not installed on this workstation; CI builds via `setup-dotnet@v4`. |
| Frontend (React) | requires Node 24 | Node 24 installed. |
| Browser extension | requires Node 24 | Same. |
| Windows agent | requires .NET SDK | Same as backend. |
| Docker Compose stack | requires Docker | Docker Desktop not installed on this workstation. |

## Ports / shared resources (allocated)

| Port / resource | Owner | Purpose |
|---|---|---|
| 8080 | `infra/compose/compose.yaml` (Traefik) | HTTP entrypoint — preflight-check before `docker compose up`. |
| internal `app` network | compose | API ↔ Traefik. |
| internal `data` network | compose | API ↔ Postgres. |
| Postgres on `data` network | compose service `postgres` | not published to host. |
| volume `pgdata` | compose | Postgres data. |

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

- [ ] First vertical MVP slice: pick scope — current candidate is crypto spike (Argon2id WASM + WebCrypto AES-GCM round-trip), tenant + user domain migration, ciphertext-only Secret CRUD API, React unlock flow.
- [ ] Add Dockerfiles for `pwdmgr-web`, `pwdmgr-worker`, `pwdmgr-agent-gateway` when their services have real content (do NOT add empty placeholder containers — see Constitution §2.5 YAGNI).
- [ ] Decide trademark / domain status for the product working name `Privora` (ADR-0003).
- [ ] Install .NET 9 SDK locally so `dotnet build` is possible without CI round-trip.
- [ ] Decide on Docker Desktop vs. Rancher Desktop for local container work.
- [ ] `src/frontend/package.json` pins every dep to `"latest"` (Constitution §10 violation in the source-of-truth file even though `package-lock.json` is deterministic). Replace `"latest"` with explicit versions in a follow-up.

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
