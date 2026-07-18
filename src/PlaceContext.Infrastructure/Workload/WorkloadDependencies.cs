using System.Security.Cryptography;
using System.Text;

namespace PlaceContext.Infrastructure.Workload;

/// <summary>
/// Dependency-manifest support for code workloads, shared by both runners: a job that ships its
/// runtime's manifest gets its packages installed before the entrypoint runs —
/// <c>requirements.txt</c> → pip, <c>package.json</c> → npm, <c>Gemfile</c> → bundler,
/// <c>go.mod</c> → module-aware go with writable caches. Installer output goes to stderr (stdout
/// is the artifact channel in-cluster), installs land in a writable deps root (the code mount /
/// rootfs may be read-only), and downloads still require the job's AllowNetworkEgress — the
/// no-network sandbox default is never silently relaxed. Every write is confined to the deps root,
/// /tmp, or a redirected $HOME, so the recipes also work unchanged when job containers run as a
/// non-root user (<see cref="WorkloadRunnerOptions.RunAsUser"/>, default nobody): pip's
/// <c>--target</c> never touches site-packages, npm's cache rides under the redirected $HOME,
/// bundler has BUNDLE_PATH, and go's GOPATH/GOCACHE are moved off /root.
///
/// <para><b>Warm dependency layers (bake once, reuse after).</b> The install is the expensive,
/// repeatable part — so both runners can build it once per
/// <see cref="BakeKey">(runtime, base image, manifest + lockfiles)</see> and reuse it afterwards:
/// the Docker runner bakes a <c>pcwarm-*</c> image (<see cref="Dockerfile"/>) and the Kubernetes
/// runner tars the installed deps into the object store (the bake Job runs the same
/// <see cref="Recipe.InstallTemplate"/> the shard would, so the tar mirrors a shard's /work). A
/// warmed shard still applies <see cref="Recipe.EnvTemplate"/> (PYTHONPATH etc. find the baked
/// deps) but skips <see cref="Recipe.InstallTemplate"/> when the cache marker is present. Any
/// warm-path failure falls back to the cold per-run install — a run never fails because warming
/// did.</para>
/// </summary>
public static class WorkloadDependencies
{
    /// <summary>
    /// One runtime's recipe. <c>{app}</c> = where the code lives at run time, <c>{deps}</c> = the
    /// writable root. <paramref name="NeedsWritableApp"/> marks tooling that must write beside the
    /// code (node_modules, .bundle) — the Docker runner copies /work into the deps tmpfs for those;
    /// on Kubernetes /work is already a writable emptyDir.
    /// </summary>
    /// <param name="SetupTemplate">Cold path verbatim: env + install in one chain (unchanged legacy).</param>
    /// <param name="EnvTemplate">Exports the runtime needs to FIND the deps (always applied, warm or cold).</param>
    /// <param name="InstallTemplate">The install itself — skipped on a warm shard (the cache marker is present).</param>
    /// <param name="Companions">Extra manifest files (lockfiles) that join the bake key and the bake COPY set.</param>
    /// <param name="BakeInstall">The bake-time install. Differs from <paramref name="InstallTemplate"/>
    /// where the cold path defers work to run time (go's <c>go run</c> downloads modules implicitly —
    /// the bake must <c>go mod download</c> explicitly to fill the cache).</param>
    /// <param name="BakeEnv">ENV pairs baked into the warm image so the runtime resolves deps from the
    /// baked layer (e.g. NODE_PATH, since the script stays at /work while node_modules lives in the image).</param>
    public sealed record Recipe(
        string Manifest,
        bool NeedsWritableApp,
        string SetupTemplate,
        string EnvTemplate,
        string InstallTemplate,
        IReadOnlyList<string> Companions,
        string BakeInstall,
        IReadOnlyList<(string Key, string Value)> BakeEnv,
        string? InvokePrefix = null);

    /// <summary>Docker: the writable, exec-capable tmpfs the runner mounts when a recipe applies.</summary>
    public const string DockerDepsRoot = "/pcdeps";

    /// <summary>Kubernetes: /work is a writable emptyDir, so deps live beside the code.</summary>
    public const string K8sDepsRoot = "/work/.pcdeps";

    /// <summary>
    /// Marker file the bake drops into the cache root; a shard that extracted a warm tar finds it
    /// and skips the install. Lives at the /work root (not inside .pcdeps) so it works for
    /// recipes whose deps land beside the code (node_modules).
    /// </summary>
    public const string BakedMarker = "/work/.baked";

    /// <summary>The install root inside a warm image / bake staging dir (substituted for {deps}).</summary>
    private const string BakeDepsRoot = "/pcw";

    /// <summary>Where manifests are copied inside a warm image / bake staging dir (substituted for {app}).</summary>
    private const string BakeAppDir = "/pcw/app";

    /// <summary>Bump when the bake layout or any recipe's bake fields change — old caches stop matching.</summary>
    private const string BakeFormatVersion = "v1";

    private static readonly Dictionary<string, Recipe> Recipes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["python"] = new(
            "requirements.txt", NeedsWritableApp: false,
            SetupTemplate: "pip install --no-cache-dir --target {deps}/lib -r {app}/requirements.txt 1>&2 && export PYTHONPATH={deps}/lib",
            EnvTemplate: "export PYTHONPATH={deps}/lib",
            InstallTemplate: "pip install --no-cache-dir --target {deps}/lib -r {app}/requirements.txt 1>&2",
            Companions: Array.Empty<string>(),
            BakeInstall: "pip install --no-cache-dir --target {deps}/lib -r {app}/requirements.txt",
            BakeEnv: new[] { ("PYTHONPATH", BakeDepsRoot + "/lib") }),
        // HOME points into the deps root: the image's real home (/root) is read-only under the
        // sandbox, and npm/bundler insist on writable caches under $HOME.
        ["node"] = new(
            "package.json", NeedsWritableApp: true,
            SetupTemplate: "cd {app} && export HOME={deps} && npm install --no-audit --no-fund --loglevel=error 1>&2",
            // HOME outside the deps root on warm paths: npm's own cache (~/.npm) must not ride in
            // the baked layer — only node_modules is payload. A warm shard never runs npm anyway.
            EnvTemplate: "export HOME=/tmp/pchome",
            InstallTemplate: "cd {app} && npm install --no-audit --no-fund --loglevel=error 1>&2",
            Companions: new[] { "package-lock.json", "npm-shrinkwrap.json", "yarn.lock", "pnpm-lock.yaml" },
            BakeInstall: "cd {app} && npm install --no-audit --no-fund",
            // NODE_PATH: the script runs from /work (read-only mount) while node_modules sits in the
            // image — the resolver walks NODE_PATH when no sibling node_modules exists.
            BakeEnv: new[] { ("NODE_PATH", BakeAppDir + "/node_modules"), ("HOME", BakeDepsRoot) }),
        ["ruby"] = new(
            "Gemfile", NeedsWritableApp: true,
            SetupTemplate: "cd {app} && export HOME={deps} BUNDLE_PATH={deps}/lib BUNDLE_APP_CONFIG={deps}/bundleconf && bundle install --quiet 1>&2",
            // BUNDLE_PATH (the gems) is the payload; bundler's own cache stays out of the baked layer.
            EnvTemplate: "export HOME=/tmp/pchome BUNDLE_PATH={deps}/lib BUNDLE_APP_CONFIG={deps}/bundleconf",
            InstallTemplate: "cd {app} && bundle install --quiet 1>&2",
            Companions: new[] { "Gemfile.lock" },
            BakeInstall: "cd {app} && bundle install",
            BakeEnv: new[] { ("HOME", BakeDepsRoot), ("BUNDLE_PATH", BakeDepsRoot + "/lib"),
                ("BUNDLE_APP_CONFIG", BakeDepsRoot + "/bundleconf"), ("BUNDLE_GEMFILE", BakeAppDir + "/Gemfile") },
            InvokePrefix: "bundle exec"),
        ["go"] = new(
            "go.mod", NeedsWritableApp: false,
            SetupTemplate: "export HOME={deps} GOPATH={deps}/go GOMODCACHE={deps}/go/pkg/mod GOCACHE={deps}/gocache GOFLAGS=-mod=mod && cd {app}",
            // GOMODCACHE (downloaded modules) is the payload and rides in the baked layer; GOCACHE
            // (compile cache) is huge and per-run, so it lives on scratch instead.
            EnvTemplate: "export HOME=/tmp/pchome GOPATH={deps}/go GOMODCACHE={deps}/go/pkg/mod GOCACHE=/tmp/gocache GOFLAGS=-mod=mod",
            // The cold path downloads modules implicitly at `go run` (GOFLAGS=-mod=mod) — there is
            // no separate install step; the cd only anchors the module context.
            InstallTemplate: "cd {app}",
            Companions: new[] { "go.sum" },
            BakeInstall: "cd {app} && go mod download",
            // GOCACHE (the compile cache) is NOT baked — it's huge; a per-run tmpfs keeps builds correct.
            BakeEnv: new[] { ("HOME", BakeDepsRoot), ("GOPATH", BakeDepsRoot + "/go"),
                ("GOMODCACHE", BakeDepsRoot + "/go/pkg/mod"), ("GOCACHE", "/tmp/gocache"), ("GOFLAGS", "-mod=mod") }),
    };

    /// <summary>The recipe for this workload, when its runtime has one and the manifest is in the file set.</summary>
    public static Recipe? For(string? runtimeId, IReadOnlyList<(string Path, string Content)>? codeFiles) =>
        runtimeId is not null && codeFiles is not null
        && Recipes.TryGetValue(runtimeId, out var recipe)
        && codeFiles.Any(f => string.Equals(f.Path, recipe.Manifest, StringComparison.OrdinalIgnoreCase))
            ? recipe
            : null;

    /// <summary>Apply a recipe template: substitutes the {app} and {deps} placeholders.</summary>
    public static string Apply(string template, string app, string deps)
        => template.Replace("{app}", app).Replace("{deps}", deps);

    /// <summary>
    /// The manifest files that define the dependency layer: the recipe's manifest plus whichever
    /// companions (lockfiles) the workload ships, in stable order. The bake key and the bake's
    /// COPY/materialize set both come from this.
    /// </summary>
    public static IReadOnlyList<(string Path, string Content)> ManifestFiles(
        Recipe recipe, IReadOnlyList<(string Path, string Content)> codeFiles)
    {
        var wanted = new[] { recipe.Manifest }.Concat(recipe.Companions);
        var files = new List<(string, string)>();
        foreach (var name in wanted)
        {
            var match = codeFiles.FirstOrDefault(f => string.Equals(f.Path, name, StringComparison.OrdinalIgnoreCase));
            if (match.Path is not null) files.Add(match);
        }
        return files;
    }

    /// <summary>
    /// Content hash (16 hex chars) identifying the dependency layer: format version, runtime, base
    /// image, and every manifest/lockfile's content. Same key ⇒ the warm layer is reusable; any
    /// manifest change ⇒ a new key (the old layer is simply never selected again).
    /// </summary>
    public static string BakeKey(string runtimeId, string baseImage, Recipe recipe,
        IReadOnlyList<(string Path, string Content)> codeFiles)
    {
        var sb = new StringBuilder(BakeFormatVersion).Append('\0')
            .Append(runtimeId).Append('\0')
            .Append(baseImage).Append('\0');
        foreach (var (path, content) in ManifestFiles(recipe, codeFiles))
            sb.Append(path).Append('\0').Append(content).Append('\0');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16].ToLowerInvariant();
    }

    /// <summary>
    /// The Dockerfile for a bake-once warm image: base image + manifest copies + the bake install +
    /// the ENV that lets the runtime resolve deps from the baked layer. Only manifests actually
    /// shipped are COPY'd (no unmatched-glob failures).
    /// </summary>
    public static string Dockerfile(string baseImage, Recipe recipe,
        IReadOnlyList<(string Path, string Content)> codeFiles)
    {
        var sb = new StringBuilder();
        sb.Append("FROM ").Append(baseImage).Append('\n');
        sb.Append("LABEL placecontext.warm=true\n");
        foreach (var (path, _) in ManifestFiles(recipe, codeFiles))
            sb.Append("COPY ").Append(path).Append(' ').Append(BakeAppDir).Append('/').Append(path).Append('\n');
        if (recipe.BakeEnv.Count > 0)
            sb.Append("ENV ").AppendJoin(' ', recipe.BakeEnv.Select(kv => $"{kv.Key}={kv.Value}")).Append('\n');
        sb.Append("RUN ").Append(Apply(recipe.BakeInstall, BakeAppDir, BakeDepsRoot)).Append('\n');
        return sb.ToString();
    }

    /// <summary>
    /// Wraps a docker CMD override so the install runs first: <c>sh -c '&lt;setup&gt; &amp;&amp; exec cmd'</c>.
    /// When the recipe needs a writable app dir, /work (read-only mount) is first copied into the
    /// deps tmpfs and the invoke command is rewritten to run from the copy. The original command is
    /// exec'd (keeps its exit code and signal handling) with each argument single-quoted.
    /// </summary>
    public static string[] WrapDockerCommand(Recipe recipe, string[] cmd)
    {
        var app = recipe.NeedsWritableApp ? $"{DockerDepsRoot}/app" : "/work";
        var setup = Apply(recipe.SetupTemplate, app, DockerDepsRoot);
        if (recipe.NeedsWritableApp)
            setup = $"cp -R /work {app} && " + setup;

        var invoke = string.Join(" ", cmd
            .Select(seg => recipe.NeedsWritableApp ? seg.Replace("/work", app, StringComparison.Ordinal) : seg)
            .Select(ShQuote));
        if (recipe.InvokePrefix is not null) invoke = recipe.InvokePrefix + " " + invoke;

        return new[] { "sh", "-c", setup + " && exec " + invoke };
    }

    /// <summary>
    /// The in-pod shell preamble for the Kubernetes runner: export the env the runtime needs
    /// (always — it locates the deps), then install (or fail the run with the installer's exit
    /// code) only when no warm cache landed — the <see cref="BakedMarker"/> check is what makes a
    /// warmed shard skip the install. /work is writable there, so {app} stays /work. Guarded by a
    /// manifest check so the shell stays harmless if the file didn't materialise.
    /// </summary>
    public static string ShellPreamble(Recipe recipe)
    {
        var env = Apply(recipe.EnvTemplate, "/work", K8sDepsRoot);
        var install = Apply(recipe.InstallTemplate, "/work", K8sDepsRoot);
        return $"if [ -f /work/{recipe.Manifest} ]; then\n" +
               $"  mkdir -p {K8sDepsRoot}\n" +
               $"  {env}\n" +
               $"  if [ ! -f {BakedMarker} ]; then\n" +
               $"    {install} || exit $?\n" +
               "  fi\n" +
               "fi\n";
    }

    private static string ShQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
