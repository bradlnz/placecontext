using PlaceContext.Domain.Common;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>A project-scoped customer managed through the CRM lifecycle.</summary>
public sealed class CrmClient : AggregateRoot
{
    private CrmClient(
        Guid id,
        Guid projectId,
        string name,
        string? company,
        string? email,
        string? phone,
        CustomerLifecycleStage lifecycleStage,
        string? notes,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        ProjectId = projectId;
        Name = name;
        Company = company;
        Email = email;
        Phone = phone;
        LifecycleStage = lifecycleStage;
        Notes = notes;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }
    public Guid ProjectId { get; }
    public string Name { get; private set; }
    public string? Company { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public CustomerLifecycleStage LifecycleStage { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static CrmClient Create(
        Guid projectId,
        string name,
        string? company,
        string? email,
        string? phone,
        CustomerLifecycleStage lifecycleStage,
        string? notes,
        DateTimeOffset now)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("ProjectId must not be empty.", nameof(projectId));
        Validate(name, email);
        return new CrmClient(
            Guid.NewGuid(), projectId, name.Trim(), Trim(company), Trim(email), Trim(phone),
            lifecycleStage, Trim(notes), now, now);
    }

    public static CrmClient Rehydrate(
        Guid id,
        Guid projectId,
        string name,
        string? company,
        string? email,
        string? phone,
        CustomerLifecycleStage lifecycleStage,
        string? notes,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
        => new(id, projectId, name, company, email, phone, lifecycleStage, notes,
            createdAt, updatedAt);

    public void Update(
        string name,
        string? company,
        string? email,
        string? phone,
        CustomerLifecycleStage lifecycleStage,
        string? notes,
        DateTimeOffset now)
    {
        Validate(name, email);
        Name = name.Trim();
        Company = Trim(company);
        Email = Trim(email);
        Phone = Trim(phone);
        LifecycleStage = lifecycleStage;
        Notes = Trim(notes);
        UpdatedAt = now;
    }

    public void MoveTo(CustomerLifecycleStage stage, DateTimeOffset now)
    {
        LifecycleStage = stage;
        UpdatedAt = now;
    }

    private static void Validate(string name, string? email)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A client needs a name.", nameof(name));
        if (!string.IsNullOrWhiteSpace(email) && !email.Contains('@', StringComparison.Ordinal))
            throw new ArgumentException("Email must be a valid address.", nameof(email));
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
