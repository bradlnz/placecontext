using PlaceContext.Application.Access;
using PlaceContext.Application.Features;
using PlaceContext.Application.DataSources;
using PlaceContext.Application.Search;
using PlaceContext.TestSupport;

namespace PlaceContext.Application.Tests;

public sealed class DataSourcePipelineTests
{
    [Fact]
    public async Task Queue_deduplication_cannot_hide_a_truncated_snapshot()
    {
        var store = new Store { Source = Definition() with { Queue = new(Guid.NewGuid(), [], []) } };
        var reader = new Reader { Rows = Enumerable.Repeat(new SourceRecord("1", "{\"id\":1}"), DataSourceRules.MaxRecords + 1).ToArray() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(store, reader, new()).SyncAsync(store.Source.ProjectId, store.Source.Id, Ct));
        Assert.Equal(0, store.Commits);
    }

    [Fact]
    public async Task Queue_preparation_holds_invalid_records_retries_corrections_and_never_embeds()
    {
        var store = new Store { Source = Definition() with { Provider = "feed", Queue = new(Guid.NewGuid(), ["body"], []) } };
        var embeddings = new Embeddings();
        var pipeline = Create(store, new Reader(), embeddings);
        var first = await pipeline.IngestAsync(store.Source.ProjectId, store.Source.Id,
            [new("1", "{\"id\":1,\"body\":\" kiwi \"}"), new("2", "{\"id\":2,\"body\":\" \"}")], [], Ct);
        Assert.Equal(1, first.Queued);
        Assert.Equal(1, first.Held);
        Assert.Equal(0, embeddings.Calls);
        Assert.All(store.Documents, document => Assert.Empty(document.Chunks));
        var retry = await pipeline.SyncAsync(store.Source.ProjectId, store.Source.Id, Ct);
        Assert.Equal(0, retry.Queued);
        Assert.Equal(1, retry.Held);
        var corrected = await pipeline.IngestAsync(store.Source.ProjectId, store.Source.Id,
            [new("2", "{\"id\":2,\"body\":\"pear\"}")], [], Ct);
        Assert.Equal(1, corrected.Queued);
        Assert.Equal(0, corrected.Held);
        Assert.Equal(1, (await pipeline.IngestAsync(store.Source.ProjectId, store.Source.Id, [], ["1"], Ct)).Deleted);
    }

    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Snapshots_reconcile_changes_and_deletions_with_stable_citations()
    {
        var store = new Store();
        var reader = new Reader { Rows = [new("1", "{\"id\":1,\"body\":\"kiwi\"}"), new("2", "{\"id\":2,\"body\":\"pear\"}")] };
        var embeddings = new Embeddings();
        var pipeline = Create(store, reader, embeddings);
        var first = await pipeline.SyncAsync(store.Source.ProjectId, store.Source.Id, Ct);
        Assert.Equal(2, first.Changed);
        var hit = Assert.Single((await pipeline.SearchAsync(store.Source.ProjectId, store.Source.Id, "kiwi", 10, Ct)).Hits);
        Assert.Equal("1", hit.RecordKey);
        Assert.Equal("public.notes", hit.Table);
        Assert.Contains("approved", hit.Tags);
        Assert.Equal(0, (await pipeline.SyncAsync(store.Source.ProjectId, store.Source.Id, Ct)).Changed);
        Assert.Equal(hit.Citation, Assert.Single((await pipeline.SearchAsync(store.Source.ProjectId, store.Source.Id, "kiwi", 10, Ct)).Hits).Citation);
        reader.Rows = [new("1", "{\"id\":1,\"body\":\"apple\"}")];
        var next = await pipeline.SyncAsync(store.Source.ProjectId, store.Source.Id, Ct);
        Assert.Equal(1, next.Changed);
        Assert.Equal(1, next.Deleted);
        Assert.Empty((await pipeline.SearchAsync(store.Source.ProjectId, store.Source.Id, "kiwi pear", 10, Ct)).Hits);
        Assert.Single((await pipeline.SearchAsync(store.Source.ProjectId, store.Source.Id, "approved", 10, Ct)).Hits);
        Assert.Equal(0, embeddings.Calls);
        reader.Rows = [];
        Assert.Equal(1, (await pipeline.SyncAsync(store.Source.ProjectId, store.Source.Id, Ct)).Deleted);
        Assert.Empty(store.Documents);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("empty")]
    [InlineData("oversize")]
    [InlineData("too-many")]
    [InlineData("failure")]
    public async Task Invalid_or_failed_snapshots_preserve_previous_records(string problem)
    {
        var store = new Store();
        var reader = new Reader { Rows = [new("1", "{\"id\":1}")] };
        var pipeline = Create(store, reader, new());
        await pipeline.SyncAsync(store.Source.ProjectId, store.Source.Id, Ct);
        var original = store.Documents;
        reader.Rows = problem switch
        {
            "duplicate" => [new("1", "{}"), new("1", "{}")],
            "empty" => [new("", "{}")],
            "oversize" => [new("1", new string('x', DataSourceRules.MaxRecordChars + 1))],
            "too-many" => Enumerable.Range(0, DataSourceRules.MaxRecords + 1).Select(i => new SourceRecord(i.ToString(), "{}")).ToArray(),
            _ => []
        };
        reader.Fail = problem == "failure";
        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.SyncAsync(store.Source.ProjectId, store.Source.Id, Ct));
        Assert.Same(original, store.Documents);
        Assert.Equal(1, store.Commits);
    }

    [Fact]
    public async Task Embedding_requires_consent_is_incremental_and_can_be_rebuilt()
    {
        var store = new Store { Source = Definition() with { EnableEmbeddings = true } };
        var reader = new Reader { Rows = [new("1", "{\"id\":1,\"body\":\"kiwi\"}")] };
        var embeddings = new Embeddings();
        var pipeline = Create(store, reader, embeddings);
        await pipeline.SyncAsync(store.Source.ProjectId, store.Source.Id, Ct);
        await pipeline.SyncAsync(store.Source.ProjectId, store.Source.Id, Ct);
        Assert.Equal(1, embeddings.Calls);
        Assert.Equal("semantic", (await pipeline.SearchAsync(store.Source.ProjectId, store.Source.Id, "fruit", 10, Ct)).Mode);
        await pipeline.SyncAsync(store.Source.ProjectId, store.Source.Id, Ct, rebuild: true);
        Assert.Equal(3, embeddings.Calls);
        var previous = store.Documents;
        embeddings.Invalid = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.SyncAsync(store.Source.ProjectId, store.Source.Id, Ct, rebuild: true));
        Assert.Same(previous, store.Documents);
        embeddings.Enabled = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.SyncAsync(store.Source.ProjectId, store.Source.Id, Ct));
        Assert.Same(previous, store.Documents);
    }

    [Fact]
    public async Task Cancellation_does_not_commit_or_delete_data()
    {
        var store = new Store();
        var reader = new Reader { Rows = [new("1", "{\"id\":1}")] };
        var pipeline = Create(store, reader, new());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.SyncAsync(store.Source.ProjectId, store.Source.Id, new(true)));
        Assert.Equal(0, store.Commits);
    }

    [Theory]
    [InlineData("bad-name")]
    [InlineData("notes;DROP TABLE notes")]
    [InlineData("")]
    public void Source_identifiers_are_validated_before_querying(string table)
        => Assert.Throws<ArgumentException>(() => DataSourceRules.Validate(Definition() with { Table = table }));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Source_workflow_uses_scoped_credentials_and_requires_write_permissions(bool allowed)
    {
        var store = new Store();
        var jobs = new InMemoryJobRepository();
        var chains = new InMemoryJobChainRepository();
        var handler = new CreateSourceWorkflowHandler(store, jobs, chains, new RecordingUnitOfWork(),
            new FakeClock(DateTimeOffset.UtcNow), new Permissions(allowed));
        var request = new CreateSourceWorkflowCommand(store.Source.ProjectId, store.Source.Id, "https://portal.example.com");
        if (!allowed)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.HandleAsync(request));
            Assert.Empty(await jobs.ListForProjectAsync(store.Source.ProjectId));
            return;
        }
        var chain = await handler.HandleAsync(request);
        Assert.NotNull(await chains.GetByIdAsync(chain));
        var job = Assert.Single(await jobs.ListForProjectAsync(store.Source.ProjectId));
        Assert.True(job.AllowNetworkEgress);
        Assert.Equal(store.Source.Id.ToString(), job.MapSpec.Env["PC_SYNC_SOURCE_ID"]);
        Assert.Equal("true", job.MapSpec.Env["PC_USE_PROJECT_DATA"]);
        Assert.False(job.MapSpec.Env.ContainsKey("PC_DATA_TOKEN"));
        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(request with { HostUrl = "https://user:password@example.com" }));
    }

    private sealed class Permissions(bool allowed) : IPermissionService
    {
        public Task<bool> HasAsync(string permission, CancellationToken ct = default) => Task.FromResult(allowed);
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetEffectivePermissionsForUserAsync(Guid userId, string roleName, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Feeds_upsert_idempotently_filter_fields_and_propagate_deletion()
    {
        var store = new Store { Source = Definition() with { Provider = "email" } };
        var pipeline = Create(store, new Reader { Fail = true }, new());
        var rows = new SourceRecord[] { new("message-1", "{\"id\":\"message-1\",\"body\":\"kiwi\",\"secret\":\"do not ingest\"}") };
        var result = await pipeline.IngestAsync(store.Source.ProjectId, store.Source.Id, rows, [], Ct);
        Assert.Equal(1, result.Changed);
        Assert.DoesNotContain("secret", Assert.Single(store.Documents).Json);
        var citation = Assert.Single((await pipeline.SearchAsync(store.Source.ProjectId, store.Source.Id, "kiwi", 10, Ct)).Hits).Citation;
        Assert.Equal(0, (await pipeline.IngestAsync(store.Source.ProjectId, store.Source.Id, rows, [], Ct)).Changed);
        Assert.Equal(1, (await pipeline.SyncAsync(store.Source.ProjectId, store.Source.Id, Ct)).Total);
        Assert.Equal(citation, Assert.Single((await pipeline.SearchAsync(store.Source.ProjectId, store.Source.Id, "kiwi", 10, Ct)).Hits).Citation);
        Assert.Equal(1, (await pipeline.IngestAsync(store.Source.ProjectId, store.Source.Id, [], ["message-1"], Ct)).Deleted);
        Assert.Empty(store.Documents);
    }

    [Theory]
    [InlineData("postgres", "{\"id\":\"1\"}")]
    [InlineData("feed", "{\"id\":\"different\"}")]
    [InlineData("feed", "[]")]
    public async Task Invalid_pushes_do_not_change_existing_records(string provider, string json)
    {
        var store = new Store { Source = Definition() with { Provider = provider } };
        var pipeline = Create(store, new(), new());
        await Assert.ThrowsAsync<ArgumentException>(() => pipeline.IngestAsync(store.Source.ProjectId, store.Source.Id, [new("1", json)], [], Ct));
        Assert.Equal(0, store.Commits);
    }

    [Fact]
    public async Task Feed_authentication_precedes_ingestion()
    {
        var handler = new ReceiveDataFeedHandler(new DeniedFeed(), null!);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.HandleAsync(new(Guid.NewGuid(), Guid.NewGuid(), "wrong", [], [])));
    }

    [Fact]
    public async Task Jev_checks_only_approved_fields_and_holds_records_before_indexing()
    {
        var store = new Store { Source = Definition() with { Provider = "feed", Processing = new("Contains useful support details", 0.9) } };
        var reviewer = new Reviewer();
        var pipeline = new DataSourcePipeline(store, new Reader(), new Embeddings(), new FakeClock(DateTimeOffset.UtcNow), reviewer);
        SourceRecord[] rows = [new("1", "{\"id\":\"1\",\"body\":\"kiwi\",\"secret\":\"private\"}"), new("2", "{\"id\":\"2\",\"body\":\"spam\"}")];
        var result = await pipeline.IngestAsync(store.Source.ProjectId, store.Source.Id, rows, [], Ct);
        Assert.Equal(1, result.Held);
        Assert.Equal("needs-review", store.Source.Execution!.Status);
        Assert.Equal(2, store.Source.Execution.Processed);
        Assert.All(reviewer.States, state => Assert.DoesNotContain("secret", state));
        var held = store.Documents.Single(doc => doc.Key == "2");
        Assert.Empty(held.Chunks);
        Assert.False(DataSourcePipeline.IsRetrievable(store.Source, held));
        Assert.Single((await pipeline.SearchAsync(store.Source.ProjectId, store.Source.Id, "kiwi spam", 10, Ct)).Hits);
        await pipeline.IngestAsync(store.Source.ProjectId, store.Source.Id, rows, [], Ct);
        Assert.Equal(2, reviewer.States.Count);
        store.Source = store.Source with { Processing = new("Changed criteria", 0.99) };
        Assert.Empty((await pipeline.SearchAsync(store.Source.ProjectId, store.Source.Id, "kiwi", 10, Ct)).Hits);
        await pipeline.SyncAsync(store.Source.ProjectId, store.Source.Id, Ct);
        Assert.Equal(4, reviewer.States.Count);
        Assert.Equal(2, store.Source.Execution!.Held);
    }

    [Theory]
    [InlineData("unavailable", "failed")]
    [InlineData("invalid", "failed")]
    [InlineData("cancelled", "interrupted")]
    public async Task Failed_Jev_runs_preserve_the_last_committed_snapshot(string failure, string status)
    {
        var store = new Store { Source = Definition() with { Provider = "feed", Processing = new("Useful details") } };
        using var cancellation = new CancellationTokenSource();
        var reviewer = new Reviewer();
        var pipeline = new DataSourcePipeline(store, new Reader(), new Embeddings(), new FakeClock(DateTimeOffset.UtcNow), reviewer);
        await pipeline.IngestAsync(store.Source.ProjectId, store.Source.Id, [new("1", "{\"id\":\"1\",\"body\":\"kiwi\"}")], [], Ct);
        var previous = store.Documents;
        reviewer.Failure = failure;
        reviewer.Cancellation = cancellation;
        await Assert.ThrowsAnyAsync<Exception>(() => pipeline.IngestAsync(store.Source.ProjectId, store.Source.Id,
            [new("1", "{\"id\":\"1\",\"body\":\"replacement\"}")], [], cancellation.Token));
        Assert.Same(previous, store.Documents);
        Assert.Equal(1, store.Commits);
        Assert.Equal(status, store.Source.Execution!.Status);
    }

    [Fact]
    public async Task An_active_run_cannot_be_replaced_by_another_ingestion()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var store = new Store { Source = Definition() with { Execution = new(Guid.NewGuid(), "running", "Receiving", clock.UtcNow) } };
        var original = store.Source;
        var pipeline = new DataSourcePipeline(store, new Reader(), new Embeddings(), clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.SyncAsync(store.Source.ProjectId, store.Source.Id, Ct));
        Assert.Same(original, store.Source);
    }

    [Fact]
    public async Task Jev_configuration_requires_write_access_and_consent_and_records_the_approver()
    {
        var store = new Store();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var user = new User();
        var command = new ConfigureSourceProcessingCommand(store.Source.ProjectId, store.Source.Id,
            new("Useful support detail", ApprovedBy: Guid.NewGuid()), true);
        var denied = new ConfigureSourceProcessingHandler(store, new Permissions(false), user, clock);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.HandleAsync(command));
        var handler = new ConfigureSourceProcessingHandler(store, new Permissions(true), user, clock);
        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(command with { ProcessingApproved = false }));
        Assert.Null(store.Source.Processing);
        var updated = await handler.HandleAsync(command);
        Assert.Equal(user.UserId, updated.Processing!.ApprovedBy);
        Assert.Equal(clock.UtcNow, updated.Processing.ApprovedAt);
        Assert.Equal("pending", updated.Execution!.Status);
        store.Source = updated with { Execution = new(Guid.NewGuid(), "running", "Receiving", clock.UtcNow) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(command));
    }

    private sealed class User : PlaceContext.Application.Tenancy.ICurrentUser
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public string Role => "Owner";
        public bool IsAuthenticated => true;
    }

    private sealed class Reviewer : PlaceContext.Application.Build.IOutcomeReviewer
    {
        public List<string> States { get; } = [];
        public string? Failure { get; set; }
        public CancellationTokenSource? Cancellation { get; set; }
        public Task<System.Text.Json.Nodes.JsonObject> EvaluateAsync(string question, string? state, CancellationToken ct, Guid? projectId = null)
        {
            States.Add(state!);
            if (Failure == "unavailable")
            {
                throw new InvalidOperationException("provider private detail");
            }
            if (Failure == "cancelled")
            {
                Cancellation!.Cancel();
                ct.ThrowIfCancellationRequested();
            }
            return Task.FromResult(new System.Text.Json.Nodes.JsonObject
            {
                ["noul"] = Failure == "invalid" ? 2 : state!.Contains("spam") ? 0.2 : 0.95,
                ["model"] = "test-reviewer"
            });
        }
    }

    private sealed class DeniedFeed : IDataFeedAccess
    {
        public Task<bool> AuthorizeAsync(Guid projectId, Guid sourceId, string token, CancellationToken ct) => Task.FromResult(false);
    }

    private static DataSourceDefinition Definition() => new(Guid.NewGuid(), Guid.NewGuid(), "Notes", "public", "notes", "id", ["id", "body"], ["approved"], false, Guid.NewGuid());
    private static DataSourcePipeline Create(Store store, Reader reader, Embeddings embeddings)
        => new(store, reader, embeddings, new FakeClock(DateTimeOffset.Parse("2026-11-03T00:00:00Z")));

    private sealed class Reader : IDataSourceReader
    {
        public IReadOnlyList<SourceRecord> Rows { get; set; } = [];
        public bool Fail { get; set; }
        public Task<IReadOnlyList<SourceRecord>> ReadAsync(DataSourceDefinition source, CancellationToken ct)
            => Fail ? throw new InvalidOperationException("Unavailable") : Task.FromResult(Rows);
    }

    private sealed class Embeddings : IEmbeddingGateway
    {
        public int Calls { get; private set; }
        public bool Enabled { get; set; } = true;
        public bool Invalid { get; set; }
        public bool IsEnabled => Enabled;
        public int Dimensions => 2;
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => Invalid ? new float[] { float.NaN, 0 } : new float[] { 1, 0 }).ToArray());
        }
    }

    private sealed class Store : IDataSourceStore
    {
        public DataSourceDefinition Source { get; set; } = Definition();
        public IReadOnlyList<SourceDocument> Documents { get; private set; } = [];
        public int Commits { get; private set; }
        public Task<DataSourceDefinition> GetAsync(Guid projectId, Guid sourceId, CancellationToken ct) => Task.FromResult(Source);
        public Task<IReadOnlyList<SourceDocument>> ReadDocumentsAsync(Guid projectId, Guid sourceId, CancellationToken ct) => Task.FromResult(Documents);
        public Task CommitAsync(DataSourceDefinition source, IReadOnlyList<SourceDocument> documents, DateTimeOffset now, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Source = source with { SyncedAt = now, RecordCount = documents.Count };
            Documents = documents;
            Commits++;
            return Task.CompletedTask;
        }
        public Task<DataSourceDefinition> UpdateAsync(DataSourceDefinition source, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Source = source with { Version = Guid.NewGuid() };
            return Task.FromResult(Source);
        }
        public Task<IReadOnlyList<DataSourceDefinition>> ListAsync(Guid projectId, CancellationToken ct) => throw new NotSupportedException();
        public Task<DataSourceDefinition> CreateAsync(DataSourceDefinition source, string connectionString, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(Guid projectId, Guid sourceId, CancellationToken ct) => throw new NotSupportedException();
    }
}
