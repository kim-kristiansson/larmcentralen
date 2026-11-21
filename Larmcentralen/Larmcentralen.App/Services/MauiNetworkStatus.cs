using Larmcentralen.Client.Core.Sync;

namespace Larmcentralen.App.Services;

/// <summary>Uses the platform's connectivity events so the app re-syncs as soon as the network is back.</summary>
public sealed class MauiNetworkStatus : INetworkStatus, IDisposable
{
    private readonly IConnectivity _connectivity = Connectivity.Current;

    public MauiNetworkStatus()
    {
        _connectivity.ConnectivityChanged += OnChanged;
    }

    public bool HasNetwork => _connectivity.NetworkAccess is NetworkAccess.Internet or NetworkAccess.ConstrainedInternet or NetworkAccess.Local;

    public event EventHandler? Changed;

    private void OnChanged(object? sender, ConnectivityChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose() => _connectivity.ConnectivityChanged -= OnChanged;
}
