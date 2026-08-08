using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Artifacts;

public static class DependencyInjection
{
    public static IServiceCollection AddArtifactsApi(this IServiceCollection services)
    {
        services.AddScoped<IQueryHandler<ListRecentArtifactsQuery, IReadOnlyList<ArtifactFileView>>, ListRecentArtifactsHandler>();
        services.AddScoped<IQueryHandler<ListProjectArtifactsQuery, IReadOnlyList<ArtifactFileView>>, ListProjectArtifactsHandler>();
        services.AddScoped<IQueryHandler<ListRunArtifactsQuery, IReadOnlyList<RunArtifactLinkView>>, ListRunArtifactsHandler>();
        return services;
    }

    public static IServiceCollection AddArtifactsModule(this IServiceCollection services)
    {
        services.AddScoped<ICommandHandler<DeleteArtifactCommand, bool>, DeleteArtifactHandler>();
        services.AddScoped<ICommandHandler<DeleteArtifactsCommand, int>, DeleteArtifactsHandler>();
        services.AddScoped<ICommandHandler<CreateArtifactShareCommand, ArtifactShareCreated>, CreateArtifactShareHandler>();
        services.AddScoped<ICommandHandler<RevokeArtifactShareCommand, bool>, RevokeArtifactShareHandler>();
        services.AddScoped<IQueryHandler<GetArtifactShareStatusQuery, ArtifactShareStatus?>, GetArtifactShareStatusHandler>();
        services.AddScoped<IQueryHandler<ListRecentArtifactsQuery, IReadOnlyList<ArtifactFileView>>, ListRecentArtifactsHandler>();
        services.AddScoped<IQueryHandler<ListProjectArtifactsQuery, IReadOnlyList<ArtifactFileView>>, ListProjectArtifactsHandler>();
        services.AddScoped<IQueryHandler<ListRunArtifactsQuery, IReadOnlyList<RunArtifactLinkView>>, ListRunArtifactsHandler>();
        services.AddScoped<IQueryHandler<ListJobRunArtifactsQuery, IReadOnlyList<RunArtifactLinkView>>, ListJobRunArtifactsHandler>();
        services.AddScoped<OcrResultStorageService>();
        services.AddScoped<IQueryHandler<ListPendingOcrQuery, IReadOnlyList<PendingOcrArtifactView>>, ListPendingOcrHandler>();
        services.AddScoped<ICommandHandler<CompleteOcrCommand, bool>, CompleteOcrHandler>();
        return services;
    }
}
