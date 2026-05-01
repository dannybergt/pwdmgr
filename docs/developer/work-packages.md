# Work Packages

The project should move in small, reviewable packages.

## Bootstrap

- AP-001: Repository structure, README, docs and ADR skeleton.
- AP-002: Docker Compose skeleton with API and PostgreSQL.
- AP-003: Backend module skeleton.
- AP-004: Frontend skeleton.
- AP-005: Browser extension skeleton.
- AP-006: Windows agent skeleton.

## MVP Foundation

- AP-010: Tenant model and migrations.
- AP-011: Local users with Argon2id password hashing.
- AP-012: MFA TOTP.
- AP-013: WebAuthn spike.
- AP-014: WebCrypto and Argon2id WASM spike.
- AP-015: Ciphertext-only Secret CRUD.
- AP-016: Protected file upload/download with encrypted chunks.
- AP-017: Secret templates and dynamic forms.
- AP-018: LDAP auth spike.
- AP-019: Audit event store with hash chaining.

## Definition of Done

- Code implemented.
- Tests added or consciously deferred with reason.
- Security impact checked.
- Documentation updated.
- ADR added for architectural decisions.
- Review completed.

