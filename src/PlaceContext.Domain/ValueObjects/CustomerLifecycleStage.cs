namespace PlaceContext.Domain.ValueObjects;

/// <summary>The customer lifecycle used by CRM mode, ordered from acquisition to retention.</summary>
public enum CustomerLifecycleStage
{
    Lead,
    Qualified,
    Onboarding,
    Active,
    AtRisk,
    Churned,
}
