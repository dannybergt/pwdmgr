# DECISIONS — ADR Index

Architectural Decision Records live in [`docs/adr/`](docs/adr/). One file per decision, numbered, ADR-format (Status, Context, Decision, Consequences).

## Accepted

| # | Title | Date | Status |
|---|---|---|---|
| 0001 | [Modular monolith for MVP](docs/adr/0001-modular-monolith-for-mvp.md) | 2026-05-01 | accepted |
| 0002 | [Zero-knowledge vault is non-negotiable](docs/adr/0002-zero-knowledge-is-non-negotiable.md) | 2026-05-01 | accepted |
| 0003 | [Codename `pwdmgr`, product working name `Privora`](docs/adr/0003-pwdmgr-codename-privora-product-name.md) | 2026-05-01 | accepted |
| 0004 | [DockerHub naming and sync strategy](docs/adr/0004-dockerhub-naming-and-sync-strategy.md) | 2026-05-16 | accepted |
| 0005 | [`docs/architecture/product-plan.md` is the single source of truth](docs/adr/0005-product-plan-is-single-source-of-truth.md) | 2026-05-16 | accepted |

## Proposed / under discussion

(none)

## Superseded / withdrawn

(none)

## When to write an ADR

Per Constitution AGENTS.md §0, write an ADR whenever a **non-trivial** technical decision is taken. Examples that require an ADR:

- Choice of cryptographic algorithm or parameter set.
- Module extraction (monolith → service).
- New external dependency with security or licensing impact.
- New persistence layer.
- Authentication / authorization model changes.
- Public API breaking changes.
- Container / registry / CI strategy shifts.

Examples that **don't** require an ADR: refactoring within a module, test additions, doc improvements, dependency patch bumps without breaking change.

## ADR file template

```markdown
# ADR-XXXX: <Title>

Status: proposed | accepted | superseded by ADR-YYYY | withdrawn  
Date: YYYY-MM-DD

## Context

Why this decision was needed.

## Decision

What we are doing.

## Consequences

Positive and negative outcomes. What this enables, what it costs.
```
