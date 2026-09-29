using System.Windows;
using ResurectPhone.Core.NokiaN9;

namespace ResurectPhone.App.Dialogs;

public partial class N9NetworkDialog : Window
{
    private readonly IN9NetworkService _network;
    private readonly IN9MaintenanceService _maintenance;
    private readonly Func<char[]?> _requestPassword;
    private bool _busy;

    public N9NetworkDialog(IN9NetworkService network, IN9MaintenanceService maintenance, Func<char[]?> requestPassword)
    {
        InitializeComponent();
        _network = network; _maintenance = maintenance; _requestPassword = requestPassword;
        var area = SystemParameters.WorkArea;
        MaxHeight = area.Height; MaxWidth = area.Width;
        Height = Math.Min(Height, area.Height); Width = Math.Min(Width, area.Width);
        Loaded += async (_, _) => await RunAsync(() => RefreshAsync());
        Closing += (_, e) => { if (_busy) e.Cancel = true; };
    }

    private async Task RefreshAsync(bool scan = false)
    {
        var selected = (Profiles.SelectedItem as N9WifiProfile)?.Id;
        var snapshot = await _network.ReadNetworksAsync(scan);
        Profiles.ItemsSource = snapshot.Profiles;
        Profiles.SelectedItem = snapshot.Profiles.FirstOrDefault(p => p.Id == selected) ??
            snapshot.Profiles.FirstOrDefault(p => p.Connected) ?? snapshot.Profiles.FirstOrDefault();
        ConnectionText.Text = $"Liaison avec le PC : {(_network.IsUsbTransport ? "USB" : "Wi-Fi")} · Adresse Wi-Fi : {snapshot.Address}\n" +
            $"Wi-Fi {(snapshot.RadioEnabled ? "allumé" : "éteint ou mode avion")} · Recherche automatique : {snapshot.SearchInterval?.ToString() ?? "inconnue"} s" +
            (snapshot.PowerSaving ? "\nLe mode économie d’énergie peut limiter la connexion automatique d’Harmattan." : "");
        if (snapshot.Profiles.Any(p => p.Forced))
            ConnectionText.Text += "\nReconnexion forcée : " + (snapshot.ReconnectState switch
            {
                "connected" => "réseau connecté", "connecting" => "connexion en cours", "retrying" => "nouvel essai prévu",
                "radio-off" => "suspendue (Wi-Fi éteint)", "error" => "service réseau indisponible",
                "waiting" => "en attente du réseau", "disabled" => "désactivée", _ => "en attente du service"
            });
        DisconnectButton.IsEnabled = _network.IsUsbTransport;
        SdkState.Text = snapshot.WifiSdkPasswordless ? "Accès SDK sans mot de passe activé en USB et en Wi-Fi." :
            "Accès SDK sans mot de passe par Wi-Fi désactivé. Une clé enregistrée peut toujours être utilisée.";
        if (scan) VisibleNetworks.Text = snapshot.Available.Count == 0 ? "Aucun réseau signalé par le N9." :
            string.Join(" · ", snapshot.Available.Select(p => p.Name));
        var backups = await _maintenance.ReadSettingsBackupsAsync();
        Backups.ItemsSource = backups.Where(b => b.FeatureId == "n9.networks").ToArray();
        Backups.SelectedIndex = 0;
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true; Actions.IsEnabled = RefreshButton.IsEnabled = CloseButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible; Result.Text = "Opération sur le N9 en cours…";
        try { await action(); if (Result.Text == "Opération sur le N9 en cours…") Result.Text = "État du N9 actualisé."; }
        catch (Exception error) { Result.Text = "Opération non terminée : " + error.Message; }
        finally { _busy = false; Actions.IsEnabled = RefreshButton.IsEnabled = CloseButton.IsEnabled = true; Progress.Visibility = Visibility.Collapsed; }
    }

    private string ProfileId() => (Profiles.SelectedItem as N9WifiProfile)?.Id ?? throw new N9ConnectionException("Choisissez un réseau enregistré.");

    private async Task ApplyAsync(Func<char[]?, Task<N9MaintenanceReport>> action)
    {
        N9MaintenanceReport report;
        try { report = await action(null); }
        catch (N9AdministratorRequiredException)
        {
            var password = _requestPassword();
            if (password is null) { Result.Text = "Opération annulée."; return; }
            try { report = await action(password); }
            finally { Array.Clear(password); }
        }
        await RefreshAsync();
        Result.Text = report.Summary + "\n" + report.Detail;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunAsync(() => RefreshAsync());
    private async void Scan_Click(object sender, RoutedEventArgs e) => await RunAsync(() => RefreshAsync(true));
    private async void Connect_Click(object sender, RoutedEventArgs e) => await RunAsync(() => ApplyAsync(_ => _network.ConnectWifiAsync(ProfileId())));
    private async void Automatic_Click(object sender, RoutedEventArgs e) => await RunAsync(() => ApplyAsync(p => _network.SetWifiAutomaticAsync(ProfileId(), true, false, p)));
    private async void Force_Click(object sender, RoutedEventArgs e) => await RunAsync(() => ApplyAsync(p => _network.SetWifiAutomaticAsync(ProfileId(), true, true, p)));
    private async void Manual_Click(object sender, RoutedEventArgs e) => await RunAsync(() => ApplyAsync(p => _network.SetWifiAutomaticAsync(ProfileId(), false, false, p)));
    private async void Disconnect_Click(object sender, RoutedEventArgs e) => await RunAsync(() => ApplyAsync(p => _network.DisconnectWifiAsync(ProfileId(), p)));
    private async void SdkUsb_Click(object sender, RoutedEventArgs e) => await RunAsync(() => ApplyAsync(p => _network.ConfigureSdkAccessAsync(false, p)));
    private async void SdkWifi_Click(object sender, RoutedEventArgs e) => await RunAsync(() => ApplyAsync(p => _network.ConfigureSdkAccessAsync(true, p)));
    private async void Restore_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (Backups.SelectedItem is not N9SettingsBackup backup) throw new N9ConnectionException("Choisissez une sauvegarde.");
        await ApplyAsync(p => _maintenance.RestoreSettingsAsync(backup.Path, p));
    });
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
