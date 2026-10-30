using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Infrastructure.Workload;

public sealed class DbQemuReplayStore(DbContextOptions<AppDbContext> options, ICurrentTenant tenant,
    IDataEncryptor encryptor, IPermissionService permissions) : IQemuReplayStore
{
    private const string Purpose = IDataEncryptor.Purpose.ChainRun + ".qemu-replay";

    // A separate context keeps capture writes independent of the workflow cancellation poll.
    public async Task AppendAsync(Guid runId, int step, QemuReplayCapture capture, CancellationToken ct)
    {
        if (!tenant.IsResolved) throw new UnauthorizedAccessException("A workspace is required.");
        var frame = capture.Frame;
        if (frame.Sequence is < 0 or >= IQemuReplayStore.MaxFrames || frame.ElapsedMilliseconds < 0 ||
            frame.Description.Length > 200 || (frame.Error?.Length ?? 0) > 500 || frame.HasImage != (capture.Png is not null)
            || frame.PointerX.HasValue != frame.PointerY.HasValue
            || frame.PointerX is { } x && (!double.IsFinite(x) || x is < 0 or > 1)
            || frame.PointerY is { } y && (!double.IsFinite(y) || y is < 0 or > 1)
            || frame.Click && frame.PointerX is null)
            throw new ArgumentException("Invalid replay frame.");
        var png = capture.Png;
        if (png is not null && (png.Length is < 8 or > IQemuReplayStore.MaxImageBytes ||
            !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })))
            throw new ArgumentException("Replay images must be bounded PNG screenshots.");
        await using var db = new AppDbContext(options, tenant);
        var run = await new EfChainRunRepository(db, encryptor).GetByIdAsync(runId, ct);
        if (run is null || !run.Steps.Any(s => s.Index == step && s.ActionType == QemuChainAction.ActionType))
            throw new InvalidOperationException("The QEMU workflow step no longer exists.");
        await db.Database.OpenConnectionAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("SELECT 1 FROM chain_runs WHERE \"Id\" = @run AND \"TenantId\" = @tenant FOR UPDATE", connection, transaction);
        command.Parameters.AddWithValue("run", runId);
        command.Parameters.AddWithValue("tenant", tenant.TenantId);
        command.Parameters.AddWithValue("step", step);
        if (await command.ExecuteScalarAsync(ct) is null) throw new InvalidOperationException("The workflow run no longer exists.");
        command.CommandText = "SELECT COALESCE(SUM(\"ImageBytes\"), 0) FROM qemu_replay_frames WHERE \"RunId\" = @run AND \"TenantId\" = @tenant AND \"Step\" = @step";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(ct)) + (png?.Length ?? 0) > IQemuReplayStore.MaxReplayBytes)
        { png = null; frame = frame with { HasImage = false, Error = "Replay image storage limit reached (32 MiB)." }; }
        command.CommandText = """
            INSERT INTO qemu_replay_frames ("TenantId", "RunId", "Step", "Sequence", "Metadata", "Image", "ImageBytes")
            VALUES (@tenant, @run, @step, @sequence, @metadata, @image, @bytes)
            ON CONFLICT ("TenantId", "RunId", "Step", "Sequence") DO NOTHING
            """;
        command.Parameters.AddWithValue("sequence", frame.Sequence);
        command.Parameters.AddWithValue("metadata", encryptor.Protect(JsonSerializer.Serialize(frame), Purpose));
        command.Parameters.AddWithValue("image", NpgsqlDbType.Bytea, png is null ? DBNull.Value : encryptor.ProtectBytes(png, Purpose));
        command.Parameters.AddWithValue("bytes", png?.Length ?? 0);
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<QemuReplayFrame>> ListAsync(Guid projectId, Guid runId, int step, CancellationToken ct)
    {
        await RequireReadAsync(ct);
        await using var db = new AppDbContext(options, tenant);
        if (!await CanReadRunAsync(db, projectId, runId, ct)) return [];
        await db.Database.OpenConnectionAsync(ct);
        await using var command = ReadCommand(db, runId, step, "\"Metadata\"");
        var frames = new List<QemuReplayFrame>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) frames.Add(JsonSerializer.Deserialize<QemuReplayFrame>(encryptor.Unprotect(reader.GetString(0), Purpose))!);
        return frames;
    }

    public async Task<byte[]?> ReadImageAsync(Guid projectId, Guid runId, int step, int sequence, CancellationToken ct)
    {
        await RequireReadAsync(ct);
        if (sequence is < 0 or >= IQemuReplayStore.MaxFrames) return null;
        await using var db = new AppDbContext(options, tenant);
        if (!await CanReadRunAsync(db, projectId, runId, ct)) return null;
        await db.Database.OpenConnectionAsync(ct);
        await using var command = ReadCommand(db, runId, step, "\"Image\"", "AND \"Sequence\" = @sequence");
        command.Parameters.AddWithValue("sequence", sequence);
        return await command.ExecuteScalarAsync(ct) is byte[] cipher ? encryptor.UnprotectBytes(cipher, Purpose) : null;
    }

    private async Task RequireReadAsync(CancellationToken ct)
    {
        if (!tenant.IsResolved || !await permissions.HasAsync(Permission.JobsView, ct))
            throw new UnauthorizedAccessException("Workflow viewing permission is required.");
    }
    private static Task<bool> CanReadRunAsync(AppDbContext db, Guid projectId, Guid runId, CancellationToken ct) =>
        db.ChainRuns.AnyAsync(run => run.Id == runId && run.ProjectId == projectId, ct);
    private NpgsqlCommand ReadCommand(AppDbContext db, Guid runId, int step, string column, string condition = "")
    {
        var command = new NpgsqlCommand($"SELECT {column} FROM qemu_replay_frames WHERE \"TenantId\" = @tenant AND \"RunId\" = @run AND \"Step\" = @step {condition} ORDER BY \"Sequence\" LIMIT 101", (NpgsqlConnection)db.Database.GetDbConnection());
        command.Parameters.AddWithValue("tenant", tenant.TenantId);
        command.Parameters.AddWithValue("run", runId);
        command.Parameters.AddWithValue("step", step);
        return command;
    }
}
