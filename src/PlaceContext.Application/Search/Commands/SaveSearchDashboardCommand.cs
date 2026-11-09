using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
namespace PlaceContext.Application.Features;

public sealed record SaveSearchDashboardCommand(
    Guid ProjectId,
    string Name,
    string IndexPattern,
    string? QueryText,
    string BucketField,
    string BucketType,
    string ChartType,
    string MetricType,
    string? MetricField,
    string? DateInterval,
    string ChartSpecJson,
    Guid? DashboardId = null) : ICommand<SearchDashboardView>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}
