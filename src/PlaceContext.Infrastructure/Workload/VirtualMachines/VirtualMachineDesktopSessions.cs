using System.Collections.Concurrent;
using System.Net.Sockets;
using PlaceContext.Application.Workload.VirtualMachines;
namespace PlaceContext.Infrastructure.Workload;

// shortcut: live consoles are local to the execution host; route portal traffic to that host until cross-host console routing is added.
public sealed class VirtualMachineDesktopSessions
{
    private readonly ConcurrentDictionary<(Guid Tenant, Guid Run, int Step), Session> _sessions = new();
    internal Session Register(Guid tenant, Guid owner, VirtualMachineRunContext run, string socket, bool pause,
        Func<string, byte[], CancellationToken, Task> save)
    {
        var session = new Session(owner, run.ProjectId, socket, pause, save);
        if (!_sessions.TryAdd((tenant, run.RunId, run.Step), session))
        {
            throw new InvalidOperationException("A desktop already exists for this step.");
        }
        return session;
    }
    internal Session? Find(Guid tenant, Guid user, VirtualMachineRunContext run) =>
        _sessions.TryGetValue((tenant, run.RunId, run.Step), out var session) && session.Owner == user && session.Project == run.ProjectId ? session : null;
    internal void Remove(Guid tenant, VirtualMachineRunContext run)
    {
        if (_sessions.TryRemove((tenant, run.RunId, run.Step), out var session))
        {
            session.Dispose();
        }
    }

    internal sealed class Session(Guid owner, Guid project, string socket, bool pause,
        Func<string, byte[], CancellationToken, Task> save) : IDisposable
    {
        internal Guid Owner { get; } = owner;
        internal Guid Project { get; } = project;
        private readonly object _sync = new();
        private readonly SemaphoreSlim _changed = new(0, 1);
        private bool _pause = pause, _closed, _resumed;
        private string _state = "Starting";
        private string? _name, _message;
        private TaskCompletionSource? _saved;
        private Socket? _console;
        internal VirtualMachineDesktopStatus Status
        {
            get
            {
                lock (_sync)
                {
                    return new(_state, _message);
                }
            }
        }
        private void Signal()
        {
            if (_changed.CurrentCount == 0)
            {
                _changed.Release();
            }
        }
        internal void TakeControl()
        {
            lock (_sync)
            {
                if (_closed)
                {
                    throw new InvalidOperationException("The desktop has ended.");
                }
                _pause = true;
                if (_state == "Running")
                {
                    _state = "Pausing";
                }
                Signal();
            }
        }
        internal void Resume()
        {
            lock (_sync)
            {
                if (_state != "Human" || _saved is not null)
                {
                    throw new InvalidOperationException("Wait until the desktop is ready before resuming.");
                }
                Disconnect();
                _pause = false;
                _resumed = true;
                _state = "Running";
                _message = null;
                Signal();
            }
        }
        internal Task SaveAsync(string name, CancellationToken ct)
        {
            name = name.Trim();
            if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
            {
                throw new ArgumentException("Name the login using 1–80 characters.");
            }
            lock (_sync)
            {
                if (_state != "Human" || _saved is not null)
                {
                    throw new InvalidOperationException("Take control before saving a login.");
                }
                _name = name;
                _saved = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Signal();
                return _saved.Task.WaitAsync(ct);
            }
        }
        internal async Task<string> CheckpointAsync(CancellationToken ct)
        {
            while (true)
            {
                lock (_sync)
                {
                    if (_closed)
                    {
                        throw new OperationCanceledException("Desktop ended.");
                    }
                    if (!_pause)
                    {
                        _state = "Running";
                        var result = _resumed ? "resumed" : "continue";
                        _resumed = false;
                        return result;
                    }
                    if (_name is not null)
                    {
                        _state = "Saving";
                        Disconnect();
                        return "save";
                    }
                    _state = "Human";
                }
                await _changed.WaitAsync(ct);
            }
        }
        internal async Task SaveProfileAsync(byte[]? archive, CancellationToken ct)
        {
            string name;
            TaskCompletionSource completion;
            lock (_sync)
            {
                name = _name ?? throw new InvalidOperationException("No login save was requested.");
                completion = _saved!;
            }
            Exception? failure = null;
            try
            {
                if (archive is null)
                {
                    throw new InvalidOperationException("Could not snapshot the browser. Try saving again.");
                }
                await save(name, archive, ct);
                lock (_sync)
                {
                    _message = "Login saved privately. Select it in the QEMU step for future runs.";
                }
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    _message = "Could not save the login. Check the session size and try again.";
                }
                failure = ex;
            }
            finally
            {
                lock (_sync)
                {
                    _name = null;
                    _saved = null;
                    if (!_closed)
                    {
                        _state = "Human";
                    }
                }
            }
            if (failure is null)
            {
                completion.TrySetResult();
                return;
            }
            completion.TrySetException(failure);
        }
        internal async Task<Stream> ConnectAsync(CancellationToken ct, bool viewOnly = false)
        {
            Socket connection;
            lock (_sync)
            {
                if (_closed || (!viewOnly && (_state != "Human" || _saved is not null)))
                {
                    throw new InvalidOperationException("Take control and wait for the agent to pause.");
                }
                Disconnect();
                connection = _console = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            }
            try
            {
                await connection.ConnectAsync(new UnixDomainSocketEndPoint(socket), ct);
                return new NetworkStream(connection, ownsSocket: true);
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }
        private void Disconnect()
        {
            _console?.Dispose();
            _console = null;
        }
        public void Dispose()
        {
            lock (_sync)
            {
                _closed = true;
                _state = "Ended";
                Disconnect();
                _saved?.TrySetException(new InvalidOperationException("The desktop ended before saving."));
                Signal();
            }
        }
    }
}
