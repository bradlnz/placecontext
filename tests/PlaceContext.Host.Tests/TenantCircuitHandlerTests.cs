using PlaceContext.Application.Tenancy;
using PlaceContext.Host.Tenancy;

namespace PlaceContext.Host.Tests;

public sealed class TenantCircuitHandlerTests
{
    [Fact]
    public async Task New_circuit_captures_request_tenant_before_membership_validation()
    {
        var tenant = new TenantInfo(Guid.NewGuid(), "test", "Test", "UTC");
        CurrentTenant.Set(tenant);
        try
        {
            var holder = new TenantHolder();
            var handler = new TenantCircuitHandler(holder);
            Assert.Equal(tenant, holder.Tenant);
            CurrentTenant.Clear();
            var invoked = false;
            await handler.CreateInboundActivityHandler(_ =>
            {
                Assert.Equal(tenant, CurrentTenant.Current);
                invoked = true;
                return Task.CompletedTask;
            })(null!);
            Assert.True(invoked);
        }
        finally
        {
            CurrentTenant.Clear();
        }
    }

    [Fact]
    public async Task Existing_circuit_never_adopts_another_requests_tenant()
    {
        var tenant = new TenantInfo(Guid.NewGuid(), "original", "Original", "UTC");
        CurrentTenant.Set(new(Guid.NewGuid(), "other", "Other", "UTC"));
        try
        {
            var holder = new TenantHolder { Tenant = tenant };
            var handler = new TenantCircuitHandler(holder);
            await handler.CreateInboundActivityHandler(_ =>
            {
                Assert.Equal(tenant, CurrentTenant.Current);
                return Task.CompletedTask;
            })(null!);
            Assert.Equal(tenant, holder.Tenant);
        }
        finally
        {
            CurrentTenant.Clear();
        }
    }

    [Fact]
    public async Task Unresolved_circuit_cannot_inherit_a_later_requests_tenant()
    {
        CurrentTenant.Clear();
        var handler = new TenantCircuitHandler(new());
        CurrentTenant.Set(new(Guid.NewGuid(), "other", "Other", "UTC"));
        try
        {
            await handler.CreateInboundActivityHandler(_ =>
            {
                Assert.Null(CurrentTenant.Current);
                return Task.CompletedTask;
            })(null!);
        }
        finally
        {
            CurrentTenant.Clear();
        }
    }
}
