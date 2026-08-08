using System.Text.Json.Nodes;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class DeleteSqlChartHandler : ICommandHandler<DeleteSqlChartCommand, bool>
{
    private readonly IProjectChartRepository _charts;
    private readonly IDataUnitOfWork _uow;

    public DeleteSqlChartHandler(IProjectChartRepository charts, IDataUnitOfWork uow)
    {
        _charts = charts;
        _uow = uow;
    }

    public async Task<bool> HandleAsync(DeleteSqlChartCommand command, CancellationToken ct = default)
    {
        await _charts.DeleteAsync(command.ProjectId, SaveSqlChartHandler.Prefix + command.Name.Trim(), ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}
