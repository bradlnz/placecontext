namespace PlaceContext.Application.Dtos;

/// <summary>Read model: the cross-project Change Ledger feed.</summary>
public sealed record RootLedgerView(IReadOnlyList<LedgerEntryView> Entries);
