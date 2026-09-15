# ADR-0007: EF Core + Npgsql as persistence layer, snake_case schema, migrations in the Infrastructure assembly

Status: accepted  
Date: 2026-09-15

## Context

The backend skeleton had no persistence at all: no ORM, no `User`, no migrations, no
database tests (see `docs/developer/mvp-slice-plan.md`, slice #2). Every later slice
(sessions, keyring, vaults, secrets) needs a schema that is versioned, migrates forward and
back, and is testable on a real Postgres. The product plan fixes Postgres as the store
(ADR-0001, ARCHITECTURE.md); the question was how to talk to it and how to shape the schema.

## Decision

- **EF Core 9 with `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.4.** Code-first entities in
  `Pwdmgr.Domain`, mappings as `IEntityTypeConfiguration<T>` in
  `Pwdmgr.Infrastructure/Persistence/Configurations`, one `PwdmgrDbContext`.
- **snake_case naming via `EFCore.NamingConventions` 9.0.0** (`UseSnakeCaseNamingConvention`).
  Tables `tenants`, `users`, `local_credentials`; indexes/constraints `ix_*`, `pk_*`, `fk_*`.
  Same author as the Npgsql provider, MIT, no transitive dependencies. Explicit
  `ToTable`/`HasColumnName` on every property would carry the same information by hand and
  drift.
- **Migrations live in `Pwdmgr.Infrastructure/Persistence/Migrations`** (EF's timestamped
  naming, first one `Identity`). A `DesignTimeDbContextFactory` with a dummy connection string
  lets `dotnet ef migrations add` run without a database.
- **`Database:MigrateOnStartup`** (bool, default `false`) applies pending migrations in
  `Program.cs` before the host starts serving. The compose dev stack sets it to `true`;
  production runs migrations as an explicit deploy step (Constitution §9 expand/contract).
- **Base classes:** `Entity` (`Id`, `CreatedAt`, `UpdatedAt`) and `TenantScopedEntity : Entity`
  (`TenantId`). `Tenant` derives from `Entity` — it previously inherited a `TenantId` it should
  never have had.
- **`LocalCredential` stores only `password_hash` (PHC string) and `pepper_version`.** The
  slice plan listed a separate `password_params` column; the PHC format
  (`$argon2id$v=19$m=..,t=..,p=..$salt$hash`) already carries the parameters, so a second
  column would be a redundant copy that can disagree with the hash. Slice #4 (ADR-0008)
  chooses the Argon2 library on that basis.
- **Readiness:** `/health/ready` runs `AddDbContextCheck<PwdmgrDbContext>` (tag `ready`);
  `/health/live` runs no checks so a database outage does not restart the pod.
- **Logging:** built-in `JsonConsole` with UTC ISO 8601 timestamps and scopes (request id,
  path); EF command logging at `Warning` so connection strings and SQL never reach normal logs.
- **Tests on a real Postgres via `PWDMGR_TEST_PG`**, one throw-away database per test class,
  xUnit v3 dynamic skip when unset. No Testcontainers (Docker socket inside the SDK container
  under SELinux is an extra failure source; TESTING.md updated).

## Consequences

Positive:

- Schema is versioned with the code; forward and rollback are proven by tests on every PR.
- Tenant scoping is a column with composite indexes from the first table on; the EF global
  query filter (slice #4) only has to reference it.
- `dotnet test` runs identically in CI (`services: postgres:16`) and locally (one container on
  its own network, no host port).

Negative:

- Two new NuGet dependencies (`Npgsql.EntityFrameworkCore.PostgreSQL`, `EFCore.NamingConventions`)
  plus `Microsoft.EntityFrameworkCore.Design` (private assets) — all pinned, Dependabot-tracked.
- Startup migration is convenient in dev but must stay off in production images; the default
  is `false` and compose sets it explicitly.
- Migration naming follows EF's timestamp convention, not the `0001_` prefix used loosely in
  the slice plan.
