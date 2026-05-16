# ROADMAP — pwdmgr / Privora

Status: living document — phase plan.

Last update: 2026-05-16

Full background and rationale: [`docs/architecture/product-plan.md`](docs/architecture/product-plan.md) §§ 26 and 27.

## Phase 1 — MVP

**Goal:** internal end-to-end usable system in a lab, demonstrating zero-knowledge encryption, multi-tenancy, LDAP login, MFA, vault CRUD and Docker Compose deployability.

- Multi-tenant data model (tenant_id everywhere) and tenant lifecycle.
- Local accounts + LDAP/AD authentication. OIDC/SAML provider stubs.
- MFA: TOTP and WebAuthn for high-impact actions.
- Personal master passphrase, separate from login. Client-side Argon2id KDF.
- Client-side encryption: per-secret DEK, vault key, user public key, group public key.
- Personal vaults and shared vaults. Collections, folders, tags.
- Typed secret templates: username/password, RDP, SSH key, certificate, DB credential, secure note, file, TOTP seed, custom.
- Role-based sharing (Owner / Admin / Manager / Editor / Contributor / Viewer / Use-only / Request-only / Approver / Auditor / Emergency / None).
- Audit log basics with hash chaining; no secret content in logs.
- Browser extension skeleton (autofill + capture).
- Windows agent skeleton with CLI + PowerShell module.
- Docker Compose deployment. PostgreSQL 16. Reverse proxy.
- Backup / restore with verified drill procedure.
- Admin UI for tenant and user management.
- Bilingual UI (German + English) from day one.

## Phase 2 — Enterprise sharing, LDAP sync, policies

- LDAP sync worker with dry-run and reports; deprovisioning of disabled accounts.
- OIDC / SAML federation production-grade.
- Policy editor: MFA, export, clipboard, session, IP / device trust.
- Approval workflows for Use-only / Request-only / Emergency.
- SIEM export: Syslog / JSON / webhook / Splunk HEC / Sentinel / Elastic.
- Reporting service: who has access to what, weak/old secrets, MFA coverage, critical service-account inventory.
- Attachment handling with chunked client-side encryption.
- Browser extension polished (phishing-aware autofill, capture quality).

## Phase 3 — Agent, CLI, SDKs

- Windows agent device binding, mTLS, DPAPI / TPM-protected tokens.
- Credential injection for RDP, .NET, Web (via extension), PowerShell, Windows applications, scheduled tasks.
- Process binding (publisher / hash / parent process).
- CLI and PowerShell module production-grade.
- SDKs for .NET, Java, Python.
- gRPC for agent traffic.
- Just-in-time short-lived tokens for service accounts.
- First Helm chart for Kubernetes deployment (alpha).

## Phase 4 — Rotation, approval, JIT

- Automatic rotation: AD service accounts, local Windows accounts, Linux via SSH, database accounts, certificates, API keys.
- Test-before-commit rotation with rollback.
- Rotation history, dependencies, maintenance windows, ticket integration.
- Approval workflows mature: vier-Augen-Prinzip, ticket-required access, network-bound access.
- Mobile companion app (read-only first, then approve and capture).

## Phase 5 — PAM session brokering

- RDP proxy, SSH proxy, Web proxy.
- Session recording (encrypted, tamper-evident).
- Command control for privileged SSH sessions.
- Just-in-time privileged access with check-in / check-out.
- Credential-less access patterns (proxy injects, user never sees plaintext).
- Ticket-system and SOAR integration.
- MSP multi-tenant operating console with billing metering hooks.

## Cross-cutting non-functional milestones

| Milestone | Target phase |
|---|---|
| OWASP ASVS L2 platform baseline | end of Phase 1 |
| OWASP ASVS L3 for crypto / admin / PAM paths | end of Phase 3 |
| External cryptography review | before public Phase 2 release |
| Penetration test cycle 1 | before public Phase 2 release |
| ISO 27001 alignment documentation | Phase 3 |
| DSGVO data-subject-rights workflow | Phase 2 |
| SOC 2 readiness | Phase 4+ (depends on customer demand) |

## How phases turn into PRs

Each phase decomposes into vertical slices (per AGENTS.md §4). A slice is a thin end-to-end implementation through API + Frontend + Tests + Docs. PRs follow the §15 template.
