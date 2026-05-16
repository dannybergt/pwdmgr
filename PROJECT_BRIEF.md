# PROJECT_BRIEF — pwdmgr / Privora

Status: living document  
Last update: 2026-05-16

## Purpose

`pwdmgr` is a commercial, multi-tenant, container-deployable enterprise platform for password, secret, credential and (roadmap) privileged-access management. Internal codename: `pwdmgr`. Product working name: `Privora` (see [ADR-0003](docs/adr/0003-pwdmgr-codename-privora-product-name.md)).

The product replaces ad-hoc credential storage (spreadsheets, shared notes, generic password managers) for organisations that need: client-side zero-knowledge encryption, LDAP/AD integration, MFA, granular sharing, full audit, Windows agent for credential injection, and a roadmap into PAM (session brokering, RDP/SSH proxy, recording).

## Scope (Phase 1 / MVP)

- Multi-tenant deployment from day one.
- Local accounts + LDAP/AD authentication with MFA (TOTP, WebAuthn).
- Personal master passphrase, separate from login.
- Client-side encryption: per-secret DEK → vault key → user/group public keys.
- Personal vaults, shared vaults, collections, folders, tags.
- Typed secret templates (password, RDP, SSH key, certificate, DB credential, secure note, file, TOTP seed, custom).
- Role-based sharing (Owner / Admin / Manager / Editor / Contributor / Viewer / Use-only / Request-only / Approver / Auditor / Emergency / None).
- Audit log with hash chaining; no secret content in logs.
- Browser extension skeleton (autofill + capture).
- Windows agent skeleton (CLI + service + PowerShell module).
- Docker Compose deployment for lab / on-prem evaluation.
- Backup/restore for ciphertext payloads, PostgreSQL PITR.

## Non-scope (MVP)

- Full PAM session brokering (RDP/SSH/Web proxy, session recording).
- Mobile apps.
- Full automatic rotation for all target systems.
- Full SIEM/SOAR connector catalog.
- Production-grade Kubernetes/Helm chart.
- MSP billing / commercial license metering.

These are phased into the roadmap; see [ROADMAP.md](ROADMAP.md).

## Stakeholders

| Role | Responsibility |
|---|---|
| Platform Owner | Product vision, commercial direction, release approval. |
| Security Architect | Threat model, crypto review, secure SDLC compliance. |
| Solution Architect | Module boundaries, deployment topology, performance targets. |
| DevSecOps | CI/CD, container hardening, secret scanning, dependency lifecycle. |
| QA | Test strategy, integration and security testing. |
| Tech Writer | Admin / user / developer / operations / security documentation. |

Single human owner (Danny Bergt) currently fills all roles; documentation must remain transferable to a team.

## Hard constraints

- **Zero knowledge is non-negotiable** ([ADR-0002](docs/adr/0002-zero-knowledge-is-non-negotiable.md)). No backdoor for platform, tenant or support admins.
- **Login passwords are never reversibly stored.** Argon2id only, with optional pepper from a separate secret store.
- **Recovery requires explicit multi-party cryptographic enrolment** before data is at risk. There is no silent admin reset path.
- **OWASP ASVS Level 2** for the platform overall, **Level 3** for cryptographic, administrative and PAM-adjacent paths.

## References

- Full architecture: [`docs/architecture/product-plan.md`](docs/architecture/product-plan.md) — Single Source of Truth.
- Module map: [`docs/architecture/module-map.md`](docs/architecture/module-map.md).
- ADRs: [`docs/adr/`](docs/adr/).
- Security baseline: [SECURITY.md](SECURITY.md), [`docs/security/`](docs/security/).
