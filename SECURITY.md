# Security Policy

`pwdmgr`/`Privora` is security-sensitive software. All security-impacting work requires review before release.

## Baseline

- No plaintext secrets in server-side logs, telemetry, database rows, queues or caches.
- No secret-reading backdoors for platform, global, tenant or support admins.
- All authentication credentials must be non-reversibly hashed or externally verified.
- Vault payloads and protected files must remain client-side encrypted.
- Recovery must use explicit multi-party cryptographic controls.

## Reporting

Security reporting channels are not configured yet.

Before public release, define:

- Security contact.
- Vulnerability disclosure process.
- Supported versions.
- Patch SLA.
- CVE handling.

