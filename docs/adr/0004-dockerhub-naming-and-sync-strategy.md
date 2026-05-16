# ADR-0004: DockerHub naming convention and sync strategy

Status: accepted  
Date: 2026-05-16

## Context

The product is built as multiple containerized services (API, web, background workers, agent gateway). It needs an unambiguous, stable container image naming scheme on Docker Hub so that:

- Operators always know where to pull from.
- CI pipelines can be configured once and stay stable across services.
- Image tags are reproducible and traceable to a commit.
- Convention matches the maintainer's existing Docker Hub projects (`dbergt/pulsight-api`, `dbergt/lms-platform-worker`, etc.).

The maintainer's Docker Hub namespace is `dbergt`. Other namespaces (e.g. an `anbergt-corp` organisation) are not in scope right now.

## Decision

### Naming

- All images for the product live under namespace `dbergt`.
- Image name pattern: `dbergt/pwdmgr-<service>` where `<service>` is the kebab-case service name.
- Service names map 1:1 to logical Phase-1 services:
  - `dbergt/pwdmgr-api` — ASP.NET Core API container, includes the modular monolith.
  - `dbergt/pwdmgr-web` — static React build served by NGINX or similar, planned with first frontend slice.
  - `dbergt/pwdmgr-worker` — background workers (LDAP sync, rotation), planned with first worker slice.
  - `dbergt/pwdmgr-agent-gateway` — mTLS endpoint for the Windows agent. **May initially run inside `pwdmgr-api`** and extract later (see ADR-0001). A separate image is provisioned only when extraction happens.
- All images are **public**, consistent with the maintainer's other open product images. Source code is open enough that hiding images adds friction without adding security.

### Tagging

- `:main` — moving tag, always points to the latest successful build from branch `main`. Not for production.
- `:vX.Y.Z` — immutable semver tag, produced for release tags `vX.Y.Z` on the repo.
- `:sha-<short>` — immutable per-commit tag, produced for every push to `main` for traceability.
- No `:latest` tag (avoids "what is latest?" ambiguity in deployment scripts).

### Multi-arch

- Build for `linux/amd64` and `linux/arm64` via `docker buildx`. ARM64 matters because both modern macOS dev machines and an increasing share of Linux servers run aarch64.

### Sync (CI)

- GitHub Actions builds images on:
  - every PR (build only, no push) — catches Dockerfile regressions.
  - every push to `main` (build + push with `:main` and `:sha-<short>`).
  - every push of a tag matching `v*` (build + push with `:vX.Y.Z`).
- Login uses GitHub Actions secrets `DOCKERHUB_USERNAME` (= `dbergt`) and `DOCKERHUB_TOKEN` (a Docker Hub Personal Access Token scoped to `dbergt/pwdmgr-*`).
- Workflow uses `docker/login-action@v3`, `docker/setup-buildx-action@v3`, `docker/build-push-action@v6` with provenance and SBOM attestations enabled.

### Repo provisioning

- Docker Hub auto-creates a repository on first push if the authenticated user has push permissions. We rely on this and do **not** create repos via a separate provisioning step. The very first successful CI image push materialises the Hub repo.

## Consequences

Positive:

- Predictable, copy-pasteable image references for ops and docs.
- No naming churn when services are extracted (extraction is the moment a new image appears).
- Immutable tags give safe rollback.
- Multi-arch unblocks Apple Silicon developers and ARM-based production hosts.
- Public images keep parity with the maintainer's other projects, simplify support.

Negative:

- Public images make image metadata (file paths, dependency versions) discoverable. This is acceptable for a product that is openly described and where the security model does not rely on image obscurity.
- Auto-creation on push means an accidental wrong image name creates a stray Hub repo. Mitigation: image names are pinned in the workflow file and reviewed in PRs.

## References

- Naming follows the pattern already used by `dbergt/pulsight-api`, `dbergt/pulsight-collector`, `dbergt/lms-platform-api`, `dbergt/lms-platform-worker`, `dbergt/bcon-socialagent-backend`, `dbergt/bcon-socialagent-frontend`.
- Builds upon [ADR-0001](0001-modular-monolith-for-mvp.md) (monolith first, extract later).
