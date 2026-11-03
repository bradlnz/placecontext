using System.Text.Json.Nodes;
using PlaceContext.Application.ProjectData;

namespace PlaceContext.Application.Features;

public static class SqlChartDefinition
{
    public static string Build(string name, string sql, string type, ProjectQueryResult result)
    {
        var spec = ChartSpec.FromSample(name, result);
        if (result.Rows.Count == 0)
        {
            spec = new ChartSpec(type, name, [], []);
        }
        if (spec is null)
        {
            throw new InvalidOperationException("Return a label column and a numeric value to chart.");
        }
        var node = JsonNode.Parse(spec.ToJson())!;
        node["type"] = type;
        node["sql"] = sql;
        return node.ToJsonString();
    }
}
