using System.Text.Json;
using System.Text.RegularExpressions;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Framework adapters for multi-method Job test blocks.</summary>
public static partial class JobTestFramework
{
    public const string ResultPrefix = "__PLACECONTEXT_TEST_RESULTS__=";

    public static string Label(string? runtimeId) => runtimeId switch
    {
        "node" => "Node test",
        "go" => "Go testing",
        "ruby" => "Minitest",
        "python" => "pytest",
        _ => "Framework runner",
    };

    public static IReadOnlyList<JobTestMethodResult> Discover(
        string? runtimeId,
        IReadOnlyList<CodeFileDto> files)
    {
        var names = new List<string>();
        foreach (var file in files)
        {
            var matches = runtimeId switch
            {
                "node" => NodeMethodRegex().Matches(file.Content)
                    .Select(match => match.Groups[1].Value),
                "go" => GoMethodRegex().Matches(file.Content)
                    .Select(match => match.Groups[1].Value),
                "ruby" => RubyMethodRegex().Matches(file.Content)
                    .Select(match => match.Groups[1].Success
                        ? match.Groups[1].Value
                        : match.Groups[2].Value),
                _ => PythonMethodRegex().Matches(file.Content)
                    .Select(match => match.Groups[1].Value),
            };
            names.AddRange(matches.Where(name => !string.IsNullOrWhiteSpace(name)));
        }
        return names.Distinct(StringComparer.Ordinal)
            .Select(name => new JobTestMethodResult(name, "NotRun"))
            .ToList();
    }

    public static (CodeFileDto Runner, string Entrypoint) BuildRunner(
        string runtimeId,
        string targetEntrypoint)
    {
        var target = JsonSerializer.Serialize(targetEntrypoint);
        return runtimeId switch
        {
            "node" => (new("_placecontext_test_runner.cjs",
                NodeRunner.Replace("__TARGET__", target, StringComparison.Ordinal)),
                "_placecontext_test_runner.cjs"),
            "go" => (new("_placecontext_test_runner.go",
                GoRunner.Replace("__TARGET__", target, StringComparison.Ordinal)),
                "_placecontext_test_runner.go"),
            "ruby" => (new("_placecontext_test_runner.rb",
                RubyRunner.Replace("__TARGET__", target, StringComparison.Ordinal)),
                "_placecontext_test_runner.rb"),
            "python" => (new("_placecontext_test_runner.py",
                PythonRunner.Replace("__TARGET__", target, StringComparison.Ordinal)),
                "_placecontext_test_runner.py"),
            _ => throw new InvalidOperationException(
                $"Runtime '{runtimeId}' does not have a configured test framework."),
        };
    }

    public static IReadOnlyList<JobTestMethodResult> ParseResults(params string?[] outputs)
    {
        foreach (var output in outputs.Reverse())
        {
            if (string.IsNullOrWhiteSpace(output)) continue;
            var index = output.LastIndexOf(ResultPrefix, StringComparison.Ordinal);
            if (index < 0) continue;
            var json = output[(index + ResultPrefix.Length)..]
                .Split('\n', 2)[0]
                .TrimEnd('\r');
            try
            {
                return JsonSerializer.Deserialize<List<JobTestMethodResult>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new List<JobTestMethodResult>();
            }
            catch (JsonException) { }
        }
        return Array.Empty<JobTestMethodResult>();
    }

    [GeneratedRegex(@"(?m)^\s*def\s+(test_[A-Za-z0-9_]+)\s*\(")]
    private static partial Regex PythonMethodRegex();

    [GeneratedRegex("(?m)\\b(?:test|it)\\s*\\(\\s*['\"]([^'\"]+)['\"]")]
    private static partial Regex NodeMethodRegex();

    [GeneratedRegex(@"(?m)^\s*func\s+(Test[A-Za-z0-9_]+)\s*\(")]
    private static partial Regex GoMethodRegex();

    [GeneratedRegex("(?m)^\\s*(?:def\\s+(test_[A-Za-z0-9_]+)|test\\s+[\"']([^\"']+)[\"'])")]
    private static partial Regex RubyMethodRegex();

    private const string PythonRunner = """
        import json
        import os
        import sys
        import pytest

        TARGET = __TARGET__
        os.environ["PC_TEST_CONTEXT"] = sys.stdin.read()

        class PlaceContextPlugin:
            def __init__(self):
                self.results = []
                self.recorded = set()

            def pytest_runtest_logreport(self, report):
                terminal = report.when == "call" or (
                    report.when == "setup" and (report.failed or report.skipped)
                )
                if not terminal or report.nodeid in self.recorded:
                    return
                self.recorded.add(report.nodeid)
                status = "Skipped" if report.skipped else "Passed" if report.passed else "Failed"
                message = None if report.passed else getattr(report, "longreprtext", str(report.longrepr))
                self.results.append({
                    "name": report.nodeid.replace("/work/", ""),
                    "status": status,
                    "durationMs": int(report.duration * 1000),
                    "message": message,
                })

        plugin = PlaceContextPlugin()
        exit_code = pytest.main(["-q", f"/work/{TARGET}"], plugins=[plugin])
        print("__PLACECONTEXT_TEST_RESULTS__=" + json.dumps(plugin.results, separators=(",", ":")))
        raise SystemExit(int(exit_code))
        """;

    private const string NodeRunner = """
        const fs = require("node:fs");
        const { spawnSync } = require("node:child_process");

        const target = __TARGET__;
        const context = fs.readFileSync(0, "utf8");
        const child = spawnSync(process.execPath,
          ["--test", "--test-reporter=tap", `/work/${target}`],
          { encoding: "utf8", env: { ...process.env, PC_TEST_CONTEXT: context } });
        if (child.stdout) process.stderr.write(child.stdout);
        if (child.stderr) process.stderr.write(child.stderr);

        const results = [];
        const expression = /^\s*(not ok|ok)\s+\d+\s+-\s+(.+?)(?:\s+#.*)?$/gm;
        for (const match of child.stdout.matchAll(expression)) {
          results.push({ name: match[2].trim(), status: match[1] === "ok" ? "Passed" : "Failed" });
        }
        console.log("__PLACECONTEXT_TEST_RESULTS__=" + JSON.stringify(results));
        process.exit(child.status ?? 1);
        """;

    private const string GoRunner = """
        package main

        import (
            "bufio"
            "encoding/json"
            "fmt"
            "os"
            "os/exec"
            "sort"
        )

        type event struct {
            Action string  `json:"Action"`
            Test string    `json:"Test"`
            Elapsed float64 `json:"Elapsed"`
            Output string  `json:"Output"`
        }
        type result struct {
            Name string `json:"name"`
            Status string `json:"status"`
            DurationMs int64 `json:"durationMs,omitempty"`
            Message string `json:"message,omitempty"`
        }

        func main() {
            context, _ := os.ReadFile("/dev/stdin")
            command := exec.Command("go", "test", "-json", "./...")
            command.Dir = "/work"
            command.Env = append(os.Environ(), "PC_TEST_CONTEXT=" + string(context))
            stdout, _ := command.StdoutPipe()
            command.Stderr = os.Stderr
            _ = command.Start()
            methods := map[string]result{}
            scanner := bufio.NewScanner(stdout)
            for scanner.Scan() {
                var row event
                if json.Unmarshal(scanner.Bytes(), &row) != nil || row.Test == "" { continue }
                switch row.Action {
                case "pass", "fail", "skip":
                    status := map[string]string{"pass":"Passed", "fail":"Failed", "skip":"Skipped"}[row.Action]
                    methods[row.Test] = result{Name: row.Test, Status: status, DurationMs: int64(row.Elapsed * 1000)}
                }
            }
            err := command.Wait()
            names := make([]string, 0, len(methods))
            for name := range methods { names = append(names, name) }
            sort.Strings(names)
            results := make([]result, 0, len(names))
            for _, name := range names { results = append(results, methods[name]) }
            encoded, _ := json.Marshal(results)
            fmt.Println("__PLACECONTEXT_TEST_RESULTS__=" + string(encoded))
            if err != nil { os.Exit(1) }
        }
        """;

    private const string RubyRunner = """
        require "json"
        require "minitest"

        target = __TARGET__
        ENV["PC_TEST_CONTEXT"] = $stdin.read
        load File.join("/work", target)

        class PlaceContextReporter < Minitest::AbstractReporter
          attr_reader :results
          def initialize
            super
            @results = []
          end
          def record(item)
            status = item.skipped? ? "Skipped" : item.passed? ? "Passed" : "Failed"
            @results << {
              name: "#{item.class}##{item.name}", status: status,
              durationMs: (item.time * 1000).round,
              message: item.failure&.message
            }
          end
        end

        reporter = PlaceContextReporter.new
        Minitest.reporter = Minitest::CompositeReporter.new
        Minitest.reporter << reporter
        passed = Minitest.run([])
        puts "__PLACECONTEXT_TEST_RESULTS__=#{JSON.generate(reporter.results)}"
        exit(passed ? 0 : 1)
        """;
}
