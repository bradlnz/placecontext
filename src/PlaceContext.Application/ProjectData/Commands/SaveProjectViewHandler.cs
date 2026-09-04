using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed class SaveProjectViewHandler : ICommandHandler<SaveProjectViewCommand, bool>
{
    private readonly IProjectDataStore _store;

    public SaveProjectViewHandler(IProjectDataStore store) => _store = store;

    public async Task<bool> HandleAsync(SaveProjectViewCommand command, CancellationToken ct = default)
    {
        var name = Ident(command.Name);
        var select = EnsureSelectOnly(command.SelectSql);
        await _store.ExecuteAsync(command.ProjectId, $"CREATE OR REPLACE VIEW \"{name}\" AS {select}", ct);
        return true;
    }

    /// <summary>SELECT-only gate shared by the data tools: one statement, no write/DDL keywords.</summary>
    public static string EnsureSelectOnly(string sql)
    {
        var trimmed = (sql ?? "").Trim().TrimEnd(';').Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("A SELECT statement is required.");
        if (trimmed.Contains(';'))
            throw new ArgumentException("Only a single statement is allowed — remove the semicolons.");
        if (!trimmed.StartsWith("select", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only SELECT statements are allowed here.");
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed,
                @"\b(insert|update|delete|drop|alter|create|truncate|grant|revoke|copy|vacuum|call|do|comment|reindex|cluster|refresh|listen|notify|prepare|deallocate|import|merge)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new ArgumentException("Only read-only SELECT statements are allowed — write/DDL keywords are rejected.");
        return trimmed;
    }

    /// <summary>Identifier gate matching the store's rules (letters/digits/underscores, ≤63)
    /// plus the reserved-name set for the <c>/api/v1</c> entity surface.</summary>
    public static string Ident(string name)
    {
        var n = (name ?? "").Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(n, @"^[A-Za-z_][A-Za-z0-9_]{0,62}$"))
            throw new ArgumentException("View names use letters, digits and underscores (max 63, no leading digit).");
        ProjectDataReservedNames.EnsureAllowed(n, "view name");
        return n;
    }
}
