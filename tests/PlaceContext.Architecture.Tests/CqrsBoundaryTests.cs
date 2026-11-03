using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Shared;
using PlaceContext.Application;
namespace PlaceContext.Architecture.Tests;

public sealed class CqrsBoundaryTests
{
    [Fact]
    public void Each_request_is_either_a_command_or_a_query_with_one_scoped_handler()
    {
        var services = new ServiceCollection().AddApplication();
        foreach (var request in typeof(IDispatcher).Assembly.GetTypes().Where(type => !type.IsAbstract))
        {
            var contracts = request.GetInterfaces().Where(type => type.IsGenericType
                && (type.GetGenericTypeDefinition() == typeof(ICommand<>)
                    || type.GetGenericTypeDefinition() == typeof(IQuery<>))).ToArray();
            if (contracts.Length == 0)
            {
                continue;
            }
            var contract = Assert.Single(contracts);
            var handler = (contract.GetGenericTypeDefinition() == typeof(ICommand<>)
                ? typeof(ICommandHandler<,>) : typeof(IQueryHandler<,>))
                .MakeGenericType(request, contract.GenericTypeArguments[0]);
            var registration = Assert.Single(services, service => service.ServiceType == handler);
            Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
            Assert.NotNull(registration.ImplementationType);
            Assert.True(handler.IsAssignableFrom(registration.ImplementationType), request.FullName);
        }
    }

    [Fact]
    public void Query_handlers_do_not_own_a_write_commit_boundary()
    {
        var handlers = typeof(IDispatcher).Assembly.GetTypes().Where(type => !type.IsAbstract
            && type.GetInterfaces().Any(contract => contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)));
        foreach (var handler in handlers)
        {
            Assert.DoesNotContain(handler.GetConstructors().SelectMany(ctor => ctor.GetParameters()),
                        parameter => typeof(IUnitOfWork).IsAssignableFrom(parameter.ParameterType));
        }
    }
}
