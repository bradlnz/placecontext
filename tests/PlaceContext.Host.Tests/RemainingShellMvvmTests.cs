using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Dtos;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class RemainingShellMvvmTests
{
    [Fact]
    public void Remaining_shell_view_models_are_component_lifetime_transient()
    {
        var services = new ServiceCollection();
        RegisterViewModels(services);

        foreach (var type in ComponentViewModelTypes)
        {
            var descriptor = services.Single(item => item.ServiceType == type);
            Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
        }

        Assert.Equal(
            ServiceLifetime.Scoped,
            services.Single(item => item.ServiceType == typeof(MainLayoutViewModel)).Lifetime
        );
        Assert.Equal(
            ServiceLifetime.Scoped,
            services.Single(item => item.ServiceType == typeof(SettingsLayoutViewModel)).Lifetime
        );

        var program = ReadHostSource("Program.cs");
        Assert.Contains(
            "typeof(IComponentViewModel).IsAssignableFrom(viewModelType)",
            program,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "builder.Services.AddTransient(viewModelType)",
            program,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Graph_catalog_uses_domain_values_for_artifacts_and_ambiguous_links()
    {
        Assert.Equal(GraphNodeKind.Artifact, GraphCatalog.NodeKind("good"));
        Assert.Equal(GraphLinkConfidence.Ambiguous, GraphCatalog.LinkConfidence("Ambiguous"));
    }

    [Fact]
    public void Param_input_catalog_and_value_forwarding_are_typed()
    {
        var vm = new ParamInputViewModel(null!, null!);
        vm.SetParameters(
            new JobParameterDto("count", "Count", true, "number", null),
            Guid.Empty,
            "1"
        );

        Assert.Equal(ParameterInputType.Number, vm.InputType);
        vm.SetValue("2");
        Assert.Equal("2", vm.Value);
    }

    [Theory]
    [InlineData("date", ParameterInputType.Date)]
    [InlineData("datetime", ParameterInputType.DateTime)]
    [InlineData("datetime-local", ParameterInputType.DateTime)]
    [InlineData("time", ParameterInputType.Time)]
    public void Param_input_catalog_maps_temporal_controls(
        string type,
        ParameterInputType expected
    ) => Assert.Equal(expected, ParameterInputCatalog.Parse(type));

    [Fact]
    public void Chain_canvas_view_model_owns_picker_drag_and_gate_formatting()
    {
        var vm = new ChainCanvasViewModel();
        vm.SetParameters(
            Array.Empty<JobChainStageView>(),
            Array.Empty<JobView>(),
            new Dictionary<int, ChainGateView>()
        );

        vm.OpenPathPicker(3);
        Assert.Equal(3, vm.PathPickerStage);
        Assert.Equal("Wait 2.5 second(s) before this stage", vm.GateTooltip(new WaitGateView(2.5)));
    }

    [Fact]
    public void Chain_connector_picker_lists_jobs_and_routes_the_selection()
    {
        var page = ReadHostSource("Components/Pages/JobChains.razor");
        Assert.Contains("job-picker-grid", page, StringComparison.Ordinal);
        Assert.Contains("Vm.ChooseStageJob(job.Id)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Target_razor_surface_does_not_contain_component_state_logic()
    {
        foreach (
            var path in new[]
            {
                "Components/Routes.razor",
                "Components/Layout/MainLayout.razor",
                "Components/Layout/SettingsLayout.razor",
                "Components/Shared/ChainCanvas.razor",
                "Components/Shared/ChainPipeline.razor",
                "Components/Shared/GraphCanvas.razor",
                "Components/Shared/NotificationsBell.razor",
                "Components/Shared/ParamInput.razor",
            }
        )
        {
            var source = ReadHostSource(path);
            Assert.DoesNotContain("private string", source, StringComparison.Ordinal);
        }
    }

    private static readonly Type[] ComponentViewModelTypes =
    [
        typeof(RoutesViewModel),
        typeof(ChainCanvasViewModel),
        typeof(ChainPipelineViewModel),
        typeof(GraphCanvasViewModel),
        typeof(NotificationsViewModel),
        typeof(ParamInputViewModel),
    ];

    private static void RegisterViewModels(IServiceCollection services)
    {
        foreach (var type in ComponentViewModelTypes)
            services.AddTransient(type);
        services.AddScoped<MainLayoutViewModel>();
        services.AddScoped<SettingsLayoutViewModel>();
    }

    private static string ReadHostSource(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var host = Path.Combine(directory.FullName, "src", "PlaceContext.Host");
            if (Directory.Exists(host))
                return File.ReadAllText(Path.Combine(host, relativePath));
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
