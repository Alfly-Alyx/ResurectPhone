using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Renci.SshNet;
using ResurectPhone.Core.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

public sealed partial class N9SshConnectionService
{
    private readonly SemaphoreSlim _maintenanceLock = new(1, 1);

    private async Task<T> WithMaintenanceClientAsync<T>(Func<SshClient, N9Pairing, Task<T>> action,
        CancellationToken cancellationToken)
    {
        await _maintenanceLock.WaitAsync(cancellationToken);
        try
        {
            var pairing = _pairingStore.TryLoad() ?? throw new N9ConnectionException("Connectez d’abord le Nokia N9.");
            using var client = new SshClient(CreateKeyConnectionInfo(pairing));
            PinHostKey(client, pairing);
            await ConnectAsync(client, cancellationToken);
            if (!(await ProbeAsync(client, cancellationToken)).IsHarmattan)
                throw new N9ConnectionException("Le téléphone connecté n’a pas confirmé Harmattan.");
            return await action(client, pairing);
        }
        finally { _maintenanceLock.Release(); }
    }

    private static async Task<T> WithAdministratorAsync<T>(SshClient client, char[]? password,
        Func<byte[], Task<T>> action, CancellationToken cancellationToken) =>
        await N9AdministratorAuthentication.UseAsync(async candidate =>
        {
            var check = await ExecuteAdministratorAsync(client, "id -u", candidate, cancellationToken);
            return check.Status == 0 && check.Output.Split('\n').Any(line => line.Trim() == "0");
        }, action, password);

    private static string Quote(string value)
    {
        if (value.IndexOf('\0') >= 0) throw new ArgumentException("Valeur de commande invalide.");
        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }

    private static async Task<(int? Status, string Output)> RunMaintenanceScriptAsync(
        SshClient client, string script, byte[]? administratorPassword, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var path = "/home/developer/.resurectphone/task-" + Guid.NewGuid().ToString("N") + ".sh";
        var bytes = Encoding.UTF8.GetBytes(("#!/bin/sh\nPATH=/bin:/sbin:/usr/bin:/usr/sbin\nLC_ALL=C\nexport PATH LC_ALL\n" + script)
            .Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));
        try
        {
            using (var upload = client.CreateCommand("umask 022; mkdir -p /home/developer/.resurectphone && chmod 755 /home/developer/.resurectphone && cat > " + path))
            {
                upload.CommandTimeout = OperationTimeout;
                var execution = upload.ExecuteAsync(cancellationToken);
                using (var input = upload.CreateInputStream()) await input.WriteAsync(bytes, cancellationToken);
                await execution;
                if (upload.ExitStatus != 0) throw new N9ConnectionException("Le transfert de l’opération a échoué.");
            }
            if (administratorPassword is not null)
                return await ExecuteAdministratorAsync(client, "sh " + path, administratorPassword, cancellationToken, timeout);
            using var command = client.CreateCommand("sh " + path);
            command.CommandTimeout = timeout ?? TimeSpan.FromMinutes(2);
            await command.ExecuteAsync(cancellationToken);
            return (command.ExitStatus, command.Result + command.Error);
        }
        finally
        {
            using var cleanup = client.CreateCommand("rm -f " + path);
            cleanup.CommandTimeout = OperationTimeout;
            try { await cleanup.ExecuteAsync(CancellationToken.None); }
            catch (Exception exception) when (IsConnectionFailure(exception)) { }
        }
    }

    private static void RequireSuccess((int? Status, string Output) result, string message)
    {
        if (result.Status != 0)
            throw new N9ConnectionException(message + "\n" + LimitOutput(result.Output));
    }

    private static string LimitOutput(string output) => output.Length <= 24000 ? output.Trim() : output[..8000].Trim() + "\n[…]\n" + output[^16000..].Trim();

    public Task<IReadOnlyList<N9Application>> ReadApplicationsAsync(CancellationToken cancellationToken = default) =>
        WithMaintenanceClientAsync(async (client, _) =>
        {
            var result = await RunMaintenanceScriptAsync(client, InventoryScript, null, cancellationToken);
            RequireSuccess(result, "L’inventaire des applications a échoué.");
            return ParseApplications(result.Output);
        }, cancellationToken);

    internal static IReadOnlyList<N9Application> ParseApplications(string output)
    {
        var packages = new Dictionary<string, N9DebPackageMetadata>(StringComparer.Ordinal);
        var desktops = new List<(string Package, string Name, string Path)>();
        foreach (var line in output.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            var fields = line.Split('\t');
            if (fields.Length == 8 && fields[0] == "PKG" && fields[7] == "install ok installed" && N9PackageMaintenancePolicy.IsSafePackageName(fields[1]))
                packages[fields[1]] = new(fields[1], fields[2], fields[3], fields[4], fields[5], fields[6], true);
            else if (fields.Length == 4 && fields[0] == "APP" && fields[3].StartsWith("/usr/share/applications/", StringComparison.Ordinal))
                desktops.Add((fields[1], fields[2], fields[3]));
        }
        return desktops.Where(app => packages.ContainsKey(app.Package)).DistinctBy(app => app.Package)
            .Select(app => new N9Application(packages[app.Package], app.Name, app.Path))
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static async Task<N9Application> RequireApplicationAsync(SshClient client, string packageId, CancellationToken cancellationToken)
    {
        if (!N9PackageMaintenancePolicy.IsSafePackageName(packageId)) throw new InvalidDataException("Paquet invalide.");
        var inventory = await RunMaintenanceScriptAsync(client, InventoryScript, null, cancellationToken);
        RequireSuccess(inventory, "Inventaire impossible.");
        return ParseApplications(inventory.Output).SingleOrDefault(app => app.Metadata.Package == packageId)
            ?? throw new N9ConnectionException("Cette application n’est plus présente dans l’inventaire du N9.");
    }

    private const string InventoryScript = """
        set -e
        dpkg-query -W -f='PKG\t${Package}\t${Version}\t${Architecture}\t${Essential}\t${Priority}\t${Aegis-Origin}\t${Status}\n'
        for desktop in /usr/share/applications/*.desktop; do
            test -f "$desktop" || continue
            grep -q '^Type=Application' "$desktop" || continue
            if grep -qE '^(Hidden|NoDisplay)=true' "$desktop"; then continue; fi
            package=$(dpkg-query -S "$desktop" 2>/dev/null | head -n 1 | cut -d : -f 1)
            test -n "$package" || continue
            name=$(sed -n 's/^Name=//p' "$desktop" | head -n 1 | tr '\t\r\n' '   ')
            printf 'APP\t%s\t%s\t%s\n' "$package" "$name" "$desktop"
        done
        """;

    public Task<N9LocalPackage> InspectLocalPackageAsync(string path, CancellationToken cancellationToken = default) =>
        N9LocalPackageReader.ReadAsync(path, cancellationToken);

    public async Task<N9MaintenanceReport> InstallLocalPackageAsync(N9LocalPackage package, char[]? administratorPassword = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var verified = await InspectLocalPackageAsync(package.Path, cancellationToken);
            if (verified.Sha256 != package.Sha256 || verified.Metadata != package.Metadata)
                throw new InvalidDataException("Le paquet a changé depuis sa vérification. Sélectionnez-le de nouveau.");
            if (verified.IsRebuiltBackup)
                throw new InvalidDataException("Cette sauvegarde reconstruite ne préserve pas la provenance Aegis. Sa restauration automatique est bloquée.");
            if (N9PackageMaintenancePolicy.IsProtectedPackage(verified.Metadata))
                throw new InvalidDataException("Ce paquet est protégé et ne peut pas être remplacé par cet outil.");
            return await WithMaintenanceClientAsync((client, pairing) => WithAdministratorAsync(client, administratorPassword,
                async password =>
                {
                    var installed = await RunMaintenanceScriptAsync(client,
                        "dpkg-query -W -f='${Package}\\t${Version}\\t${Architecture}\\t${Essential}\\t${Priority}\\n' " + Quote(verified.Metadata.Package), null, cancellationToken);
                    var fields = installed.Output.Trim().Split('\t');
                    if (installed.Status == 0 && fields.Length == 5 && N9PackageMaintenancePolicy.IsProtectedPackage(
                        new(fields[0], fields[1], fields[2], fields[3], fields[4])))
                        throw new InvalidDataException("La version installée de ce paquet est un composant système protégé.");
                    var path = "/home/developer/.resurectphone/package-" + Guid.NewGuid().ToString("N") + ".deb";
                    using var transfer = new SftpClient(CreateKeyConnectionInfo(pairing));
                    PinHostKey(transfer, pairing);
                    await ConnectAsync(transfer, cancellationToken);
                    try
                    {
                        using (var input = File.OpenRead(verified.Path))
                        {
                            if (Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken)) != verified.Sha256)
                                throw new InvalidDataException("Le paquet a changé avant le transfert.");
                            input.Position = 0;
                            await Task.Run(() => transfer.UploadFile(input, path), cancellationToken);
                        }
                        // SSH.NET expects octal digits (644), not the decimal value of 0644.
                        transfer.ChangePermissions(path, 644);
                        await ReleasePackageManagerAsync(client, password, cancellationToken);
                        var script = "set -e\ndpkg -i " + Quote(path) + "\ndpkg-query -W -f='${Status} ${Version}\\n' " + Quote(verified.Metadata.Package);
                        var result = await RunMaintenanceScriptAsync(client, script, password, cancellationToken);
                        RequireSuccess(result, "L’installation a été refusée par Harmattan (dépendances ou provenance Aegis).");
                        if (!result.Output.Contains("install ok installed " + verified.Metadata.Version, StringComparison.Ordinal))
                            throw new N9ConnectionException("Le paquet n’a pas confirmé son état installé.");
                        return new N9MaintenanceReport("Application installée", verified.Metadata.Package + " " + verified.Metadata.Version + "\n" + LimitOutput(result.Output));
                    }
                    finally { if (transfer.IsConnected && transfer.Exists(path)) transfer.DeleteFile(path); }
                }, cancellationToken), cancellationToken);
        }
        finally { if (administratorPassword is not null) Array.Clear(administratorPassword); }
    }

    public async Task<N9MaintenanceReport> ExportApplicationAsync(string packageId, string destination,
        char[]? administratorPassword = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await WithMaintenanceClientAsync(async (client, pairing) =>
            {
                var app = await RequireApplicationAsync(client, packageId, cancellationToken);
                return await WithAdministratorAsync(client, administratorPassword, async password =>
                {
                    var work = "/var/tmp/resurectphone-" + Guid.NewGuid().ToString("N");
                    var localWork = Path.Combine(Path.GetTempPath(), "resurectphone-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(localWork);
                    try
                    {
                        var export = "set -- " + Quote(packageId) + " " + work + " " + work + "/control.tar.gz " + work + "/data.tar.gz\n" + N9PackageCommands.ExportScript;
                        export += "\nchmod 755 " + work + "\n";
                        var result = await RunMaintenanceScriptAsync(client, export, password, cancellationToken);
                        RequireSuccess(result, "La sauvegarde de l’application a échoué.");
                        using var transfer = new SftpClient(CreateKeyConnectionInfo(pairing));
                        PinHostKey(transfer, pairing);
                        await ConnectAsync(transfer, cancellationToken);
                        foreach (var name in new[] { "control.tar.gz", "data.tar.gz" })
                        {
                            using var output = File.Create(Path.Combine(localWork, name));
                            await Task.Run(() => transfer.DownloadFile(work + "/" + name, output), cancellationToken);
                        }
                        await N9DebianArchiveAssembler.AssembleAsync(Path.Combine(localWork, "control.tar.gz"),
                            Path.Combine(localWork, "data.tar.gz"), destination, cancellationToken);
                        var archive = await InspectLocalPackageAsync(destination, cancellationToken);
                        if (archive.Metadata.Package != app.Metadata.Package || !archive.IsRebuiltBackup)
                            throw new InvalidDataException("La sauvegarde assemblée n’a pas passé la vérification.");
                        return new N9MaintenanceReport("Sauvegarde créée", Path.GetFullPath(destination) + "\nSHA-256 : " + archive.Sha256 +
                            "\nArchive Debian vérifiée. La restauration Aegis n’est pas garantie et reste bloquée.");
                    }
                    finally
                    {
                        try { await RunMaintenanceScriptAsync(client, "rm -rf " + work, password, CancellationToken.None); }
                        catch (Exception exception) when (IsConnectionFailure(exception)) { }
                        finally
                        {
                            foreach (var name in new[] { "control.tar.gz", "data.tar.gz" }) File.Delete(Path.Combine(localWork, name));
                            Directory.Delete(localWork);
                        }
                    }
                }, cancellationToken);
            }, cancellationToken);
        }
        finally { if (administratorPassword is not null) Array.Clear(administratorPassword); }
    }

    public Task<N9MaintenanceReport> PreviewRemovalAsync(string packageId, CancellationToken cancellationToken = default) =>
        WithMaintenanceClientAsync(async (client, _) =>
        {
            var app = await RequireApplicationAsync(client, packageId, cancellationToken);
            if (app.RemovalBlock is not null) return new N9MaintenanceReport("Suppression interdite", app.RemovalBlock);
            // dpkg checks this removal's reverse dependencies without solving unrelated
            // broken packages left by earlier Harmattan installations.
            var direct = await RunMaintenanceScriptAsync(client, "dpkg --no-act --remove " + Quote(packageId), null, cancellationToken);
            if (direct.Status == 0)
                return new N9MaintenanceReport("Suppression normale prête", LimitOutput(direct.Output), true, "Supprimer cette application");
            var result = await RunMaintenanceScriptAsync(client, "apt-get -s remove " + Quote(packageId), null, cancellationToken);
            return new N9MaintenanceReport("Dépendances à examiner",
                LimitOutput(direct.Output + "\n" + result.Output), false, "Supprimer cette application", CanForceRemoval: IsDependencyRemovalBlock(result.Status, result.Output, packageId));
        }, cancellationToken);

    internal static IReadOnlyList<string> ParseAptRemovals(string output) => output.Split('\n')
        .Where(line => line.StartsWith("Remv ", StringComparison.Ordinal))
        .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]).Distinct().ToArray();

    internal static bool IsDependencyRemovalBlock(int? status, string output, string packageId)
    {
        if (status == 0)
        {
            var removals = ParseAptRemovals(output);
            return removals.Contains(packageId) && removals.Any(name => name != packageId);
        }
        var errors = output.Split('\n').Where(line => line.StartsWith("E:", StringComparison.Ordinal)).ToArray();
        return status == 100 && errors.Length > 0 &&
            errors.All(line => line.StartsWith("E: Unmet dependencies", StringComparison.Ordinal) || line.StartsWith("E: Broken packages", StringComparison.Ordinal)) &&
            Regex.IsMatch(output, @"Depends:[^\n]*?(?<![a-z0-9+.-])" + Regex.Escape(packageId) + @"(?![a-z0-9+.-])",
                RegexOptions.CultureInvariant);
    }

    public async Task<N9MaintenanceReport> RemoveApplicationAsync(string packageId, bool forceDependencies,
        char[]? administratorPassword = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await WithMaintenanceClientAsync(async (client, _) =>
            {
                var app = await RequireApplicationAsync(client, packageId, cancellationToken);
                if (app.RemovalBlock is not null) throw new InvalidDataException(app.RemovalBlock);
                var simulation = await RunMaintenanceScriptAsync(client,
                    (forceDependencies ? "apt-get -s remove " : "dpkg --no-act --remove ") + Quote(packageId), null, cancellationToken);
                if (forceDependencies && !IsDependencyRemovalBlock(simulation.Status, simulation.Output, packageId))
                    throw new N9ConnectionException("Le mode expert est réservé à un blocage de dépendances confirmé par la simulation APT.");
                if (!forceDependencies && simulation.Status != 0)
                    throw new N9ConnectionException("La suppression normale rencontre un blocage.\n" + LimitOutput(simulation.Output));
                return await WithAdministratorAsync(client, administratorPassword, async password =>
                {
                    await ReleasePackageManagerAsync(client, password, cancellationToken);
                    var command = forceDependencies
                        ? N9PackageCommands.BuildExpertRemovalCommand(app.Metadata, true)
                        : N9PackageCommands.BuildStandardRemovalCommand(app.Metadata);
                    var result = await RunMaintenanceScriptAsync(client, command + "\nresult=$?\ndpkg-query -W -f='${Status}\\n' " + Quote(packageId) +
                        " 2>/dev/null || true\nexit $result", password, cancellationToken);
                    RequireSuccess(result, "La suppression a été refusée.");
                    var check = await RunMaintenanceScriptAsync(client, "dpkg-query -W -f='${Status}\\n' " + Quote(packageId), null, cancellationToken);
                    if (check.Output.Contains("install ok installed", StringComparison.Ordinal)) throw new N9ConnectionException("Le paquet est encore installé.");
                    return new N9MaintenanceReport("Application supprimée", packageId + "\n" + LimitOutput(result.Output) +
                        (forceDependencies ? "\nMode expert : l’état des dépendances doit être vérifié." : ""));
                }, cancellationToken);
            }, cancellationToken);
        }
        finally { if (administratorPassword is not null) Array.Clear(administratorPassword); }
    }
}
