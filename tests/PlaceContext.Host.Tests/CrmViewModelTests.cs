using PlaceContext.Application.Features;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.ViewModels.Crm;

namespace PlaceContext.Host.Tests;

public sealed class CrmViewModelTests
{
    [Fact]
    public void Customer_detail_yields_primary_position_to_action_panels()
    {
        var viewModel = new CrmViewModel(null!, null!, null!, null!, null!, null!, null!)
        {
            Selected = new CrmClientView(
                Guid.NewGuid(), Guid.NewGuid(), "Acme", null, null, null, "Lead", null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        };

        Assert.True(viewModel.ShowCustomerDetailPanel);

        viewModel.ShowEditor = true;
        Assert.False(viewModel.ShowCustomerDetailPanel);
        viewModel.ShowEditor = false;

        viewModel.NotesMetadataOpen = true;
        Assert.False(viewModel.ShowCustomerDetailPanel);
    }

    [Fact]
    public void Stage_presentation_is_centralized_in_the_crm_view_model()
    {
        Assert.Equal("At risk", CrmViewModel.StageLabel(CustomerLifecycleStage.AtRisk));
        Assert.Equal(
            "Needs attention",
            CrmViewModel.StageDescription(CustomerLifecycleStage.AtRisk)
        );
        Assert.Equal("stage-qualified", CrmViewModel.StageClass(CustomerLifecycleStage.Qualified));
    }

    [Fact]
    public void Crm_catalog_maps_legacy_keys_to_typed_ui_state()
    {
        Assert.Equal(
            CrmSection.Opportunities,
            CrmPresentationCatalog.ParseSection("opportunities")
        );
        Assert.Equal(CrmDetailTab.Communications, CrmPresentationCatalog.ParseDetailTab("comms"));
        Assert.Equal(
            CrmArtifactSource.Automation,
            CrmPresentationCatalog.ParseArtifactSource("automation")
        );
        Assert.Equal(CrmCommunicationChannel.Sms, CrmPresentationCatalog.ParseChannel("Sms"));
        Assert.Equal(CrmCommunicationStatus.Failed, CrmPresentationCatalog.ParseStatus("Failed"));
    }

    [Fact]
    public void Crm_page_uses_only_view_model_and_lifecycle_glue()
    {
        var page = ReadHostSource("Components/Pages/Crm.razor");

        Assert.Contains("@inject CrmViewModel Vm", page, StringComparison.Ordinal);
        Assert.Contains("Vm.Attach", page, StringComparison.Ordinal);
        Assert.Contains("Vm.Detach", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject IPlaceContextService", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@inject IJSRuntime", page, StringComparison.Ordinal);
        Assert.Contains("Vm.ProjectId = ProjectId", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Crm_page_routes_calendar_deletion_through_the_view_model()
    {
        var page = ReadHostSource("Components/Pages/Crm.razor");

        Assert.Contains(
            "@onclick=\"@(() => Vm.DeleteCalendar(calendar.Id))\"",
            page,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "@onclick=\"@(() => DeleteCalendar(calendar.Id))\"",
            page,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("Contact prefers email. {\"source\": \"referral\"} Follow up next week.", "{\"source\": \"referral\"}")]
    [InlineData("Started as [1, 2, 3] array.", "[1, 2, 3]")]
    [InlineData("No json here", null)]
    [InlineData("", null)]
    public void ExtractNotesMetadata_pulls_first_valid_json_block(string notes, string? expectedJson)
    {
        var extracted = CrmViewModel.ExtractNotesMetadata(notes);
        if (expectedJson is null)
        {
            Assert.Null(extracted);
        }
        else
        {
            Assert.NotNull(extracted);
            Assert.Contains(expectedJson, extracted, StringComparison.Ordinal);
        }
    }

    private static string ReadHostSource(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var host = Path.Combine(directory.FullName, "src", "PlaceContext.Host");
            if (Directory.Exists(host))
            {
                return File.ReadAllText(Path.Combine(host, relativePath));
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
