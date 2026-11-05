using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Infrastructure.Tests;

public sealed class ModelOutcomeReviewerTests
{
    [Fact]
    public async Task Scores_use_one_typed_batch_with_descriptive_levels_and_complete_evidence()
    {
        using var api = new ModelReviewTestApi(async (request, ct) =>
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            Assert.Equal("complete evidence", body["state"]!.GetValue<string>());
            Assert.Equal("score", body["questions"]!["security"]!["type"]!.GetValue<string>());
            Assert.Equal("No vulnerabilities found by executed checks", body["questions"]!["security"]!["criteria"]![1]!.GetValue<string>());
            Assert.Null(body["questions"]!["security"]!["weight"]);
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"model":"test","answers":{"security":{"type":"score","score":1,"confidence":0.95,"probabilities":{"0":0,"1":1}}}}""") };
        });
        var reviewer = new ModelOutcomeReviewer(api, Options.Create(new ModelReviewOptions { ApiKey = "test-key" }));
        var response = await reviewer.ScoreAsync([new("security", "Are checks passing?", ["Missing checks", "No vulnerabilities found by executed checks"])], "complete evidence", default);
        Assert.Equal(.95, response["answers"]!["security"]!["confidence"]!.GetValue<double>()); Assert.Equal(1, api.Requests);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(529)]
    public async Task Throttling_returns_a_workflow_retry_signal_with_provider_delay(int status)
    {
        using var api = new ModelReviewTestApi((_, _) =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("private response") };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(75));
            return Task.FromResult(response);
        });
        var reviewer = new ModelOutcomeReviewer(api, Options.Create(new ModelReviewOptions { ApiKey = "test-key" }));
        var error = await Assert.ThrowsAsync<PlaceContext.Application.Build.OutcomeReviewThrottledException>(
            () => reviewer.EvaluateAsync("Correct?", "{}", default));
        Assert.Equal(TimeSpan.FromSeconds(75), error.RetryAfter);
        Assert.Contains(status.ToString(), error.Message);
        Assert.DoesNotContain("private response", error.Message);
    }

    [Fact]
    public async Task Sends_typed_question_and_complete_state_and_preserves_probability_and_model()
    {
        using var api = new ModelReviewTestApi(async (request, ct) =>
        {
            Assert.Equal("https://api.typesafe.ai/v1/systemone", request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("Bearer test-key", request.Headers.Authorization!.ToString());
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            Assert.Equal("jev-1.14.0", body["model"]!.GetValue<string>());
            Assert.Equal("{\"total\":42}", body["state"]!.GetValue<string>());
            Assert.Equal("noul", body["questions"]!["verification"]!["type"]!.GetValue<string>());
            Assert.Equal("Is the total correct?", body["questions"]!["verification"]!["instructions"]!.GetValue<string>());
            Assert.DoesNotContain("test-key", body.ToJsonString());
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"model":"jev-1.14.0","answers":{"verification":{"type":"noul","noul":0.73}}}""")
            };
        });
        var reviewer = new ModelOutcomeReviewer(api, Options.Create(new ModelReviewOptions { ApiKey = "test-key" }));
        var answer = await reviewer.EvaluateAsync("Is the total correct?", "{\"total\":42}", default);
        Assert.Equal(0.73, answer["noul"]!.GetValue<double>());
        Assert.Equal("jev-1.14.0", answer["model"]!.GetValue<string>());
        Assert.Equal("Is the total correct?", answer["question"]!.GetValue<string>());
        Assert.False(answer.ContainsKey("approved"));
        Assert.Equal(1, api.Requests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"model\":\"jev\",\"answers\":{\"verification\":{\"type\":\"noul\",\"noul\":1.1}}}")]
    [InlineData("{\"model\":\"jev\",\"answers\":{\"verification\":{\"type\":\"noul\",\"noul\":-0.1}}}")]
    [InlineData("{\"model\":\"jev\",\"answers\":{\"verification\":{\"type\":\"noul\",\"noul\":\"0.9\"}}}")]
    [InlineData("{\"model\":\"jev\",\"answers\":{\"verification\":{\"type\":\"choice\",\"noul\":0.9}}}")]
    public async Task Invalid_answers_are_errors(string body)
    {
        using var api = new ModelReviewTestApi((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
        var reviewer = new ModelOutcomeReviewer(api, Options.Create(new ModelReviewOptions { ApiKey = "test-key" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reviewer.EvaluateAsync("Correct?", "{}", default));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(302)]
    public async Task Http_errors_do_not_expose_provider_response_bodies(int status)
    {
        using var api = new ModelReviewTestApi((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("sensitive-provider-response") }));
        var reviewer = new ModelOutcomeReviewer(api, Options.Create(new ModelReviewOptions { ApiKey = "test-key" }));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => reviewer.EvaluateAsync("Correct?", "{}", default));
        Assert.Contains(status.ToString(), error.Message);
        Assert.DoesNotContain("sensitive-provider-response", error.Message);
        Assert.Equal(1, api.Requests);
    }

    [Fact]
    public async Task Complete_json_text_is_sent_once_without_truncation()
    {
        var state = "{\"records\":[{\"text\":\"" + new string('x', 60_000) + "\"},{\"id\":2}]}";
        using var api = new ModelReviewTestApi(async (request, ct) =>
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            Assert.Equal(state, body["state"]!.GetValue<string>());
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"model":"jev-1.14.0","answers":{"verification":{"type":"noul","noul":0.95}}}""")
            };
        });
        var reviewer = new ModelOutcomeReviewer(api, Options.Create(new ModelReviewOptions { ApiKey = "test-key" }));
        Assert.Equal(0.95, (await reviewer.EvaluateAsync("Every record valid?", state, default))["noul"]!.GetValue<double>());
        Assert.Equal(1, api.Requests);
    }

    [Fact]
    public async Task Missing_credentials_and_oversized_state_are_rejected_before_sending()
    {
        using var api = new ModelReviewTestApi((_, _) => throw new InvalidOperationException("Must not send"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ModelOutcomeReviewer(api, Options.Create(new ModelReviewOptions())).EvaluateAsync("Correct?", "{}", default));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new ModelOutcomeReviewer(api, Options.Create(new ModelReviewOptions { ApiKey = "secret\ninvalid" })).EvaluateAsync("Correct?", "{}", default));
        Assert.DoesNotContain("secret", error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ModelOutcomeReviewer(api, Options.Create(new ModelReviewOptions { ApiKey = "test-key" })).EvaluateAsync("Correct?", new string('x', 65_537), default));
        Assert.Equal(0, api.Requests);
    }

    [Fact]
    public async Task Cancellation_reaches_the_http_request()
    {
        using var cancellation = new CancellationTokenSource();
        using var api = new ModelReviewTestApi(async (_, ct) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return new(HttpStatusCode.OK);
        });
        var reviewer = new ModelOutcomeReviewer(api, Options.Create(new ModelReviewOptions { ApiKey = "test-key" }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reviewer.EvaluateAsync("Correct?", "{}", cancellation.Token));
    }
}
