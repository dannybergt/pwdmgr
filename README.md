# pwdmgr

Technical codename: `pwdmgr`  
Product working name: `Privora`

`pwdmgr` is the project foundation for a commercial, multi-tenant, zero-knowledge enterprise platform for password management, secrets management, credential handling and future privileged-access-management capabilities.

## Current Status

This repository is in project bootstrap phase.

The first milestone is a secure MVP foundation:

- Multi-tenant data model from day one.
- ASP.NET Core modular monolith backend.
- React/TypeScript frontend.
- PostgreSQL as primary database.
- Client-side zero-knowledge cryptography.
- Local users, LDAP/AD, MFA, personal master passphrase.
- Personal vaults, shared vaults, encrypted files and typed secret templates.
- Browser extension and Windows agent skeletons.
- Docker Compose for local/lab deployment.
- Security, architecture and operating documentation as living docs.

## Repository Layout

```text
src/backend/         ASP.NET Core backend modules
src/frontend/        React web frontend
src/extension/       Browser extension
src/agent/           Windows agent, CLI and PowerShell module
infra/compose/       Docker Compose deployment
infra/k8s/           Future Kubernetes/Helm assets
docs/architecture/   Product and system architecture
docs/adr/            Architecture decision records
docs/admin/          Administrator documentation
docs/user/           User documentation
docs/developer/      Developer documentation
docs/operations/     Operations handbook
docs/security/       Threat model and security controls
tests/               Automated tests
tools/               Development and release tooling
```

## Important Security Principles

- Login passwords are never reversibly stored.
- Local login passwords use Argon2id with salt and optional pepper.
- Vault secrets and protected files are decryptable only on trusted clients.
- Server-side services must never receive plaintext secrets.
- Global admins, tenant admins and support admins must not have secret-reading backdoors.
- Recovery must be possible only through explicit multi-party cryptographic recovery.

## Next Work Packages

1. Establish backend build once .NET SDK is available.
2. Implement tenant model and initial migrations.
3. Build crypto spike for WebCrypto AES-GCM and Argon2id WASM.
4. Implement ciphertext-only Secret CRUD API.
5. Add React unlock-flow prototype.

