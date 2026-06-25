namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// Value Object: a declared input field a job needs before it can run (e.g. a customer email from a
/// form). When a job declares parameters, a manual run prompts for them (a modal in the portal) and an
/// event-source run injects matching fields from the event payload. The collected values are assembled
/// into the run's input payload. Industry-agnostic — PlaceContext never interprets the values.
/// </summary>
public sealed record JobParameter
{
    public JobParameter(string name, string? label = null, bool required = true)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Parameter name must not be empty.", nameof(name));
        Name = name.Trim();
        Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        Required = required;
    }

    /// <summary>Field key used in the assembled payload JSON, e.g. "customerEmail".</summary>
    public string Name { get; }

    /// <summary>Optional human label shown in the prompt; falls back to <see cref="Name"/>.</summary>
    public string? Label { get; }

    /// <summary>Whether a value must be supplied before the job can run.</summary>
    public bool Required { get; }
}
