using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;
using Xunit;

namespace PlaceContext.Domain.Tests;

public class ProjectContextTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Append_separates_sections_with_blank_line()
    {
        var ctx = ProjectContext.Start(ProjectId.New(), T0);
        Assert.True(ctx.IsEmpty);

        ctx.Append("# Goals", T0);
        ctx.Append("# Conventions", T0.AddMinutes(1));

        Assert.Equal("# Goals\n\n# Conventions", ctx.Markdown);
        Assert.False(ctx.IsEmpty);
        Assert.Equal(T0.AddMinutes(1), ctx.UpdatedAt);
    }

    [Fact]
    public void Append_rejects_blank_section()
        => Assert.Throws<ArgumentException>(() => ProjectContext.Start(ProjectId.New(), T0).Append("  ", T0));
}
