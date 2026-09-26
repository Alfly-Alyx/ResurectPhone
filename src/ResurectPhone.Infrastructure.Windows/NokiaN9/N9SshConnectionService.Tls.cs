using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using Renci.SshNet;
using ResurectPhone.Core.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

public sealed partial class N9SshConnectionService
{
    private Task<N9MaintenanceReport> InspectTlsAsync(CancellationToken cancellationToken) =>
        WithMaintenanceClientAsync(async (client, _) =>
        {
            var marker = await RunMaintenanceScriptAsync(client, "cat /var/lib/resurectphone/tls-backup 2>/dev/null || true", null, cancellationToken);
            var backup = marker.Output.Trim();
            if (!Regex.IsMatch(backup, @"^/var/lib/resurectphone/maintenance/[a-f0-9]{32}$", RegexOptions.CultureInvariant))
                return new N9MaintenanceReport("Correctif TLS non enregistré", "Aucune installation TLS ResurectPhone enregistrée.");
            return new N9MaintenanceReport("Installation TLS enregistrée", "Une installation TLS a été lancée et sa sauvegarde est disponible. Vérifiez la négociation TLS ou restaurez les paquets Nokia.\nSauvegarde : " + backup,
                true, "Vérifier TLS 1.2", backup);
        }, cancellationToken);

    private Task<N9MaintenanceReport> VerifyTlsAsync(string backup, char[]? administratorPassword, CancellationToken cancellationToken) =>
        WithMaintenanceClientAsync(async (client, _) =>
        {
            await WithAdministratorAsync(client, administratorPassword, async password =>
            {
                await SynchronizeClockAsync(client, password, cancellationToken);
                return true;
            }, cancellationToken);
            // Carry raw TLS through USB: the phone performs the handshake and certificate check.
            using var forward = new ForwardedPortRemote("127.0.0.1", (uint)Random.Shared.Next(30000, 55000), "openrepos.net", 443);
            client.AddForwardedPort(forward);
            forward.Start();
            try
            {
                var script = "set -e\n/usr/local/bin/openssl version\n" +
                    "printf '\\nConnexion TLS du N9 vers OpenRepos, transportée par USB :\\n'\necho | /usr/local/bin/openssl s_client -connect 127.0.0.1:" + forward.BoundPort +
                    " -servername openrepos.net -tls1_2 -verify_return_error -verify_hostname openrepos.net -CApath /usr/local/ssl/certs 2>&1";
                var result = await RunMaintenanceScriptAsync(client, script, null, cancellationToken);
                var passed = result.Status == 0 && result.Output.Contains("TLSv1.2", StringComparison.Ordinal) &&
                    result.Output.Contains("Verify return code: 0 (ok)", StringComparison.Ordinal);
                return new N9MaintenanceReport(passed ? "TLS 1.2 et certificat OpenRepos vérifiés sur le N9" : "TLS installé, connexion non validée",
                    TlsPublicSummary(result.Output) + "\nLe navigateur et les applications doivent être relancés pour charger les bibliothèques. Une connexion Wi-Fi/mobile reste nécessaire hors ResurectPhone.",
                    true, "Vérifier TLS 1.2", backup);
            }
            finally { forward.Stop(); client.RemoveForwardedPort(forward); }
        }, cancellationToken);

    internal static string TlsPublicSummary(string output) => string.Join('\n', output.Split('\n')
        .Select(line => line.Trim()).Where(line =>
            line.StartsWith("OpenSSL ", StringComparison.Ordinal) ||
            line.StartsWith("Protocol ", StringComparison.Ordinal) ||
            line.StartsWith("Cipher ", StringComparison.Ordinal) ||
            line.StartsWith("Verify return code:", StringComparison.Ordinal) ||
            line.StartsWith("verify error:", StringComparison.Ordinal) ||
            line.StartsWith("connect:", StringComparison.Ordinal) ||
            line.Contains(":error:", StringComparison.Ordinal) ||
            line.Contains("not found", StringComparison.Ordinal)));

    private async Task<N9MaintenanceReport> InstallTlsAsync(char[]? administratorPassword, CancellationToken cancellationToken)
    {
        // No library is touched until both the patch AND all stock rollback packages are on the PC.
        var local = await N9TlsDownloads.PrepareAsync(cancellationToken);
        return await WithMaintenanceClientAsync((client, pairing) => WithAdministratorAsync(client, administratorPassword, async password =>
        {
            var preflight = await RunMaintenanceScriptAsync(client, TlsPreflightScript(), password, cancellationToken);
            RequireSuccess(preflight, "Le correctif TLS exige un N9 PR1.3 et les versions Nokia prévues. Aucun paquet n’a été remplacé.");
            await SynchronizeClockAsync(client, password, cancellationToken);
            var backup = "/var/lib/resurectphone/maintenance/" + Guid.NewGuid().ToString("N");
            var staging = "/home/developer/.resurectphone/tls-" + Guid.NewGuid().ToString("N");
            var prepare = await RunMaintenanceScriptAsync(client, "set -e\numask 022\nmkdir -p " + staging + "/patched " + staging + "/original", null, cancellationToken);
            RequireSuccess(prepare, "Préparation du transfert TLS impossible.");
            try
            {
                using var transfer = new SftpClient(CreateKeyConnectionInfo(pairing));
                PinHostKey(transfer, pairing);
                await ConnectAsync(transfer, cancellationToken);
                foreach (var entry in local.Hashes)
                {
                    using var source = File.OpenRead(Path.Combine(local.Directory, entry.Key));
                    var hash = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken));
                    if (!hash.Equals(entry.Value, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Un paquet TLS a changé avant le transfert. Installation interrompue.");
                    source.Position = 0;
                    var target = staging + "/" + entry.Key;
                    await Task.Run(() => transfer.UploadFile(source, target), cancellationToken);
                    transfer.ChangePermissions(target, 644);
                }
                var result = await RunMaintenanceScriptAsync(client, BuildTlsInstallScript(backup, staging), password, cancellationToken,
                    TimeSpan.FromMinutes(10));
                if (result.Status != 0)
                    throw new N9MaintenanceFailureException("Installation TLS interrompue. Consultez le rapport et restaurez les paquets si nécessaire.\n" + LimitOutput(result.Output), backup);
                return new N9MaintenanceReport("Correctif TLS installé — vérification Internet nécessaire", LimitOutput(result.Output) +
                    "\nPaquets Nokia de restauration sur le PC : " + Path.Combine(local.Directory, "original") +
                    "\nSauvegarde sur le N9 : " + backup +
                    "\nRelancez les applications pour qu’elles chargent les nouvelles bibliothèques. Le retour arrière restaure les paquets ; il conserve les autorités de certification ajoutées et ne recule pas l’heure.", BackupPath: backup);
            }
            finally
            {
                try { await RunMaintenanceScriptAsync(client, "rm -rf " + staging, null, CancellationToken.None); }
                catch (Exception e) when (IsConnectionFailure(e)) { }
            }
        }, cancellationToken), cancellationToken);
    }

    internal static string TlsPreflightScript()
    {
        var script = new StringBuilder("set -eu\ncommand -v aegis-dpkg >/dev/null\nsysinfoclient -p /device/sw-release-ver | grep -q '^DFL61_HARMATTAN_40.2012.21-3_PR_'\ntest \"$(sysinfoclient -p /component/product)\" = RM-696\n");
        // Replacing an existing community SSL installation needs a different rollback plan.
        script.AppendLine("test ! -e /usr/local/ssl\ntest ! -e /usr/local/bin/openssl\ntest ! -f /var/lib/resurectphone/tls-backup");
        script.AppendLine("test \"$(df -k / | awk 'NR==2 {print $4}')\" -gt 160000\ntest -z \"$(dpkg --audit)\"");
        foreach (var package in N9TlsManifest.Packages)
        {
            var query = "dpkg-query -W -f='${Status}|${Version}' " + Quote(package.Package) + " 2>/dev/null";
            script.AppendLine(package.OriginalSha256 is not null
                ? "test \"$(" + query + ")\" = " + Quote("install ok installed|" + package.Version)
                : "if " + query + " | grep -q '^install ok installed|'; then echo 'Paquet additionnel déjà présent : " + package.Package + "' >&2; exit 1; fi");
        }
        script.AppendLine("echo 'Versions et espace libre vérifiés.'");
        return script.ToString();
    }

    internal static string BuildTlsInstallScript(string backup, string staging)
    {
        const string restore = """
            test -d ./original
            command -v aegis-dpkg >/dev/null
            aegis-dpkg -i ./original/*.deb
            dpkg --remove libaccounts-glib-tools openssl-local wunderw-perl-opt
            # Preserve generated certificate links and any subsequently added files.
            # This directory did not exist before our installation (preflight).
            if test -d /usr/local/ssl; then mv /usr/local/ssl "./ssl-leftovers-$(date +%s)"; fi
            ldconfig
            test -z "$(dpkg --audit)"
            rm -f /var/lib/resurectphone/tls-backup
            echo 'Paquets Nokia restaurés. Relancez les applications ou redémarrez le téléphone.'
            """;
        var script = new StringBuilder(BackupPreamble(backup, restore));
        script.AppendLine("mkdir -p \"$backup/original\"\ncp " + staging + "/original/*.deb \"$backup/original/\"\ndpkg-query -W > \"$backup/packages-before\"");
        script.AppendLine("printf '%s\\n' \"$backup\" > /var/lib/resurectphone/tls-backup\nchmod 644 /var/lib/resurectphone/tls-backup");
        script.AppendLine(SettingsRollback);
        script.AppendLine("changed=1");
        foreach (var name in new[] { "wunderw-perl-opt", "openssl-local" })
            script.AppendLine("aegis-dpkg -i " + Quote(staging + "/patched/" + N9TlsManifest.Packages.Single(package => package.Package == name).Filename));
        script.AppendLine("aegis-dpkg -i " + string.Join(' ', N9TlsManifest.Packages.Where(package => package.Package is not ("wunderw-perl-opt" or "openssl-local"))
            .Select(package => Quote(staging + "/patched/" + package.Filename))));
        foreach (var package in N9TlsManifest.Packages)
            script.AppendLine("test \"$(dpkg-query -W -f='${Status}|${Version}' " + Quote(package.Package) + ")\" = " + Quote("install ok installed|" + package.Version));
        script.AppendLine("test -z \"$(dpkg --audit)\"\n/usr/local/bin/openssl version\nfinished=1\necho 'Les 16 paquets du correctif TLS 1.2 sont installés et configurés.'");
        return script.ToString();
    }
}
