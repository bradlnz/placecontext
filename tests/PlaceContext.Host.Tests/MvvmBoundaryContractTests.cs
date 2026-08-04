using System.Text.RegularExpressions;

namespace PlaceContext.Host.Tests;

public sealed class MvvmBoundaryContractTests
{
    private static readonly Regex ViewModelInjection = new(
        @"^@inject\s+\S*ViewModel\s+Vm\s*$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant
    );

    private static readonly HashSet<string> PresentationalLayouts =
    [
        "Components/Layout/AuthLayout.razor",
        "Components/Layout/ProjectLayout.razor",
    ];

    private static readonly HashSet<string> PresentationalComponents =
    [
        "Components/Shared/CrmIcon.razor",
        "Components/Shared/DataTabs.razor",
        "Components/Shared/OpenSearchDataValue.razor",
        "Components/Shared/Skeleton.razor",
    ];

    private static readonly HashSet<string> StatelessPresentationalComponents =
    [
        "Components/Shared/CrmIcon.razor",
        "Components/Shared/DataTabs.razor",
    ];

    [Fact]
    public void Every_page_and_layout_is_backed_by_a_view_model()
    {
        var hostRoot = FindHostRoot();
        var views = EnumerateRazor(hostRoot, "Components/Pages")
            .Concat(EnumerateRazor(hostRoot, "Components/Layout"))
            .Where(view => !PresentationalLayouts.Contains(view.RelativePath));

        var violations = views
            .Where(view => !ViewModelInjection.IsMatch(view.Source))
            .Select(view => view.RelativePath)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Views without a ViewModel:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}"
        );
    }

    [Fact]
    public void Stateful_shared_components_are_backed_by_view_models()
    {
        var hostRoot = FindHostRoot();
        var violations = EnumerateRazor(hostRoot, "Components/Shared")
            .Where(view => !PresentationalComponents.Contains(view.RelativePath))
            .Where(view => !ViewModelInjection.IsMatch(view.Source))
            .Select(view => view.RelativePath)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Stateful components without a ViewModel:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}"
        );
    }

    [Fact]
    public void Razor_views_do_not_inject_application_or_infrastructure_services()
    {
        var hostRoot = FindHostRoot();
        var violations = EnumerateRazor(hostRoot, "Components")
            .SelectMany(view =>
                Regex
                    .Matches(view.Source, @"^@inject\s+(?<type>\S+)", RegexOptions.Multiline)
                    .Select(match => (view.RelativePath, Type: match.Groups["type"].Value))
            )
            .Where(injection => !injection.Type.EndsWith("ViewModel", StringComparison.Ordinal))
            .Where(injection => injection.Type is not "NavigationManager")
            .Select(injection => $"{injection.RelativePath}: {injection.Type}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Views directly injecting services:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}"
        );
    }

    [Fact]
    public void Presentational_component_exceptions_remain_parameter_driven_and_stateless()
    {
        var hostRoot = FindHostRoot();
        var violations = StatelessPresentationalComponents
            .Select(path => new RazorView(path, File.ReadAllText(Path.Combine(hostRoot, path))))
            .Where(view =>
                Regex.IsMatch(view.Source, @"^@inject\s+", RegexOptions.Multiline)
                || Regex.IsMatch(
                    view.Source,
                    @"\bprivate\s+(?!static\s+readonly)",
                    RegexOptions.CultureInvariant
                )
            )
            .Select(view => view.RelativePath)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Presentational exceptions acquired state or services:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}"
        );
    }

    [Fact]
    public void All_concrete_page_view_models_are_scoped_by_convention()
    {
        var program = File.ReadAllText(Path.Combine(FindHostRoot(), "Program.cs"));

        Assert.Contains("pageViewModelType.Assembly.GetTypes()", program, StringComparison.Ordinal);
        Assert.Contains(
            "builder.Services.AddScoped(viewModelType)",
            program,
            StringComparison.Ordinal
        );
    }

    private static IEnumerable<RazorView> EnumerateRazor(string hostRoot, string relativeDirectory)
    {
        var directory = Path.Combine(hostRoot, relativeDirectory);
        return Directory
            .EnumerateFiles(directory, "*.razor", SearchOption.AllDirectories)
            .Select(path => new RazorView(
                Path.GetRelativePath(hostRoot, path).Replace('\\', '/'),
                File.ReadAllText(path)
            ));
    }

    private static string FindHostRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var host = Path.Combine(directory.FullName, "src", "PlaceContext.Host");
            if (Directory.Exists(host))
                return host;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("PlaceContext.Host root not found.");
    }

    private sealed record RazorView(string RelativePath, string Source);
}
