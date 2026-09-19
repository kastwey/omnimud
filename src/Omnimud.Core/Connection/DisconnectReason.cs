namespace Omnimud.Core.Connection;

public enum DisconnectReason
{
    UserRequested,
    ServerClosed,
    Error,
    Timeout
}
