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
}
