using PlaceContext.Agents.Domain.Entities;

namespace PlaceContext.Agents.Tests;

public sealed class AgentProfileTests
{
    [Fact]
    public void Create_captures_autonomy_guardrails_and_starts_at_version_one()
    {
        var now = DateTimeOffset.Parse("2026-08-08T00:00:00Z");
        var profile = AgentProfile.Create(
            "Planning researcher", "Researcher", "Finds planning evidence", "Investigate and report",
            "Use authoritative sources.", "openai", "gpt-5", "high", ["search", "jobs"], [], [],
            ["planning"], ["jobs.run"], true, true, true, 100_000, 20m, 90, 2, 3, 1, now);

        Assert.Equal(1, profile.Version);
        Assert.True(profile.RequireExternalActionApproval);
        Assert.Equal(["search", "jobs"], profile.AllowedTools);
    }

    [Fact]
    public void Update_increments_version_so_assignments_can_pin_configuration()
    {
        var now = DateTimeOffset.Parse("2026-08-08T00:00:00Z");
        var profile = AgentProfile.Create("Researcher", "Researcher", "", "", "Research.",
            "openai", "gpt-5", "high", [], [], [], [], [], true, true, true,
            100_000, 20m, 90, 2, 3, 1, now);

        profile.Update("Research lead", "Lead", "", "", "Research and review.",
            "openai", "gpt-5", "high", [], [], [], [], [], true, true, true,
            120_000, 25m, 120, 3, 4, 2, now.AddMinutes(1));

        Assert.Equal(2, profile.Version);
        Assert.Equal("Research lead", profile.Name);
    }
}
