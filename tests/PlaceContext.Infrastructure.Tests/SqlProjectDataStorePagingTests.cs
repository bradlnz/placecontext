using PlaceContext.Infrastructure.ProjectData;

namespace PlaceContext.Infrastructure.Tests;

/// <summary>
/// Pure pagination + search-predicate math for the entity Records tab, split out of
/// <see cref="SqlProjectDataStore"/> as internal static helpers so it's testable without a
/// live Postgres connection.
/// </summary>
public class SqlProjectDataStorePagingTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public void ClampPage_never_goes_below_one(int input, int expected)
        => Assert.Equal(expected, SqlProjectDataStore.ClampPage(input));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(50, 50)]
    [InlineData(9999, SqlProjectDataStore.MaxPageSize)]
    public void ClampPageSize_stays_within_the_allowed_range(int input, int expected)
        => Assert.Equal(expected, SqlProjectDataStore.ClampPageSize(input));

    [Theory]
    [InlineData(1, 50, 0)]
    [InlineData(2, 50, 50)]
    [InlineData(3, 25, 50)]
    [InlineData(0, 50, 0)]   // page 0 clamps to page 1 → offset 0
    public void OffsetFor_computes_the_windowed_offset(int page, int pageSize, long expectedOffset)
        => Assert.Equal(expectedOffset, SqlProjectDataStore.OffsetFor(page, pageSize));

    [Fact]
    public void BuildSearchWhereClause_is_null_when_there_is_no_search_term()
    {
        Assert.Null(SqlProjectDataStore.BuildSearchWhereClause(new[] { "name", "email" }, null));
        Assert.Null(SqlProjectDataStore.BuildSearchWhereClause(new[] { "name", "email" }, ""));
        Assert.Null(SqlProjectDataStore.BuildSearchWhereClause(new[] { "name", "email" }, "   "));
    }

    [Fact]
    public void BuildSearchWhereClause_is_null_when_the_table_has_no_columns()
        => Assert.Null(SqlProjectDataStore.BuildSearchWhereClause(Array.Empty<string>(), "smith"));

    [Fact]
    public void BuildSearchWhereClause_ORs_every_column_cast_to_text_and_binds_the_term_as_a_parameter()
    {
        var clause = SqlProjectDataStore.BuildSearchWhereClause(new[] { "name", "email" }, "smith");

        Assert.NotNull(clause);
        Assert.Equal(" WHERE \"name\"::text ILIKE @search OR \"email\"::text ILIKE @search", clause);
        // The search term itself never appears in the generated SQL — it travels as a bound
        // parameter (see QueryTablePageAsync), which is what keeps this injection-safe.
        Assert.DoesNotContain("smith", clause);
    }

    [Fact]
    public void BuildSearchWhereClause_quotes_identifiers_so_a_reserved_or_mixed_case_column_still_works()
    {
        var clause = SqlProjectDataStore.BuildSearchWhereClause(new[] { "Order", "select" }, "x");

        Assert.Equal(" WHERE \"Order\"::text ILIKE @search OR \"select\"::text ILIKE @search", clause);
    }

    [Fact]
    public void BuildOrderByClause_falls_back_to_first_column_when_no_sort_is_given()
    {
        Assert.Equal(" ORDER BY 1", SqlProjectDataStore.BuildOrderByClause(new[] { "name" }, null, false));
        Assert.Equal(" ORDER BY 1", SqlProjectDataStore.BuildOrderByClause(new[] { "name" }, "", false));
        Assert.Equal(" ORDER BY 1", SqlProjectDataStore.BuildOrderByClause(new[] { "name" }, "   ", true));
    }

    [Fact]
    public void BuildOrderByClause_quotes_a_valid_sort_column_and_keeps_first_column_as_tiebreaker()
    {
        Assert.Equal(" ORDER BY \"amount\" ASC NULLS LAST, 1",
            SqlProjectDataStore.BuildOrderByClause(new[] { "id", "amount" }, "amount", false));
        Assert.Equal(" ORDER BY \"amount\" DESC NULLS LAST, 1",
            SqlProjectDataStore.BuildOrderByClause(new[] { "id", "amount" }, "amount", true));
    }

    [Fact]
    public void BuildOrderByClause_rejects_columns_that_do_not_exist_so_nothing_is_interpolated()
    {
        // Anything not in the table's actual column list — including injection attempts —
        // falls back to the default ordering and never reaches the SQL text.
        var evil = "amount; DROP TABLE users--";
        var clause = SqlProjectDataStore.BuildOrderByClause(new[] { "id", "amount" }, evil, false);

        Assert.Equal(" ORDER BY 1", clause);
        Assert.DoesNotContain("DROP", clause);
        // Case must match exactly (Postgres identifiers are case-sensitive when quoted).
        Assert.Equal(" ORDER BY 1", SqlProjectDataStore.BuildOrderByClause(new[] { "id", "amount" }, "AMOUNT", false));
    }
}
