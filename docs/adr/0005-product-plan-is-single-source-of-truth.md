# ADR-0005: `docs/architecture/product-plan.md` is the single source of truth for the product plan

Status: accepted  
Date: 2026-05-16

## Context

Two copies of the master product plan existed:

- `C:/data/codex/enterprise-zero-knowledge-pam-plan.md` (1903 lines, version 0.1, dated 2026-05-01) — sitting next to the repo at workspace root.
- `pwdmgr/docs/architecture/product-plan.md` (1903 lines, byte-identical SHA-256 `141a30ea…81d83b`) — inside the repo.

Two living copies of a plan-of-record drift apart over time. Reviewers wouldn't know which is authoritative, search results would surface both, and links would rot.

## Decision

- `pwdmgr/docs/architecture/product-plan.md` is the **single source of truth** for the product plan. It is versioned with the code that implements it.
- The external duplicate `C:/data/codex/enterprise-zero-knowledge-pam-plan.md` is **deleted**. No legacy archive is created because the content is fully preserved (and now versioned) inside the repo.
- New plan-level changes are made by editing the in-repo file via a PR. The plan's own version header inside the file (currently "Version 0.1 Architekturplanung") is updated as part of those PRs.
- Constitution-required root documents ([PROJECT_BRIEF.md](../../PROJECT_BRIEF.md), [ARCHITECTURE.md](../../ARCHITECTURE.md), [STATE.md](../../STATE.md), [ROADMAP.md](../../ROADMAP.md), [TESTING.md](../../TESTING.md), [OPERATIONS.md](../../OPERATIONS.md)) link **into** the product plan rather than duplicating its depth. Their job is to give entry points and current-session state, not to re-state design decisions.

## Consequences

Positive:

- One canonical place for "what the product is and how it works."
- Plan history is recoverable through git, including who changed what and why.
- Reviewers and tools only have to read one file.
- Root docs stay small and reflect their distinct responsibilities (brief, current state, phases, operating notes).

Negative:

- Plan-only browsing outside the repo is no longer possible. Anyone needing to read the plan must check out the repo or browse it on GitHub.
- Future external "shareable" excerpts must be generated from the in-repo file rather than maintained as a separate doc.

## References

- Verification of byte-equality before deletion: `sha256sum` of both files matched (`141a30ea1902b70558f7e6d1522358cba07879f8b2d7dfa07f4a7f28d481d83b`).
- AGENTS.md §0 lists `ARCHITECTURE.md`, `STATE.md`, `DECISIONS.md` etc. as required project documents and prohibits ambiguity about sources of truth.
