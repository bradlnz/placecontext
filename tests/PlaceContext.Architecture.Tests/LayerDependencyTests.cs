using System.Reflection;
using PlaceContext.Domain.Entities;
using NetArchTest.Rules;
using Xunit;

namespace PlaceContext.Architecture.Tests;

/// <summary>
/// The Onion direction rule made executable: dependencies point INWARD only, and the infrastructure
/// concerns (git, sqlite, process, ASP.NET) never leak into the inner layers. Runs in CI, so an
/// accidental outward reference fails the build — not just a lint warning.
/// </summary>
public class LayerDependencyTests
{
    private const string Domain = "PlaceContext.Domain";
    private const string Application = "PlaceContext.Application";
    private const string Infrastructure = "PlaceContext.Infrastructure";
    private const string Host = "PlaceContext.Host";

    private static readonly Assembly DomainAsm = typeof(Project).Assembly;
    private static readonly Assembly ApplicationAsm = Load(Application);
    private static readonly Assembly InfrastructureAsm = Load(Infrastructure);

    [Fact]
    public void Domain_depends_on_nothing_outward()
        => AssertNoDependency(DomainAsm, Application, Infrastructure, Host);

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_host()
        => AssertNoDependency(ApplicationAsm, Infrastructure, Host);

    [Fact]
    public void Infrastructure_does_not_depend_on_host()
        => AssertNoDependency(InfrastructureAsm, Host);

    [Fact]
    public void Inner_layers_are_free_of_infrastructure_concerns()
    {
        string[] concerns =
        {
            "LibGit2Sharp", "Microsoft.Data.Sqlite", "Dapper",
            "System.Diagnostics.Process", "Microsoft.AspNetCore"
        };
        AssertNoDependency(DomainAsm, concerns);
        AssertNoDependency(ApplicationAsm, concerns);
    }

    private static Assembly Load(string name) => Assembly.Load(new AssemblyName(name));

    private static void AssertNoDependency(Assembly assembly, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            "offending types: " + string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }
}
