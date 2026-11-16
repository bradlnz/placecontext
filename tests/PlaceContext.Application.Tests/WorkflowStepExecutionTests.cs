using PlaceContext.Application.Features;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Tests;

public sealed class WorkflowStepExecutionTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 3)]
    public async Task Failed_results_and_exceptions_respect_the_retry_budget(int retries, int expected)
    {
        var calls = 0; var recorded = new List<int>();
        var result = await WorkflowStepExecution.RunAsync(new(retries, 0), _ => Task.FromResult(++calls), default,
            retrying: (retry, _) => { recorded.Add(retry); return Task.CompletedTask; }, failed: _ => true);
        Assert.Equal(expected, result); Assert.Equal(retries, recorded.Count);
        calls = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => WorkflowStepExecution.RunAsync<int>(new(retries, 0),
            _ => { calls++; throw new InvalidOperationException("Unavailable"); }, default));
        Assert.Equal(expected, calls);
    }

    [Fact]
    public async Task Recovery_stops_retrying_and_an_existing_budget_is_respected()
    {
        var calls = 0;
        Assert.Equal(2, await WorkflowStepExecution.RunAsync(new(3, 0), _ => Task.FromResult(++calls), default, failed: value => value < 2));
        calls = 0;
        await WorkflowStepExecution.RunAsync(new(3, 0), _ => Task.FromResult(++calls), default, retriesUsed: 2, failed: _ => true);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Timeouts_cancel_the_attempt_before_retrying_and_end_as_failures()
    {
        var active = 0; var cancelled = 0; var calls = 0;
        var error = await Assert.ThrowsAsync<TimeoutException>(() => WorkflowStepExecution.RunAsync(new(1, 0, 1), async token =>
        {
            Assert.Equal(1, Interlocked.Increment(ref active)); calls++;
            try { await Task.Delay(Timeout.Infinite, token); return true; }
            finally { Interlocked.Decrement(ref active); cancelled++; }
        }, default));
        Assert.Equal(2, calls); Assert.Equal(2, cancelled); Assert.Contains("1s", error.Message);
    }

    [Fact]
    public async Task Cancellation_during_delay_prevents_another_attempt()
    {
        using var cancellation = new CancellationTokenSource(); var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WorkflowStepExecution.RunAsync(new(3, 60),
            _ => Task.FromResult(++calls), cancellation.Token,
            retrying: (_, _) => { cancellation.Cancel(); return Task.CompletedTask; }, failed: _ => true));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Permission_and_validation_errors_are_not_retried()
    {
        foreach (var error in new Exception[] { new UnauthorizedAccessException(), new ArgumentException() })
        {
            var calls = 0;
            var actual = await Assert.ThrowsAnyAsync<Exception>(() => WorkflowStepExecution.RunAsync<int>(new(3, 0),
                _ => { calls++; throw error; }, default));
            Assert.Same(error, actual); Assert.Equal(1, calls);
        }
    }

    [Theory]
    [InlineData(-1, 0, 1)]
    [InlineData(11, 0, 1)]
    [InlineData(1, -1, 1)]
    [InlineData(1, 3601, 1)]
    [InlineData(1, 0, 0)]
    [InlineData(1, 0, 86401)]
    public void Invalid_limits_are_rejected(int retries, int delay, int timeout) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new StepExecutionPolicy(retries, delay, timeout));
}
