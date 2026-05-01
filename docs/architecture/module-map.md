# Module Map

## Backend MVP Modules

- Identity: local users, external identities, MFA, sessions.
- Tenants: tenant lifecycle, settings, licensing hooks.
- Vault: vault metadata, secret metadata, ciphertext payload routing.
- Crypto Metadata: public keys, wrapped keys, crypto versions, recovery metadata.
- Sharing: ACLs, group access, temporary access and ownership transfer.
- Policy: MFA, export, clipboard, agent, template and retention policies.
- Audit: append-only events and hash chaining.
- Agent Gateway: agent registration, device trust and local secret request mediation.
- Files: encrypted file manifests, chunk metadata and object storage routing.
- Templates: typed forms for credentials, certificates, credit cards, licenses and custom records.
- Reporting: access reports, compliance exports and dashboards.

## Extraction Candidates

- Audit exporter.
- Agent gateway.
- LDAP sync worker.
- Rotation worker.
- Reporting service.
- PAM session broker.

