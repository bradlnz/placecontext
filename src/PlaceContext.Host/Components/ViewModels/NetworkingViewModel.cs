using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class NetworkingViewModel(IWorkerNetworkService networks, PlaceContextService service, PortalUiState ui,
    NetworkFlowService? flowService = null) : PageViewModel
{
    public Guid ProjectId { get; private set; }
    public WorkerNetworkState State { get; private set; } = new([], []);
    public IReadOnlyList<JobView> Jobs { get; private set; } = [];
    public bool Busy { get; private set; }
    public string? Error { get; private set; }
    public string? FormError { get; private set; }
    public bool ShowEditor { get; private set; }
    public bool ShowAssignment { get; private set; }
    public bool EditingGroup { get; private set; }
    public bool CreatingGroup { get; private set; }
    public WorkerNetwork? Deleting { get; private set; }
    public Guid? SelectedGroupId { get; private set; }
    public string RuleTab { get; private set; } = "ingress";
    public WorkerNetwork? SelectedGroupNetwork => State.Networks.FirstOrDefault(network => network.Groups.Any(group => group.Id == SelectedGroupId));
    public NetworkSecurityGroup? SelectedGroup => SelectedGroupNetwork?.Groups.FirstOrDefault(group => group.Id == SelectedGroupId);
    public void SelectGroup(Guid id) { if (SelectedGroupId != id) ClearFlowResult(); SelectedGroupId = id; }
    public void SelectFlowNetwork(string? id)
    {
        var group = State.Networks.FirstOrDefault(network => network.Id.ToString() == id)?.Groups.FirstOrDefault();
        if (group is not null) SelectGroup(group.Id);
    }
    public void SelectFlowGroup(string? id) { if (Guid.TryParse(id, out var group)) SelectGroup(group); }
    public IEnumerable<JobView> FlowJobs => Jobs.Where(job => State.Assignments.Any(assignment => assignment.JobId == job.Id && assignment.GroupId == SelectedGroupId));
    public string TestDirection { get; set; } = "egress";
    public string TestProtocol { get; set; } = "TCP";
    public string TestAddress { get; set; } = "";
    public int TestPort { get; set; } = 443;
    public bool TestingFlow { get; private set; }
    public NetworkFlowResult? FlowResult { get; private set; }
    public string? FlowError { get; private set; }
    private CancellationTokenSource? _flowCancellation;

    public void ClearFlowResult() { CancelFlow(); FlowResult = null; FlowError = null; }
    public void CancelFlow() => _flowCancellation?.Cancel();
    public void SelectFlowRule(NetworkRule rule)
    {
        ClearFlowResult();
        TestDirection = rule.Direction;
        TestProtocol = rule.Protocol;
        TestPort = rule.FromPort;
        if (rule.Cidr.EndsWith("/32", StringComparison.Ordinal)) TestAddress = rule.Cidr[..^3];
    }

    public async Task TestFlowAsync(bool live)
    {
        if (TestingFlow || SelectedGroupNetwork is not { } network || SelectedGroupId is not { } groupId) return;
        var version = _version;
        using var cancel = new CancellationTokenSource();
        _flowCancellation = cancel;
        TestingFlow = true;
        FlowResult = null;
        FlowError = null;
        try
        {
            var result = await (flowService ?? throw new InvalidOperationException("Network tests are unavailable."))
                .CheckAsync(ProjectId, network.Id, groupId, TestDirection, TestProtocol, TestAddress.Trim(), TestPort, live, cancel.Token);
            if (version == _version && !cancel.IsCancellationRequested) FlowResult = result;
        }
        catch (OperationCanceledException) { if (version == _version) FlowError = "Network test cancelled."; }
        catch (Exception ex) { if (version == _version && !cancel.IsCancellationRequested) FlowError = ex.Message; }
        finally { _flowCancellation = null; TestingFlow = false; NotifyStateChanged(); }
    }
    public void SelectRuleTab(string tab) { RuleTab = tab == "egress" ? "egress" : "ingress"; }
    public void EditSelectedGroup()
    {
        if (SelectedGroupNetwork is { } network && SelectedGroupId is { } id) EditGroup(network, id);
    }
    public string Tab { get; private set; } = "vpcs";
    public string Search { get; set; } = "";
    public string NetworkFilter { get; set; } = "";
    public string TabUrl(string tab) => $"/project/{ProjectId}/networking?tab={tab}";
    public IEnumerable<WorkerNetwork> VisibleNetworks => State.Networks.Where(network => Matches(network.Name, network.Cidr, network.Id.ToString()));
    public IEnumerable<(WorkerNetwork Network, NetworkSecurityGroup Group)> VisibleGroups => State.Networks
        .Where(network => NetworkFilter.Length == 0 || network.Id.ToString() == NetworkFilter)
        .SelectMany(network => network.Groups.Select(group => (Network: network, Group: group)))
        .Where(item => Matches(item.Group.Name, item.Network.Name, item.Group.Id.ToString()));
    public IEnumerable<JobView> VisibleJobs => Jobs.Where(job => Matches(job.Name, AssignmentLabel(job.Id)));
    private bool Matches(params string[] fields) => fields.Any(field => field.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase));
    public Guid EditId { get; private set; }
    public long Version { get; private set; }
    public string Name { get; set; } = "";
    public string Cidr { get; set; } = "10.20.0.0/24";
    public List<GroupEdit> Groups { get; private set; } = [];
    public Guid GroupId { get; set; }
    public GroupEdit? Group => Groups.FirstOrDefault(group => group.Id == GroupId);
    public string Direction { get; set; } = "egress";
    public string Protocol { get; set; } = "TCP";
    public string RuleCidr { get; set; } = "10.20.0.0/24";
    public int FromPort { get; set; } = 443;
    public int ToPort { get; set; } = 443;
    public string JobId { get; set; } = "";
    public string AssignedNetwork { get; set; } = "";
    public string AssignedGroup { get; set; } = "";
    public IReadOnlyList<NetworkSecurityGroup> AssignmentGroups => State.Networks.FirstOrDefault(network => network.Id.ToString() == AssignedNetwork)?.Groups ?? [];
    private int _version;

    public sealed class GroupEdit
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string Name { get; set; } = "New group";
        public List<NetworkRule> Rules { get; init; } = [];
    }

    public async Task LoadAsync(Guid projectId, string? tab = null)
    {
        ClearFlowResult();
        Tab = tab is "security-groups" or "assignments" or "flow" ? tab : "vpcs";
        if (ProjectId != projectId) { SelectedGroupId = null; Search = NetworkFilter = ""; }
        var version = ++_version;
        ProjectId = projectId;
        ShowEditor = ShowAssignment = false;
        Deleting = null;
        Busy = true;
        Error = null;
        try
        {
            var state = await networks.LoadAsync(projectId);
            var jobs = await service.ListJobsAsync(projectId);
            if (version != _version) return;
            State = state;
            if (SelectedGroup is null) SelectedGroupId = State.Networks.SelectMany(network => network.Groups).FirstOrDefault()?.Id;
            Jobs = jobs;
            ui.Set("Networking", "Private worker networks and job security groups");
        }
        catch (Exception ex) { if (version == _version) Error = ex.Message; }
        finally { if (version == _version) { Busy = false; NotifyStateChanged(); } }
    }

    public void Edit(WorkerNetwork? network = null)
    {
        if (Busy) return;
        EditId = network?.Id ?? Guid.NewGuid();
        Version = network?.Version ?? 0;
        Name = network?.Name ?? "";
        Cidr = network?.Cidr ?? "10.20.0.0/24";
        Groups = network?.Groups.Select(group => new GroupEdit { Id = group.Id, Name = group.Name, Rules = group.Rules.ToList() }).ToList()
            ?? [new GroupEdit { Name = "Default" }];
        GroupId = Groups[0].Id;
        RuleCidr = Cidr;
        FormError = null;
        ShowEditor = true;
        ShowAssignment = false;
        EditingGroup = CreatingGroup = false;
    }

    public void EditGroup(WorkerNetwork network, Guid? groupId = null)
    {
        if (Busy) return;
        Edit(network);
        EditingGroup = true;
        CreatingGroup = !groupId.HasValue;
        if (groupId.HasValue) GroupId = groupId.Value;
        else AddGroup();
    }

    public void CreateGroup()
    {
        var network = State.Networks.FirstOrDefault(item => item.Groups.Count < 20 && (NetworkFilter.Length == 0 || item.Id.ToString() == NetworkFilter));
        if (network is not null) EditGroup(network);
    }

    public void ChangeGroupNetwork(string? id)
    {
        var network = State.Networks.FirstOrDefault(item => item.Id.ToString() == id && item.Groups.Count < 20);
        if (network is not null) EditGroup(network);
    }

    public Task RefreshAsync() => LoadAsync(ProjectId, Tab);

    public void RequestDelete(WorkerNetwork network) { if (!Busy) { FormError = null; Deleting = network; } }
    public void CancelDelete() { if (!Busy) Deleting = null; }
    public async Task DeleteAsync()
    {
        if (Busy || Deleting is null) return;
        Busy = true;
        FormError = null;
        try
        {
            await networks.DeleteAsync(ProjectId, Deleting.Id, Deleting.Version);
            Deleting = null;
        }
        catch (Exception ex) { FormError = ex.Message; }
        finally { Busy = false; }
        if (Deleting is null) await RefreshAsync();
    }

    public void AddGroup()
    {
        if (Busy || Groups.Count >= 20) return;
        var group = new GroupEdit();
        Groups.Add(group);
        GroupId = group.Id;
    }

    public void AddRule()
    {
        if (Busy || Group is null) return;
        FormError = null;
        try
        {
            var rule = new NetworkRule(Direction, Protocol, RuleCidr.Trim(), FromPort, ToPort);
            rule.Validate();
            if (Group.Rules.Count >= 100) throw new ArgumentException("A group supports at most 100 rules.");
            Group.Rules.Add(rule);
        }
        catch (ArgumentException ex) { FormError = ex.Message; }
    }

    public void RemoveRule(NetworkRule rule) { if (!Busy) Group?.Rules.Remove(rule); }
    public void CloseEditor() { if (!Busy) ShowEditor = ShowAssignment = false; }
    public void DisposePage() { _version++; CancelFlow(); ShowEditor = ShowAssignment = false; Detach(); }

    public async Task SaveAsync()
    {
        if (Busy) return;
        Busy = true;
        FormError = null;
        try
        {
            await networks.SaveAsync(new(EditId, ProjectId, Name.Trim(), Cidr.Trim(),
                Groups.Select(group => new NetworkSecurityGroup(group.Id, group.Name.Trim(), group.Rules.ToArray())).ToArray(), Version));
            if (EditingGroup) SelectedGroupId = GroupId;
            ShowEditor = false;
        }
        catch (Exception ex) { FormError = ex.Message; }
        finally { Busy = false; }
        if (!ShowEditor) await RefreshAsync();
    }

    public string AssignmentLabel(Guid jobId)
    {
        var assignment = State.Assignments.FirstOrDefault(item => item.JobId == jobId);
        var network = State.Networks.FirstOrDefault(item => item.Id == assignment?.NetworkId);
        return network is null ? "Job network setting" : $"{network.Name} / {network.Groups.First(group => group.Id == assignment!.GroupId).Name}";
    }

    public void Assign(JobView job)
    {
        if (Busy) return;
        var current = State.Assignments.FirstOrDefault(item => item.JobId == job.Id);
        JobId = job.Id.ToString();
        AssignedNetwork = current?.NetworkId.ToString() ?? "";
        AssignedGroup = current?.GroupId.ToString() ?? "";
        FormError = null;
        ShowAssignment = true;
    }

    public async Task SaveAssignmentAsync()
    {
        if (Busy) return;
        Busy = true;
        FormError = null;
        try
        {
            await networks.AssignAsync(ProjectId, Guid.Parse(JobId),
                AssignedNetwork.Length == 0 ? null : Guid.Parse(AssignedNetwork),
                AssignedGroup.Length == 0 ? null : Guid.Parse(AssignedGroup));
            ShowAssignment = false;
        }
        catch (Exception ex) { FormError = ex.Message; }
        finally { Busy = false; }
        if (!ShowAssignment) await RefreshAsync();
    }
}
