# Zielkatalog — what the `verifier` proves on the running system

One row per observable goal. The `verifier` subagent (Constitution §4 Phase 4) measures against
this table, not against green exit codes. Status carries the revision at which the proof was
last produced. Rows are added per slice; a slice is not done while one of its rows is OPEN.

Layers: L0 = code reading only · L1 = HTTP against the running stack · L3 = database / process
state observed directly.

## Slice #2 — persistence foundation (ADR-0007)

| ID | Goal (source) | Observable criterion | Layer | Proof step | Negative control | Status |
|---|---|---|---|---|---|---|
| P2-01 | `dotnet test` green against a real Postgres (mvp-slice-plan.md, slice #2) | `Passed: N, Skipped: 0`, exit 0 in the `sdk:9.0` container with `PWDMGR_TEST_PG` | L3 | TESTING.md "Backend database tests" | without the variable: `Skipped: N`; with `CI=true` and no variable: `Failed` | PROVEN 117e0a8 (8 tests, Skipped 0; no var → Skipped 8; `CI=true` → Failed 8) |
| P2-02 | Compose stack starts, readiness green | `/health/ready` and `/health/live` 200 `Healthy`, `/api/v1/platform/info` 200 JSON | L1 | `docker compose -p pwdmgr up -d --build` + curl | P2-06 | PROVEN 117e0a8 |
| P2-03 | Migration creates the schema | `\dt` = `__EFMigrationsHistory`, `local_credentials`, `tenants`, `users`; history has `…_Identity` | L3 | psql `\dt`, `SELECT * FROM "__EFMigrationsHistory"` | empty database before start | PROVEN 117e0a8 (citext 1.6, `ak_users_tenant_id_id`, `fk_local_credentials_users_tenant_id_user_id`) |
| P2-04 | Second start is idempotent (§9) | log `No migrations were applied`, 0 Error lines, history unchanged | L3 | `docker compose -p pwdmgr restart api` + log segment | first run logs `Applying migration` | PROVEN 117e0a8 (no `HTTP_PORTS` override warning; NC with `ASPNETCORE_URLS` reproduces it) |
| P2-05 | Migration down on a DB copy | `dotnet-ef database update 0` exit 0 on the copy, only history table left, original untouched, forward again exit 0 | L3 | `pg_dump pwdmgr \| psql pwdmgr_copy`, dotnet-ef on the `pwdmgr_data` network (`CREATE DATABASE … TEMPLATE` fails while the API pool is open) | indexes before vs. after | PROVEN 117e0a8 (down/up on `pwdmgr_copy`, indexes identical; `citext` extension survives `Down()` by design) |
| P2-06 | Readiness detects a DB outage, liveness does not | ready 503 `Unhealthy`, live 200 `Healthy` with Postgres stopped; ready 200 again after start | L1 | `docker compose -p pwdmgr stop/start postgres` | P2-02 | PROVEN 117e0a8 |
| P2-07 | Unique constraints hold on the running artefact | duplicate slug → `23505 ix_tenants_slug`; same e-mail in different case → `23505 ix_users_tenant_id_email` | L3 | psql INSERT ×2 | first INSERT succeeds | PROVEN 117e0a8 (slug + e-mail case via citext) |
| P2-08 | Logs are JSON/UTC without connection strings (§8, §6.4) | 100 % lines `jq`-valid, `Timestamp` matches `…Z`, 0 hits for `Password=` / `Host=postgres` | L1 | `docker compose -p pwdmgr logs api` + jq/grep | the same grep against the container env hits | PROVEN 117e0a8 |
| P2-09 | Credential cannot reference a user of another tenant (ADR-0007) | INSERT into `local_credentials` with foreign `tenant_id` → `23503 fk_local_credentials_users_tenant_id_user_id` | L3 | xUnit `Credential_cannot_reference_a_user_of_another_tenant` + psql | matching tenant → `INSERT 0 1` | PROVEN 117e0a8 (psql `23503`; xUnit asserts SqlState only) |
| P2-10 | Migration has no destructive operations (§9) | `Up()` contains no `Drop*` / `AlterColumn` | L0 | `grep -E "Drop\|AlterColumn"` on `…_Identity.cs` `Up()` → 0 | — | PROVEN 117e0a8 (`Up()`: 0 Drop/AlterColumn; `Down()`: 3 DropTable) |

Known noise, not a gap: EF Core 9 logs one `Error`-level `Failed executing DbCommand … __EFMigrationsHistory`
line on the very first start against an empty database (it probes the history table before creating it).
It does not recur on restart.
