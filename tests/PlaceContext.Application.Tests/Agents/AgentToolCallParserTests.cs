using PlaceContext.Application.Agents.Services;
using Xunit;

namespace PlaceContext.Application.Tests.Agents;

public class AgentToolCallParserTests
{
    [Fact]
    public void Parse_single_call()
    {
        var calls = AgentToolCallParser.Parse("Let me check. [[tool:list_tables|]]");

        var call = Assert.Single(calls);
        Assert.Equal("list_tables", call.Name);
        Assert.Equal("", call.Args);
    }

    [Fact]
    public void Parse_multiple_calls_in_order()
    {
        var calls = AgentToolCallParser.Parse(
            "[[tool:list_jobs|]] then [[tool:query_table|cashflow|2]] done");

        Assert.Equal(2, calls.Count);
        Assert.Equal("list_jobs", calls[0].Name);
        Assert.Equal("query_table", calls[1].Name);
        Assert.Equal("cashflow|2", calls[1].Args);
    }

    [Fact]
    public void Parse_args_containing_pipes_and_json()
    {
        var id = Guid.NewGuid();
        var calls = AgentToolCallParser.Parse($"[[tool:run_job_chain|{id}|{{\"a\":1}}]]");

        var call = Assert.Single(calls);
        Assert.Equal("run_job_chain", call.Name);
        Assert.Equal($"{id}|{{\"a\":1}}", call.Args);
    }

    [Fact]
    public void Parse_no_calls_returns_empty()
    {
        Assert.Empty(AgentToolCallParser.Parse("Just a plain answer."));
        Assert.Empty(AgentToolCallParser.Parse(""));
    }

    [Fact]
    public void Parse_malformed_markers_returns_empty()
    {
        Assert.Empty(AgentToolCallParser.Parse("[[tool:missing args bracket]]"));
        Assert.Empty(AgentToolCallParser.Parse("[[tool:|no_name]]"));
        Assert.Empty(AgentToolCallParser.Parse("[[tool:unterminated|args"));
    }

    [Fact]
    public void StripToolCalls_removes_markers()
    {
        Assert.Equal("Done.", AgentToolCallParser.StripToolCalls("[[tool:list_jobs|]] Done."));
        Assert.Equal("", AgentToolCallParser.StripToolCalls("[[tool:list_jobs|]]"));
    }
}
