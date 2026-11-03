using Microsoft.AspNetCore.Authorization;
using PlaceContext.Host.Auth;
using PlaceContext.Host.Controllers;

namespace PlaceContext.Host.Tests;

public sealed class ManagedJobSubmissionContractTests
{
    [Fact]
    public void Core_job_submissions_have_read_scoped_receipts_and_durable_202_response()
    {
        var endpoint = typeof(CoreApiJobsController).GetMethod(nameof(CoreApiJobsController.GetJobSubmission));
        Assert.NotNull(endpoint);
        Assert.Contains(endpoint!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), rule => rule.Policy == CoreApiScopes.JobsRead);
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "PlaceContext.slnx")))
        {
            d = d.Parent;
        }
        Assert.NotNull(d);
        var source = File.ReadAllText(Path.Combine(d!.FullName,
            "src/PlaceContext.Host/Controllers/CoreApiJobsController.cs"));
        Assert.Contains("AcceptedAtAction(nameof(GetJobSubmission)", source);
        Assert.Contains("_submissions.EnqueueAsync", source);
    }
}
