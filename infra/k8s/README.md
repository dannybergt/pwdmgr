# Kubernetes Target

Kubernetes and Helm are planned for the enterprise roadmap.

Initial target characteristics:

- Helm chart with separate values for lab, staging and production.
- NetworkPolicy default deny.
- Restricted Pod Security Standards.
- External PostgreSQL HA or PostgreSQL operator.
- cert-manager for TLS.
- Signed images pinned by digest.
- Prometheus ServiceMonitor.
- Loki/OpenSearch integration.

