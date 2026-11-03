using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Jobs;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class DeleteJobTestCaseHandler : ICommandHandler<DeleteJobTestCaseCommand, bool>
{
    private readonly IJobTestStore _tests;
    public DeleteJobTestCaseHandler(IJobTestStore tests) => _tests = tests;

    public Task<bool> HandleAsync(DeleteJobTestCaseCommand command, CancellationToken ct = default)
        => _tests.DeleteAsync(command.TestId, ct);
}
