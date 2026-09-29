using System.Text;
using System.Text.RegularExpressions;
using Renci.SshNet;
using Renci.SshNet.Common;
using ResurectPhone.Core.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

public sealed partial class N9SshConnectionService
{
    public async Task<N9UsbAccessResult> PrepareUsbAccessAsync(
        byte[] setupScript,
        char[]? administratorPassword = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pairing = _pairingStore.TryLoad() ?? throw new N9ConnectionException(
                "Connectez d’abord le N9 pour préparer son accès USB.");
            using var client = new SshClient(CreateKeyConnectionInfo(pairing));
            PinHostKey(client, pairing);
            await ConnectAsync(client, cancellationToken);
            if (!(await ProbeAsync(client, cancellationToken)).IsHarmattan)
                throw new N9ConnectionException("Le téléphone n’a pas confirmé Harmattan.");

            return await N9AdministratorAuthentication.UseAsync(
                async password =>
                {
                    var result = await ExecuteAdministratorAsync(client, "id -u", password, cancellationToken);
                    return result.Status == 0 && result.Output.Split('\n').Any(line => line.Trim() == "0");
                },
                password => ConfigureUsbAsync(client, pairing, setupScript, password, cancellationToken),
                administratorPassword);
        }
        finally
        {
            // Also clear on transport failure before authentication begins.
            if (administratorPassword is not null)
                Array.Clear(administratorPassword);
        }
    }

    private static async Task<N9UsbAccessResult> ConfigureUsbAsync(
        SshClient client, N9Pairing pairing, byte[] setupScript, byte[] password,
        CancellationToken cancellationToken)
    {
        const string readiness = "test -f /var/lib/resurectphone/usb-enabled && " +
            "grep -q \"^# ResurectPhone USB access v4$\" /etc/ssh/sshd_config && " +
            "dpkg -s resurectphone-n9 | grep -q \"^Version: 0[.]1[.]2$\" && " +
            "/sbin/initctl status resurectphone-usb-access | grep -q start/running";
        var ready = await ExecuteAdministratorAsync(client, readiness, password, cancellationToken);
        if (ready.Status == 0 && await CanConnectAnonymouslyAsync(pairing, cancellationToken))
        {
            await SetDefaultUsbModeAsync(client, cancellationToken);
            return new(true, string.Empty);
        }

        var remoteScript = "/home/developer/.resurectphone/usb-setup-" + Guid.NewGuid().ToString("N") + ".sh";
        var script = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(setupScript)
            .Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));
        string? backup = null;
        try
        {
            using (var upload = client.CreateCommand(
                "umask 022; mkdir -p /home/developer/.resurectphone && " +
                "chmod 755 /home/developer/.resurectphone && cat > " + remoteScript +
                " && chmod 644 " + remoteScript))
            {
                upload.CommandTimeout = OperationTimeout;
                var execution = upload.ExecuteAsync(cancellationToken);
                using (var input = upload.CreateInputStream())
                    await input.WriteAsync(script, cancellationToken);
                await execution;
                if (upload.ExitStatus is not 0)
                    throw new N9ConnectionException("La préparation USB n’a pas pu être transférée au N9.");
            }

            var result = await ExecuteAdministratorAsync(client, "sh " + remoteScript, password, cancellationToken);
            var match = Regex.Match(result.Output,
                @"(?m)^RESURECTPHONE_BACKUP=(/var/lib/resurectphone/backups/usb-[0-9]+-[0-9]+)\r?$",
                RegexOptions.CultureInvariant);
            if (match.Success)
                backup = match.Groups[1].Value;
            if (result.Status != 0 || backup is null ||
                !result.Output.Contains("RESURECTPHONE_USB_READY", StringComparison.Ordinal))
                throw new N9ConnectionException("La préparation USB a échoué. Vérifiez le service ResurectPhone sur le N9 ; une restauration est tentée automatiquement.");

            if (!await CanConnectAnonymouslyAsync(pairing, cancellationToken))
                throw new N9ConnectionException("Le N9 refuse encore l’accès sans mot de passe.");
            await SetDefaultUsbModeAsync(client, cancellationToken);
            return new(false, backup);
        }
        catch (Exception exception)
        {
            if (backup is not null)
            {
                try
                {
                    var restored = await ExecuteAdministratorAsync(client, "sh " + backup + "/restore.sh", password, CancellationToken.None);
                    if (restored.Status != 0)
                        throw new IOException("La restauration a été refusée.");
                }
                catch (Exception restoreError)
                {
                    throw new N9ConnectionException("Préparation interrompue. La restauration automatique a échoué ; sauvegarde sur le N9 : " + backup,
                        new AggregateException(exception, restoreError));
                }
            }
            throw;
        }
        finally
        {
            try
            {
                using var cleanup = client.CreateCommand("rm -f " + remoteScript);
                cleanup.CommandTimeout = OperationTimeout;
                await cleanup.ExecuteAsync(CancellationToken.None);
            }
            catch (Exception exception) when (IsConnectionFailure(exception)) { }
        }
    }

    private static async Task SetDefaultUsbModeAsync(SshClient client, CancellationToken cancellationToken)
    {
        using var command = client.CreateCommand(
            "gconftool-2 --type string --set /Meego/System/UsbMode windows_network && " +
            "test \"$(gconftool-2 --get /Meego/System/UsbMode)\" = windows_network");
        command.CommandTimeout = OperationTimeout;
        await command.ExecuteAsync(cancellationToken);
        if (command.ExitStatus is not 0)
            throw new N9ConnectionException("Le N9 n’a pas enregistré le mode USB SDK par défaut.");
    }

    private static async Task<bool> CanConnectAnonymouslyAsync(N9Pairing pairing, CancellationToken cancellationToken)
    {
        using var anonymous = new SshClient(CreateConnectionInfo(new NoneAuthenticationMethod(DefaultUserName)));
        PinHostKey(anonymous, pairing);
        try
        {
            await ConnectAsync(anonymous, cancellationToken);
            return (await ProbeAsync(anonymous, cancellationToken)).IsHarmattan;
        }
        catch (N9ConnectionException exception) when (exception.InnerException is SshAuthenticationException)
        {
            return false;
        }
    }

    private static async Task<(int? Status, string Output)> ExecuteAdministratorAsync(
        SshClient client, string commandText, byte[] password, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        using var command = client.CreateCommand(N9PackageCommands.WrapWithDevelSu(commandText));
        command.CommandTimeout = timeout ?? TimeSpan.FromMinutes(2);
        var execution = command.ExecuteAsync(cancellationToken);
        using (var input = command.CreateInputStream())
            await N9PackageCommands.SendPasswordToStandardInputAsync(input, password.ToArray(), cancellationToken);
        await execution;
        // Never surface raw administrator output, which may contain account data.
        return (command.ExitStatus, command.Result + command.Error);
    }
}
