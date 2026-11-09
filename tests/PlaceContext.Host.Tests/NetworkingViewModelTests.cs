using PlaceContext.Application.Networking;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class NetworkingViewModelTests
{
    [Fact]
    public void Canvas_rule_selection_prepares_the_test_without_guessing_a_host_in_a_subnet()
    {
        var vm = new NetworkingViewModel(null!, null!, new PortalUiState());
        vm.TestAddress = "10.20.0.10";
        vm.SelectFlowRule(new("ingress", "UDP", "10.30.0.0/24", 53, 53));
        Assert.Equal("ingress", vm.TestDirection);
        Assert.Equal("UDP", vm.TestProtocol);
        Assert.Equal(53, vm.TestPort);
        Assert.Equal("10.20.0.10", vm.TestAddress);
        vm.SelectFlowRule(new("egress", "TCP", "10.30.0.12/32", 8000, 8100));
        Assert.Equal("10.30.0.12", vm.TestAddress);
        Assert.Equal(8000, vm.TestPort);
    }

    [Fact]
    public void Group_editor_preserves_other_groups_and_cancel_does_not_change_saved_rules()
    {
        var rule = new NetworkRule("egress", "TCP", "10.20.0.0/24", 443, 443);
        var group = new NetworkSecurityGroup(Guid.NewGuid(), "Web", [rule]);
        var network = new WorkerNetwork(Guid.NewGuid(), Guid.NewGuid(), "Private", "10.20.0.0/24",
            [new(Guid.NewGuid(), "Default", []), group], 2);
        var vm = new NetworkingViewModel(null!, null!, new PortalUiState());
        vm.EditGroup(network, group.Id);
        Assert.True(vm.EditingGroup);
        Assert.False(vm.CreatingGroup);
        Assert.Equal(group.Id, vm.GroupId);
        vm.RemoveRule(rule);
        vm.Group!.Name = "Changed";
        Assert.Empty(vm.Group.Rules);
        vm.CloseEditor();
        Assert.False(vm.ShowEditor);
        Assert.Single(group.Rules);
        Assert.Equal("Web", group.Name);
        vm.EditGroup(network);
        Assert.True(vm.CreatingGroup);
        Assert.Equal(3, vm.Groups.Count);
        Assert.Empty(vm.Group!.Rules);
        Assert.Equal(group.Id, vm.Groups[1].Id);
        Assert.Single(vm.Groups[1].Rules);
    }
}
