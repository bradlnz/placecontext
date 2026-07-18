using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Infrastructure.Tests;

/// <summary>
/// Dependency-manifest recipes: a code workload shipping its runtime's manifest gets its packages
/// installed before the entrypoint runs, in a sandbox-safe way (writable deps root, installer
/// output on stderr, original command exec'd).
/// </summary>
public class WorkloadDependenciesTests
{
    private static IReadOnlyList<(string, string)> Files(params string[] names) =>
        names.Select(n => (n, "content")).ToList();

    [Theory]
    [InlineData("python", "requirements.txt")]
    [InlineData("node", "package.json")]
    [InlineData("ruby", "Gemfile")]
    [InlineData("go", "go.mod")]
    public void Recipe_applies_when_the_runtime_manifest_ships(string runtime, string manifest)
    {
        Assert.NotNull(WorkloadDependencies.For(runtime, Files("main", manifest)));
        Assert.Null(WorkloadDependencies.For(runtime, Files("main")));            // no manifest
        Assert.Null(WorkloadDependencies.For(runtime, null));                     // image workload
    }

    [Fact]
    public void Manifest_of_a_different_runtime_does_not_trigger()
    {
        // A python job carrying package.json (say, as data) must not npm install.
        Assert.Null(WorkloadDependencies.For("python", Files("main.py", "package.json")));
        Assert.Null(WorkloadDependencies.For("dotnet", Files("main.cs", "requirements.txt")));
    }

    [Fact]
    public void Docker_wrap_installs_then_execs_the_original_command()
    {
        var recipe = WorkloadDependencies.For("python", Files("main.py", "requirements.txt"))!;
        var cmd = WorkloadDependencies.WrapDockerCommand(recipe, new[] { "python", "/work/main.py" });

        Assert.Equal(new[] { "sh", "-c" }, cmd[..2]);
        Assert.Contains("pip install", cmd[2]);
        Assert.Contains("-r /work/requirements.txt 1>&2", cmd[2]);                // installer noise → stderr
        Assert.Contains("export PYTHONPATH=/pcdeps/lib", cmd[2]);
        Assert.EndsWith("exec 'python' '/work/main.py'", cmd[2]);                 // exec'd, args quoted
    }

    [Fact]
    public void Docker_wrap_copies_to_a_writable_app_dir_when_tooling_writes_beside_the_code()
    {
        var recipe = WorkloadDependencies.For("node", Files("index.js", "package.json"))!;
        var cmd = WorkloadDependencies.WrapDockerCommand(recipe, new[] { "node", "/work/index.js" });

        Assert.Contains("cp -R /work /pcdeps/app", cmd[2]);                       // /work mount is read-only
        Assert.Contains("npm install", cmd[2]);
        Assert.EndsWith("exec 'node' '/pcdeps/app/index.js'", cmd[2]);            // invoke follows the copy
    }

    [Fact]
    public void Ruby_invoke_runs_under_bundle_exec()
    {
        var recipe = WorkloadDependencies.For("ruby", Files("main.rb", "Gemfile"))!;
        var cmd = WorkloadDependencies.WrapDockerCommand(recipe, new[] { "ruby", "/work/main.rb" });
        Assert.Contains("bundle install", cmd[2]);
        Assert.EndsWith("exec bundle exec 'ruby' '/pcdeps/app/main.rb'", cmd[2]);
    }

    [Fact]
    public void K8s_preamble_is_guarded_and_fails_the_run_with_the_installer_exit_code()
    {
        var recipe = WorkloadDependencies.For("python", Files("main.py", "requirements.txt"))!;
        var preamble = WorkloadDependencies.ShellPreamble(recipe);
        Assert.Contains("if [ -f /work/requirements.txt ]; then", preamble);
        Assert.Contains("|| exit $?", preamble);
        Assert.Contains("--target /work/.pcdeps/lib", preamble);                  // /work is writable in-cluster
    }

    // ── warm dependency layers (bake once, reuse after) ──────────────────────────────────────────

    [Fact]
    public void K8s_preamble_always_exports_the_env_and_skips_the_install_when_the_cache_landed()
    {
        var recipe = WorkloadDependencies.For("python", Files("main.py", "requirements.txt"))!;
        var preamble = WorkloadDependencies.ShellPreamble(recipe);
        Assert.Contains("export PYTHONPATH=/work/.pcdeps/lib", preamble); // the runtime must find the deps either way
        Assert.Contains("if [ ! -f /work/.baked ]; then", preamble);      // …but the install only runs when cold
    }

    [Fact]
    public void Bake_key_is_stable_for_the_same_layer_and_changes_with_manifest_runtime_or_lockfiles()
    {
        var recipe = WorkloadDependencies.For("node", Files("package.json"))!;
        var files = new[] { ("index.js", "code-v1"), ("package.json", "{}"), ("package-lock.json", "L1") };
        var key = WorkloadDependencies.BakeKey("node", "node:22-slim", recipe, files);

        Assert.Equal(key, WorkloadDependencies.BakeKey("node", "node:22-slim", recipe, files));
        // Code-only changes reuse the layer — the key is defined by the manifests, not the source.
        Assert.Equal(key, WorkloadDependencies.BakeKey("node", "node:22-slim", recipe,
            new[] { ("index.js", "code-v2"), ("package.json", "{}"), ("package-lock.json", "L1") }));

        Assert.NotEqual(key, WorkloadDependencies.BakeKey("node", "node:22-slim", recipe,
            new[] { ("index.js", "code-v1"), ("package.json", "{\"changed\":1}"), ("package-lock.json", "L1") }));
        Assert.NotEqual(key, WorkloadDependencies.BakeKey("node", "node:20-slim", recipe, files));
        Assert.NotEqual(key, WorkloadDependencies.BakeKey("node", "node:22-slim", recipe,
            new[] { ("index.js", "code-v1"), ("package.json", "{}"), ("package-lock.json", "L2") }));
    }

    [Fact]
    public void Dockerfile_bakes_the_install_and_env_with_only_shipped_manifests()
    {
        var files = new[] { ("index.js", "x"), ("package.json", "{}"), ("package-lock.json", "L") };
        var recipe = WorkloadDependencies.For("node", files)!;
        var dockerfile = WorkloadDependencies.Dockerfile("node:22-slim", recipe, files);

        Assert.StartsWith("FROM node:22-slim\n", dockerfile);
        Assert.Contains("LABEL placecontext.warm=true", dockerfile);
        Assert.Contains("COPY package.json /pcw/app/package.json", dockerfile);
        Assert.Contains("COPY package-lock.json /pcw/app/package-lock.json", dockerfile);
        Assert.DoesNotContain("yarn.lock", dockerfile); // not shipped → no COPY for it
        Assert.Contains("RUN cd /pcw/app && npm install", dockerfile);
        Assert.Contains("ENV NODE_PATH=/pcw/app/node_modules", dockerfile);
    }

    [Fact]
    public void Dockerfile_for_python_bakes_pip_target_and_pythonpath()
    {
        var files = new[] { ("main.py", "x"), ("requirements.txt", "six") };
        var recipe = WorkloadDependencies.For("python", files)!;
        var dockerfile = WorkloadDependencies.Dockerfile("python:3.12-slim", recipe, files);

        Assert.Contains("COPY requirements.txt /pcw/app/requirements.txt", dockerfile);
        Assert.Contains("RUN pip install --no-cache-dir --target /pcw/lib -r /pcw/app/requirements.txt", dockerfile);
        Assert.Contains("ENV PYTHONPATH=/pcw/lib", dockerfile);
    }

    [Fact]
    public void Dockerfile_for_go_downloads_modules_but_keeps_the_compile_cache_on_scratch()
    {
        var files = new[] { ("main.go", "package main"), ("go.mod", "mod x"), ("go.sum", "s") };
        var recipe = WorkloadDependencies.For("go", files)!;
        var dockerfile = WorkloadDependencies.Dockerfile("golang:1.23-alpine", recipe, files);

        Assert.Contains("COPY go.sum /pcw/app/go.sum", dockerfile);
        Assert.Contains("RUN cd /pcw/app && go mod download", dockerfile); // the bake, not `go run`'s implicit fetch
        Assert.Contains("GOCACHE=/tmp/gocache", dockerfile);               // never baked into the layer
    }

    // ── non-root safety ──────────────────────────────────────────────────────────────────────────
    // Job containers run as RunAsUser (nobody, 65534) with a read-only rootfs: every recipe must
    // confine its writes to the deps root / a redirected $HOME — never system site-packages or
    // /root. These lock that invariant so a future recipe edit can't quietly reintroduce a
    // root-owned write.

    [Fact]
    public void Pip_installs_into_the_deps_root_never_site_packages()
    {
        var recipe = WorkloadDependencies.For("python", Files("main.py", "requirements.txt"))!;
        Assert.Contains("--target {deps}/lib", recipe.InstallTemplate); // redirected, not /usr/lib/python*
        Assert.Contains("--no-cache-dir", recipe.InstallTemplate);      // no ~/.cache/pip writes
        Assert.DoesNotContain("--user", recipe.InstallTemplate);        // --target already fully redirects
    }

    [Fact]
    public void Npm_install_and_cache_stay_on_writable_paths()
    {
        var recipe = WorkloadDependencies.For("node", Files("index.js", "package.json"))!;
        Assert.Contains("export HOME={deps}", recipe.SetupTemplate); // npm's cache rides under $HOME
        Assert.DoesNotContain("--global", recipe.InstallTemplate);   // local install, never the global prefix
        Assert.DoesNotContain(" -g ", recipe.InstallTemplate);
    }

    [Fact]
    public void Bundler_installs_into_the_deps_root()
    {
        var recipe = WorkloadDependencies.For("ruby", Files("main.rb", "Gemfile"))!;
        Assert.Contains("BUNDLE_PATH={deps}/lib", recipe.SetupTemplate); // the persistent form of --path
        Assert.Contains("BUNDLE_APP_CONFIG={deps}/bundleconf", recipe.SetupTemplate); // config off ~/.bundle
    }

    [Fact]
    public void Go_caches_are_redirected_off_root_owned_paths()
    {
        var recipe = WorkloadDependencies.For("go", Files("main.go", "go.mod"))!;
        Assert.Contains("GOPATH={deps}/go", recipe.SetupTemplate);
        Assert.Contains("GOCACHE={deps}/gocache", recipe.SetupTemplate);  // cold path: deps tmpfs
        Assert.Contains("GOCACHE=/tmp/gocache", recipe.EnvTemplate);      // warm path: per-run scratch
    }
}
