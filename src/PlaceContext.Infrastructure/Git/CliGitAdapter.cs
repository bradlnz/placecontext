using System.Diagnostics;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Infrastructure.Git;

/// <summary>
/// Git adapter driving the <c>git</c> CLI via child processes. Chosen over a native binding to avoid
/// a native dependency on this machine; the port keeps it swappable. Commits are scoped to exactly
/// the touched files.
/// </summary>
public sealed class CliGitAdapter : IGitPort
{
    public bool IsRepository(RepoPath path)
        => Directory.Exists(Path.Combine(path.Value, ".git"))
           || Run(path.Value, "rev-parse", "--is-inside-work-tree").ExitCode == 0;

    public async Task<CommitSha?> CommitScopedAsync(
        RepoPath path, IReadOnlyList<string> files, string message, Author author, CancellationToken ct = default)
    {
        if (!IsRepository(path)) return null;
        if (files.Count > 0)
        {
            var add = new List<string> { "add", "--" };
            add.AddRange(files);
            Run(path.Value, add.ToArray());
        }

        var authorArg = $"{author.Name} <placecontext@local>";
        var commit = Run(path.Value, "commit", "-m", message, "--author", authorArg);
        if (commit.ExitCode != 0)
            return null; // nothing staged / nothing to commit

        var head = Run(path.Value, "rev-parse", "HEAD");
        return head.ExitCode == 0 && head.Stdout.Trim().Length >= 7
            ? CommitSha.From(head.Stdout.Trim())
            : null;
    }

    public Task<DateTimeOffset?> GetFileLastModifiedAsync(RepoPath path, string relativeFile, CancellationToken ct = default)
    {
        var r = Run(path.Value, "log", "-1", "--format=%cI", "--", relativeFile);
        if (r.ExitCode != 0 || string.IsNullOrWhiteSpace(r.Stdout))
            return Task.FromResult<DateTimeOffset?>(null);
        return Task.FromResult<DateTimeOffset?>(DateTimeOffset.Parse(r.Stdout.Trim()));
    }

    public Task<IReadOnlyList<CommitInfo>> GetRecentCommitsAsync(RepoPath path, int limit, CancellationToken ct = default)
    {
        if (!IsRepository(path) || limit <= 0)
            return Task.FromResult<IReadOnlyList<CommitInfo>>(Array.Empty<CommitInfo>());

        // Each commit prints a "@@C@@"-prefixed header line whose fields are separated by US (0x1f,
        // emitted by git's %x1f), then its changed files (--name-only).
        const string marker = "@@C@@";
        const char us = (char)0x1f;
        var r = Run(path.Value, "log", $"-n{limit}", "--no-merges", "--name-only",
            "--format=" + marker + "%H%x1f%an%x1f%cI%x1f%s");
        if (r.ExitCode != 0)
            return Task.FromResult<IReadOnlyList<CommitInfo>>(Array.Empty<CommitInfo>());

        var commits = new List<CommitInfo>();
        string? sha = null, author = null, message = null;
        DateTimeOffset date = default;
        var files = new List<string>();

        void Flush()
        {
            if (sha is not null)
                commits.Add(new CommitInfo(sha, author ?? "", message ?? "", date, files.ToList()));
        }

        foreach (var raw in r.Stdout.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith(marker, StringComparison.Ordinal))
            {
                Flush();
                files.Clear();
                var parts = line[marker.Length..].Split(us);
                sha = parts.ElementAtOrDefault(0);
                author = parts.ElementAtOrDefault(1);
                date = DateTimeOffset.TryParse(parts.ElementAtOrDefault(2), out var d) ? d : default;
                message = parts.ElementAtOrDefault(3);
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                files.Add(line.Trim());
            }
        }
        Flush();

        return Task.FromResult<IReadOnlyList<CommitInfo>>(commits);
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(string workingDir, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, stdout, stderr);
    }
}
