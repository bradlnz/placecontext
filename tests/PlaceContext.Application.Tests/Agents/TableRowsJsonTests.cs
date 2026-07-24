using System.Text.Json;
using PlaceContext.Application.Agents.Services;
using Xunit;

namespace PlaceContext.Application.Tests.Agents;

public class TableRowsJsonTests
{
    [Fact]
    public void Convert_zips_columns_and_rows_into_objects()
    {
        var json = TableRowsJson.Convert(
            new[] { "id", "name" },
            new IReadOnlyList<string?>[] { new string?[] { "1", "alpha" }, new string?[] { "2", "beta" } });

        var docs = JsonSerializer.Deserialize<List<Dictionary<string, string?>>>(json);
        Assert.NotNull(docs);
        Assert.Equal(2, docs!.Count);
        Assert.Equal("1", docs[0]["id"]);
        Assert.Equal("alpha", docs[0]["name"]);
        Assert.Equal("beta", docs[1]["name"]);
    }

    [Fact]
    public void Convert_null_cells_become_json_null()
    {
        var json = TableRowsJson.Convert(
            new[] { "id", "name" },
            new IReadOnlyList<string?>[] { new string?[] { "1", null } });

        var docs = JsonSerializer.Deserialize<List<Dictionary<string, string?>>>(json);
        Assert.Null(docs![0]["name"]);
    }

    [Fact]
    public void Convert_truncates_at_max_rows_with_note()
    {
        var rows = Enumerable.Range(1, 10)
            .Select(i => (IReadOnlyList<string?>)new string?[] { i.ToString() })
            .ToList();

        var result = TableRowsJson.Convert(new[] { "id" }, rows, maxRows: 3);

        Assert.Contains("… (truncated: showing 3 of 10 rows)", result);
        var docs = JsonSerializer.Deserialize<List<Dictionary<string, string?>>>(
            result[..result.IndexOf("\n…", StringComparison.Ordinal)]);
        Assert.Equal(3, docs!.Count);
    }

    [Fact]
    public void Convert_truncates_at_max_chars_with_note()
    {
        var rows = Enumerable.Range(1, 100)
            .Select(i => (IReadOnlyList<string?>)new string?[] { i.ToString(), new string('x', 200) })
            .ToList();

        var result = TableRowsJson.Convert(new[] { "id", "blob" }, rows, maxRows: 100, maxChars: 1000);

        Assert.Contains("… (truncated: showing", result);
        Assert.Contains("of 100 rows)", result);
        // At least one row is always emitted, even over budget.
        Assert.Contains("\"id\":\"1\"", result);
    }

    [Fact]
    public void Convert_missing_cells_become_null()
    {
        var json = TableRowsJson.Convert(
            new[] { "a", "b" },
            new IReadOnlyList<string?>[] { new string?[] { "only-a" } });

        var docs = JsonSerializer.Deserialize<List<Dictionary<string, string?>>>(json);
        Assert.Equal("only-a", docs![0]["a"]);
        Assert.Null(docs[0]["b"]);
    }
}
