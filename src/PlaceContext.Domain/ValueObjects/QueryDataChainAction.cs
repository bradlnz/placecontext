namespace PlaceContext.Domain.ValueObjects;

public sealed class QueryDataChainAction : ChainAction
{
    public const string ActionType = "queryData";
    public QueryDataChainAction(string sql, string outputField = "query_data")
    {
        if (string.IsNullOrWhiteSpace(sql) || sql.Length > 32768)
            throw new ArgumentException("Enter a read-only SQL query of up to 32,768 characters.");
        if (!ScoringGateChainAction.ValidField(outputField)) throw new ArgumentException("Choose an output field of up to 80 letters, digits or underscores.");
        Sql = sql.Trim(); OutputField = outputField;
    }
    public override string Type => ActionType;
    public override string DisplayName => "Query data";
    public string Sql { get; }
    public string OutputField { get; }
}
