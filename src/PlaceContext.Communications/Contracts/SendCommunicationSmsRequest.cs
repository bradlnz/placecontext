namespace PlaceContext.Communications.Contracts;

public sealed record SendCommunicationSmsRequest(
    string Recipient,
    string Body,
    bool Authentication = false);
