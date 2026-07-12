using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

/// <summary>
/// Creates or replaces a named VIEW in the project's database from a SELECT (joins across the
/// project's tables). Guarded twice: the definition must be a single SELECT-only statement, and
/// the DDL executes as the project's own Postgres role inside its schema — so a view can only read
/// this project's tables, and data edits remain possible only on tables the project defines
/// (system tables stay read-only at the Postgres level).
/// </summary>
public sealed record SaveProjectViewCommand(Guid ProjectId, string Name, string SelectSql) : ICommand<bool>;

/// <summary>Drops a view from the project's schema.</summary>
public sealed record DropProjectViewCommand(Guid ProjectId, string Name) : ICommand<bool>;

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

    /// <summary>Identifier gate matching the store's rules (letters/digits/underscores, ≤63).</summary>
    public static string Ident(string name)
    {
        var n = (name ?? "").Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(n, @"^[A-Za-z_][A-Za-z0-9_]{0,62}$"))
            throw new ArgumentException("View names use letters, digits and underscores (max 63, no leading digit).");
        return n;
    }
}

public sealed class DropProjectViewHandler : ICommandHandler<DropProjectViewCommand, bool>
{
    private readonly IProjectDataStore _store;

    public DropProjectViewHandler(IProjectDataStore store) => _store = store;

    public async Task<bool> HandleAsync(DropProjectViewCommand command, CancellationToken ct = default)
    {
        var name = SaveProjectViewHandler.Ident(command.Name);
        await _store.ExecuteAsync(command.ProjectId, $"DROP VIEW IF EXISTS \"{name}\"", ct);
        return true;
    }
}
