using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ResurectPhone.Core.NokiaN9;
using Renci.SshNet;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

public sealed partial class N9SshConnectionService
{
    public bool IsUsbTransport => _pairingStore.TryLoad()?.Host == DefaultUsbHost;
    private static string NetworkPython
    {
        get
        {
            using var stream = typeof(N9SshConnectionService).Assembly.GetManifestResourceStream(
                "ResurectPhone.Infrastructure.Windows.NokiaN9.Resources.network-manager.py")!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('\uFEFF');
        }
    }
    internal static string NetworkRequest(object request) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(request));
    private static string NetworkCommand(object request) => "python - " + NetworkRequest(request) +
        " <<'RESURECTPHONE_NETWORK_PYTHON'\n" + NetworkPython + "\nRESURECTPHONE_NETWORK_PYTHON\n";

    private static async Task<JsonDocument> RunNetworkAsync(SshClient client, object request, CancellationToken cancellationToken)
    {
        var result = await RunMaintenanceScriptAsync(client, NetworkCommand(request), null, cancellationToken);
        RequireSuccess(result, "L’opération réseau du N9 n’a pas abouti.");
        // Native Harmattan libraries can emit diagnostic lines on stderr.
        var json = result.Output.Split('\n').FirstOrDefault(line => line.TrimStart().StartsWith("{", StringComparison.Ordinal))
            ?? throw new N9ConnectionException("Le N9 n’a pas renvoyé son état réseau.");
        return JsonDocument.Parse(json);
    }

    internal static N9NetworkSnapshot ParseNetworkSnapshot(string json, bool wifiSdkPasswordless)
    {
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement;
        var profiles = data.GetProperty("profiles").EnumerateArray().Select(p => new N9WifiProfile(
            p.GetProperty("id").GetString()!, p.GetProperty("name").GetString()!, p.GetProperty("security").GetString()!,
            p.GetProperty("automatic").ValueKind == JsonValueKind.Null ? null : p.GetProperty("automatic").GetBoolean(),
            p.GetProperty("forced").GetBoolean(), p.GetProperty("connected").GetBoolean())).ToArray();
        var available = data.GetProperty("available").EnumerateArray().Select(p => new N9VisibleWifi(
            p.GetProperty("name").GetString()!, p.GetProperty("signal").GetInt32())).DistinctBy(p => p.Name).ToArray();
        return new(profiles, available, data.GetProperty("address").GetString()!,
            (data.GetProperty("radios").GetInt32() & 5) == 5, data.GetProperty("powerSaving").GetBoolean(),
            data.GetProperty("searchInterval").ValueKind == JsonValueKind.Number ? data.GetProperty("searchInterval").GetInt32() : null,
            data.GetProperty("daemon").TryGetProperty("phase", out var phase) ? phase.GetString()! : "absent", wifiSdkPasswordless);
    }

    public Task<N9NetworkSnapshot> ReadNetworksAsync(bool scan = false, CancellationToken cancellationToken = default) =>
        WithMaintenanceClientAsync(async (client, _) =>
        {
            using var document = await RunNetworkAsync(client, new { action = scan ? "scan" : "status" }, cancellationToken);
            var sdk = await RunMaintenanceScriptAsync(client, "test -f /var/lib/resurectphone/wifi-sdk-enabled", null, cancellationToken);
            return ParseNetworkSnapshot(document.RootElement.GetRawText(), sdk.Status == 0);
        }, cancellationToken);

    public Task<N9MaintenanceReport> ConnectWifiAsync(string profileId, CancellationToken cancellationToken = default) =>
        WithMaintenanceClientAsync(async (client, _) =>
        {
            // Switching a network while SSH itself uses Wi-Fi would cut off its verification.
            if (!IsUsbTransport)
            {
                using var before = await RunNetworkAsync(client, new { action = "status" }, cancellationToken);
                if (!ParseNetworkSnapshot(before.RootElement.GetRawText(), false).Profiles.Any(p => p.Id == profileId && p.Connected))
                    throw new N9ConnectionException("Branchez le câble USB pour changer de réseau Wi-Fi sans interrompre la liaison avec ResurectPhone.");
            }
            using var result = await RunNetworkAsync(client, new { action = "connect", id = profileId }, cancellationToken);
            var state = ParseNetworkSnapshot(result.RootElement.GetRawText(), false);
            if (!state.Profiles.Any(p => p.Id == profileId && p.Connected)) throw new N9ConnectionException("La connexion au réseau choisi n’est pas confirmée.");
            return new N9MaintenanceReport("Wi-Fi connecté et vérifié", "Adresse du N9 : " + state.Address);
        }, cancellationToken);

    public async Task<N9MaintenanceReport> SetWifiAutomaticAsync(string profileId, bool enabled, bool force,
        char[]? administratorPassword = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await WithMaintenanceClientAsync((client, pairing) => WithAdministratorAsync(client, administratorPassword, async password =>
            {
                if (force && !enabled) throw new ArgumentException("La reconnexion forcée exige l’activation de la connexion automatique.");
                using var before = await RunNetworkAsync(client, new { action = "status" }, cancellationToken);
                var profile = ParseNetworkSnapshot(before.RootElement.GetRawText(), false).Profiles.SingleOrDefault(p => p.Id == profileId)
                    ?? throw new N9ConnectionException("Le profil Wi-Fi choisi n’existe plus.");
                if (force) await EnsureWifiCompanionAsync(client, password, cancellationToken);
                var id = Guid.NewGuid().ToString("N");
                var backup = "/var/lib/resurectphone/maintenance/" + id;
                var snapshot = "/home/user/.config/resurectphone/backups/" + id + ".json";
                var restore = "devel-su user -c " + Quote(NetworkCommand(new { action = "restore", backup = snapshot })) +
                    "\ninitctl restart resurectphone-wifi >/dev/null 2>&1 || true\n";
                var prepare = await RunMaintenanceScriptAsync(client, BackupPreamble(backup, restore), password, cancellationToken);
                RequireSuccess(prepare, "La sauvegarde réseau n’a pas pu être préparée.");
                await new N9SettingsHistory(pairing.HostKeySha256).SaveAsync("n9.networks", backup, "Wi-Fi : " + profile.Name, cancellationToken);
                try
                {
                    using var result = await RunNetworkAsync(client, new { action = "automatic", id = profileId, enabled, forced = force, backup = snapshot }, cancellationToken);
                    if (force)
                    {
                        var start = await RunMaintenanceScriptAsync(client,
                            "initctl reload-configuration\ninitctl restart resurectphone-wifi >/dev/null 2>&1 || initctl start resurectphone-wifi >/dev/null 2>&1\ninitctl status resurectphone-wifi | grep -q 'start/running'", password, cancellationToken);
                        RequireSuccess(start, "Le service de reconnexion ne démarre pas.");
                    }
                    using var verified = await RunNetworkAsync(client, new { action = "status" }, cancellationToken);
                    var updated = ParseNetworkSnapshot(verified.RootElement.GetRawText(), false).Profiles.Single(p => p.Id == profileId);
                    if (updated.Automatic != enabled || updated.Forced != force) throw new N9ConnectionException("Le N9 n’a pas conservé les réglages demandés.");
                    return new N9MaintenanceReport(force ? "Reconnexion Wi-Fi forcée activée" : enabled ? "Connexion Wi-Fi automatique activée" : "Connexion automatique désactivée",
                        profile.Name + (force ? "\nLe service du N9 relance la connexion lorsque le Wi-Fi est perdu, même lorsque ResurectPhone est fermé. Le mode avion et l’arrêt du Wi-Fi restent respectés." : "\nRéglage enregistré et relu sur le téléphone.") +
                        "\nLes mots de passe Wi-Fi restent sur le téléphone. La sauvegarde est disponible dans l’historique.", BackupPath: backup);
                }
                catch (Exception error)
                {
                    var rollback = await RunMaintenanceScriptAsync(client, "sh " + backup + "/restore.sh", password, CancellationToken.None);
                    throw new N9MaintenanceFailureException(error.Message + (rollback.Status == 0
                        ? "\nLes réglages précédents ont été restaurés."
                        : "\nLa restauration automatique est incomplète. Utilisez la sauvegarde des réglages."), backup);
                }
            }, cancellationToken), cancellationToken);
        }
        finally { if (administratorPassword is not null) Array.Clear(administratorPassword); }
    }

    public async Task<N9MaintenanceReport> DisconnectWifiAsync(string profileId, char[]? administratorPassword = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!IsUsbTransport) throw new N9ConnectionException("Branchez le câble USB pour déconnecter le Wi-Fi sans perdre le contrôle du N9.");
            var settings = await SetWifiAutomaticAsync(profileId, false, false, administratorPassword?.ToArray(), cancellationToken);
            return await WithMaintenanceClientAsync(async (client, _) =>
            {
                using var result = await RunNetworkAsync(client, new { action = "disconnect", id = profileId }, cancellationToken);
                if (ParseNetworkSnapshot(result.RootElement.GetRawText(), false).Profiles.Any(p => p.Id == profileId && p.Connected))
                    throw new N9MaintenanceFailureException("La déconnexion n’est pas encore confirmée ; actualisez l’état.", settings.BackupPath!);
                return settings with { Summary = "Wi-Fi déconnecté", Detail = "La reconnexion automatique de ce profil est désactivée. Vous pouvez la rétablir ou restaurer la sauvegarde." };
            }, cancellationToken);
        }
        finally { if (administratorPassword is not null) Array.Clear(administratorPassword); }
    }

    private static async Task EnsureWifiCompanionAsync(SshClient client, byte[] password, CancellationToken cancellationToken)
    {
        var python = NetworkPython;
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(python + "\n"))).ToLowerInvariant();
        var check = await RunMaintenanceScriptAsync(client, "test -f /etc/init/resurectphone-wifi.conf && test -f /usr/lib/resurectphone/network-manager.py && sha1sum /usr/lib/resurectphone/network-manager.py", null, cancellationToken);
        if (check.Status == 0 && check.Output.StartsWith(hash + " ", StringComparison.Ordinal)) return;
        var work = "/home/developer/.resurectphone/wifi-" + Guid.NewGuid().ToString("N");
        var script = "set -eu\numask 022\nmkdir -p " + work + "/data/usr/lib/resurectphone " + work + "/data/etc/init " + work + "/control\n" +
            "cat > " + work + "/data/usr/lib/resurectphone/network-manager.py <<'RESURECTPHONE_NETWORK_PYTHON'\n" + python + "\nRESURECTPHONE_NETWORK_PYTHON\n" +
            "cat > " + work + "/data/etc/init/resurectphone-wifi.conf <<'RESURECTPHONE_WIFI_JOB'\n" +
            "description \"ResurectPhone Wi-Fi reconnect\"\nstart on started xsession\nstop on stopping xsession\nrespawn\nrespawn limit 5 60\n" +
            "exec /bin/devel-su user -c \"HOME=/home/user /usr/bin/python /usr/lib/resurectphone/network-manager.py daemon\"\nRESURECTPHONE_WIFI_JOB\n" +
            "cat > " + work + "/control/control <<'RESURECTPHONE_WIFI_CONTROL'\nPackage: resurectphone-n9-network\nVersion: 0.1.0\nArchitecture: all\nMaintainer: ResurectPhone\nPriority: optional\nSection: net\nDescription: Persistent Wi-Fi reconnection for Nokia N9\nRESURECTPHONE_WIFI_CONTROL\n" +
            "chmod 755 " + work + "/data/usr/lib/resurectphone/network-manager.py\n" +
            "cd " + work + "/data\nfor file in usr/lib/resurectphone/network-manager.py etc/init/resurectphone-wifi.conf; do\n" +
            "hash=$(sha1sum \"$file\" | cut -d ' ' -f 1)\nprintf 'S 15 com.nokia.maemo H 40 %s R %s %s\\n' \"$hash\" \"${#file}\" \"$file\"\ndone > " + work + "/control/digsigsums\n" +
            "busybox tar czf " + work + "/data.tar.gz .\ncd " + work + "/control\nbusybox tar czf " + work + "/control.tar.gz .\ncd " + work + "\nprintf '2.0\\n' > debian-binary\n" +
            "(printf '!<arch>\\n'; for file in debian-binary control.tar.gz data.tar.gz; do size=$(wc -c < \"$file\" | tr -d ' '); printf '%-16s%-12s%-6s%-6s%-8s%-10s`\\n' \"$file\" 0 0 0 100644 \"$size\"; cat \"$file\"; if [ $((size % 2)) = 1 ]; then printf '\\n'; fi; done) > companion.deb\n";
        try
        {
            RequireSuccess(await RunMaintenanceScriptAsync(client, script, null, cancellationToken), "La préparation du service Wi-Fi a échoué.");
            await ReleasePackageManagerAsync(client, password, cancellationToken);
            var install = await RunMaintenanceScriptAsync(client, "dpkg -i " + work + "/companion.deb", password, cancellationToken);
            RequireSuccess(install, "L’installation du service Wi-Fi a échoué.");
        }
        finally
        {
            try { await RunMaintenanceScriptAsync(client, "rm -rf " + work, null, CancellationToken.None); }
            catch (Exception error) when (IsConnectionFailure(error)) { }
        }
    }
}
