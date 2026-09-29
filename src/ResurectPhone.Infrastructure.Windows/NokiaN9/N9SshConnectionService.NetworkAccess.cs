using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using ResurectPhone.Core.NokiaN9;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

public sealed partial class N9SshConnectionService
{
    public string? SavedAddress => _pairingStore.TryLoad()?.Host;

    internal static bool IsLocalAddress(string address)
    {
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = ip.GetAddressBytes();
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168);
    }

    public async Task<N9ConnectionStatus> ConnectAtAddressAsync(string address, string? temporaryPassword = null,
        Func<N9HostKeyIdentity, bool>? approveHostKey = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(temporaryPassword)) temporaryPassword = null;
        if (!IsLocalAddress(address)) throw new N9ConnectionException("Saisissez l’adresse IPv4 locale affichée par SDK Connectivity.");
        await _maintenanceLock.WaitAsync(cancellationToken);
        try
        {
            var saved = _pairingStore.TryLoad();
            var pairing = saved is null ? null : saved with { Host = address };
            if (pairing is not null && temporaryPassword is null)
            {
                using var known = new SshClient(CreateKeyConnectionInfo(pairing));
                PinHostKey(known, pairing);
                try
                {
                    await ConnectAsync(known, cancellationToken);
                    var status = await ProbeAsync(known, cancellationToken);
                    if (!status.IsHarmattan) throw new N9ConnectionException("Le téléphone n’a pas confirmé Harmattan.");
                    _pairingStore.Save(pairing);
                    return status with { IsPaired = true };
                }
                catch (N9ConnectionException error) when (error.InnerException is SshAuthenticationException) { }
            }
            var auth = temporaryPassword is null ? (AuthenticationMethod)new NoneAuthenticationMethod(DefaultUserName)
                : new PasswordAuthenticationMethod(DefaultUserName, temporaryPassword);
            using var client = new SshClient(new ConnectionInfo(address, DefaultPort, DefaultUserName, auth)
            { Timeout = ConnectionTimeout, RetryAttempts = 1, Encoding = Encoding.UTF8 });
            N9HostKeyIdentity? accepted = null;
            client.HostKeyReceived += (_, args) =>
            {
                var identity = ToIdentity(args);
                args.CanTrust = pairing is not null
                    ? identity.Algorithm == pairing.HostKeyName && identity.Sha256Fingerprint == pairing.HostKeySha256
                    : approveHostKey?.Invoke(identity) == true;
                if (args.CanTrust) accepted = identity;
            };
            try { await ConnectAsync(client, cancellationToken); }
            catch (N9ConnectionException error) when (error.InnerException is SshAuthenticationException)
            { throw new N9AuthenticationRequiredException("Saisissez une fois le mot de passe actuel de SDK Connectivity pour enregistrer ce PC.", error); }
            var probe = await ProbeAsync(client, cancellationToken);
            if (!probe.IsHarmattan || accepted is null) throw new N9ConnectionException("L’identité du N9 n’a pas été confirmée.");
            return await CompletePairingAsync(client, accepted, probe, cancellationToken);
        }
        finally { _maintenanceLock.Release(); }
    }

    public async Task<N9MaintenanceReport> ConfigureSdkAccessAsync(bool includeWifi, char[]? administratorPassword = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await WithMaintenanceClientAsync((client, pairing) => WithAdministratorAsync(client, administratorPassword, async password =>
            {
                using var resource = typeof(N9SshConnectionService).Assembly.GetManifestResourceStream("ResurectPhone.N9SdkSetup")!;
                using var reader = new StreamReader(resource);
                var script = "set -- " + (includeWifi ? "usb-wifi" : "usb") + "\n" + await reader.ReadToEndAsync(cancellationToken);
                await ReleasePackageManagerAsync(client, password, cancellationToken);
                var result = await RunMaintenanceScriptAsync(client, script, password, cancellationToken);
                RequireSuccess(result, "Le réglage de l’accès SDK a échoué.");
                var match = Regex.Match(result.Output, @"(?m)^RESURECTPHONE_BACKUP=(/var/lib/resurectphone/backups/usb-[0-9]+-[0-9]+)\r?$");
                if (!match.Success) throw new N9ConnectionException("La sauvegarde de l’accès SDK n’a pas été confirmée.");
                var original = match.Groups[1].Value;
                var backup = "/var/lib/resurectphone/maintenance/" + Guid.NewGuid().ToString("N");
                try
                {
                    RequireSuccess(await RunMaintenanceScriptAsync(client, BackupPreamble(backup, "sh " + original + "/restore.sh"), password, cancellationToken),
                        "L’enregistrement du retour arrière a échoué.");
                    await new N9SettingsHistory(pairing.HostKeySha256).SaveAsync("n9.networks", backup, "Accès SDK USB / Wi-Fi", cancellationToken);
                    using var anonymous = new SshClient(new ConnectionInfo(pairing.Host, pairing.Port, pairing.UserName,
                        new NoneAuthenticationMethod(pairing.UserName)) { Timeout = ConnectionTimeout });
                    PinHostKey(anonymous, pairing);
                    var shouldAllow = includeWifi || IsUsbTransport;
                    try
                    {
                        await ConnectAsync(anonymous, cancellationToken);
                        if (!shouldAllow) throw new N9ConnectionException("L’accès SDK sans mot de passe reste ouvert par Wi-Fi.");
                        if (!(await ProbeAsync(anonymous, cancellationToken)).IsHarmattan)
                            throw new N9ConnectionException("La connexion SDK sans mot de passe n’a pas été confirmée.");
                    }
                    catch (N9ConnectionException error) when (!shouldAllow && error.InnerException is SshAuthenticationException) { }
                }
                catch
                {
                    RequireSuccess(await RunMaintenanceScriptAsync(client, "sh " + original + "/restore.sh", password, CancellationToken.None),
                        "La restauration automatique de l’accès SDK a échoué. Sauvegarde : " + original);
                    throw;
                }
                return new N9MaintenanceReport(includeWifi ? "SDK sans mot de passe en USB et en Wi-Fi" : "SDK sans mot de passe en USB",
                    includeWifi ? "L’accès SDK est ouvert aux PC branchés en USB et aux appareils pouvant joindre le N9 par le réseau. Le réglage reste actif lorsque SDK Connectivity renouvelle son mot de passe."
                        : "Le câble USB suffit pour appairer un nouveau PC. En Wi-Fi, une clé enregistrée est nécessaire. L’accès administrateur conserve son mot de passe.", BackupPath: backup);
            }, cancellationToken), cancellationToken);
        }
        finally { if (administratorPassword is not null) Array.Clear(administratorPassword); }
    }
}
