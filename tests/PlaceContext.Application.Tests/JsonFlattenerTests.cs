using System.Text.Json;
using PlaceContext.Application.Features;
using Xunit;

namespace PlaceContext.Application.Tests;

public class JsonFlattenerTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    // ── Flatten ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Nested_objects_recurse_into_dot_paths_in_document_order()
    {
        var leaves = JsonFlattener.Flatten(Parse("""{"a":"x","b":{"c":1,"d":{"e":true}}}"""));
        Assert.Equal(new[] { "a", "b.c", "b.d.e" }, leaves.Select(l => l.Path));
        Assert.Equal("x", leaves[0].Value.GetString());
        Assert.Equal(1, leaves[1].Value.GetInt32());
        Assert.True(leaves[2].Value.GetBoolean());
    }

    [Fact]
    public void Arrays_and_empty_objects_are_leaves_not_recursed()
    {
        var leaves = JsonFlattener.Flatten(Parse("""{"tags":["a","b"],"empty":{},"objs":[{"x":1}]}"""));
        Assert.Equal(new[] { "tags", "empty", "objs" }, leaves.Select(l => l.Path));
        Assert.Equal(JsonValueKind.Array, leaves[0].Value.ValueKind);
        Assert.Equal(JsonValueKind.Object, leaves[1].Value.ValueKind); // the empty object
        Assert.Equal(JsonValueKind.Array, leaves[2].Value.ValueKind);  // arrays of objects stay whole
    }

    [Fact]
    public void A_non_object_root_is_one_leaf_with_an_empty_path()
    {
        var leaf = Assert.Single(JsonFlattener.Flatten(Parse("""["a","b"]""")));
        Assert.Equal("", leaf.Path);
        var scalar = Assert.Single(JsonFlattener.Flatten(Parse("42")));
        Assert.Equal("", scalar.Path);
    }

    // ── ColumnName ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("meta", "region", "meta_region")]
    [InlineData("meta", "a.b.c", "meta_a_b_c")]
    [InlineData("meta", "Region Name", "meta_region_name")]   // case + spaces
    [InlineData("meta", "a--b!!c", "meta_a_b_c")]             // invalid runs collapse to one _
    [InlineData("meta", "123abc", "meta_123abc")]
    [InlineData("meta", "", "meta")]                          // empty path = the prefix itself
    public void Column_names_sanitize_to_postgres_identifiers(string prefix, string path, string expected)
        => Assert.Equal(expected, JsonFlattener.ColumnName(prefix, path));

    [Fact]
    public void Column_names_are_capped_at_63_chars()
    {
        var name = JsonFlattener.ColumnName("meta", new string('x', 100));
        Assert.Equal(63, name.Length);
    }

    // ── Type inference ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("true", "boolean")]
    [InlineData("42", "numeric")]
    [InlineData("4.2", "numeric")]
    [InlineData("\"s\"", "text")]
    [InlineData("[1,2]", "jsonb")]
    [InlineData("{}", "jsonb")]
    public void Single_kinds_infer_their_column_type(string json, string expected)
    {
        var merged = JsonFlattener.MergeKind(null, Parse(json));
        Assert.Equal(expected, JsonFlattener.InferredType(merged));
    }

    [Fact]
    public void Mixed_kinds_degrade_to_text_and_nulls_dont_move_inference()
    {
        var kind = JsonFlattener.MergeKind(null, Parse("42"));
        kind = JsonFlattener.MergeKind(kind, Parse("\"later\""));
        Assert.Equal("text", JsonFlattener.InferredType(kind));

        kind = JsonFlattener.MergeKind(null, Parse("null"));
        kind = JsonFlattener.MergeKind(kind, Parse("true"));
        Assert.Equal("boolean", JsonFlattener.InferredType(kind));

        Assert.Equal("text", JsonFlattener.InferredType(null)); // only nulls ever seen
    }

    // ── ValueText ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("\"s\"", "s")]
    [InlineData("42", "42")]
    [InlineData("true", "true")]
    [InlineData("null", null)]
    [InlineData("[1,2]", "[1,2]")]
    [InlineData("{}", "{}")]
    public void Values_travel_as_text(string json, string? expected)
        => Assert.Equal(expected, JsonFlattener.ValueText(Parse(json)));
}
