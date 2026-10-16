# Security Incident Report 2026-091 (SYNTHETIC DATA, internal)

Summary: On 3 September an attacker used a leaked service-principal secret from a public repository to read customer exports from a storage account in the finance subscription.

Root cause: The secret was committed to a public repository by a contractor, and the storage account allowed public network access.
Impact: 12,400 customer records were exposed for 6 hours. The regulator was notified within 72 hours.
Affected host: 10.20.4.17, storage account stexamplexports01.

Remediation: Secret rotated, public network access disabled, Private Link enabled, secret scanning enforced in CI.
Owner: Can Yildiz, can.yildiz@contoso-corp.example.
