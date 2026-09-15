# OPERATIONS — pwdmgr / Privora

Status: living document — operating handbook.

Last update: 2026-09-15

## Deployment targets

| Target | Phase | Status |
|---|---|---|
| Docker Compose (single host, **development / evaluation**) | MVP | `infra/compose/compose.yaml`: Traefik (TLS on 8443 with the built-in self-signed cert, http only on 127.0.0.1:8080 → redirect), API, web, Postgres. Development mode, seed admin, credentials from `infra/compose/.env`. Not a production deployment — see "Operating the compose stack". |
| Helm chart on Kubernetes | Enterprise roadmap (Phase 3) | `infra/k8s/` empty. |
| OpenShift / Rancher | Enterprise roadmap (Phase 3+) | not addressed. |

For deep dive see [`docs/architecture/product-plan.md`](docs/architecture/product-plan.md) §§ 18 and 32–33.

## Container registry

All images publish to Docker Hub namespace `dbergt`. Naming convention is `dbergt/pwdmgr-<service>` and is recorded in [ADR-0004](docs/adr/0004-dockerhub-naming-and-sync-strategy.md).

| Image | Status | Source |
|---|---|---|
| `dbergt/pwdmgr-api` | live | `infra/compose/docker/api.Dockerfile` |
| `dbergt/pwdmgr-web` | live from the web slice | `infra/compose/docker/web.Dockerfile` (nginx-unprivileged, static client) |
| `dbergt/pwdmgr-worker` | planned | follows with LDAP sync / rotation worker |
| `dbergt/pwdmgr-agent-gateway` | planned | may stay merged into `pwdmgr-api` until extraction is justified |

Image tagging: `:main` for latest main-branch build, `:vX.Y.Z` for semver release tags, `:sha-<short>` for traceability. Multi-arch (`linux/amd64`, `linux/arm64`) via `docker buildx`.

## Required secrets (operating the platform)

| Secret | Where | Purpose |
|---|---|---|
| `POSTGRES_PASSWORD` | `infra/compose/.env` (dev stack, git-ignored), K8s Secret in cluster | Postgres `pwdmgr` user. **Never** in the image, never in repo. |
| `SEED_ADMIN_PASSWORD` | `infra/compose/.env` (dev stack only) | Password of the Development-only seed admin `admin@dev.local`; the seed is ignored outside Development. |
| `ASPNETCORE_KESTREL__CERTIFICATES__DEFAULT__PASSWORD` | secret store | Local TLS cert password if used. |
| `PWDMGR_PEPPER` | secret store / KMS / HSM-backed | Optional pepper for Argon2id login hashing. Not derivable from DB. |
| `PWDMGR_AUDIT_HASH_CHAIN_SEED` | secret store | Initial seed for audit log hash chain. |

These are **operating** secrets (the platform itself needs them). They are completely separate from **user vault secrets**, which never reach the server unencrypted.

## Required GitHub Actions secrets (CI)

| Secret | Value | Purpose |
|---|---|---|
| `DOCKERHUB_USERNAME` | `dbergt` | Docker Hub login. |
| `DOCKERHUB_TOKEN` | PAT with `Read, Write` scope on `dbergt/pwdmgr-*` | Docker Hub push. **Never** a long-lived password. |

To create the PAT: <https://hub.docker.com/settings/security> → "New Access Token" → restrict to needed repos. Configure in GitHub: <https://github.com/dannybergt/pwdmgr/settings/secrets/actions>.

## Observability

| Concern | Tooling | Status |
|---|---|---|
| Structured logs (JSON, UTC ISO-8601, request ID) | ASP.NET Core `JsonConsole` with scopes; audit category `Pwdmgr.Audit.Auth` (login/logout outcomes, no e-mail/password/token) | implemented (API) |
| Liveness probe | `GET /health/live` (runs no checks) | implemented |
| Readiness probe | `GET /health/ready` (Postgres via EF Core check, 503 when down) | implemented |
| Metrics | Prometheus scrape endpoint `/metrics` | not implemented — Traefik answers 418 on `/metrics` so nothing reads the SPA as a metrics page; planned: `OpenTelemetry.Exporter.Prometheus.AspNetCore` + Traefik `--metrics.prometheus` |
| Traces | OpenTelemetry export | Phase 2 |
| Audit export to SIEM | Syslog / JSON webhook / Splunk HEC / Sentinel / Elastic | Phase 2 (see product-plan §§ 16 and 21) |

Operating without these is acceptable in dev. They are **mandatory** before any production deployment.

## Operating the compose stack (development / evaluation)

Everything below runs on the shared Docker daemon with project name `pwdmgr` (fixed in
`compose.yaml`, so `-p` is optional). Ports: 8080 (http, 127.0.0.1 only, redirects to https;
`/health` is served plain for probes), 8443 (https). Preflight before every start:
`ss -ltn | grep -E ':(8080|8443) '` must be empty.

```sh
cd infra/compose
cp .env.example .env                      # once; dev-only credentials, git-ignored
docker compose up -d --build              # start / rebuild after code changes
docker compose ps                         # every service reports (healthy) after ~40 s
curl -sk https://localhost:8443/health/ready      # Healthy
docker compose logs -f api                # JSON lines; audit events: Category Pwdmgr.Audit.Auth / Pwdmgr.Audit.Vault
docker compose restart api                # picks up env changes; migrations re-run idempotently
docker compose down                       # stops containers, keeps the pgdata volume
```

- **What "healthy" means:** api = `/health/ready` answered 200 (Postgres reachable); web = nginx serves `/`; reverse-proxy = Traefik ping; postgres = `pg_isready`. Traefik also probes the API every 10 s and takes it out of routing (503) when the probe fails — a 503 from `/api` with `docker compose ps` showing `api (healthy)` is Traefik's probe lagging by one interval.
- **Restart policy** `unless-stopped`: after a host reboot the stack comes back by itself; a crashed API is restarted by Docker.
- **Reading a problem:** every API error response carries `traceId`; grep the API log for that value to get the request's log scope. Login attempts appear as `Login outcome=… client=…` (never the e-mail or password); rate limiting as `outcome=rate_limited|account_throttled|busy`.
- **Known log noise:** one EF Core `Error` line `Failed executing DbCommand … __EFMigrationsHistory` on the very first start against an empty database; DataProtection warnings `No XML encryptor configured` / `Storing keys in a directory …` at startup (unused by the opaque-token sessions). Traefik logs `aliasHeadersStrategy is not configured` once per entrypoint.
- **Client address behind the proxy:** the API trusts `X-Forwarded-*` only from `PWDMGR_PROXY_NETWORK` (default `172.16.0.0/12`). If `docker network inspect pwdmgr_app` shows another subnet, set it in `.env` — otherwise every client counts as one for the login throttle and cookies are never `Secure`. The startup log line `Forwarded headers trusted from …` shows what is in effect.
- **Backup of the dev database:** `docker compose exec -T postgres pg_dump -U pwdmgr pwdmgr > pwdmgr-$(date +%F).sql`; restore into an empty database with `docker compose exec -T postgres psql -U pwdmgr -d pwdmgr < file.sql`. Backups hold ciphertext only.
- **Schema rollback is destructive:** `dotnet ef database update <earlier migration>` drops the crypto tables (`user_keyrings`, `vaults`, `wrapped_keys`, `secrets`, `secret_versions`) and the server cannot reconstruct their content. Rollback = redeploy the previous image and keep the schema; run `Down()` only on an empty deployment or after a fresh `pg_dump`.
- **Emergency stop:** `docker compose stop reverse-proxy` cuts all external access while the API and database keep running.
- **Metrics:** none yet; `/metrics` answers 418 from Traefik so a scraper never sees a false 200 (see Observability).
- **Not for the network:** the stack is Development mode with a seed admin and a self-signed certificate. A reachable deployment needs a real certificate (Traefik `tls.certificates` or ACME in `traefik/dynamic.yaml`), `ASPNETCORE_ENVIRONMENT=Production`, `SEED_ENABLED=false` and its own credentials.

## Backup / restore

| Object | Backup method | Restore test |
|---|---|---|
| PostgreSQL (ciphertext + metadata + audit) | continuous WAL archiving + nightly base backup, PITR-capable | restore drill at least quarterly |
| Object storage (encrypted attachments) | provider snapshots or S3 versioning + replication | sample restore drill quarterly |
| Operating secrets (pepper, audit chain seed) | secured at provisioning time, recovery procedure documented separately | manual restore drill per release |

Backups contain **only ciphertext and hashed credentials**. Vault payloads cannot be decrypted from a backup alone — even by the operator running the restore.

## Disaster recovery

- RTO target: 4 hours for MVP-scale deployments. Driven by Postgres restore + container redeploy.
- RPO target: 15 minutes via WAL archiving.
- Loss of operating secrets without recovery: requires re-issuing tenant recovery keys; vault payloads remain confidential but become unreadable until users re-establish their master keys.
- Loss of a user's master passphrase: only recoverable via M-of-N enterprise recovery if the user / tenant had enrolled in it beforehand.

## Update / rollback

- Migrations follow expand / contract (AGENTS.md §9): schema first, code second, cleanup third.
- Every release tag triggers a container build with both moving (`:main`) and immutable (`:vX.Y.Z`, `:sha-<short>`) tags.
- Rollback = redeploy a previous immutable tag plus, if needed, an `ef migrations` down step. Rollback paths are listed per release in the changelog.
- No `--no-verify` and no force-push to `main`. See Constitution §14.

## Incident response

Pre-production. Once production exists:

- Security contact, vulnerability disclosure process, supported versions and patch SLA must be defined in [SECURITY.md](SECURITY.md). Currently flagged as "not configured yet" there.
- On suspected key / agent / admin compromise: rotate group keys for affected vaults, revoke agent tokens, revoke sessions, rotate operating secrets, audit the chain forward from the incident window.

## Run-book stubs (to fill before first production deploy)

- [x] TLS: Traefik terminates TLS on `:8443` (self-signed default certificate, HSTS); http on `127.0.0.1:8080` redirects to https except `/health`. **The web client needs a secure context (WebCrypto).** Production: mount a real certificate (Traefik `tls.certificates` in the dynamic file or ACME).
- [ ] How to apply EF Core migrations safely. Current state: `Database:MigrateOnStartup=true` only in the compose dev stack (`Database__MigrateOnStartup`); production images default to `false` and migrate as an explicit deploy step (ADR-0007).
- [ ] How to rotate `PWDMGR_PEPPER` (requires re-hash on next login, not full re-encrypt). Not introduced yet (`pepper_version` = 0).
- [ ] Session settings: `Auth__SessionTtl` (default 8 h, absolute), `Auth__CookieSecurePolicy` (`Always` default; only the plain-http dev stack uses `SameAsRequest`), `Auth__LoginRateLimitPermits`/`Window`/`LoginRateLimitPermitsPerClient`/`MaxConcurrentVerifications`, `Forwarded__KnownNetworks__0` (proxy CIDR; required behind Traefik for correct client addresses and `Secure` cookies). `Seed__Enabled`/`Seed__AdminPassword` are Development-only.
- [ ] How to rotate `DOCKERHUB_TOKEN`.
- [ ] How to onboard a new tenant admin.
- [ ] How to perform M-of-N recovery for a private vault.
- [ ] How to revoke a Windows agent.
- [ ] How to read the audit hash chain and detect tampering.
