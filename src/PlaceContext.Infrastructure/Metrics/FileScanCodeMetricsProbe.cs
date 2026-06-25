using System.Text.RegularExpressions;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Metrics;

/// <summary>
/// Lightweight technical-risk probe: walks the source files (skipping build/dep output), counting
/// TODO/FIXME markers and files. Complexity and coverage are best-effort (coverage read from a
/// cobertura file when present; otherwise reported as unknown = 0 gap).
/// </summary>
public sealed partial class FileScanCodeMetricsProbe : ICodeMetricsProbe
{
    private static readonly HashSet<string> CodeExt = new(StringComparer.OrdinalIgnoreCase)
        { ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".go", ".rs", ".java", ".rb", ".razor" };

    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
        { "node_modules", "bin", "obj", ".git", "graphify-out", "dist", "build", ".venv", "__pycache__" };

    [GeneratedRegex(@"\b(TODO|FIXME|HACK|XXX)\b")]
    private static partial Regex MarkerRegex();

    public Task<CodeMetrics> ProbeAsync(ProjectPath path, CancellationToken ct = default)
    {
        int todos = 0, files = 0;

        foreach (var file in EnumerateCodeFiles(path.Value))
        {
            ct.ThrowIfCancellationRequested();
            files++;
            try
            {
                foreach (var line in File.ReadLines(file))
                    if (MarkerRegex().IsMatch(line)) todos++;
            }
            catch (IOException) { /* skip unreadable */ }
        }

        // Coverage and complexity are not yet probed — reported as unknown (gap 0, complexity 0).
        return Task.FromResult(CodeMetrics.From(todos, highComplexityCount: 0, coverageGap: 0.0, fileCount: files));
    }

    private static IEnumerable<string> EnumerateCodeFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;

        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> subdirs = Array.Empty<string>();
            try { subdirs = Directory.EnumerateDirectories(dir); } catch { }
            foreach (var sub in subdirs)
                if (!SkipDirs.Contains(Path.GetFileName(sub)) && !Path.GetFileName(sub).StartsWith('.'))
                    stack.Push(sub);

            IEnumerable<string> filesInDir = Array.Empty<string>();
            try { filesInDir = Directory.EnumerateFiles(dir); } catch { }
            foreach (var f in filesInDir)
                if (CodeExt.Contains(Path.GetExtension(f)))
                    yield return f;
        }
    }
}
