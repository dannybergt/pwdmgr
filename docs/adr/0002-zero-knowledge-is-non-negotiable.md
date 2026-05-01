# ADR-0002: Zero-Knowledge Vault Is Non-Negotiable

Status: accepted  
Date: 2026-05-01

## Context

The platform stores credentials, secrets, protected files and privileged-access material. Server-side plaintext would create unacceptable insider, admin, database and backup risk.

## Decision

Vault payloads, protected files and sensitive fields are encrypted client-side. The server stores only ciphertext, metadata, public keys and wrapped keys. Global admins, tenant admins, support admins and platform operators do not receive a secret-reading backdoor.

## Consequences

Positive:

- Strong protection against database and backup compromise.
- Clear customer trust story.
- Better separation between authentication and decryption.

Negative:

- Web client supply-chain protection becomes critical.
- Server-side search, scanning and previews are limited.
- Recovery must be deliberately designed and cannot be a simple password reset.

