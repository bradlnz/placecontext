using System.Collections.Concurrent;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

public sealed class FakeToolCallLog : IToolCallLog
{
    private readonly List<ToolCallEntry> _entries = new();
    public void Record(ToolCallEntry entry) => _entries.Add(entry);
    public IReadOnlyList<ToolCallEntry> Recent(int take = 100)
        => _entries.AsEnumerable().Reverse().Take(take).ToList();
}
