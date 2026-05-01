# ADR-0003: Use pwdmgr as Codename and Privora as Product Working Name

Status: accepted  
Date: 2026-05-01

## Context

The technical project needs a short, stable name for repository paths, service identifiers and tooling. The commercial product needs a broader name that covers credentials, secrets, files, agents and future PAM.

## Decision

Use `pwdmgr` as internal codename and technical namespace. Use `Privora` as the product working name until trademark, domain and market checks are completed.

## Consequences

Positive:

- Technical simplicity.
- Product name is not limited to passwords.
- Module names can become Privora Vault, Privora Agent, Privora Connect and Privora PAM.

Negative:

- Naming may need a controlled rename if legal checks fail.

