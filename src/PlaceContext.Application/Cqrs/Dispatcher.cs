using PlaceContext.Application.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace PlaceContext.Application.Cqrs;

/// <summary>
/// Reflection-light dispatcher. For a command of runtime type <c>C : ICommand&lt;R&gt;</c> it resolves
/// <c>ICommandHandler&lt;C, R&gt;</c> from a fresh DI scope and invokes it. Handlers are registered
/// per-type in <see cref="DependencyInjection"/>.
///
/// Each dispatch opens its own scope so the dispatch boundary *is* the unit-of-work boundary: every
/// command/query gets an isolated scoped <c>DbContext</c>. This is what makes the dispatcher safe to
/// call concurrently — e.g. the Blazor portal renders many components in one circuit (one DI scope),
/// and without per-dispatch isolation they would share, and collide on, a single <c>DbContext</c>.
/// Handlers return detached DTO views, so disposing the scope once the handler completes is safe.
///
/// Commands that implement <see cref="IRequiresPermission"/> are also authorization-checked here,
/// against the caller's effective permissions, before the handler ever runs — see that interface for
/// why this single choke point matters more than any UI-level hiding of the triggering control.
/// </summary>
public sealed class Dispatcher : IDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;

    public Dispatcher(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await using var scope = _scopeFactory.CreateAsyncScope();
        if (command is IRequiresPermission gated)
        {
            var permissions = scope.ServiceProvider.GetRequiredService<IPermissionService>();
            if (!await permissions.HasAsync(gated.RequiredPermission, ct))
                throw new UnauthorizedAccessException($"Missing permission '{gated.RequiredPermission}'.");
        }
        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResult));
        var handler = scope.ServiceProvider.GetRequiredService(handlerType);
        return await Invoke<TResult>(handler, command, ct);
    }

    public async Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handlerType = typeof(IQueryHandler<,>).MakeGenericType(query.GetType(), typeof(TResult));
        var handler = scope.ServiceProvider.GetRequiredService(handlerType);
        return await Invoke<TResult>(handler, query, ct);
    }

    private static Task<TResult> Invoke<TResult>(object handler, object message, CancellationToken ct)
    {
        var method = handler.GetType().GetMethod("HandleAsync")
                     ?? throw new InvalidOperationException($"Handler {handler.GetType().Name} has no HandleAsync.");
        return (Task<TResult>)method.Invoke(handler, new[] { message, ct })!;
    }
}
