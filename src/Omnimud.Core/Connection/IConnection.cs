namespace Omnimud.Core.Connection;

public interface IConnection : IAsyncDisposable
{
    ConnectionState State { get; }

    event Func<ReadOnlyMemory<byte>, Task>? DataReceived;
    event Func<DisconnectReason, Task>? Disconnected;

    Task ConnectAsync(ConnectionConfig config, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    Task SendAsync(string command, CancellationToken ct = default);
    Task SendRawAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);
}
