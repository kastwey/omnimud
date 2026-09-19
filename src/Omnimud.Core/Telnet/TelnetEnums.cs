namespace Omnimud.Core.Telnet;

public enum TelnetVerb : byte
{
    Will = 251,
    Wont = 252,
    Do = 253,
    Dont = 254
}

public enum TelnetCommand : byte
{
    Echo = 1,
    SuppressGoAhead = 3,
    TerminalType = 24,
    EndOfRecord = 25,
    WindowSize = 31,
    Msp = 90,
    Gmcp = 201,
    Iac = 255
}
