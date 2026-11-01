using Microsoft.AspNetCore.Authorization;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Controllers.Api;

namespace PlaceContext.Host.Tests;

public sealed class ManagedWorkflowQueueContractTests
{
    [Fact]
    public void Management_api_exposes_durable_receipts_with_read_authorization()
    {
        var endpoint = typeof(JobsController).GetMethod(nameof(JobsController.GetWorkflowSubmission));
        Assert.NotNull(endpoint);
        Assert.Contains(endpoint!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), policy => policy.Policy == Permission.JobsView);
        var content = File.ReadAllText(Path.Combine(FindRoot(), "src/PlaceContext.Host/Controllers/Api/JobsController.cs"));
        Assert.Contains("AcceptedAtAction(nameof(GetWorkflowSubmission)", content);
        Assert.Contains("idempotencyKey", content);
        Assert.Contains("AcceptedAtAction(nameof(GetWorkflowSubmission)", content);
        Assert.Contains("replayOriginalRunId:", content);
        Assert.Contains("replayFromStepIndex:", content);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PlaceContext.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("PlaceContext root not found.");
    }
}
