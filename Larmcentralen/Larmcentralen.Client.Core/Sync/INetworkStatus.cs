namespace Larmcentralen.Client.Core.Sync;

/// <summary>Whether the device has a network at all. The MAUI app plugs in the platform's connectivity.</summary>
public interface INetworkStatus
{
    bool HasNetwork { get; }
    event EventHandler? Changed;
}

/// <summary>Assumes a network is always there. Used in tests and in hosts without connectivity events.</summary>
public sealed class AlwaysOnlineNetworkStatus : INetworkStatus
{
    public bool HasNetwork => true;
    public event EventHandler? Changed { add { } remove { } }
}
