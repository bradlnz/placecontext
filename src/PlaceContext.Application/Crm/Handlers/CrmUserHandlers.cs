using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Auth;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class CreateCrmUserHandler : ICommandHandler<CreateCrmUserCommand, CrmUserCreationResult>
{
    private readonly ICrmUserRepository _users;
    private readonly ICrmClientRepository _clients;
    private readonly ICrmClientUserAssignmentRepository _assignments;
    private readonly IClientCommunicationSender _sender;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public CreateCrmUserHandler(
        ICrmUserRepository users,
        ICrmClientRepository clients,
        ICrmClientUserAssignmentRepository assignments,
        IClientCommunicationSender sender,
        IUnitOfWork uow,
        IClock clock)
        => (_users, _clients, _assignments, _sender, _uow, _clock) =
        (users, clients, assignments, sender, uow, clock);

    public async Task<CrmUserCreationResult> HandleAsync(
        CreateCrmUserCommand command,
        CancellationToken ct = default)
    {
        var duplicate = await _users.GetByEmailAsync(command.ProjectId, command.Email, ct);
        if (duplicate is not null)
            throw new InvalidOperationException("A CRM user with this email already exists in this project.");

        var now = _clock.UtcNow;
        var joinCode = CrmUser.GenerateJoinCode();
        var user = CrmUser.Create(command.ProjectId, command.Email, command.Name, joinCode, now.AddDays(7), now);
        await _users.AddAsync(user, ct);

        Guid? assignedClientId = null;
        if (command.ClientId is { } clientId && clientId != Guid.Empty)
        {
            var client = await _clients.GetByIdAsync(clientId, ct)
                ?? throw new InvalidOperationException("The selected contact was not found.");
            if (client.ProjectId != command.ProjectId)
                throw new InvalidOperationException("The selected contact does not belong to this project.");

            assignedClientId = clientId;
            await _assignments.SetForClientAsync(command.ProjectId, clientId, [user.Id], ct);
        }

        var capabilities = await _sender.GetCapabilitiesAsync(ct);
        var emailSent = false;
        string? emailError = null;
        if (capabilities.EmailEnabled)
        {
            var recipientName = string.IsNullOrWhiteSpace(user.Name) ? user.Email : user.Name;
            try
            {
                await _sender.SendEmailAsync(
                    user.Email,
                    recipientName,
                    "Complete your CRM registration",
                    ComposeJoinMessage(recipientName, joinCode),
                    ct
                );
                emailSent = true;
            }
            catch (Exception ex)
            {
                emailError = ex.Message;
            }
        }

        await _uow.SaveChangesAsync(ct);
        return new CrmUserCreationResult(
            CrmUserMapper.ToView(user),
            joinCode,
            assignedClientId,
            emailSent,
            capabilities.EmailEnabled,
            emailError);
    }

    private static string ComposeJoinMessage(string recipientName, string joinCode)
    {
        return $"""
Hi {recipientName},

You’ve been invited to access PlaceContext CRM.
Use this onboarding code:
{joinCode}

Open this page in your browser to finish setup:
/crm/onboarding?code={joinCode}

Use this code to complete setup on the CRM user onboarding screen.
""";
    }

}

public sealed class CompleteCrmOnboardingHandler
    : ICommandHandler<CompleteCrmOnboardingCommand, CrmOnboardingResult>
{
    private readonly ICrmUserRepository _users;
    private readonly IAuthService _auth;
    private readonly IClock _clock;

    public CompleteCrmOnboardingHandler(
        ICrmUserRepository users,
        IAuthService auth,
        IClock clock)
        => (_users, _auth, _clock) = (users, auth, clock);

    public async Task<CrmOnboardingResult> HandleAsync(
        CompleteCrmOnboardingCommand command,
        CancellationToken ct = default)
    {
        var code = CrmUser.NormalizeJoinCode(command.JoinCode);
        if (!CrmUser.IsJoinCodeFormatValid(code))
            throw new ArgumentException("A join code is required.");

        var policyError = PasswordPolicy.Validate(command.Password);
        if (policyError is not null)
            throw new ArgumentException(policyError);

        var now = _clock.UtcNow;
        var user = await _users.GetByJoinCodeAsync(code, now, ct);
        if (user is null)
            throw new InvalidOperationException("This onboarding code is invalid, expired, or already used.");

        var displayName = string.IsNullOrWhiteSpace(command.DisplayName)
            ? (string.IsNullOrWhiteSpace(user.Name)
                ? user.Email.Split('@')[0]
                : user.Name)
            : command.DisplayName.Trim();

        var created = await _auth.RegisterAsync(user.Email, displayName, command.Password, UserRole.CrmUser, ct);
        if (created is null)
            throw new InvalidOperationException("An account already exists for this email. Please sign in instead.");

        if (!await _users.MarkOnboardedByJoinCodeAsync(code, created.Id, now, ct))
            throw new InvalidOperationException("This onboarding code is invalid, expired, or already used.");

        return new CrmOnboardingResult(user.ProjectId, user.Email);
    }

}

public sealed class DeleteCrmUserHandler : ICommandHandler<DeleteCrmUserCommand, bool>
{
    private readonly ICrmUserRepository _users;
    private readonly IUnitOfWork _uow;

    public DeleteCrmUserHandler(ICrmUserRepository users, IUnitOfWork uow) => (_users, _uow) = (users, uow);

    public async Task<bool> HandleAsync(DeleteCrmUserCommand command, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(command.CrmUserId, ct);
        if (user is null)
            return false;
        if (user.ProjectId != command.ProjectId)
            throw new InvalidOperationException("The CRM user does not belong to this project.");

        await _users.DeleteAsync(command.CrmUserId, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class ListCrmUsersHandler
    : IQueryHandler<ListCrmUsersQuery, IReadOnlyList<CrmUserView>>
{
    private readonly ICrmUserRepository _users;

    public ListCrmUsersHandler(ICrmUserRepository users) => _users = users;

    public async Task<IReadOnlyList<CrmUserView>> HandleAsync(
        ListCrmUsersQuery query,
        CancellationToken ct = default)
        => (await _users.ListForProjectAsync(query.ProjectId, ct))
            .Select(CrmUserMapper.ToView)
            .ToList();
}

public sealed class ListCrmClientAssignedUsersHandler
    : IQueryHandler<ListCrmClientAssignedUsersQuery, IReadOnlyList<Guid>>
{
    private readonly ICrmClientRepository _clients;
    private readonly ICrmClientUserAssignmentRepository _assignments;
    private readonly CrmUserScope _scope;

    public ListCrmClientAssignedUsersHandler(
        ICrmClientRepository clients,
        ICrmClientUserAssignmentRepository assignments,
        CrmUserScope scope)
        => (_clients, _assignments, _scope) = (clients, assignments, scope);

    public async Task<IReadOnlyList<Guid>> HandleAsync(
        ListCrmClientAssignedUsersQuery query,
        CancellationToken ct = default)
    {
        var client = await _clients.GetByIdAsync(query.ClientId, ct)
            ?? throw new InvalidOperationException($"Client {query.ClientId} not found.");
        if (client.ProjectId != query.ProjectId)
            throw new InvalidOperationException("The client and project do not match.");
        await _scope.EnsureClientAccessAsync(client.ProjectId, client.Id, ct);

        return await _assignments.ListForClientAsync(query.ProjectId, query.ClientId, ct);
    }
}

public sealed class SetCrmClientAssignedUsersHandler
    : ICommandHandler<SetCrmClientAssignedUsersCommand, IReadOnlyList<Guid>>
{
    private readonly ICrmClientRepository _clients;
    private readonly ICrmClientUserAssignmentRepository _assignments;
    private readonly ICrmUserRepository _users;
    private readonly CrmUserScope _scope;
    private readonly IUnitOfWork _uow;

    public SetCrmClientAssignedUsersHandler(
        ICrmClientRepository clients,
        ICrmClientUserAssignmentRepository assignments,
        ICrmUserRepository users,
        CrmUserScope scope,
        IUnitOfWork uow)
        => (_clients, _assignments, _users, _scope, _uow) = (clients, assignments, users, scope, uow);

    public async Task<IReadOnlyList<Guid>> HandleAsync(
        SetCrmClientAssignedUsersCommand command,
        CancellationToken ct = default)
    {
        var client = await _clients.GetByIdAsync(command.ClientId, ct)
            ?? throw new InvalidOperationException($"Client {command.ClientId} not found.");
        if (client.ProjectId != command.ProjectId)
            throw new InvalidOperationException("The client and project do not match.");
        await _scope.EnsureClientAccessAsync(client.ProjectId, client.Id, ct);

        var desired = command.CrmUserIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        if (desired.Length > 0)
        {
            var projectUserIds = (await _users.ListForProjectAsync(command.ProjectId, ct))
                .Select(user => user.Id)
                .ToHashSet();
            var invalid = desired.FirstOrDefault(userId => !projectUserIds.Contains(userId));
            if (invalid != Guid.Empty)
                throw new InvalidOperationException("One or more CRM users do not belong to this project.");
        }

        await _assignments.SetForClientAsync(command.ProjectId, command.ClientId, desired, ct);
        await _uow.SaveChangesAsync(ct);
        return desired;
    }
}

internal static class CrmUserMapper
{
    public static CrmUserView ToView(CrmUser user) => new(
        user.Id,
        user.ProjectId,
        user.Name,
        user.Email,
        user.CreatedAt,
        user.UpdatedAt);
}
