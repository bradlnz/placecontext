using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Data.Controllers;

namespace PlaceContext.Data.Tests;

public sealed class EntityControllersTests
{
    [Fact]
    public void Controllers_preserve_routes_and_authorization()
    {
        var entities = typeof(EntitiesController);
        Assert.Equal("api/v1", entities.GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.Contains(entities.GetCustomAttributes<AuthorizeAttribute>(), attribute =>
            attribute.Policy is null);

        AssertAction<EntitiesController>(nameof(EntitiesController.ListEntities), "entities", Permission.DataRead);
        AssertAction<EntitiesController>(nameof(EntitiesController.ListRecords), "{entityName}", Permission.DataRead);
        AssertAction<EntitiesController>(nameof(EntitiesController.RunJob), "{entityName}/jobs/{jobId:guid}/run", Permission.JobsRun);
        AssertAction<EntitiesController>(nameof(EntitiesController.RunJobByName), "{jobName}", Permission.JobsRun);
        AssertAction<EntitiesController>(nameof(EntitiesController.GetByKey), "{entityName}/{key}", Permission.DataRead);

        var browse = typeof(EntityBrowsePageController);
        Assert.Equal("api/v1/projects/{projectId:guid}/entity-page/{entityName}", browse.GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.Equal(Permission.DataRead, browse.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        AssertAction<EntityBrowsePageController>(nameof(EntityBrowsePageController.Create), "records/create", Permission.DataWrite);
        AssertAction<EntityBrowsePageController>(nameof(EntityBrowsePageController.Update), "records/update", Permission.DataWrite);
        AssertAction<EntityBrowsePageController>(nameof(EntityBrowsePageController.Delete), "records/delete", Permission.DataWrite);
    }

    [Theory]
    [InlineData("Sites", "sites_table", "Sites", true)]
    [InlineData("Sites", "sites_table", "sites", true)]
    [InlineData("Sites", "sites_table", "sites_table", true)]
    [InlineData("Suburb Markets", "suburb_markets", "suburb-markets", true)]
    [InlineData("Sites", "sites_table", "listings", false)]
    public void Entity_name_matching_preserves_name_table_and_slug(
        string name, string table, string request, bool expected)
    {
        var entity = new DataEntityView(
            Guid.NewGuid(), Guid.NewGuid(), name, table, "id", [], [], DateTimeOffset.UtcNow);
        Assert.Equal(expected, EntitiesController.EntityNameMatches(entity, request));
    }

    [Fact]
    public void Data_api_registers_handlers_required_by_entity_controllers()
    {
        var services = new ServiceCollection();
        services.AddDataApi();

        AssertRegistered<IQueryHandler<ListDataEntitiesQuery, IReadOnlyList<DataEntityView>>>(services);
        AssertRegistered<IQueryHandler<QueryProjectTablePageQuery, ProjectTablePageResult>>(services);
        AssertRegistered<IQueryHandler<ListProjectTableColumnsQuery, IReadOnlyList<ProjectColumnInfo>>>(services);
        AssertRegistered<ICommandHandler<CreateEntityRecordCommand, CreateEntityRecordResult>>(services);
        AssertRegistered<ICommandHandler<UpdateEntityRecordCommand, int>>(services);
        AssertRegistered<ICommandHandler<DeleteEntityRecordCommand, int>>(services);
        AssertRegistered<IQueryHandler<RelatedRecordLinksForRowQuery, IReadOnlyList<RecordLink>>>(services);
    }

    private static void AssertAction<TController>(string name, string template, string policy)
    {
        var method = typeof(TController).GetMethod(name)!;
        var route = method.GetCustomAttributes<HttpMethodAttribute>().Single();
        Assert.Equal(template, route.Template);
        Assert.Equal(policy, method.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
    }

    private static void AssertRegistered<TService>(IServiceCollection services) =>
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(TService));
}
