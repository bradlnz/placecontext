using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Files;

/// <summary>
/// Knowledge context as an encrypted file under <see cref="PlaceContextOptions.DataRoot"/>, not a DB
/// column. Wire path: <c>{DataRoot}/knowledge/{projectId}/context.pcenc</c>. On first read after
/// upgrade, any legacy <c>project_contexts</c> row is migrated to disk and the DB body is cleared.
/// </summary>
public sealed class FileProjectContextRepository : IProjectContextRepository
{
    private readonly string _root;
    private readonly IDataEncryptor _enc;
    private readonly AppDbContext _db;
    private readonly IContentIndexer? _indexer;
    private static string P => IDataEncryptor.Purpose.Context;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>Hard cap on context markdown (chars) — defends against OOM from oversized agent writes.</summary>
    public const int MaxMarkdownChars = 2 * 1024 * 1024; // 2 MiB

    public FileProjectContextRepository(
        IOptions<PlaceContextOptions> options,
        IDataEncryptor enc,
        AppDbContext db,
        IContentIndexer? indexer = null)
    {
        _root = options.Value.DataRoot;
        _enc = enc;
        _db = db;
        _indexer = indexer;
    }

    public async Task<ProjectContext?> GetForProjectAsync(ProjectId projectId, CancellationToken ct = default)
    {
        var path = PathFor(projectId);
        if (File.Exists(path))
        {
            var envelope = await ReadEnvelopeAsync(path, ct);
            return envelope is null
                ? null
                : ProjectContext.Rehydrate(projectId, envelope.Markdown, envelope.CreatedAt, envelope.UpdatedAt);
        }

        // One-shot legacy migrate: DB row → encrypted file.
        var row = await _db.ProjectContexts.FirstOrDefaultAsync(x => x.ProjectId == projectId.Value, ct);
        if (row is null) return null;

        var markdown = _enc.Unprotect(row.Markdown, P);
        var ctx = ProjectContext.Rehydrate(projectId, markdown, row.CreatedAt, row.UpdatedAt);
        await WriteFileAsync(ctx, ct);
        // Clear body so a dump of project_contexts no longer holds knowledge plaintext/ciphertext.
        row.Markdown = "";
        await _db.SaveChangesAsync(ct);
        return ctx;
    }

    public async Task SaveAsync(ProjectContext context, CancellationToken ct = default)
    {
        if (context.Markdown.Length > MaxMarkdownChars)
            throw new InvalidOperationException(
                $"Context is too large ({context.Markdown.Length:N0} chars; max {MaxMarkdownChars:N0}).");
        await WriteFileAsync(context, ct);

        // Keep a thin DB row for tenant/project inventory if one already exists; never store body.
        var row = await _db.ProjectContexts.FindAsync(new object[] { context.ProjectId.Value }, ct);
        if (row is not null)
        {
            row.Markdown = "";
            row.UpdatedAt = context.UpdatedAt;
        }

        // Index for RAG (encrypted text in content_embeddings).
        if (_indexer is { IsEnabled: true } && !string.IsNullOrWhiteSpace(context.Markdown))
        {
            try
            {
                await _indexer.IndexAsync(
                    context.ProjectId.Value, ContentKind.Context, "context", context.Markdown, ct);
            }
            catch { /* best-effort */ }
        }
    }

    private async Task WriteFileAsync(ProjectContext context, CancellationToken ct)
    {
        var path = PathFor(context.ProjectId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var envelope = new Envelope(context.Markdown, context.CreatedAt, context.UpdatedAt);
        var plain = JsonSerializer.Serialize(envelope, Json);
        var cipher = _enc.Protect(plain, P);
        // Atomic replace: write temp then move so readers never see a half-written file.
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, cipher, ct);
        File.Move(tmp, path, overwrite: true);
    }

    private async Task<Envelope?> ReadEnvelopeAsync(string path, CancellationToken ct)
    {
        var cipher = await File.ReadAllTextAsync(path, ct);
        if (cipher.Length > MaxMarkdownChars * 2) // ciphertext can be larger than plain
            throw new InvalidOperationException("Context file on disk exceeds safe size limit.");
        var plain = _enc.Unprotect(cipher, P);
        if (string.IsNullOrWhiteSpace(plain)) return null;
        try
        {
            return JsonSerializer.Deserialize<Envelope>(plain, Json);
        }
        catch (JsonException)
        {
            // Pre-envelope plain markdown file (if any) — treat whole body as markdown.
            var now = DateTimeOffset.UtcNow;
            return new Envelope(plain, now, now);
        }
    }

    internal string PathFor(ProjectId projectId)
        => Path.Combine(_root, "knowledge", projectId.Value.ToString("N"), "context.pcenc");

    private sealed record Envelope(string Markdown, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
