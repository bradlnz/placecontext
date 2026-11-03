using Microsoft.Extensions.Hosting;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Architecture.Tests;

public sealed class BackgroundServiceNamingTests
{
    [Fact]
    public void Hosted_service_names_describe_their_framework_role()
    {
        foreach (var type in typeof(AppDbContext).Assembly.GetTypes().Where(type => !type.IsAbstract))
        {
            if (typeof(BackgroundService).IsAssignableFrom(type))
            {
                Assert.EndsWith("BackgroundService", type.Name);
                continue;
            }
            if (typeof(IHostedService).IsAssignableFrom(type))
            {
                Assert.EndsWith("HostedService", type.Name);
            }
        }
    }
}
