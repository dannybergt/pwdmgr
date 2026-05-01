# ADR-0001: Use a Modular Monolith for the MVP

Status: accepted  
Date: 2026-05-01

## Context

The product needs strong security boundaries, tenant isolation, auditability and fast MVP delivery. Starting with too many independently deployed services would increase operational complexity before the domain model is stable.

## Decision

The MVP backend starts as an ASP.NET Core modular monolith with explicit module boundaries:

- Identity.
- Tenants.
- Vault.
- Crypto metadata.
- Sharing and ACL.
- Policy.
- Audit.
- Agent gateway.
- Reporting.
- Licensing.

Modules can later be extracted into services when scaling, team boundaries or security zoning justify it.

## Consequences

Positive:

- Faster delivery.
- Fewer deployment parts.
- Easier local development.
- Simpler transactional consistency for ACL, audit and key metadata.

Negative:

- Requires discipline to prevent module coupling.
- Independent scaling is limited early on.

