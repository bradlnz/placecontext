using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class ConfigureCrmClientPortalHandler
    : ICommandHandler<ConfigureCrmClientPortalCommand, CrmClientView>
{
    private readonly ICrmClientRepository _clients;
    private readonly ICurrentTenant _tenant;
    private readonly ICrmUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ICustomerPortalProvisioner _provisioner;

    public ConfigureCrmClientPortalHandler(
        ICrmClientRepository clients,
        ICurrentTenant tenant,
        ICrmUnitOfWork uow,
        IClock clock,
        ICustomerPortalProvisioner provisioner)
        => (_clients, _tenant, _uow, _clock, _provisioner) = (clients, tenant, uow, clock, provisioner);

    public async Task<CrmClientView> HandleAsync(
        ConfigureCrmClientPortalCommand command, CancellationToken ct = default)
    {
        var client = await _clients.GetByIdAsync(command.ClientId, ct)
            ?? throw new InvalidOperationException($"Client {command.ClientId} not found.");
        if (command.Enabled)
        {
            if (!_tenant.IsResolved)
                throw new InvalidOperationException("Cannot provision a customer portal outside a tenant request context.");

            if (string.IsNullOrWhiteSpace(command.Slug))
                throw new ArgumentException("Customer portal slug is required when enabling a portal.", nameof(command.Slug));

            await _provisioner.ProvisionAsync(
                _tenant.TenantId,
                command.Slug.Trim(),
                command.Domain?.Trim(),
                command.PortalBrandName,
                command.PortalBrandLogoUrl,
                command.DefaultPortalUserName,
                command.DefaultPortalUserEmail,
                command.DefaultPortalUserPassword,
                ct);
        }

            client.ConfigurePortal(
                command.Enabled,
                command.Slug,
                command.Domain,
                command.PortalBrandName,
                command.PortalBrandLogoUrl,
                _clock.UtcNow);
            await _clients.UpdateAsync(client, ct);
            await _uow.SaveChangesAsync(ct);
            return CrmClientMapper.ToView(client);
    }
}
