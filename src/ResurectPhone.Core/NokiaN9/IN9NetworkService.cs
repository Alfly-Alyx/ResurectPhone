namespace ResurectPhone.Core.NokiaN9;

public sealed record N9WifiProfile(string Id, string Name, string Security, bool? Automatic, bool Forced, bool Connected)
{
    public string DisplayName => Name + (Connected ? " — connecté" : "") +
        (Forced ? " — reconnexion forcée" : Automatic == true ? " — automatique" : " — manuel");
}
public sealed record N9VisibleWifi(string Name, int Signal);
public sealed record N9NetworkSnapshot(IReadOnlyList<N9WifiProfile> Profiles, IReadOnlyList<N9VisibleWifi> Available,
    string Address, bool RadioEnabled, bool PowerSaving, int? SearchInterval, string ReconnectState, bool WifiSdkPasswordless);

public interface IN9NetworkService
{
    bool IsUsbTransport { get; }
    Task<N9NetworkSnapshot> ReadNetworksAsync(bool scan = false, CancellationToken cancellationToken = default);
    Task<N9MaintenanceReport> SetWifiAutomaticAsync(string profileId, bool enabled, bool force,
        char[]? administratorPassword = null, CancellationToken cancellationToken = default);
    Task<N9MaintenanceReport> ConnectWifiAsync(string profileId, CancellationToken cancellationToken = default);
    Task<N9MaintenanceReport> DisconnectWifiAsync(string profileId, char[]? administratorPassword = null,
        CancellationToken cancellationToken = default);
    Task<N9MaintenanceReport> ConfigureSdkAccessAsync(bool includeWifi, char[]? administratorPassword = null,
        CancellationToken cancellationToken = default);
}
