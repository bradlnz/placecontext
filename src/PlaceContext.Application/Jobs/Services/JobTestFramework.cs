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
        "python" => "unittest",
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
        import importlib.util
        import json
        import os
        import sys
        import time
        import unittest

        TARGET = __TARGET__
        os.environ["PC_TEST_CONTEXT"] = sys.stdin.read()

        spec = importlib.util.spec_from_file_location("placecontext_user_tests", f"/work/{TARGET}")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)

        class RecordingResult(unittest.TextTestResult):
            def __init__(self, *args, **kwargs):
                super().__init__(*args, **kwargs)
                self.method_results = []
                self.started = {}

            def startTest(self, test):
                self.started[id(test)] = time.perf_counter()
                super().startTest(test)

            def record(self, test, status, message=None):
                elapsed = int((time.perf_counter() - self.started.get(id(test), time.perf_counter())) * 1000)
                self.method_results.append({
                    "name": test.id().replace("placecontext_user_tests.", ""),
                    "status": status,
                    "durationMs": elapsed,
                    "message": message,
                })

            def addSuccess(self, test):
                super().addSuccess(test)
                self.record(test, "Passed")

            def addFailure(self, test, err):
                super().addFailure(test, err)
                self.record(test, "Failed", self._exc_info_to_string(err, test))

            def addError(self, test, err):
                super().addError(test, err)
                self.record(test, "Failed", self._exc_info_to_string(err, test))

            def addSkip(self, test, reason):
                super().addSkip(test, reason)
                self.record(test, "Skipped", reason)

        suite = unittest.defaultTestLoader.loadTestsFromModule(module)
        result = unittest.TextTestRunner(stream=sys.stderr, verbosity=2, resultclass=RecordingResult).run(suite)
        print("__PLACECONTEXT_TEST_RESULTS__=" + json.dumps(result.method_results, separators=(",", ":")))
        raise SystemExit(0 if result.wasSuccessful() else 1)
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
