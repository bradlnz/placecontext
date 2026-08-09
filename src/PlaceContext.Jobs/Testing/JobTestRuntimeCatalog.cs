namespace PlaceContext.Application.Features;

public static class JobTestRuntimeCatalog
{
    public const string Python = "python";
    public const string Node = "node";
    public const string Go = "go";
    public const string Ruby = "ruby";
    public const string Default = Python;

    public static string Label(string runtime) => runtime switch
    {
        Node => "Node.js",
        Go => "Go",
        Ruby => "Ruby",
        _ => "Python",
    };

    public static (string Path, string Content) Starter(string runtime) => runtime switch
    {
        Node => (
            "job.test.js",
            "const test = require(\"node:test\");\nconst assert = require(\"node:assert/strict\");\nconst context = JSON.parse(process.env.PC_TEST_CONTEXT);\ntest(\"job succeeds\", () => assert.equal(context.run.status, \"Succeeded\"));\ntest(\"job reports its shard results\", () => assert.ok(context.run.shards.length > 0));\n"),
        Go => (
            "job_test.go",
            "package main\nimport (\"encoding/json\"; \"os\"; \"testing\")\nfunc TestJobSucceeds(t *testing.T) {\n    var context struct { Run struct { Status string `json:\"status\"` } `json:\"run\"` }\n    if err := json.Unmarshal([]byte(os.Getenv(\"PC_TEST_CONTEXT\")), &context); err != nil { t.Fatal(err) }\n    if context.Run.Status != \"Succeeded\" { t.Fatalf(\"expected Succeeded, got %s\", context.Run.Status) }\n}\n"),
        Ruby => (
            "job_test.rb",
            "require \"json\"\nrequire \"minitest/test\"\nclass JobOutputTest < Minitest::Test\n  def test_job_succeeds\n    assert_equal \"Succeeded\", JSON.parse(ENV.fetch(\"PC_TEST_CONTEXT\")).dig(\"run\", \"status\")\n  end\nend\n"),
        _ => (
            "test_job.py",
            "import json\nimport os\ndef test_job_succeeds():\n    context = json.loads(os.environ[\"PC_TEST_CONTEXT\"])\n    assert context[\"run\"][\"status\"] == \"Succeeded\"\n"),
    };

    public static string DefaultEntrypoint(string runtime) => Starter(runtime).Path;
}
