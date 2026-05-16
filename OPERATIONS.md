# OPERATIONS — pwdmgr / Privora

Status: living document — operating handbook.

Last update: 2026-05-16

## Deployment targets

| Target | Phase | Status |
|---|---|---|
| Docker Compose (single host, lab / on-prem evaluation) | MVP | `infra/compose/compose.yaml` exists, minimal services. |
| Helm chart on Kubernetes | Enterprise roadmap (Phase 3) | `infra/k8s/` empty. |
| OpenShift / Rancher | Enterprise roadmap (Phase 3+) | not addressed. |

For deep dive see [`docs/architecture/product-plan.md`](docs/architecture/product-plan.md) §§ 18 and 32–33.

## Container registry

All images publish to Docker Hub namespace `dbergt`. Naming convention is `dbergt/pwdmgr-<service>` and is recorded in [ADR-0004](docs/adr/0004-dockerhub-naming-and-sync-strategy.md).

| Image | Status | Source |
|---|---|---|
| `dbergt/pwdmgr-api` | wiring up in current session | `infra/compose/docker/api.Dockerfile` |
| `dbergt/pwdmgr-web` | planned | follows with first frontend slice |
| `dbergt/pwdmgr-worker` | planned | follows with LDAP sync / rotation worker |
| `dbergt/pwdmgr-agent-gateway` | planned | may stay merged into `pwdmgr-api` until extraction is justified |

Image tagging: `:main` for latest main-branch build, `:vX.Y.Z` for semver release tags, `:sha-<short>` for traceability. Multi-arch (`linux/amd64`, `linux/arm64`) via `docker buildx`.

## Required secrets (operating the platform)

| Secret | Where | Purpose |
|---|---|---|
| `POSTGRES_PASSWORD` | Docker secret in Compose, K8s Secret in cluster | Postgres `pwdmgr` user. **Never** in the image, never in repo. |
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
| Structured logs (JSON, UTC ISO-8601, request ID) | ASP.NET Core + Serilog (planned) | not implemented |
| Liveness probe | `GET /healthz` | not implemented |
| Readiness probe | `GET /readyz` | not implemented |
| Metrics | Prometheus scrape endpoint `/metrics` | not implemented |
| Traces | OpenTelemetry export | Phase 2 |
| Audit export to SIEM | Syslog / JSON webhook / Splunk HEC / Sentinel / Elastic | Phase 2 (see product-plan §§ 16 and 21) |

Operating without these is acceptable in dev. They are **mandatory** before any production deployment.

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

- [ ] How to apply EF Core migrations safely.
- [ ] How to rotate `PWDMGR_PEPPER` (requires re-hash on next login, not full re-encrypt).
- [ ] How to rotate `DOCKERHUB_TOKEN`.
- [ ] How to onboard a new tenant admin.
- [ ] How to perform M-of-N recovery for a private vault.
- [ ] How to revoke a Windows agent.
- [ ] How to read the audit hash chain and detect tampering.
