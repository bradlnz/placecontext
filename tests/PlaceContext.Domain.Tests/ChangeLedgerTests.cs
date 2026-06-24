using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using Xunit;

namespace PlaceContext.Domain.Tests;

public class ChangeLedgerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProjectId Pid = ProjectId.New();

    private static ChangeRecord Append(ChangeLedger ledger, params string[] nodeIds) =>
        ledger.Append(
            "change summary",
            Author.Agent("claude"),
            Rationale.Of("because"),
            TestDelta.None,
            DebtDelta.None,
            ChangeVerification.None,
            touchedFiles: new[] { "a.cs" },
            touchedNodes: nodeIds.Select(GraphNodeId.From),
            T0);

    [Fact]
    public void Append_assigns_monotonic_contiguous_sequences()
    {
        var ledger = ChangeLedger.Start(Pid);
        var a = Append(ledger);
        var b = Append(ledger);
        var c = Append(ledger);

        Assert.Equal(new[] { 1, 2, 3 }, new[] { a.Sequence, b.Sequence, c.Sequence });
        Assert.Equal(3, ledger.Count);
    }

    [Fact]
    public void Append_raises_ChangeRecorded()
    {
        var ledger = ChangeLedger.Start(Pid);
        Append(ledger);
        Assert.Contains(ledger.PullDomainEvents(), e => e is Events.ChangeRecorded);
    }

    [Fact]
    public void RecentWindow_returns_last_n_newest_last()
    {
        var ledger = ChangeLedger.Start(Pid);
        for (var i = 0; i < 5; i++) Append(ledger);

        var window = ledger.RecentWindow(2);
        Assert.Equal(new[] { 4, 5 }, window.Select(r => r.Sequence));
    }

    [Fact]
    public void TouchesWithin_detects_recent_churn_of_same_node()
    {
        var ledger = ChangeLedger.Start(Pid);
        Append(ledger, "auth");          // seq 1 touches auth
        Append(ledger, "billing");       // seq 2
        var latest = Append(ledger, "auth"); // seq 3 touches auth again

        var reTouched = ledger.TouchesWithin(
            latest.TouchedNodes, window: 3, excludingSequence: latest.Sequence);

        Assert.True(reTouched);
    }

    [Fact]
    public void TouchesWithin_false_when_node_not_seen_recently()
    {
        var ledger = ChangeLedger.Start(Pid);
        Append(ledger, "auth");
        var latest = Append(ledger, "payments");

        var reTouched = ledger.TouchesWithin(
            latest.TouchedNodes, window: 1, excludingSequence: latest.Sequence);

        Assert.False(reTouched);
    }

    [Fact]
    public void AttachCommit_is_one_time_only()
    {
        var ledger = ChangeLedger.Start(Pid);
        var record = Append(ledger);
        record.AttachCommit(CommitSha.From("abc1234"));

        Assert.True(record.IsCommitted);
        Assert.Throws<InvalidOperationException>(() => record.AttachCommit(CommitSha.From("def5678")));
    }
}
