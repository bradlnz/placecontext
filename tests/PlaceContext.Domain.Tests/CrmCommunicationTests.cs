using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Tests;

public sealed class CrmCommunicationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 31, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Note_is_added_to_the_internal_timeline()
    {
        var note = CrmCommunication.CreateNote(
            Guid.NewGuid(), Guid.NewGuid(), "  Follow up Monday  ", Guid.NewGuid(), T0);

        Assert.Equal(CrmCommunicationChannel.Note, note.Channel);
        Assert.Equal(CrmCommunicationStatus.Added, note.Status);
        Assert.Equal("Follow up Monday", note.Body);
        Assert.Null(note.Recipient);
    }

    [Fact]
    public void Email_requires_subject_and_records_successful_delivery()
    {
        var message = CrmCommunication.CreateOutbound(
            Guid.NewGuid(), Guid.NewGuid(), CrmCommunicationChannel.Email,
            " Next steps ", "Thanks for your time.", "ada@example.test", Guid.NewGuid(), T0);

        message.MarkSent("SendGrid", "message-123", T0.AddSeconds(2));

        Assert.Equal(CrmCommunicationStatus.Sent, message.Status);
        Assert.Equal("Next steps", message.Subject);
        Assert.Equal("message-123", message.ExternalId);
        Assert.Equal(T0.AddSeconds(2), message.SentAt);
    }

    [Fact]
    public void Failed_delivery_remains_in_the_timeline_with_an_error()
    {
        var message = CrmCommunication.CreateOutbound(
            Guid.NewGuid(), Guid.NewGuid(), CrmCommunicationChannel.Sms,
            null, "Hello", "+61400000000", Guid.NewGuid(), T0);

        message.MarkFailed("Twilio", "Sender is not configured.");

        Assert.Equal(CrmCommunicationStatus.Failed, message.Status);
        Assert.Equal("Sender is not configured.", message.Error);
        Assert.Null(message.SentAt);
    }

    [Fact]
    public void Outbound_email_rejects_an_empty_subject()
        => Assert.Throws<ArgumentException>(() => CrmCommunication.CreateOutbound(
            Guid.NewGuid(), Guid.NewGuid(), CrmCommunicationChannel.Email,
            null, "Hello", "ada@example.test", Guid.NewGuid(), T0));
}
