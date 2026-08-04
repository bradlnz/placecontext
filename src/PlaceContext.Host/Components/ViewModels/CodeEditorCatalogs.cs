namespace PlaceContext.Host.Components.ViewModels;

public static class EditorLanguageCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Languages = new Dictionary<
        string,
        string
    >(StringComparer.OrdinalIgnoreCase)
    {
        [".js"] = "javascript",
        [".cjs"] = "javascript",
        [".mjs"] = "javascript",
        [".ts"] = "typescript",
        [".py"] = "python",
        [".go"] = "go",
        [".rb"] = "ruby",
        [".cs"] = "csharp",
        [".json"] = "json",
        [".sh"] = "shell",
        [".md"] = "markdown",
        [".html"] = "html",
        [".css"] = "css",
        [".yml"] = "yaml",
        [".yaml"] = "yaml",
    };

    public static string ForPath(string path)
    {
        var extension = System.IO.Path.GetExtension(path);
        return Languages.GetValueOrDefault(extension, "plaintext");
    }
}

public static class EditorPathCatalog
{
    public static string Normalize(string value) => value.Trim().Replace('\\', '/').TrimStart('/');
}

public static class JobEditorStatusCatalog
{
    public const string Succeeded = "Succeeded";
    public const string Partial = "Partial";
    public const string Failed = "Failed";
    public const string Running = "Running";

    public static string Color(string status) =>
        status switch
        {
            Succeeded => "var(--good)",
            Partial => "var(--warn)",
            Failed => "var(--bad)",
            Running => "var(--brand-2)",
            _ => "var(--text-3)",
        };

    public static string Background(string status) =>
        status switch
        {
            Succeeded => "var(--good-bg)",
            Partial => "var(--warn-bg)",
            Failed => "var(--bad-bg)",
            Running => "var(--brand-bg)",
            _ => "var(--card-2)",
        };
}

public static class JobTestRuntimeCatalog
{
    public const string Python = "python";
    public const string Node = "node";
    public const string Go = "go";
    public const string Ruby = "ruby";
    public const string Default = Python;

    public static string Label(string runtime) =>
        runtime switch
        {
            Node => "Node.js",
            Go => "Go",
            Ruby => "Ruby",
            _ => "Python",
        };

    public static (string Path, string Content) Starter(string runtime) =>
        runtime switch
        {
            Node => (
                "job.test.js",
                """
                const test = require("node:test");
                const assert = require("node:assert/strict");
                const context = JSON.parse(process.env.PC_TEST_CONTEXT);
                test("job succeeds", () => assert.equal(context.run.status, "Succeeded"));
                test("job reports its shard results", () => assert.ok(context.run.shards.length > 0));
                """
            ),
            Go => (
                "job_test.go",
                """
                package main
                import ("encoding/json"; "os"; "testing")
                func TestJobSucceeds(t *testing.T) {
                    var context struct { Run struct { Status string `json:"status"` } `json:"run"` }
                    if err := json.Unmarshal([]byte(os.Getenv("PC_TEST_CONTEXT")), &context); err != nil { t.Fatal(err) }
                    if context.Run.Status != "Succeeded" { t.Fatalf("expected Succeeded, got %s", context.Run.Status) }
                }
                """
            ),
            Ruby => (
                "job_test.rb",
                """
                require "json"
                require "minitest/test"
                class JobOutputTest < Minitest::Test
                  def test_job_succeeds
                    assert_equal "Succeeded", JSON.parse(ENV.fetch("PC_TEST_CONTEXT")).dig("run", "status")
                  end
                end
                """
            ),
            _ => (
                "test_job.py",
                """
                import json
                import os
                def test_job_succeeds():
                    context = json.loads(os.environ["PC_TEST_CONTEXT"])
                    assert context["run"]["status"] == "Succeeded"
                """
            ),
        };

    public static string DefaultEntrypoint(string runtime) => Starter(runtime).Path;
}
