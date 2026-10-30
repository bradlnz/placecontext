using PlaceContext.Application.AgentWorkflows;

namespace PlaceContext.Infrastructure.AgentWorkflows;

internal static class AgentCatalog
{
    internal static readonly AgentDefinition[] Definitions =
    [
        Define("call-abandonment", "Enterprise communications", "Understand abandoned calls",
            "Compare queue outcomes and waiting times to identify where callers leave.",
            "Which queues lose callers, when does it happen, and what should we investigate first?",
            "Contact centre operations",
            ["Queue or call-detail exports", "Service targets and reporting window, when available"],
            ["Queue and time-window exceptions", "Evidence-backed hypotheses and missing measurements", "Prioritized service review actions"],
            [("queueMetrics", "Offered, answered and abandoned calls, waiting times and timestamps. Include metric definitions and exclusions.",
                ["queue_metrics", "call_queue_metrics", "contact_centre_queue_metrics", "contact_center_queue_metrics"]),
             ("serviceTargets", "Optional: target answer times, service levels and denominator definitions by queue.",
                ["queue_service_targets", "contact_centre_service_targets", "service_targets"]),
             ("reportingWindow", "Optional: report start/end times, timezone, business hours and known export gaps.",
                ["queue_reporting_window", "call_reporting_window"])],
            ["queueMetrics"],
            ["Check coverage and metric definitions", "Compare queue outcomes", "Rank exceptions and alternative explanations", "Prepare service review actions"],
            "Review contact-centre abandonment using supplied queue metrics. Show numerator, denominator and time window for calculated rates. "
            + "Separate abandonment from unanswered or failed calls. Treat missing service targets, timezones and exclusion rules as unknown; "
            + "do not claim SLA compliance or infer staffing causes without evidence. Produce prioritized exceptions, supported hypotheses and the next measurements needed."),

        Define("phone-inventory", "Enterprise communications", "Reconcile numbers and extensions",
            "Find duplicate, unmatched and unassigned services in your phone inventory.",
            "Which phone numbers and extensions have an owner, and which need an administrator to investigate?",
            "Unified communications and IT asset teams",
            ["PBX or unified communications assignments", "Carrier inventory and owner directory, when available"],
            ["Number-to-owner reconciliation", "Duplicate and unmatched exceptions", "Administrator review list with source references"],
            [("assignments", "Phone-number and extension assignments, service identifiers, sites and export date. Preserve country codes.",
                ["phone_assignments", "number_assignments", "extension_assignments", "pbx_extensions"]),
             ("carrierInventory", "Optional: carrier numbers, trunks, service identifiers and current service status.",
                ["carrier_inventory", "phone_services", "carrier_numbers"]),
             ("owners", "Optional: approved user, team or service ownership and site identifiers; omit unnecessary personal data.",
                ["phone_owners", "extension_owners", "telephony_owners"])],
            ["assignments"],
            ["Align identifiers and export dates", "Match assignments to available ownership records", "Separate duplicate and unmatched records", "Prepare administrator review actions"],
            "Reconcile supplied phone assignments with any available carrier inventory and ownership records. Match only explicit identifiers; "
            + "do not assume similar names identify the same person. Preserve country codes and distinguish extensions from external numbers. "
            + "Flag ambiguous matches and unavailable sources. An unmatched number is not proof of non-use. Do not deactivate numbers or provision services."),

        Define("call-routing", "Enterprise communications", "Review routing and failover",
            "Inspect call flows for dead ends, unclear ownership and untested fallback paths.",
            "Can callers reach the right team during opening hours, after hours and when a destination fails?",
            "Telephony administrators and service owners",
            ["Call-flow or dial-plan exports", "Opening hours, escalation policy and test results, when available"],
            ["Routing and ownership gaps", "After-hours and failure scenario test matrix", "Proposed changes awaiting administrator approval"],
            [("callFlows", "Entry numbers, IVR choices, queues, destinations, overflow rules and fallback paths.",
                ["call_flows", "call_routes", "dial_plan", "ivr_routes"]),
             ("routingPolicies", "Optional: timezones, opening hours, holidays, escalation owners and required fallback behaviour.",
                ["routing_policies", "call_routing_policies", "telephony_hours"]),
             ("testResults", "Optional: recorded routing/failover test outcomes and dates. Untested paths remain unknown.",
                ["routing_tests", "call_routing_tests", "telephony_failover_tests"])],
            ["callFlows"],
            ["Map entry points to destinations", "Compare available hours and escalation policies", "Identify untested or failed paths", "Prepare a change and test plan"],
            "Review call flows against any supplied routing policies and tests. Identify documented loops, unreachable destinations, missing owners "
            + "or unspecified fallback paths. Distinguish configuration intent from tested behaviour. Missing policy or tests do not establish correctness. "
            + "Produce a scenario test matrix. Do not place calls, change routing or certify emergency-calling compliance."),

        Define("call-quality", "Enterprise communications", "Investigate call-quality incidents",
            "Group poor calls by site, endpoint, trunk and time to narrow an investigation.",
            "Do poor calls cluster around a site, endpoint, trunk or time window?",
            "Voice engineering and network operations",
            ["Call-quality or session-detail exports", "Network events and incident scope, when available"],
            ["Affected-call and site breakdown", "Timeline of correlated observations", "Investigation checklist with evidence limits"],
            [("callQuality", "Call timestamps, site/trunk identifiers, outcomes and available loss, jitter, latency or quality measurements with units.",
                ["call_quality", "call_quality_metrics", "voice_quality", "session_quality"]),
             ("networkEvents", "Optional: network changes, link events and carrier incidents with timestamps; note missing sources.",
                ["voice_network_events", "telephony_network_events", "carrier_incidents"]),
             ("incidentScope", "Optional: incident window, timezone, affected services, metric limits and site/trunk relationships.",
                ["voice_incident_scope", "telephony_incident_scope"])],
            ["callQuality"],
            ["Align timestamps, metric units and scope", "Group affected calls by site and service", "Compare observations with available events", "Prioritize checks and unresolved hypotheses"],
            "Investigate supplied call-quality observations and correlate them with any supplied network events. Do not mix different metric units "
            + "or invent a quality score. Report sample sizes, missing telemetry and timestamp mismatches. Correlation is a hypothesis, not a proven "
            + "root cause. Produce an incident timeline and prioritized checks. Do not restart equipment or alter network settings."),

        Define("telephony-cost", "Enterprise communications", "Find phone-service cost exceptions",
            "Review billed licences, numbers and trunks for unmatched charges or possible under-use.",
            "Which licences, numbers and trunks need a billing or utilisation review?",
            "IT operations and procurement",
            ["Carrier or licence invoice line items", "Assignments and matching-period usage, when available"],
            ["Unmatched invoice and assignment exceptions", "Usage gaps and candidate review items", "Estimated opportunity with assumptions and exclusions"],
            [("billing", "Invoice period, currency, service identifiers, quantities, unit prices and recurring versus one-off charges.",
                ["telephony_billing", "carrier_invoice_lines", "phone_invoice_lines", "voice_licence_costs"]),
             ("assignments", "Optional: service assignments, owners and capacity reserved for resilience or seasonal use.",
                ["phone_assignments", "voice_licence_assignments", "telephony_services"]),
             ("usage", "Optional: usage and observation period for the same identifiers, including gaps and retention limits.",
                ["telephony_usage", "phone_usage", "voice_licence_usage"])],
            ["billing"],
            ["Align periods and service identifiers", "Reconcile available assignments", "Check usage coverage and reserved capacity", "Prepare procurement review candidates"],
            "Review supplied phone-service billing alongside any assignments and usage from the same period. Separate currencies and recurring "
            + "from one-off charges. Missing usage is not proof a service is unnecessary; account for failover, reserved and seasonal capacity. "
            + "Show assumptions for estimated opportunities. Without assignment or usage evidence, limit findings to billing exceptions. "
            + "Do not claim confirmed savings or cancel services."),

        Define("support-escalation", "Service operations", "Prioritize support escalations",
            "Review case age, ownership and service commitments to prepare a focused handover.",
            "Which cases need attention next, who owns them, and what evidence is missing?",
            "Service desk leads and customer operations",
            ["Open ticket or case export", "Service commitments and ownership records, when available"],
            ["Prioritized case review queue", "Ownership and evidence gaps", "Draft handover with the next proposed action"],
            [("cases", "Case identifiers, severity, status, owner, creation/update timestamps, commitments and relevant notes.",
                ["support_cases", "support_tickets", "service_desk_cases", "service_desk_tickets"]),
             ("servicePolicy", "Optional: response/resolution targets, business-hour rules, escalation criteria and exclusions.",
                ["support_service_policy", "support_sla", "service_desk_policy"]),
             ("ownership", "Optional: service owners, support groups and handover status.",
                ["support_ownership", "support_teams", "service_desk_ownership"])],
            ["cases"],
            ["Check case age and available policy", "Compare commitments and ownership", "Rank cases requiring attention", "Prepare an evidence-linked handover"],
            "Prioritize supplied cases using explicit severity, timestamps and any service policy or ownership information. Explain each priority. "
            + "Do not invent SLA targets or treat missing clocks and exclusions as confirmed breaches. Do not invent resolution steps, "
            + "change ticket status, assign staff or notify customers. Return a proposed handover for review."),

        Define("retail", "Retail", "Plan stock replenishment",
            "Compare stock and sales to prepare a replenishment proposal with explicit assumptions.",
            "Which products need a stock review before the next ordering cycle?",
            "Retail operations and inventory planners",
            ["Product catalogue", "Sales by product and period", "Available and incoming stock"],
            ["Shortage and surplus exceptions", "Replenishment proposal with assumptions", "Missing data and buyer review actions"],
            [("products", "Product identifiers, pack sizes, lead times and reorder constraints.", ["products", "product_catalogue", "product_catalog"]),
             ("sales", "Sales quantities by product, reporting period and exceptional promotions.", ["sales", "product_sales", "retail_sales"]),
             ("stock", "On-hand, reserved and incoming quantities with the snapshot time.", ["stock", "product_stock", "stock_levels"])],
            ["products", "sales", "stock"],
            ["Match product and stock identifiers", "Compare demand and available stock", "Identify assumptions and exceptions", "Prepare buyer review actions"],
            "Compare supplied stock and sales, identify shortages and surplus, and produce a prioritized replenishment proposal. "
            + "Show reporting periods and assumptions. Do not extrapolate demand without labelling the assumption, or place orders."),

        Define("services", "Professional services", "Prepare client onboarding",
            "Review scope and available documents to identify the next onboarding steps.",
            "What is missing before this client can be handed over to delivery?",
            "Client services and delivery leads",
            ["Approved client brief", "Agreed scope", "Available onboarding documents"],
            ["Missing information and dependencies", "Sequenced onboarding plan", "Proposed responsibility and handover checklist"],
            [("client", "Client/account summary and relevant service owners.", ["client", "client_brief", "client_onboarding"]),
             ("scope", "Agreed outcomes, deliverables, dates, responsibilities and exclusions.", ["client_scope", "onboarding_scope"]),
             ("documents", "Available documents or relevant contents, plus known missing approvals.", ["client_documents", "onboarding_documents"])],
            ["client", "scope", "documents"],
            ["Check the agreed scope", "Identify information and approval gaps", "Sequence dependencies", "Prepare a delivery handover"],
            "Review the supplied client scope and documents, identify missing onboarding information, and produce a sequenced onboarding "
            + "plan with proposed responsibilities. Distinguish documented agreements from assumptions. Do not contact anyone or claim approvals."),

        Define("property", "Property operations", "Triage maintenance requests",
            "Review a reported fault, property details and access constraints for a maintenance handover.",
            "What needs attention, what is unknown, and who should review this request?",
            "Property managers and facilities teams",
            ["Property or asset record", "Maintenance request", "Access arrangements and restrictions"],
            ["Issue summary and information gaps", "Proposed triage and handoff plan", "Access questions for the responsible team"],
            [("property", "Property/asset identifier, site and responsible team.", ["property", "maintenance_property", "property_assets"]),
             ("issue", "Reported fault, observations, timing and existing inspection notes.", ["maintenance_requests", "maintenance_issues"]),
             ("access", "Access arrangements, contact process and known restrictions.", ["property_access", "maintenance_access"])],
            ["property", "issue", "access"],
            ["Identify the affected asset", "Review the reported observations", "Check information and access gaps", "Prepare a maintenance handover"],
            "Review the supplied maintenance request, identify missing information, and produce a triage and handoff plan. "
            + "Distinguish reported symptoms from verified findings. Do not diagnose unseen hazards as safe, book contractors or claim an inspection occurred.")
    ];

    internal static IReadOnlyList<AgentWorkflow> Workflows { get; } = Definitions.SelectMany(d => new[]
    {
        d.Workflow with
        {
            Id = d.Workflow.Id + "-checklist",
            Name = d.Workflow.Name + " — input check",
            Paid = false,
            Description = "Check required input availability before preparing this analysis. No AI tokens used.",
            Outcome = "Identify missing core inputs for this use case.",
            Steps = ["Collect available context", "Check required inputs", "Report missing information"],
            Deliverables = ["Required-input availability result", "List of missing core inputs"],
            ExecutionScope = "Checks input presence only. It does not validate values, analyse the data or connect to external systems."
        },
        d.Workflow
    }).ToArray();

    private static AgentDefinition Define(string id, string industry, string name, string description,
        string outcome, string intendedFor, string[] dataSources, string[] deliverables,
        (string Field, string Hint, string[] Aliases)[] inputs, string[] required, string[] steps, string instruction)
    {
        var fields = inputs.Select(x => x.Field).ToArray();
        return new(new(id, name, industry, description, true, steps, fields)
        {
            Outcome = outcome,
            IntendedFor = intendedFor,
            DataSources = dataSources,
            Deliverables = deliverables,
            RequiredInputFields = required,
            InputHints = inputs.ToDictionary(x => x.Field, x => x.Hint, StringComparer.Ordinal),
            SourceAliases = inputs.ToDictionary(x => x.Field, x => (IReadOnlyList<string>)x.Aliases, StringComparer.Ordinal),
            ExecutionScope = "Uses your configured model to analyse prepared project evidence and propose a plan for review. "
                + "It does not access live business systems, place calls, provision services or carry out external changes."
        }, required, instruction);
    }
}
