namespace AgentsPromptsSkills.Mobile.Services;

/// <summary>MAUI connectivity wrapper — monitors network state.</summary>
public class ConnectivityService
{
    public bool IsConnected => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    public event Action? ConnectivityChanged;

    public ConnectivityService()
        => Connectivity.Current.ConnectivityChanged += OnChanged;

    private void OnChanged(object? sender, ConnectivityChangedEventArgs e)
        => ConnectivityChanged?.Invoke();

    ~ConnectivityService()
        => Connectivity.Current.ConnectivityChanged -= OnChanged;
}
