using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>
/// The inbound SMS gateway: sender and body are encrypted before anything is stored, a
/// sms.received event fires (with the sender masked — never the body), provider retries are
/// deduplicated, and reads decrypt for display.
/// </summary>
public class InboundSmsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Reversible marker "encryption" so tests can assert ciphertext-at-rest without real keys.</summary>
    private sealed class MarkingProtector : ISecretProtector
    {
        public string Protect(string plaintext) => "enc:" + plaintext;
        public string Unprotect(string ciphertext) => ciphertext.StartsWith("enc:") ? ciphertext[4..] : ciphertext;
    }

    private sealed class InMemorySmsRepository : IInboundSmsRepository
    {
        public readonly List<SmsMessage> Messages = new();

        public Task AddAsync(SmsMessage message, CancellationToken ct = default)
        { Messages.Add(message); return Task.CompletedTask; }

        public Task<IReadOnlyList<SmsMessage>> ListRecentAsync(int take = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SmsMessage>>(
                Messages.OrderByDescending(m => m.ReceivedAt).Take(take).ToList());

        public Task<bool> ExistsAsync(string provider, string externalId, CancellationToken ct = default)
            => Task.FromResult(Messages.Any(m => m.Provider == provider && m.ExternalId == externalId));
    }

    private sealed class RecordingDispatcher : IDispatcher
    {
        public readonly List<object> Sent = new();

        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
        {
            Sent.Add(command);
            return Task.FromResult(default(TResult)!);
        }

        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
            => Task.FromResult(default(TResult)!);
    }

    private static (ReceiveInboundSmsHandler handler, InMemorySmsRepository repo, RecordingDispatcher bus) Handler()
    {
        var repo = new InMemorySmsRepository();
        var bus = new RecordingDispatcher();
        return (new ReceiveInboundSmsHandler(repo, new MarkingProtector(), bus, new RecordingUnitOfWork(), new FakeClock(T0)), repo, bus);
    }

    [Fact]
    public async Task Sender_and_body_are_stored_encrypted_and_never_appear_in_the_event()
    {
        var (handler, repo, bus) = Handler();

        var view = await handler.HandleAsync(new ReceiveInboundSmsCommand(
            "+15551234567", "+15550001111", "the door code is 4321", "twilio", "SM123", null));

        var stored = repo.Messages.Single();
        Assert.Equal("enc:+15551234567", stored.FromProtected);         // ciphertext at rest
        Assert.Equal("enc:the door code is 4321", stored.BodyProtected);
        Assert.Equal("+15550001111", stored.To);                         // routing metadata stays plain

        var evt = Assert.IsType<EmitEventCommand>(bus.Sent.Single());
        Assert.Equal("sms.received", evt.Name);
        Assert.DoesNotContain("4321", evt.Payload);                      // body never leaves ciphertext
        Assert.DoesNotContain("+15551234567", evt.Payload);              // full sender never in the event
        Assert.Contains("4567", evt.Payload!);                           // masked tail is enough to route on

        Assert.Equal("+15551234567", view.From);                         // the caller's view is plaintext
        Assert.Equal("the door code is 4321", view.Body);
    }

    [Fact]
    public async Task Provider_retries_with_the_same_external_id_are_deduplicated()
    {
        var (handler, repo, bus) = Handler();
        var cmd = new ReceiveInboundSmsCommand("+15551234567", "+15550001111", "hello", "twilio", "SM123", null);

        await handler.HandleAsync(cmd);
        await handler.HandleAsync(cmd); // webhook retry

        Assert.Single(repo.Messages);
        Assert.Single(bus.Sent); // and the event fired once
    }

    [Fact]
    public async Task Blank_sender_or_body_is_rejected()
    {
        var (handler, _, _) = Handler();
        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(
            new ReceiveInboundSmsCommand(" ", "+15550001111", "hi", "generic", null, null)));
        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(
            new ReceiveInboundSmsCommand("+15551234567", "+15550001111", "", "generic", null, null)));
    }

    [Fact]
    public async Task Listing_decrypts_for_display_newest_first()
    {
        var (handler, repo, _) = Handler();
        await handler.HandleAsync(new ReceiveInboundSmsCommand("+15551111111", "+15550001111", "first", "generic", null, null));
        await handler.HandleAsync(new ReceiveInboundSmsCommand("+15552222222", "+15550001111", "second", "generic", null, null));

        var list = await new ListInboundSmsHandler(repo, new MarkingProtector())
            .HandleAsync(new ListInboundSmsQuery(10));

        Assert.Equal(2, list.Count);
        Assert.All(list, m => Assert.DoesNotContain("enc:", m.Body));
        Assert.Contains(list, m => m.From == "+15552222222" && m.Body == "second");
    }
}
