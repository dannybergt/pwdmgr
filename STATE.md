# STATE — pwdmgr / Privora

> Living, per-session document. Read at session start, update at session end. Source of truth for "what is running, what is open, what is next."

Last update: 2026-05-16

## Snapshot

- **Phase:** project bootstrap. Planning is comprehensive (see [`docs/architecture/product-plan.md`](docs/architecture/product-plan.md)); no production code yet. Skeleton projects compile; no domain logic.
- **Branch:** `main`. Single commit. Working tree clean.
- **Remote:** GitHub `dannybergt/pwdmgr` (PUBLIC). Local remote being wired up in current session.
- **DockerHub namespace:** `dbergt`. Image repos for pwdmgr planned (see [ADR-0004](docs/adr/0004-dockerhub-naming-and-sync-strategy.md)), not yet published.

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
| GitHub `dannybergt/pwdmgr` | exists, public, empty (no default branch yet). Wiring up `origin` and initial push in current session. |
| Docker Hub `dbergt/pwdmgr-api` | not yet created. Will be auto-created on first successful CI push once `DOCKERHUB_USERNAME` + `DOCKERHUB_TOKEN` secrets are configured on the repo. |
| Docker Hub `dbergt/pwdmgr-web` | not yet created. Dockerfile pending — follows with first frontend vertical slice. |
| Docker Hub `dbergt/pwdmgr-worker` | not yet created. Service implementation pending (LDAP sync / rotation worker). |
| Docker Hub `dbergt/pwdmgr-agent-gateway` | not yet created. May start as a module inside `pwdmgr-api` and extract later (per ADR-0001). |

Required GitHub Actions secrets (to be set by maintainer, not by automation):

- `DOCKERHUB_USERNAME` — value: `dbergt`.
- `DOCKERHUB_TOKEN` — Personal Access Token from <https://hub.docker.com/settings/security> with `Read, Write` scope on `dbergt/pwdmgr-*`.

## Open threads / next steps

- [ ] Maintainer configures `DOCKERHUB_USERNAME` + `DOCKERHUB_TOKEN` GitHub secrets so CI can push images.
- [ ] First vertical MVP slice: pick scope — current candidate is crypto spike (Argon2id WASM + WebCrypto AES-GCM round-trip), tenant + user domain migration, ciphertext-only Secret CRUD API, React unlock flow.
- [ ] Add Dockerfiles for `pwdmgr-web`, `pwdmgr-worker`, `pwdmgr-agent-gateway` when their services have real content (do NOT add empty placeholder containers — see Constitution §2.5 YAGNI).
- [ ] Decide trademark / domain status for the product working name `Privora` (ADR-0003).
- [ ] Install .NET 9 SDK locally so `dotnet build` is possible without CI round-trip.
- [ ] Decide on Docker Desktop vs. Rancher Desktop for local container work.

## Assumptions / decisions deferred

- Argon2id parameters (memory cost, iterations, parallelism) will be benchmarked during the crypto spike; current placeholder is OWASP guidance (m=64MiB, t=3, p=4).
- OIDC/SAML federation: provider-side prep only; production-grade SSO is Phase 2.
- Mobile apps: Phase 4+; not addressed in current code.

## Session log

- 2026-05-16: foundation session. Plan-doc duplicate at `C:/data/codex/enterprise-zero-knowledge-pam-plan.md` deleted (byte-identical to repo copy; repo is SSoT per ADR-0005). Constitution root docs created. ADR-0004 (DockerHub naming) and ADR-0005 (plan SSoT) added. Pre-commit + gitleaks wired up. CI extended with image build/push for `pwdmgr-api`. GitHub `origin` set, initial push, PR opened.
