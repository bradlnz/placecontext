using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using PlaceContext.Application.DataSources;

namespace PlaceContext.Infrastructure.DataSources;

internal sealed class LakeSourceConnector(HttpClient client)
{
    public static SourceDataset Dataset { get; } = new("files", "documents", "id", ["id", "path", "content"]);

    public async Task<IReadOnlyList<SourceRecord>> ReadAsync(string provider, string settings, bool discover, CancellationToken ct)
    {
        var values = SourceSettings.Parse(provider, settings);
        var rows = new List<SourceRecord>();
        var totalChars = 0;
        async Task Add(string path, Stream stream)
        {
            if (!new[] { ".txt", ".md", ".csv", ".json", ".jsonl", ".ndjson" }.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Choose a folder containing only TXT, Markdown, CSV, JSON or JSONL files.");
            }
            if (discover)
            {
                return;
            }
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
            var buffer = new char[DataSourceRules.MaxRecordChars + 1];
            var count = await reader.ReadBlockAsync(buffer, ct);
            var json = JsonSerializer.Serialize(new { id = path, path, content = new string(buffer, 0, count) });
            totalChars += json.Length;
            if (count > DataSourceRules.MaxRecordChars || json.Length > DataSourceRules.MaxRecordChars || totalChars > 8_000_000 || rows.Count >= DataSourceRules.MaxRecords)
            {
                throw new InvalidOperationException("Lake files exceed the source limits. Existing data was retained.");
            }
            rows.Add(new(path, json));
        }
        if (provider == "s3")
        {
            var config = new AmazonS3Config { RegionEndpoint = RegionEndpoint.GetBySystemName(values["region"]), Timeout = TimeSpan.FromSeconds(30) };
            if (!string.IsNullOrWhiteSpace(values.GetValueOrDefault("endpoint")))
            {
                config.ServiceURL = values["endpoint"];
                config.ForcePathStyle = true;
            }
            using var s3 = new AmazonS3Client(values["username"], values["password"], config);
            var listing = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = values["bucket"], Prefix = values.GetValueOrDefault("prefix", ""), MaxKeys = DataSourceRules.MaxRecords + 1 }, ct);
            if (listing.IsTruncated || listing.S3Objects.Count > DataSourceRules.MaxRecords)
            {
                throw new InvalidOperationException("Choose a smaller folder (up to 1,000 files).");
            }
            foreach (var file in listing.S3Objects.Where(file => !file.Key.EndsWith('/')))
            {
                if (discover)
                {
                    await Add(file.Key, Stream.Null);
                    continue;
                }
                using var response = await s3.GetObjectAsync(values["bucket"], file.Key, ct);
                await Add(file.Key, response.ResponseStream);
            }
        }
        if (provider != "s3")
        {
            using var auth = await client.PostAsync($"https://login.microsoftonline.com/{values["tenant"]}/oauth2/v2.0/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = values["username"], ["client_secret"] = values["password"], ["grant_type"] = "client_credentials", ["scope"] = "https://storage.azure.com/.default"
            }), ct);
            auth.EnsureSuccessStatusCode();
            var token = (await auth.Content.ReadFromJsonAsync<JsonObject>(ct))!["access_token"]!.GetValue<string>();
            var root = values["endpoint"].TrimEnd('/') + "/" + Uri.EscapeDataString(values["bucket"]);
            async Task<HttpResponseMessage> Get(string url)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Add("x-ms-version", "2023-08-03");
                var response = await client.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    response.Dispose();
                    throw new InvalidOperationException("Lake request failed. Check endpoint and access.");
                }
                return response;
            }
            using var listing = await Get(root + "?resource=filesystem&recursive=true&maxResults=1001&directory=" + Uri.EscapeDataString(values.GetValueOrDefault("prefix", "")));
            var paths = (await listing.Content.ReadFromJsonAsync<JsonObject>(ct))!["paths"]!.AsArray();
            if (listing.Headers.TryGetValues("x-ms-continuation", out var continuation) && continuation.Any(value => value.Length > 0) || paths.Count > DataSourceRules.MaxRecords)
            {
                throw new InvalidOperationException("Choose a smaller folder (up to 1,000 paths).");
            }
            foreach (var file in paths.Where(file => file?["isDirectory"]?.ToString() is not ("true" or "True")))
            {
                var path = file!["name"]!.GetValue<string>();
                if (path.Split('/').Any(segment => segment is "." or ".."))
                {
                    throw new InvalidOperationException("Invalid lake path.");
                }
                if (discover)
                {
                    await Add(path, Stream.Null);
                    continue;
                }
                using var response = await Get(root + "/" + string.Join('/', path.Split('/').Select(Uri.EscapeDataString)));
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                await Add(path, stream);
            }
        }
        return rows;
    }
}
