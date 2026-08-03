using System.Text.Json;
using PlaceContext.Host.Controllers;

namespace PlaceContext.Host.Tests;

public sealed class CrmSiteQueueSubmissionTests
{
    [Fact]
    public void Uses_submitted_address_and_initial_queue_state()
    {
        var request = Request(address: " 10 Queen Street, Brisbane City QLD 4000 ");

        var queued = CrmSiteQueueSubmission.From(request);

        Assert.NotNull(queued);
        Assert.Equal("queue_sites", CrmSiteQueueSubmission.TableName);
        Assert.Equal(queued.Id.ToString(), queued.Values["id"]);
        Assert.Equal("10 Queen Street, Brisbane City QLD 4000", queued.Values["address"]);
        Assert.Equal("NOT_RUN", queued.Values["status"]);
        Assert.Equal("0", queued.Values["retry_attempt"]);
        Assert.Null(queued.Values["error"]);
        Assert.Null(queued.Values["last_run_at"]);
        Assert.DoesNotContain("created_at", queued.Values.Keys);
    }

    [Fact]
    public void Falls_back_to_report_site_address_in_metadata()
    {
        using var document = JsonDocument.Parse(
            """{"site":{"address":"20 George Street, Brisbane QLD 4000"}}""");
        var metadata = document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());

        var queued = CrmSiteQueueSubmission.From(Request(metadata: metadata));

        Assert.NotNull(queued);
        Assert.Equal("20 George Street, Brisbane QLD 4000", queued.Values["address"]);
    }

    [Fact]
    public void Queues_canonical_report_site_address()
    {
        using var document = JsonDocument.Parse(
            """{"event":"feasibility_report_ordered","site":{"address":"20 Balfour Street, Darra QLD 4076"}}""");

        var queued = CrmSiteQueueSubmission.From(document.RootElement);

        Assert.NotNull(queued);
        Assert.Equal("20 Balfour Street, Darra QLD 4076", queued.Values["address"]);
    }

    [Fact]
    public void Passes_canonical_report_order_to_job_chain_unchanged()
    {
        const string payload = """{"event":"feasibility_report_ordered","report_uuid":"c636bba4-cb2c-4337-85f8-0af9b9391cd1","product_tier":"premium","site":{"address":"20 Balfour Street, Darra QLD 4076","street":"20 Balfour Street","suburb":"Darra","state":"QLD","postcode":"4076","lat":"-27.5720207","lon":"152.9555556","place_id":"N6215137771"}}""";
        using var document = JsonDocument.Parse(payload);

        Assert.Equal(payload, CrmIngestionPayload.JobChainInput(document.RootElement));
    }

    [Fact]
    public void Unwraps_legacy_report_metadata_for_job_chain()
    {
        using var document = JsonDocument.Parse(
            """{"name":"Ada","email":"ada@example.com","metadata":{"event":"feasibility_report_ordered","report_uuid":"report-1","product_tier":"premium","site":{"address":"20 Balfour Street, Darra QLD 4076"}}}""");

        var chainInput = CrmIngestionPayload.JobChainInput(document.RootElement);
        using var normalized = JsonDocument.Parse(chainInput);

        Assert.Equal("feasibility_report_ordered", normalized.RootElement.GetProperty("event").GetString());
        Assert.Equal("report-1", normalized.RootElement.GetProperty("report_uuid").GetString());
        Assert.Equal("20 Balfour Street, Darra QLD 4076",
            normalized.RootElement.GetProperty("site").GetProperty("address").GetString());
        Assert.False(normalized.RootElement.TryGetProperty("name", out _));
    }

    [Fact]
    public void Leaves_non_report_automation_payload_unchanged()
    {
        const string payload = """{"event":"contact_submitted","value":42}""";
        using var document = JsonDocument.Parse(payload);

        Assert.Equal(payload, CrmIngestionPayload.JobChainInput(document.RootElement));
    }

    [Fact]
    public void Does_not_queue_a_contact_only_submission()
    {
        Assert.Null(CrmSiteQueueSubmission.From(Request()));
    }

    private static LeadIngestionRequest Request(
        string? address = null,
        Dictionary<string, JsonElement>? metadata = null)
        => new(
            Name: "Ada Lovelace",
            Email: "ada@example.com",
            Phone: null,
            Company: null,
            Message: null,
            Source: "ossen-reports",
            Address: address,
            Metadata: metadata,
            Website: null);
}
