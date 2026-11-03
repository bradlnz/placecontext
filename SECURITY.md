# Security policy

## Supported versions

Security fixes are applied to the latest release and the default branch. Older releases may require
an upgrade to receive a fix.

## Reporting a vulnerability

Do not open a public issue for a suspected vulnerability. Use the repository's
[private vulnerability reporting](https://github.com/bradlnz/placecontext/security/advisories/new)
page and include:

- the affected version or commit;
- reproduction steps or a proof of concept;
- the security impact; and
- any suggested mitigation.

Please avoid accessing data that is not yours, disrupting running services, or publishing details
before a fix is available. Acknowledgement and remediation timing depend on severity and the quality
of the report.

## SOC 2 Type II readiness

SOC 2 Type II is the confirmed assurance target. This repository does not establish that an
examination has been completed. Type II covers control design and operating effectiveness over
a specified period; passing tests or enabling encryption does not establish that operating history.
See the [AICPA SOC resources](https://www.aicpa-cima.com/resources/landing/system-and-organization-controls-soc-suite-of-services)
and its [explanation of Type II reporting](https://www.journalofaccountancy.com/issues/2011/jul/20103500/).

The proposed system boundary includes ingestion, project tables, workflow workers, artifacts,
derived search/vector data, agent APIs, identity, keys, deployment infrastructure, and supporting
operations. Document responsibilities separately for hosted and customer-managed deployments,
including external data, identity, model, storage, and execution providers. Confirm the boundary,
applicable Trust Services Criteria, examination period, and evidence requirements with the
independent CPA firm. Security and Confidentiality are proposed priorities; assess Availability,
Processing Integrity, and Privacy against service commitments before finalizing scope.

### Engineering and evidence gaps

The following is a readiness backlog based on repository inspection, not an exhaustive control
assessment or a statement about production operations. Evidence listed below still needs to be
collected and reviewed.

| Area | Current implementation or limitation | Required work and operating evidence |
| --- | --- | --- |
| Job access | The read-only project data API checks `data.read`; personal tokens inherit the user's permissions. Runtime project/job/run IDs do not restrict token authority. | Introduce scoped service identities and project/table/field grants, explicit decryption permissions, and least-privilege secret injection. Retain grant approvals, revocations, access reviews, and denied-access tests. |
| PII in tables | Generic project cell encryption is disabled by default and is not suitable for every column type or SQL write path. Reads can automatically decrypt protected cells. | Classify fields and implement type-safe protection across ingestion and writes, masking, authorized decryption, and a verified migration of existing data. Record policy changes and migration validation without recording PII. |
| Derived data and model processing | New vector source text and source keys are encrypted. Legacy remote plaintext is not migrated; embedding requests still carry plaintext and vectors remain sensitive data. | Enforce permitted processing destinations and masking/tokenization before external calls; migrate legacy payloads and propagate source updates, deletion, and retention to derived data. Retain provider reviews and deletion/reindex evidence. |
| Keys and encryption | Data Protection supports a persistent protected key ring; purposes are not tenant-specific key boundaries. | Review key custody, tenant isolation requirements, transport and storage encryption, rotation, recovery, and revocation. Retain configuration reviews and rotation/recovery exercise results; keep keys out of jobs and evidence files. |
| Audit and detection | Complete coverage of sensitive reads, decryption, exports, permission changes, and administrative actions has not been verified. | Establish protected audit events with actor, tenant, action, result, and correlation IDs, without plaintext PII or secrets. Verify retention, access restrictions, alert delivery, and response records. |
| Recovery and retention | Configuration exports do not back up all business data, artifacts, run history, Vault contents, or required keys. | Define recovery and retention commitments, protected backups, restore exercises, and deletion handling for backups and derived stores. Keep dated restore results and exception/remediation records. |
| Operational controls | Repository changes and tests are development evidence only; production control operation has not been assessed. | Assign accountable owners and document access lifecycle/MFA, change approval, vulnerability remediation, incident response, supplier reviews, and personnel security. Retain the corresponding approvals, reviews, exercises, and remediation history throughout the agreed period. |

Before starting the examination period, assign a named owner and review cadence to each applicable
control, link it to the agreed criteria, and record its evidence location and open exceptions.
Store operational evidence in an access-controlled system rather than committing customer data,
credentials, or incident details to this repository. Preserve dated evidence throughout the period
and track failures and remediation honestly. Owners, auditor, dates, and production effectiveness
remain unconfirmed.
