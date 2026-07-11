namespace PlaceContext.Infrastructure.Workload;

/// <summary>
/// Dependency-manifest support for code workloads, shared by both runners: a job that ships its
/// runtime's manifest gets its packages installed before the entrypoint runs —
/// <c>requirements.txt</c> → pip, <c>package.json</c> → npm, <c>Gemfile</c> → bundler,
/// <c>go.mod</c> → module-aware go with writable caches. Installer output goes to stderr (stdout
/// is the artifact channel in-cluster), installs land in a writable deps root (the code mount /
/// rootfs may be read-only), and downloads still require the job's AllowNetworkEgress — the
/// no-network sandbox default is never silently relaxed.
/// </summary>
public static class WorkloadDependencies
{
    /// <summary>
    /// One runtime's recipe. <c>{app}</c> = where the code lives at run time, <c>{deps}</c> = the
    /// writable root. <paramref name="NeedsWritableApp"/> marks tooling that must write beside the
    /// code (node_modules, .bundle) — the Docker runner copies /work into the deps tmpfs for those;
    /// on Kubernetes /work is already a writable emptyDir.
    /// </summary>
    public sealed record Recipe(string Manifest, bool NeedsWritableApp, string SetupTemplate, string? InvokePrefix = null);

    /// <summary>Docker: the writable, exec-capable tmpfs the runner mounts when a recipe applies.</summary>
    public const string DockerDepsRoot = "/pcdeps";

    /// <summary>Kubernetes: /work is a writable emptyDir, so deps live beside the code.</summary>
    public const string K8sDepsRoot = "/work/.pcdeps";

    private static readonly Dictionary<string, Recipe> Recipes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["python"] = new(
            "requirements.txt", NeedsWritableApp: false,
            "pip install --no-cache-dir --target {deps}/lib -r {app}/requirements.txt 1>&2 && export PYTHONPATH={deps}/lib"),
        // HOME points into the deps root: the image's real home (/root) is read-only under the
        // sandbox, and npm/bundler insist on writable caches under $HOME.
        ["node"] = new(
            "package.json", NeedsWritableApp: true,
            "cd {app} && export HOME={deps} && npm install --no-audit --no-fund --loglevel=error 1>&2"),
        ["ruby"] = new(
            "Gemfile", NeedsWritableApp: true,
            "cd {app} && export HOME={deps} BUNDLE_PATH={deps}/lib BUNDLE_APP_CONFIG={deps}/bundleconf && bundle install --quiet 1>&2",
            InvokePrefix: "bundle exec"),
        ["go"] = new(
            "go.mod", NeedsWritableApp: false,
            "export HOME={deps} GOPATH={deps}/go GOMODCACHE={deps}/go/pkg/mod GOCACHE={deps}/gocache GOFLAGS=-mod=mod && cd {app}"),
    };

    /// <summary>The recipe for this workload, when its runtime has one and the manifest is in the file set.</summary>
    public static Recipe? For(string? runtimeId, IReadOnlyList<(string Path, string Content)>? codeFiles) =>
        runtimeId is not null && codeFiles is not null
        && Recipes.TryGetValue(runtimeId, out var recipe)
        && codeFiles.Any(f => string.Equals(f.Path, recipe.Manifest, StringComparison.OrdinalIgnoreCase))
            ? recipe
            : null;

    /// <summary>
    /// Wraps a docker CMD override so the install runs first: <c>sh -c '&lt;setup&gt; &amp;&amp; exec cmd'</c>.
    /// When the recipe needs a writable app dir, /work (read-only mount) is first copied into the
    /// deps tmpfs and the invoke command is rewritten to run from the copy. The original command is
    /// exec'd (keeps its exit code and signal handling) with each argument single-quoted.
    /// </summary>
    public static string[] WrapDockerCommand(Recipe recipe, string[] cmd)
    {
        var app = recipe.NeedsWritableApp ? $"{DockerDepsRoot}/app" : "/work";
        var setup = recipe.SetupTemplate.Replace("{app}", app).Replace("{deps}", DockerDepsRoot);
        if (recipe.NeedsWritableApp)
            setup = $"cp -R /work {app} && " + setup;

        var invoke = string.Join(" ", cmd
            .Select(seg => recipe.NeedsWritableApp ? seg.Replace("/work", app, StringComparison.Ordinal) : seg)
            .Select(ShQuote));
        if (recipe.InvokePrefix is not null) invoke = recipe.InvokePrefix + " " + invoke;

        return new[] { "sh", "-c", setup + " && exec " + invoke };
    }

    /// <summary>
    /// The in-pod shell preamble for the Kubernetes runner: install (or fail the run with the
    /// installer's exit code) then run. /work is writable there, so {app} stays /work. Guarded by
    /// a manifest check so the shell stays harmless if the file didn't materialise.
    /// </summary>
    public static string ShellPreamble(Recipe recipe)
    {
        var setup = recipe.SetupTemplate.Replace("{app}", "/work").Replace("{deps}", K8sDepsRoot);
        return $"if [ -f /work/{recipe.Manifest} ]; then\n" +
               $"  mkdir -p {K8sDepsRoot}\n" +
               $"  {setup} || exit $?\n" +
               "fi\n";
    }

    private static string ShQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
