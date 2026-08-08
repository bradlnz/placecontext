using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Host.Controllers.Api.Records;

namespace PlaceContext.Host.Controllers.Api.Mappers;

internal static class JobTestPageMapper
{
    public static JobTestBlockResponse Map(JobTestCaseView test)
    {
        var runtime = test.RuntimeId ?? JobTestRuntimeCatalog.Default;
        var methods = test.MethodResults is { Count: > 0 }
            ? test.MethodResults
            : JobTestFramework.Discover(runtime, test.CodeFiles);
        return new JobTestBlockResponse(
            test.Id, test.ProjectId, test.JobId, test.JobName, test.Name, test.InputPayload,
            test.AssertionType.ToString(), test.ExpectedValue, test.Enabled, test.LastStatus,
            test.LastMessage, test.LastActualOutput, test.LastDurationMs, runtime,
            JobTestFramework.Label(runtime), test.Entrypoint,
            test.CodeFiles.Select(file => new JobTestCodeFileResponse(file.Path, file.Content)).ToList(),
            methods.Select(method => new JobTestMethodResponse(
                method.Name, method.Status, method.DurationMs, method.Message)).ToList());
    }
}
