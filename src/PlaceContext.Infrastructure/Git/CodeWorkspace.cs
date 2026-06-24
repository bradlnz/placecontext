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
