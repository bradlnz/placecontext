// PlaceContext test job — .NET runtime smoke test (.NET 10 file-based app).
// Sandbox contract: shard payload JSON on stdin, result JSON on stdout, logs on stderr.
// NOTE: file-based apps disable reflection-based System.Text.Json by default —
// use a source-generated JsonSerializerContext (below) or add:
//   #:property JsonSerializerIsReflectionEnabledByDefault=true
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

var raw = Console.In.ReadToEnd();
var input = string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw);

Console.Error.WriteLine("hello from dotnet");

var result = new JobResult("dotnet", true, input);
Console.Out.Write(JsonSerializer.Serialize(result, JobJsonContext.Default.JobResult));

internal sealed record JobResult(string Runtime, bool Ok, JsonNode? Input);

[JsonSerializable(typeof(JobResult))]
internal partial class JobJsonContext : JsonSerializerContext;
