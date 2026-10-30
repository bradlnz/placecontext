# Agent Store

*Start with an outcome, review available project context, and turn it into an agent in Build.*

Open **Agent Store** beside Build. Search by outcome, team or source and choose an agent.
Each outcome appears once; context readiness and the optional AI plan are modes of that agent.

## Start with project data

Selecting an agent looks for matching tables in the current project. It does not call a model
or perform an external action. Review the candidate sources, then choose **Use project data**
to add them to the run. **Add or edit context** is available when something is missing or needs
correcting. Supporting policies and targets are optional for the enterprise use cases; missing
supporting evidence limits the conclusions an agent can draw.

Preparation requires permission to read project data. It uses explicit table-name matches,
including common underscore, hyphen and case variations. If several sources match one input,
it asks you to resolve the ambiguity instead of choosing a source silently. It does not scan
unrelated tables or connect to a phone provider.

The preview is a bounded sample: up to eight rows and 24 columns from each matching table.
Long values may be shortened, and the total context is limited. These are the first available
rows in the source's default order, not a representative sample or a complete data audit.
Unknown source timestamps and missing evidence stay explicit. Review the payload before using
it with a model; credentials belong in the vault.

## Enterprise phone and service agents

| Agent | Core project source examples | Useful supporting context |
|---|---|---|
| Understand abandoned calls | `queue_metrics`, `call_queue_metrics` | Service targets and reporting window |
| Reconcile numbers and extensions | `phone_assignments`, `pbx_extensions` | Carrier inventory and approved owners |
| Review routing and failover | `call_flows`, `dial_plan`, `ivr_routes` | Routing policies and recorded test outcomes |
| Investigate call-quality incidents | `call_quality`, `voice_quality` | Network events, incident scope and metric units |
| Find phone-service cost exceptions | `telephony_billing`, `carrier_invoice_lines` | Assignments and matching-period usage |
| Prioritize support escalations | `support_cases`, `support_tickets` | Service commitments and ownership |

Stock replenishment, client onboarding and maintenance triage remain available. Source names
identify candidate inputs; they do not prove the contents are complete, current or suitable.

## Continue in Build

**Set up this agent in Build** opens an editable request describing the outcome, expected
sources and deliverables. The request asks Build to inspect existing project capabilities first
and ask only for missing essentials. Project record values are not copied into the link.
Review and send the draft to start; opening the link does not run anything. An existing draft
or image attachment is preserved, and a background conversation continues independently.

## Context checks and AI plans

A **context readiness** run is local and checks whether required inputs are present. It does
not validate values or analyse business outcomes. Standalone **AI plan** runs retain the host's
existing development-only subscription simulation and require enabled access plus configured
model and Jev services. No real subscription payments are collected; provider usage can still
incur charges. In other environments, continue in Build to configure a project workflow.

Agent plans are proposed reviews and investigations. They do not place calls, provision users,
change routing, cancel services, update support tickets or send messages. Such actions need
their own configured integration and approval in the resulting workflow.
