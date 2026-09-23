<!-- state: v2 -->
# STATE — pwdmgr / Privora

Format: `docs/state-v2.md` in agent-baseline (headings German by format, content English). History up to 2026-09-18: [`docs/history/STATE-2026.md`](docs/history/STATE-2026.md).

## Jetzt

- **Phase:** post-MVP wave A (slices #10–#14: lint gate, onboarding, shared vaults, web UI) is implemented, reviewed (R/S/ops/migration) and verifier-proven on the local compose stack (P11–P14 PROVEN, Playwright 4/4, catalogue 0 OPEN) — **but only on a local, unpushed branch stack** in `/claude/pwdmgr`. Push + PRs wait for the operator's go.
- **`main`** = `768f4c0` (measured 2026-09-23): MVP slices #1–#9 (merged 2026-09-15 as #24/#25, #28–#32) plus Dependabot #34/#35/#36/#39 and STATE #40. No release tag (dev-stack defaults: seed admin, self-signed cert).
- **Local stack** (measured 2026-09-23, none of it on `origin`): `docs/post-mvp-slice-plan` `a17b3b5` → `feature/frontend-lint` `532052e` (#10) → `feature/onboarding-api` `7158c0c` (#11) → `feature/sharing-api` `c0273ae` (#12 + negative controls) → `feature/web-onboarding` `a79d55f` (#13) → `feature/web-sharing` `194b5da` (#14 + follow-ups, 16 commits ahead of `main`). Plan: `docs/developer/post-mvp-slice-plan.md` (on the stack, not on `main`).
- **Try it:** `cp .env.example .env && docker compose up -d --build` in `infra/compose`, open <https://localhost:8443/>, sign in with the dev seed (server holds ciphertext only).
- **Images:** `dbergt/pwdmgr-api` and `dbergt/pwdmgr-web` live on Docker Hub, `:main` = `sha-b96b089` (MVP). `pwdmgr-worker` / `pwdmgr-agent-gateway` only once they have real content (§2.5).
- **Remote:** GitHub `dannybergt/pwdmgr` (public). Actions secrets `DOCKERHUB_USERNAME`, `DOCKERHUB_TOKEN` set (verified 2026-05-16).
- **Tooling on `dev-claude`:** no `dotnet`/`node` on the host — SDK and `node:24-alpine` containers per TESTING.md; SELinux → bind mounts need `:z`.

## Nächster Schritt

- [mensch:entscheidung] Go for pushing the local wave-A stack and opening the stacked PRs (docs → #10 … #14, each targeting the previous); PR bodies: regenerate from commit messages + history.
- [mensch:entscheidung] Product questions to settle in those PRs: one-time password shown on the admin's screen/clipboard (A6); Viewers can see member e-mails; shares arrive without an accept step.
- [mensch:entscheidung] Order of this STATE-v2 PR vs. the stack: the stack's top commits (`1e0640f`, `194b5da`, `6947c56`, `a17b3b5`) rewrite the old-format `STATE.md` and will conflict — resolve by keeping the v2 file and folding their news into it.
- [mensch:FREIGABE] Merge the wave-A PRs in order once opened (squash; `main` into the next branch, tree-diff-0 check, close/reopen for CI).
- [mensch:FREIGABE] Dependabot #42 (build-push-action 7.4.0), #43 (@types/chrome 0.3.0), #44 (postgres 16.14, supersedes closed #41), #45 (frontend minor/patch group) — all CI green and mergeable (measured 2026-09-23).
- [auto:p2] Node deployment slice `chore/node-deploy` once wave A is on `main`: root `docker-compose.yml` = `include:` of `infra/compose/compose.yaml` + `infra/compose/compose.node.yaml` (published `dbergt/pwdmgr-*:${IMAGE_TAG:-main}`, `build: !reset null`, Watchtower label, `env_file: infra/compose/.env`), node `.env` template (`SEED_ENABLED=false`, `BOOTSTRAP_*`, `PWDMGR_HTTPS_PORT=8447`, `PWDMGR_TRUSTED_PROXIES`), ADR "node runs published images via a root wrapper". Details in the history file ("Next (3)").
- [mensch:entscheidung] Operator input for the node: outer proxy IP for `PWDMGR_TRUSTED_PROXIES=<ip>/32` (still open as of 2026-09-18).
- [mensch:FREIGABE] Merge `chore/node-deploy` — git-sync on BC-KI01 rolls it out = production deploy.
- [betreiber:bc-ki01] Open port 8447 in the Nexainer firewall UI (LAN-CIDR whitelist must include the outer proxy); outer proxy → `https://<node>:8447`, certificate check off; then P14-08 probe.
- [betreiber:dev-claude] Clean up the dev stack leftovers the guard blocks for agents: `dropdb pwdmgr_copy` inside volume `pwdmgr_pgdata`, and the dangling verifier volume `9f2fa0da11f6…` (`docker volume ls -f dangling=true`) — both still present (measured 2026-09-23).
- [auto:p3] After wave A: wave B (audit events with hash chain AP-019, vault-key rotation + ownership transfer, NuGet lockfile + `--locked-mode`, CodeQL), then waves C/D as sketched at the end of the post-MVP plan.
- [mensch:entscheidung] Trademark / domain status of the working name `Privora` (ADR-0003) before any public release.

## Offene Threads

- Local stack unpushed since 2026-09-15/18; the node deployment is blocked on it (otherwise the node would run the MVP without sharing/onboarding).
- CI `e2e` job runs all specs against a fresh DB (seed admin, deterministic passphrase); local spec runs against the long-lived volume need a fresh TenantAdmin via `E2E_EMAIL`/`E2E_PASSWORD` (TESTING.md).
- git-sync on BC-KI01 was FAILED because of **pulsight** (`POSTGRES_PASSWORD` missing in its node `.env`) — not this project; stand 2026-09-18, not re-measured. BC-KI01 has `pwdmgr` registered `active` with checkout `/data/pwdmgr` on `main`, no root compose, no `.env`, no containers (stand 2026-09-18).
- Not solvable here: a real TLS certificate for a non-localhost deployment (Traefik `tls.certificates`/ACME, OPERATIONS.md) — the node plan terminates TLS at the outer proxy.
- Roadmap-sized review follow-ups (not blockers): per-tenant storage quotas, MFA, retention/purge of soft-deleted secret versions, `/metrics` (answers 418 until then), image pinning by digest, composite primary keys (409 existence oracle), Ed25519 pinning, groups, invitations.
- Extension compiles with `tsc` only (no bundler); fine while `background.ts`/`content.ts` stay import-free. Revisit when a bundler arrives.
- Deferred decisions: Argon2id `KDF_DEFAULT` m=64 MiB/t=3/p=4, `KDF_MINIMUM` m=19 MiB/t=2/p=1 (ADR-0006), lower per-device params for mobile later; OIDC/SAML is Phase 2; mobile apps Phase 4+.

## Ressourcen

Measured 2026-09-23 on `dev-claude`: no `pwdmgr` containers running, 8080/8443/8447 not listening.

| Port / resource | Owner | Purpose |
|---|---|---|
| 8080 (127.0.0.1) | `infra/compose` Traefik | HTTP entrypoint, `/health` plain, rest 301 → https. Preflight before `compose up`. |
| 8443 (`PWDMGR_HTTPS_PORT`; `127.0.0.1:<port>` behind an outer proxy) | `infra/compose` Traefik | HTTPS entrypoint, self-signed default cert (web client needs a secure context). |
| 8447 on BC-KI01 | planned node deployment | operator's chosen host port (not yet in use). |
| networks `app`, `data` (internal) | compose | API ↔ Traefik, API ↔ Postgres; Postgres not published. |
| volume `pwdmgr_pgdata` | compose | dev Postgres; holds verifier users (`v.*`, `v12.*`, `v14.*`, `v15.*`, `e2e.*`, `bob+*` in tenant `dev`) and copy DB `pwdmgr_copy`. `infra/compose/.env` (git-ignored) carries the matching dev seed values. |
| volume `pwdmgr-nuget` | SDK container builds | NuGet cache, host uid. |
| network `pwdmgr-test`, container `pwdmgr-test-postgres` | backend tests | throw-away, currently removed; recreate per TESTING.md. |
| network `pwdmgr-bench`, container `pwdmgr-bench-web` (5173, unpublished) | KDF benchmark | transient, one-off; remove after the run. |
| anonymous volume `9f2fa0da11f6…` | verifier's throw-away Postgres (2026-09-18) | dangling, operator removal pending. |
| local images `pwdmgr-api`, `pwdmgr-web` | built from `6e2d0e3` | dev stack. |

No production environment allocated.

## Letzte Session

- 2026-09-23: STATE v2 migration (`state-compact`, autonomous subagent). Source was the newest STATE on the local branch `feature/web-sharing` (`194b5da`), not the older one on `main`; checked against `gh`/`git`: Dependabot #41 closed (superseded by #44), #42–#45 new and green, stack still unpushed, no competing STATE PR open. Old file archived verbatim to `docs/history/STATE-2026.md`. The local stack was not touched.
- davor: session of 2026-09-18 (ended 18:05Z) — #14 review follow-ups, outer-proxy client-address fix, verifier full + delta pass, catalogue 0 OPEN; see `docs/history/STATE-2026.md` ("Session log").
