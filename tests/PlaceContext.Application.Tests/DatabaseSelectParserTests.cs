using PlaceContext.Application.DatabaseInspection;

namespace PlaceContext.Application.Tests;

public sealed class DatabaseSelectParserTests
{
    [Fact]
    public void Select_preserves_quoted_names_and_literal_values_without_treating_them_as_sql()
    {
        var query = DatabaseSelectParser.Parse("SELECT \"DisplayName\", \"Role\" FROM public.users WHERE \"DisplayName\" = 'O''Brien; DROP TABLE users' AND \"DeletedAt\" IS NULL ORDER BY \"DisplayName\" DESC LIMIT 20 OFFSET 40;");
        Assert.Equal("public.users", query.Table);
        Assert.Equal(new[] { "DisplayName", "Role" }, query.Columns);
        Assert.Equal("O'Brien; DROP TABLE users", query.Filters[0].Value);
        Assert.Equal("IS NULL", query.Filters[1].Operator);
        Assert.True(Assert.Single(query.Order).Descending);
        Assert.Equal(20, query.Limit);
        Assert.Equal(40, query.Offset);
    }

    [Fact]
    public void Count_and_typed_comparisons_are_supported()
    {
        var query = DatabaseSelectParser.Parse("select count(*) from \"public\".\"users\" where \"Enabled\" = true AND \"UsedRuns\" >= 2");
        Assert.True(query.Count);
        Assert.Equal("public.users", query.Table);
        Assert.Equal("true", query.Filters[0].Value);
        Assert.Equal("2", query.Filters[1].Value);
        Assert.Equal(100, query.Limit);
    }

    [Theory]
    [InlineData("DELETE FROM users")]
    [InlineData("SELECT * FROM users; SELECT * FROM secrets")]
    [InlineData("SELECT pg_read_file('/etc/passwd') FROM users")]
    [InlineData("SELECT * FROM users JOIN secrets ON true")]
    [InlineData("SELECT * FROM users WHERE \"TenantId\" = 'x' OR true")]
    [InlineData("SELECT * FROM users WHERE \"Name\" = (SELECT secret FROM secrets)")]
    [InlineData("SELECT * FROM users -- bypass")]
    [InlineData("SELECT * FROM users LIMIT -1")]
    [InlineData("SELECT * FROM users LIMIT 501")]
    [InlineData("SELECT * FROM users LIMIT 0")]
    [InlineData("SELECT * FROM users OFFSET 1000001")]
    [InlineData("SELECT * FROM users WHERE \"Name\" = E'escape'")]
    [InlineData("SELECT * FROM users FOR UPDATE")]
    public void Unsupported_or_unbounded_sql_is_rejected(string sql) =>
        Assert.Throws<ArgumentException>(() => DatabaseSelectParser.Parse(sql));
}
