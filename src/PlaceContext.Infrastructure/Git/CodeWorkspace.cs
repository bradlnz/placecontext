using System.Diagnostics;
using PlaceContext.Application.Ports;
using Microsoft.Extensions.Options;

namespace PlaceContext.Infrastructure.Git;

/// <summary>
/// Clones GitHub repositories into a per-tenant local workspace
/// (<c>{WorkspaceRoot}/{tenantSlug}/{repo}</c>) so they can be registered as projects. For private
/// repos the OAuth token is injected into the clone URL. If the directory already exists, the existing
/// checkout is reused (idempotent import).
/// </summary>
public sealed class CodeWorkspace : ICodeWorkspace
{
    private readonly string _root;
    public CodeWorkspace(IOptions<PlaceContextOptions> options) => _root = options.Value.WorkspaceRoot;

    public async Task<string> CloneAsync(string tenantSlug, string cloneUrl, string repoName, string? accessToken, CancellationToken ct = default)
    {
        var dir = Path.Combine(_root, Sanitize(tenantSlug), Sanitize(repoName));
        if (Directory.Exists(Path.Combine(dir, ".git")))
            return dir; // already imported

        Directory.CreateDirectory(Path.GetDirectoryName(dir)!);

        var url = cloneUrl;
        if (!string.IsNullOrEmpty(accessToken) && cloneUrl.StartsWith("https://", StringComparison.Ordinal))
            url = "https://x-access-token:" + accessToken + "@" + cloneUrl["https://".Length..];

        var (exit, stderr) = await RunAsync(_root, ct, "clone", "--depth", "1", url, dir);
        if (exit != 0)
            throw new InvalidOperationException($"git clone failed for {repoName}: {stderr}");

        return dir;
    }

    // Uploaded-archive imports (Obsidian vaults). Bounded and zip-slip-safe: every entry path is
    // resolved and must stay under the target directory; the caps stop a zip bomb from filling
    // the disk before extraction finishes.
    private const int MaxEntries = 20_000;
    private const long MaxUncompressedBytes = 512L * 1024 * 1024;

    public async Task<string> ExtractArchiveAsync(string tenantSlug, string name, Stream zipStream, CancellationToken ct = default)
    {
        var dir = Path.Combine(_root, Sanitize(tenantSlug), Sanitize(name));
        if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
            throw new InvalidOperationException($"'{name}' already exists in the workspace — pick another name.");
        Directory.CreateDirectory(dir);
        var rootFull = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;

        using var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Read);
        if (archive.Entries.Count > MaxEntries)
            throw new InvalidOperationException($"Archive has too many entries ({archive.Entries.Count} > {MaxEntries}).");

        long total = 0;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry

            total += entry.Length;
            if (total > MaxUncompressedBytes)
                throw new InvalidOperationException("Archive is too large when uncompressed (limit 512 MB).");

            var target = Path.GetFullPath(Path.Combine(dir, entry.FullName));
            if (!target.StartsWith(rootFull, StringComparison.Ordinal))
                throw new InvalidOperationException($"Archive entry escapes the target directory: {entry.FullName}");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var source = entry.Open();
            await using var dest = File.Create(target);
            await source.CopyToAsync(dest, ct);
        }
        return dir;
    }

    private static string Sanitize(string s) => string.Concat(s.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.'));

    private static async Task<(int Exit, string Stderr)> RunAsync(string workingDir, CancellationToken ct, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)!;
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return (proc.ExitCode, stderr);
    }
}
